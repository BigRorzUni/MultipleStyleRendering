using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class SimpleOutlinePass : ScriptableRenderPass
{
    // Identifies which shader pass to use ("UniversalForward" in URP)
    private ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    // Filters what to render. In this case, only opaque objects
    private FilteringSettings _filteringSettings;
    // Outline material to override the original materials
    private Material _outlineMaterial;

    class PassData
    {
        public RendererListHandle rendererList;
        public TextureHandle idTexture;
    }

    /// <summary>
    /// Constructor sets up filtering and render event.
    /// </summary>
    public SimpleOutlinePass(Material mat)
    {
        // Run this pass after all opaque objects are rendered
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        // Only render opaque objects in this pass
        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        // Store the outline material
        _outlineMaterial = mat;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_outlineMaterial == null) return;
        
        // URP frame data
        var resources = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData = frameData.Get<UniversalLightData>();

        // id of pixels
        var nprData = frameData.Get<NprFrameData>();
        var idTex   = nprData.idTexture;

        // Build draw settings + override material (outline shader)
        var drawing = RenderingUtils.CreateDrawingSettings(
            _shaderTagId,
            renderingData,
            cameraData,
            lightData,
            SortingCriteria.CommonOpaque
        );

        drawing.overrideMaterial = _outlineMaterial;
        drawing.overrideMaterialPassIndex = 0;

        // Create renderer list handle (RenderGraph-friendly)
        var rlp = new RendererListParams(renderingData.cullResults, drawing, _filteringSettings);
        var rl  = renderGraph.CreateRendererList(rlp);

        using var builder = renderGraph.AddRasterRenderPass<PassData>("Simple Outline", out var passData);

        // Render into the camera’s active color/depth
        builder.SetRenderAttachment(resources.activeColorTexture, 0);
        builder.SetRenderAttachmentDepth(resources.activeDepthTexture);

        builder.UseTexture(idTex, AccessFlags.Read);

        passData.rendererList = rl;
        passData.idTexture = idTex;
        builder.UseRendererList(passData.rendererList);

        builder.SetRenderFunc( (PassData data, RasterGraphContext ctx) =>
        {
            _outlineMaterial.SetTexture("_StylisedIDTexture", data.idTexture);
            ctx.cmd.DrawRendererList(data.rendererList);
        });
    }
}

