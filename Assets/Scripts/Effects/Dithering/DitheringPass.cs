using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class DitheringPass : EffectPass
{
    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int DitheringBitID = Shader.PropertyToID("_DitheringBit");

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int UseOcclusionID = Shader.PropertyToID("_UseOcclusion");
    static readonly int MaskBufferID = Shader.PropertyToID("_BBoxMasks");

    private class PassData
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

    public DitheringPass(Shader shader, StyleBits.ImageSpaceEffect requiredBit) : base(shader, "DitheringPass", requiredBit)
    {
    }

    protected override bool ShouldRun(UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        if (!base.ShouldRun(frameData, cameraData, nprFrameData))
            return false;

        if (!nprFrameData.sourceTexture.IsValid())
            return false;

        return true;
    }

    protected override void RunFullscreen(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        using (var builder = renderGraph.AddRasterRenderPass("Fullscreen Dithering Pass", out PassData passData))
        {
            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.requiredBit = (int)_requiredBit;

            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexID, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetInt(DitheringBitID, data.requiredBit);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }
    }

    protected override void RunCpu(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        foreach (var bbox in nprFrameData.bboxes)
        {
            if ((bbox.styles & _requiredBit) == 0)
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
                passData.requiredBit = (int)_requiredBit;

                passData.useOcclusion = 0;

                if (NprTestingConfig.UseOcclusion && nprFrameData.visibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.visibilityBuffer;
                    passData.useOcclusion = 1;
                }

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
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

    protected override void RunGpu(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        if (nprFrameData.rectBuffer == null || nprFrameData.maskBuffer == null || nprFrameData.indirectArgsBuffer == null)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
        Vector4 screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);

        using (var builder = renderGraph.AddRasterRenderPass("Batched Dithering Pass (GPU)", out PassData passData))
        {
            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.requiredBit = (int)_requiredBit;

            passData.instanceBuffer = nprFrameData.rectBuffer;
            passData.maskBuffer = nprFrameData.maskBuffer;
            passData.indirectArgsBuffer = nprFrameData.indirectArgsBuffer;
            passData.screenSize = screenSize;

            passData.useOcclusion = 0;

            if (NprTestingConfig.UseOcclusion && nprFrameData.visibilityBuffer != null)
            {
                passData.visibilityBuffer = nprFrameData.visibilityBuffer;
                passData.useOcclusion = 1;
            }

            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
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

                ctx.cmd.DrawProceduralIndirect(Matrix4x4.identity, data.mat, 0, MeshTopology.Triangles, data.indirectArgsBuffer, 0);
            });
        }
    }
}