using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine;


public enum NprRenderMode
{
    Fullscreen,
    CPU,
    GPU
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

    // CPU PATH
    public List<BoundingBox> bboxes;
    public List<BoundingBox> occlusionCandidateBoxes; 

    public int bboxCount;
    public int bboxVisibilityCount;

    // public int bboxRectBufferCapacity;
    // public int bboxMaskBufferCapacity;
    // public int bboxVisibilityBufferCapacity;

    public ComputeBuffer bboxVisibilityBuffer;
    public ComputeBuffer bboxRectBuffer;
    public ComputeBuffer bboxMaskBuffer;
    public ComputeBuffer bboxCountBuffer;
    public ComputeBuffer bboxIndirectArgsBuffer;

    public StyleBits.ImageSpaceEffect presentImageBits;

    public uint presentTestStyles;  

    public static void EnsureBufferCapacity(ref ComputeBuffer buffer, ref int capacity, int count, int stride,     ComputeBufferType type = ComputeBufferType.Default)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (buffer == null || capacity < requiredCapacity)
        {
            if (buffer != null)
                buffer.Release();

            capacity = requiredCapacity;
            buffer = new ComputeBuffer(capacity, stride, type);
        }
    }

    public static void EnsureFixedBuffer(ref ComputeBuffer buffer, int count, int stride, ComputeBufferType type = ComputeBufferType.Default)
    {
        if (buffer == null)
            buffer = new ComputeBuffer(count, stride, type);
    }


    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
        normalsTexture = TextureHandle.nullHandle;
        sourceTexture = TextureHandle.nullHandle;

        if (bboxes != null)
            bboxes.Clear();

        if (occlusionCandidateBoxes != null)
            occlusionCandidateBoxes.Clear();

        bboxRectBuffer = null;
        bboxMaskBuffer = null;
        bboxVisibilityBuffer = null;

        bboxCountBuffer = null;
        bboxIndirectArgsBuffer = null;

        bboxCount = 0;
        bboxVisibilityCount = 0;

        presentImageBits = 0;
        presentTestStyles = 0;
    }
}