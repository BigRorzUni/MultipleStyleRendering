using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine;

public class BoundingBox
{
    public uint styles;
    public RectInt box;
}

public sealed class NprFrameData : ContextItem
{
    public TextureHandle idTexture;
    public TextureHandle normalsTexture;
    //public TextureHandle edgesTexture;
    public TextureHandle sourceTexture;
    public TextureHandle currentColour;
    public List<BoundingBox> bboxes;

    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
        normalsTexture = TextureHandle.nullHandle;
        //edgesTexture = TextureHandle.nullHandle;
        sourceTexture = TextureHandle.nullHandle;
        currentColour = TextureHandle.nullHandle;
        if(bboxes != null)
            bboxes.Clear();
    }
}