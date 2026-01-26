using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class IdPrepass : ScriptableRenderPass
{
    // Identifies which shader pass to use ("UniversalForward" in URP)
    private ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    // Filters what to render. In this case, only opaque objects
    private FilteringSettings _filteringSettings;
    private readonly Material _idMaterial;

    class PassData
    {
        public RendererListHandle rendererList;
        public TextureHandle idTexture;
    }
    public struct IdPrepassResult
    {
        public TextureHandle idTexture;
    }

    /// <summary>
    /// Constructor sets up filtering and render event.
    /// </summary>
    public IdPrepass(Material idMaterial, LayerMask layerMask)
    {
        _idMaterial = idMaterial;

        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_idMaterial == null) return;
        
        var resources = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData = frameData.Get<UniversalLightData>();

        // Allocate ID texture (match camera target size)
        var desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;                // keep IDs stable/simple
        desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm; // 0..1 encoded
        desc.sRGB = false;

        TextureHandle idTex = renderGraph.CreateTexture(
            new TextureDesc(desc.width, desc.height)
            {
                name = "_StylisedIDTexture",
                colorFormat = desc.graphicsFormat,
                clearBuffer = true,
                clearColor = Color.black, // ID = 0
                filterMode = FilterMode.Point, // IDs should not blur
                enableRandomWrite = false
            });
        
        var npr = frameData.Create<NprFrameData>();
        npr.idTexture = idTex;

        // Build draw settings + override material
        var drawing = RenderingUtils.CreateDrawingSettings(
            _shaderTagId,
            renderingData,
            cameraData,
            lightData,
            SortingCriteria.CommonOpaque
        );

        drawing.overrideMaterial = _idMaterial;
        drawing.overrideMaterialPassIndex = 0;

        var rlp = new RendererListParams(renderingData.cullResults, drawing, _filteringSettings);
        var rl  = renderGraph.CreateRendererList(rlp);

        using var builder = renderGraph.AddRasterRenderPass<PassData>("ID Prepass", out var passData);

        // Write ID into our texture, share the camera depth (so it matches visible surfaces)
        builder.SetRenderAttachment(idTex, 0);
        builder.SetRenderAttachmentDepth(resources.activeDepthTexture);

        passData.rendererList = rl;
        passData.idTexture = idTex;

        builder.UseRendererList(passData.rendererList);

        builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
        {
            ctx.cmd.DrawRendererList(data.rendererList);
        });
    }
}

