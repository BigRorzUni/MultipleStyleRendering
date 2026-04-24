using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using System.Runtime.InteropServices;

[System.Serializable]
public class GpuMerging : Prepass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    readonly ComputeShader _bboxMerging;
    readonly int _findPairsKernel;
    readonly int _resolvePairsKernel;
    readonly int _emitMergedKernel;
    readonly int _buildDrawArgsKernel;

    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int MaskBufferID = Shader.PropertyToID("_Masks");
    static readonly int VisibilityBufferID = Shader.PropertyToID("_Visibility");

    static readonly int PairBufferID = Shader.PropertyToID("_Pairs");
    static readonly int ValidPairBufferID = Shader.PropertyToID("_ValidPairs");
    static readonly int CanMergeBufferID = Shader.PropertyToID("_CanMerge");

    static readonly int OutputRectBufferID = Shader.PropertyToID("_OutputRects");
    static readonly int OutputMaskBufferID = Shader.PropertyToID("_OutputMasks");
    static readonly int OutputVisibilityBufferID = Shader.PropertyToID("_OutputVisibility");
    static readonly int OutputCountBufferID = Shader.PropertyToID("_OutputCount");
    static readonly int IndirectArgsBufferID = Shader.PropertyToID("_IndirectArgs");

    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");

    ComputeBuffer _pairBuffer;
    int _pairBufferCapacity = 0;

    ComputeBuffer _validPairBuffer;
    int _validPairBufferCapacity = 0;

    ComputeBuffer _canMergeBuffer;

    ComputeBuffer _outputRectBuffer;
    int _outputRectBufferCapacity = 0;

    ComputeBuffer _outputMaskBuffer;
    int _outputMaskBufferCapacity = 0;

    ComputeBuffer _outputVisibilityBuffer;
    int _outputVisibilityBufferCapacity = 0;

    ComputeBuffer _outputCountBuffer;
    ComputeBuffer _indirectArgsBuffer;

    public GpuMerging(ComputeShader bboxMerging) : base("GpuMergingPrepass")
    {
        if (bboxMerging != null)
        {
            _bboxMerging = bboxMerging;
            _findPairsKernel = _bboxMerging.FindKernel("FindMergePairs");
            _resolvePairsKernel = _bboxMerging.FindKernel("ResolveMergePairs");
            _emitMergedKernel = _bboxMerging.FindKernel("EmitMergedBoxes");
            _buildDrawArgsKernel = _bboxMerging.FindKernel("BuildDrawArgs");
        }
    }

    private class ComputePassData
    {
        public ComputeShader compute;

        public int findPairsKernel;
        public int resolvePairsKernel;
        public int emitMergedKernel;
        public int buildDrawArgsKernel;

        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeBuffer visibilityBuffer;

        public ComputeBuffer pairBuffer;
        public ComputeBuffer validPairBuffer;
        public ComputeBuffer canMergeBuffer;

        public ComputeBuffer outputRectBuffer;
        public ComputeBuffer outputMaskBuffer;
        public ComputeBuffer outputVisibilityBuffer;
        public ComputeBuffer outputCountBuffer;
        public ComputeBuffer indirectArgsBuffer;

        public int bboxCount;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        if (NprTestingConfig.RenderMode != NprRenderMode.GPU)
            return;

        if (!NprTestingConfig.UseMerging)
            return;

        if (_bboxMerging == null)
            return;

        if (nprFrameData.rectBuffer == null || nprFrameData.maskBuffer == null || nprFrameData.visibilityBuffer == null)
            return;

        if (nprFrameData.bboxCount <= 0)
            return;

        NprFrameData.EnsureBufferCapacity(ref _pairBuffer, ref _pairBufferCapacity, nprFrameData.bboxCount, sizeof(int));
        NprFrameData.EnsureBufferCapacity(ref _validPairBuffer, ref _validPairBufferCapacity, nprFrameData.bboxCount, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _canMergeBuffer, 1, sizeof(uint));

        // these buffers are larger than bboxCount as merging can produce more bboxes than there were previously
        NprFrameData.EnsureBufferCapacity(ref _outputRectBuffer, ref _outputRectBufferCapacity, nprFrameData.bboxCount * 3, Marshal.SizeOf<Vector4>());
        NprFrameData.EnsureBufferCapacity(ref _outputMaskBuffer, ref _outputMaskBufferCapacity, nprFrameData.bboxCount * 3, sizeof(uint));
        NprFrameData.EnsureBufferCapacity(ref _outputVisibilityBuffer, ref _outputVisibilityBufferCapacity, nprFrameData.bboxCount * 3, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _outputCountBuffer, 1, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _indirectArgsBuffer, 4, sizeof(uint), ComputeBufferType.IndirectArguments);

        int[] pairInit = new int[_pairBufferCapacity];
        for (int i = 0; i < pairInit.Length; i++)
            pairInit[i] = -1;
        _pairBuffer.SetData(pairInit);

        uint[] validPairInit = new uint[_validPairBufferCapacity];
        _validPairBuffer.SetData(validPairInit);

        uint[] canMergeInit = new uint[1] { 0u };
        _canMergeBuffer.SetData(canMergeInit, 0, 0, 1);

        uint[] outputCountInit = new uint[1] { 0u };
        _outputCountBuffer.SetData(outputCountInit, 0, 0, 1);

        uint[] indirectArgsInit = new uint[4] { 6u, 0u, 0u, 0u };
        _indirectArgsBuffer.SetData(indirectArgsInit, 0, 0, 4);

        // THIS NEEDS TO ITERATE
        using (var builder = renderGraph.AddComputePass("GPU BBox Merging", out ComputePassData passData, profilingSampler))
        {
            builder.AllowPassCulling(false);

            passData.compute = _bboxMerging;

            passData.findPairsKernel = _findPairsKernel;
            passData.resolvePairsKernel = _resolvePairsKernel;
            passData.emitMergedKernel = _emitMergedKernel;
            passData.buildDrawArgsKernel = _buildDrawArgsKernel;

            passData.rectBuffer = nprFrameData.rectBuffer;
            passData.maskBuffer = nprFrameData.maskBuffer;
            passData.visibilityBuffer = nprFrameData.visibilityBuffer;

            passData.pairBuffer = _pairBuffer;
            passData.validPairBuffer = _validPairBuffer;
            passData.canMergeBuffer = _canMergeBuffer;

            passData.outputRectBuffer = _outputRectBuffer;
            passData.outputMaskBuffer = _outputMaskBuffer;
            passData.outputVisibilityBuffer = _outputVisibilityBuffer;
            passData.outputCountBuffer = _outputCountBuffer;
            passData.indirectArgsBuffer = _indirectArgsBuffer;

            passData.bboxCount = nprFrameData.bboxCount;

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
            {
                int threadGroupsX = Mathf.CeilToInt(data.bboxCount / 64.0f);

                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, RectBufferID, data.rectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, MaskBufferID, data.maskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, VisibilityBufferID, data.visibilityBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, PairBufferID, data.pairBuffer);
                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                ctx.cmd.DispatchCompute(data.compute, data.findPairsKernel, threadGroupsX, 1, 1);

                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, PairBufferID, data.pairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, ValidPairBufferID, data.validPairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, CanMergeBufferID, data.canMergeBuffer);
                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                ctx.cmd.DispatchCompute(data.compute, data.resolvePairsKernel, threadGroupsX, 1, 1);

                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, RectBufferID, data.rectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, MaskBufferID, data.maskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, VisibilityBufferID, data.visibilityBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, PairBufferID, data.pairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, ValidPairBufferID, data.validPairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, OutputRectBufferID, data.outputRectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, OutputMaskBufferID, data.outputMaskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, OutputVisibilityBufferID, data.outputVisibilityBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitMergedKernel, OutputCountBufferID, data.outputCountBuffer);
                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                ctx.cmd.DispatchCompute(data.compute, data.emitMergedKernel, threadGroupsX, 1, 1);

                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, OutputCountBufferID, data.outputCountBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, IndirectArgsBufferID, data.indirectArgsBuffer);
                ctx.cmd.DispatchCompute(data.compute, data.buildDrawArgsKernel, 1, 1, 1);
            });
        }

        nprFrameData.rectBuffer = _outputRectBuffer;
        nprFrameData.maskBuffer = _outputMaskBuffer;
        nprFrameData.visibilityBuffer = _outputVisibilityBuffer;
        nprFrameData.countBuffer = _outputCountBuffer;
        nprFrameData.indirectArgsBuffer = _indirectArgsBuffer;

        GpuDebugState.SetOutputBuffers(_outputRectBuffer, _outputMaskBuffer, _outputVisibilityBuffer, _outputCountBuffer, _indirectArgsBuffer);
    }

    public override void Dispose()
    {
        if (_pairBuffer != null)
        {
            _pairBuffer.Release();
            _pairBuffer = null;
        }

        if (_validPairBuffer != null)
        {
            _validPairBuffer.Release();
            _validPairBuffer = null;
        }

        if (_canMergeBuffer != null)
        {
            _canMergeBuffer.Release();
            _canMergeBuffer = null;
        }

        if (_outputRectBuffer != null)
        {
            _outputRectBuffer.Release();
            _outputRectBuffer = null;
        }

        if (_outputMaskBuffer != null)
        {
            _outputMaskBuffer.Release();
            _outputMaskBuffer = null;
        }

        if (_outputVisibilityBuffer != null)
        {
            _outputVisibilityBuffer.Release();
            _outputVisibilityBuffer = null;
        }

        if (_outputCountBuffer != null)
        {
            _outputCountBuffer.Release();
            _outputCountBuffer = null;
        }

        if (_indirectArgsBuffer != null)
        {
            _indirectArgsBuffer.Release();
            _indirectArgsBuffer = null;
        }
    }
}