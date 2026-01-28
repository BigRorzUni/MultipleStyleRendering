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
        None          = 0,
        ConvexOutline = 1u << 0,
        ToonShading   = 1u << 1,
        Hatching      = 1u << 2,
    }

    public StylisedEffect effects = StylisedEffect.None;

    static readonly int StylisedMaskID = Shader.PropertyToID("_StylisedMask");

    Renderer[] _renderers;
    MaterialPropertyBlock _mpb;

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
        if (_mpb == null) 
            _mpb = new MaterialPropertyBlock();

        if (forceRefreshRenderers || _renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
    }

    void Apply()
    {
        if (_renderers == null) return;

        float mask = (uint)effects;

        foreach (var r in _renderers)
        {
            if (!r) 
                continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(StylisedMaskID, mask);
            r.SetPropertyBlock(_mpb);
        }
    }

#if UNITY_EDITOR
    static void OnPlayModeStateChanged(PlayModeStateChange s)
    {
        // When leaving play mode, MPBs get reset; re-apply them in edit mode.
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