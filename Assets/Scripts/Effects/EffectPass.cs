using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public abstract class EffectPass : ScriptableRenderPass, IDisposable
{
    protected Material _mat;
    protected readonly StyleBits.ImageSpaceEffect _requiredBit;
    public readonly string PassName;
    public ProfilingSampler Sampler => profilingSampler;

    protected EffectPass(Shader shader, string passName, StyleBits.ImageSpaceEffect requiredBit)
    {
        PassName = passName;
        _requiredBit = requiredBit;
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        profilingSampler = new ProfilingSampler(passName); // makes pass appear in profiler

        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);
    }


    public virtual void ApplySettings(Settings settings)
    {
        // if RendererFeature applies settings they can be given to pass here
    }

    public sealed override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        if (!ShouldRun(frameData, cameraData, nprFrameData))
            return;

        switch (NprTestingConfig.RenderMode)
        {
            case NprRenderMode.Fullscreen:
                RunFullscreen(renderGraph, frameData, cameraData, nprFrameData);
                break;

            case NprRenderMode.CPU:
                RunCpu(renderGraph, frameData, cameraData, nprFrameData);
                break;

            case NprRenderMode.GPU:
                RunGpu(renderGraph, frameData, cameraData, nprFrameData);
                break;
            
            case NprRenderMode.Tiling:
                RunGpu(renderGraph, frameData, cameraData, nprFrameData); // For now run GPU version for tiling as well
                break;
        }
    }

    protected virtual bool ShouldRun(UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        if (_requiredBit != StyleBits.ImageSpaceEffect.None && ((nprFrameData.presentImageBits & _requiredBit) == 0) && NprTestingConfig.RenderMode != NprRenderMode.Fullscreen)
            return false;

        return true;
    }

    protected abstract void RunFullscreen(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData);

    protected abstract void RunCpu(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData);

    protected abstract void RunGpu(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData);

    // TODO: Add CPU batched pass

    public virtual void Dispose()
    {
        if (_mat != null)
        {
            CoreUtils.Destroy(_mat);
            _mat = null;
        }
    }
}