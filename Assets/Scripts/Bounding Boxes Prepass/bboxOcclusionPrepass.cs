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

    private readonly ComputeBuffer[] _resultBuffers = new ComputeBuffer[2];
    private readonly uint[] _resultData = new uint[1];

    private int _writeIndex = 0;
    private readonly bool[] _pendingReadback = new bool[2];
    private readonly RectInt[] _pendingRects = new RectInt[2];

    private readonly List<RectInt> _pendingRemovalRects = new List<RectInt>();

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
    }

    public void Dispose()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_resultBuffers[i] != null)
            {
                _resultBuffers[i].Release();
                _resultBuffers[i] = null;
            }
        }
}

    public BBoxOcclusionPrepass(Shader visibilityShader, ComputeShader occlusionComputeShader)
    {
        if (visibilityShader != null)
            _visibilityMat = CoreUtils.CreateEngineMaterial(visibilityShader);

        _occlusionCompute = occlusionComputeShader;
        if (_occlusionCompute != null)
        {
            _occlusionKernel = _occlusionCompute.FindKernel("OcclusionCheck");
        }

        for (int i = 0; i < 2; i++)
        {
            _resultBuffers[i] = new ComputeBuffer(1, sizeof(uint));
            _resultBuffers[i].SetData(_resultData);
        }

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

        if (_pendingRemovalRects.Count > 0)
        {
            nprFrameData.bboxes.RemoveAll(b => _pendingRemovalRects.Contains(b.box));
            nprFrameData.occlusionCandidateBoxes.RemoveAll(b => _pendingRemovalRects.Contains(b.box));
            _pendingRemovalRects.Clear();
        }

        // if no potentially occluded boxes then skip this pass
        if (nprFrameData.occlusionCandidateBoxes == null || nprFrameData.occlusionCandidateBoxes.Count == 0)
            return;

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

        // read visibility from previous frame if available
        int readIndex = 1 - _writeIndex;

        // Debug.Log("reading back results from prev frame");
        // Debug.Log($"pending readback for index {readIndex} = {_pendingReadback[readIndex]}");
        if (_pendingReadback[readIndex])
        {

            RectInt testedRect = _pendingRects[readIndex];
            ComputeBuffer readBuffer = _resultBuffers[readIndex];
            

            AsyncGPUReadback.Request(readBuffer, request =>
            {
                if (request.hasError)
                    return;

                var data = request.GetData<uint>();
                // Debug.Log($"raw value = {data[0]}");

                bool anyVisible = data.Length > 0 && data[0] != 0;

                if (NprTestingConfig.debugBBoxes)
                    BBoxOcclusionDebugStore.Clear();

                if (!anyVisible)
                {
                    if (NprTestingConfig.debugBBoxes)
                        BBoxOcclusionDebugStore.Add(testedRect, Color.red, "Occluded");

                    _pendingRemovalRects.Add(testedRect);
                }
            });

            _pendingReadback[readIndex] = false;
        }

        // Debug.Log($"occlusion candidate count = {nprFrameData.occlusionCandidateBoxes.Count}");
        // Debug.Log($"testing candidate bbox = {bbox.box}");

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

        //     passData.src = visibilityTex;

        //     builder.SetRenderFunc(static (DebugPassData data, RasterGraphContext ctx) =>
        //     {
        //         Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
        //     });
        // }

        // feed visibilityTex into a compute shader that checks whether any pixel is nonzero
        
        // 0 = fully hidden, 1 = visible
        _resultData[0] = 0;
        _resultBuffers[_writeIndex].SetData(_resultData);

        // scan visibilityTex and output one 0/1 result
        using (var builder = renderGraph.AddComputePass("BBox Occlusion Analyse", out ComputePassData passData))
        {
            builder.AllowPassCulling(false);

            passData.visibilityTex = visibilityTex;
            passData.resultBuffer = _resultBuffers[_writeIndex];
            passData.rect = bbox.box;
            passData.compute = _occlusionCompute;
            passData.kernel = _occlusionKernel;

            // Debug.Log($"compute dispatch for rect {passData.rect}");

            builder.UseTexture(passData.visibilityTex, AccessFlags.Read);

            builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
            {
                ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, VisibilityTexID, data.visibilityTex);
                ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, ResultBufferID, data.resultBuffer);
                ctx.cmd.SetComputeVectorParam(data.compute, RectID, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));

                // shader loops over all pixels in the bbox
                ctx.cmd.DispatchCompute(data.compute, data.kernel, 1, 1, 1);

            });

            _pendingRects[_writeIndex] = bbox.box;
            _pendingReadback[_writeIndex] = true;

            _writeIndex = 1 - _writeIndex;
        }

        // if box has been occluded then remove it from nprFrameData.bboxes so it doesn't get rendered in main pass


        // do the same for all occlusion candidates
    }


}