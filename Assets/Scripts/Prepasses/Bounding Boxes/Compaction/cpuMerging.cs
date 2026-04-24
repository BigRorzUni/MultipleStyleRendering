using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

[System.Serializable]
public class CpuMerging : Prepass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    public CpuMerging() : base("CpuMergingPrepass") { }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        if (NprTestingConfig.RenderMode != NprRenderMode.CPU)
            return;

        if (!NprTestingConfig.UseMerging)
            return;

        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        bool merged = false;
        List<BoundingBox> newBoxes = new List<BoundingBox>();
        List<BoundingBox> toRemove = new List<BoundingBox>();

        Debug.Log($"Before merge: {nprFrameData.bboxes.Count}");

        while (!merged)
        {
            merged = true;

            if (NprTestingConfig.TestMode)
            {
                foreach (var bboxA in nprFrameData.bboxes)
                {
                    uint testEffectsA = bboxA.testMask;

                    if (testEffectsA == 0)
                        continue;

                    foreach (var bboxB in nprFrameData.bboxes)
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

                            if (unionArea < areaA + areaB)
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

            foreach (var bboxA in nprFrameData.bboxes)
            {
                StyleBits.ImageSpaceEffect effectsA = bboxA.styles;

                if (effectsA == 0)
                    continue;

                foreach (var bboxB in nprFrameData.bboxes)
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

                        if (unionArea < areaA + areaB)
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

        Debug.Log($"After merge: {nprFrameData.bboxes.Count}");
    }

    public override void Dispose()
    {
    }
}