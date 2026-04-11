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


    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int MaskBufferID = Shader.PropertyToID("_Masks");
    static readonly int VisibilityBufferID = Shader.PropertyToID("_Visibility");

    static readonly int PairBufferID = Shader.PropertyToID("_Pairs");

    static readonly int ValidPairBufferID = Shader.PropertyToID("_ValidPairs");
    static readonly int CanMergeBufferID = Shader.PropertyToID("_CanMerge");


    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");

    ComputeBuffer _pairBuffer;
    int _pairBufferCapacity = 0;
    ComputeBuffer _validPairBuffer;

    int _validPairBufferCapacity = 0;

    ComputeBuffer _canMergeBuffer;

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

    public GpuMergingPrepass(ComputeShader bboxMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        if (bboxMerging != null)
        {
            _bboxMerging = bboxMerging;
            _findPairsKernel = _bboxMerging.FindKernel("FindMergePairs");
            _resolvePairsKernel = _bboxMerging.FindKernel("ResolveMergePairs");
        }
    }

    private class ComputePassData
    {
        public ComputeShader compute;

        public int findPairsKernel;
        public int resolvePairsKernel;


        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeBuffer visibilityBuffer;

        public ComputeBuffer pairBuffer;

        public ComputeBuffer validPairBuffer;
        public ComputeBuffer canMergeBuffer;




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

        if(!NprTestingConfig.BatchedDraws)
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
       
        int[] pairInit = new int[_pairBufferCapacity];
        for (int i = 0; i < pairInit.Length; i++)
            pairInit[i] = -1;
        _pairBuffer.SetData(pairInit);

        EnsureValidPairBufferCapacity(nprFrameData.bboxCount);
        uint[] validPairInit = new uint[_validPairBufferCapacity];
        _validPairBuffer.SetData(validPairInit);
        
        EnsureCanMergeBuffer();
        uint[] canMergeInit = new uint[1] {0u};
        _canMergeBuffer.SetData(canMergeInit, 0, 0, 1);


        using (var builder = renderGraph.AddComputePass("GPU BBox Merging", out ComputePassData passData))
        {
            passData.compute = _bboxMerging;

            passData.findPairsKernel = _findPairsKernel;
            passData.resolvePairsKernel = _resolvePairsKernel;

            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;

            passData.pairBuffer = _pairBuffer;

            passData.validPairBuffer = _validPairBuffer;
            passData.canMergeBuffer = _canMergeBuffer;


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


                // resolve pairs into mergeable and unmergeable
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, PairBufferID, data.pairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, ValidPairBufferID, data.validPairBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.resolvePairsKernel, CanMergeBufferID, data.canMergeBuffer);

                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);

                ctx.cmd.DispatchCompute(data.compute, data.resolvePairsKernel, threadGroupsX, 1, 1);
            });
        }
    }

    public void Dispose()
    {
        if(_pairBuffer != null)
            _pairBuffer.Release();
        
        if (_validPairBuffer != null)
            _validPairBuffer.Release();

        if (_canMergeBuffer != null)
            _canMergeBuffer.Release();
    }
}