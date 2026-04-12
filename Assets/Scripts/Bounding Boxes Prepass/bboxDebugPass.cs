using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class BboxDebugPass : ScriptableRenderPass
{
    private readonly Material _bboxMat;
    private readonly Material _occlusionMat;

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BBoxVisibilityFlags");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int MaskBufferID = Shader.PropertyToID("_BBoxMasks");

    private class BBoxPassData
    {
        public Material mat;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public Vector4 screenSize;
        public int instanceCount;

        public ComputeBuffer indirectArgsBuffer;
        public int useIndirect;
    }

    private class OcclusionPassData
    {
        public Material mat;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer visibilityBuffer;
        public Vector4 screenSize;
        public int instanceCount;

        public ComputeBuffer indirectArgsBuffer;
        public int useIndirect;
    }

    public BboxDebugPass(Shader occlusionShader, Shader bboxShader)
    {
        if (occlusionShader != null)
            _occlusionMat = CoreUtils.CreateEngineMaterial(occlusionShader);

        if (bboxShader != null)
            _bboxMat = CoreUtils.CreateEngineMaterial(bboxShader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public void Dispose()
    {
        if (_occlusionMat != null)
            CoreUtils.Destroy(_occlusionMat);

        if (_bboxMat != null)
            CoreUtils.Destroy(_bboxMat);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (!NprTestingConfig.DebugBBoxes)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        if (nprFrameData.bboxRectBuffer == null)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        bool useIndirect = nprFrameData.bboxIndirectArgsBuffer != null;

        if (_bboxMat != null && nprFrameData.bboxMaskBuffer != null)
        {
            int bboxInstanceCount = 0;

            if (!useIndirect)
            {
                if (nprFrameData.bboxCount <= 0)
                    bboxInstanceCount = 0;
                else
                    bboxInstanceCount = nprFrameData.bboxCount;
            }

            if (useIndirect || bboxInstanceCount > 0)
            {
                using (var builder = renderGraph.AddRasterRenderPass("BBox Debug Overlay", out BBoxPassData passData))
                {
                    passData.mat = _bboxMat;
                    passData.rectBuffer = nprFrameData.bboxRectBuffer;
                    passData.maskBuffer = nprFrameData.bboxMaskBuffer;
                    passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
                    passData.instanceCount = bboxInstanceCount;

                    passData.indirectArgsBuffer = nprFrameData.bboxIndirectArgsBuffer;
                    passData.useIndirect = useIndirect ? 1 : 0;

                    builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (BBoxPassData data, RasterGraphContext ctx) =>
                    {
                        data.mat.SetBuffer(InstanceBufferID, data.rectBuffer);
                        data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
                        data.mat.SetVector(ScreenParamsID, data.screenSize);

                        if (data.useIndirect != 0 && data.indirectArgsBuffer != null)
                        {
                            ctx.cmd.DrawProceduralIndirect(
                                Matrix4x4.identity,
                                data.mat,
                                0,
                                MeshTopology.Triangles,
                                data.indirectArgsBuffer,
                                0
                            );
                        }
                        else
                        {
                            ctx.cmd.DrawProcedural(
                                Matrix4x4.identity,
                                data.mat,
                                0,
                                MeshTopology.Triangles,
                                6,
                                data.instanceCount
                            );
                        }
                    });
                }
            }
        }

        if (!NprTestingConfig.OcclusionCulling)
            return;

        if (_occlusionMat == null)
            return;

        if (nprFrameData.bboxVisibilityBuffer == null)
            return;

        int occlusionInstanceCount = 0;

        if (!useIndirect)
        {
            if (nprFrameData.bboxVisibilityCount <= 0)
                occlusionInstanceCount = 0;
            else
                occlusionInstanceCount = nprFrameData.bboxVisibilityCount;
        }

        if (useIndirect || occlusionInstanceCount > 0)
        {
            using (var builder = renderGraph.AddRasterRenderPass("Occlusion Debug Overlay", out OcclusionPassData passData))
            {
                passData.mat = _occlusionMat;
                passData.rectBuffer = nprFrameData.bboxRectBuffer;
                passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
                passData.instanceCount = occlusionInstanceCount;

                passData.indirectArgsBuffer = nprFrameData.bboxIndirectArgsBuffer;
                passData.useIndirect = useIndirect ? 1 : 0;

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (OcclusionPassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetBuffer(InstanceBufferID, data.rectBuffer);
                    data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);
                    data.mat.SetVector(ScreenParamsID, data.screenSize);

                    if (data.useIndirect != 0 && data.indirectArgsBuffer != null)
                    {
                        ctx.cmd.DrawProceduralIndirect(
                            Matrix4x4.identity,
                            data.mat,
                            0,
                            MeshTopology.Triangles,
                            data.indirectArgsBuffer,
                            0
                        );
                    }
                    else
                    {
                        ctx.cmd.DrawProcedural(
                            Matrix4x4.identity,
                            data.mat,
                            0,
                            MeshTopology.Triangles,
                            6,
                            data.instanceCount
                        );
                    }
                });
            }
        }
    }
}