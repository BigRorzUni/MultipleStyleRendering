using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed class GreyscaleEffect : Effect
{
    public override string Name => "Greyscale";

    public override ScriptableRenderPassInput RequiredInputs => ScriptableRenderPassInput.Color;

    public override StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.Greyscale;

    public GreyscaleEffect(Shader shader)
    {
        _passes.Add(new GreyscalePass(shader, RequiredImageBits));
    }
}