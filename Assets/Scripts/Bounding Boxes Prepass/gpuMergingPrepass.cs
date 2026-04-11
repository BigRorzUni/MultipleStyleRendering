using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class GpuMergingPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    // merging compute shader
    readonly ComputeShader _bboxMerging;
    readonly int _bboxMergingKernel;

    public GpuMergingPrepass(ComputeShader bboxMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        if (bboxMerging != null)
        {
            _bboxMerging = bboxMerging;
            _bboxMergingKernel = _bboxMerging.FindKernel("MergeBboxes");
        }
    }

    private class ComputePassData
    {
        public ComputeShader compute;
        public int kernel;

        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeBuffer visibilityBuffer;

        public int bboxCount;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        if (!NprTestingConfig.BoundingBoxes || !NprTestingConfig.BBoxMerging)
            return;

        if (!NprTestingConfig.BatchedBBoxMerging)
            return;

        if(!NprTestingConfig.BatchedDraws)
        {
            Debug.LogWarning("GPU merging is only valid with batched drawing of effects. No merging will take place");
            return;
        }

        if (_bboxMerging == null)
            return;

        if (nprFrameData.bboxRectBuffer == null || nprFrameData.bboxMaskBuffer == null || nprFrameData.bboxVisibilityBuffer == null)
            return;

        if (nprFrameData.bboxCount <= 0)
            return;

        using (var builder = renderGraph.AddComputePass("GPU BBox Merging", out ComputePassData passData))
        {
            passData.compute = _bboxMerging;
            passData.kernel = _bboxMergingKernel;

            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
            passData.bboxCount = nprFrameData.bboxCount;

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
            {
                
            });
        }
    }
}