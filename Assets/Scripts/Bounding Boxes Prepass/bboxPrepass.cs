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
    public float padding1; // 4 bytes

    public Vector3 extents; // 12 bytes
    public uint mask; // 4 bytes byte

}

public class bboxPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    // bbox compute shader
    readonly ComputeShader _bboxGeneration;
    readonly int _bboxGenerationKernel;

    static readonly int BBoxInputBufferID = Shader.PropertyToID("_Inputs");
    static readonly int BBoxRectBufferID = Shader.PropertyToID("_Rects");
    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");
    static readonly int WorldToCameraID = Shader.PropertyToID("_WorldToCamera");
    static readonly int ProjectionID = Shader.PropertyToID("_Projection");
    static readonly int NearZID = Shader.PropertyToID("_NearZ");
    static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");

    // input to gpu bbox generation
    ComputeBuffer _bboxInputBuffer;
    int _bboxInputCapacity = 0;

    void EnsureInputBufferCapacity(int count)
    {
        int required = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_bboxInputBuffer == null || _bboxInputCapacity < required)
        {
            if (_bboxInputBuffer != null)
                _bboxInputBuffer.Release();

            _bboxInputCapacity = required;
            _bboxInputBuffer = new ComputeBuffer(
                _bboxInputCapacity,
                Marshal.SizeOf<BBoxGenerationInput>()
            );
        }
    }

    // bounding box rect buffer
    private ComputeBuffer _bboxRectBuffer;
    private int _bboxRectBufferCapacity = 0;
    private QuadInstanceData[] _bboxRectInitData;

    void EnsureRectBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_bboxRectBuffer == null || _bboxRectBufferCapacity < requiredCapacity)
        {
            if (_bboxRectBuffer != null)
                _bboxRectBuffer.Release();

            _bboxRectBufferCapacity = requiredCapacity;
            _bboxRectBuffer = new ComputeBuffer(_bboxRectBufferCapacity, System.Runtime.InteropServices.Marshal.SizeOf<QuadInstanceData>());
        }

        if (_bboxRectInitData == null || _bboxRectInitData.Length < _bboxRectBufferCapacity)
            _bboxRectInitData = new QuadInstanceData[_bboxRectBufferCapacity];
    }

    // bounding box visibility buffer 
    private ComputeBuffer _bboxVisibilityBuffer;
    private int _bboxVisibilityBufferCapacity = 0;
    private uint[] _bboxVisibilityInitData;

    void EnsureVisibilityBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_bboxVisibilityBuffer == null || _bboxVisibilityBufferCapacity < requiredCapacity)
        {
            if (_bboxVisibilityBuffer != null)
                _bboxVisibilityBuffer.Release();

            _bboxVisibilityBufferCapacity = requiredCapacity;
            _bboxVisibilityBuffer = new ComputeBuffer(_bboxVisibilityBufferCapacity, sizeof(uint));
        }

        if (_bboxVisibilityInitData == null || _bboxVisibilityInitData.Length < _bboxVisibilityBufferCapacity)
            _bboxVisibilityInitData = new uint[_bboxVisibilityBufferCapacity];
    }

    // bounding box masks buffer
    private ComputeBuffer _bboxMaskBuffer;
    private int _bboxMaskBufferCapacity = 0;
    private uint[] _bboxMaskInitData;

    void EnsureMaskBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_bboxMaskBuffer == null || _bboxMaskBufferCapacity < requiredCapacity)
        {
            if (_bboxMaskBuffer != null)
                _bboxMaskBuffer.Release();

            _bboxMaskBufferCapacity = requiredCapacity;
            _bboxMaskBuffer = new ComputeBuffer(_bboxMaskBufferCapacity, sizeof(uint));
        }

        if (_bboxMaskInitData == null || _bboxMaskInitData.Length < _bboxMaskBufferCapacity)
            _bboxMaskInitData = new uint[_bboxMaskBufferCapacity];
    }



    class PassData
    {
        public TextureHandle src;
        public TextureHandle dst;
        public RectInt rect;
        public Vector2 srcTexelSize;
    }

    public bboxPrepass(ComputeShader bboxGeneration)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        if(bboxGeneration != null)
        {
            _bboxGeneration = bboxGeneration;
            _bboxGenerationKernel = _bboxGeneration.FindKernel("GenerateBboxes");
        }
    }

    public bboxPrepass(ComputeShader bboxGeneration, int testCount, bool testModeEnabled)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        testStyleCount = testCount;
        _testModeEnabled = testModeEnabled;

        if(bboxGeneration != null)
        {
            _bboxGeneration = bboxGeneration;
            _bboxGenerationKernel = _bboxGeneration.FindKernel("GenerateBboxes");
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
        Camera camera = cameraData.camera; // extend to multiple cameras?

        if(camera == null)
            return;
            
        // get/create NPR frame data
        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        nprFrameData.presentImageBits = 0;
        nprFrameData.presentTestStyles = 0;

        if(NprTestingConfig.UseBoundingBoxes)
        {
            if (nprFrameData.bboxes == null) 
                nprFrameData.bboxes = new List<BoundingBox>();
            else 
                nprFrameData.bboxes.Clear();

            if(NprTestingConfig.UseOcclusionCulling && !NprTestingConfig.BatchedOcclusion)
            {
                if (nprFrameData.occlusionCandidateBoxes == null) 
                    nprFrameData.occlusionCandidateBoxes = new List<BoundingBox>();
                else 
                    nprFrameData.occlusionCandidateBoxes.Clear();
            }

            // get all active tagged objects using the attached StylisedTag component
            StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
            if(!(NprTestingConfig.BatchedBboxGeneration && NprTestingConfig.BatchedDraws))
            {
                foreach (var tag in tags)
                {
                    GameObject obj = tag.gameObject;
                    Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

                    foreach(Renderer renderer in renderers)
                    {
                        // check that the object wants an image effect applied and is visible, otherwise skip
                        if(renderer == null || (renderer.renderingLayerMask & StyleBits.ImageSpaceBit) == 0)
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

                            // image effects are still tracked separately
                            nprFrameData.presentImageBits |= tag.imageEffects;

                            bbox.renderers.Add(renderer); 

                            nprFrameData.bboxes.Add(bbox);
                        }
                    }
                }
            }
            else
            {
                // gather cpu bounds and masks
                List<BBoxGenerationInput> gpuInputs = new List<BBoxGenerationInput>();

                foreach (var tag in tags)
                {
                    GameObject obj = tag.gameObject;
                    Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

                    foreach (Renderer renderer in renderers)
                    {
                        // check that the object wants an image effect applied and is visible, otherwise skip
                        if (renderer == null || (renderer.renderingLayerMask & StyleBits.ImageSpaceBit) == 0)
                            continue;

                        uint mask;
                        BoundingBox bbox;

                        if (NprTestingConfig.TestMode)
                        {
                            mask = tag.currentTestEffects;
                            nprFrameData.presentTestStyles |= tag.currentTestEffects;
                            bbox = BoundingBox.CreateTestBox(mask, new RectInt(0, 0, 0, 0));
                        }
                        else
                        {
                            mask = (uint)tag.imageEffects;
                            bbox = new BoundingBox(mask, new RectInt(0, 0, 0, 0));
                        }

                        // image effects are still tracked separately
                        nprFrameData.presentImageBits |= tag.imageEffects;

                        bbox.renderers.Add(renderer);
                        nprFrameData.bboxes.Add(bbox);

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

                EnsureInputBufferCapacity(nprFrameData.bboxCount);
                EnsureMaskBufferCapacity(nprFrameData.bboxCount);
                EnsureRectBufferCapacity(nprFrameData.bboxCount);

                if (nprFrameData.bboxCount > 0)
                {
                    // upload bounds to GPU
                    _bboxInputBuffer.SetData(gpuInputs);

                    // initialise mask buffer
                    for (int i = 0; i < nprFrameData.bboxCount; i++)
                    {
                        _bboxMaskInitData[i] = gpuInputs[i].mask;
                    }

                    _bboxMaskBuffer.SetData(_bboxMaskInitData, 0, 0, nprFrameData.bboxCount);

                    if (_bboxGeneration == null)
                    {
                        Debug.LogError("bboxPrepass: bbox generation compute shader not assigned.");
                        return;
                    }

                    Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
                    Matrix4x4 projection = camera.projectionMatrix;
                    float nearZ = -camera.nearClipPlane;

                    // compute shader to write rect buffer
                    CommandBuffer cmd = CommandBufferPool.Get("GPU BBox Generation");

                    cmd.SetComputeBufferParam(_bboxGeneration, _bboxGenerationKernel, BBoxInputBufferID, _bboxInputBuffer);
                    cmd.SetComputeBufferParam(_bboxGeneration, _bboxGenerationKernel, BBoxRectBufferID, _bboxRectBuffer);
                    cmd.SetComputeIntParam(_bboxGeneration, BBoxCountID, nprFrameData.bboxCount);
                    cmd.SetComputeMatrixParam(_bboxGeneration, WorldToCameraID, worldToCamera);
                    cmd.SetComputeMatrixParam(_bboxGeneration, ProjectionID, projection);
                    cmd.SetComputeVectorParam(_bboxGeneration, ScreenSizeID, new Vector2(camera.pixelWidth, camera.pixelHeight));
                    cmd.SetComputeFloatParam(_bboxGeneration, NearZID, nearZ);

                    int threadGroupsX = Mathf.CeilToInt(nprFrameData.bboxCount / 64.0f);
                    cmd.DispatchCompute(_bboxGeneration, _bboxGenerationKernel, threadGroupsX, 1, 1);

                    Graphics.ExecuteCommandBuffer(cmd);
                    CommandBufferPool.Release(cmd);
                }

                nprFrameData.bboxMaskBuffer = _bboxMaskBuffer;
                nprFrameData.bboxRectBuffer = _bboxRectBuffer;
            }
        }
        else // if not using bounding boxes, we gather the present image/test effect bits for the whole screen without needing to check visibility
        {
            StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
            foreach (var tag in tags)
            {
                nprFrameData.presentImageBits |= tag.imageEffects;

                // add test effect to bbox mask
                if(NprTestingConfig.TestMode)
                {
                    nprFrameData.presentTestStyles |= tag.currentTestEffects;
                }
            }
        }

        // merge bboxes for optimality
        if (NprTestingConfig.UseBoundingBoxes && !(NprTestingConfig.BatchedBboxGeneration && NprTestingConfig.BatchedDraws))
        {
            bool merged = false;
            List<BoundingBox> newBoxes = new List<BoundingBox>();
            List<BoundingBox> toRemove = new List<BoundingBox>();
            while(!merged)
            {
                merged = true;
                if(NprTestingConfig.TestMode)
                {
                    // Debug.Log("Merging bboxes with test mode on");
                    foreach(var bboxA in nprFrameData.bboxes)
                    {
                        uint testEffectsA = bboxA.testMask;

                        if(testEffectsA == 0)
                            continue;

                        foreach(var bboxB in nprFrameData.bboxes)
                        {
                            if (bboxA == bboxB)
                                continue;

                            uint testEffectsB = bboxB.testMask;

                            // if they share any test effect bits
                            if ((testEffectsA & testEffectsB) != 0)
                            {
                                // compute area of the two boxes
                                int areaA = bboxA.box.width * bboxA.box.height;
                                int areaB = bboxB.box.width * bboxB.box.height;

                                // compute area of their union
                                int UnionMinX = Mathf.Min(bboxA.box.xMin, bboxB.box.xMin);
                                int UnionMinY = Mathf.Min(bboxA.box.yMin, bboxB.box.yMin);
                                int UnionMaxX = Mathf.Max(bboxA.box.xMax, bboxB.box.xMax);
                                int UnionMaxY = Mathf.Max(bboxA.box.yMax, bboxB.box.yMax);

                                int unionArea = (UnionMaxX - UnionMinX) * (UnionMaxY - UnionMinY);

                                if(unionArea < areaA + areaB)
                                {
                                    merged = true;
                                    int unionWidth = UnionMaxX - UnionMinX;
                                    int unionHeight = UnionMaxY - UnionMinY;
                                    RectInt unionRect = new RectInt(UnionMinX, UnionMinY, unionWidth, unionHeight);
                                    
                                    // create new bbox with shared test bits
                                    uint sharedTestEffects = testEffectsA & testEffectsB;
                                    BoundingBox mergedBox = BoundingBox.CreateTestBox(sharedTestEffects, unionRect);

                                    // remove shared bits from original boxes
                                    bboxA.testMask &= ~sharedTestEffects;
                                    bboxB.testMask &= ~sharedTestEffects;

                                    mergedBox.renderers.AddRange(bboxA.renderers);
                                    foreach (var r in bboxB.renderers)
                                    {
                                        if (!mergedBox.renderers.Contains(r))
                                            mergedBox.renderers.Add(r);
                                    }

                                    // add merged box to list
                                    newBoxes.Add(mergedBox); 

                                    // debug show the merged box
                                    // if(NprTestingConfig.debugBBoxes)
                                        // BBoxDebugStore.Add(unionRect, Color.red, $"Merged {sharedTestEffects}");
                                    
                                    // remove b if it has no bits left
                                    if (bboxB.testMask == 0)
                                    {
                                        toRemove.Add(bboxB);
                                    }

                                    // remove a if it has no bits left
                                    if (bboxA.testMask == 0)
                                    {
                                        toRemove.Add(bboxA);
                                        break; 
                                    }
                                }
                            }
                        }

                    }

                    nprFrameData.bboxes.RemoveAll(b => toRemove.Contains(b));
                    nprFrameData.bboxes.AddRange(newBoxes);

                    toRemove.Clear();
                    newBoxes.Clear();
                    
                    continue;
                }

                foreach(var bboxA in nprFrameData.bboxes)
                {
                    StyleBits.ImageSpaceEffect effectsA = bboxA.styles;

                    if(effectsA == 0)
                        continue;

                    foreach(var bboxB in nprFrameData.bboxes)
                    {
                        if (bboxA == bboxB)
                            continue;

                        StyleBits.ImageSpaceEffect effectsB = bboxB.styles;

                        // if they share any image effect bits
                        if ((effectsA & effectsB) != 0)
                        {
                            // compute area of the two boxes
                            int areaA = bboxA.box.width * bboxA.box.height;
                            int areaB = bboxB.box.width * bboxB.box.height;

                            // compute area of their union
                            int UnionMinX = Mathf.Min(bboxA.box.xMin, bboxB.box.xMin);
                            int UnionMinY = Mathf.Min(bboxA.box.yMin, bboxB.box.yMin);
                            int UnionMaxX = Mathf.Max(bboxA.box.xMax, bboxB.box.xMax);
                            int UnionMaxY = Mathf.Max(bboxA.box.yMax, bboxB.box.yMax);

                            int unionArea = (UnionMaxX - UnionMinX) * (UnionMaxY - UnionMinY);

                            if(unionArea < areaA + areaB)
                            {
                                merged = true;
                                int unionWidth = UnionMaxX - UnionMinX;
                                int unionHeight = UnionMaxY - UnionMinY;
                                RectInt unionRect = new RectInt(UnionMinX, UnionMinY, unionWidth, unionHeight);

                                // create new bbox with shared bits
                                StyleBits.ImageSpaceEffect sharedEffects = effectsA & effectsB;
                                BoundingBox mergedBox = new BoundingBox((uint)sharedEffects, unionRect);

                                // remove shared bits from original boxes
                                bboxA.styles &= ~sharedEffects;
                                bboxB.styles &= ~sharedEffects;

                                mergedBox.renderers.AddRange(bboxA.renderers);
                                foreach (var r in bboxB.renderers)
                                {
                                    if (!mergedBox.renderers.Contains(r))
                                        mergedBox.renderers.Add(r);
                                }

                                // add merged box to list
                                newBoxes.Add(mergedBox); 

                                // debug show the merged box
                                // if(NprTestingConfig.debugBBoxes)
                                    // BBoxDebugStore.Add(unionRect, Color.orange, $"Merged {effectsA & effectsB}");

                                // remove b if it has no bits left
                                if (bboxB.styles == 0)
                                {
                                    toRemove.Add(bboxB);
                                }

                                // remove a if it has no bits left
                                if (bboxA.styles == 0)
                                {
                                    toRemove.Add(bboxA);
                                    break; 
                                }

                            }
                        }
                    }
                }

                nprFrameData.bboxes.RemoveAll(b => toRemove.Contains(b));
                nprFrameData.bboxes.AddRange(newBoxes);

                toRemove.Clear();
                newBoxes.Clear();
            }
        }

        if (NprTestingConfig.UseBoundingBoxes)
            nprFrameData.bboxCount = nprFrameData.bboxes.Count;
        else
            nprFrameData.bboxCount = 0;

        // create / initialise GPU buffers
        if (NprTestingConfig.UseBoundingBoxes)
        {
            // if not batching generation then we need to generate rect and mask buffers here
            if(!(NprTestingConfig.BatchedBboxGeneration && NprTestingConfig.BatchedDraws))
            {
                EnsureRectBufferCapacity(nprFrameData.bboxCount);
                EnsureMaskBufferCapacity(nprFrameData.bboxCount);

                for (int i = 0; i < nprFrameData.bboxCount; i++)
                {
                    BoundingBox b = nprFrameData.bboxes[i];
                    _bboxRectInitData[i].rect = new Vector4(b.box.x, b.box.y, b.box.width, b.box.height);

                    if (!NprTestingConfig.TestMode)
                        _bboxMaskInitData[i] = (uint)b.styles;
                    else
                        _bboxMaskInitData[i] = b.testMask;
                }

                if (_bboxRectBuffer != null && _bboxRectInitData != null)
                    _bboxRectBuffer.SetData(_bboxRectInitData, 0, 0, nprFrameData.bboxCount);

                if (_bboxMaskBuffer != null && _bboxMaskInitData != null)
                    _bboxMaskBuffer.SetData(_bboxMaskInitData, 0, 0, nprFrameData.bboxCount);

                nprFrameData.bboxRectBuffer = _bboxRectBuffer;
                nprFrameData.bboxMaskBuffer = _bboxMaskBuffer;
            }

            // visibility is always needed for occlusion culling
            EnsureVisibilityBufferCapacity(nprFrameData.bboxCount);

            for (int i = 0; i < nprFrameData.bboxCount; i++)
            {
                _bboxVisibilityInitData[i] = 1u;
            }

            if (_bboxVisibilityBuffer != null && _bboxVisibilityInitData != null)
                _bboxVisibilityBuffer.SetData(_bboxVisibilityInitData, 0, 0, nprFrameData.bboxCount);

            nprFrameData.bboxVisibilityBuffer = _bboxVisibilityBuffer;
            nprFrameData.bboxVisibilityCount = nprFrameData.bboxCount;
        }

        // initialise source texture
        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
        camDesc.depthBufferBits = 0;
        camDesc.msaaSamples = 1; 

        nprFrameData.sourceTexture = renderGraph.CreateTexture(new TextureDesc(camDesc.width, camDesc.height)
        {
            name = "_NprSourceCopy",
            colorFormat = camDesc.graphicsFormat,   
            clearBuffer = false,
            filterMode = FilterMode.Point
        });
    }

    // this describes the edges as the pair of vertices that they connect
    static readonly int[,] BoxEdges = new int[,]
    {
        {0,1}, {1,3}, {3,2}, {2,0}, // edges on the bottom face
        {4,5}, {5,7}, {7,6}, {6,4}, // edges on the top face
        {0,4}, {1,5}, {2,6}, {3,7}  // edges connecting the faces
    };

    static Vector3[] GetBoxCorners(Bounds b)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        return new Vector3[8]
        {
            c + new Vector3(-e.x, -e.y, -e.z), // v0
            c + new Vector3(e.x, -e.y, -e.z), // v1
            c + new Vector3(-e.x, e.y, -e.z), // v2
            c + new Vector3(e.x, e.y, -e.z), // v3
            c + new Vector3(-e.x, -e.y, e.z), // v4
            c + new Vector3(e.x, -e.y, e.z), // v5
            c + new Vector3(-e.x, e.y, e.z), // v6
            c + new Vector3(e.x, e.y, e.z), // v7
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

    public void Dispose()
    {
        if (_bboxInputBuffer != null)
            _bboxInputBuffer.Release();

        if (_bboxRectBuffer != null)
            _bboxRectBuffer.Release();

        if (_bboxVisibilityBuffer != null)
            _bboxVisibilityBuffer.Release();

        if (_bboxMaskBuffer != null)
            _bboxMaskBuffer.Release();
    }
}