using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class GpuTileMergingPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    readonly ComputeShader _tileMerging;

    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int MaskBufferID = Shader.PropertyToID("_Masks");
    static readonly int VisibilityBufferID = Shader.PropertyToID("_Visibility");


    static readonly int OutputRectBufferID = Shader.PropertyToID("_OutputRects");
    static readonly int OutputMaskBufferID = Shader.PropertyToID("_OutputMasks");
    static readonly int OutputVisibilityBufferID = Shader.PropertyToID("_OutputVisibility");
    static readonly int OutputCountBufferID = Shader.PropertyToID("_OutputCount");
    static readonly int IndirectArgsBufferID = Shader.PropertyToID("_IndirectArgs");

    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");


    ComputeBuffer _outputRectBuffer;
    int _outputRectBufferCapacity = 0;

    ComputeBuffer _outputMaskBuffer;
    int _outputMaskBufferCapacity = 0;

    ComputeBuffer _outputVisibilityBuffer;
    int _outputVisibilityBufferCapacity = 0;

    ComputeBuffer _outputCountBuffer;
    ComputeBuffer _indirectArgsBuffer;

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

    public GpuTileMergingPrepass(ComputeShader bboxMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        if (bboxMerging != null)
        {
            _tileMerging = bboxMerging;
        }
    }

    private class ComputePassData
    {
        public ComputeShader compute;


        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeBuffer visibilityBuffer;



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

        if (_tileMerging == null)
            return;

        if (nprFrameData.bboxRectBuffer == null || nprFrameData.bboxMaskBuffer == null || nprFrameData.bboxVisibilityBuffer == null)
            return;

        if (nprFrameData.bboxCount <= 0)
            return;

        EnsureOutputRectBufferCapacity(nprFrameData.bboxCount);
        EnsureOutputMaskBufferCapacity(nprFrameData.bboxCount);
        EnsureOutputVisibilityBufferCapacity(nprFrameData.bboxCount);
        EnsureOutputCountBuffer();
        EnsureIndirectArgsBuffer();

        uint[] outputCountInit = new uint[1] { 0u };
        _outputCountBuffer.SetData(outputCountInit, 0, 0, 1);

        uint[] indirectArgsInit = new uint[4] { 6u, 0u, 0u, 0u };
        _indirectArgsBuffer.SetData(indirectArgsInit, 0, 0, 4);

        using (var builder = renderGraph.AddComputePass("GPU BBox Merging", out ComputePassData passData))
        {
            builder.AllowPassCulling(false);

            passData.compute = _tileMerging;


            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;


            passData.outputRectBuffer = _outputRectBuffer;
            passData.outputMaskBuffer = _outputMaskBuffer;
            passData.outputVisibilityBuffer = _outputVisibilityBuffer;
            passData.outputCountBuffer = _outputCountBuffer;
            passData.indirectArgsBuffer = _indirectArgsBuffer;

            passData.bboxCount = nprFrameData.bboxCount;

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
            {
                // should this be for each bit in presentstyles?
                int threadGroupsX = Mathf.CeilToInt(data.bboxCount / 64.0f);


            });
        }

        nprFrameData.bboxRectBuffer = _outputRectBuffer;
        nprFrameData.bboxMaskBuffer = _outputMaskBuffer;
        nprFrameData.bboxVisibilityBuffer = _outputVisibilityBuffer;
        nprFrameData.bboxCountBuffer = _outputCountBuffer;
        nprFrameData.bboxIndirectArgsBuffer = _indirectArgsBuffer;
    }

    public void Dispose()
    {
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