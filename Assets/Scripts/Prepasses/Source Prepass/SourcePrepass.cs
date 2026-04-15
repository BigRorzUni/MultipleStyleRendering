using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class SourcePrepass : Prepass
{
    private class CopyPassData
    {
        public TextureHandle src;
    }

    public SourcePrepass() : base("SourcePrepass")
    {
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();

        // get/create NPR frame data
        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        // initialise source tex
        if (!nprFrameData.sourceTexture.IsValid())
        {
            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
            camDesc.depthBufferBits = 0;
            camDesc.msaaSamples = 1;

            nprFrameData.sourceTexture = renderGraph.CreateTexture(new TextureDesc(camDesc.width, camDesc.height)
            {
                name = "_NprSourceCopy",
                colorFormat = camDesc.graphicsFormat,
                clearBuffer = false,
                filterMode = FilterMode.Point
            });
        }

        // SOURCE COPY
        using (var builder = renderGraph.AddRasterRenderPass("Source Copy", out CopyPassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }
    }

    public override void Dispose()
    {
    }
}