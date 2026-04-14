using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[System.Serializable]
public class DummyPass : ScriptableRenderPass
{
    private Material _mat;
    private readonly uint _requiredBit;
    private readonly string _name;

    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int RequiredBitID = Shader.PropertyToID("_RequiredBit");

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int UseOcclusionID = Shader.PropertyToID("_UseOcclusion");
    static readonly int MaskBufferID = Shader.PropertyToID("_BBoxMasks");


    private class PassData
    {
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;
        public uint requiredBit;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;

        public ComputeBuffer visibilityBuffer;
        public int useOcclusion;

        public ComputeBuffer maskBuffer;

        public ComputeBuffer indirectArgsBuffer;
    }

    private class CopyPassData
    {
        public TextureHandle src;
    }

    public DummyPass(Shader shader, string name, int requiredIndex)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _requiredBit = 1u << requiredIndex;
        _name = name;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        if (!nprFrameData.idTexture.IsValid())
            return;
        if (!nprFrameData.sourceTexture.IsValid())
            return;
        if ((nprFrameData.presentTestStyles & _requiredBit) == 0)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        // SOURCE COPY 
        using (var builder = renderGraph.AddRasterRenderPass($"{_name} Source Copy", out CopyPassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc((CopyPassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        switch (NprTestingConfig.RenderMode)
        {
            case NprRenderMode.Fullscreen:
                RunFullscreen(renderGraph, frameData, nprFrameData);
                return;

            case NprRenderMode.CPU:
                // maybe a check for cpu with batched drawing next?
                RunCpu(renderGraph, frameData, nprFrameData);
                return;



            case NprRenderMode.GPU:
                RunGpu(renderGraph, frameData, nprFrameData, camDesc);
                return;
        }
    }

    void RunFullscreen(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData)
    {
        using (var builder = renderGraph.AddRasterRenderPass($"Fullscreen {_name} Pass", out PassData passData))
        {
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.requiredBit = _requiredBit;

            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetInt(RequiredBitID, (int)data.requiredBit);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }
    }

    void RunCpu(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData)
    {
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        // for each bbox in cpu-side list we scissor the texture and apply effect
        foreach (var bbox in nprFrameData.bboxes)
        {
            if ((bbox.testMask & _requiredBit) == 0)
                continue;

            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            using (var builder = renderGraph.AddRasterRenderPass($"BBox {_name}", out PassData passData))
            {
                builder.AllowGlobalStateModification(true);

                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;
                passData.rect = bbox.box;
                passData.requiredBit = _requiredBit;

                passData.useOcclusion = 0;

                if (NprTestingConfig.UseOcclusion && nprFrameData.bboxVisibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.useOcclusion = 1;
                }

                builder.UseTexture(passData.ids, AccessFlags.Read);
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetInt(RequiredBitID, (int)data.requiredBit);

                        ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                        ctx.cmd.DisableScissorRect();
                });
            }
        }
    }

    // TODO: Add CPU batched pass
    // using (var builder = renderGraph.AddRasterRenderPass($"Batched Test Pass (GPU GEN BBOXES)", out PassData passData))
    // {
    //     passData.src = nprFrameData.sourceTexture;
    //     passData.ids = nprFrameData.idTexture;
    //     passData.requiredBit = (int)_RequiredBit;

    //     passData.mat = _mat;


    //     passData.instanceBuffer = nprFrameData.bboxRectBuffer;
    //     passData.screenSize = _screenSize;
    //     passData.instanceCount = nprFrameData.bboxVisibilityCount;

    //     passData.visibilityBuffer = null;
    //     passData.bboxIndexBuffer = null;
    //     passData.useOcclusion = 0;

    //     passData.maskBuffer = nprFrameData.bboxMaskBuffer;
    //     passData.useBboxIndices = 0;

    //     if (NprTestingConfig.OcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
    //     {
    //         passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
    //         passData.useOcclusion = 1;
    //     }

    //     builder.UseTexture(passData.src, AccessFlags.Read);
    //     builder.UseTexture(passData.ids, AccessFlags.Read);
    //     builder.UseTexture(passData.normals, AccessFlags.Read);
    //     builder.UseTexture(passData.depth, AccessFlags.Read);

    //     builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
    //     builder.AllowGlobalStateModification(true);

    //     builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
    //     {
    //         data.mat.SetTexture(SourceTexId, data.src);
    //         data.mat.SetTexture(IdTexId, data.ids);
    //         data.mat.SetInt(RequiredBitID, data.requiredBit);

    //         data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
    //         data.mat.SetVector(ScreenParamsID, data.screenSize);

    //         data.mat.SetInt(UseOcclusionID, data.useOcclusion);

    //         if (data.useOcclusion != 0)
    //             data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);

    //         data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
    //         data.mat.SetInt(UseBboxIndicesID, data.useBboxIndices);

    //         ctx.cmd.DrawProcedural(
    //             Matrix4x4.identity,
    //             data.mat,
    //             0,
    //             MeshTopology.Triangles,
    //             6,
    //             data.instanceCount
    //         );
    //     });
    // }

    // Indirect batching
    void RunGpu(RenderGraph renderGraph, UniversalResourceData frameData, NprFrameData nprFrameData, RenderTextureDescriptor camDesc)
    {
        // GPU path requires fully prepared buffers
        if (nprFrameData.bboxRectBuffer == null ||
            nprFrameData.bboxMaskBuffer == null ||
            nprFrameData.bboxIndirectArgsBuffer == null)
            return;

        Vector4 screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);

        using (var builder = renderGraph.AddRasterRenderPass($"Batched {_name} Pass (GPU)", out PassData passData))
        {
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.instanceBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.indirectArgsBuffer = nprFrameData.bboxIndirectArgsBuffer;

            passData.screenSize = screenSize;

            passData.requiredBit = _requiredBit;

            passData.useOcclusion = 0;

            if (NprTestingConfig.UseOcclusion && nprFrameData.bboxVisibilityBuffer != null)
            {
                passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.useOcclusion = 1;
            }

            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
                data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
                data.mat.SetVector(ScreenParamsID, data.screenSize);
                data.mat.SetInt(RequiredBitID, (int)data.requiredBit);
                data.mat.SetInt(UseOcclusionID, data.useOcclusion);

                if (data.useOcclusion != 0)
                    data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);

                ctx.cmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    data.mat,
                    0,
                    MeshTopology.Triangles,
                    data.indirectArgsBuffer,
                    0
                );
            });
        }
    }
}