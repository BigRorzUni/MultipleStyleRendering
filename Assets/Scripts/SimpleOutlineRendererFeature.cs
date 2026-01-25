using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SimpleOutlineRendererFeature : ScriptableRendererFeature
{
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
        var shader = Shader.Find("Custom/SimpleOutline");
        if (shader == null)
        {
            Debug.LogError("Could not find shader 'Custom/SimpleOutline'");
            return;
        }
 
        // Create the outline material and render pass
        _outlineMaterial = new Material(shader);
        _outlinePass = new SimpleOutlinePass(_outlineMaterial);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
    ref RenderingData renderingData)
    {
        // Don’t continue if the material is missing
        if (_outlineMaterial == null)
            return;
 
        // Update the outline shader with the current settings
        _outlineMaterial.SetColor("_OutlineColor", _outlineColor);
        _outlineMaterial.SetFloat("_OutlineThickness", _outlineThickness);
 
        // Add the custom render pass to the renderer's queue
        renderer.EnqueuePass(_outlinePass);
    }
}
