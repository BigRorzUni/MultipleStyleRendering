using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine;

public class BoundingBox
{
    public StyleBits.ImageSpaceEffect styles;
    public RectInt box;
    // public TextureDesc desc;
    // public TextureHandle currentTex;

    public BoundingBox(uint s, RectInt b)
    {
        styles = (StyleBits.ImageSpaceEffect)s;
        box = b;
        // currentTex = TextureHandle.nullHandle;
    }
}

public sealed class NprFrameData : ContextItem
{
    public TextureHandle idTexture;
    public TextureHandle normalsTexture;
    // public TextureHandle edgesTexture;
    public TextureHandle sourceTexture;
    // public TextureHandle currentColour;
    public List<BoundingBox> bboxes;

    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
        normalsTexture = TextureHandle.nullHandle;
        // edgesTexture = TextureHandle.nullHandle;
        sourceTexture = TextureHandle.nullHandle;
        // currentColour = TextureHandle.nullHandle;
        if(bboxes != null)
            bboxes.Clear();
    }
}