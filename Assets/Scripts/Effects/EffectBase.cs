using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using System;


public abstract class Effect 
{
    public abstract string Name { get; }
    public virtual StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.None;

    protected readonly List<ScriptableRenderPass> _passes = new();
    public IReadOnlyList<ScriptableRenderPass> Passes => _passes; // exposes list for reading
}

public static class StyleBits
{
    [Flags]
    public enum ImageSpaceEffect : uint
    {
        None = 0,
        Outline = 1u << 0,
        Dithering = 1u << 1,
    }

    public const uint DefaultBit = 1u << 0;
    public const uint ImageSpaceBit = 1u << 8;
}