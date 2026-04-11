using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine;
using System.Runtime.InteropServices;


public static class OcclusionData
{
    public static List<BoundingBox> bboxes;
    public static List<BoundingBox> occlusionCandidateBoxes;
}


public class BoundingBox
{
    public StyleBits.ImageSpaceEffect styles;
    public RectInt box;

    public uint testMask; // for testing
    public List<Renderer> renderers = new List<Renderer>();

    public BoundingBox(uint s, RectInt b)
    {
        styles = (StyleBits.ImageSpaceEffect)s;
        box = b;
        testMask = 0;
    }

    public static BoundingBox CreateTestBox(uint testMask, RectInt rect)
    {
        BoundingBox box = new BoundingBox(0, rect)
        {
            testMask = testMask
        };
        
        return box;
    }
}

public sealed class NprFrameData : ContextItem
{
    public TextureHandle idTexture;
    public TextureHandle normalsTexture;
    public TextureHandle sourceTexture;

    public List<BoundingBox> bboxes;
    public List<BoundingBox> occlusionCandidateBoxes; // bboxes that passed occlusion culling and need to be drawn in id prepass

    public int bboxCount;
    public ComputeBuffer bboxVisibilityBuffer;
    public ComputeBuffer bboxRectBuffer;
    public ComputeBuffer bboxMaskBuffer;
    public int bboxVisibilityCount;

    public StyleBits.ImageSpaceEffect presentImageBits;

    public uint presentTestStyles;  


    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
        normalsTexture = TextureHandle.nullHandle;
        sourceTexture = TextureHandle.nullHandle;

        if (bboxes != null)
            bboxes.Clear();

        if (occlusionCandidateBoxes != null)
            occlusionCandidateBoxes.Clear();

        bboxVisibilityBuffer = null;
        bboxRectBuffer = null;
        bboxMaskBuffer = null;
        bboxVisibilityCount = 0;

        presentImageBits = 0;
        presentTestStyles = 0;
    }
}