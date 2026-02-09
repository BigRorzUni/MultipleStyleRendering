using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class ScreenspaceOutlinesPass : ScriptableRenderPass, INprPass
{
    Material _mat;

    public Color outlineColour;
    public float outlineThickness;

    static readonly int _DepthTexId = Shader.PropertyToID("_NprDepthTexture");
    static readonly int _NormalsTexId = Shader.PropertyToID("_NprNormalsTexture");
    static readonly int _IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int _SourceTexId = Shader.PropertyToID("_NprSourceTexture");

    static readonly int _ThicknessId = Shader.PropertyToID("_ThicknessPx");
    static readonly int _DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    static readonly int _DepthStrengthId = Shader.PropertyToID("_DepthStrength");
    static readonly int _NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
    static readonly int _NormalStrengthId = Shader.PropertyToID("_NormalStrength");
    
    static readonly int OutlineColourId = Shader.PropertyToID("_OutlineColour");
    //static readonly int EdgesTexID = Shader.PropertyToID("_NprEdgesTexture");

    [SerializeField] 
    float _depthThreshold = 0.02f;
    [SerializeField] 
    float _depthStrength = 1.0f;
    [SerializeField] 
    float _normalThreshold = 0.2f;
    [SerializeField] 
    float _normalStrength = 1.0f;

    public void ApplySettings(NprSettings settings)
    {
        outlineColour = settings.outlineColour;
        outlineThickness = settings.outlineThickness;
    }

    class CopyData
    {
        public TextureHandle src;
    }

    class PassData
    {
        public TextureHandle depth;
        public TextureHandle normals;
        public TextureHandle ids;
        public TextureHandle source;

        public Material mat;

        public Color outlineCol;
        public float thicknessPx;
        public float depthThreshold;
        public float depthStrength;
        public float normalThreshold;
        public float normalStrength;
    }

    public ScreenspaceOutlinesPass(Shader shader)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
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

        // if (!nprFrameData.sourceTexture.IsValid())  
        //     return;
        if (!nprFrameData.idTexture.IsValid())      
            return;
        if (!nprFrameData.normalsTexture.IsValid()) 
            return;
        // using urp camera depth texture
        if (!frameData.activeDepthTexture.IsValid()) 
            return;

        // copy frame into a texture
        RenderTextureDescriptor srcDesc = cameraData.cameraTargetDescriptor;
        srcDesc.depthBufferBits = 0;
        srcDesc.msaaSamples = 1;
        srcDesc.sRGB = false;

        TextureHandle srcCopy = renderGraph.CreateTexture(new TextureDesc(srcDesc)
        {
            name = "_NprOutlinesSourceCopy",
            colorFormat = srcDesc.graphicsFormat,
            clearBuffer = false,
            filterMode = FilterMode.Point
        });

        // blit frame into a copy for sampling in outlines pass
        using (var builder = renderGraph.AddRasterRenderPass("NPR Outlines Copy Pass", out CopyData copyData))
        {
            builder.SetRenderAttachment(srcCopy, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyData.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (CopyData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), 0, false);
            });
        }

        using (var builder = renderGraph.AddRasterRenderPass("Screenspace Outline Composite", out PassData passData))
        {
            // write to screen colour
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            // read from normal, id, depth and source textures
            // builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
            builder.UseTexture(srcCopy, AccessFlags.Read);
            builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);
            builder.UseTexture(nprFrameData.normalsTexture, AccessFlags.Read);
            builder.UseTexture(frameData.activeDepthTexture, AccessFlags.Read);

            // passData.source = nprFrameData.sourceTexture;
            passData.source = srcCopy;
            passData.ids = nprFrameData.idTexture;
            passData.normals = nprFrameData.normalsTexture;
            passData.depth = frameData.activeDepthTexture;

            passData.mat = _mat;

            passData.outlineCol = outlineColour;
            passData.thicknessPx = outlineThickness;
            passData.depthThreshold = _depthThreshold;
            passData.depthStrength = _depthStrength;
            passData.normalThreshold = _normalThreshold;
            passData.normalStrength = _normalStrength;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(_SourceTexId, data.source);
                data.mat.SetTexture(_IdTexId, data.ids);
                data.mat.SetTexture(_NormalsTexId, data.normals);
                data.mat.SetTexture(_DepthTexId, data.depth);

                data.mat.SetColor(OutlineColourId, data.outlineCol);

                data.mat.SetFloat(_ThicknessId, data.thicknessPx);
                data.mat.SetFloat(_DepthThresholdId, data.depthThreshold);
                data.mat.SetFloat(_DepthStrengthId, data.depthStrength);
                data.mat.SetFloat(_NormalThresholdId, data.normalThreshold);
                data.mat.SetFloat(_NormalStrengthId, data.normalStrength);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }
    }
}