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
    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int EdgesTexID  = Shader.PropertyToID("_NprEdgesTexture");

    public void ApplySettings(NprSettings settings)
    {
        outlineColour = settings.outlineColour;
    }

    class PassData
    {
        public TextureHandle src;
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

    public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
    {
        if (_mat == null) return;

        var res = frameData.Get<UniversalResourceData>();
        var cam = frameData.Get<UniversalCameraData>();

        NprFrameData npr;
            if (frameData.Contains<NprFrameData>())
                npr = frameData.Get<NprFrameData>();
            else
                npr = frameData.Create<NprFrameData>();

        if (!npr.edgesTexture.IsValid())
            return;

        var desc = cam.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.sRGB = false;

        var outTex = rg.CreateTexture(new TextureDesc(desc.width, desc.height)
        {
            name = "_NprOutlineComposite",
            colorFormat = desc.graphicsFormat,
            clearBuffer = false,
            filterMode = FilterMode.Bilinear,
            useMipMap = false
        });

        // read in camera colour
        using (var b = rg.AddRasterRenderPass<PassData>("Screenspace Outline Composite", out var pd))
        {
            b.SetRenderAttachment(outTex, 0, AccessFlags.Write);

            b.UseTexture(res.activeColorTexture, AccessFlags.Read);
            b.UseTexture(npr.edgesTexture, AccessFlags.Read);

            pd.src = res.activeColorTexture;
            pd.edges = npr.edgesTexture;
            pd.mat = _mat;
            pd.col = outlineColour;

            b.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetColor(OutlineColourID, data.col);
                data.mat.SetTexture(SourceTexID, data.src);
                data.mat.SetTexture(EdgesTexID, data.edges);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }

        // write outlined texture to camera
        using (var b = rg.AddRasterRenderPass<CopyData>("Screenspace Outline CopyBack", out var cd))
        {
            b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
            b.UseTexture(outTex, AccessFlags.Read);
            cd.src = outTex;

            b.SetRenderFunc(static (CopyData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        // Look into AA for this
    }

    class CopyData { public TextureHandle src; }
}