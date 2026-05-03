using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class NprStylesRendererFeature : ScriptableRendererFeature
{
    // prepasses
    private SourcePrepass _sourcePrepass;
    private IdPrepass _idPrepass;
    private CpuGeneration _cpuGenerationPrepass;
    private GpuGeneration _gpuGenerationPrepass;

    private CpuOcclusion _cpuOcclusionprepass;
    private GpuOcclusion _gpuOcclusionPrepass;

    private CpuMerging _cpuMergingPrepass;
    private TileMerging _tileMergingPrepass;

    private BboxDebugPass _bboxDebugPass;
    private TileGeneration _tileGenerationPrepass;

    // IMAGE EFFECTS
    [SerializeField]
    List<Effect> imageEffects = new();
    DitheringEffect ditheringEffect;
    ScreenspaceOutlinesEffect outlinesEffect;

    // shaders
    [SerializeField] private Shader idShader;

    [SerializeField] private Shader ssOutlinesShader;
    [SerializeField] private Shader ssOutlineBatchedShader;
    [SerializeField] private Shader ditheringShader;
    [SerializeField] private Shader ditheringBatchedShader;

    [SerializeField] private ComputeShader gpuGenerationComputeShader;

    [SerializeField] private ComputeShader cpuOcclusionComputeShader;
    [SerializeField] private ComputeShader occlusionComputeShader;
    [SerializeField] private Shader occlusionDebugShader;
    [SerializeField] private ComputeShader tileMergingComputeShader;   
    [SerializeField] private ComputeShader tilingComputeShader;
    [SerializeField] private Shader bboxDebugShader;

    // TEST EFFECTS
    [SerializeField, Min(1)] private int testEffectCount = 32;
    public int TestEffectCount => testEffectCount;


    // The shader all dummy effects use
    [SerializeField] private Shader testDummyShader;
    [SerializeField] private Shader testDummyBatchedShader;
    [SerializeField] private Shader dummyHeavyShader;
    [SerializeField] private Shader dummyHeavyBatchedShader;

    // settings
    public Settings settings = new();

    public void EnableTestMode(int styleCount)
    {
        NprTestingConfig.TestMode = true;
        testEffectCount = Mathf.Clamp(styleCount, 0, 32);
        Create();
    }

    public void DisableTestMode()
    {
        NprTestingConfig.TestMode = false;
        Create();
    }

    bool UseGpuMode()
    {
        return NprTestingConfig.RenderMode == NprRenderMode.GPU;
    }

    bool UseCpuMode()
    {
        return NprTestingConfig.RenderMode == NprRenderMode.CPU;
    }

    bool UseTiling()
    {
        return NprTestingConfig.RenderMode == NprRenderMode.Tiling;
    }

    bool UseBatchedScreenPasses()
    {
        return UseGpuMode() || UseTiling();
    }

    bool UseHeavyDummy()
    {
        return NprTestingConfig.CurrentTestEffect == TestEffect.Heavy;
    }

    private void ApplySettingsToConfig()
    {
        NprTestingConfig.RenderMode = settings.renderMode;
        NprTestingConfig.CurrentTestEffect = settings.currentTestEffect;
        NprTestingConfig.CurrentTileSize = settings.currentTileSize;

        NprTestingConfig.UseMerging = settings.useMerging;
        NprTestingConfig.UseOcclusion = settings.useOcclusion;
        NprTestingConfig.TestMode = settings.testMode;

        NprTestingConfig.DebugBBoxes = settings.debugBBoxes;
        NprTestingConfig.DebugID = settings.debugID;
    }

    private void ConfigureTagsForSettings()
    {
        foreach (var tag in StylisedTag.ActiveTags)
        {
            if (!tag) continue;

            tag.SetTestEffectCount(TestEffectCount);

            if (NprTestingConfig.TestMode)
            {
                tag.UseInspectorTestEffects();
            }

            tag.Apply();
        }
    }

    void OnValidate()
    {
        Create();
    }

    public override void Create()
    {
        if (!NprTestingConfig.IsBenchmarkRunning && !NprTestingConfig.IsValidationRunning)
        {
            ApplySettingsToConfig();
            ConfigureTagsForSettings();
        }
        DisposePasses();
        
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        _idPrepass = new IdPrepass(idShader);

        _sourcePrepass = new SourcePrepass();

        if(UseTiling())
        {
            if (tilingComputeShader == null)
            {
                Debug.LogError("Tiling compute shader 'tileGeneration' not set");
                return;
            }

            if(NprTestingConfig.TestMode)
                _tileGenerationPrepass = new TileGeneration(tilingComputeShader, testEffectCount, NprTestingConfig.TestMode, (int)NprTestingConfig.CurrentTileSize);
            else
                _tileGenerationPrepass = new TileGeneration(tilingComputeShader, (int)NprTestingConfig.CurrentTileSize);
        }

        if(UseCpuMode())
        {
            if (NprTestingConfig.TestMode)
                _cpuGenerationPrepass = new CpuGeneration(testEffectCount, true);
            else
                _cpuGenerationPrepass = new CpuGeneration();

            if (NprTestingConfig.UseMerging)
            {
                _cpuMergingPrepass = new CpuMerging();
            }

            
            if (NprTestingConfig.UseOcclusion)
            {
                if (cpuOcclusionComputeShader == null)
                {
                    Debug.LogError("Occlusion compute shader not set");
                    return;
                }

                _cpuOcclusionprepass = new CpuOcclusion(cpuOcclusionComputeShader);
            }
        }


        if(UseGpuMode())
        {
            if (gpuGenerationComputeShader == null)
            {
                Debug.LogError("GPU generation compute shader not set");
                return;
            }

            if (NprTestingConfig.TestMode)
                _gpuGenerationPrepass = new GpuGeneration(gpuGenerationComputeShader, testEffectCount, true);
            else
                _gpuGenerationPrepass = new GpuGeneration(gpuGenerationComputeShader);

            
            if (NprTestingConfig.UseOcclusion)
            {
                if (occlusionComputeShader == null)
                {
                    Debug.LogError("Occlusion compute shader not set");
                    return;
                }

                _gpuOcclusionPrepass = new GpuOcclusion(occlusionComputeShader);
            }

            if(NprTestingConfig.UseMerging)
            {
                if (tileMergingComputeShader == null)
                {
                    Debug.LogError("Tile merging compute shader not set");
                    return;
                }

                _tileMergingPrepass = new TileMerging(tileMergingComputeShader);
            }
        }


        if (NprTestingConfig.DebugBBoxes && !(NprTestingConfig.RenderMode == NprRenderMode.Fullscreen))
        {
            if (occlusionDebugShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/occlusionDebug'");
                return;
            }

            if (bboxDebugShader == null)
            {
                Debug.LogError("Could not find shader 'Custom/bboxDebug'");
                return;
            }

            _bboxDebugPass = new BboxDebugPass(occlusionDebugShader, bboxDebugShader);
        }




        if (UseBatchedScreenPasses())
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

        if (UseBatchedScreenPasses())
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

        imageEffects.Clear();





        if (NprTestingConfig.TestMode)
        {
            Shader chosenShader;

            if (UseBatchedScreenPasses())
            {
                if (UseHeavyDummy())
                {
                    chosenShader = dummyHeavyBatchedShader;

                    if (dummyHeavyBatchedShader == null)
                    {
                        Debug.LogError("useTestEffects is enabled and gpu mode is on but dummy Heavy batched shader is not set.");
                        return;
                    }
                }
                else
                {
                    chosenShader = testDummyBatchedShader;

                    if (chosenShader == null)
                    {
                        Debug.LogError("useTestEffects is enabled and gpu mode is on but dummy batched shader is not set.");
                        return;
                    }
                }
            }
            else
            {
                if (UseHeavyDummy())
                {
                    chosenShader = dummyHeavyShader;

                    if (chosenShader == null)
                    {
                        Debug.LogError("Could not find shader 'Custom/DummyHeavy'");
                        return;
                    }
                }
                else
                {
                    chosenShader = testDummyShader;

                    if (chosenShader == null)
                    {
                        Debug.LogError("Could not find shader 'Custom/Dummy'");
                        return;
                    }
                }
            }

            for (int i = 0; i < testEffectCount; i++)
                imageEffects.Add(new DummyEffect($"TestEffect_{i}", chosenShader, i));

            Debug.Log("Queued test pases");
        }
        else
        {
            imageEffects.Add(ditheringEffect);
            imageEffects.Add(outlinesEffect);

            Debug.Log("queued proper rendering passes");
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_idPrepass == null)
            return;

        renderer.EnqueuePass(_idPrepass);

        if(UseCpuMode())
        {
            if(_cpuGenerationPrepass != null)
                renderer.EnqueuePass(_cpuGenerationPrepass);
        
            if (NprTestingConfig.UseMerging && UseCpuMode() && _cpuMergingPrepass != null)
                renderer.EnqueuePass(_cpuMergingPrepass);

            if (NprTestingConfig.UseOcclusion && _cpuOcclusionprepass != null)
                renderer.EnqueuePass(_cpuOcclusionprepass);
        }
        else if(UseGpuMode())
        {
            if(_gpuGenerationPrepass != null)
                renderer.EnqueuePass(_gpuGenerationPrepass);
        

            if (NprTestingConfig.UseOcclusion && _gpuOcclusionPrepass != null)
                renderer.EnqueuePass(_gpuOcclusionPrepass);

            if (NprTestingConfig.UseMerging && UseGpuMode())
            {
                if(_tileMergingPrepass != null)
                    renderer.EnqueuePass(_tileMergingPrepass);
            }
        }
        else if (UseTiling())
        {
            if (_tileGenerationPrepass == null)
                return;

            renderer.EnqueuePass(_tileGenerationPrepass);
        }

        foreach (Effect effect in imageEffects)
        {
            // update source texture
            renderer.EnqueuePass(_sourcePrepass);

            foreach (EffectPass pass in effect.Passes)
            {

                // allow effects to access urp supplied render passes e.g. normals and depth
                pass.ConfigureInput(effect.RequiredInputs);

                renderer.EnqueuePass(pass);
            }
        }

        if (NprTestingConfig.DebugBBoxes && _bboxDebugPass != null)
            renderer.EnqueuePass(_bboxDebugPass);
    }

    private void DisposePasses()
    {
        GpuDebugState.Clear();
        
        _sourcePrepass?.Dispose();
        _sourcePrepass = null;

        _idPrepass?.Dispose();
        _idPrepass = null;

        _tileGenerationPrepass?.Dispose();
        _tileGenerationPrepass = null;

        _cpuGenerationPrepass?.Dispose();
        _cpuGenerationPrepass = null;

        _gpuGenerationPrepass?.Dispose();
        _gpuGenerationPrepass = null;

        _cpuOcclusionprepass?.Dispose();
        _cpuOcclusionprepass = null;

        _gpuOcclusionPrepass?.Dispose();
        _gpuOcclusionPrepass = null;

        _cpuMergingPrepass?.Dispose();
        _cpuMergingPrepass = null;

        _tileMergingPrepass?.Dispose();
        _tileMergingPrepass = null;

        _bboxDebugPass?.Dispose();
        _bboxDebugPass = null;

        if (imageEffects != null)
        {
            foreach (Effect effect in imageEffects)
            {
                if (effect == null || effect.Passes == null)
                    continue;

                foreach (EffectPass pass in effect.Passes)
                    pass?.Dispose();
            }

            imageEffects.Clear();
        }

        ditheringEffect = null;
        outlinesEffect = null;
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposePasses();
        }
    }
}