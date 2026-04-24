using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[System.Serializable]
public class BBoxOcclusion : Prepass
{
    private readonly ComputeShader _occlusionCompute;
    private readonly int _occlusionKernelSingle;
    private readonly int _occlusionKernelBatched;

    static readonly int VisibilityTexID = Shader.PropertyToID("_VisibilityTex");
    static readonly int ExpectedMaskID = Shader.PropertyToID("_ExpectedMask");
    static readonly int ResultBufferID = Shader.PropertyToID("_Result");
    static readonly int RectID = Shader.PropertyToID("_Rect");
    static readonly int BBoxIndexID = Shader.PropertyToID("_BboxIndex");

    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int BBoxCountID = Shader.PropertyToID("_BBoxCount");
    static readonly int BBoxMaskBufferID = Shader.PropertyToID("_ExpectedMasks");


    private class ComputePassData
    {
        public TextureHandle visibilityTex;
        public ComputeBuffer resultBuffer;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public RectInt rect;
        public ComputeShader compute;
        public int kernel;
        public uint bboxIndex;
        public int bboxCount;
        public uint expectedMask;
    }

    public BBoxOcclusion(ComputeShader occlusionComputeShader) : base("BBoxOcclusion")
    {
        _occlusionCompute = occlusionComputeShader;
        if (_occlusionCompute != null)
        {
            _occlusionKernelSingle = _occlusionCompute.FindKernel("OcclusionCheckSingle");
            _occlusionKernelBatched = _occlusionCompute.FindKernel("OcclusionCheckBatched");
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_occlusionCompute == null)
            return;

        if (NprTestingConfig.RenderMode == NprRenderMode.Fullscreen)
            return;

        if (!NprTestingConfig.UseOcclusion)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        if (nprFrameData.visibilityBuffer == null)
            return;

        if (nprFrameData.bboxCount <= 0)
            return;

        if (NprTestingConfig.RenderMode == NprRenderMode.CPU)
        {
            // occlusion using CPU bbox list but reading from id tex
            if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
                return;

            if (!nprFrameData.idTexture.IsValid())
                return;

            for (int i = 0; i < nprFrameData.bboxes.Count; i++)
            {
                BoundingBox bbox = nprFrameData.bboxes[i];

                if (bbox == null)
                    continue;

                if (bbox.box.width <= 0 || bbox.box.height <= 0)
                    continue;

                using (var builder = renderGraph.AddComputePass($"BBox Occlusion Analyse {i}", out ComputePassData passData, profilingSampler))
                {
                    builder.AllowPassCulling(false);

                    passData.visibilityTex = nprFrameData.idTexture;
                    passData.resultBuffer = nprFrameData.visibilityBuffer;
                    passData.rect = bbox.box;
                    passData.compute = _occlusionCompute;
                    passData.kernel = _occlusionKernelSingle;
                    passData.bboxIndex = (uint)i;
                    passData.expectedMask = bbox.testMask;

                    builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

                    builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
                    {
                        ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                        ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                        ctx.cmd.SetComputeVectorParam(data.compute, RectID, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        ctx.cmd.SetComputeIntParam(data.compute, BBoxIndexID, (int)data.bboxIndex);
                        ctx.cmd.SetComputeIntParam(data.compute, ExpectedMaskID, (int)data.expectedMask);
                        ctx.cmd.DispatchCompute(data.compute, data.kernel, 1, 1, 1);
                    });
                }
            }

            nprFrameData.bboxVisibilityCount = nprFrameData.bboxCount;
        }
        else
        {
            
            // occlusion using gpu bbox buffers and id tex
            if (nprFrameData.rectBuffer == null)
                return;

            if (nprFrameData.maskBuffer == null)
                return;

            using (var builder = renderGraph.AddComputePass("BBox Occlusion Analysis (ID Tex)", out ComputePassData passData, profilingSampler))
            {
                builder.AllowPassCulling(false);

                passData.visibilityTex = nprFrameData.idTexture;
                passData.resultBuffer = nprFrameData.visibilityBuffer;
                passData.compute = _occlusionCompute;
                passData.kernel = _occlusionKernelBatched;
                passData.rectBuffer = nprFrameData.rectBuffer;
                passData.maskBuffer = nprFrameData.maskBuffer;
                passData.bboxCount = nprFrameData.bboxCount;

                builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

                builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, RectBufferID, data.rectBuffer);
                    ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, data.bboxCount);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, BBoxMaskBufferID, data.maskBuffer);
                    ctx.cmd.DispatchCompute(data.compute, data.kernel, data.bboxCount, 1, 1);
                });
            }

            nprFrameData.bboxVisibilityCount = nprFrameData.bboxCount;
        }
    }


    public override void Dispose()
    {
    }
}