using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class GpuMergingPrepass : ScriptableRenderPass
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

    void EnsurePairBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_pairBuffer == null || _pairBufferCapacity < requiredCapacity)
        {
            if (_pairBuffer != null)
                _pairBuffer.Release();

            _pairBufferCapacity = requiredCapacity;
            _pairBuffer = new ComputeBuffer(_pairBufferCapacity, sizeof(int));
        }
    }

    void EnsureValidPairBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_validPairBuffer == null || _validPairBufferCapacity < requiredCapacity)
        {
            if (_validPairBuffer != null)
                _validPairBuffer.Release();

            _validPairBufferCapacity = requiredCapacity;
            _validPairBuffer = new ComputeBuffer(_validPairBufferCapacity, sizeof(uint));
        }
    }

    void EnsureCanMergeBuffer()
    {
        if (_canMergeBuffer == null)
            _canMergeBuffer = new ComputeBuffer(1, sizeof(uint));
    }

    void EnsureOutputRectBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count * 3));

        if (_outputRectBuffer == null || _outputRectBufferCapacity < requiredCapacity)
        {
            if (_outputRectBuffer != null)
                _outputRectBuffer.Release();

            _outputRectBufferCapacity = requiredCapacity;
            _outputRectBuffer = new ComputeBuffer(_outputRectBufferCapacity, sizeof(float) * 4);
        }
    }

    void EnsureOutputMaskBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count * 3));

        if (_outputMaskBuffer == null || _outputMaskBufferCapacity < requiredCapacity)
        {
            if (_outputMaskBuffer != null)
                _outputMaskBuffer.Release();

            _outputMaskBufferCapacity = requiredCapacity;
            _outputMaskBuffer = new ComputeBuffer(_outputMaskBufferCapacity, sizeof(uint));
        }
    }

    void EnsureOutputVisibilityBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count * 3));

        if (_outputVisibilityBuffer == null || _outputVisibilityBufferCapacity < requiredCapacity)
        {
            if (_outputVisibilityBuffer != null)
                _outputVisibilityBuffer.Release();

            _outputVisibilityBufferCapacity = requiredCapacity;
            _outputVisibilityBuffer = new ComputeBuffer(_outputVisibilityBufferCapacity, sizeof(uint));
        }
    }

    void EnsureOutputCountBuffer()
    {
        if (_outputCountBuffer == null)
            _outputCountBuffer = new ComputeBuffer(1, sizeof(uint));
    }

    void EnsureIndirectArgsBuffer()
    {
        if (_indirectArgsBuffer == null)
            _indirectArgsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    public GpuMergingPrepass(ComputeShader bboxMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

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

        if (!NprTestingConfig.BoundingBoxes || !NprTestingConfig.BBoxMerging)
            return;

        if (!NprTestingConfig.BatchedBBoxMerging)
            return;

        if (!NprTestingConfig.BatchedDraws)
        {
            Debug.LogWarning("GPU merging is only valid with batched drawing of effects. No merging will take place");
            return;
        }

        if (_bboxMerging == null)
            return;

        if (nprFrameData.bboxRectBuffer == null || nprFrameData.bboxMaskBuffer == null || nprFrameData.bboxVisibilityBuffer == null)
            return;

        if (nprFrameData.bboxCount <= 0)
            return;

        EnsurePairBufferCapacity(nprFrameData.bboxCount);
        EnsureValidPairBufferCapacity(nprFrameData.bboxCount);
        EnsureCanMergeBuffer();

        EnsureOutputRectBufferCapacity(nprFrameData.bboxCount);
        EnsureOutputMaskBufferCapacity(nprFrameData.bboxCount);
        EnsureOutputVisibilityBufferCapacity(nprFrameData.bboxCount);
        EnsureOutputCountBuffer();
        EnsureIndirectArgsBuffer();

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

        using (var builder = renderGraph.AddComputePass("GPU BBox Merging", out ComputePassData passData))
        {
            builder.AllowPassCulling(false);
            
            passData.compute = _bboxMerging;

            passData.findPairsKernel = _findPairsKernel;
            passData.resolvePairsKernel = _resolvePairsKernel;
            passData.emitMergedKernel = _emitMergedKernel;
            passData.buildDrawArgsKernel = _buildDrawArgsKernel;

            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;

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

                // find merge pairs
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, RectBufferID, data.rectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, MaskBufferID, data.maskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, VisibilityBufferID, data.visibilityBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPairsKernel, PairBufferID, data.pairBuffer);

                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                ctx.cmd.DispatchCompute(data.compute, data.findPairsKernel, threadGroupsX, 1, 1);

                // resolve pairs to avoid conflicts
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, PairBufferID, data.pairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, ValidPairBufferID, data.validPairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, CanMergeBufferID, data.canMergeBuffer);

                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                ctx.cmd.DispatchCompute(data.compute, data.resolvePairsKernel, threadGroupsX, 1, 1);

                // merge bboxes, remove null ones, return the results
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

                // build indirect draw args from merged output count
                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, OutputCountBufferID, data.outputCountBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, IndirectArgsBufferID, data.indirectArgsBuffer);
                ctx.cmd.DispatchCompute(data.compute, data.buildDrawArgsKernel, 1, 1, 1);
            });
        }

        // merged output buffers are now the canonical bbox data
        nprFrameData.bboxRectBuffer = _outputRectBuffer;
        nprFrameData.bboxMaskBuffer = _outputMaskBuffer;
        nprFrameData.bboxVisibilityBuffer = _outputVisibilityBuffer;
        nprFrameData.bboxCountBuffer = _outputCountBuffer;
        nprFrameData.bboxIndirectArgsBuffer = _indirectArgsBuffer;

        NprGpuDebugState.SetBuffers(
            nprFrameData.bboxRectBuffer,
            nprFrameData.bboxMaskBuffer,
            nprFrameData.bboxVisibilityBuffer,
            nprFrameData.bboxCountBuffer,
            nprFrameData.bboxIndirectArgsBuffer,
            nprFrameData.bboxCount,
            nprFrameData.bboxVisibilityCount
        );
    }

    public void Dispose()
    {
        if (_pairBuffer != null)
            _pairBuffer.Release();

        if (_validPairBuffer != null)
            _validPairBuffer.Release();

        if (_canMergeBuffer != null)
            _canMergeBuffer.Release();

        if (_outputRectBuffer != null)
            _outputRectBuffer.Release();

        if (_outputMaskBuffer != null)
            _outputMaskBuffer.Release();

        if (_outputVisibilityBuffer != null)
            _outputVisibilityBuffer.Release();

        if (_outputCountBuffer != null)
            _outputCountBuffer.Release();

        if (_indirectArgsBuffer != null)
            _indirectArgsBuffer.Release();
    }
}