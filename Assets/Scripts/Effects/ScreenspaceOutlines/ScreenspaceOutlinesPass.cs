using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[System.Serializable]
public class ScreenspaceOutlinesPass : ScriptableRenderPass, INprPass
{
    StyleBits.ImageSpaceEffect _outlinesBit;
    Material _mat;

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
    public Color _outlineColour;
    public float _outlineThickness;

    class PassData
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

    class CopyPassData
    {
        public TextureHandle src;
    }

    public void ApplySettings(Settings settings)
    {
        _outlineColour = settings.outlines.colour;
        _outlineThickness = settings.outlines.thickness;
        _depthThreshold = settings.outlines.depthThreshold;
        _depthStrength = settings.outlines.depthStrength;
        _normalThreshold = settings.outlines.normalThreshold;
        _normalStrength = settings.outlines.normalStrength;
    }

    public ScreenspaceOutlinesPass(Shader shader, StyleBits.ImageSpaceEffect requiredBit)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _outlinesBit = requiredBit;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        if (!frameData.activeDepthTexture.IsValid())
            return;

        if(!frameData.cameraNormalsTexture.IsValid())
            return;

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        if (!nprFrameData.sourceTexture.IsValid())
            return;
        if (!nprFrameData.idTexture.IsValid())
            return;

        if ((nprFrameData.presentImageBits & _outlinesBit) == 0)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        // SOURCE COPY (shared across all modes)
        using (var builder = renderGraph.AddRasterRenderPass("NPR Outlines Source Copy", out CopyPassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc((CopyPassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

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
        using (var builder = renderGraph.AddRasterRenderPass("Fullscreen Outline Pass", out PassData passData))
        {
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
            builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);
            builder.UseTexture(frameData.cameraNormalsTexture, AccessFlags.Read);
            builder.UseTexture(frameData.activeDepthTexture, AccessFlags.Read);

            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.normals = frameData.cameraNormalsTexture;
            passData.depth = frameData.activeDepthTexture;
            passData.requiredBit = (int)_outlinesBit;

            passData.mat = _mat;

            passData.outlineCol = _outlineColour;
            passData.thicknessPx = _outlineThickness;
            passData.depthThreshold = _depthThreshold;
            passData.depthStrength = _depthStrength;
            passData.normalThreshold = _normalThreshold;
            passData.normalStrength = _normalStrength;

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
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

    void RunCpu(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData)
    {
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        foreach (var bbox in nprFrameData.bboxes)
        {
            if ((bbox.styles & _outlinesBit) == 0)
                continue;

            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            using (var builder = renderGraph.AddRasterRenderPass($"BBox Outline ({bbox.box})", out PassData passData))
            {
                builder.AllowGlobalStateModification(true);

                builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
                builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);
                builder.UseTexture(frameData.cameraNormalsTexture, AccessFlags.Read);
                builder.UseTexture(frameData.activeDepthTexture, AccessFlags.Read);

                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.normals = frameData.cameraNormalsTexture;
                passData.depth = frameData.activeDepthTexture;
                passData.rect = bbox.box;
                passData.requiredBit = (int)_outlinesBit;

                passData.mat = _mat;

                passData.outlineCol = _outlineColour;
                passData.thicknessPx = _outlineThickness;
                passData.depthThreshold = _depthThreshold;
                passData.depthStrength = _depthStrength;
                passData.normalThreshold = _normalThreshold;
                passData.normalStrength = _normalStrength;

                passData.useOcclusion = 0;

                if (NprTestingConfig.UseOcclusion && nprFrameData.bboxVisibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.useOcclusion = 1;
                }

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);
                builder.UseTexture(passData.normals, AccessFlags.Read);
                builder.UseTexture(passData.depth, AccessFlags.Read);

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
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

    void RunGpu(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData, RenderTextureDescriptor camDesc)
    {
        if (nprFrameData.bboxRectBuffer == null ||
            nprFrameData.bboxMaskBuffer == null ||
            nprFrameData.bboxIndirectArgsBuffer == null)
            return;

        Vector4 screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);

        using (var builder = renderGraph.AddRasterRenderPass("Batched Outline Pass (GPU)", out PassData passData))
        {
            builder.AllowGlobalStateModification(true);

            builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
            builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);
            builder.UseTexture(frameData.cameraNormalsTexture, AccessFlags.Read);
            builder.UseTexture(frameData.activeDepthTexture, AccessFlags.Read);

            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.normals = frameData.cameraNormalsTexture;
            passData.depth = frameData.activeDepthTexture;
            passData.requiredBit = (int)_outlinesBit;

            passData.mat = _mat;

            passData.outlineCol = _outlineColour;
            passData.thicknessPx = _outlineThickness;
            passData.depthThreshold = _depthThreshold;
            passData.depthStrength = _depthStrength;
            passData.normalThreshold = _normalThreshold;
            passData.normalStrength = _normalStrength;

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

            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.UseTexture(passData.normals, AccessFlags.Read);
            builder.UseTexture(passData.depth, AccessFlags.Read);

            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
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