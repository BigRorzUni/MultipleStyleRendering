using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[System.Serializable]
public class IdPrepass : ScriptableRenderPass, INprPass
{
    readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
    readonly FilteringSettings _filteringSettings;
    readonly Shader _idShader;
    private readonly Material _idMat;

    public bool debugToScreen;

    public void ApplySettings(Settings settings)
    {
        debugToScreen = settings.debugView == NprDebugView.StylisedID;
    }  

    class PassData
    {
        public RendererListHandle rendererList;
        public bool debug;
        public Material mat;
        public BoundingBox bbox;
    }

    const string DebugKeyword = "_DEBUG_ID_COLOUR";

    public IdPrepass(Shader idShader)
    {
        _idShader = idShader;
        if (_idShader != null)
            _idMat = CoreUtils.CreateEngineMaterial(_idShader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque)
        {
            renderingLayerMask = StyleBits.ImageSpaceBit
        };
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_idShader == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
        UniversalRenderingData renderingData = frameContext.Get<UniversalRenderingData>();
        UniversalLightData lightData = frameContext.Get<UniversalLightData>();

        // get/create NPR frame data
        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        // match id texture to camera resolution + settings
        RenderTextureDescriptor idTexDescriptor = cameraData.cameraTargetDescriptor;

       // tweak format to fit what an id texture needs
        idTexDescriptor.depthBufferBits = 0;
        idTexDescriptor.msaaSamples = 1;
        idTexDescriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
        idTexDescriptor.sRGB = false;

        // allocate id texture
        TextureHandle idTex = renderGraph.CreateTexture(new TextureDesc(idTexDescriptor)
        {
            name = "_StylisedIDTexture",
            colorFormat = idTexDescriptor.graphicsFormat,
            clearBuffer = true,
            clearColor = Color.black,
            filterMode = FilterMode.Point,
            useMipMap = false
        });
        nprFrameData.idTexture = idTex;

        // FULLSCREEN MODE (RENDER LAYER MASK AND RENDERLISTHANDLE IS THIS FASTER THAN BBOXES?)
        if (!NprTestingConfig.UseBoundingBoxes || !NprTestingConfig.IdBoundingBoxes)
        {
            // Debug.Log("id prepass NOT using bounding boxes");
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                _shaderTagId,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque
            );

            drawing.overrideShader = _idShader;
            drawing.overrideShaderPassIndex = 0;

            RendererListParams rlp = new RendererListParams(
                renderingData.cullResults,
                drawing,
                _filteringSettings
            );

            RendererListHandle rendererList = renderGraph.CreateRendererList(rlp);

            using (var builder = renderGraph.AddRasterRenderPass("Fullscreen ID Prepass", out PassData passData))
            {
                if (debugToScreen)
                    builder.SetRenderAttachment(frameData.activeColorTexture, 0);
                else
                    builder.SetRenderAttachment(nprFrameData.idTexture, 0);

                builder.SetRenderAttachmentDepth(frameData.activeDepthTexture);
                builder.UseRendererList(rendererList);
                builder.AllowGlobalStateModification(true);

                passData.rendererList = rendererList;
                passData.debug = debugToScreen;

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    if (data.debug)
                        ctx.cmd.EnableShaderKeyword(DebugKeyword);
                    else
                        ctx.cmd.DisableShaderKeyword(DebugKeyword);

                    ctx.cmd.DrawRendererList(data.rendererList);

                    if (data.debug)
                        ctx.cmd.DisableShaderKeyword(DebugKeyword);
                });
            }

            return;
        }
        

        // BBOX MODE
        // Debug.Log("id prepass is using bounding boxes");

        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        foreach (var bbox in nprFrameData.bboxes)
        {
            // Debug.Log($"BBox {bbox.box} has {bbox.renderers.Count} renderers");

            if (bbox == null || bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            if (bbox.renderers == null || bbox.renderers.Count == 0)
                continue;

            using (var builder = renderGraph.AddRasterRenderPass($"BBox ID Prepass ({bbox.box})", out PassData passData))
            {
                if (debugToScreen)
                    builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.ReadWrite);
                else
                    builder.SetRenderAttachment(nprFrameData.idTexture, 0, AccessFlags.ReadWrite);

                builder.SetRenderAttachmentDepth(frameData.activeDepthTexture);
                builder.AllowGlobalStateModification(true);

                passData.mat = _idMat;
                passData.bbox = bbox;
                passData.debug = debugToScreen;

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    if (data.debug)
                        ctx.cmd.EnableShaderKeyword(DebugKeyword);
                    else
                        ctx.cmd.DisableShaderKeyword(DebugKeyword);

                    ctx.cmd.EnableScissorRect(new Rect(data.bbox.box.x, data.bbox.box.y, data.bbox.box.width, data.bbox.box.height));

                    List<Renderer> renderers = data.bbox.renderers;
                    for (int i = 0; i < renderers.Count; i++)
                    {
                        // render each renderer in the bbox using the id material
                        Renderer renderer = renderers[i];
                        if (renderer == null)
                            continue;

                        // submesh index is needed for meshes with multiple materials across submeshes
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

                    if (data.debug)
                        ctx.cmd.DisableShaderKeyword(DebugKeyword);
                });
            }
        }
    }
}