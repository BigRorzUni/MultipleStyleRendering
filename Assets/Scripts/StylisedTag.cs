using UnityEngine;

[DisallowMultipleComponent]
public class StylisedTag : MonoBehaviour
{
    [Range(0,255)]
    public int stylisedID = 0;

    static readonly int StylisedID = Shader.PropertyToID("_StylisedID");

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
        if (_renderers == null) return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(StylisedID, stylisedID);
            r.SetPropertyBlock(_mpb);
        }
    }
}