using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CpuGeneration : Prepass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    static readonly Vector3[] _worldCorners = new Vector3[8];
    static readonly Vector3[] _camCorners = new Vector3[8];
    static readonly List<Vector3> _clippedCamPoints = new List<Vector3>(16);

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
        if(NprConfig.RenderMode != NprRenderMode.CPU)
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
        nprFrameData.visibilityBuffer = null; 

        nprFrameData.bboxes.Clear();

        foreach (var tag in StylisedTag.ActiveTags)
        {
            if (tag == null)
                continue;

            GameObject obj = tag.gameObject;
            Renderer[] renderers = tag.Renderers;

            if (!NprConfig.TestMode && tag.imageEffects == StyleBits.ImageSpaceEffect.None)
                continue;

            if (NprConfig.TestMode && tag.currentTestEffects == 0)
                continue;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (TryGetNearClippedScreenRect(renderer, camera, out RectInt screenRect))
                {
                    BoundingBox bbox;
                    if (NprConfig.TestMode)
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

    static readonly int[,] BoxEdges = new int[,]
    {
        {0,1}, {1,3}, {3,2}, {2,0},
        {4,5}, {5,7}, {7,6}, {6,4},
        {0,4}, {1,5}, {2,6}, {3,7}
    };

    static void GetBoxCorners(Bounds b, Vector3[] corners)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        corners[0] = c + new Vector3(-e.x, -e.y, -e.z);
        corners[1] = c + new Vector3(e.x, -e.y, -e.z);
        corners[2] = c + new Vector3(-e.x, e.y, -e.z);
        corners[3] = c + new Vector3(e.x, e.y, -e.z);
        corners[4] = c + new Vector3(-e.x, -e.y, e.z);
        corners[5] = c + new Vector3(e.x, -e.y, e.z);
        corners[6] = c + new Vector3(-e.x, e.y, e.z);
        corners[7] = c + new Vector3(e.x, e.y, e.z);
    }

    public static bool TryGetNearClippedScreenRect(Renderer renderer, Camera camera, out RectInt rect)
    {
        rect = default;

        if (renderer == null || camera == null)
            return false;

        // world-space corners bounding box
        GetBoxCorners(renderer.bounds, _worldCorners);

        // transform corners from world -> camera space
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        for (int i = 0; i < 8; i++)
            _camCorners[i] = worldToCamera.MultiplyPoint(_worldCorners[i]);

        // store all corners that are past the near plane (clipped)
        float nearZ = -camera.nearClipPlane;
        const float eps = 1e-5f;

        _clippedCamPoints.Clear();

        // keep corners that arent behind the near plane
        for (int i = 0; i < 8; i++)
        {
            if (_camCorners[i].z <= nearZ + eps)
                _clippedCamPoints.Add(_camCorners[i]);
        }

        // clip each box edge against the near plane
        for (int i = 0; i < 12; i++)
        {
            // for each edge check if it crosses the near plane
            Vector3 a = _camCorners[BoxEdges[i, 0]];
            Vector3 b = _camCorners[BoxEdges[i, 1]];

            bool aIn = a.z <= nearZ + eps;
            bool bIn = b.z <= nearZ + eps;

            // one inside, one outside means that edge crosses near plane
            if (aIn != bIn)
            {
                // use the parametric form of the line to find the plane intersection
                float t = (nearZ - a.z) / (b.z - a.z);
                Vector3 point = Vector3.Lerp(a, b, t);
                _clippedCamPoints.Add(point);
            }
        }

        // if no points are past the near plane then the box isn't visible
        if (_clippedCamPoints.Count == 0)
            return false;

        // project valid points to screen space
        Matrix4x4 proj = camera.projectionMatrix;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        int valid = 0;

        for (int i = 0; i < _clippedCamPoints.Count; i++)
        {
            // camera -> clip space
            Vector4 p = new Vector4(_clippedCamPoints[i].x, _clippedCamPoints[i].y, _clippedCamPoints[i].z, 1.0f);
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
    }
}