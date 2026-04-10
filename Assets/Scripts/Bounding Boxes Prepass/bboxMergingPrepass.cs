using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[System.Serializable]
public class BBoxMergingPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    // merging compute shader
    readonly ComputeShader _bboxMerging;
    readonly int _bboxMergingKernel;

    public BBoxMergingPrepass(ComputeShader bboxMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        if(bboxMerging != null)
        {
            _bboxMerging = bboxMerging;
            _bboxMergingKernel = _bboxMerging.FindKernel("MergeBboxes");
        }
    }


    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        if (!NprTestingConfig.BoundingBoxes || !NprTestingConfig.BBoxMerging)
            return;

        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        // cpu merging
        if (!NprTestingConfig.BatchedBBoxGeneration)
        {
            bool merged = false;
            List<BoundingBox> newBoxes = new List<BoundingBox>();
            List<BoundingBox> toRemove = new List<BoundingBox>();

            while(!merged)
            {
                merged = true;

                if(NprTestingConfig.TestMode)
                {
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
                                    merged = false;

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

                                    newBoxes.Add(mergedBox);

                                    // remove b if it has no bits left
                                    if (bboxB.testMask == 0)
                                        toRemove.Add(bboxB);

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

                    if (toRemove.Count > 0)
                        nprFrameData.bboxes.RemoveAll(b => toRemove.Contains(b));

                    if (newBoxes.Count > 0)
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
                                merged = false;

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

                                newBoxes.Add(mergedBox);

                                // remove b if it has no bits left
                                if (bboxB.styles == 0)
                                    toRemove.Add(bboxB);

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

                if (toRemove.Count > 0)
                    nprFrameData.bboxes.RemoveAll(b => toRemove.Contains(b));

                if (newBoxes.Count > 0)
                    nprFrameData.bboxes.AddRange(newBoxes);

                toRemove.Clear();
                newBoxes.Clear();
            }
        }
        else // gpu merging
        {
            nprFrameData.bboxCount = nprFrameData.bboxes.Count;

            // compute pass

            // update all buffers

            return;
        }

        nprFrameData.bboxCount = nprFrameData.bboxes.Count;

        // after cpu merging the visibility buffer indices no longer match the old bbox list
        // all boxes remaining after occlusion filtering are visible, so rebuild visibility
        if (!NprTestingConfig.BatchedBBoxGeneration)
        {
            if (nprFrameData.bboxVisibilityBuffer != null && nprFrameData.bboxCount > 0)
            {
                uint[] visibilityData = new uint[nprFrameData.bboxCount];

                for (int i = 0; i < nprFrameData.bboxCount; i++)
                    visibilityData[i] = 1u;

                nprFrameData.bboxVisibilityBuffer.SetData(visibilityData, 0, 0, nprFrameData.bboxCount);
            }

            nprFrameData.bboxVisibilityCount = nprFrameData.bboxCount;
        }

        // if effects are using gpu buffers, rebuild them from the merged cpu list
        if (NprTestingConfig.BatchedDraws && !NprTestingConfig.BatchedBBoxGeneration)
        {
            if (nprFrameData.bboxRectBuffer != null && nprFrameData.bboxMaskBuffer != null)
            {
                if (nprFrameData.bboxCount > 0)
                {
                    QuadInstanceData[] rectData = new QuadInstanceData[nprFrameData.bboxCount];
                    uint[] maskData = new uint[nprFrameData.bboxCount];

                    for (int i = 0; i < nprFrameData.bboxCount; i++)
                    {
                        BoundingBox b = nprFrameData.bboxes[i];
                        rectData[i].rect = new Vector4(b.box.x, b.box.y, b.box.width, b.box.height);

                        if (!NprTestingConfig.TestMode)
                            maskData[i] = (uint)b.styles;
                        else
                            maskData[i] = b.testMask;
                    }

                    nprFrameData.bboxRectBuffer.SetData(rectData, 0, 0, nprFrameData.bboxCount);
                    nprFrameData.bboxMaskBuffer.SetData(maskData, 0, 0, nprFrameData.bboxCount);
                }
            }
        }
    }
}