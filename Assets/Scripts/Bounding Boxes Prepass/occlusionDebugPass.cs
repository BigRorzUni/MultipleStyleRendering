using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class OcclusionDebugPass : ScriptableRenderPass
{
    private readonly Material _debugMat;

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BBoxVisibilityFlags");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");

    private class PassData
    {
        public Material mat;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer visibilityBuffer;
        public Vector4 screenSize;
        public int instanceCount;
    }

    public OcclusionDebugPass(Shader debugShader)
    {
        if (debugShader != null)
            _debugMat = CoreUtils.CreateEngineMaterial(debugShader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public void Dispose()
    {
        if (_debugMat != null)
            CoreUtils.Destroy(_debugMat);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_debugMat == null || !NprTestingConfig.debugBBoxes)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        if (nprFrameData.bboxRectBuffer == null)
            return;

        if (nprFrameData.bboxVisibilityBuffer == null)
            return;

        if (nprFrameData.bboxVisibilityCount <= 0)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        using (var builder = renderGraph.AddRasterRenderPass("Occlusion Debug Overlay", out PassData passData))
        {
            passData.mat = _debugMat;
            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
            passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
            passData.instanceCount = nprFrameData.bboxVisibilityCount;

            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetBuffer(InstanceBufferID, data.rectBuffer);
                data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);
                data.mat.SetVector(ScreenParamsID, data.screenSize);

                ctx.cmd.DrawProcedural(
                    Matrix4x4.identity,
                    data.mat,
                    0,
                    MeshTopology.Triangles,
                    6,
                    data.instanceCount
                );
            });
        }
    }
}