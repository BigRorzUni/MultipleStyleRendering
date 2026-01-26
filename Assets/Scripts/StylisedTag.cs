using UnityEngine;
using System;


[DisallowMultipleComponent]
public class StylisedTag : MonoBehaviour
{
    [Flags]
    public enum StylisedEffect : uint
    {
        None            = 0,
        ConvexOutline   = 1u << 0,
        ToonShading     = 1u << 1,
        Hatching        = 1u << 2,
    }

    public StylisedEffect effects = StylisedEffect.None;

    static readonly int StylisedMaskID = Shader.PropertyToID("_StylisedMask");

    Renderer[] _renderers;
    MaterialPropertyBlock _mpb;

    void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        _renderers = GetComponentsInChildren<Renderer>(true);
        Apply();
    }

    void OnEnable()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);

        Apply();
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        _renderers = GetComponentsInChildren<Renderer>(true);
        Apply();
    }

    void Apply()
    {
        uint mask = (uint)effects; 

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);

            // Use float for maximum shader compatibility (bitwise via uint() cast in HLSL).
            _mpb.SetFloat(StylisedMaskID, mask);

            r.SetPropertyBlock(_mpb);
        }
    }
}