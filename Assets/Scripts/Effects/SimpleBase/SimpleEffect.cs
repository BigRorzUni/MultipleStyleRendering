using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed class SimpleEffect : Effect
{
    readonly string _name;
    readonly StyleBits.ImageSpaceEffect _imageBits;

    public override string Name => _name;

    public override ScriptableRenderPassInput RequiredInputs => ScriptableRenderPassInput.Color;

    public override StyleBits.ImageSpaceEffect RequiredImageBits => _imageBits;

    public SimpleEffect(Shader shader, string name, StyleBits.ImageSpaceEffect imageBits)
    {
        _name = name;
        _imageBits = imageBits;

        _passes.Add(new StylePass(shader, _imageBits, Name));
    }
}