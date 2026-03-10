using UnityEngine;
using System;
using System.Collections.Generic;



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

    [Header("Test Effects")]
    private List<int> testIndices = new();

    public uint testEffects;

    Renderer[] _renderers;

    // make sure that no other render layers are interacted with
    const uint ObjectControlledBits =
        StyleBits.DefaultBit |
        (uint)StyleBits.ObjectSpaceEffect.Toon;

    static readonly int ImageStyleId = Shader.PropertyToID("_ImageStyleID");
    
    void OnEnable()
    {
        Ensure();
        if (!NprTestingConfig.TestMode)
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
        if (!NprTestingConfig.TestMode)
            Apply();
    }

    void OnTransformChildrenChanged()
    {
        Ensure(true);
        if (!NprTestingConfig.TestMode)
            Apply();
    }

    void Ensure(bool force = false)
    {
        if (force || _renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
    }

    public void Apply()
    {
        if (!isActiveAndEnabled)
            return;
            
        if(NprTestingConfig.TestMode)
        {
            Debug.Log("apply test effects");
            ApplyTestEffects();
        }
        else
        {
            ApplyObjectSpace();
            ApplyImageSpace();
        }

        Debug.Log("Effects applied");
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

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            
            if (imageEffects != StyleBits.ImageSpaceEffect.None)
            {
                mpb.SetInteger(ImageStyleId, (int)imageEffects);
            }
            else
            {
                mpb.SetInteger(ImageStyleId, 0);
            }

            r.SetPropertyBlock(mpb);
        }
    }

    void ApplyTestEffects()
    {
        testEffects = 0u;

        if (testIndices == null || testIndices.Count == 0)
        {   
            Debug.Log("Test effect list is empty");
            return;
        }
        for (int i = 0; i < testIndices.Count; i++)
        {
            int idx = testIndices[i];
            if ((uint)idx >= 32u)
                continue;

            testEffects |= 1u << idx;
        }

        Debug.Log($"Test mask (bin): {Convert.ToString(testEffects, 2).PadLeft(32, '0')}");

        if (_renderers == null) return;

        foreach (var r in _renderers)
        {
            if (!r) continue;

            r.renderingLayerMask = StyleBits.DefaultBit | StyleBits.ImageSpaceBit;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            mpb.SetInteger(ImageStyleId, (int)testEffects);

            r.SetPropertyBlock(mpb);
        }
    }


    public void AddTestEffect(int N)
    {
        if((uint)N >= 32u)
            return;

        if(!testIndices.Contains(N))
        {
            Debug.Log("Added style");
            testIndices.Add(N);
        }
    }


    public void ClearTestEffects()
    {
        testIndices?.Clear();
        // Apply();
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