using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CpuGeneration : Prepass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    ComputeBuffer _bboxVisibilityBuffer;
    int _bboxVisibilityBufferCapacity = 0;
    uint[] _bboxVisibilityInitData;

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

    public CpuGeneration(): base("CpuGeneration")
    {

    }

    public CpuGeneration(int testCount, bool testModeEnabled) : base("CpuGeneration")
    {
        testStyleCount = testCount;
        _testModeEnabled = testModeEnabled;

    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if(NprTestingConfig.RenderMode != NprRenderMode.CPU)
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

        if (nprFrameData.bboxes == null)
            nprFrameData.bboxes = new List<BoundingBox>();
        else
            nprFrameData.bboxes.Clear();

        StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);

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
        nprFrameData.visibilityBuffer = null; 
        
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
        if (_bboxVisibilityBuffer != null)
        {
            _bboxVisibilityBuffer.Release();
            _bboxVisibilityBuffer = null;
        }
    }
}