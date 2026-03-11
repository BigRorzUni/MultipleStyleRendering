using System.Collections.Generic;
using UnityEngine;

public static class BBoxDebugStore
{
    public struct DebugRect
    {
        public RectInt rect;
        public Color color;
        public string label;
    }

    static readonly List<DebugRect> _rects = new();

    public static void Clear()
    {
        _rects.Clear();
    }

    public static void Add(RectInt rect, Color color, string label = "")
    {
        _rects.Add(new DebugRect
        {
            rect = rect,
            color = color,
            label = label
        });
    }

    public static IReadOnlyList<DebugRect> Rects => _rects;
}