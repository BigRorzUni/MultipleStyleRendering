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
    readonly int _tilesKernel;

    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int MaskBufferID = Shader.PropertyToID("_Masks");
    static readonly int VisibilityBufferID = Shader.PropertyToID("_Visibility");

    static readonly int TileMaskBufferID = Shader.PropertyToID("_TileMasks");
    static readonly int TileGridSizeID = Shader.PropertyToID("_TileGridSize");
    static readonly int TileSizeID = Shader.PropertyToID("_TileSize");
    static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");

    static readonly int OutputRectBufferID = Shader.PropertyToID("_OutputRects");
    static readonly int OutputMaskBufferID = Shader.PropertyToID("_OutputMasks");
    static readonly int OutputVisibilityBufferID = Shader.PropertyToID("_OutputVisibility");
    static readonly int OutputCountBufferID = Shader.PropertyToID("_OutputCount");
    static readonly int IndirectArgsBufferID = Shader.PropertyToID("_IndirectArgs");

    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");

    const int _tileSize = 16;

    ComputeBuffer _tileMaskBuffer;
    int _tileMaskBufferCapacity = 0;

    ComputeBuffer _outputRectBuffer;
    int _outputRectBufferCapacity = 0;

    ComputeBuffer _outputMaskBuffer;
    int _outputMaskBufferCapacity = 0;

    ComputeBuffer _outputVisibilityBuffer;
    int _outputVisibilityBufferCapacity = 0;

    ComputeBuffer _outputCountBuffer;
    ComputeBuffer _indirectArgsBuffer;

    void EnsureTileMaskBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_tileMaskBuffer == null || _tileMaskBufferCapacity < requiredCapacity)
        {
            if (_tileMaskBuffer != null)
                _tileMaskBuffer.Release();

            _tileMaskBufferCapacity = requiredCapacity;
            _tileMaskBuffer = new ComputeBuffer(_tileMaskBufferCapacity, sizeof(uint));
        }
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

    public GpuTileMergingPrepass(ComputeShader tileMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        if (tileMerging != null)
        {
            _tileMerging = tileMerging;
            _tilesKernel = _tileMerging.FindKernel("RasteriseRectsToTiles");
        }
    }

    private class ComputePassData
    {
        public ComputeShader compute;
        public int tilesKernel;

        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeBuffer visibilityBuffer;

        public ComputeBuffer tileMaskBuffer;

        public ComputeBuffer outputRectBuffer;
        public ComputeBuffer outputMaskBuffer;
        public ComputeBuffer outputVisibilityBuffer;
        public ComputeBuffer outputCountBuffer;
        public ComputeBuffer indirectArgsBuffer;

        public Vector2Int tileGridSize;
        public int tileSize;
        public Vector2 screenSize;

        public int bboxCount;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

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

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        int tilesX = Mathf.CeilToInt(camDesc.width / (float)_tileSize);
        int tilesY = Mathf.CeilToInt(camDesc.height / (float)_tileSize);
        int tileCount = tilesX * tilesY;

        EnsureTileMaskBufferCapacity(tileCount);

        uint[] tileMaskInit = new uint[_tileMaskBufferCapacity];
        _tileMaskBuffer.SetData(tileMaskInit);

        using (var builder = renderGraph.AddComputePass("GPU Tile Rasterisation", out ComputePassData passData))
        {
            builder.AllowPassCulling(false);

            passData.compute = _tileMerging;
            passData.tilesKernel = _tilesKernel;

            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;

            passData.tileMaskBuffer = _tileMaskBuffer;

            passData.bboxCount = nprFrameData.bboxCount;
            passData.tileGridSize = new Vector2Int(tilesX, tilesY);
            passData.tileSize = _tileSize;
            passData.screenSize = new Vector2(camDesc.width, camDesc.height);

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
            {
                int threadGroupsX = Mathf.CeilToInt(data.bboxCount / 64.0f);

                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, RectBufferID, data.rectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, MaskBufferID, data.maskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, VisibilityBufferID, data.visibilityBuffer);

                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, TileMaskBufferID, data.tileMaskBuffer);

                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                ctx.cmd.SetComputeVectorParam(data.compute, TileGridSizeID, new Vector4(data.tileGridSize.x, data.tileGridSize.y, 0, 0));
                ctx.cmd.SetComputeIntParam(data.compute, TileSizeID, data.tileSize);
                ctx.cmd.SetComputeVectorParam(data.compute, ScreenSizeID, new Vector4(data.screenSize.x, data.screenSize.y, 0, 0));

                ctx.cmd.DispatchCompute(data.compute, data.tilesKernel, threadGroupsX, 1, 1);
            });
        }

        NprGpuTileDebugState.SetBuffers(_tileMaskBuffer, tilesX, tilesY, _tileSize, nprFrameData.bboxCount);
    }
    
    public void Dispose()
    {
        if (_tileMaskBuffer != null)
            _tileMaskBuffer.Release();

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