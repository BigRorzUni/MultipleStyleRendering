using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices;

[System.Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct BBoxGenerationInput // 32 bytes total for GPU
{
    public Vector3 center; // 12 bytes
    public float padding; // 4 bytes

    public Vector3 extents; // 12 bytes
    public uint mask; // 4 bytes
}

public class GpuGeneration : Prepass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    readonly ComputeShader _gpuBboxGeneration;
    readonly int _bboxGenerationKernel;
    readonly int _buildDrawArgsKernel;

    static readonly int BBoxInputBufferID = Shader.PropertyToID("_Inputs");
    static readonly int BBoxRectBufferID = Shader.PropertyToID("_Rects");
    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");
    static readonly int WorldToCameraID = Shader.PropertyToID("_WorldToCamera");
    static readonly int ProjectionID = Shader.PropertyToID("_Projection");
    static readonly int NearZID = Shader.PropertyToID("_NearZ");
    static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");

    static readonly int IndirectArgsBufferID = Shader.PropertyToID("_IndirectArgs");

    ComputeBuffer _bboxInputBuffer;
    int _bboxInputCapacity = 0;

    ComputeBuffer _bboxRectBuffer;
    int _bboxRectBufferCapacity = 0;

    ComputeBuffer _bboxMaskBuffer;
    int _bboxMaskBufferCapacity = 0;
    uint[] _bboxMaskInitData;

    ComputeBuffer _countBuffer;
    ComputeBuffer _indirectArgsBuffer;

    private class ComputePassData
    {
        public ComputeShader compute;
        public int bboxGenerationKernel;
        public int buildDrawArgsKernel;

        public ComputeBuffer bboxInputBuffer;
        public ComputeBuffer bboxRectBuffer;
        public ComputeBuffer bboxIndirectArgsBuffer;

        public int bboxCount;
        public Matrix4x4 worldToCamera;
        public Matrix4x4 projection;
        public Vector2 screenSize;
        public float nearZ;
    }

    public GpuGeneration(ComputeShader gpuBboxGeneration) : base("GpuGeneration")
    {
        if (gpuBboxGeneration != null)
        {
            _gpuBboxGeneration = gpuBboxGeneration;
            _bboxGenerationKernel = _gpuBboxGeneration.FindKernel("GenerateBboxes");
            _buildDrawArgsKernel = _gpuBboxGeneration.FindKernel("BuildDrawArgs");
        }
    }

    public GpuGeneration(ComputeShader bboxGeneration, int testCount, bool testModeEnabled) : base("GpuGeneration")
    {
        testStyleCount = testCount;
        _testModeEnabled = testModeEnabled;

        if (bboxGeneration != null)
        {
            _gpuBboxGeneration = bboxGeneration;
            _bboxGenerationKernel = _gpuBboxGeneration.FindKernel("GenerateBboxes");
            _buildDrawArgsKernel = _gpuBboxGeneration.FindKernel("BuildDrawArgs");
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if(NprTestingConfig.RenderMode == NprRenderMode.Fullscreen || NprTestingConfig.RenderMode == NprRenderMode.Tiling)
            return;
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
        Camera camera = cameraData.camera;

        if (camera == null)
            return;

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        nprFrameData.presentImageBits = 0;
        nprFrameData.presentTestStyles = 0;
        nprFrameData.countBuffer = null;
        nprFrameData.indirectArgsBuffer = null;

        if (nprFrameData.bboxes == null)
            nprFrameData.bboxes = new List<BoundingBox>();
        else
            nprFrameData.bboxes.Clear();

        StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);

        List<BBoxGenerationInput> gpuInputs = new List<BBoxGenerationInput>();

        foreach (var tag in tags)
        {
            GameObject obj = tag.gameObject;
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

            if (!NprTestingConfig.TestMode && tag.imageEffects == StyleBits.ImageSpaceEffect.None)
                continue;

            if (NprTestingConfig.TestMode && tag.currentTestEffects == 0)
                continue;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                uint mask;
                if (NprTestingConfig.TestMode)
                {
                    mask = tag.currentTestEffects;
                    nprFrameData.presentTestStyles |= tag.currentTestEffects;
                }
                else
                {
                    mask = (uint)tag.imageEffects;
                }

                nprFrameData.presentImageBits |= tag.imageEffects;

                Bounds b = renderer.bounds;

                gpuInputs.Add(new BBoxGenerationInput
                {
                    center = b.center,
                    extents = b.extents,
                    mask = mask
                });
            }
        }

        nprFrameData.bboxCount = gpuInputs.Count;

        NprFrameData.EnsureBufferCapacity(ref _bboxInputBuffer, ref _bboxInputCapacity, nprFrameData.bboxCount, Marshal.SizeOf<BBoxGenerationInput>());
        NprFrameData.EnsureBufferCapacity(ref _bboxRectBuffer, ref _bboxRectBufferCapacity, nprFrameData.bboxCount, Marshal.SizeOf<Vector4>());
        NprFrameData.EnsureBufferCapacity(ref _bboxMaskBuffer, ref _bboxMaskBufferCapacity, nprFrameData.bboxCount, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _countBuffer, 1, sizeof(uint));
        NprFrameData.EnsureFixedBuffer(ref _indirectArgsBuffer, 4, sizeof(uint), ComputeBufferType.IndirectArguments);

        nprFrameData.rectBuffer = _bboxRectBuffer;
        nprFrameData.maskBuffer = _bboxMaskBuffer;
        nprFrameData.countBuffer = _countBuffer;
        nprFrameData.indirectArgsBuffer = _indirectArgsBuffer;

        if (_bboxMaskInitData == null || _bboxMaskInitData.Length < _bboxMaskBufferCapacity)
            _bboxMaskInitData = new uint[_bboxMaskBufferCapacity];

        if (nprFrameData.bboxCount > 0)
        {
            _bboxInputBuffer.SetData(gpuInputs);

            for (int i = 0; i < nprFrameData.bboxCount; i++)
                _bboxMaskInitData[i] = gpuInputs[i].mask;

            nprFrameData.maskBuffer.SetData(_bboxMaskInitData, 0, 0, nprFrameData.bboxCount);

            if (_gpuBboxGeneration == null)
            {
                Debug.LogError("bbox generation compute shader not assigned.");
                return;
            }

            Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
            Matrix4x4 projection = camera.projectionMatrix;
            float nearZ = -camera.nearClipPlane;

            uint[] countInit = new uint[1] { (uint)nprFrameData.bboxCount };
            nprFrameData.countBuffer.SetData(countInit, 0, 0, 1);

            uint[] argsInit = new uint[4] { 0u, 0u, 0u, 0u };
            nprFrameData.indirectArgsBuffer.SetData(argsInit, 0, 0, 4);

            // execute bbox generation through a compute pass
            using (var builder = renderGraph.AddComputePass("GPU BBox Generation", out ComputePassData passData, profilingSampler))
            {
                builder.AllowPassCulling(false);

                passData.compute = _gpuBboxGeneration;
                passData.bboxGenerationKernel = _bboxGenerationKernel;
                passData.buildDrawArgsKernel = _buildDrawArgsKernel;

                passData.bboxInputBuffer = _bboxInputBuffer;
                passData.bboxRectBuffer = nprFrameData.rectBuffer;
                passData.bboxIndirectArgsBuffer = nprFrameData.indirectArgsBuffer;

                passData.bboxCount = nprFrameData.bboxCount;
                passData.worldToCamera = worldToCamera;
                passData.projection = projection;
                passData.screenSize = new Vector2(camera.pixelWidth, camera.pixelHeight);
                passData.nearZ = nearZ;

                builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
                {
                    int threadGroupsX = Mathf.CeilToInt(data.bboxCount / 64.0f);

                    ctx.cmd.SetComputeBufferParam(data.compute, data.bboxGenerationKernel, BBoxInputBufferID, data.bboxInputBuffer);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.bboxGenerationKernel, BBoxRectBufferID, data.bboxRectBuffer);
                    ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                    ctx.cmd.SetComputeMatrixParam(data.compute, WorldToCameraID, data.worldToCamera);
                    ctx.cmd.SetComputeMatrixParam(data.compute, ProjectionID, data.projection);
                    ctx.cmd.SetComputeVectorParam(data.compute, ScreenSizeID, data.screenSize);
                    ctx.cmd.SetComputeFloatParam(data.compute, NearZID, data.nearZ);
                    ctx.cmd.DispatchCompute(data.compute, data.bboxGenerationKernel, threadGroupsX, 1, 1);

                    ctx.cmd.SetComputeBufferParam(data.compute, data.buildDrawArgsKernel, IndirectArgsBufferID, data.bboxIndirectArgsBuffer);
                    ctx.cmd.DispatchCompute(data.compute, data.buildDrawArgsKernel, 1, 1, 1);
                });
            }
        }
        else
        {
            uint[] countInit = new uint[1] { 0u };
            nprFrameData.countBuffer.SetData(countInit, 0, 0, 1);

            uint[] argsInit = new uint[4] { 6u, 0u, 0u, 0u };
            nprFrameData.indirectArgsBuffer.SetData(argsInit, 0, 0, 4);
        }        

        // GpuDebugState.SetOutputBuffers(
        //     nprFrameData.rectBuffer,
        //     nprFrameData.maskBuffer,
        //     nprFrameData.visibilityBuffer,
        //     nprFrameData.countBuffer,
        //     nprFrameData.indirectArgsBuffer
        // );
    }

    public override void Dispose()
    {
        if (_bboxInputBuffer != null)
        {
            _bboxInputBuffer.Release();
            _bboxInputBuffer = null;
        }

        if (_bboxRectBuffer != null)
        {
            _bboxRectBuffer.Release();
            _bboxRectBuffer = null;
        }

        if (_bboxMaskBuffer != null)
        {
            _bboxMaskBuffer.Release();
            _bboxMaskBuffer = null;
        }

        if (_countBuffer != null)
        {
            _countBuffer.Release();
            _countBuffer = null;
        }

        if (_indirectArgsBuffer != null)
        {
            _indirectArgsBuffer.Release();
            _indirectArgsBuffer = null;
        }

        _bboxInputCapacity = 0;
        _bboxRectBufferCapacity = 0;
        _bboxMaskBufferCapacity = 0;
    }
}