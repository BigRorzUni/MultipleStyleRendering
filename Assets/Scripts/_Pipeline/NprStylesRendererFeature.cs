using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem.Interactions;
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

<<<<<<< Updated upstream
    // STYLES: screen passes
    List<ScriptableRenderPass> _screenPasses = new();
    private ScreenspaceOutlinesPass _ssOutlinesPass;
    private DitheringPass _ditheringPass;
    private PixelisationPass _pixelisationPass;
=======

    // IMAGE EFFECTS
    [SerializeField]
    List<Effect> imageEffects = new();
    DitheringEffect ditheringEffect;
    ScreenspaceOutlinesEffect outlinesEffect;
>>>>>>> Stashed changes


    // shaders
    [SerializeField] private Shader idShader;
    [SerializeField] private Shader normalsShader;
    [SerializeField] private Shader toonShader;
    [SerializeField] private Shader ssOutlinesShader;
    [SerializeField] private Shader ditheringShader;
    [SerializeField] private Shader pixelisationShader;
<<<<<<< Updated upstream
 
    // settings
    public NprSettings settings = new();
 
    // Called when the renderer feature is first created or reset.
=======

    // TEST EFFECTS
    [SerializeField]
    List<Effect> testImgEffects = new();

    
    [SerializeField] public bool useTestEffects = true;
    [SerializeField, Min(1)] private int testEffectCount = 32;

    // The shader all dummy effects use
    [SerializeField] private Shader testDummyShader;

  
    // settings
    public Settings settings = new();

    public void EnableTestMode(int styleCount)
    {
        useTestEffects = true;
        testEffectCount = Mathf.Clamp(styleCount, 1, 32);

        Create(); 
    }

    public void DisableTestMode()
    {
        useTestEffects = false;
        Create();
    }

>>>>>>> Stashed changes
    public override void Create()
    {
        // find associated shaders and create passes
        // prepasses:
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        _idPrepass = new IdPrepass(idShader, (LayerMask)(-1));

        if (normalsShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Normals'");
            return;
        }
        _normalsPrepass = new NormalsPrepass(normalsShader, (LayerMask)(-1));

<<<<<<< Updated upstream
        // Shader edgesShader = Shader.Find("Custom/Edges");
        // if (edgesShader == null)
        // {
        //     Debug.LogError("Could not find shader 'Custom/Edges'");
        //     return;
        // }
        // _edgesPrepass = new EdgesPrepass(edgesShader);
=======
        if(useTestEffects)
        {
            _bboxPrepass = new bboxPrepass(testEffectCount, true);
        }
        else
            _bboxPrepass = new bboxPrepass();
>>>>>>> Stashed changes

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

<<<<<<< Updated upstream
        // add screenpasses in their execution order
        _screenPasses.Clear();
        _screenPasses.Add(_pixelisationPass);
        _screenPasses.Add(_ditheringPass);
        _screenPasses.Add(_ssOutlinesPass);
=======
        // add image effects in their execution order
        imageEffects.Clear();
        testImgEffects.Clear();

        if(useTestEffects)
        {
            if (testDummyShader == null)
            {
                Debug.LogError("useTestEffects is enabled but testDummyShader is not set.");
                return;
            }

            for (int i = 0; i < testEffectCount; i++)
            {
                imageEffects.Add(new DummyImageEffect($"TestEffect_{i}", testDummyShader, i));

                //Debug.Log($"Added test_effect_{i}");
            }

            //Debug.Log("render feature in test mode");
            Debug.Log("Queued test pases");
        }
        else
        {           
            imageEffects.Add(ditheringEffect);
            imageEffects.Add(outlinesEffect);

            Debug.Log("queued proper rendering passes");
        }
>>>>>>> Stashed changes
        
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

        //TODO: check if normals are needed
        _normalsPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_normalsPrepass);

        // _edgesPrepass.ApplySettings(settings);
        // renderer.EnqueuePass(_edgesPrepass);
        

        // screen passes
        foreach (var pass in _screenPasses)
        {
<<<<<<< Updated upstream
            if (pass is INprPass nprPass)
                nprPass.ApplySettings(settings);
            if(settings.debugView == NprDebugView.None)
                renderer.EnqueuePass(pass);
=======
            foreach(var pass in effect.Passes)
            {
                if (pass is INprPass nprPass)
                    nprPass.ApplySettings(settings);
                if(settings.debugView == NprDebugView.None)
                    renderer.EnqueuePass(pass);
            }
>>>>>>> Stashed changes
        }
    }
}
