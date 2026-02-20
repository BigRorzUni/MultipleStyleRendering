using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class ToonPass : ScriptableRenderPass, INprPass
{
    readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    FilteringSettings _filteringSettings;
    readonly Shader _toonShader;

    public void ApplySettings(NprSettings settings)
    {
        
    }

    class PassData 
    {
        public RendererListHandle rl; 
    }

    public ToonPass(Shader toonShader)
    {
        _toonShader = toonShader;
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque)
        {
            renderingLayerMask = (uint)StyleBits.ObjectSpaceEffect.Toon
        };
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_toonShader == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraDescriptor = frameContext.Get<UniversalCameraData>();
        UniversalRenderingData renderingData = frameContext.Get<UniversalRenderingData>();
        UniversalLightData lightData = frameContext.Get<UniversalLightData>();

        // draw objects with toon shader
        DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraDescriptor, lightData, SortingCriteria.CommonOpaque);

        drawing.overrideShader = _toonShader;
        drawing.overrideShaderPassIndex = 0;

        RendererListParams rlp = new RendererListParams(renderingData.cullResults, drawing, _filteringSettings);
        RendererListHandle rendererList = renderGraph.CreateRendererList(rlp);

        using (var builder = renderGraph.AddRasterRenderPass("Toon", out PassData passData))
        {
            builder.SetRenderAttachment(frameData.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(frameData.activeDepthTexture);

            builder.UseRendererList(rendererList);

            passData.rl = rendererList;
            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                ctx.cmd.DrawRendererList(data.rl);
            });
        }
    }
} 