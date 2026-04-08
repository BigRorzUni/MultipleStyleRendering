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
    [Header("Image Effects")]
    public StyleBits.ImageSpaceEffect imageEffects = StyleBits.ImageSpaceEffect.None;

    [Header("Test Effects")]
    [SerializeField] private TestEffectAssignmentMode testAssignmentMode = TestEffectAssignmentMode.Inspector;

    [SerializeField] private List<bool> inspectorTestEffects = new();
    [NonSerialized] private List<bool> runtimeTestEffects = new();

    // mask currently being applied
    [SerializeField] public uint currentTestEffects;

    Renderer[] _renderers;

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

        if (runtimeTestEffects == null)
            runtimeTestEffects = new List<bool>();

        int count = Mathf.Max(inspectorTestEffects.Count, runtimeTestEffects.Count);
        ResizeBoolList(inspectorTestEffects, count);
        ResizeBoolList(runtimeTestEffects, count);
    }

    public void Apply()
    {
        if (!isActiveAndEnabled)
            return;
            
        if (NprTestingConfig.TestMode)
        {
            Debug.Log("apply test effects");
            ApplyTestEffects();
        }
        else
        {
            ApplyImageSpace();
        }

        Debug.Log("Effects applied");
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
        List<bool> activeEffects = GetActiveTestEffects();
        currentTestEffects = BuildMask(activeEffects);

        Debug.Log($"Test mask (bin): {Convert.ToString(currentTestEffects, 2).PadLeft(32, '0')}");

        if (_renderers == null) return;

        foreach (var r in _renderers)
        {
            if (!r) continue;

            r.renderingLayerMask = StyleBits.DefaultBit | StyleBits.ImageSpaceBit;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            mpb.SetInteger(ImageStyleId, (int)currentTestEffects);

            r.SetPropertyBlock(mpb);
        }
    }

    List<bool> GetActiveTestEffects()
    {
        if (testAssignmentMode == TestEffectAssignmentMode.Runtime)
            return runtimeTestEffects;

        return inspectorTestEffects;
    }

    uint BuildMask(List<bool> effects)
    {
        if (effects == null || effects.Count == 0)
        {
            Debug.Log("Test effect list is empty");
            return 0u;
        }

        uint mask = 0u;
        int count = Mathf.Min(effects.Count, 32);

        for (int i = 0; i < count; i++)
        {
            if (effects[i])
                mask |= 1u << i;
        }

        return mask;
    }

    static void ResizeBoolList(List<bool> list, int count)
    {
        if (list == null)
            return;

        while (list.Count < count)
            list.Add(false);

        while (list.Count > count)
            list.RemoveAt(list.Count - 1);
    }

    public void SetTestEffectCount(int count)
    {
        count = Mathf.Clamp(count, 0, 32);

        ResizeBoolList(inspectorTestEffects, count);
        ResizeBoolList(runtimeTestEffects, count);
    }

    public void SetAssignmentMode(TestEffectAssignmentMode mode)
    {
        testAssignmentMode = mode;
    }

    public void UseInspectorTestEffects()
    {
        testAssignmentMode = TestEffectAssignmentMode.Inspector;
    }

    public void UseRuntimeTestEffects()
    {
        testAssignmentMode = TestEffectAssignmentMode.Runtime;
    }

    public void SetRuntimeTestEffects(IEnumerable<int> indices)
    {
        ClearRuntimeTestEffects();

        if (indices == null)
            return;

        foreach (int idx in indices)
        {
            if ((uint)idx >= 32u)
                continue;

            if (idx >= runtimeTestEffects.Count)
                continue;

            runtimeTestEffects[idx] = true;
        }
    }

    public void SetRuntimeTestEffect(int index, bool enabled)
    {
        if ((uint)index >= 32u)
            return;

        if (index >= runtimeTestEffects.Count)
            return;

        runtimeTestEffects[index] = enabled;
    }

    public void ClearRuntimeTestEffects()
    {
        for (int i = 0; i < runtimeTestEffects.Count; i++)
            runtimeTestEffects[i] = false;
    }

    public void ClearInspectorTestEffects()
    {
        for (int i = 0; i < inspectorTestEffects.Count; i++)
            inspectorTestEffects[i] = false;
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