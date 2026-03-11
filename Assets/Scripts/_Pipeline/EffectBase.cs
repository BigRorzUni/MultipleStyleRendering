using System.Collections.Generic;
using UnityEngine.Rendering.Universal;

public interface IEffect
{
    string Name { get; }

    StyleBits.ImageSpaceEffect RequiredImageBits { get; }
    StyleBits.ObjectSpaceEffect RequiredObjectBits { get; }


    IReadOnlyList<ScriptableRenderPass> Passes { get; }
}

public abstract class Effect : IEffect
{
    public abstract string Name { get; }
    public virtual StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.None;
    public virtual StyleBits.ObjectSpaceEffect RequiredObjectBits => StyleBits.ObjectSpaceEffect.None;

    protected readonly List<ScriptableRenderPass> _passes = new();
    public IReadOnlyList<ScriptableRenderPass> Passes => _passes;
}