using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class DummyPass : EffectPass
{
    private readonly uint _testRequiredBit;

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

    public DummyPass(Shader shader, string name, int requiredIndex) : base(shader, name, StyleBits.ImageSpaceEffect.None)
    {
        _testRequiredBit = 1u << requiredIndex;
    }

    protected override bool ShouldRun(
        UniversalResourceData frameData,
        UniversalCameraData cameraData,
        NprFrameData nprFrameData)
    {
        if (_mat == null)
            return false;

        if (!nprFrameData.idTexture.IsValid())
            return false;

        if ((nprFrameData.presentTestStyles & _testRequiredBit) == 0)
            return false;

        return true;
    }

    protected override void RunFullscreen(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        using (var builder = renderGraph.AddRasterRenderPass($"Fullscreen {PassName} Pass", out PassData passData, profilingSampler))
        {
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.requiredBit = _testRequiredBit;

            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetInt(RequiredBitID, (int)data.requiredBit);

                CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
            });
        }
    }

    protected override void RunCpu(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        foreach (var bbox in nprFrameData.bboxes)
        {
            if ((bbox.testMask & _testRequiredBit) == 0)
                continue;

            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            using (var builder = renderGraph.AddRasterRenderPass($"BBox {PassName}", out PassData passData, profilingSampler))
            {
                builder.AllowGlobalStateModification(true);

                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;
                passData.rect = bbox.box;
                passData.requiredBit = _testRequiredBit;

                passData.useOcclusion = 0;

                if (NprTestingConfig.UseOcclusion && nprFrameData.bboxVisibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.useOcclusion = 1;
                }

                builder.UseTexture(passData.ids, AccessFlags.Read);
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
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

    protected override void RunGpu(RenderGraph renderGraph, UniversalResourceData frameData, UniversalCameraData cameraData, NprFrameData nprFrameData)
    {
        if (nprFrameData.bboxRectBuffer == null || nprFrameData.bboxMaskBuffer == null || nprFrameData.bboxIndirectArgsBuffer == null)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
        Vector4 screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);

        using (var builder = renderGraph.AddRasterRenderPass($"Batched {PassName} Pass (GPU)", out PassData passData, profilingSampler))
        {
            passData.ids = nprFrameData.idTexture;
            passData.mat = _mat;
            passData.instanceBuffer = nprFrameData.bboxRectBuffer;
            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.indirectArgsBuffer = nprFrameData.bboxIndirectArgsBuffer;
            passData.screenSize = screenSize;
            passData.requiredBit = _testRequiredBit;

            passData.useOcclusion = 0;

            if (NprTestingConfig.UseOcclusion && nprFrameData.bboxVisibilityBuffer != null)
            {
                passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.useOcclusion = 1;
            }

            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
                data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
                data.mat.SetVector(ScreenParamsID, data.screenSize);
                data.mat.SetInt(RequiredBitID, (int)data.requiredBit);
                data.mat.SetInt(UseOcclusionID, data.useOcclusion);

                if (data.useOcclusion != 0)
                    data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);

                ctx.cmd.DrawProceduralIndirect(Matrix4x4.identity, data.mat, 0, MeshTopology.Triangles, data.indirectArgsBuffer, 0);
            });
        }
    }
}