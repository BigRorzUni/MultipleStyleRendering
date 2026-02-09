using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class EdgesPrepass : ScriptableRenderPass, INprPass
{
    Material _edgeMat;

    public bool debugToScreen;
    public float outlineThickness;

    static readonly int _DepthTexId  = Shader.PropertyToID("_NprDepthTexture");
    static readonly int _NormalsTexId = Shader.PropertyToID("_NprNormalsTexture");
    static readonly int _IdTexId = Shader.PropertyToID("_NprIdTexture");

    static readonly int _ThicknessId = Shader.PropertyToID("_ThicknessPx");
    static readonly int _DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    static readonly int _DepthStrengthId = Shader.PropertyToID("_DepthStrength");
    static readonly int _NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
    static readonly int _NormalStrengthId = Shader.PropertyToID("_NormalStrength");



    public void ApplySettings(NprSettings settings)
    {
        debugToScreen = settings.debugView == NprDebugView.Edges;
        outlineThickness = settings.outlineThickness;
    }

    class PassData
    {
        public TextureHandle depth;
        public TextureHandle normals;
        public TextureHandle ids;
        public Material mat;
    }

    class DebugData 
    { 
        public TextureHandle edges; 
    }

    public EdgesPrepass(Shader edgesShader)
    {
        if (edgesShader != null)
            _edgeMat = CoreUtils.CreateEngineMaterial(edgesShader);

        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_edgeMat == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        // get/create NPR frame data
        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        if (!nprFrameData.normalsTexture.IsValid())
            return;

        // match edge texture to camera resolution + settings
        RenderTextureDescriptor edgesTexDescriptor = cameraData.cameraTargetDescriptor;

       // tweak format to fit what an edges texture needs
        edgesTexDescriptor.depthBufferBits = 0;
        edgesTexDescriptor.msaaSamples = 1;
        edgesTexDescriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
        edgesTexDescriptor.sRGB = false;

        // allocate edges texture 
        TextureHandle edgesTex = renderGraph.CreateTexture(new TextureDesc(edgesTexDescriptor)
        {
            name = "_NprEdgesTexture",
            colorFormat = edgesTexDescriptor.graphicsFormat,
            clearBuffer = true,
            clearColor = Color.black,
            filterMode = FilterMode.Point
        });
        // nprFrameData.edgesTexture = edgesTex;

        _edgeMat.SetFloat(_ThicknessId, outlineThickness);

        _edgeMat.SetFloat(_DepthThresholdId, 0.02f);
        _edgeMat.SetFloat(_DepthStrengthId, 1.0f);
        
        _edgeMat.SetFloat(_NormalThresholdId, 0.12f);
        _edgeMat.SetFloat(_NormalStrengthId, 1.0f);

        // edge detection fullscreen pass
        using (var builder = renderGraph.AddRasterRenderPass("NPR Edges (Screenspace)", out PassData passData))
        {
            // write to edge texture
            builder.SetRenderAttachment(edgesTex, 0, AccessFlags.Write);

            // read from depth, normals and id textures
            builder.UseTexture(frameData.activeDepthTexture, AccessFlags.Read);
            builder.UseTexture(nprFrameData.normalsTexture, AccessFlags.Read);
            builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);

            passData.depth = frameData.activeDepthTexture;
            passData.normals = nprFrameData.normalsTexture;
            passData.ids = nprFrameData.idTexture;
            passData.mat = _edgeMat;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                // bind textures expected by shader
                data.mat.SetTexture(_DepthTexId, data.depth);
                data.mat.SetTexture(_NormalsTexId, data.normals);
                data.mat.SetTexture(_IdTexId, data.ids);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);

            });
        }

        // debug view
        if (debugToScreen)
        {
            using (var builder = renderGraph.AddRasterRenderPass("NPR Edges Debug", out DebugData debugData))
            {
                // write to screen colour
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                // read from edge texture
                builder.UseTexture(edgesTex, AccessFlags.Read);
                debugData.edges = edgesTex;

                builder.SetRenderFunc(static (DebugData data, RasterGraphContext ctx) =>
                {
                    // red lines as only using red channel since the tex is stored as an R8_UNorm
                    Blitter.BlitTexture(ctx.cmd, data.edges, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }
}