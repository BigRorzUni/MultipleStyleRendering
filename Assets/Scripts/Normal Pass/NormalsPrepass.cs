using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class NormalsPrepass : ScriptableRenderPass
{
    readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    readonly FilteringSettings _filtering;
    readonly Shader _normalsShader;

    public bool debugToScreen = false;

    class PassData { public RendererListHandle rl; }
    class DebugData { public TextureHandle normals; }

    public NormalsPrepass(Shader normalsShader, LayerMask layerMask)
    {
        _normalsShader = normalsShader;
        _filtering = new FilteringSettings(RenderQueueRange.opaque, layerMask);
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_normalsShader == null) return;

        var resources     = frameData.Get<UniversalResourceData>();
        var cameraData    = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData     = frameData.Get<UniversalLightData>();

        // Allocate normals texture
        var desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.sRGB = false;

        // URP-like choice (good default)
        var fmt = SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormatUsage.Render)
            ? GraphicsFormat.R8G8B8A8_UNorm
            : GraphicsFormat.R16G16B16A16_SFloat;

        var normalsTex = renderGraph.CreateTexture(new TextureDesc(desc.width, desc.height)
        {
            name = "_NprNormalsTexture",
            colorFormat = fmt,
            clearBuffer = true,
            clearColor = Color.black,
            filterMode = FilterMode.Point
        });

        // Publish in NprFrameData, this has already been created in ID pass
        NprFrameData npr;
            if (frameData.Contains<NprFrameData>())
                npr = frameData.Get<NprFrameData>();
            else
                npr = frameData.Create<NprFrameData>();
        npr.normalsTexture = normalsTex;

        // Draw opaque objects with override shader
        var drawing = RenderingUtils.CreateDrawingSettings(
            _shaderTagId, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);

        drawing.overrideShader = _normalsShader;
        drawing.overrideShaderPassIndex = 0;
        drawing.perObjectData = PerObjectData.None;

        var rlp = new RendererListParams(renderingData.cullResults, drawing, _filtering);
        var rl  = renderGraph.CreateRendererList(rlp);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("NPR Normals Prepass", out var passData))
        {
            builder.SetRenderAttachment(normalsTex, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);

            passData.rl = rl;
            builder.UseRendererList(passData.rl);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                ctx.cmd.DrawRendererList(data.rl);
            });
        }

        // Debug view (blit normals to camera colour)
        if (debugToScreen)
        {
            using var builder = renderGraph.AddRasterRenderPass<DebugData>("NPR Normals Debug", out var dbg);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
            builder.UseTexture(normalsTex, AccessFlags.Read);

            dbg.normals = normalsTex;

            builder.SetRenderFunc(static (DebugData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.normals, new Vector4(1,1,0,0), 0, false);
            });
        }
    }
}