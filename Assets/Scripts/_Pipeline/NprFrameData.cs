using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine;

public class BoundingBox
{
    public StyleBits.ImageSpaceEffect styles;
    public RectInt box;

    public uint testMask; // for testing

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


    public StyleBits.ImageSpaceEffect presentImageBits;

    public uint presentTestStyles;  

    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
        normalsTexture = TextureHandle.nullHandle;
        sourceTexture = TextureHandle.nullHandle;

        if(bboxes != null)
            bboxes.Clear();

        presentImageBits = 0;
        presentTestStyles = 0;


    }
}