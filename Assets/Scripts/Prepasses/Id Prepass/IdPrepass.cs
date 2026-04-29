using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class IdPrepass : Prepass
{
    readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    readonly FilteringSettings _filteringSettings;
    readonly Shader _idShader;


    class PassData
    {
        public RendererListHandle rendererList;
        public bool debug;
    }

    const string DebugKeyword = "_DEBUG_ID_COLOUR";

    public IdPrepass(Shader idShader) : base("IdPrepass")
    {
        _idShader = idShader;
        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_idShader == null)
            return;

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
        // tweak format to fit what an id texture needs
        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
        camDesc.depthBufferBits = 0;
        camDesc.msaaSamples = 1;
        camDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
        camDesc.sRGB = false;

        // allocate id texture
        TextureHandle idTex = renderGraph.CreateTexture(new TextureDesc(camDesc)
        {
            name = "_NPRIdTexture",
            colorFormat = camDesc.graphicsFormat,
            clearBuffer = true,
            clearColor = Color.clear,
            filterMode = FilterMode.Point,
            useMipMap = false
        });
        nprFrameData.idTexture = idTex;

        DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
            _shaderTagId,
            renderingData,
            cameraData,
            lightData,
            SortingCriteria.CommonOpaque
        );

        drawing.overrideShader = _idShader;
        drawing.overrideShaderPassIndex = 0;

        RendererListParams rlp = new RendererListParams(
            renderingData.cullResults,
            drawing,
            _filteringSettings
        );

        RendererListHandle rendererList = renderGraph.CreateRendererList(rlp);

        using (var builder = renderGraph.AddRasterRenderPass("Fullscreen ID Prepass", out PassData passData, profilingSampler))
        {

            builder.SetRenderAttachment(nprFrameData.idTexture, 0);
            builder.SetRenderAttachmentDepth(frameData.activeDepthTexture, AccessFlags.Write);
            builder.UseRendererList(rendererList);
            builder.AllowGlobalStateModification(true);

            passData.rendererList = rendererList;
            passData.debug = NprTestingConfig.DebugID;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                if (data.debug)
                    ctx.cmd.EnableShaderKeyword(DebugKeyword);
                else
                    ctx.cmd.DisableShaderKeyword(DebugKeyword);

                ctx.cmd.DrawRendererList(data.rendererList);

                if (data.debug)
                    ctx.cmd.DisableShaderKeyword(DebugKeyword);
            });

        
        StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
        foreach (var tag in tags)
        {
            if (tag == null)
                continue;

            nprFrameData.presentImageBits |= tag.imageEffects;

            if (NprTestingConfig.TestMode)
                nprFrameData.presentTestStyles |= tag.currentTestEffects;
        }
        }
    }

    public override void Dispose()
    {

    }
}