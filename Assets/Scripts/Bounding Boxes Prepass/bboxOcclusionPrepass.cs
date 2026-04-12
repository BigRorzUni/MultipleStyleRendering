using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[System.Serializable]
public class BBoxOcclusionPrepass : ScriptableRenderPass
{
    private readonly Material _visibilityMat;

    private readonly ComputeShader _occlusionCompute;
    private readonly int _occlusionKernelSingle;
    private readonly int _occlusionKernelBatched;

    static readonly int VisibilityTexID = Shader.PropertyToID("_VisibilityTex");
    static readonly int ResultBufferID = Shader.PropertyToID("_Result");
    static readonly int RectID = Shader.PropertyToID("_Rect");
    static readonly int BBoxIndexID = Shader.PropertyToID("_BboxIndex");

    static readonly int RectBufferID = Shader.PropertyToID("_Rects");
    static readonly int BBoxCountID = Shader.PropertyToID("_BboxCount");
    static readonly int BBoxMaskBufferID = Shader.PropertyToID("_ExpectedMasks");

    private readonly ComputeBuffer _resultBuffer;

    private RenderTexture _visibilityRT;

    public void Dispose()
    {
        if (_resultBuffer != null)
            _resultBuffer.Release();

        if (_visibilityRT != null)
            _visibilityRT.Release();
    }

    private class RasterPassData
    {
        public BoundingBox bbox;
        public Material mat;
    }

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
    }

    public BBoxOcclusionPrepass(Shader visibilityShader, ComputeShader occlusionComputeShader)
    {
        if (visibilityShader != null)
            _visibilityMat = CoreUtils.CreateEngineMaterial(visibilityShader);

        _occlusionCompute = occlusionComputeShader;
        if (_occlusionCompute != null)
        {
            _occlusionKernelSingle = _occlusionCompute.FindKernel("OcclusionCheckSingle");
            _occlusionKernelBatched = _occlusionCompute.FindKernel("OcclusionCheckBatched");
        }

        _resultBuffer = new ComputeBuffer(1, sizeof(uint));

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
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

        if (nprFrameData.bboxVisibilityBuffer == null)
            return;

        if (nprFrameData.bboxCount <= 0)
            return;

        if (NprTestingConfig.RenderMode == NprRenderMode.CPU)
        {
            // occlusion using CPU bbox list
            if (_visibilityMat == null)
                return;

            if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
                return;

            if (nprFrameData.occlusionCandidateBoxes == null)
                nprFrameData.occlusionCandidateBoxes = new List<BoundingBox>();
            else
                nprFrameData.occlusionCandidateBoxes.Clear();

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

            if (nprFrameData.occlusionCandidateBoxes.Count == 0)
            {
                nprFrameData.bboxVisibilityCount = nprFrameData.bboxCount;
                return;
            }

            foreach (var bbox in nprFrameData.occlusionCandidateBoxes)
            {
                if (bbox == null || bbox.renderers == null || bbox.renderers.Count == 0)
                    continue;

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
                desc.sRGB = false;

                TextureHandle visibilityTex = renderGraph.CreateTexture(new TextureDesc(desc)
                {
                    name = "_BBoxVisibilityMask",
                    colorFormat = desc.graphicsFormat,
                    clearBuffer = true,
                    clearColor = Color.black,
                    filterMode = FilterMode.Point,
                    useMipMap = false
                });

                using (var builder = renderGraph.AddRasterRenderPass($"BBox Occlusion Test {nprFrameData.bboxes.IndexOf(bbox)}", out RasterPassData passData))
                {
                    builder.AllowPassCulling(false);

                    builder.SetRenderAttachment(visibilityTex, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(frameData.activeDepthTexture, AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);

                    passData.mat = _visibilityMat;
                    passData.bbox = bbox;

                    builder.SetRenderFunc(static (RasterPassData data, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.EnableScissorRect(new Rect(
                            data.bbox.box.x,
                            data.bbox.box.y,
                            data.bbox.box.width,
                            data.bbox.box.height
                        ));

                        List<Renderer> renderers = data.bbox.renderers;
                        for (int i = 0; i < renderers.Count; i++)
                        {
                            Renderer renderer = renderers[i];
                            if (renderer == null)
                                continue;

                            int submeshCount;
                            if (renderer.sharedMaterials != null)
                                submeshCount = renderer.sharedMaterials.Length;
                            else
                                submeshCount = 1;

                            for (int sub = 0; sub < submeshCount; sub++)
                                ctx.cmd.DrawRenderer(renderer, data.mat, sub, 0);
                        }

                        ctx.cmd.DisableScissorRect();
                    });
                }

                using (var builder = renderGraph.AddComputePass("BBox Occlusion Analyse", out ComputePassData passData))
                {
                    builder.AllowPassCulling(false);

                    passData.visibilityTex = visibilityTex;
                    passData.resultBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.rect = bbox.box;
                    passData.compute = _occlusionCompute;
                    passData.kernel = _occlusionKernelSingle;

                    int bboxIndex = nprFrameData.bboxes.IndexOf(bbox);
                    if (bboxIndex < 0)
                    {
                        Debug.LogError($"Occlusion candidate bbox not found in bboxes list: {bbox.box}");
                        continue;
                    }

                    passData.bboxIndex = (uint)bboxIndex;

                    builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

                    builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
                    {
                        ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                        ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                        ctx.cmd.SetComputeVectorParam(data.compute, RectID, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        ctx.cmd.SetComputeIntParam(data.compute, BBoxIndexID, (int)data.bboxIndex);
                        ctx.cmd.DispatchCompute(data.compute, data.kernel, 1, 1, 1);
                    });
                }
            }

            nprFrameData.bboxVisibilityCount = nprFrameData.bboxCount;
        }
        else
        {
            // occlusion using gpu bbox buffers and id tex
            if (nprFrameData.bboxRectBuffer == null)
                return;

            if (nprFrameData.bboxMaskBuffer == null)
                return;

            using (var builder = renderGraph.AddComputePass("BBox Occlusion Analysis (ID Tex)", out ComputePassData passData))
            {
                builder.AllowPassCulling(false);

                passData.visibilityTex = nprFrameData.idTexture;
                passData.resultBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.compute = _occlusionCompute;
                passData.kernel = _occlusionKernelBatched;
                passData.rectBuffer = nprFrameData.bboxRectBuffer;
                passData.maskBuffer = nprFrameData.bboxMaskBuffer;
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

    bool ContainsRect(RectInt outer, RectInt inner)
    {
        return outer.xMin <= inner.xMin && outer.xMax >= inner.xMax && outer.yMin <= inner.yMin && outer.yMax >= inner.yMax;
    }
}