using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class EdgesPrepass : ScriptableRenderPass, INprPass
{
    Material _edgeMat;

    public bool debugToScreen;


    static readonly int _DepthTexId   = Shader.PropertyToID("_NprDepthTexture");
    static readonly int _NormalsTexId = Shader.PropertyToID("_NprNormalsTexture");

    static readonly int _ThicknessId      = Shader.PropertyToID("_ThicknessPx");
    static readonly int _DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    static readonly int _DepthStrengthId  = Shader.PropertyToID("_DepthStrength");
    static readonly int _NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
    static readonly int _NormalStrengthId  = Shader.PropertyToID("_NormalStrength");

    public void ApplySettings(NprSettings settings)
    {
        debugToScreen = settings.debugView == NprDebugView.Edges;
    }

    class PassData
    {
        public TextureHandle depth;
        public TextureHandle normals;
        public Material mat;
    }

    class DebugData { public TextureHandle edges; }

    public EdgesPrepass(Shader edgesShader)
    {
        if (edgesShader != null)
            _edgeMat = CoreUtils.CreateEngineMaterial(edgesShader);

        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_edgeMat == null) return;

        var resources  = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        // Get/create NPR frame data
        NprFrameData npr;
        if (frameData.Contains<NprFrameData>())
            npr = frameData.Get<NprFrameData>();
        else
            npr = frameData.Create<NprFrameData>();

        if (!npr.normalsTexture.IsValid())
            return;

        // Allocate edges texture 
        var desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.sRGB = false;

        var edgeFmt = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render) ? GraphicsFormat.R8_UNorm : GraphicsFormat.R16_SFloat;

        var edgesTex = renderGraph.CreateTexture(new TextureDesc(desc.width, desc.height)
        {
            name = "_NprEdgesTexture",
            colorFormat = edgeFmt,
            clearBuffer = true,
            clearColor = Color.black,
            filterMode = FilterMode.Point
        });

        npr.edgesTexture = edgesTex;

        _edgeMat.SetFloat(_ThicknessId, 1f);

        _edgeMat.SetFloat(_DepthThresholdId, 0.02f);
        _edgeMat.SetFloat(_DepthStrengthId, 1.5f);
        
        _edgeMat.SetFloat(_NormalThresholdId, 0.12f);
        _edgeMat.SetFloat(_NormalStrengthId, 1.0f);

        // edge detection fullscreen pass
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("NPR Edges (Screenspace)", out var passData))
        {
            builder.SetRenderAttachment(edgesTex, 0, AccessFlags.Write);

            // declare reads (RenderGraph lifetime + barriers)
            builder.UseTexture(resources.activeDepthTexture, AccessFlags.Read);
            builder.UseTexture(npr.normalsTexture, AccessFlags.Read);

            passData.depth = resources.activeDepthTexture;
            passData.normals = npr.normalsTexture;
            passData.mat = _edgeMat;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                // bind textures expected by shader
                data.mat.SetTexture(_DepthTexId, data.depth);
                data.mat.SetTexture(_NormalsTexId, data.normals);

                // fullscreen draw into edgesTex
                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);

            });
        }

        // debug view
        if (debugToScreen)
        {
            using var builder = renderGraph.AddRasterRenderPass<DebugData>("NPR Edges Debug", out var dbg);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
            builder.UseTexture(edgesTex, AccessFlags.Read);
            dbg.edges = edgesTex;

            builder.SetRenderFunc(static (DebugData data, RasterGraphContext ctx) =>
            {
                // red lines as only using red channel since the tex is stored as an R8_UNorm
                Blitter.BlitTexture(ctx.cmd, data.edges, new Vector4(1, 1, 0, 0), 0, false);
            });
        }
    }
}