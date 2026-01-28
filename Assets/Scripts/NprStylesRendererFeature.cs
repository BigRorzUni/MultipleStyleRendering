using System.Collections;
using System.Collections.Generic;
using NUnit.Framework.Constraints;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum NprDebugView
{
    None,
    StylisedID,
    Normals
}

[System.Serializable]
public class NprSettings
{
    public Color outlineColor = Color.black;
    public float outlineThickness = 0.03f;


    public NprDebugView debugView = NprDebugView.None;
}

public interface INprPass
{
    void ApplySettings(NprSettings settings);
}

public class NprStylesRendererFeature : ScriptableRendererFeature
{

    private IdPrepass _idPrepass;
    private NormalsPrepass _normalsPrepass;

    List<ScriptableRenderPass> _stylePasses = new();
    private SimpleOutlinePass _outlinePass;
 
    public NprSettings settings = new();
 
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

        _stylePasses.Clear();
        // this is execution order (maybe have an enqueue pass in each pass class to do it automatically)
        _stylePasses.Add(_outlinePass);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
    ref RenderingData renderingData)
    {
        if (_idPrepass == null || _normalsPrepass == null) return;

        // Always produce shared buffers you rely on (pick what you want here)
        _idPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_idPrepass);

        _normalsPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_normalsPrepass);

        // set vars
        foreach (var pass in _stylePasses)
        {
            if (pass is INprPass nprPass)
                nprPass.ApplySettings(settings);
            if(settings.debugView == NprDebugView.None)
                renderer.EnqueuePass(pass);
        }
    }
}
