using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class ScreenspaceOutlinesPass : ScriptableRenderPass, INprPass
{
    Material _mat;

    public Color outlineColour;
    
    static readonly int OutlineColourID  = Shader.PropertyToID("_OutlineColour");
    static readonly int EdgesTexID  = Shader.PropertyToID("_NprEdgesTexture");

    public void ApplySettings(NprSettings settings)
    {
        outlineColour = settings.outlineColour;
    }

    class PassData
    {
        public TextureHandle edges;
        public Material mat;
        public Color col;
    }

    public ScreenspaceOutlinesPass(Shader shader)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();

        NprFrameData nprFrameData;
            if (frameContext.Contains<NprFrameData>())
                nprFrameData = frameContext.Get<NprFrameData>();
            else
                nprFrameData = frameContext.Create<NprFrameData>();

        if (!nprFrameData.edgesTexture.IsValid())
            return;

        using (var builder = renderGraph.AddRasterRenderPass("Screenspace Outline Composite", out PassData passData))
        {
            // write to screen colour
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            // read from edge texture
            builder.UseTexture(nprFrameData.edgesTexture, AccessFlags.Read);

            passData.edges = nprFrameData.edgesTexture;
            passData.mat = _mat;
            passData.col = outlineColour;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetColor(OutlineColourID, data.col);
                data.mat.SetTexture(EdgesTexID, data.edges);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }
    }
}