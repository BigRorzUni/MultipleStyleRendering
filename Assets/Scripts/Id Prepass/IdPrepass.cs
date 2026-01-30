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

    class PassData
    {
        public RendererListHandle rendererList;
        public bool debug;
    }

    const string DebugKeyword = "_DEBUG_ID_COLOUR";

    public IdPrepass(Shader idShader, LayerMask layerMask)
    {
        _idShader = idShader;
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_idShader == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
        UniversalRenderingData renderingData = frameContext.Get<UniversalRenderingData>();
        UniversalLightData lightData = frameContext.Get<UniversalLightData>();

        // get/create NPR frame data
        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        // match id texture to camera resolution + settings
        RenderTextureDescriptor idTexDescriptor = cameraData.cameraTargetDescriptor;

       // tweak format to fit what an id texture needs
        idTexDescriptor.depthBufferBits = 0;
        idTexDescriptor.msaaSamples = 1;
        idTexDescriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
        idTexDescriptor.sRGB = false;

        // allocate id texture
        TextureHandle idTex = renderGraph.CreateTexture(new TextureDesc(idTexDescriptor)
        {
            name = "_StylisedIDTexture",
            colorFormat = idTexDescriptor.graphicsFormat,
            clearBuffer = true,
            clearColor = Color.black,
            filterMode = FilterMode.Point,
            useMipMap = false
        });
        nprFrameData.idTexture = idTex;

        // draw objects with id shader
        DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);

        drawing.overrideShader = _idShader;
        drawing.overrideShaderPassIndex = 0;

        RendererListParams rlp = new RendererListParams(renderingData.cullResults, drawing, _filteringSettings);
        RendererListHandle rendererList = renderGraph.CreateRendererList(rlp);

        // can't just blit for debugging as the unhashed values are all <10
        using (var builder = renderGraph.AddRasterRenderPass("ID Prepass", out PassData passData))
        {
            if (debugToScreen)
                builder.SetRenderAttachment(frameData.activeColorTexture, 0);
            else
                builder.SetRenderAttachment(idTex, 0);

            builder.SetRenderAttachmentDepth(frameData.activeDepthTexture);

            builder.UseRendererList(rendererList);

            // for the global keyword in id shader
            builder.AllowGlobalStateModification(true);

            passData.rendererList = rendererList;
            passData.debug = debugToScreen;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                if (data.debug) 
                    ctx.cmd.EnableShaderKeyword(DebugKeyword);
                else            
                    ctx.cmd.DisableShaderKeyword(DebugKeyword);

                ctx.cmd.DrawRendererList(data.rendererList);

                // clean up global keyword
                if (data.debug) 
                    ctx.cmd.DisableShaderKeyword(DebugKeyword);
            });
        }
    }
}