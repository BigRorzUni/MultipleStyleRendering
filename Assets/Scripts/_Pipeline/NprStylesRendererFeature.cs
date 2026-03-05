using System.Collections;
using System.Collections.Generic;
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
public interface INprPass
{
    void ApplySettings(Settings settings);
}

public class NprStylesRendererFeature : ScriptableRendererFeature
{
    // prepasses
    private IdPrepass _idPrepass;
    private NormalsPrepass _normalsPrepass;
    private bboxPrepass _bboxPrepass;



    // IMAGE EFFECTS
    [SerializeField]
    List<Effect> imageEffects = new();
    DitheringEffect ditheringEffect;
    ScreenspaceOutlinesEffect outlinesEffect;


    // shaders
    [SerializeField] private Shader idShader;
    [SerializeField] private Shader normalsShader;
    [SerializeField] private Shader toonShader;
    [SerializeField] private Shader ssOutlinesShader;
    [SerializeField] private Shader ditheringShader;
    [SerializeField] private Shader pixelisationShader;

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

        _bboxPrepass = new bboxPrepass();
        if(useTestEffects)
        {
            _bboxPrepass = new bboxPrepass(testEffectCount, true);
        }
        else
            _bboxPrepass = new bboxPrepass();

        // object passes
        if (toonShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Toon'");
            return;
        }
        toonEffect = new ToonEffect(toonShader);

        // screen passes
        if (ssOutlinesShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ScreenspaceOutlines'");
            return;
        }
        outlinesEffect = new ScreenspaceOutlinesEffect(ssOutlinesShader);

        if (ditheringShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/Dithering'");
            return;
        }
        ditheringEffect = new DitheringEffect(ditheringShader);

        // add object passes in their execution order
        objectEffects.Clear();
        objectEffects.Add(toonEffect);
        //_objectPasses.Add(outlinePass);

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
        
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
    ref RenderingData renderingData)
    {
        // object effects
        foreach(var effect in objectEffects)
        {
            foreach(var pass in effect.Passes)
            {
                if (pass is INprPass nprPass)
                    nprPass.ApplySettings(settings);
                if(settings.debugView == NprDebugView.None)
                    renderer.EnqueuePass(pass);
            }
        }
        
        if (_idPrepass == null || _bboxPrepass == null) return;

        // always produce id texture
        _idPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_idPrepass);

        // need to compute bounding boxes after id texture is created
        renderer.EnqueuePass(_bboxPrepass);

        // compute normals
        _normalsPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_normalsPrepass);

        // image effects
        foreach(var effect in imageEffects)
        {
            
            foreach(var pass in effect.Passes)
            {
                if (pass is INprPass nprPass)
                    nprPass.ApplySettings(settings);
                if(settings.debugView == NprDebugView.None)
                    renderer.EnqueuePass(pass);
            }
        }
    }
}
