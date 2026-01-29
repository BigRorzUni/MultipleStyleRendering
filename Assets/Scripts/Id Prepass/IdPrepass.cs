using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class IdPrepass : ScriptableRenderPass, INprPass
{
    readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    readonly FilteringSettings _filteringSettings;
    readonly Shader _idShader;

    public bool debugToScreen;

    public void ApplySettings(NprSettings settings)
    {
        debugToScreen = settings.debugView == NprDebugView.StylisedID;
    }  

    const string DebugKeyword = "_DEBUG_ID_COLOUR";

    class PassData
    {
        public RendererListHandle rendererList;
        public bool debug;
    }

    public IdPrepass(Shader idShader, LayerMask layerMask)
    {
        _idShader = idShader;
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_idShader == null) return;

        var resources = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData = frameData.Get<UniversalLightData>();

        TextureHandle idTex = TextureHandle.nullHandle;

        if (!debugToScreen)
        {
            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
            desc.sRGB = false;

            idTex = renderGraph.CreateTexture(new TextureDesc(desc.width, desc.height)
            {
                name = "_StylisedIDTexture",
                colorFormat = desc.graphicsFormat,
                clearBuffer = true,
                clearColor = Color.black,
                filterMode = FilterMode.Point,
                useMipMap = false
            });

            NprFrameData npr;
            if (frameData.Contains<NprFrameData>())
                npr = frameData.Get<NprFrameData>();
            else
                npr = frameData.Create<NprFrameData>();

            npr.idTexture = idTex;
        }

        var drawing = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);

        drawing.overrideShader = _idShader;
        drawing.overrideShaderPassIndex = 0;

        var rlp = new RendererListParams(renderingData.cullResults, drawing, _filteringSettings);
        var rl = renderGraph.CreateRendererList(rlp);

        using var builder = renderGraph.AddRasterRenderPass<PassData>("ID Prepass", out var passData);

        if (debugToScreen)
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
        else
            builder.SetRenderAttachment(idTex, 0);

        builder.SetRenderAttachmentDepth(resources.activeDepthTexture);

        builder.UseRendererList(rl);

        // for the global keyword in id shader
        builder.AllowGlobalStateModification(true);

        passData.rendererList = rl;
        passData.debug = debugToScreen;

        builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
        {
            if (data.debug) 
                ctx.cmd.EnableShaderKeyword(DebugKeyword);
            else            
                ctx.cmd.DisableShaderKeyword(DebugKeyword);

            ctx.cmd.DrawRendererList(data.rendererList);

            // clean up global keyword
            if (data.debug) ctx.cmd.DisableShaderKeyword(DebugKeyword);
        });
    }
}