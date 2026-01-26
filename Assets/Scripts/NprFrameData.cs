using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public sealed class NprFrameData : ContextItem
{
    public TextureHandle idTexture;

    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
    }
}