using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices;

[System.Serializable]
public class BboxDebugPass : ScriptableRenderPass
{
    private readonly Material _bboxMat;
    private readonly Material _occlusionMat;

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BBoxVisibilityFlags");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int MaskBufferID = Shader.PropertyToID("_BBoxMasks");

    ComputeBuffer _cpuRectBuffer;
    int _cpuRectBufferCapacity = 0;

    ComputeBuffer _cpuMaskBuffer;
    int _cpuMaskBufferCapacity = 0;

    void EnsureCpuRectBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_cpuRectBuffer == null || _cpuRectBufferCapacity < requiredCapacity)
        {
            if (_cpuRectBuffer != null)
                _cpuRectBuffer.Release();

            _cpuRectBufferCapacity = requiredCapacity;
            _cpuRectBuffer = new ComputeBuffer(_cpuRectBufferCapacity, Marshal.SizeOf<Vector4>());
        }
    }

    void EnsureCpuMaskBufferCapacity(int count)
    {
        int requiredCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));

        if (_cpuMaskBuffer == null || _cpuMaskBufferCapacity < requiredCapacity)
        {
            if (_cpuMaskBuffer != null)
                _cpuMaskBuffer.Release();

            _cpuMaskBufferCapacity = requiredCapacity;
            _cpuMaskBuffer = new ComputeBuffer(_cpuMaskBufferCapacity, sizeof(uint));
        }
    }

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

        if (_cpuRectBuffer != null)
            _cpuRectBuffer.Release();

        if (_cpuMaskBuffer != null)
            _cpuMaskBuffer.Release();
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (!NprTestingConfig.DebugBBoxes)
            return;

        if (NprTestingConfig.RenderMode == NprRenderMode.Fullscreen)
            return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        if (!frameContext.Contains<NprFrameData>())
            return;

        NprFrameData nprFrameData = frameContext.Get<NprFrameData>();

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
        Vector4 screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);

        bool gpuMode = NprTestingConfig.RenderMode == NprRenderMode.GPU;
        bool cpuMode = NprTestingConfig.RenderMode == NprRenderMode.CPU;

        ComputeBuffer rectBuffer = null;
        ComputeBuffer maskBuffer = null;
        ComputeBuffer visibilityBuffer = null;
        ComputeBuffer indirectArgsBuffer = null;

        int bboxInstanceCount = 0;
        bool useIndirect = false;

        if (gpuMode || NprTestingConfig.RenderMode == NprRenderMode.Tiling)
        {
            if (nprFrameData.rectBuffer == null || nprFrameData.maskBuffer == null)
                return;

            rectBuffer = nprFrameData.rectBuffer;
            maskBuffer = nprFrameData.maskBuffer;
            visibilityBuffer = nprFrameData.visibilityBuffer;

            indirectArgsBuffer = nprFrameData.indirectArgsBuffer;
            useIndirect = indirectArgsBuffer != null;

            if (!useIndirect)
            {
                bboxInstanceCount = nprFrameData.bboxCount;
            }
        }
        else if (cpuMode)
        {
            if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
                return;

            int count = nprFrameData.bboxes.Count;

            EnsureCpuRectBufferCapacity(count);
            EnsureCpuMaskBufferCapacity(count);

            Vector4[] rectData = new Vector4[count];
            uint[] maskData = new uint[count];

            for (int i = 0; i < count; i++)
            {
                BoundingBox bbox = nprFrameData.bboxes[i];
                rectData[i] = new Vector4(bbox.box.x, bbox.box.y, bbox.box.width, bbox.box.height);

                if (NprTestingConfig.TestMode)
                    maskData[i] = bbox.testMask;
                else
                    maskData[i] = (uint)bbox.styles;
            }

            _cpuRectBuffer.SetData(rectData, 0, 0, count);
            _cpuMaskBuffer.SetData(maskData, 0, 0, count);

            rectBuffer = _cpuRectBuffer;
            maskBuffer = _cpuMaskBuffer;
            visibilityBuffer = nprFrameData.visibilityBuffer;

            bboxInstanceCount = count;
            useIndirect = false;
        }

        if (_bboxMat != null && rectBuffer != null && maskBuffer != null)
        {
            if (useIndirect || bboxInstanceCount > 0)
            {
                using (var builder = renderGraph.AddRasterRenderPass("BBox Debug Overlay", out BBoxPassData passData))
                {
                    passData.mat = _bboxMat;
                    passData.rectBuffer = rectBuffer;
                    passData.maskBuffer = maskBuffer;
                    passData.screenSize = screenSize;
                    passData.instanceCount = bboxInstanceCount;

                    passData.indirectArgsBuffer = indirectArgsBuffer;
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

        if (!NprTestingConfig.UseOcclusion)
            return;

        if (_occlusionMat == null)
            return;

        if (rectBuffer == null || visibilityBuffer == null)
            return;

        if (useIndirect || bboxInstanceCount > 0)
        {
            using (var builder = renderGraph.AddRasterRenderPass("Occlusion Debug Overlay", out OcclusionPassData passData))
            {
                passData.mat = _occlusionMat;
                passData.rectBuffer = rectBuffer;
                passData.visibilityBuffer = visibilityBuffer;
                passData.screenSize = screenSize;
                passData.instanceCount = bboxInstanceCount;

                passData.indirectArgsBuffer = indirectArgsBuffer;
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