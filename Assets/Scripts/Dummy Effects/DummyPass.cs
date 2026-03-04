using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class DummyPass : ScriptableRenderPass
{
    private Material _mat;
    private readonly uint _requiredBit;
    private readonly string _name;

    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");

    private class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;
    }

    public DummyPass(Shader shader, string name, int requiredIndex)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _requiredBit = 1u << requiredIndex;
        _name = name;
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
        if (!nprFrameData.sourceTexture.IsValid())   
            return;
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0) 
            return;
        
        if ((nprFrameData.presentTestStyles & _requiredBit) == 0)
            return;

        using (var builder = renderGraph.AddRasterRenderPass($"{_name} Source Copy", out PassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        foreach (var bbox in nprFrameData.bboxes)
        {
            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            if ((bbox.testMask & _requiredBit) == 0)
                continue;

            using (var builder = renderGraph.AddRasterRenderPass($"BBox {_name} ({bbox.box})", out PassData passData))
            {
                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;
                passData.rect = bbox.box;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);

                    ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                    ctx.cmd.DisableScissorRect();
                });
            }
        }
    }
}