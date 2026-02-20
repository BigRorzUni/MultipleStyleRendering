using System;

public static class StyleBits
{
    [Flags]
    public enum ObjectSpaceEffect : uint
    {
        None = 0,
        Toon = 1u << 1,
    }

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