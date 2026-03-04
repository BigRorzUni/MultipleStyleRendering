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

    [Header("Test Effects")]
    private List<int> testIndices = new();

    public uint testEffects;

    Renderer[] _renderers;

    const uint DefaultBit = 1u << 0;

    const uint ControlledBits =
        DefaultBit |
        (uint)StylisedEffect.Toon |
        (uint)StylisedEffect.Outline |
        (uint)StylisedEffect.Dithering |
        (uint)StylisedEffect.Pixelisation;

<<<<<<< Updated upstream
    void OnEnable() 
    { 
        Ensure(); 
        Apply(); 
        #if UNITY_EDITOR
        Hook(); 
        #endif
=======
    TestRunner _testRunner;

void Awake()
{
    _testRunner = FindAnyObjectByType<TestRunner>();

    if (_testRunner == null)
        Debug.LogWarning("StylisedTag: no TestRunner found in scene");
}

    void OnEnable()
    {
        Ensure();
        Apply();
#if UNITY_EDITOR
        Hook();
#endif

>>>>>>> Stashed changes
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

    public void Apply()
    {
<<<<<<< Updated upstream
=======
        if (!isActiveAndEnabled)
            return;
            
        if(_testRunner == null)
        {
            _testRunner = FindAnyObjectByType<TestRunner>();

            if (_testRunner == null)
            {
                Debug.LogWarning("StylisedTag: no TestRunner found in scene");
                return;
            }

        }
        Debug.Log(_testRunner.setRendererTestmode);
        if(_testRunner.setRendererTestmode)
        {
            Debug.Log("apply test effects");
            ApplyTestEffects();
        }
        else
        {
            ApplyObjectSpace();
            ApplyImageSpace();
        }
    }

    // object space effects - render layers
    void ApplyObjectSpace()
    {
>>>>>>> Stashed changes
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

<<<<<<< Updated upstream
=======
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
            Apply();
        }
    }


    public void ClearTestEffects()
    {
        testIndices?.Clear();
        Apply();
    }



    static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform t in obj.transform)
            SetLayerRecursive(t.gameObject, layer);
    }

>>>>>>> Stashed changes
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