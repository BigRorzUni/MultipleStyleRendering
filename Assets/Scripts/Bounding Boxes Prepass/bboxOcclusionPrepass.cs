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
    private readonly int _occlusionKernel;

    static readonly int VisibilityTexID = Shader.PropertyToID("_VisibilityTex");
    static readonly int ResultBufferID = Shader.PropertyToID("_Result");
    static readonly int RectID = Shader.PropertyToID("_Rect");
    static readonly int ExpectedMaskID = Shader.PropertyToID("_ExpectedMask");
    static readonly int BBoxIndexID = Shader.PropertyToID("_BBoxIndex");

    private ComputeBuffer _bboxVisibilityBuffer;
    private int _bboxVisibilityBufferCapacity = 0;
    private uint[] _bboxVisibilityInitData;

    private readonly ComputeBuffer _resultBuffer;
    private readonly uint[] _resultData = new uint[1];

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
        
        if (_bboxVisibilityBuffer != null)
            _bboxVisibilityBuffer.Release();
    }

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
        {
            _bboxVisibilityInitData = new uint[_bboxVisibilityBufferCapacity];
        }
    }
    private class RasterPassData
    {
        public BoundingBox bbox;
        public Material mat;
    }

    private class DebugPassData
    {
        public TextureHandle src;
    }

    private class ComputePassData
    {
        public TextureHandle visibilityTex;
        public ComputeBuffer resultBuffer;
        public RectInt rect;
        public ComputeShader compute;
        public int kernel;
        public uint expectedMask;
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
            _occlusionKernel = _occlusionCompute.FindKernel("OcclusionCheck");
        }

        // for (int i = 0; i < 2; i++)
        // {
        //     _resultBuffers[i] = new ComputeBuffer(1, sizeof(uint));
        //     _resultBuffers[i].SetData(_resultData);
        // }


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

        // if no potentially occluded boxes then skip this pass
        if (nprFrameData.occlusionCandidateBoxes == null || nprFrameData.occlusionCandidateBoxes.Count == 0)
            return;
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        Debug.Log("running bbox occlusion prepass");

        // create / initialise GPU visibility buffer
        EnsureVisibilityBufferCapacity(nprFrameData.bboxes.Count);

        for (int i = 0; i < nprFrameData.bboxes.Count; i++)
            _bboxVisibilityInitData[i] = 1u; // default visible

        if (_bboxVisibilityBuffer == null || _bboxVisibilityInitData == null)
            return;
        
        _bboxVisibilityBuffer.SetData(_bboxVisibilityInitData, 0, 0, nprFrameData.bboxes.Count);

        // expose for later passes/shaders
        nprFrameData.bboxVisibilityBuffer = _bboxVisibilityBuffer;
        nprFrameData.bboxVisibilityCount = nprFrameData.bboxes.Count;

        // change this to all boxes once compute shader working properly 
        BoundingBox bbox = nprFrameData.occlusionCandidateBoxes[0];

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

        using (var builder = renderGraph.AddRasterRenderPass($"BBox Occlusion Test {bbox.box}", out RasterPassData passData))
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
            passData.resultBuffer = _bboxVisibilityBuffer;
            passData.rect = bbox.box;
            passData.compute = _occlusionCompute;
            passData.kernel = _occlusionKernel;
            passData.bboxIndex = (uint)bbox.frameIndex;

            if (!NprTestingConfig.TestMode)
                passData.expectedMask = (uint)bbox.styles;
            else
                passData.expectedMask = bbox.testMask;

            builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

            builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
            {
                ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                ctx.cmd.SetComputeVectorParam(data.compute, RectID, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                ctx.cmd.SetComputeIntParam(data.compute, ExpectedMaskID, (int)data.expectedMask);
                ctx.cmd.SetComputeIntParam(data.compute, BBoxIndexID, (int)data.bboxIndex);

                // shader loops over all pixels in the bbox
                ctx.cmd.DispatchCompute(data.compute, data.kernel, 1, 1, 1);
            });
        }

        // if box has been occluded then later gpu passes can read bboxVisibilityBuffer
        // do the same for all occlusion candidates
    }
    // public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    // {
    //     if (_visibilityMat == null || _occlusionCompute == null)
    //         return;

    //     Camera camera = renderingData.cameraData.camera;
    //     if (camera == null)
    //         return;

    //     ScriptableRenderer renderer = renderingData.cameraData.renderer;

    //     List<BoundingBox> bboxes = OcclusionData.bboxes;
    //     List<BoundingBox> occlusionCandidates = OcclusionData.occlusionCandidateBoxes;

    //     // if no potentially occluded boxes then skip this pass
    //     if (occlusionCandidates == null || occlusionCandidates.Count == 0)
    //         return;

    //     Debug.Log("running bbox occlusion prepass (blocking)");

    //     // change this to all boxes once compute shader working properly 
    //     BoundingBox bbox = occlusionCandidates[0];

    //     if (bbox == null || bbox.renderers == null || bbox.renderers.Count == 0)
    //         return;

    //     CommandBuffer cmd = CommandBufferPool.Get("BBox Occlusion Blocking");

    //     // temporary visibility texture
    //     int width = camera.pixelWidth;
    //     int height = camera.pixelHeight;

    //     if (_visibilityRT == null || _visibilityRT.width != width || _visibilityRT.height != height)
    //     {
    //         if (_visibilityRT != null)
    //             _visibilityRT.Release();

    //         _visibilityRT = new RenderTexture(width, height, 0, RenderTextureFormat.R8)
    //         {
    //             enableRandomWrite = false,
    //             filterMode = FilterMode.Point
    //         };
    //         _visibilityRT.Create();
    //     }

    //     // 0 = fully hidden, 1 = visible
    //     _resultData[0] = 0;
    //     _resultBuffer.SetData(_resultData);

    //     // render visibility into RT
    //     cmd.SetRenderTarget(_visibilityRT);
    //     cmd.ClearRenderTarget(false, true, Color.black);

    //     cmd.EnableScissorRect(new Rect(
    //         bbox.box.x,
    //         bbox.box.y,
    //         bbox.box.width,
    //         bbox.box.height
    //     ));

    //     List<Renderer> renderers = bbox.renderers;
    //     for (int i = 0; i < renderers.Count; i++)
    //     {
    //         Renderer r = renderers[i];
    //         if (r == null)
    //             continue;

    //         int submeshCount = r.sharedMaterials != null ? r.sharedMaterials.Length : 1;

    //         for (int sub = 0; sub < submeshCount; sub++)
    //         {
    //             cmd.DrawRenderer(r, _visibilityMat, sub, 0);
    //         }
    //     }

    //     cmd.DisableScissorRect();

    //     // scan visibilityTex and output one 0/1 result
    //     cmd.SetComputeTextureParam(_occlusionCompute, _occlusionKernel, VisibilityTexID, _visibilityRT);
    //     cmd.SetComputeBufferParam(_occlusionCompute, _occlusionKernel, ResultBufferID, _resultBuffer);
    //     cmd.SetComputeVectorParam(_occlusionCompute, RectID,
    //         new Vector4(bbox.box.x, bbox.box.y, bbox.box.width, bbox.box.height));

    //     // shader loops over all pixels in the bbox
    //     cmd.DispatchCompute(_occlusionCompute, _occlusionKernel, 1, 1, 1);

    //     // execute GPU work immediately
    //     context.ExecuteCommandBuffer(cmd);
    //     cmd.Clear();

    //     // BLOCKING readback (this stalls GPU → CPU)
    //     _resultBuffer.GetData(_resultData);

    //     bool anyVisible = _resultData[0] != 0;

    //     if (NprTestingConfig.debugBBoxes)
    //         BBoxOcclusionDebugStore.Clear();

    //     if (!anyVisible)
    //     {
    //         if (NprTestingConfig.debugBBoxes)
    //             BBoxOcclusionDebugStore.Add(bbox.box, Color.red, "Occluded");

    //         OcclusionData.bboxes.RemoveAll(b => b.box == bbox.box);
    //         OcclusionData.occlusionCandidateBoxes.RemoveAll(b => b.box == bbox.box);
    //     }

    //     CommandBufferPool.Release(cmd);
    // }

}