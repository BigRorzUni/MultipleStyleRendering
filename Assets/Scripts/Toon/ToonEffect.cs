using UnityEngine;

public sealed class ToonEffect : Effect
{
    public override string Name => "Toon";
    public override StyleBits.ObjectSpaceEffect RequiredObjectBits => StyleBits.ObjectSpaceEffect.Toon;

    public ToonEffect(Shader shader)
    {
        _passes.Add(new ToonPass(shader, RequiredObjectBits));
    }
}