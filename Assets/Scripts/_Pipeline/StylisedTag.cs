using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteAlways]
public class StylisedTag : MonoBehaviour
{
    // Map these to Rendering Layer bits (0..31).
    [Flags]
    public enum StylisedEffect : uint
    {
        None = 0,
        ToonShading       = 1u << 1,
        ScreenSpaceOutline = 1u << 2,
        Hatching          = 1u << 3,
    }

    [Tooltip("Styles to apply to this object.")]
    public StylisedEffect effects = StylisedEffect.None;

    Renderer[] _renderers;

    // All bits this component controls (update if you add effects)
    const uint ControlledBits =
        (uint)(StylisedEffect.ScreenSpaceOutline |
               StylisedEffect.ToonShading |
               StylisedEffect.Hatching);

    const uint DefaultLayerBit = 1u << 0;

    void OnEnable()
    {
        Ensure();
        Apply();
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
    }

    void OnValidate()
    {
        Ensure();
        Apply();
    }

    void OnTransformChildrenChanged()
    {
        Ensure(forceRefreshRenderers: true);
        Apply();
    }

    void Ensure(bool forceRefreshRenderers = false)
    {
        if (forceRefreshRenderers || _renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
    }

    void Apply()
    {
        if (_renderers == null) return;

        uint desired = (uint)effects;

        if(desired == uint.MaxValue)
            desired = ControlledBits;

        desired &= ControlledBits;

        foreach (var r in _renderers)
        {
            if (!r) 
                continue;

            uint current = r.renderingLayerMask;
            uint next;
            
            if (desired == 0)
            {
                // None => Default only
                next = DefaultLayerBit;
            }
            else
            {
                // Any style bits => style bits only (no Default, no other bits)
                next = desired;
            }

            if (current != next)
                r.renderingLayerMask = next;
        }
    }

#if UNITY_EDITOR
    static void OnPlayModeStateChanged(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredEditMode)
        {
            foreach (var tag in FindObjectsByType<StylisedTag>(FindObjectsSortMode.None))
            {
                tag.Ensure(forceRefreshRenderers: true);
                tag.Apply();
            }
        }
    }
#endif
}