using UnityEngine;
using UnityEngine.Rendering.Universal;
public sealed class DitheringEffect : Effect
{
    public override string Name => "Dithering";
    public override ScriptableRenderPassInput RequiredInputs => ScriptableRenderPassInput.Color;
    public override StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.Dithering;

    public DitheringEffect(Shader shader)
    {
        _passes.Add(new DitheringPass(shader, RequiredImageBits));
    }
}