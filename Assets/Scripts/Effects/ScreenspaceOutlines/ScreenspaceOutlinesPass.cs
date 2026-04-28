using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class ScreenspaceOutlinesPass : EffectPass
{
    static readonly int DepthTexId = Shader.PropertyToID("_NprDepthTexture");
    static readonly int NormalsTexId = Shader.PropertyToID("_NprNormalsTexture");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int SourceTexId = Shader.PropertyToID("_NprSourceTexture");
    static readonly int OutlinesBitID = Shader.PropertyToID("_OutlinesBit");

    static readonly int ThicknessId = Shader.PropertyToID("_ThicknessPx");
    static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    static readonly int DepthStrengthId = Shader.PropertyToID("_DepthStrength");
    static readonly int NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
    static readonly int NormalStrengthId = Shader.PropertyToID("_NormalStrength");
    static readonly int OutlineColourId = Shader.PropertyToID("_OutlineColour");

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int UseOcclusionID = Shader.PropertyToID("_UseOcclusion");
    static readonly int MaskBufferID = Shader.PropertyToID("_BBoxMasks");

    float _depthThreshold = 0.02f;
    float _depthStrength = 1.0f;
    float _normalThreshold = 0.2f;
    float _normalStrength = 1.0f;
    public Color _outlineColour = Color.black;
    public float _outlineThickness = 1.5f;

    private class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public TextureHandle normals;
        public TextureHandle depth;

        public Material mat;
        public RectInt rect;
        public int requiredBit;

        public Color outlineCol;
        public float thicknessPx;
        public float depthThreshold;
        public float depthStrength;
        public float normalThreshold;
        public float normalStrength;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;

        public ComputeBuffer visibilityBuffer;
        public int useOcclusion;

        public ComputeBuffer maskBuffer;
        public ComputeBuffer indirectArgsBuffer;
    }

    public ScreenspaceOutlinesPass(Shader shader, StyleBits.ImageSpaceEffect requiredBit) : base(shader, "ScreenspaceOutlinesPass", requiredBit)
    {
    }

    protected override bool ShouldRun(UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        if (!base.ShouldRun(frameData, cameraData, nprFrameData))
            return false;

        if (!frameData.activeDepthTexture.IsValid())
            return false;

        if (!frameData.cameraNormalsTexture.IsValid())
            return false;

        if (!nprFrameData.sourceTexture.IsValid())
            return false;

        return true;
    }

    protected override void RunFullscreen(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        using (var builder = renderGraph.AddRasterRenderPass("Fullscreen Outline Pass", out PassData passData))
        {
            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.normals = frameData.cameraNormalsTexture;
            passData.depth = frameData.activeDepthTexture;
            passData.requiredBit = (int)_requiredBit;
            passData.mat = _mat;

            passData.outlineCol = _outlineColour;
            passData.thicknessPx = _outlineThickness;
            passData.depthThreshold = _depthThreshold;
            passData.depthStrength = _depthStrength;
            passData.normalThreshold = _normalThreshold;
            passData.normalStrength = _normalStrength;

            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.UseTexture(passData.normals, AccessFlags.Read);
            builder.UseTexture(passData.depth, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexId, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetTexture(NormalsTexId, data.normals);
                data.mat.SetTexture(DepthTexId, data.depth);
                data.mat.SetInt(OutlinesBitID, data.requiredBit);

                data.mat.SetColor(OutlineColourId, data.outlineCol);
                data.mat.SetFloat(ThicknessId, data.thicknessPx);
                data.mat.SetFloat(DepthThresholdId, data.depthThreshold);
                data.mat.SetFloat(DepthStrengthId, data.depthStrength);
                data.mat.SetFloat(NormalThresholdId, data.normalThreshold);
                data.mat.SetFloat(NormalStrengthId, data.normalStrength);

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

            using (var builder = renderGraph.AddRasterRenderPass($"BBox Outline ({bbox.box})", out PassData passData))
            {
                builder.AllowGlobalStateModification(true);

                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.normals = frameData.cameraNormalsTexture;
                passData.depth = frameData.activeDepthTexture;
                passData.rect = bbox.box;
                passData.requiredBit = (int)_requiredBit;
                passData.mat = _mat;

                passData.outlineCol = _outlineColour;
                passData.thicknessPx = _outlineThickness;
                passData.depthThreshold = _depthThreshold;
                passData.depthStrength = _depthStrength;
                passData.normalThreshold = _normalThreshold;
                passData.normalStrength = _normalStrength;

                passData.useOcclusion = 0;

                if (NprTestingConfig.UseOcclusion && nprFrameData.visibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.visibilityBuffer;
                    passData.useOcclusion = 1;
                }

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);
                builder.UseTexture(passData.normals, AccessFlags.Read);
                builder.UseTexture(passData.depth, AccessFlags.Read);
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexId, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetTexture(NormalsTexId, data.normals);
                    data.mat.SetTexture(DepthTexId, data.depth);
                    data.mat.SetInt(OutlinesBitID, data.requiredBit);

                    data.mat.SetColor(OutlineColourId, data.outlineCol);
                    data.mat.SetFloat(ThicknessId, data.thicknessPx);
                    data.mat.SetFloat(DepthThresholdId, data.depthThreshold);
                    data.mat.SetFloat(DepthStrengthId, data.depthStrength);
                    data.mat.SetFloat(NormalThresholdId, data.normalThreshold);
                    data.mat.SetFloat(NormalStrengthId, data.normalStrength);

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

        using (var builder = renderGraph.AddRasterRenderPass("Batched Outline Pass (GPU)", out PassData passData))
        {
            builder.AllowGlobalStateModification(true);

            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.normals = frameData.cameraNormalsTexture;
            passData.depth = frameData.activeDepthTexture;
            passData.requiredBit = (int)_requiredBit;
            passData.mat = _mat;

            passData.outlineCol = _outlineColour;
            passData.thicknessPx = _outlineThickness;
            passData.depthThreshold = _depthThreshold;
            passData.depthStrength = _depthStrength;
            passData.normalThreshold = _normalThreshold;
            passData.normalStrength = _normalStrength;

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
            builder.UseTexture(passData.normals, AccessFlags.Read);
            builder.UseTexture(passData.depth, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexId, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetTexture(NormalsTexId, data.normals);
                data.mat.SetTexture(DepthTexId, data.depth);
                data.mat.SetInt(OutlinesBitID, data.requiredBit);

                data.mat.SetColor(OutlineColourId, data.outlineCol);
                data.mat.SetFloat(ThicknessId, data.thicknessPx);
                data.mat.SetFloat(DepthThresholdId, data.depthThreshold);
                data.mat.SetFloat(DepthStrengthId, data.depthStrength);
                data.mat.SetFloat(NormalThresholdId, data.normalThreshold);
                data.mat.SetFloat(NormalStrengthId, data.normalStrength);

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