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

    /// <summary>
    /// This is where the actual rendering work happens.
    /// </summary>
    // public override void Execute(ScriptableRenderContext context,
    //                                 ref RenderingData renderingData)
    // {
    //     if (_outlineMaterial == null) return;

    //     var cmd = CommandBufferPool.Get("Simple Outline Pass");

    //     // Sort like opaques (front-to-back)
    //     var drawSettings = CreateDrawingSettings(_shaderTagId, ref renderingData, SortingCriteria.CommonOpaque);
    //     drawSettings.overrideMaterial = _outlineMaterial;
    //     drawSettings.overrideMaterialPassIndex = 0;

    //     context.ExecuteCommandBuffer(cmd);
    //     cmd.Clear();


    //     context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref _filteringSettings);

    //     context.ExecuteCommandBuffer(cmd);
    //     CommandBufferPool.Release(cmd);
    // }
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_outlineMaterial == null) return;

        // URP frame data
        var resources      = frameData.Get<UniversalResourceData>();
        var cameraData     = frameData.Get<UniversalCameraData>();
        var renderingData  = frameData.Get<UniversalRenderingData>();
        var lightData     = frameData.Get<UniversalLightData>();

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

        passData.rendererList = rl;
        builder.UseRendererList(passData.rendererList);

        builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
        {
            ctx.cmd.DrawRendererList(data.rendererList);
        });
    }
}

