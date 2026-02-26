using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class ScreenspaceOutlinesPass : ScriptableRenderPass, INprPass
{
    Material _mat;

    public Color outlineColour;
    public float outlineThickness;

    static readonly int _DepthTexId = Shader.PropertyToID("_NprDepthTexture");
    static readonly int _NormalsTexId = Shader.PropertyToID("_NprNormalsTexture");
    static readonly int _IdTexId = Shader.PropertyToID("_NprIdTexture");
    static readonly int _SourceTexId = Shader.PropertyToID("_NprSourceTexture");

    static readonly int _ThicknessId = Shader.PropertyToID("_ThicknessPx");
    static readonly int _DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    static readonly int _DepthStrengthId = Shader.PropertyToID("_DepthStrength");
    static readonly int _NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
    static readonly int _NormalStrengthId = Shader.PropertyToID("_NormalStrength");
    
    static readonly int OutlineColourId = Shader.PropertyToID("_OutlineColour");
    

    static readonly int RectId = Shader.PropertyToID("_Rect");
    static readonly int ScreenTexelSizeId = Shader.PropertyToID("_ScreenTexelSize");

    [SerializeField] 
    float _depthThreshold = 0.02f;
    [SerializeField] 
    float _depthStrength = 1.0f;
    [SerializeField] 
    float _normalThreshold = 0.2f;
    [SerializeField] 
    float _normalStrength = 1.0f;

    public void ApplySettings(NprSettings settings)
    {
        outlineColour = settings.outlineColour;
        outlineThickness = settings.outlineThickness;
    }

    class CopyData
    {
        public TextureHandle src;
    }

    class PassData
    {
        public TextureHandle depth;
        public TextureHandle normals;
        public TextureHandle ids;
        public TextureHandle source;

        public Material mat;

        public Color outlineCol;
        public float thicknessPx;
        public float depthThreshold;
        public float depthStrength;
        public float normalThreshold;
        public float normalStrength;

        public RectInt rect;
    }

    public ScreenspaceOutlinesPass(Shader shader)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);

        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
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

        // if (!nprFrameData.sourceTexture.IsValid())  
        //     return;
        if (!nprFrameData.idTexture.IsValid())      
            return;
        if (!nprFrameData.normalsTexture.IsValid()) 
            return;
        // using urp camera depth texture
        if (!frameData.activeDepthTexture.IsValid()) 
            return;
        
        if(nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
            return;

        var camDesc = cameraData.cameraTargetDescriptor;
        Vector2 screenTexelSize = new Vector2(1f / camDesc.width, 1f / camDesc.height);

        RenderTextureDescriptor copyDesc = camDesc;
        copyDesc.depthBufferBits = 0;
        copyDesc.msaaSamples = 1; 
        TextureHandle srcCopy = renderGraph.CreateTexture(new TextureDesc(copyDesc.width, copyDesc.height)
        {
            name = "_NprSourceCopy_Dither",
            colorFormat = copyDesc.graphicsFormat,   
            clearBuffer = false,
            filterMode = FilterMode.Point
        });

        // copy camera color into srcCopy
        using (var builder = renderGraph.AddRasterRenderPass("NPR Dither Source Copy", out CopyData copyPass))
        {
            builder.SetRenderAttachment(srcCopy, 0, AccessFlags.Write);
            builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

            copyPass.src = frameData.activeColorTexture;

            builder.SetRenderFunc(static (CopyData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }


        foreach(var bbox in nprFrameData.bboxes)
        {
            if (bbox.box.width <= 0 || bbox.box.height <= 0)
                continue;
            
            if((bbox.styles & StyleBits.ImageSpaceEffect.Outline) == 0)
                continue;

            // if(!bbox.currentTex.IsValid())
            //     continue;

            // TextureHandle outTex = renderGraph.CreateTexture(bbox.desc);
            using (var builder = renderGraph.AddRasterRenderPass($"BBox Outline ({bbox.box})", out PassData passData))
            {
                // write to bbox colour
                builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

                // passData.source = nprFrameData.sourceTexture;
                passData.source = srcCopy;
                passData.ids = nprFrameData.idTexture;
                passData.normals = nprFrameData.normalsTexture;
                passData.depth = frameData.activeDepthTexture;
                passData.rect = bbox.box;

                passData.mat = _mat;

                passData.outlineCol = outlineColour;
                passData.thicknessPx = outlineThickness;
                passData.depthThreshold = _depthThreshold;
                passData.depthStrength = _depthStrength;
                passData.normalThreshold = _normalThreshold;
                passData.normalStrength = _normalStrength;

                // read from normal, id, depth and source textures
                // builder.UseTexture(nprFrameData.sourceTexture, AccessFlags.Read);
                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(nprFrameData.idTexture, AccessFlags.Read);
                builder.UseTexture(nprFrameData.normalsTexture, AccessFlags.Read);
                builder.UseTexture(frameData.activeDepthTexture, AccessFlags.Read);


                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(_SourceTexId, data.source);
                    data.mat.SetTexture(_IdTexId, data.ids);
                    data.mat.SetTexture(_NormalsTexId, data.normals);
                    data.mat.SetTexture(_DepthTexId, data.depth);

                    data.mat.SetColor(OutlineColourId, data.outlineCol);

                    data.mat.SetFloat(_ThicknessId, data.thicknessPx);
                    data.mat.SetFloat(_DepthThresholdId, data.depthThreshold);
                    data.mat.SetFloat(_DepthStrengthId, data.depthStrength);
                    data.mat.SetFloat(_NormalThresholdId, data.normalThreshold);
                    data.mat.SetFloat(_NormalStrengthId, data.normalStrength);

                    ctx.cmd.EnableScissorRect(new Rect(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
                    CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
                    ctx.cmd.DisableScissorRect();
                });
            }

            // bbox.currentTex = outTex;
        }
    }
}