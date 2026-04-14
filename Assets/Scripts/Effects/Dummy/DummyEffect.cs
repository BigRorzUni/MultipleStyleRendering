using UnityEngine.Rendering.Universal;
using UnityEngine;
public sealed class DummyEffect : Effect
{
    public override string Name { get; }
    public override ScriptableRenderPassInput RequiredInputs => ScriptableRenderPassInput.Color;
    int _requiredIndex;

    public DummyEffect(string name, Shader shader, int index)
    {
        Name = name;
        _requiredIndex = index;
        _passes.Add(new DummyPass(shader, name, _requiredIndex));
    }
}