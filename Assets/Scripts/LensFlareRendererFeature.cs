using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LensFlareRendererFeature : ScriptableRendererFeature
{
    class LensFlarePass : ScriptableRenderPass
    {
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Debug.Log(message: "The Execute() method runs.");
        }
    }

    private LensFlarePass _lensFlarePass;

    public override void Create()
    {
        _lensFlarePass = new LensFlarePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_lensFlarePass);
    }
}