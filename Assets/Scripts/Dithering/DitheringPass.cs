using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;


[System.Serializable]
public class DitheringPass : ScriptableRenderPass//, INprPass
{
    Material _mat;
    StyleBits.ImageSpaceEffect _requiredBit;

    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");

    ComputeBuffer _instanceBuffer;
    int _instanceBufferCapacity = 0;

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int UseOcclusionCullingID = Shader.PropertyToID("_UseOcclusionCulling");


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

    class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;
        public int instanceCount;

        public ComputeBuffer visibilityBuffer;
        public int useOcclusionCulling;
    }

    public DitheringPass(Shader shader, StyleBits.ImageSpaceEffect requiredBit)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _requiredBit = requiredBit;
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
        if(!nprFrameData.sourceTexture.IsValid())
            return;
        if ((nprFrameData.presentImageBits & _requiredBit) == 0)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        // copy camera color into srcCopy
        using (var builder = renderGraph.AddRasterRenderPass("NPR Dither Source Copy", out PassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        if(!NprTestingConfig.UseBoundingBoxes)
        {
            // dithering pass
            using (var builder = renderGraph.AddRasterRenderPass("Fullscreen Dithering Pass", out PassData passData))
            {
                // write to screen colour
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                // read from id and screen textures
                builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
                builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);

                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);

                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                });
            }

            return;
        }

        if(nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        if (!NprTestingConfig.BatchedDraws)
        {
            // current per-bbox scissored path
            foreach(var bbox in nprFrameData.bboxes)
            {
                if (bbox.box.width <= 0 || bbox.box.height <= 0)
                    continue;
                
                if((bbox.styles & StyleBits.ImageSpaceEffect.Dithering) == 0)
                    continue;

                // TextureHandle outTex = renderGraph.CreateTexture(bbox.desc);
                using (var builder = renderGraph.AddRasterRenderPass($"BBox Dithering ({bbox.box})", out PassData passData))
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

                // bbox.currentTex = outTex;
            }

            return;
        }
            // new batched instanced path
            // Debug.Log("Batched dithering pass");

            List<BoundingBox> batchedBBoxes = new List<BoundingBox>();
            List<QuadInstanceData> instances = new List<QuadInstanceData>();
            foreach (var bbox in nprFrameData.bboxes)
            {
                if (bbox.box.width <= 0 || bbox.box.height <= 0)
                    continue;

                if ((bbox.styles & StyleBits.ImageSpaceEffect.Dithering) == 0)
                    continue;

                batchedBBoxes.Add(bbox);
                
                instances.Add(new QuadInstanceData
                {
                    rect = new Vector4(bbox.box.x, bbox.box.y, bbox.box.width, bbox.box.height),
                    index = bbox.frameIndex
                });
            }

            if (instances.Count == 0)
                return;

            EnsureInstanceBufferCapacity(instances.Count);
            _instanceBuffer.SetData(instances);

            using (var builder = renderGraph.AddRasterRenderPass("Batched Dithering Pass", out PassData passData))
            {
                passData.mat = _mat;

                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;

                passData.instanceBuffer = _instanceBuffer;
                passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
                passData.instanceCount = instances.Count;

                if(NprTestingConfig.UseOcclusionCulling)
                {
                    passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.useOcclusionCulling = 1;
                }
                else
                {
                    passData.visibilityBuffer = null;
                    passData.useOcclusionCulling = 0;
                }

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetBuffer(InstanceBufferID,data.instanceBuffer);
                    data.mat.SetVector(ScreenParamsID, data.screenSize);
                    data.mat.SetInt(UseOcclusionCullingID, data.useOcclusionCulling);
                    if(data.useOcclusionCulling == 1 && data.visibilityBuffer != null)
                        data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);
                    

                    ctx.cmd.DrawProcedural(
                        Matrix4x4.identity,
                        data.mat, // dithering material
                        0,
                        MeshTopology.Triangles,
                        6, // 2 triangles per quad
                        data.instanceCount // 1 instance per bbox
                    );

                });
            }

        

   }
}