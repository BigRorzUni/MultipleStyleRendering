using System.Collections;
using System.Collections.Generic;
using NUnit.Framework.Constraints;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum NprDebugView
{
    None,
    StylisedID,
    Normals,
    Edges
}

[System.Serializable]
public class NprSettings
{
    public Color outlineColor = Color.black;
    public float outlineThickness = 0.03f;

    public Color ssOutlineColour = Color.darkRed;
    public float ssOutlineThickness = 0.03f;


    public NprDebugView debugView = NprDebugView.None;
}

public interface INprPass
{
    void ApplySettings(NprSettings settings);
}

public class NprStylesRendererFeature : ScriptableRendererFeature
{

    // prepasses
    private IdPrepass _idPrepass;
    private NormalsPrepass _normalsPrepass;
    private EdgesPrepass _edgesPrepass;

    // style passes
    List<ScriptableRenderPass> _stylePasses = new();
    private SimpleOutlinePass _outlinePass;
 
    // settings
    public NprSettings settings = new();
 
    // Called when the renderer feature is first created or reset.
    public override void Create()
    {
        // find the custom outline shader 
        var outlineshader = Shader.Find("Custom/SimpleOutline");
        if (outlineshader == null)
        {
            Debug.LogError("Could not find shader 'Custom/SimpleOutline'");
            return;
        }
        // create the outline render pass
        _outlinePass = new SimpleOutlinePass(outlineshader);

        // same for id pass
        var idShader = Shader.Find("Custom/ID");
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        _idPrepass = new IdPrepass(idShader, (LayerMask)(-1));

        // same for normal pass
        var normalsShader = Shader.Find("Custom/Normals");
        if (normalsShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Normals'");
            return;
        }
        _normalsPrepass = new NormalsPrepass(normalsShader, (LayerMask)(-1));

        // same for edges pass
        var edgesShader = Shader.Find("Custom/Edges");
        if (edgesShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Edges'");
            return;
        }
        _edgesPrepass = new EdgesPrepass(edgesShader);

        _stylePasses.Clear();

        // this is execution order (maybe have an enqueue pass in each pass class to do it automatically)
        _stylePasses.Add(_outlinePass);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
    ref RenderingData renderingData)
    {
        if (_idPrepass == null) return;

        // always produce id texture
        _idPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_idPrepass);

        //TODO: check if normals are needed
        _normalsPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_normalsPrepass);

        //TODO: check if edges are needed
        _edgesPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_edgesPrepass);

        // effect passes
        foreach (var pass in _stylePasses)
        {
            if (pass is INprPass nprPass)
                nprPass.ApplySettings(settings);
            if(settings.debugView == NprDebugView.None)
                renderer.EnqueuePass(pass);
        }
    }
}
