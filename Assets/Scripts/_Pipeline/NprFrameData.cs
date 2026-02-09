using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public sealed class NprFrameData : ContextItem
{
    public TextureHandle idTexture;
    public TextureHandle normalsTexture;
    //public TextureHandle edgesTexture;
    public TextureHandle sourceTexture;
    public TextureHandle currentColour;

    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
        normalsTexture = TextureHandle.nullHandle;
        //edgesTexture = TextureHandle.nullHandle;
        sourceTexture = TextureHandle.nullHandle;
        currentColour = TextureHandle.nullHandle;
    }
}