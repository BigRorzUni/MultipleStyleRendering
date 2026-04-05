using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using NUnit.Framework.Internal;

[System.Serializable]
public class bboxPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    class PassData
    {
        public TextureHandle src;
        public TextureHandle dst;
        public RectInt rect;
        public Vector2 srcTexelSize;
    }

    public bboxPrepass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public bboxPrepass(int testCount, bool testModeEnabled)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        testStyleCount = testCount;
        _testModeEnabled = testModeEnabled;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
        Camera camera = cameraData.camera; // extend to multiple cameras?

        if(camera == null)
            return;

        if(NprTestingConfig.debugBBoxes)
            BBoxDebugStore.Clear();
            
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

            if(NprTestingConfig.UseOcclusionCulling)
            {
                if (nprFrameData.occlusionCandidateBoxes == null) 
                    nprFrameData.occlusionCandidateBoxes = new List<BoundingBox>();
                else 
                    nprFrameData.occlusionCandidateBoxes.Clear();
            }

            // get all active tagged objects using the attached StylisedTag component
            StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
            foreach (var tag in tags)
            {
                GameObject obj = tag.gameObject;
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

                foreach(Renderer renderer in renderers)
                {
                    // check that the object wants an image effect applied and is visible, otherwise skip
                    if(renderer == null || (renderer.renderingLayerMask & (uint)StyleBits.ImageSpaceBit) == 0 || !renderer.isVisible)
                        continue;

                    if (TryGetNearClippedScreenRect(renderer, camera, out RectInt screenRect))
                    {
                        BoundingBox bbox;
                        if (NprTestingConfig.TestMode)
                        {
                            bbox = BoundingBox.CreateTestBox(tag.testEffects, screenRect);
                            nprFrameData.presentTestStyles |= tag.testEffects;
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
        else // if not using bounding boxes, we gather the present image/test    effect bits for the whole screen without needing to check visibility
        {
            StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
            foreach (var tag in tags)
            {
                nprFrameData.presentImageBits |= tag.imageEffects;

                // add test effect to bbox mask
                if(NprTestingConfig.TestMode)
                {
                    nprFrameData.presentTestStyles |= tag.testEffects;
                }
            }
        }
        // need to check that the object in the bbox is actually visible
        // not sure how this would work (raycasts?)


        // merge bboxes for optimality
        if(NprTestingConfig.UseBoundingBoxes)
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

        // find occlusion candidates if enabled
        if(NprTestingConfig.UseOcclusionCulling)
        {
            nprFrameData.occlusionCandidateBoxes = new List<BoundingBox>(nprFrameData.bboxes);

             for (int i = 0; i < nprFrameData.bboxes.Count; i++)
            {
                BoundingBox inner = nprFrameData.bboxes[i];

                for (int j = 0; j < nprFrameData.bboxes.Count; j++)
                {
                    if (i == j)
                        continue;

                    BoundingBox outer = nprFrameData.bboxes[j];

                    if (ContainsRect(outer.box, inner.box))
                    {
                        nprFrameData.occlusionCandidateBoxes.Add(inner);
                        break;
                    }
                }
            }

        }

        // debug show all the final boxes
        if(NprTestingConfig.debugBBoxes && NprTestingConfig.UseBoundingBoxes)
        {
            foreach(var bbox in nprFrameData.bboxes)
            {                
                BBoxDebugStore.Add(bbox.box, Color.green, $"Final {bbox.styles} test {bbox.testMask}");
            }

            if(NprTestingConfig.UseOcclusionCulling)
            {
                foreach(var bbox in nprFrameData.occlusionCandidateBoxes)
                {                
                    BBoxDebugStore.Add(bbox.box, Color.blue, $"Occlusion Candidate {bbox.styles} test {bbox.testMask}");
                }
            }
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

    bool ContainsRect(RectInt outer, RectInt inner)
    {
        return outer.xMin <= inner.xMin && outer.xMax >= inner.xMax && outer.yMin <= inner.yMin && outer.yMax >= inner.yMax;
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
}

                    // Bounds rendererBounds = renderer.bounds;
                    // Vector3 centre = rendererBounds.center;
                    // Vector3 ext = rendererBounds.extents;

                    // // corner of the renderer boudns
                    // Vector3[] corners = new Vector3[8]
                    // {
                    //     centre + new Vector3(-ext.x, -ext.y, -ext.z),
                    //     centre + new Vector3(ext.x, -ext.y, -ext.z),
                    //     centre + new Vector3(-ext.x, ext.y, -ext.z),
                    //     centre + new Vector3(ext.x, ext.y, -ext.z),
                    //     centre + new Vector3(-ext.x, -ext.y, ext.z),
                    //     centre + new Vector3(ext.x, -ext.y, ext.z),
                    //     centre + new Vector3(-ext.x, ext.y, ext.z),
                    //     centre + new Vector3(ext.x, ext.y, ext.z)
                    // };

                    // // get min and max of screen coordinates for the bounding box
                    // float minX = float.MaxValue, maxX = float.MinValue;
                    // float minY = float.MaxValue, maxY = float.MinValue;
                    // int validCorners = 0;

                    // foreach (var corner in corners)
                    // {
                    //     Vector3 screenPoint = camera.WorldToScreenPoint(corner);

                    //     // ignore points behind camera
                    //     // if (screenPoint.z <= 0f)
                    //     //     continue;

                    //     validCorners++;

                    //     minX = Mathf.Min(minX, screenPoint.x);
                    //     maxX = Mathf.Max(maxX, screenPoint.x);
                    //     minY = Mathf.Min(minY, screenPoint.y);
                    //     maxY = Mathf.Max(maxY, screenPoint.y);
                    // }

                    // // if nothing was in front of the camera, skip
                    // if (validCorners == 0)
                    //     continue;

                    // // clamp final bounds to screen
                    // minX = Mathf.Clamp(minX, 0f, camera.pixelWidth);
                    // maxX = Mathf.Clamp(maxX, 0f, camera.pixelWidth);
                    // minY = Mathf.Clamp(minY, 0f, camera.pixelHeight);
                    // maxY = Mathf.Clamp(maxY, 0f, camera.pixelHeight);

                    // // convert to ints
                    // int xMin = Mathf.FloorToInt(minX);
                    // int yMin = Mathf.FloorToInt(minY);
                    // int xMax = Mathf.CeilToInt(maxX);
                    // int yMax = Mathf.CeilToInt(maxY);

                    // // compute size
                    // int width = xMax - xMin;
                    // int height = yMax - yMin;

                    // // skip degenerate boxes
                    // if (width <= 0 || height <= 0)
                    //     continue;