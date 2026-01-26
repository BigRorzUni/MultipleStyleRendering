using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class NprStylesRendererFeature : ScriptableRendererFeature
{
    List<ScriptableRenderPass> _passes = new();
    private IdPrepass _idPrepass;
    private SimpleOutlinePass _outlinePass;


    // Public parameters exposed in the Inspector for customization
    public Color _outlineColor = Color.black;
    public float _outlineThickness = 0.03f;
 
    // Internal material instance using the custom outline shader
    private Material _outlineMaterial;
 
 
    /// <summary>
    /// Called when the renderer feature is first created or reset.
    /// </summary>
    public override void Create()
    {
        // Find the custom outline shader (must exist in the project)
        
        var outlineshader = Shader.Find("Custom/SimpleOutline");
        if (outlineshader == null)
        {
            Debug.LogError("Could not find shader 'Custom/SimpleOutline'");
            return;
        }
        // Create the outline material and render pass
        _outlineMaterial = new Material(outlineshader);
        _outlinePass = new SimpleOutlinePass(_outlineMaterial);

        var idShader = Shader.Find("Custom/ID");
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        var idMaterial = new Material(idShader);
        _idPrepass = new IdPrepass(idMaterial, (LayerMask)(-1));

        _passes.Clear();
        // this is execution order (maybe have an enqueue pass in each pass class to do it automatically)
        _passes.Add(_idPrepass);
        _passes.Add(_outlinePass);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
    ref RenderingData renderingData)
    {
        if (_idPrepass == null || _outlinePass == null) return;

        if (_outlineMaterial != null)
        {
            _outlineMaterial.SetColor("_OutlineColor", _outlineColor);
            _outlineMaterial.SetFloat("_OutlineThickness", _outlineThickness);
        }

        foreach (var p in _passes)
            renderer.EnqueuePass(p);
    }
}
