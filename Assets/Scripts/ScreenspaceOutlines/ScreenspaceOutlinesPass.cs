using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;


[System.Serializable]
public class ScreenspaceOutlinesPass : ScriptableRenderPass, INprPass
{
    StyleBits.ImageSpaceEffect _outlinesBit;
    Material _mat;

    static readonly int DepthTexId = Shader.PropertyToID("_NprDepthTexture");
    static readonly int NormalsTexId = Shader.PropertyToID("_NprNormalsTexture");
    static readonly int IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int SourceTexId = Shader.PropertyToID("_NprSourceTexture");
    static readonly int OutlinesBitID = Shader.PropertyToID("_OutlinesBit");

    static readonly int ThicknessId = Shader.PropertyToID("_ThicknessPx");
    static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    static readonly int DepthStrengthId = Shader.PropertyToID("_DepthStrength");
    static readonly int NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
    static readonly int NormalStrengthId = Shader.PropertyToID("_NormalStrength");
    static readonly int OutlineColourId = Shader.PropertyToID("_OutlineColour");
    
    float _depthThreshold = 0.02f;
    float _depthStrength = 1.0f;
    float _normalThreshold = 0.2f;
    float _normalStrength = 1.0f;
    public Color _outlineColour;
    public float _outlineThickness;

    readonly List<Material> _tempMaterials = new();

    ComputeBuffer _instanceBuffer;
    int _instanceBufferCapacity = 0;
    ComputeBuffer _bboxIndexBuffer;
    int _bboxIndexBufferCapacity = 0;

    static readonly int InstanceBufferID = Shader.PropertyToID("_InstanceData");
    static readonly int ScreenParamsID = Shader.PropertyToID("_NprScreenSize");
    static readonly int VisibilityFlagsID = Shader.PropertyToID("_BboxVisibilityFlags");
    static readonly int BBoxIndicesID = Shader.PropertyToID("_BboxIndices");
    static readonly int UseOcclusionID = Shader.PropertyToID("_UseOcclusion");
    static readonly int CurrentBBoxIndexID = Shader.PropertyToID("_CurrentBboxIndex");
    static readonly int MaskBufferID = Shader.PropertyToID("_BBoxMasks");
    static readonly int UseBboxIndicesID = Shader.PropertyToID("_UseBboxIndices");

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

    public void ApplySettings(Settings settings)
    {
        _outlineColour = settings.outlines.colour;
        _outlineThickness = settings.outlines.thickness;
        _depthThreshold = settings.outlines.depthThreshold;
        _depthStrength = settings.outlines.depthStrength;
        _normalThreshold = settings.outlines.normalThreshold;
        _normalStrength = settings.outlines.normalStrength;
    }

    class PassData
    {
        public TextureHandle depth;
        public TextureHandle normals;
        public TextureHandle ids;
        public TextureHandle src;
        public int requiredBit;

        public Material mat;

        public Color outlineCol;
        public float thicknessPx;
        public float depthThreshold;
        public float depthStrength;
        public float normalThreshold;
        public float normalStrength;

        public RectInt rect;

        public ComputeBuffer instanceBuffer;
        public Vector4 screenSize;
        public int instanceCount;

        public ComputeBuffer visibilityBuffer;
        public ComputeBuffer bboxIndexBuffer;
        public int useOcclusion;
        public int currentBBoxIndex;

        public ComputeBuffer maskBuffer;
        public int useBboxIndices;
    }

    public ScreenspaceOutlinesPass(Shader shader, StyleBits.ImageSpaceEffect requiredBit)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        _outlinesBit = requiredBit;
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

        if (!nprFrameData.sourceTexture.IsValid())  
            return;
        if (!nprFrameData.idTexture.IsValid())      
            return;
        if (!nprFrameData.normalsTexture.IsValid()) 
            return;
        // using urp camera depth texture
        if (!frameData.activeDepthTexture.IsValid()) 
            return;
        if ((nprFrameData.presentImageBits & _outlinesBit) == 0)
            return;

