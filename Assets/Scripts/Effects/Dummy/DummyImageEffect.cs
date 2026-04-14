using UnityEngine;

public sealed class DummyImageEffect : Effect
{
    public override string Name { get; }
    int _requiredIndex;

    public DummyImageEffect(string name, Shader shader, int index)
    {
        Name = name;
        _requiredIndex = index;
        _passes.Add(new DummyPass(shader, name, _requiredIndex));
    }
}