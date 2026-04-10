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
public interface INprPass
{    
    void ApplySettings(Settings settings);
}

public class NprStylesRendererFeature : ScriptableRendererFeature
{
    // prepasses
    private IdPrepass _idPrepass;
    private NormalsPrepass _normalsPrepass;
    private BBoxPrepass _bboxPrepass;
    private BBoxOcclusionPrepass _bboxOcclusionPrepass;
    private BBoxMergingPrepass _bboxMergingPrepass;
    private BboxDebugPass _bboxDebugPass;


    // IMAGE EFFECTS
    [SerializeField]
    List<Effect> imageEffects = new();
    DitheringEffect ditheringEffect;
    ScreenspaceOutlinesEffect outlinesEffect;

    // shaders
    [SerializeField] private Shader idShader;
    [SerializeField] private Shader normalsShader;
    // [SerializeField] private Shader toonShader;
    [SerializeField] private Shader ssOutlinesShader;
    [SerializeField] private Shader ssOutlineBatchedShader;
    [SerializeField] private Shader ditheringShader;
    [SerializeField] private Shader ditheringBatchedShader;
    // [SerializeField] private Shader pixelisationShader;

    [SerializeField] private ComputeShader bboxGenerationComputeShader;
    [SerializeField] private Shader occlusionShader;
    [SerializeField] private ComputeShader occlusionComputeShader;
    [SerializeField] private Shader occlusionDebugShader;
    [SerializeField] private ComputeShader bboxMergingComputeShader;
    [SerializeField] private Shader bboxDebugShader;


    // TEST EFFECTS

    
    [SerializeField, Min(1)] private int testEffectCount = 32;
    public int TestEffectCount => testEffectCount;

    // The shader all dummy effects use
    [SerializeField] private Shader testDummyShader;
    [SerializeField] private Shader testDummyBatchedShader;

  
    // settings
    public Settings settings = new();

    public void EnableTestMode(int styleCount)
    {
        NprTestingConfig.TestMode = true;
        testEffectCount = Mathf.Clamp(styleCount, 1, 32);

        Create();
    }

    public void DisableTestMode()
    {
        NprTestingConfig.TestMode = false;
        Create();
    }

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


        if(bboxGenerationComputeShader == null)
        {
            Debug.LogError("Occlusion compute shader 'GenerateBboxes' not set");
            return;        
        }

        if(NprTestingConfig.TestMode)
            _bboxPrepass = new BBoxPrepass(bboxGenerationComputeShader, testEffectCount, true);
        else
            _bboxPrepass = new BBoxPrepass(bboxGenerationComputeShader);
        
        if(NprTestingConfig.OcclusionCulling && NprTestingConfig.BoundingBoxes)
        {
            if(occlusionShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/bboxOcclusion'");
                return;
            }

            if(occlusionComputeShader == null)
            {
                Debug.LogError("Occlusion compute shader 'OcclusionCheck' not set");
                return;
            }

            _bboxOcclusionPrepass = new BBoxOcclusionPrepass(occlusionShader, occlusionComputeShader);
        }

        if(NprTestingConfig.BBoxMerging && NprTestingConfig.BoundingBoxes)
        {
            if(bboxMergingComputeShader == null)
            {
                Debug.LogError("Merging compute shader 'MergeBboxes' not set");
                return;
            }

            _bboxMergingPrepass = new BBoxMergingPrepass(bboxMergingComputeShader);
        }

        if(NprTestingConfig.DebugBBoxes && NprTestingConfig.BoundingBoxes)
        {
            if(occlusionDebugShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/occlusionDebug'");
                return;
            }
            if(bboxDebugShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/bboxDebug'");
                return;
            }
            
            _bboxDebugPass = new BboxDebugPass(occlusionDebugShader, bboxDebugShader); 
        }

        // object passes
        // if (toonShader == null)
        // {
        //     Debug.LogError("Could not find shader 'Custom/Toon'");
        //     return;
        // }
        // toonEffect = new ToonEffect(toonShader);

        // screen passes
        if(NprTestingConfig.BatchedDraws && NprTestingConfig.BoundingBoxes)
        {
            if (ssOutlineBatchedShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/ScreenspaceOutlinesBatched'");
                return;
            }
            outlinesEffect = new ScreenspaceOutlinesEffect(ssOutlineBatchedShader);
        }
        else
        {
            if (ssOutlinesShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/ScreenspaceOutlines'");
                return;
            }
            outlinesEffect = new ScreenspaceOutlinesEffect(ssOutlinesShader);
        }

        if(NprTestingConfig.BatchedDraws && NprTestingConfig.BoundingBoxes)
        {
            if (ditheringBatchedShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/DitheringBatched'");
                return;
            }
            ditheringEffect = new DitheringEffect(ditheringBatchedShader);
        }
        else    
        {
            if (ditheringShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/Dithering'");
                return;
            }
            ditheringEffect = new DitheringEffect(ditheringShader);
        }
        // add object passes in their execution order
        // objectEffects.Clear();
        // objectEffects.Add(toonEffect);
        //_objectPasses.Add(outlinePass);

        // add image effects in their execution order
        imageEffects.Clear();

        if(NprTestingConfig.TestMode)
        {
            if(NprTestingConfig.BatchedDraws && NprTestingConfig.BoundingBoxes)
            {
                if(testDummyBatchedShader == null)
                {
                    Debug.LogError("useTestEffects is enabled and batched draws is on but dummy batched shader is not set.");
                    return;
                }
                for (int i = 0; i < testEffectCount; i++)
                {
                    imageEffects.Add(new DummyImageEffect($"TestEffect_{i}", testDummyBatchedShader, i));

                    //Debug.Log($"Added test_effect_{i} (BATCHED)");
                }

            }
            else
            {
                if (testDummyShader == null)
                {
                    Debug.LogError("useTestEffects is enabled but dummy shader is not set.");
                    return;
                }

                for (int i = 0; i < testEffectCount; i++)
                {
                    imageEffects.Add(new DummyImageEffect($"TestEffect_{i}", testDummyShader, i));

                    //Debug.Log($"Added test_effect_{i}");
                }
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
        // foreach(var effect in objectEffects)
        // {
        //     foreach(var pass in effect.Passes)
        //     {
        //         if (pass is INprPass nprPass)
        //             nprPass.ApplySettings(settings);
        //         if(settings.debugView == NprDebugView.None)
        //             renderer.EnqueuePass(pass);
        //     }
        // }
        
        if (_idPrepass == null || _bboxPrepass == null) return;

        // need to compute bounding boxes after id texture is created
        renderer.EnqueuePass(_bboxPrepass);

        // always produce id texture
        _idPrepass.ApplySettings(settings);
        renderer.EnqueuePass(_idPrepass);

        if(NprTestingConfig.OcclusionCulling && NprTestingConfig.BoundingBoxes)
        {
            renderer.EnqueuePass(_bboxOcclusionPrepass);
        }

        if(NprTestingConfig.BBoxMerging && NprTestingConfig.BoundingBoxes)
        {
            renderer.EnqueuePass(_bboxMergingPrepass);
        }

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

        if(NprTestingConfig.DebugBBoxes)
            renderer.EnqueuePass(_bboxDebugPass);
    }
}
