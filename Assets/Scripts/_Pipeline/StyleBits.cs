using System;

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