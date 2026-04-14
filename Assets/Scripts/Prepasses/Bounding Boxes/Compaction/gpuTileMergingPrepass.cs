using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices;

[System.Serializable]
public class GpuTileMergingPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    readonly ComputeShader _tileMerging;
    readonly int _rasteriseTilesKernel;
    readonly int _emitTilesKernel;
    readonly int _buildDrawArgsKernel;


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

    public GpuTileMergingPrepass(ComputeShader tileMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        if (tileMerging != null)
        {
            _tileMerging = tileMerging;
            _rasteriseTilesKernel = _tileMerging.FindKernel("RasteriseRectsToTiles");
            _emitTilesKernel = _tileMerging.FindKernel("EmitTilesToRects");
            _buildDrawArgsKernel = _tileMerging.FindKernel("BuildDrawArgs");
        }
    }

    private class ComputePassData
    {
        public ComputeShader compute;
        public int tilesKernel;
        public int emitTilesKernel;
        public int buildDrawArgsKernel;

        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeBuffer visibilityBuffer;

        public ComputeBuffer tileMaskBuffer;

        public ComputeBuffer outputRectBuffer;
        public ComputeBuffer outputMaskBuffer;
        public ComputeBuffer outputVisibilityBuffer;
        public ComputeBuffer outputCountBuffer;
        public ComputeBuffer indirectArgsBuffer;

        public int bboxCount;
        public Vector2Int tileGridSize;
        public int tileSize;
        public Vector2 screenSize;
        public int tileCount;
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

        NprFrameData.EnsureBufferCapacity(ref _tileMaskBuffer, ref _tileMaskBufferCapacity, tileCount, sizeof(uint));
        NprFrameData.EnsureBufferCapacity(ref _outputRectBuffer, ref _outputRectBufferCapacity, tileCount, Marshal.SizeOf<Vector4>()); 
        NprFrameData.EnsureBufferCapacity(ref _outputMaskBuffer, ref _outputMaskBufferCapacity, tileCount, sizeof(uint));
        NprFrameData.EnsureBufferCapacity(ref _outputVisibilityBuffer, ref _outputVisibilityBufferCapacity, tileCount, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _outputCountBuffer, 1, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _indirectArgsBuffer, 4, sizeof(uint), ComputeBufferType.IndirectArguments);

        uint[] tileMaskInit = new uint[_tileMaskBufferCapacity];
        _tileMaskBuffer.SetData(tileMaskInit);

        uint[] outputCountInit = new uint[1] { 0u };
        _outputCountBuffer.SetData(outputCountInit, 0, 0, 1);

        uint[] indirectArgsInit = new uint[4] { 6u, 0u, 0u, 0u };
        _indirectArgsBuffer.SetData(indirectArgsInit, 0, 0, 4);

        using (var builder = renderGraph.AddComputePass("GPU Tile Merging", out ComputePassData passData))
        {
            builder.AllowPassCulling(false);

            passData.compute = _tileMerging;
            passData.tilesKernel = _rasteriseTilesKernel;
            passData.emitTilesKernel = _emitTilesKernel;
            passData.buildDrawArgsKernel = _buildDrawArgsKernel;

            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;

            passData.tileMaskBuffer = _tileMaskBuffer;

            passData.outputRectBuffer = _outputRectBuffer;
            passData.outputMaskBuffer = _outputMaskBuffer;
            passData.outputVisibilityBuffer = _outputVisibilityBuffer;
            passData.outputCountBuffer = _outputCountBuffer;
            passData.indirectArgsBuffer = _indirectArgsBuffer;

            passData.bboxCount = nprFrameData.bboxCount;
            passData.tileGridSize = new Vector2Int(tilesX, tilesY);
            passData.tileSize = _tileSize;
            passData.screenSize = new Vector2(camDesc.width, camDesc.height);
            passData.tileCount = tileCount;

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
            {
                int bboxThreadGroupsX = Mathf.CeilToInt(data.bboxCount / 64.0f);
                int tileThreadGroupsX = Mathf.CeilToInt(data.tileCount / 64.0f);


                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, RectBufferID, data.rectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, MaskBufferID, data.maskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, VisibilityBufferID, data.visibilityBuffer);

                ctx.cmd.SetComputeBufferParam(data.compute, data.tilesKernel, TileMaskBufferID, data.tileMaskBuffer);

                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                ctx.cmd.SetComputeVectorParam(data.compute, TileGridSizeID, new Vector4(data.tileGridSize.x, data.tileGridSize.y, 0, 0));
                ctx.cmd.SetComputeIntParam(data.compute, TileSizeID, data.tileSize);
                ctx.cmd.SetComputeVectorParam(data.compute, ScreenSizeID, new Vector4(data.screenSize.x, data.screenSize.y, 0, 0));

                ctx.cmd.DispatchCompute(data.compute, data.tilesKernel, bboxThreadGroupsX, 1, 1);

                ctx.cmd.SetComputeBufferParam(data.compute, data.emitTilesKernel, TileMaskBufferID, data.tileMaskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitTilesKernel, OutputRectBufferID, data.outputRectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitTilesKernel, OutputMaskBufferID, data.outputMaskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitTilesKernel, OutputVisibilityBufferID, data.outputVisibilityBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.emitTilesKernel, OutputCountBufferID, data.outputCountBuffer);

                ctx.cmd.SetComputeIntParam(data.compute, TileSizeID, data.tileSize);
                ctx.cmd.SetComputeVectorParam(data.compute, TileGridSizeID, new Vector4(data.tileGridSize.x, data.tileGridSize.y, 0, 0));
                ctx.cmd.SetComputeVectorParam(data.compute, ScreenSizeID, new Vector4(data.screenSize.x, data.screenSize.y, 0, 0));

                ctx.cmd.DispatchCompute(data.compute, data.emitTilesKernel, tileThreadGroupsX, 1, 1);

                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, OutputCountBufferID, data.outputCountBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, IndirectArgsBufferID, data.indirectArgsBuffer);
                ctx.cmd.DispatchCompute(data.compute, data.buildDrawArgsKernel, 1, 1, 1);
            });
        }

        nprFrameData.bboxRectBuffer = _outputRectBuffer;
        nprFrameData.bboxMaskBuffer = _outputMaskBuffer;
        nprFrameData.bboxVisibilityBuffer = _outputVisibilityBuffer;
        nprFrameData.bboxCountBuffer = _outputCountBuffer;
        nprFrameData.bboxIndirectArgsBuffer = _indirectArgsBuffer;

        GpuDebugState.SetTileBuffers(_tileMaskBuffer, tilesX, tilesY, _tileSize, nprFrameData.bboxCount);
        GpuDebugState.SetOutputBuffers(_outputRectBuffer, _outputMaskBuffer, _outputVisibilityBuffer, _outputCountBuffer, _indirectArgsBuffer);
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