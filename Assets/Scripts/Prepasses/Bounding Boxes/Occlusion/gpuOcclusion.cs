using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
[System.Serializable]
public class GpuOcclusion : Prepass
{
    private readonly ComputeShader _occlusionCompute;
    private readonly int _occlusionKernel;

    static readonly int VisibilityTexID = Shader.PropertyToID("_VisibilityTex");
    static readonly int ResultBufferID = Shader.PropertyToID("_Result");

    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int BBoxMaskBufferID = Shader.PropertyToID("_ExpectedMasks");

    ComputeBuffer _visibilityBuffer;
    int _bboxVisibilityBufferCapacity = 0;
    uint[] _bboxVisibilityInitData;


    private class ComputePassData
    {
        public TextureHandle visibilityTex;
        public ComputeBuffer resultBuffer;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeShader compute;
        public int kernel;
        public int bboxCount;
    }

    public GpuOcclusion(ComputeShader occlusionComputeShader) : base("GpuOcclusion")
    {
        _occlusionCompute = occlusionComputeShader;
        if (_occlusionCompute != null)
        {
            _occlusionKernel = _occlusionCompute.FindKernel("OcclusionCheckBatched");
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_occlusionCompute == null)
            return;

        if (NprTestingConfig.RenderMode != NprRenderMode.GPU)
            return;

        if (!NprTestingConfig.UseOcclusion)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();


        NprFrameData.EnsureBufferCapacity(ref _visibilityBuffer, ref _bboxVisibilityBufferCapacity, nprFrameData.bboxCount, sizeof(uint));
        nprFrameData.visibilityBuffer = _visibilityBuffer;

        if (_bboxVisibilityInitData == null || _bboxVisibilityInitData.Length < _bboxVisibilityBufferCapacity)
            _bboxVisibilityInitData = new uint[_bboxVisibilityBufferCapacity];

        for (int i = 0; i < nprFrameData.bboxCount; i++)
            _bboxVisibilityInitData[i] = 1u;

        if (nprFrameData.visibilityBuffer != null && _bboxVisibilityInitData != null)
            nprFrameData.visibilityBuffer.SetData(_bboxVisibilityInitData, 0, 0, nprFrameData.bboxCount);

        if (nprFrameData.visibilityBuffer == null)
            return;

        // occlusion using gpu bbox buffers and id tex
        if (nprFrameData.rectBuffer == null)
            return;

        if (nprFrameData.maskBuffer == null)
            return;

        using (var builder = renderGraph.AddComputePass("BBox Occlusion Analysis (ID Tex)", out ComputePassData passData, profilingSampler))
        {
            builder.AllowPassCulling(false);

            passData.visibilityTex = nprFrameData.idTexture;
            passData.resultBuffer = nprFrameData.visibilityBuffer;
            passData.compute = _occlusionCompute;
            passData.kernel = _occlusionKernel;
            passData.rectBuffer = nprFrameData.rectBuffer;
            passData.maskBuffer = nprFrameData.maskBuffer;
            passData.bboxCount = nprFrameData.bboxCount;

            builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

            builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
            {
                ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, RectBufferID, data.rectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, BBoxMaskBufferID, data.maskBuffer);
                ctx.cmd.DispatchCompute(data.compute, data.kernel, data.bboxCount, 1, 1);
            });

            // GpuDebugState.SetOutputBuffers(
            //     nprFrameData.rectBuffer,
            //     nprFrameData.maskBuffer,
            //     nprFrameData.visibilityBuffer,
            //     nprFrameData.countBuffer,
            //     nprFrameData.indirectArgsBuffer
            // );
        }
    }


    public override void Dispose()
    {
        if (_visibilityBuffer != null)
        {
            _visibilityBuffer.Release();
            _visibilityBuffer = null;
        }

        _bboxVisibilityBufferCapacity = 0;
    }
}