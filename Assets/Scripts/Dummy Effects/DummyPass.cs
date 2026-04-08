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

    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int RequiredBitID = Shader.PropertyToID("_RequiredBit");

    ComputeBuffer _instanceBuffer;
    int _instanceBufferCapacity = 0;

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int BBoxIndicesID = Shader.PropertyToID("_BboxIndices");
    static readonly int UseOcclusionID = Shader.PropertyToID("_UseOcclusion");
    static readonly int CurrentBBoxIndexID = Shader.PropertyToID("_CurrentBboxIndex");

    ComputeBuffer _bboxIndexBuffer;
    int _bboxIndexBufferCapacity = 0;

    readonly List<Material> _tempMaterials = new();

    void EnsureIndexBufferCapacity(int count)
    {
        if (_bboxIndexBuffer != null && _bboxIndexBufferCapacity >= count)
            return;

        if (_bboxIndexBuffer != null)
            _bboxIndexBuffer.Release();

        _bboxIndexBufferCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));
        _bboxIndexBuffer = new ComputeBuffer(_bboxIndexBufferCapacity, sizeof(uint));
    }

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
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;
        public uint requiredBit;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;
        public int instanceCount;

        public ComputeBuffer visibilityBuffer;
        public ComputeBuffer bboxIndexBuffer;
        public int useOcclusion;
        public int currentBBoxIndex;
    }

    private class CopyPassData
    {
        public TextureHandle src;
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

        using (var builder = renderGraph.AddRasterRenderPass($"{_name} Source Copy", out CopyPassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        // FULLSCREEN MODE: ignore all bbox usage
        if (!NprTestingConfig.UseBoundingBoxes)
        {
            using (var builder = renderGraph.AddRasterRenderPass($"Fullscreen {_name} Pass", out PassData passData))
            {
                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;
                passData.requiredBit = _requiredBit;

                builder.UseTexture(passData.ids, AccessFlags.Read);

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetInt(RequiredBitID, (int)data.requiredBit);

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

                int index = nprFrameData.bboxes.IndexOf(bbox);
                using (var builder = renderGraph.AddRasterRenderPass($"BBox {_name} ({index})", out PassData passData))
                {
                    builder.AllowGlobalStateModification(true);

                    passData.ids = nprFrameData.idTexture;

                    if(NprTestingConfig.UseOcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                    {
                        Material perPassMat = new Material(_mat);
                        _tempMaterials.Add(perPassMat);
                        passData.mat = perPassMat;
                    }
                    else
                    {
                        passData.mat = _mat;
                    }

                    passData.rect = bbox.box;
                    passData.requiredBit = _requiredBit;

                    passData.visibilityBuffer = null;
                    passData.currentBBoxIndex = index;
                    passData.useOcclusion = 0;

                    if (NprTestingConfig.UseOcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                    {
                        passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                        passData.useOcclusion = 1;
                    }

                    builder.UseTexture(passData.ids, AccessFlags.Read);

                    builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        data.mat.SetTexture(IdTexId, data.ids);
                        data.mat.SetInt(RequiredBitID, (int)data.requiredBit);

                        ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                        ctx.cmd.DisableScissorRect();
                    });
                }
            }

            return;
        }

        List<BoundingBox> batchedBBoxes = new List<BoundingBox>();
        List<QuadInstanceData> instances = new List<QuadInstanceData>();
        List<uint> bboxIndices = new List<uint>();

        for (int i = 0; i < nprFrameData.bboxes.Count; i++)
        {
            var bbox = nprFrameData.bboxes[i];

            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            if ((bbox.testMask & _requiredBit) == 0)
                continue;

            batchedBBoxes.Add(bbox);

            instances.Add(new QuadInstanceData
            {
                rect = new Vector4(bbox.box.x, bbox.box.y, bbox.box.width, bbox.box.height)
            });

            bboxIndices.Add((uint)i);
        }

        if (instances.Count == 0)
            return;

        EnsureInstanceBufferCapacity(instances.Count);
        _instanceBuffer.SetData(instances);

        EnsureIndexBufferCapacity(bboxIndices.Count);
        _bboxIndexBuffer.SetData(bboxIndices);

        using (var builder = renderGraph.AddRasterRenderPass($"Batched {_name} Pass", out PassData passData))
        {
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.instanceBuffer = _instanceBuffer;
            passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
            passData.instanceCount = instances.Count;
            passData.requiredBit = _requiredBit;

            passData.visibilityBuffer = null;
            passData.bboxIndexBuffer = null;
            passData.useOcclusion = 0;
            passData.currentBBoxIndex = 0;

            if (NprTestingConfig.UseOcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
            {
                passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.bboxIndexBuffer = _bboxIndexBuffer;
                passData.useOcclusion = 1;
            }

            builder.UseTexture(passData.ids, AccessFlags.Read);

            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
                data.mat.SetVector(ScreenParamsID, data.screenSize);
                data.mat.SetInt(RequiredBitID, (int)data.requiredBit);
                data.mat.SetInt(UseOcclusionID, data.useOcclusion);

                if (data.useOcclusion != 0)
                {
                    data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);
                    data.mat.SetBuffer(BBoxIndicesID, data.bboxIndexBuffer);
                }

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

    public void Dispose()
    {
        if (_instanceBuffer != null)
            _instanceBuffer.Release();

        if (_bboxIndexBuffer != null)
            _bboxIndexBuffer.Release();

        for (int i = 0; i < _tempMaterials.Count; i++)
        {
            if (_tempMaterials[i] != null)
                CoreUtils.Destroy(_tempMaterials[i]);
        }
        
        _tempMaterials.Clear();
    }
}