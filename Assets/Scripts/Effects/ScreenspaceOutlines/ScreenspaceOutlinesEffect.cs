using UnityEngine;
using UnityEngine.Rendering.Universal;
public sealed class ScreenspaceOutlinesEffect : Effect
{
    public override string Name => "Screenspace Outlines";
    public override StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.Outline;
    public override ScriptableRenderPassInput RequiredInputs => ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;

    public ScreenspaceOutlinesEffect(Shader shader)
    {
        _passes.Add(new ScreenspaceOutlinesPass(shader, RequiredImageBits));
    }

}