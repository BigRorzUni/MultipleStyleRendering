using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[System.Serializable]
public class DitheringPass : ScriptableRenderPass
{
    Material _mat;
    StyleBits.ImageSpaceEffect _ditheringBit;

    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int DitheringBitID = Shader.PropertyToID("_DitheringBit");

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int UseOcclusionID = Shader.PropertyToID("_UseOcclusion");
    static readonly int MaskBufferID = Shader.PropertyToID("_BBoxMasks");

    class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;
        public int requiredBit;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;

        public ComputeBuffer visibilityBuffer;
        public int useOcclusion;

        public ComputeBuffer maskBuffer;
        public ComputeBuffer indirectArgsBuffer;
    }

    class CopyPassData
    {
        public TextureHandle src;
    }

    public DitheringPass(Shader shader, StyleBits.ImageSpaceEffect requiredBit)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _ditheringBit = requiredBit;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null)
            return;

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
        if ((nprFrameData.presentImageBits & _ditheringBit) == 0)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        switch (NprTestingConfig.RenderMode)
        {
            case NprRenderMode.Fullscreen:
                RunFullscreen(renderGraph, frameData, nprFrameData);
                return;

            case NprRenderMode.CPU:
                RunCpu(renderGraph, frameData, nprFrameData);
                return;

            case NprRenderMode.GPU:
                RunGpu(renderGraph, frameData, nprFrameData, camDesc);
                return;
        }
    }

    void RunFullscreen(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData)
    {
        using (var builder = renderGraph.AddRasterRenderPass("Fullscreen Dithering Pass", out PassData passData))
        {
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
            builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);

            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.requiredBit = (int)_ditheringBit;

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexID, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetInt(DitheringBitID, data.requiredBit);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }
    }

    void RunCpu(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData)
    {
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        // for each bbox in cpu-side list we scissor the texture and apply effect
        foreach (var bbox in nprFrameData.bboxes)
        {
            if ((bbox.styles & _ditheringBit) == 0)
                continue;

            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            using (var builder = renderGraph.AddRasterRenderPass($"BBox Dithering ({bbox.box})", out PassData passData))
            {
                builder.AllowGlobalStateModification(true);

                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;
                passData.rect = bbox.box;
                passData.requiredBit = (int)_ditheringBit;

                passData.useOcclusion = 0;

                if (NprTestingConfig.UseOcclusion && nprFrameData.bboxVisibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.useOcclusion = 1;
                }

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetInt(DitheringBitID, data.requiredBit);

                    ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                    ctx.cmd.DisableScissorRect();
                });
            }
        }
    }

    void RunGpu(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData, RenderTextureDescriptor camDesc)
    {
        if (nprFrameData.bboxRectBuffer == null ||
            nprFrameData.bboxMaskBuffer == null ||
            nprFrameData.bboxIndirectArgsBuffer == null)
            return;

        Vector4 screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);

        using (var builder = renderGraph.AddRasterRenderPass("Batched Dithering Pass (GPU)", out PassData passData))
        {
            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.requiredBit = (int)_ditheringBit;

            passData.instanceBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.indirectArgsBuffer = nprFrameData.bboxIndirectArgsBuffer;
            passData.screenSize = screenSize;

            passData.useOcclusion = 0;

            if (NprTestingConfig.UseOcclusion && nprFrameData.bboxVisibilityBuffer != null)
            {
                passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.useOcclusion = 1;
            }

            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.UseTexture(passData.ids, AccessFlags.Read);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexID, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetInt(DitheringBitID, data.requiredBit);

                data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
                data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
                data.mat.SetVector(ScreenParamsID, data.screenSize);
                data.mat.SetInt(UseOcclusionID, data.useOcclusion);

                if (data.useOcclusion != 0)
                    data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);

                ctx.cmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    data.mat,
                    0,
                    MeshTopology.Triangles,
                    data.indirectArgsBuffer,
                    0
                );
            });
        }
    }
}