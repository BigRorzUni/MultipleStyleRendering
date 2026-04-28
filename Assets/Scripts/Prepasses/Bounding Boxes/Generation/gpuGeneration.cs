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

    ComputeBuffer _bboxVisibilityBuffer;
    int _bboxVisibilityBufferCapacity = 0;
    uint[] _bboxVisibilityInitData;

    ComputeBuffer _bboxMaskBuffer;
    int _bboxMaskBufferCapacity = 0;
    uint[] _bboxMaskInitData;

    ComputeBuffer _bboxCountBuffer;
    ComputeBuffer _bboxIndirectArgsBuffer;

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

        bool gpuMode = NprTestingConfig.RenderMode == NprRenderMode.GPU;
        bool cpuMode = NprTestingConfig.RenderMode == NprRenderMode.CPU;
        bool fullscreenMode = false;

        if (!fullscreenMode)
        {
            if (nprFrameData.bboxes == null)
                nprFrameData.bboxes = new List<BoundingBox>();
            else
                nprFrameData.bboxes.Clear();

            StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);

            if (cpuMode)
            {
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

                        if (TryGetNearClippedScreenRect(renderer, camera, out RectInt screenRect))
                        {
                            BoundingBox bbox;
                            if (NprTestingConfig.TestMode)
                            {
                                bbox = BoundingBox.CreateTestBox(tag.currentTestEffects, screenRect);
                                nprFrameData.presentTestStyles |= tag.currentTestEffects;
                            }
                            else
                            {
                                bbox = new BoundingBox((uint)tag.imageEffects, screenRect);
                            }

                            nprFrameData.presentImageBits |= tag.imageEffects;

                            bbox.renderers.Add(renderer);
                            nprFrameData.bboxes.Add(bbox);
                        }
                    }
                }

                nprFrameData.bboxCount = nprFrameData.bboxes.Count;
            }
            else if (gpuMode)
            {
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
                NprFrameData.EnsureFixedBuffer(ref _bboxCountBuffer, 1, sizeof(uint));
                NprFrameData.EnsureFixedBuffer(ref _bboxIndirectArgsBuffer, 4, sizeof(uint), ComputeBufferType.IndirectArguments);

                if (_bboxMaskInitData == null || _bboxMaskInitData.Length < _bboxMaskBufferCapacity)
                    _bboxMaskInitData = new uint[_bboxMaskBufferCapacity];

                if (nprFrameData.bboxCount > 0)
                {
                    _bboxInputBuffer.SetData(gpuInputs);

                    for (int i = 0; i < nprFrameData.bboxCount; i++)
                        _bboxMaskInitData[i] = gpuInputs[i].mask;

                    _bboxMaskBuffer.SetData(_bboxMaskInitData, 0, 0, nprFrameData.bboxCount);

                    if (_gpuBboxGeneration == null)
                    {
                        Debug.LogError("bboxPrepass: bbox generation compute shader not assigned.");
                        return;
                    }

                    Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
                    Matrix4x4 projection = camera.projectionMatrix;
                    float nearZ = -camera.nearClipPlane;

                    uint[] countInit = new uint[1] { (uint)nprFrameData.bboxCount };
                    _bboxCountBuffer.SetData(countInit, 0, 0, 1);

                    uint[] argsInit = new uint[4] { 0u, 0u, 0u, 0u };
                    _bboxIndirectArgsBuffer.SetData(argsInit, 0, 0, 4);

                    // execute bbox generation through a compute pass
                    using (var builder = renderGraph.AddComputePass("GPU BBox Generation", out ComputePassData passData, profilingSampler))
                    {
                        builder.AllowPassCulling(false);

                        passData.compute = _gpuBboxGeneration;
                        passData.bboxGenerationKernel = _bboxGenerationKernel;
                        passData.buildDrawArgsKernel = _buildDrawArgsKernel;

                        passData.bboxInputBuffer = _bboxInputBuffer;
                        passData.bboxRectBuffer = _bboxRectBuffer;
                        passData.bboxIndirectArgsBuffer = _bboxIndirectArgsBuffer;

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
                    _bboxCountBuffer.SetData(countInit, 0, 0, 1);

                    uint[] argsInit = new uint[4] { 6u, 0u, 0u, 0u };
                    _bboxIndirectArgsBuffer.SetData(argsInit, 0, 0, 4);
                }

                nprFrameData.rectBuffer = _bboxRectBuffer;
                nprFrameData.maskBuffer = _bboxMaskBuffer;
                nprFrameData.countBuffer = _bboxCountBuffer;
                nprFrameData.indirectArgsBuffer = _bboxIndirectArgsBuffer;
            }
        }

        if (cpuMode)
        {
            if (nprFrameData.bboxCount > 0)
            {
                if (NprTestingConfig.UseOcclusion)
                {
                    NprFrameData.EnsureBufferCapacity(ref _bboxVisibilityBuffer, ref _bboxVisibilityBufferCapacity, nprFrameData.bboxCount, sizeof(uint));

                    if (_bboxVisibilityInitData == null || _bboxVisibilityInitData.Length < _bboxVisibilityBufferCapacity)
                        _bboxVisibilityInitData = new uint[_bboxVisibilityBufferCapacity];

                    for (int i = 0; i < nprFrameData.bboxCount; i++)
                        _bboxVisibilityInitData[i] = 1u;

                    if (_bboxVisibilityBuffer != null && _bboxVisibilityInitData != null)
                        _bboxVisibilityBuffer.SetData(_bboxVisibilityInitData, 0, 0, nprFrameData.bboxCount);

                    nprFrameData.visibilityBuffer = _bboxVisibilityBuffer;
                }
                else
                {
                    nprFrameData.visibilityBuffer = null;
                }
            }
            else
            {
                nprFrameData.visibilityBuffer = null;
            }
        }

        if (gpuMode)
        {
            NprFrameData.EnsureBufferCapacity(ref _bboxVisibilityBuffer, ref _bboxVisibilityBufferCapacity, nprFrameData.bboxCount, sizeof(uint));

            if (_bboxVisibilityInitData == null || _bboxVisibilityInitData.Length < _bboxVisibilityBufferCapacity)
                _bboxVisibilityInitData = new uint[_bboxVisibilityBufferCapacity];

            for (int i = 0; i < nprFrameData.bboxCount; i++)
                _bboxVisibilityInitData[i] = 1u;

            if (_bboxVisibilityBuffer != null && _bboxVisibilityInitData != null)
                _bboxVisibilityBuffer.SetData(_bboxVisibilityInitData, 0, 0, nprFrameData.bboxCount);

            nprFrameData.visibilityBuffer = _bboxVisibilityBuffer;

            // GpuDebugState.SetOutputBuffers(
            //     nprFrameData.rectBuffer,
            //     nprFrameData.maskBuffer,
            //     nprFrameData.visibilityBuffer,
            //     nprFrameData.countBuffer,
            //     nprFrameData.indirectArgsBuffer
            // );
        }
    }

    static readonly int[,] BoxEdges = new int[,]
    {
        {0,1}, {1,3}, {3,2}, {2,0},
        {4,5}, {5,7}, {7,6}, {6,4},
        {0,4}, {1,5}, {2,6}, {3,7}
    };

    static Vector3[] GetBoxCorners(Bounds b)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        return new Vector3[8]
        {
            c + new Vector3(-e.x, -e.y, -e.z),
            c + new Vector3(e.x, -e.y, -e.z),
            c + new Vector3(-e.x, e.y, -e.z),
            c + new Vector3(e.x, e.y, -e.z),
            c + new Vector3(-e.x, -e.y, e.z),
            c + new Vector3(e.x, -e.y, e.z),
            c + new Vector3(-e.x, e.y, e.z),
            c + new Vector3(e.x, e.y, e.z),
        };
    }

    public static bool TryGetNearClippedScreenRect(Renderer renderer, Camera camera, out RectInt rect)
    {
        rect = default;

        if (renderer == null || camera == null)
            return false;

        // world-space corners bounding box
        Vector3[] worldCorners = GetBoxCorners(renderer.bounds);

        // transform corners from world -> camera space
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        Vector3[] camCorners = new Vector3[8];
        for (int i = 0; i < 8; i++)
            camCorners[i] = worldToCamera.MultiplyPoint(worldCorners[i]);

        // store all corners that are past the near plane (clipped)
        float nearZ = -camera.nearClipPlane;
        const float eps = 1e-5f;

        List<Vector3> clippedCamPoints = new List<Vector3>();

        // keep corners that arent behind the near plane
        for (int i = 0; i < 8; i++)
        {
            if (camCorners[i].z <= nearZ + eps)
                clippedCamPoints.Add(camCorners[i]);
        }

        // clip each box edge against the near plane
        for (int i = 0; i < 12; i++)
        {
            // for each edge check if it crosses the near plane
            Vector3 a = camCorners[BoxEdges[i, 0]];
            Vector3 b = camCorners[BoxEdges[i, 1]];

            bool aIn = a.z <= nearZ + eps;
            bool bIn = b.z <= nearZ + eps;

            // one inside, one outside means that edge crosses near plane
            if (aIn != bIn)
            {
                // use the parametric form of the line to find the plane intersection
                float t = (nearZ - a.z) / (b.z - a.z);
                Vector3 point = Vector3.Lerp(a, b, t);
                clippedCamPoints.Add(point);
            }
        }

        // if no points are past the near plane then the box isn't visible
        if (clippedCamPoints.Count == 0)
            return false;

        // project valid points to screen space
        Matrix4x4 proj = camera.projectionMatrix;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        int valid = 0;

        for (int i = 0; i < clippedCamPoints.Count; i++)
        {
            // camera -> clip space
            Vector4 p = new Vector4(clippedCamPoints[i].x, clippedCamPoints[i].y, clippedCamPoints[i].z, 1.0f);
            Vector4 clip = proj * p;

            // reject invalid projections
            if (Mathf.Abs(clip.w) < eps)
                continue;

            // clip space -> ndc
            Vector3 ndc = new Vector3(clip.x, clip.y, clip.z) / clip.w;

            // ndc -> screen space
            float sx = (ndc.x * 0.5f + 0.5f) * camera.pixelWidth;
            float sy = (ndc.y * 0.5f + 0.5f) * camera.pixelHeight;

            // track min and max screen coords to get bounding rect
            valid++;
            minX = Mathf.Min(minX, sx);
            maxX = Mathf.Max(maxX, sx);
            minY = Mathf.Min(minY, sy);
            maxY = Mathf.Max(maxY, sy);
        }

        // if no valid projections then box isn't visible
        if (valid == 0)
            return false;

        // clamp final rect to screen (only clipping against near plane, so box could still be offscreen in other directions)
        minX = Mathf.Clamp(minX, 0f, camera.pixelWidth);
        maxX = Mathf.Clamp(maxX, 0f, camera.pixelWidth);
        minY = Mathf.Clamp(minY, 0f, camera.pixelHeight);
        maxY = Mathf.Clamp(maxY, 0f, camera.pixelHeight);

        int xMin = Mathf.FloorToInt(minX);
        int yMin = Mathf.FloorToInt(minY);
        int xMax = Mathf.CeilToInt(maxX);
        int yMax = Mathf.CeilToInt(maxY);

        int width = xMax - xMin;
        int height = yMax - yMin;

        if (width <= 0 || height <= 0)
            return false;

        rect = new RectInt(xMin, yMin, width, height);
        return true;
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

        if (_bboxVisibilityBuffer != null)
        {
            _bboxVisibilityBuffer.Release();
            _bboxVisibilityBuffer = null;
        }

        if (_bboxMaskBuffer != null)
        {
            _bboxMaskBuffer.Release();
            _bboxMaskBuffer = null;
        }

        if (_bboxCountBuffer != null)
        {
            _bboxCountBuffer.Release();
            _bboxCountBuffer = null;
        }

        if (_bboxIndirectArgsBuffer != null)
        {
            _bboxIndirectArgsBuffer.Release();
            _bboxIndirectArgsBuffer = null;
        }
    }
}