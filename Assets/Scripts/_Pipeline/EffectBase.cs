using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using System;

public interface IEffect
{
    string Name { get; }

    StyleBits.ImageSpaceEffect RequiredImageBits { get; }

    IReadOnlyList<ScriptableRenderPass> Passes { get; }
}

public abstract class Effect : IEffect
{
    public abstract string Name { get; }
    public virtual StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.None;

    protected readonly List<ScriptableRenderPass> _passes = new();
    public IReadOnlyList<ScriptableRenderPass> Passes => _passes;

    // public bool needsNormalPass;
}

public static class StyleBits
{
    [Flags]
    public enum ImageSpaceEffect : uint
    {
        None = 0,
        Outline = 1u << 0,
        Dithering = 1u << 1,
        Pixelisation = 1u << 2,
    }

    public const uint DefaultBit = 1u << 0;
    public const uint ImageSpaceBit = 1u << 8;
}