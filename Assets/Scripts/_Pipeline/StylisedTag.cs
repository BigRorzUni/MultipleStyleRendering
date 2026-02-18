using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteAlways]
public class StylisedTag : MonoBehaviour
{
    [Flags]
    public enum StylisedEffect : uint
    {
        None = 0,
        Toon = 1u << 1, 
        Outline = 1u << 2, 
        Dithering = 1u << 3, 
        Pixelisation = 1u << 4
    }

    public StylisedEffect effects = StylisedEffect.None;

    Renderer[] _renderers;

    const uint DefaultBit = 1u << 0;

    const uint ControlledBits =
        DefaultBit |
        (uint)StylisedEffect.Toon |
        (uint)StylisedEffect.Outline |
        (uint)StylisedEffect.Dithering |
        (uint)StylisedEffect.Pixelisation;

    void OnEnable() 
    { 
        Ensure(); 
        Apply(); 
        #if UNITY_EDITOR
        Hook(); 
        #endif
    }
    void OnDisable() 
    { 
        #if UNITY_EDITOR
        Unhook(); 
        #endif
    }
    void OnValidate() 
    { 
        Ensure(); 
        Apply(); 
    }
    void OnTransformChildrenChanged() 
    { 
        Ensure(true); 
        Apply(); 
    }

    void Ensure(bool force = false)
    {
        if (force || _renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
    }

    void Apply()
    {
        if (_renderers == null) return;

        uint desired = DefaultBit | ((uint)effects);

        // prevent 'everything' option from including non-stylisation bits
        desired &= ControlledBits;

        foreach (var r in _renderers)
        {
            if (!r) continue;

            // preserves unrelated layers
            uint keep = r.renderingLayerMask & ~ControlledBits;
            uint next = keep | desired;

            if (r.renderingLayerMask != next)
                r.renderingLayerMask = next;
        }
    }

#if UNITY_EDITOR
    void Hook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    void Unhook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredEditMode)
        {
            foreach (var tag in FindObjectsByType<StylisedTag>(FindObjectsSortMode.None))
            {
                tag.Ensure(true);
                tag.Apply();
            }
        }
    }
#endif
}