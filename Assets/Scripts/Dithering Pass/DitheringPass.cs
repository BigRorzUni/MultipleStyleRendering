using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class DitheringPass : ScriptableRenderPass//, INprPass
{
    Material _mat;

    static readonly int RectId = Shader.PropertyToID("_Rect");
    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int ScreenTexelSizeId = Shader.PropertyToID("_ScreenTexelSize");


    // public void ApplySettings(NprSettings settings)
    // {

    // }

    class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;
        public Vector2 screenTexelSize;
    }

    public DitheringPass(Shader shader)
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

        if(nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        var camDesc = cameraData.cameraTargetDescriptor;
        Vector2 screenTexelSize = new Vector2(1f / camDesc.width, 1f / camDesc.height);

        foreach(var bbox in nprFrameData.bboxes)
        {
            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;
            
            if((bbox.styles & StyleBits.ImageSpaceEffect.Dithering) == 0)
                continue;

            if(!bbox.currentTex.IsValid())
                continue;

            TextureHandle outTex = renderGraph.CreateTexture(bbox.desc);
            using (var builder = renderGraph.AddRasterRenderPass($"BBox Dither ({bbox.box})", out PassData passData))
            {
                passData.src = bbox.currentTex;
                passData.ids = nprFrameData.idTexture;
                passData.mat = Object.Instantiate(_mat);
                passData.rect = bbox.box;
                passData.screenTexelSize = screenTexelSize;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);

                builder.SetRenderAttachment(outTex, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetVector(RectId, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                    data.mat.SetVector(ScreenTexelSizeId, data.screenTexelSize);

                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                });
            }

            bbox.currentTex = outTex;
        }

#region OLD METHOD
        // blit frame into a copy for sampling in dithering pass
        // using (var builder = renderGraph.AddRasterRenderPass("NPR Dither Copy Pass", out PassData copyData))
        // {
        //     builder.SetRenderAttachment(srcCopy, 0, AccessFlags.Write);
        //     builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

        //     copyData.src = frameData.activeColorTexture;

        //     builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
        //     {
        //         Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), 0, false);
        //     });
        // }

        // dithering pass
        // using (var builder = renderGraph.AddRasterRenderPass("Dithering Composite Pass", out PassData passData))
        // {
        //     // write to screen colour
        //     builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

        //     // read from id and screen textures
        //     builder.UseTexture(srcCopy, AccessFlags.Read);
        //     builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);

        //     passData.src = srcCopy;
        //     passData.ids = nprFrameData.idTexture;
        //     passData.mat = _mat;

        //     builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
        //     {
        //         data.mat.SetTexture(SourceTexID, data.src);
        //         data.mat.SetTexture(IdTexId, data.ids);

        //         CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
        //     });
        // }
#endregion
    }
}