using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class NormalsPrepass : ScriptableRenderPass, INprPass
{
    readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    readonly FilteringSettings _filtering;
    readonly Shader _normalsShader;

    public bool debugToScreen;
    public void ApplySettings(Settings settings)
    {
        debugToScreen = settings.debugView == NprDebugView.Normals;
    }

    class PassData 
    { 
        public RendererListHandle rl; 
    }
    class DebugData 
    { 
        public TextureHandle normals; 
    }

    public NormalsPrepass(Shader normalsShader)
    {
        _normalsShader = normalsShader;
        _filtering = new FilteringSettings(RenderQueueRange.opaque);
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_normalsShader == null) return;

        // get data from URP
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

        // match normal texture to camera resolution + settings
        RenderTextureDescriptor normalsTexDescriptor = cameraData.cameraTargetDescriptor;

        // tweak format to fit what a normal texture needs
        normalsTexDescriptor.depthBufferBits = 0;
        normalsTexDescriptor.msaaSamples = 1;
        normalsTexDescriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
        normalsTexDescriptor.sRGB = false;

        // allocate normal texture
        TextureHandle normalsTex = renderGraph.CreateTexture(new TextureDesc(normalsTexDescriptor.width, normalsTexDescriptor.height)
        {
            name = "_NprNormalsTexture",
            colorFormat = normalsTexDescriptor.graphicsFormat,
            clearBuffer = true,
            clearColor = Color.black,
            filterMode = FilterMode.Point
        });
        nprFrameData.normalsTexture = normalsTex;

        // draw objects with normal shader
        DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);

        drawing.overrideShader = _normalsShader;
        drawing.overrideShaderPassIndex = 0;
        drawing.perObjectData = PerObjectData.None;

        RendererListParams rlp = new RendererListParams(renderingData.cullResults, drawing, _filtering);
        RendererListHandle rendererList = renderGraph.CreateRendererList(rlp);

        // draw to normal tex
        using (var builder = renderGraph.AddRasterRenderPass("NPR Normals Prepass", out PassData passData))
        {
            builder.SetRenderAttachment(normalsTex, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(frameData.activeDepthTexture, AccessFlags.ReadWrite);

            passData.rl = rendererList;
            builder.UseRendererList(passData.rl);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                ctx.cmd.DrawRendererList(data.rl);
            });
        }

        // debug view (blit normals to camera colour)
        if (debugToScreen)
        {
            using var builder = renderGraph.AddRasterRenderPass("NPR Normals Debug", out DebugData dbg);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.UseTexture(normalsTex, AccessFlags.Read);

            dbg.normals = normalsTex;

            builder.SetRenderFunc(static (DebugData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.normals, new Vector4(1,1,0,0), 0, false);
            });
        }
    }
}