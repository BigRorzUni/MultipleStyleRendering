using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteAlways]
public class StylisedTag : MonoBehaviour
{


    [Header("Object Space")]
    public StyleBits.ObjectSpaceEffect objectEffects = StyleBits.ObjectSpaceEffect.None;

    [Header("Image Space")]
    public StyleBits.ImageSpaceEffect imageEffects = StyleBits.ImageSpaceEffect.None;

    Renderer[] _renderers;

    // make sure that no other render layers are interacted with
    const uint ObjectControlledBits =
        StyleBits.DefaultBit |
        (uint)StyleBits.ObjectSpaceEffect.Toon;

    static readonly int ImageStyleId = Shader.PropertyToID("_ImageStyleID");


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
        Ensure(true);
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
        ApplyObjectSpace();
        ApplyImageSpace();
    }

    // object space effects - render layers
    void ApplyObjectSpace()
    {
        if (_renderers == null) return;

        uint desired = StyleBits.DefaultBit | (uint)objectEffects;
        desired &= ObjectControlledBits;

        foreach (var r in _renderers)
        {
            if (!r) continue;

            uint keep = r.renderingLayerMask & ~ObjectControlledBits;
            uint next = keep | desired;

            if (r.renderingLayerMask != next)
                r.renderingLayerMask = next;
        }
    }

    // image space effects - mpbs
    void ApplyImageSpace()
    {
        if (_renderers == null) return;

        foreach (var r in _renderers)
        {
            if (!r) continue;

            uint mask = r.renderingLayerMask;

            if (imageEffects != StyleBits.ImageSpaceEffect.None)
                mask |= StyleBits.ImageSpaceBit;
            else
                mask &= ~StyleBits.ImageSpaceBit;

            r.renderingLayerMask = mask;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            if (imageEffects != StyleBits.ImageSpaceEffect.None)
            {
                mpb.SetInt(ImageStyleId, (int)imageEffects);
            }
            else
            {
                mpb.SetInt(ImageStyleId, 0);
            }

            r.SetPropertyBlock(mpb);
        }
    }

    static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform t in obj.transform)
            SetLayerRecursive(t.gameObject, layer);
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