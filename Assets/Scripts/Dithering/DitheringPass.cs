using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;


[System.Serializable]
public class DitheringPass : ScriptableRenderPass//, INprPass
{
    Material _mat;
    StyleBits.ImageSpaceEffect _requiredBit;

    static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int BBoxIndicesID = Shader.PropertyToID("_BboxIndices");
    static readonly int UseOcclusionID = Shader.PropertyToID("_UseOcclusion");
    static readonly int CurrentBBoxIndexID = Shader.PropertyToID("_CurrentBboxIndex");

    readonly List<Material> _tempMaterials = new();

    ComputeBuffer _bboxIndexBuffer;
    int _bboxIndexBufferCapacity = 0;
    ComputeBuffer _instanceBuffer;
    int _instanceBufferCapacity = 0;

    // make sure the compute buffer is big enough for the given instance count
    void EnsureInstanceBufferCapacity(int count)
    {
        if (_instanceBuffer != null && _instanceBufferCapacity >= count)
            return;

        if (_instanceBuffer != null)
            _instanceBuffer.Release();

        _instanceBufferCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));
        _instanceBuffer = new ComputeBuffer(_instanceBufferCapacity, Marshal.SizeOf<QuadInstanceData>());
    }
    void EnsureIndexBufferCapacity(int count)
    {
        if (_bboxIndexBuffer != null && _bboxIndexBufferCapacity >= count)
            return;

        if (_bboxIndexBuffer != null)
            _bboxIndexBuffer.Release();

        _bboxIndexBufferCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, count));
        _bboxIndexBuffer = new ComputeBuffer(_bboxIndexBufferCapacity, sizeof(uint));
    }

    class PassData
    {
        public TextureHandle src;
        public TextureHandle ids;
        public Material mat;
        public RectInt rect;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;
        public int instanceCount;

        public ComputeBuffer visibilityBuffer;
        public ComputeBuffer bboxIndexBuffer;
        public int useOcclusion;
        public int currentBBoxIndex;
    }

    public DitheringPass(Shader shader, StyleBits.ImageSpaceEffect requiredBit)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _requiredBit = requiredBit;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        if (_mat == null) return;

        UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

        NprFrameData nprFrameData;
            if (frameContext.Contains<NprFrameData>())
                nprFrameData = frameContext.Get<NprFrameData>();
            else
                nprFrameData = frameContext.Create<NprFrameData>();

        if (!nprFrameData.idTexture.IsValid())
            return;
        if(!nprFrameData.sourceTexture.IsValid())
            return;
        if ((nprFrameData.presentImageBits & _requiredBit) == 0)
            return;

        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

        // copy camera color into srcCopy
        using (var builder = renderGraph.AddRasterRenderPass("NPR Dither Source Copy", out PassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        if(!NprTestingConfig.UseBoundingBoxes)
        {
            // dithering pass
            using (var builder = renderGraph.AddRasterRenderPass("Fullscreen Dithering Pass", out PassData passData))
            {
                // write to screen colour
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                // read from id and screen textures
                builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
                builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);

                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.mat = _mat;

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);

                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                });
            }

            return;
        }

        if(nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;
            

        if (!NprTestingConfig.BatchedDraws)
        {
            // current per-bbox scissored path
            foreach(var bbox in nprFrameData.bboxes)
            {
                if (bbox.box.width <= 0 || bbox.box.height <= 0)
                    continue;
                
                if((bbox.styles & StyleBits.ImageSpaceEffect.Dithering) == 0)
                    continue;

                int index = nprFrameData.bboxes.IndexOf(bbox);
                // Debug.Log($"Scheduling dithering pass for bbox {index} at {bbox.box} with effect bits {bbox.styles}");

                using (var builder = renderGraph.AddRasterRenderPass($"BBox Dithering ({index})", out PassData passData))
                {
                    // global state modification 
                    builder.AllowGlobalStateModification(true);

                    passData.src = nprFrameData.sourceTexture;
                    passData.ids = nprFrameData.idTexture;

                    if(NprTestingConfig.UseOcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                    {
                        Material perPassMat = new Material(_mat);
                        _tempMaterials.Add(perPassMat);
                        passData.mat = perPassMat;
                    }
                    else
                    {
                        passData.mat = _mat;
                    }
                    passData.rect = bbox.box;

                    passData.visibilityBuffer = null;
                    passData.currentBBoxIndex = index;
                    passData.useOcclusion = 0;

                    if (NprTestingConfig.UseOcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                    {
                        passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                        passData.useOcclusion = 1;
                    }

                    builder.UseTexture(passData.src, AccessFlags.Read);
                    builder.UseTexture(passData.ids, AccessFlags.Read);

                    builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        data.mat.SetTexture(SourceTexID, data.src);
                        data.mat.SetTexture(IdTexId, data.ids);

                        data.mat.SetInt(UseOcclusionID, data.useOcclusion);
                        data.mat.SetInt(CurrentBBoxIndexID, data.currentBBoxIndex);

                        if (data.useOcclusion != 0)
                            data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);

                        ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                        ctx.cmd.DisableScissorRect();
                    });
                }
            }

            return;
        }
            // new batched instanced path
            // Debug.Log("Batched dithering pass");

            List<BoundingBox> batchedBBoxes = new List<BoundingBox>();
            List<QuadInstanceData> instances = new List<QuadInstanceData>();
            List<uint> bboxIndices = new List<uint>();

            foreach (var bbox in nprFrameData.bboxes)
            {
                if (bbox.box.width <= 0 || bbox.box.height <= 0)
                    continue;

                if ((bbox.styles & StyleBits.ImageSpaceEffect.Dithering) == 0)
                    continue;

                batchedBBoxes.Add(bbox);

                instances.Add(new QuadInstanceData
                {
                    rect = new Vector4(bbox.box.x, bbox.box.y, bbox.box.width, bbox.box.height)
                });

                bboxIndices.Add((uint)nprFrameData.bboxes.IndexOf(bbox));
            }

            if (instances.Count == 0)
                return;

            EnsureInstanceBufferCapacity(instances.Count);
            _instanceBuffer.SetData(instances);

            EnsureIndexBufferCapacity(bboxIndices.Count);
            _bboxIndexBuffer.SetData(bboxIndices);

            using (var builder = renderGraph.AddRasterRenderPass("Batched Dithering Pass", out PassData passData))
            {
                passData.mat = _mat;

                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;

                passData.instanceBuffer = _instanceBuffer;
                passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
                passData.instanceCount = instances.Count;

                passData.visibilityBuffer = null;
                passData.bboxIndexBuffer = null;
                passData.useOcclusion = 0;

                if (NprTestingConfig.UseOcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.bboxIndexBuffer = _bboxIndexBuffer;
                    passData.useOcclusion = 1;
                }

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexID, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetBuffer(InstanceBufferID,data.instanceBuffer);
                    data.mat.SetVector(ScreenParamsID, data.screenSize);
                    data.mat.SetInt(UseOcclusionID, data.useOcclusion);

                    if (data.useOcclusion != 0)
                    {
                        data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);
                        data.mat.SetBuffer(BBoxIndicesID, data.bboxIndexBuffer);
                    }

                    ctx.cmd.DrawProcedural(
                        Matrix4x4.identity,
                        data.mat, // dithering material
                        0,
                        MeshTopology.Triangles,
                        6, // 2 triangles per quad
                        data.instanceCount // 1 instance per bbox
                    );

                });
            }
   }

   public void Dispose()
    {
        if (_instanceBuffer != null)
            _instanceBuffer.Release();

        if (_bboxIndexBuffer != null)
            _bboxIndexBuffer.Release();

        for (int i = 0; i < _tempMaterials.Count; i++)
        {
            if (_tempMaterials[i] != null)
                CoreUtils.Destroy(_tempMaterials[i]);
        }
        
        _tempMaterials.Clear();
    }
}