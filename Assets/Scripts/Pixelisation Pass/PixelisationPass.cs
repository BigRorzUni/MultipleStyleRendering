using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class PixelisationPass : ScriptableRenderPass//, INprPass
{
    Material _mat;

    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int DepthTexId = Shader.PropertyToID("_NprDepthTexture");

    class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public TextureHandle depth;
        public Material mat;
    }

    public PixelisationPass(Shader shader)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
            if (frameContext.Contains<NprFrameData>())
                nprFrameData = frameContext.Get<NprFrameData>();
            else
                nprFrameData = frameContext.Create<NprFrameData>();

        if (!nprFrameData.idTexture.IsValid())
            return;
        if (!frameData.activeDepthTexture.IsValid()) 
            return;

        // copy frame into a texture
        RenderTextureDescriptor srcDesc = cameraData.cameraTargetDescriptor;
        srcDesc.depthBufferBits = 0;
        srcDesc.msaaSamples = 1;
        srcDesc.sRGB = false;

        TextureHandle srcCopy = renderGraph.CreateTexture(new TextureDesc(srcDesc)
        {
            name = "_NprPixelisationSourceCopy",
            colorFormat = srcDesc.graphicsFormat,
            clearBuffer = false,
            filterMode = FilterMode.Point
        });

        // blit frame into a copy for sampling in dithering pass
        using (var builder = renderGraph.AddRasterRenderPass("NPR Pixelisation Copy Pass", out PassData copyData))
        {
            builder.SetRenderAttachment(srcCopy, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyData.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), 0, false);
            });
        }

        // pixelisation pass
        using (var builder = renderGraph.AddRasterRenderPass("Pixelisation Composite Pass", out PassData passData))
        {
            // write to screen colour
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            // read from id, depth and screen textures
            builder.UseTexture(srcCopy, AccessFlags.Read);
            builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);
            builder.UseTexture(frameData.activeDepthTexture, AccessFlags.Read);

            passData.src = srcCopy;
            passData.ids = nprFrameData.idTexture;
            passData.depth = frameData.activeDepthTexture;
            passData.mat = _mat;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexID, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetTexture(DepthTexId, data.depth);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }
    }
}