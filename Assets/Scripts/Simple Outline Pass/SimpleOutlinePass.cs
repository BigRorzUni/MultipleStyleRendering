using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class SimpleOutlinePass : ScriptableRenderPass, INprPass
{
    readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    readonly FilteringSettings _filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
    readonly Shader _outlineShader;

    public Color outlineColor;
    public float outlineThickness;

    public void ApplySettings(NprSettings settings)
    {
        outlineColor = settings.outlineColor;
        outlineThickness = settings.outlineThickness;
    }

    static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineThicknessID = Shader.PropertyToID("_OutlineThickness");

    class PassData { public RendererListHandle rl; public Color col; public float thk; }

    public SimpleOutlinePass(Shader outlineShader)
    {
        _outlineShader = outlineShader;
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void RecordRenderGraph(RenderGraph rg, ContextContainer fd)
    {
        if (_outlineShader == null) return;

        var res  = fd.Get<UniversalResourceData>();
        var cam  = fd.Get<UniversalCameraData>();
        var rnd  = fd.Get<UniversalRenderingData>();
        var lgt  = fd.Get<UniversalLightData>();

        var drawing = RenderingUtils.CreateDrawingSettings(_shaderTagId, rnd, cam, lgt, SortingCriteria.CommonOpaque);

        drawing.overrideShader = _outlineShader;
        drawing.overrideShaderPassIndex = 0;

        var rlp = new RendererListParams(rnd.cullResults, drawing, _filteringSettings);
        var rl  = rg.CreateRendererList(rlp);

        using var b = rg.AddRasterRenderPass<PassData>("Simple Outline", out var pd);

        b.SetRenderAttachment(res.activeColorTexture, 0);
        b.SetRenderAttachmentDepth(res.activeDepthTexture);

        b.UseRendererList(rl);

        // We set globals -> allow it
        b.AllowGlobalStateModification(true);

        pd.rl = rl;
        pd.col = outlineColor;
        pd.thk = outlineThickness;

        b.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
        {
            ctx.cmd.SetGlobalColor(OutlineColorID, data.col);
            ctx.cmd.SetGlobalFloat(OutlineThicknessID, data.thk);
            ctx.cmd.DrawRendererList(data.rl);
        });
    }
}