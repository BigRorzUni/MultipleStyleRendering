using System.Collections.Generic;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine;
using Unity.VisualScripting;

public class BoundingBox
{
    public int objectId;
    public List<ScriptableRenderPass> passes;
    public RectInt box;
<<<<<<< Updated upstream
=======
    public uint testMask; // for testing

    public BoundingBox(uint s, RectInt b)
    {
        styles = (StyleBits.ImageSpaceEffect)s;
        box = b;
        testMask = 0;
    }
>>>>>>> Stashed changes
}

public sealed class NprFrameData : ContextItem
{
    public TextureHandle idTexture;
    public TextureHandle normalsTexture;
    //public TextureHandle edgesTexture;
    public TextureHandle sourceTexture;
    public TextureHandle currentColour;
    public List<BoundingBox> bboxes;

<<<<<<< Updated upstream
=======
    public StyleBits.ImageSpaceEffect presentImageBits;

    public int testStyleCount;
    public uint presentTestStyles;  

>>>>>>> Stashed changes
    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
        normalsTexture = TextureHandle.nullHandle;
        //edgesTexture = TextureHandle.nullHandle;
        sourceTexture = TextureHandle.nullHandle;
        currentColour = TextureHandle.nullHandle;
        if(bboxes != null)
            bboxes.Clear();
<<<<<<< Updated upstream
=======

        presentImageBits = 0;

        testStyleCount = 0;
        presentTestStyles = 0;

>>>>>>> Stashed changes
    }
}