        var camDesc = cameraData.cameraTargetDescriptor;
        Vector2 screenTexelSize = new Vector2(1f / camDesc.width, 1f / camDesc.height);

        // copy camera color into srcCopy
        using (var builder = renderGraph.AddRasterRenderPass("NPR Outlines Source Copy", out PassData copyPass))
        {
            builder.SetRenderAttachment(nprFrameData.sourceTexture, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        if(!NprTestingConfig.BoundingBoxes)
        {
            // FULLSCREEN MODE: ignore all bbox usage and just draw a single fullscreen pass    
            using (var builder = renderGraph.AddRasterRenderPass("Fullscreen Outline Pass", out PassData passData))
            {
                // write to bbox colour
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                // passData.source = nprFrameData.sourceTexture;
                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.normals = nprFrameData.normalsTexture;
                passData.depth = frameData.activeDepthTexture;
                passData.requiredBit = (int)_outlinesBit;

                passData.mat = _mat;

                passData.outlineCol = _outlineColour;
                passData.thicknessPx = _outlineThickness;
                passData.depthThreshold = _depthThreshold;
                passData.depthStrength = _depthStrength;
                passData.normalThreshold = _normalThreshold;
                passData.normalStrength = _normalStrength;

                // read from normal, id, depth and source textures
                // builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);
                builder.UseTexture(passData.normals, AccessFlags.Read);
                builder.UseTexture(passData.depth, AccessFlags.Read);


                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexId, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetTexture(NormalsTexId, data.normals);
                    data.mat.SetTexture(DepthTexId, data.depth);
                    data.mat.SetInt(OutlinesBitID, data.requiredBit);

                    data.mat.SetColor(OutlineColourId, data.outlineCol);

                    data.mat.SetFloat(ThicknessId, data.thicknessPx);
                    data.mat.SetFloat(DepthThresholdId, data.depthThreshold);
                    data.mat.SetFloat(DepthStrengthId, data.depthStrength);
                    data.mat.SetFloat(NormalThresholdId, data.normalThreshold);
                    data.mat.SetFloat(NormalStrengthId, data.normalStrength);

                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                });
            }

            return;
        }

        if(nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        if (!NprTestingConfig.BatchedDraws)
        {
            foreach(var bbox in nprFrameData.bboxes)
            {
                if (bbox.box.width <= 0 || bbox.box.height <= 0)
                    continue;
                
                if((bbox.styles & StyleBits.ImageSpaceEffect.Outline) == 0)
                    continue;

                int index = nprFrameData.bboxes.IndexOf(bbox);
                using (var builder = renderGraph.AddRasterRenderPass($"BBox Outline ({bbox.box})", out PassData passData))
                {
                    passData.src = nprFrameData.sourceTexture;
                    passData.ids = nprFrameData.idTexture;
                    passData.normals = nprFrameData.normalsTexture;
                    passData.depth = frameData.activeDepthTexture; // provided by urp camera depth texture
                    passData.rect = bbox.box;
                    passData.requiredBit = (int)_outlinesBit;

                    if(NprTestingConfig.OcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                    {
                        Material perPassMat = new Material(_mat);
                        _tempMaterials.Add(perPassMat);
                        passData.mat = perPassMat;
                    }
                    else
                    {
                        passData.mat = _mat;
                    }

                    passData.outlineCol = _outlineColour;
                    passData.thicknessPx = _outlineThickness;
                    passData.depthThreshold = _depthThreshold;
                    passData.depthStrength = _depthStrength;
                    passData.normalThreshold = _normalThreshold;
                    passData.normalStrength = _normalStrength;

                    passData.visibilityBuffer = null;
                    passData.currentBBoxIndex = index; 
                    passData.useOcclusion = 0;


                    if (NprTestingConfig.OcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                    {
                        passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                        passData.useOcclusion = 1;
                    }

                    // read from normal, id, depth and source textures
                    builder.UseTexture(passData.src, AccessFlags.Read);
                    builder.UseTexture(passData.ids, AccessFlags.Read);
                    builder.UseTexture(passData.normals, AccessFlags.Read);
                    builder.UseTexture(passData.depth, AccessFlags.Read);

                    // write to bbox colour
                    builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        data.mat.SetTexture(SourceTexId, data.src);
                        data.mat.SetTexture(IdTexId, data.ids);
                        data.mat.SetTexture(NormalsTexId, data.normals);
                        data.mat.SetTexture(DepthTexId, data.depth);
                        data.mat.SetInt(OutlinesBitID, data.requiredBit);

                        data.mat.SetColor(OutlineColourId, data.outlineCol);

                        data.mat.SetFloat(ThicknessId, data.thicknessPx);
                        data.mat.SetFloat(DepthThresholdId, data.depthThreshold);
                        data.mat.SetFloat(DepthStrengthId, data.depthStrength);
                        data.mat.SetFloat(NormalThresholdId, data.normalThreshold);
                        data.mat.SetFloat(NormalStrengthId, data.normalStrength);

                        data.mat.SetInt(UseOcclusionID, data.useOcclusion);
                        data.mat.SetInt(CurrentBBoxIndexID, data.currentBBoxIndex);

                        if (data.useOcclusion != 0 && data.visibilityBuffer != null)
                            data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);

                        ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                        CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                        ctx.cmd.DisableScissorRect();
                    });
                }
            }

            return;
        }

