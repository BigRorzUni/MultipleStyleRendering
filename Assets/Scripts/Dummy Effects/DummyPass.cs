using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[System.Serializable]
public class DummyPass : ScriptableRenderPass
{
    private Material _mat;
    private readonly uint _requiredBit;
    private readonly string _name;

    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");

    ComputeBuffer _instanceBuffer;
    int _instanceBufferCapacity = 0;

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");

    // make sure the compute buffer is big enough for the given instance count
    void EnsureInstanceBufferCapacity(int count)
    {
        if (_instanceBuffer != null && _instanceBufferCapacity >= count)
            return;

        if (_instanceBuffer != null)
            _instanceBuffer.Release();

        _instanceBufferCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));
        _instanceBuffer = new ComputeBuffer(_instanceBufferCapacity, Marshal.SizeOf<QuadInstanceData>());
    }

    private class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;
        public int instanceCount;
    }

    public DummyPass(Shader shader, string name, int requiredIndex)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _requiredBit = 1u << requiredIndex;
        _name = name;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
            if (frameContext.Contains<NprFrameData>())
                nprFrameData = frameContext.Get<NprFrameData>();
            else
                nprFrameData = frameContext.Create<NprFrameData>();

        if (!nprFrameData.idTexture.IsValid())       
            return;
        if (!nprFrameData.sourceTexture.IsValid())   
            return;
        if ((nprFrameData.presentTestStyles & _requiredBit) == 0)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;    

        using (var builder = renderGraph.AddRasterRenderPass($"{_name} Source Copy", out PassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        // FULLSCREEN MODE: ignore all bbox usage
        if (!NprTestingConfig.UseBoundingBoxes)
        {
            using (var builder = renderGraph.AddRasterRenderPass($"Fullscreen {_name} Pass", out PassData passData))
            {
                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);

                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                });
            }

            return;
        }

        // BBOX MODE
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0) 
            return;

        if (!NprTestingConfig.BatchedDraws)
        {
            foreach (var bbox in nprFrameData.bboxes)
            {
                if (bbox.box.width <= 0 || bbox.box.height <= 0)
                    continue;

                if ((bbox.testMask & _requiredBit) == 0)
                    continue;
                
                // Debug.Log($"[DummyPass] Rendering bbox {bbox.box} | " + $"bboxMask: {Convert.ToString((int)bbox.testMask, 2).PadLeft(32,'0')} | " + $"requiredBit: {Convert.ToString((int)_requiredBit, 2).PadLeft(32,'0')}");

                using (var builder = renderGraph.AddRasterRenderPass($"BBox {_name} ({bbox.box})", out PassData passData))
                {
                    passData.src = nprFrameData.sourceTexture;
                    passData.ids = nprFrameData.idTexture;
                    passData.mat = _mat;
                    passData.rect = bbox.box;

                    builder.UseTexture(passData.src, AccessFlags.Read);
                    builder.UseTexture(passData.ids, AccessFlags.Read);

                    builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        data.mat.SetTexture(SourceTexID, data.src);
                        data.mat.SetTexture(IdTexId, data.ids);

                        ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                        ctx.cmd.DisableScissorRect();
                    });
                }
            }

            return;
        }

        List<BoundingBox> batchedBBoxes = new List<BoundingBox>();
        foreach (var bbox in nprFrameData.bboxes)
        {
            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            if ((bbox.testMask & _requiredBit) == 0)
                continue;

            batchedBBoxes.Add(bbox);
        }

        List<QuadInstanceData> instances = new List<QuadInstanceData>();
        foreach (var bbox in batchedBBoxes)
        {
            instances.Add(new QuadInstanceData
            {
                rect = new Vector4(bbox.box.x, bbox.box.y, bbox.box.width, bbox.box.height)
            });
        }

        if (instances.Count == 0)
            return;

        EnsureInstanceBufferCapacity(instances.Count);
        _instanceBuffer.SetData(instances);

        using (var builder = renderGraph.AddRasterRenderPass($"Batched {_name} Pass", out PassData passData))
        {
            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.instanceBuffer = _instanceBuffer;
            passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
            passData.instanceCount = instances.Count;

            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.UseTexture(passData.ids, AccessFlags.Read);

            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexID, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
                data.mat.SetVector(ScreenParamsID, data.screenSize);

                ctx.cmd.DrawProcedural(
                    Matrix4x4.identity,
                    data.mat, // dummy material
                    0,
                    MeshTopology.Triangles,
                    6, // 2 triangles per quad
                    data.instanceCount // 1 instance per bbox
                );
            });
        }
        
    }
}