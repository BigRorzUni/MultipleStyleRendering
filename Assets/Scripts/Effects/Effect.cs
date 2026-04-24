using System.Collections.Generic;
using UnityEngine.Rendering.Universal;


public abstract class Effect 
{
    public abstract string Name { get; }
    public virtual StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.None;

    public virtual ScriptableRenderPassInput RequiredInputs => ScriptableRenderPassInput.None;

    protected readonly List<ScriptableRenderPass> _passes = new();
    public IReadOnlyList<ScriptableRenderPass> Passes => _passes; // exposes list for reading
}