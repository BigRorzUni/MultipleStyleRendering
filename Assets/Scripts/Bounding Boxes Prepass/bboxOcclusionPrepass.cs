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



    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int BBoxCountID = Shader.PropertyToID("_BboxCount");
    static readonly int BBoxMaskBufferID = Shader.PropertyToID("_ExpectedMasks");

    private readonly ComputeBuffer _resultBuffer;

    // private int _writeIndex = 0;
    // private readonly bool[] _pendingReadback = new bool[2];
    // private readonly RectInt[] _pendingRects = new RectInt[2];

    // private readonly List<RectInt> _pendingRemovalRects = new List<RectInt>();

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

    private class DebugPassData
    {
        public TextureHandle src;            
        public Material mat;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer visibilityBuffer;
        public Vector4 screenSize;
        public int instanceCount;
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
    }

    // public void Dispose()
    // {
    //     for (int i = 0; i < 2; i++)
    //     {
    //         if (_resultBuffers[i] != null)
    //         {
    //             _resultBuffers[i].Release();
    //             _resultBuffers[i] = null;
    //         }
    //     }
    // }

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
        if (_visibilityMat == null || _occlusionCompute == null)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();


        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        if (nprFrameData.bboxVisibilityBuffer == null)
            return;

        if (nprFrameData.bboxRectBuffer == null)
            return;

        if (nprFrameData.bboxVisibilityCount <= 0)
            return;

        // if no potentially occluded boxes then skip this pass
        if (nprFrameData.occlusionCandidateBoxes == null || nprFrameData.occlusionCandidateBoxes.Count == 0)
            return;

        Debug.Log("running bbox occlusion prepass");

        // loop over all potentially occluded boxes 
        if(!NprTestingConfig.IdTexOcclusion)
        {
            foreach(var bbox in nprFrameData.occlusionCandidateBoxes)
            {
                if (bbox == null || bbox.renderers == null || bbox.renderers.Count == 0)
                    return;

                // temporary visibility texture
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
                            {
                                ctx.cmd.DrawRenderer(renderer, data.mat, sub, 0);
                            }
                        }

                        ctx.cmd.DisableScissorRect();
                    });
                }

                // DEBUG
                // using (var builder = renderGraph.AddRasterRenderPass("Debug VisibilityTex", out DebugPassData passData))
                // {
                //     builder.UseTexture(visibilityTex, AccessFlags.Read);
                //     builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
                //
                //     passData.src = visibilityTex;
                //
                //     builder.SetRenderFunc(static (DebugPassData data, RasterGraphContext ctx) =>
                //     {
                //         Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
                //     });
                // }

                // scan visibilityTex and output one 0/1 result into the gpu visibility buffer
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
                    // passData.bboxIndex = (uint)nprFrameData.bboxes.IndexOf(bbox);

                    // if (!NprTestingConfig.TestMode)
                    //     passData.expectedMask = (uint)bbox.styles;
                    // else
                    //     passData.expectedMask = bbox.testMask;

                    builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

                    builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
                    {
                        ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                        ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                        ctx.cmd.SetComputeVectorParam(data.compute, RectID, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        // ctx.cmd.SetComputeIntParam(data.compute, ExpectedMaskID, (int)data.expectedMask);
                        ctx.cmd.SetComputeIntParam(data.compute, BBoxIndexID, (int)data.bboxIndex);

                        // shader executes over all pixels in the bbox
                        ctx.cmd.DispatchCompute(data.compute, data.kernel, 1, 1, 1);
                    });
                }
            }
        }
        else
        {
            using (var builder = renderGraph.AddComputePass("BBox Occlusion Analyse (ID Tex)", out ComputePassData passData))
            {
                builder.AllowPassCulling(false);

                passData.visibilityTex = nprFrameData.idTexture; // ID TEX OVER PER BBOX VISIBILITY CHECK // slight correctness loss but should be faster
                passData.resultBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.compute = _occlusionCompute;
                passData.kernel = _occlusionKernelBatched;
                passData.rectBuffer = nprFrameData.bboxRectBuffer;
                passData.maskBuffer = nprFrameData.bboxMaskBuffer;

                builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

                builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, InstanceBufferID, data.rectBuffer);
                    ctx.cmd.SetComputeIntParam(data.compute, BBoxCountID, nprFrameData.bboxes.Count);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, BBoxMaskBufferID, data.maskBuffer);

                    // dispatch 1 threadgroup per bbox
                    ctx.cmd.DispatchCompute(data.compute, data.kernel, nprFrameData.bboxes.Count, 1, 1);
                });
            }
        }
    }
}