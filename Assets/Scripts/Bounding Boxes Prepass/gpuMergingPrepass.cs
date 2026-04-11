using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class GpuMergingPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    readonly ComputeShader _bboxMerging;
    readonly int _findPartnersKernel;


    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int MaskBufferID = Shader.PropertyToID("_Masks");
    static readonly int VisibilityBufferID = Shader.PropertyToID("_Visibility");

    static readonly int PartnerBufferID = Shader.PropertyToID("_Partners");


    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");

    ComputeBuffer _partnerBuffer;
    int _partnerBufferCapacity = 0;

        void EnsurePartnerBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_partnerBuffer == null || _partnerBufferCapacity < requiredCapacity)
        {
            if (_partnerBuffer != null)
                _partnerBuffer.Release();

            _partnerBufferCapacity = requiredCapacity;
            _partnerBuffer = new ComputeBuffer(_partnerBufferCapacity, sizeof(int));
        }
    }

    public GpuMergingPrepass(ComputeShader bboxMerging)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        if (bboxMerging != null)
        {
            _bboxMerging = bboxMerging;
            _findPartnersKernel = _bboxMerging.FindKernel("FindMergePartners");
        }
    }

    private class ComputePassData
    {
        public ComputeShader compute;

        public int findPartnersKernel;


        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public ComputeBuffer visibilityBuffer;

        public ComputeBuffer partnerBuffer;




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

        EnsurePartnerBufferCapacity(nprFrameData.bboxCount);
       
        int[] partnerInit = new int[_partnerBufferCapacity];
        for (int i = 0; i < partnerInit.Length; i++)
            partnerInit[i] = -1;
        _partnerBuffer.SetData(partnerInit);


        using (var builder = renderGraph.AddComputePass("GPU BBox Merging", out ComputePassData passData))
        {
            passData.compute = _bboxMerging;

            passData.findPartnersKernel = _findPartnersKernel;


            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;

            passData.partnerBuffer = _partnerBuffer;




            passData.bboxCount = nprFrameData.bboxCount;

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
            {
                int threadGroupsX = Mathf.CeilToInt(data.bboxCount / 64.0f);

                // find merge partners
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPartnersKernel, RectBufferID, data.rectBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPartnersKernel, MaskBufferID, data.maskBuffer);
                ctx.cmd.SetComputeBufferParam(data.compute, data.findPartnersKernel, VisibilityBufferID, data.visibilityBuffer);

                ctx.cmd.SetComputeBufferParam(data.compute, data.findPartnersKernel, PartnerBufferID, data.partnerBuffer);

                ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                
                ctx.cmd.DispatchCompute(data.compute, data.findPartnersKernel, threadGroupsX, 1, 1);


            });
        }
    }
}