using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class CpuOcclusion : Prepass
{
    private readonly ComputeShader _occlusionCompute;
    private readonly int _occlusionKernelSingle;

    static readonly int VisibilityTexID = Shader.PropertyToID("_VisibilityTex");
    static readonly int ExpectedMaskID = Shader.PropertyToID("_ExpectedMask");
    static readonly int ResultBufferID = Shader.PropertyToID("_Result");
    static readonly int RectID = Shader.PropertyToID("_Rect");
    static readonly int BBoxIndexID = Shader.PropertyToID("_BboxIndex");

    ComputeBuffer _visibilityBuffer;
    int _bboxVisibilityBufferCapacity = 0;
    uint[] _bboxVisibilityInitData;


    private class ComputePassData
    {
        public TextureHandle visibilityTex;
        public ComputeBuffer resultBuffer;
        public RectInt rect;
        public ComputeShader compute;
        public int kernel;
        public uint bboxIndex;
        public uint expectedMask;
    }

    public CpuOcclusion(ComputeShader occlusionComputeShader) : base("CpuOcclusion")
    {
        _occlusionCompute = occlusionComputeShader;
        if (_occlusionCompute != null)
        {
            _occlusionKernelSingle = _occlusionCompute.FindKernel("OcclusionCheckSingle");
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_occlusionCompute == null)
            return;

        if (NprTestingConfig.RenderMode != NprRenderMode.CPU)
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


        NprFrameData.EnsureBufferCapacity(ref _visibilityBuffer, ref _bboxVisibilityBufferCapacity, nprFrameData.bboxCount, sizeof(uint));
        nprFrameData.visibilityBuffer = _visibilityBuffer;

        if (_bboxVisibilityInitData == null || _bboxVisibilityInitData.Length < _bboxVisibilityBufferCapacity)
            _bboxVisibilityInitData = new uint[_bboxVisibilityBufferCapacity];

        for (int i = 0; i < nprFrameData.bboxCount; i++)
            _bboxVisibilityInitData[i] = 1u;

        if (nprFrameData.visibilityBuffer != null && _bboxVisibilityInitData != null)
            nprFrameData.visibilityBuffer.SetData(_bboxVisibilityInitData, 0, 0, nprFrameData.bboxCount);

        if (nprFrameData.visibilityBuffer == null)
            return;

        if (nprFrameData.bboxCount <= 0)
            return;

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
                if(NprTestingConfig.TestMode)
                    passData.expectedMask = bbox.testMask;
                else
                    passData.expectedMask = (uint)bbox.styles;

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
    }


    public override void Dispose()
    {
        if (_visibilityBuffer != null)
        {
            _visibilityBuffer.Release();
            _visibilityBuffer = null;
        }

        _bboxVisibilityBufferCapacity = 0;
    }
}