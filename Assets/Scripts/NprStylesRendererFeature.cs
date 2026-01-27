using System.Collections;
using System.Collections.Generic;
using NUnit.Framework.Constraints;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class NprStylesRendererFeature : ScriptableRendererFeature
{
    public bool debugStylisedID = false;
    public bool debugNormals = false;

    List<ScriptableRenderPass> _passes = new();
    private IdPrepass _idPrepass;
    private NormalsPrepass _normalsPrepass;
    private SimpleOutlinePass _outlinePass;


    // Public parameters exposed in the Inspector for customization
    public Color _outlineColor = Color.black;
    public float _outlineThickness = 0.03f;
 
    // Internal material instance using the custom outline shader
    private Material _outlineMaterial;

    // material for debug id viewing
    Material _debugIdMat;
 
 
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
        // Create the outline render pass
        _outlinePass = new SimpleOutlinePass(outlineshader);

        var idShader = Shader.Find("Custom/ID");
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        _idPrepass = new IdPrepass(idShader, (LayerMask)(-1));

        var normalsShader = Shader.Find("Custom/Normals");
        if (normalsShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Normals'");
            return;
        }
        _normalsPrepass = new NormalsPrepass(normalsShader, (LayerMask)(-1));

        _passes.Clear();

        // this is execution order (maybe have an enqueue pass in each pass class to do it automatically)
        _passes.Add(_outlinePass);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
    ref RenderingData renderingData)
    {
        if (_idPrepass == null || _normalsPrepass == null || _outlinePass == null) return;

        // set vars
        _idPrepass.debugToScreen = debugStylisedID;
        _normalsPrepass.debugToScreen = debugNormals;    

        _outlinePass.outlineColor = _outlineColor;
        _outlinePass.outlineThickness = _outlineThickness;

        // enqueue passes
        renderer.EnqueuePass(_idPrepass);
        renderer.EnqueuePass(_normalsPrepass);

        if (debugStylisedID || debugNormals)
            return;

        foreach (var p in _passes)
        {
            renderer.EnqueuePass(p);
        }

    }
}
