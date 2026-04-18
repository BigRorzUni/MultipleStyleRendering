using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class NprStylesRendererFeature : ScriptableRendererFeature
{
    // prepasses
    private SourcePrepass _sourcePrepass;
    private IdPrepass _idPrepass;
    private BBoxGeneration _bboxPrepass;
    private BBoxOcclusion _bboxOcclusionPrepass;
    private CpuMerging _cpuMergingPrepass;
    private GpuMerging _gpuMergingPrepass;
    private GpuTiling _gpuTileMergingPrepass;
    private BboxDebugPass _bboxDebugPass;

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

    [SerializeField] private ComputeShader bboxGenerationComputeShader;
    [SerializeField] private ComputeShader occlusionComputeShader;
    [SerializeField] private Shader occlusionDebugShader;
    [SerializeField] private ComputeShader bboxMergingComputeShader;
    [SerializeField] private ComputeShader TileMergingComputeShader;   
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
        testEffectCount = Mathf.Clamp(styleCount, 1, 32);
        Create();
    }

    public void DisableTestMode()
    {
        NprTestingConfig.TestMode = false;
        Create();
    }

    bool UseBoundingBoxes()
    {
        return NprTestingConfig.RenderMode != NprRenderMode.Fullscreen;
    }

    bool UseGpuMode()
    {
        return NprTestingConfig.RenderMode == NprRenderMode.GPU;
    }

    bool UseCpuMode()
    {
        return NprTestingConfig.RenderMode == NprRenderMode.CPU;
    }

    bool UseBatchedScreenPasses()
    {
        return UseGpuMode();
    }

    bool UseIterativeGpuMerging()
    {
        return NprTestingConfig.GPUMergeMethod == GpuMergeMethod.PairwiseIterative;
    }

    bool UseHeavyDummy()
    {
        return NprTestingConfig.CurrentTestEffect == TestEffect.Heavy;
    }

    public override void Create()
    {
        DisposePasses();
        
        if (idShader == null)
        {
            Debug.LogError("Could not find shader 'Custom/ID'");
            return;
        }
        _idPrepass = new IdPrepass(idShader);

        _sourcePrepass = new SourcePrepass();

        if (bboxGenerationComputeShader == null)
        {
            Debug.LogError("Occlusion compute shader 'GenerateBboxes' not set");
            return;
        }

        if (NprTestingConfig.TestMode)
            _bboxPrepass = new BBoxGeneration(bboxGenerationComputeShader, testEffectCount, true);
        else
            _bboxPrepass = new BBoxGeneration(bboxGenerationComputeShader);

        if (NprTestingConfig.UseOcclusion && UseBoundingBoxes())
        {
            if (occlusionComputeShader == null)
            {
                Debug.LogError("Occlusion compute shader 'OcclusionCheck' not set");
                return;
            }

            _bboxOcclusionPrepass = new BBoxOcclusion(occlusionComputeShader);
        }

        if (NprTestingConfig.UseMerging && UseBoundingBoxes())
        {
            if (UseCpuMode())
            {
                _cpuMergingPrepass = new CpuMerging();
            }
            else if (UseGpuMode())
            {
                if (bboxMergingComputeShader == null)
                {
                    Debug.LogError("Merging compute shader 'MergeBboxes' not set");
                    return;
                }

                _gpuMergingPrepass = new GpuMerging(bboxMergingComputeShader);

                if (TileMergingComputeShader == null)
                {
                    Debug.LogError("Tile merging compute shader 'TileMerge' not set");
                    return;
                }

                _gpuTileMergingPrepass = new GpuTiling(TileMergingComputeShader);
            }
        }

        if (NprTestingConfig.DebugBBoxes && UseBoundingBoxes())
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
            Shader chosenShader = null;

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
        if (_idPrepass == null || _bboxPrepass == null)
            return;

        renderer.EnqueuePass(_idPrepass);

        renderer.EnqueuePass(_bboxPrepass);

        if (NprTestingConfig.UseMerging && UseBoundingBoxes() && UseCpuMode() && _cpuMergingPrepass != null)
            renderer.EnqueuePass(_cpuMergingPrepass);

        if (NprTestingConfig.UseOcclusion && UseBoundingBoxes() && _bboxOcclusionPrepass != null)
            renderer.EnqueuePass(_bboxOcclusionPrepass);

        if (NprTestingConfig.UseMerging && UseBoundingBoxes() && UseGpuMode())
        {
            if(UseIterativeGpuMerging() && _gpuMergingPrepass != null)
                renderer.EnqueuePass(_gpuMergingPrepass);
            else if(_gpuTileMergingPrepass != null)
                renderer.EnqueuePass(_gpuTileMergingPrepass);
        }

        foreach (Effect effect in imageEffects)
        {
            // update source texture
            renderer.EnqueuePass(_sourcePrepass);

            foreach (EffectPass pass in effect.Passes)
            {
                // make sure multipass effects ping pong textures properly when i implement them

                pass.ConfigureInput(effect.RequiredInputs);

                pass.ApplySettings(settings);

                renderer.EnqueuePass(pass);
            }
        }

        if (NprTestingConfig.DebugBBoxes && _bboxDebugPass != null)
            renderer.EnqueuePass(_bboxDebugPass);

        if(NprTestingConfig.DebugID)
            // debug id pass
            Debug.Log("show hashed IDs");
    }

    private void DisposePasses()
    {
        _sourcePrepass?.Dispose();
        _sourcePrepass = null;

        _idPrepass?.Dispose();
        _idPrepass = null;

        _bboxPrepass?.Dispose();
        _bboxPrepass = null;

        _bboxOcclusionPrepass?.Dispose();
        _bboxOcclusionPrepass = null;

        _cpuMergingPrepass?.Dispose();
        _cpuMergingPrepass = null;

        _gpuMergingPrepass?.Dispose();
        _gpuMergingPrepass = null;

        _gpuTileMergingPrepass?.Dispose();
        _gpuTileMergingPrepass = null;

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


    public List<ProfilingSampler> GetBenchmarkSamplers()
    {
        List<ProfilingSampler> samplers = new();

        void AddSampler(Prepass pass)
        {
            if (pass != null && pass.Sampler != null)
                samplers.Add(pass.Sampler);
        }

        AddSampler(_sourcePrepass);
        AddSampler(_idPrepass);
        AddSampler(_bboxPrepass);
        AddSampler(_bboxOcclusionPrepass);
        AddSampler(_cpuMergingPrepass);
        AddSampler(_gpuMergingPrepass);
        AddSampler(_gpuTileMergingPrepass);

        foreach (Effect effect in imageEffects)
        {
            if (effect == null || effect.Passes == null)
                continue;

            foreach (EffectPass pass in effect.Passes)
            {
                if (pass != null && pass.Sampler != null)
                    samplers.Add(pass.Sampler);
            }
        }

        return samplers;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposePasses();
        }
    }
}