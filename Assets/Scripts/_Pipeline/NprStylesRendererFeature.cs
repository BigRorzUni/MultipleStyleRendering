using System.Collections.Generic;
using UnityEngine;
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
    public Color outlineColour = Color.black;
    public float outlineThickness = 1f;
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
    // private EdgesPrepass _edgesPrepass;
    private SourcePrepass _sourcePrepass;

    // STYLES: object passes
    List<ScriptableRenderPass> _objectPasses = new();
    private ToonPass _toonPass;
    // private SimpleOutlinePass _outlinePass;

    // STYLES: screen passes
    List<ScriptableRenderPass> _screenPasses = new();
    private ScreenspaceOutlinesPass _ssOutlinesPass;
    private DitheringPass _ditheringPass;
    private PixelisationPass _pixelisationPass;
 
    // settings
    public NprSettings settings = new();
 
    // Called when the renderer feature is first created or reset.
    public override void Create()
    {
        // find associated shaders and create passes
        // prepasses:
        Shader idShader = Shader.Find("Custom/ID");
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        _idPrepass = new IdPrepass(idShader, (LayerMask)(-1));

        Shader normalsShader = Shader.Find("Custom/Normals");
        if (normalsShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Normals'");
            return;
        }
        _normalsPrepass = new NormalsPrepass(normalsShader, (LayerMask)(-1));

        // Shader edgesShader = Shader.Find("Custom/Edges");
        // if (edgesShader == null)
        // {
        //     Debug.LogError("Could not find shader 'Custom/Edges'");
        //     return;
        // }
        // _edgesPrepass = new EdgesPrepass(edgesShader);

        // object passes
        Shader toonShader = Shader.Find("Custom/Toon");
        if (toonShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Toon'");
            return;
        }
        _toonPass = new ToonPass(toonShader);

        // var outlineshader = Shader.Find("Custom/SimpleOutline");
        // if (outlineshader == null)
        // {
        //     Debug.LogError("Could not find shader 'Custom/SimpleOutline'");
        //     return;
        // }
        // // create the outline render pass
        // _outlinePass = new SimpleOutlinePass(outlineshader);

        // screen passes
        Shader ssOutlinesShader = Shader.Find("Custom/ScreenspaceOutlines");
        if (ssOutlinesShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ScreenspaceOutlines'");
            return;
        }
        _ssOutlinesPass = new ScreenspaceOutlinesPass(ssOutlinesShader);

        Shader ditheringShader = Shader.Find("Custom/Dithering");
        if (ditheringShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Dithering'");
            return;
        }
        _ditheringPass = new DitheringPass(ditheringShader);

        Shader pixelisationShader = Shader.Find("Custom/Pixelisation");
        if (pixelisationShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Pixelisation'");
            return;
        }
        _pixelisationPass = new PixelisationPass(pixelisationShader);

        // add object passes in their execution order
        _objectPasses.Clear();
        _objectPasses.Add(_toonPass);
        //_objectPasses.Add(outlinePass);

        // add screenpasses in their execution order
        _screenPasses.Clear();
        _screenPasses.Add(_pixelisationPass);
        _screenPasses.Add(_ditheringPass);
        _screenPasses.Add(_ssOutlinesPass);
        
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

        // _edgesPrepass.ApplySettings(settings);
        // renderer.EnqueuePass(_edgesPrepass);

        // object passes
        foreach (var pass in _objectPasses)
        {
            if (pass is INprPass nprPass)
                nprPass.ApplySettings(settings);
            if(settings.debugView == NprDebugView.None)
                renderer.EnqueuePass(pass);
        }

        // get source texture pass ?
        

        // screen passes
        foreach (var pass in _screenPasses)
        {
            if (pass is INprPass nprPass)
                nprPass.ApplySettings(settings);
            if(settings.debugView == NprDebugView.None)
                renderer.EnqueuePass(pass);
        }
    }
}
