using System.Collections.Generic;
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
    // private SourcePrepass _sourcePrepass;
    private bboxPrepass _bboxPrepass;
    private CompositePass _compositePass;

    // STYLES: object passes
    List<ScriptableRenderPass> _objectPasses = new();
    private ToonPass _toonPass;
    // private SimpleOutlinePass _outlinePass;

    // STYLES: screen passes
    List<ScriptableRenderPass> _screenPasses = new();
    private ScreenspaceOutlinesPass _ssOutlinesPass;
    private DitheringPass _ditheringPass;
    private PixelisationPass _pixelisationPass;

    // shaders
    [SerializeField] private Shader idShader;
    [SerializeField] private Shader normalsShader;
    [SerializeField] private Shader toonShader;
    [SerializeField] private Shader ssOutlinesShader;
    [SerializeField] private Shader ditheringShader;
    [SerializeField] private Shader pixelisationShader;
    [SerializeField] private Shader bboxShader;
    [SerializeField] private Shader compositeShader;
  
    // settings
    public NprSettings settings = new();
 
    // Called when the renderer feature is first created or reset.
    public override void Create()
    {
        // find associated shaders and create passes
        // prepasses:
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        _idPrepass = new IdPrepass(idShader);

        if (normalsShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Normals'");
            return;
        }
        _normalsPrepass = new NormalsPrepass(normalsShader);

        if(bboxShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/bbox'");
            return;
        }
        _bboxPrepass = new bboxPrepass(bboxShader);

        if(compositeShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Composite'");
            return;
        }
        _compositePass = new CompositePass(compositeShader);   

        // Shader edgesShader = Shader.Find("Custom/Edges");
        // if (edgesShader == null)
        // {
        //     Debug.LogError("Could not find shader 'Custom/Edges'");
        //     return;
        // }
        // _edgesPrepass = new EdgesPrepass(edgesShader);

        // object passes
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
        if (ssOutlinesShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ScreenspaceOutlines'");
            return;
        }
        _ssOutlinesPass = new ScreenspaceOutlinesPass(ssOutlinesShader);

        if (ditheringShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Dithering'");
            return;
        }
        _ditheringPass = new DitheringPass(ditheringShader);

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

        // object passes
        foreach (var pass in _objectPasses)
        {
            if (pass is INprPass nprPass)
                nprPass.ApplySettings(settings);
            if(settings.debugView == NprDebugView.None)
                renderer.EnqueuePass(pass);
        }
        
        if (_idPrepass == null) return;

        // always produce id texture
        _idPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_idPrepass);

        // need to compute bounding boxes after id texture is created
        renderer.EnqueuePass(_bboxPrepass);

        //TODO: check if normals are needed
        _normalsPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_normalsPrepass);

        // _edgesPrepass.ApplySettings(settings);
        // renderer.EnqueuePass(_edgesPrepass);
        
        

        // screen passes
        foreach (var pass in _screenPasses)
        {
            if (pass is INprPass nprPass)
                nprPass.ApplySettings(settings);
            if(settings.debugView == NprDebugView.None)
                renderer.EnqueuePass(pass);
        }

        // composite pass
        renderer.EnqueuePass(_compositePass);
    }
}
