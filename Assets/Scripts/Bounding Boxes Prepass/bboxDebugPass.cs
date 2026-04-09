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

    ComputeBuffer _bboxMaskBuffer;
    int _bboxMaskBufferCapacity = 0;
    uint[] _bboxMaskInitData;

    void EnsureMaskBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_bboxMaskBuffer == null || _bboxMaskBufferCapacity < requiredCapacity)
        {
            if (_bboxMaskBuffer != null)
                _bboxMaskBuffer.Release();

            _bboxMaskBufferCapacity = requiredCapacity;
            _bboxMaskBuffer = new ComputeBuffer(_bboxMaskBufferCapacity, sizeof(uint));
        }

        if (_bboxMaskInitData == null || _bboxMaskInitData.Length < _bboxMaskBufferCapacity)
            _bboxMaskInitData = new uint[_bboxMaskBufferCapacity];
    }

    private class BBoxPassData
    {
        public Material mat;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer maskBuffer;
        public Vector4 screenSize;
        public int instanceCount;
    }

    private class OcclusionPassData
    {
        public Material mat;
        public ComputeBuffer rectBuffer;
        public ComputeBuffer visibilityBuffer;
        public Vector4 screenSize;
        public int instanceCount;
    }

    public BboxDebugPass(Shader occlusionShader, Shader bboxShader)
    {
        if(occlusionShader != null)
            _occlusionMat = CoreUtils.CreateEngineMaterial(occlusionShader);
        
        if(bboxShader != null)
            _bboxMat = CoreUtils.CreateEngineMaterial(bboxShader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public void Dispose()
    {
        if (_occlusionMat != null)
            CoreUtils.Destroy(_occlusionMat);

        if(_bboxMat != null)
            CoreUtils.Destroy(_bboxMat);

        if (_bboxMaskBuffer != null)
            _bboxMaskBuffer.Release();
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_occlusionMat == null || !NprTestingConfig.debugBBoxes)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        if (nprFrameData.bboxRectBuffer == null)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        if (_bboxMat != null)
        {
            EnsureMaskBufferCapacity(nprFrameData.bboxVisibilityCount);

            for (int i = 0; i < nprFrameData.bboxVisibilityCount; i++)
            {
                BoundingBox b = nprFrameData.bboxes[i];

                if (!NprTestingConfig.TestMode)
                    _bboxMaskInitData[i] = (uint)b.styles;
                else
                    _bboxMaskInitData[i] = b.testMask;
            }

            _bboxMaskBuffer.SetData(_bboxMaskInitData, 0, 0, nprFrameData.bboxVisibilityCount);

            using (var builder = renderGraph.AddRasterRenderPass("BBox Debug Overlay", out BBoxPassData passData))
            {
                passData.mat = _bboxMat;
                passData.rectBuffer = nprFrameData.bboxRectBuffer;
                passData.maskBuffer = _bboxMaskBuffer;
                passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
                passData.instanceCount = nprFrameData.bboxVisibilityCount;

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (BBoxPassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetBuffer(InstanceBufferID, data.rectBuffer);
                    data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
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

        if(!NprTestingConfig.UseOcclusionCulling)
            return;

        if (nprFrameData.bboxVisibilityBuffer == null)
            return;

        if (nprFrameData.bboxVisibilityCount <= 0)
            return;

        using (var builder = renderGraph.AddRasterRenderPass("Occlusion Debug Overlay", out OcclusionPassData passData))
        {
            passData.mat = _occlusionMat;
            passData.rectBuffer = nprFrameData.bboxRectBuffer;
            passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
            passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
            passData.instanceCount = nprFrameData.bboxVisibilityCount;

            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (OcclusionPassData data, RasterGraphContext ctx) =>
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