        if (NprTestingConfig.BatchedBBoxGeneration)
        {
            using (var builder = renderGraph.AddRasterRenderPass($"Batched Outline Pass (GPU GEN BBOXES)", out PassData passData))
            {
                passData.src = nprFrameData.sourceTexture;
                passData.ids = nprFrameData.idTexture;
                passData.normals = nprFrameData.normalsTexture;
                passData.depth = frameData.activeDepthTexture;
                passData.requiredBit = (int)_outlinesBit;

                passData.mat = _mat;

                passData.outlineCol = _outlineColour;
                passData.thicknessPx = _outlineThickness;
                passData.depthThreshold = _depthThreshold;
                passData.depthStrength = _depthStrength;
                passData.normalThreshold = _normalThreshold;
                passData.normalStrength = _normalStrength;

                passData.instanceBuffer = nprFrameData.bboxRectBuffer;
                passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
                passData.instanceCount = nprFrameData.bboxVisibilityCount;

                passData.visibilityBuffer = null;
                passData.bboxIndexBuffer = null;
                passData.useOcclusion = 0;

                passData.maskBuffer = nprFrameData.bboxMaskBuffer;
                passData.useBboxIndices = 0;

                if (NprTestingConfig.OcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
                {
                    passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                    passData.useOcclusion = 1;
                }

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.UseTexture(passData.ids, AccessFlags.Read);
                builder.UseTexture(passData.normals, AccessFlags.Read);
                builder.UseTexture(passData.depth, AccessFlags.Read);

                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(SourceTexId, data.src);
                    data.mat.SetTexture(IdTexId, data.ids);
                    data.mat.SetTexture(NormalsTexId, data.normals);
                    data.mat.SetTexture(DepthTexId, data.depth);
                    data.mat.SetInt(OutlinesBitID, data.requiredBit);

                    data.mat.SetColor(OutlineColourId, data.outlineCol);

                    data.mat.SetFloat(ThicknessId, data.thicknessPx);
                    data.mat.SetFloat(DepthThresholdId, data.depthThreshold);
                    data.mat.SetFloat(DepthStrengthId, data.depthStrength);
                    data.mat.SetFloat(NormalThresholdId, data.normalThreshold);
                    data.mat.SetFloat(NormalStrengthId, data.normalStrength);

                    data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
                    data.mat.SetVector(ScreenParamsID, data.screenSize);

                    data.mat.SetInt(UseOcclusionID, data.useOcclusion);

                    if (data.useOcclusion != 0)
                        data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);

                    data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
                    data.mat.SetInt(UseBboxIndicesID, data.useBboxIndices);

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

            return;
        }

        // Debug.Log("Batched outline pass");
        List<uint> bboxIndices = new List<uint>();

        foreach (var bbox in nprFrameData.bboxes)
        {
            int index = nprFrameData.bboxes.IndexOf(bbox);

            if ((bbox.styles & StyleBits.ImageSpaceEffect.Outline) == 0)
                continue;

            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;

            bboxIndices.Add((uint)index);
        }

        if (bboxIndices.Count == 0)
            return;

        EnsureIndexBufferCapacity(bboxIndices.Count);
        _bboxIndexBuffer.SetData(bboxIndices);

        using (var builder = renderGraph.AddRasterRenderPass($"Batched Outline Pass (CPU GEN BBOXES)", out PassData passData))
        {
            passData.src = nprFrameData.sourceTexture;
            passData.ids = nprFrameData.idTexture;
            passData.normals = nprFrameData.normalsTexture;
            passData.depth = frameData.activeDepthTexture;
            passData.requiredBit = (int)_outlinesBit;

            passData.mat = _mat;

            passData.outlineCol = _outlineColour;
            passData.thicknessPx = _outlineThickness;
            passData.depthThreshold = _depthThreshold;
            passData.depthStrength = _depthStrength;
            passData.normalThreshold = _normalThreshold;
            passData.normalStrength = _normalStrength;

            List<QuadInstanceData> instanceData = new List<QuadInstanceData>();

            foreach (uint bboxIndex in bboxIndices)
            {
                BoundingBox bbox = nprFrameData.bboxes[(int)bboxIndex];
                instanceData.Add(new QuadInstanceData
                {
                    rect = new Vector4(bbox.box.x, bbox.box.y, bbox.box.width, bbox.box.height)
                });
            }

            EnsureInstanceBufferCapacity(instanceData.Count);
            _instanceBuffer.SetData(instanceData);
            passData.instanceBuffer = _instanceBuffer;

            passData.screenSize = new Vector4(camDesc.width, camDesc.height, 1f / camDesc.width, 1f / camDesc.height);
            passData.instanceCount = bboxIndices.Count;

            passData.visibilityBuffer = null;
            passData.bboxIndexBuffer = null;
            passData.useOcclusion = 0;

            passData.maskBuffer = nprFrameData.bboxMaskBuffer;
            passData.useBboxIndices = 1;

            if (NprTestingConfig.OcclusionCulling && nprFrameData.bboxVisibilityBuffer != null)
            {
                passData.visibilityBuffer = nprFrameData.bboxVisibilityBuffer;
                passData.bboxIndexBuffer = _bboxIndexBuffer;
                passData.useOcclusion = 1;
            }

            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.UseTexture(passData.ids, AccessFlags.Read);
            builder.UseTexture(passData.normals, AccessFlags.Read);
            builder.UseTexture(passData.depth, AccessFlags.Read);

            builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.mat.SetTexture(SourceTexId, data.src);
                data.mat.SetTexture(IdTexId, data.ids);
                data.mat.SetTexture(NormalsTexId, data.normals);
                data.mat.SetTexture(DepthTexId, data.depth);
                data.mat.SetInt(OutlinesBitID, data.requiredBit);

                data.mat.SetColor(OutlineColourId, data.outlineCol);

                data.mat.SetFloat(ThicknessId, data.thicknessPx);
                data.mat.SetFloat(DepthThresholdId, data.depthThreshold);
                data.mat.SetFloat(DepthStrengthId, data.depthStrength);
                data.mat.SetFloat(NormalThresholdId, data.normalThreshold);
                data.mat.SetFloat(NormalStrengthId, data.normalStrength);

                data.mat.SetBuffer(InstanceBufferID, data.instanceBuffer);
                data.mat.SetVector(ScreenParamsID, data.screenSize);

                data.mat.SetInt(UseOcclusionID, data.useOcclusion);

                if (data.useOcclusion != 0)
                {
                    data.mat.SetBuffer(VisibilityFlagsID, data.visibilityBuffer);
                    data.mat.SetBuffer(BBoxIndicesID, data.bboxIndexBuffer);
                }

                data.mat.SetBuffer(MaskBufferID, data.maskBuffer);
                data.mat.SetInt(UseBboxIndicesID, data.useBboxIndices);

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

    // need to properly implement this
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