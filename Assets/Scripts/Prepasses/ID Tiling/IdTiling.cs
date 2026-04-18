using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class IdTiling : Prepass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    readonly ComputeShader _tileGeneration;
    readonly int _tileGenerationKernel;
    readonly int _buildDrawArgsKernel;

    readonly int _tileSize;

    static readonly int IdTexID = Shader.PropertyToID("_IdTexture");
    static readonly int TileRectBufferID = Shader.PropertyToID("_TileRectBuffer");
    static readonly int TileMaskBufferID = Shader.PropertyToID("_TileMaskBuffer");
    static readonly int TileCountBufferID = Shader.PropertyToID("_TileCountBuffer");
    static readonly int IndirectArgsBufferID = Shader.PropertyToID("_IndirectArgs");
    static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");
    static readonly int TileSizeID = Shader.PropertyToID("_TileSize");
    static readonly int TileGridSizeID = Shader.PropertyToID("_TileGridSize");

    ComputeBuffer _tileRectBuffer;
    int _tileRectBufferCapacity = 0;

    ComputeBuffer _tileMaskBuffer;
    int _tileMaskBufferCapacity = 0;

    ComputeBuffer _tileCountBuffer;
    ComputeBuffer _tileIndirectArgsBuffer;

    private class ComputePassData
    {
        public ComputeShader compute;
        public int tileGenerationKernel;
        public int buildDrawArgsKernel;

        public TextureHandle idTexture;

        public ComputeBuffer tileRectBuffer;
        public ComputeBuffer tileMaskBuffer;
        public ComputeBuffer tileCountBuffer;
        public ComputeBuffer indirectArgsBuffer;

        public Vector2Int screenSize;
        public Vector2Int tileGridSize;
        public int tileSize;
    }

    public IdTiling(ComputeShader tileGeneration, int tileSize = 16) : base("IdTiling")
    {
        _tileGeneration = tileGeneration;
        _tileSize = Mathf.Max(1, tileSize);

        if (_tileGeneration != null)
        {
            _tileGenerationKernel = _tileGeneration.FindKernel("GenerateTiles");
            _buildDrawArgsKernel = _tileGeneration.FindKernel("BuildDrawArgs");
        }
    }

    public IdTiling(ComputeShader tileGeneration, int testCount, bool testModeEnabled, int tileSize = 16) : base("IdTiling")
    {
        testStyleCount = testCount;
        _testModeEnabled = testModeEnabled;
        _tileGeneration = tileGeneration;
        _tileSize = Mathf.Max(1, tileSize);

        if (_tileGeneration != null)
        {
            _tileGenerationKernel = _tileGeneration.FindKernel("GenerateTiles");
            _buildDrawArgsKernel = _tileGeneration.FindKernel("BuildDrawArgs");
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_tileGeneration == null)
            return;

        if (NprTestingConfig.RenderMode != NprRenderMode.Tiling)
            return;

        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        nprFrameData.presentImageBits = 0;
        nprFrameData.presentTestStyles = 0;

        if (!nprFrameData.idTexture.IsValid())
            return;

        StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
        foreach (var tag in tags)
        {
            if (tag == null)
                continue;

            nprFrameData.presentImageBits |= tag.imageEffects;

            if (NprTestingConfig.TestMode)
                nprFrameData.presentTestStyles |= tag.currentTestEffects;
        }

        int screenWidth = cameraData.cameraTargetDescriptor.width;
        int screenHeight = cameraData.cameraTargetDescriptor.height;

        int tileCountX = Mathf.CeilToInt(screenWidth / (float)_tileSize);
        int tileCountY = Mathf.CeilToInt(screenHeight / (float)_tileSize);
        int maxTileCount = tileCountX * tileCountY;

        if (maxTileCount <= 0)
            return;

        NprFrameData.EnsureBufferCapacity(ref _tileRectBuffer, ref _tileRectBufferCapacity, maxTileCount, sizeof(float) * 4);
        NprFrameData.EnsureBufferCapacity(ref _tileMaskBuffer, ref _tileMaskBufferCapacity, maxTileCount, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _tileCountBuffer, 1, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _tileIndirectArgsBuffer, 4, sizeof(uint), ComputeBufferType.IndirectArguments);

        uint[] countInit = new uint[1] { 0u };
        _tileCountBuffer.SetData(countInit, 0, 0, 1);

        uint[] argsInit = new uint[4] { 6u, 0u, 0u, 0u };
        _tileIndirectArgsBuffer.SetData(argsInit, 0, 0, 4);

        using (var builder = renderGraph.AddComputePass("ID Tile Generation", out ComputePassData passData, profilingSampler))
        {
            builder.AllowPassCulling(false);

            passData.compute = _tileGeneration;
            passData.tileGenerationKernel = _tileGenerationKernel;
            passData.buildDrawArgsKernel = _buildDrawArgsKernel;

            passData.idTexture = nprFrameData.idTexture;

            passData.tileRectBuffer = _tileRectBuffer;
            passData.tileMaskBuffer = _tileMaskBuffer;
            passData.tileCountBuffer = _tileCountBuffer;
            passData.indirectArgsBuffer = _tileIndirectArgsBuffer;

            passData.screenSize = new Vector2Int(screenWidth, screenHeight);
            passData.tileGridSize = new Vector2Int(tileCountX, tileCountY);
            passData.tileSize = _tileSize;

            builder.UseTexture(passData.idTexture, AccessFlags.Read);

            builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
            {
                ctx.cmd.SetComputeTextureParam(data.compute, data.tileGenerationKernel, IdTexID, data.idTexture);
                ctx.cmd.SetComputeBufferParam(data.compute, data.tileGenerationKernel, TileRectBufferID, data.tileRectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.tileGenerationKernel, TileMaskBufferID, data.tileMaskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.tileGenerationKernel, TileCountBufferID, data.tileCountBuffer);
                ctx.cmd.SetComputeVectorParam(data.compute, ScreenSizeID, new Vector4(data.screenSize.x, data.screenSize.y, 0f, 0f));
                ctx.cmd.SetComputeVectorParam(data.compute, TileGridSizeID, new Vector4(data.tileGridSize.x, data.tileGridSize.y, 0f, 0f));
                ctx.cmd.SetComputeIntParam(data.compute, TileSizeID, data.tileSize);

                ctx.cmd.DispatchCompute(
                    data.compute,
                    data.tileGenerationKernel,
                    data.tileGridSize.x,
                    data.tileGridSize.y,
                    1
                );

                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, TileCountBufferID, data.tileCountBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, IndirectArgsBufferID, data.indirectArgsBuffer);
                ctx.cmd.DispatchCompute(data.compute, data.buildDrawArgsKernel, 1, 1, 1);
            });
        }

        nprFrameData.rectBuffer = _tileRectBuffer;
        nprFrameData.maskBuffer = _tileMaskBuffer;
        nprFrameData.countBuffer = _tileCountBuffer;
        nprFrameData.indirectArgsBuffer = _tileIndirectArgsBuffer;

        nprFrameData.visibilityBuffer = null;
        nprFrameData.bboxVisibilityCount = 0;

        nprFrameData.bboxCount = maxTileCount;

        GpuDebugState.SetTileBuffers(_tileMaskBuffer, tileCountX, tileCountY, _tileSize, maxTileCount);
    }

    public override void Dispose()
    {
        if (_tileRectBuffer != null)
        {
            _tileRectBuffer.Release();
            _tileRectBuffer = null;
        }

        if (_tileMaskBuffer != null)
        {
            _tileMaskBuffer.Release();
            _tileMaskBuffer = null;
        }

        if (_tileCountBuffer != null)
        {
            _tileCountBuffer.Release();
            _tileCountBuffer = null;
        }

        if (_tileIndirectArgsBuffer != null)
        {
            _tileIndirectArgsBuffer.Release();
            _tileIndirectArgsBuffer = null;
        }
    }
}