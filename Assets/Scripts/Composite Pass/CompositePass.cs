// using UnityEngine;
// using UnityEngine.Rendering;
// using UnityEngine.Rendering.RenderGraphModule;
// using UnityEngine.Rendering.Universal;

// [System.Serializable]
// public class CompositePass : ScriptableRenderPass
// {
//     Material _mat;

//     static readonly int RectId = Shader.PropertyToID("_Rect");
//     static readonly int SourceTexID = Shader.PropertyToID("_SrcTex");
//     static readonly int BBoxTexId = Shader.PropertyToID("_BBoxTex");
//     static readonly int ScreenTexelSizeId = Shader.PropertyToID("_ScreenTexelSize");


//     // public void ApplySettings(NprSettings settings)
//     // {

//     // }

//     class CopyData
//     {
//         public TextureHandle srcTex;
//     }
//     class PassData
//     {
//         public TextureHandle srcTex;
//         public TextureHandle bboxTex;
//         public Material mat;
//         public RectInt rect;
//         public Vector2 screenTexelSize;
//     }

//     public CompositePass(Shader shader)
//     {
//         if (shader != null)
//             _mat = CoreUtils.CreateEngineMaterial(shader);

//         renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
//     }

//     public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
//     {
//         if (_mat == null) return;

//         UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
//         UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

//         NprFrameData nprFrameData;
//             if (frameContext.Contains<NprFrameData>())
//                 nprFrameData = frameContext.Get<NprFrameData>();
//             else
//                 nprFrameData = frameContext.Create<NprFrameData>();

//         if(nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
//             return;

//         // copy frame into a texture
//         RenderTextureDescriptor srcDesc = cameraData.cameraTargetDescriptor;
//         srcDesc.depthBufferBits = 0;
//         srcDesc.msaaSamples = 1;
//         srcDesc.sRGB = false;

//         TextureHandle srcCopy = renderGraph.CreateTexture(new TextureDesc(srcDesc)
//         {
//             name = "_CompositeSourceCopy",
//             colorFormat = srcDesc.graphicsFormat,
//             clearBuffer = false,
//             filterMode = FilterMode.Point
//         });

//         using (var builder = renderGraph.AddRasterRenderPass("Composite Pass: Copy Source", out CopyData copyData))
//         {
//             builder.SetRenderAttachment(srcCopy, 0, AccessFlags.Write);
//             builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

//             copyData.srcTex = frameData.activeColorTexture;

//             builder.SetRenderFunc(static (CopyData data, RasterGraphContext ctx) =>
//             {
//                 Blitter.BlitTexture(ctx.cmd, data.srcTex, new Vector4(1, 1, 0, 0), 0, false);
//             });
//         }

//         // using (var builder = renderGraph.AddRasterRenderPass("DEBUG: Show srcCopy", out CopyData pd))
//         // {
//         //     builder.AllowPassCulling(false);
//         //     pd.srcTex = srcCopy;
//         //     builder.UseTexture(pd.srcTex, AccessFlags.Read);
//         //     builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);

//         //     builder.SetRenderFunc(static (CopyData data, RasterGraphContext ctx) =>
//         //     {
//         //         Blitter.BlitTexture(ctx.cmd, data.srcTex, new Vector4(1, 1, 0, 0), 0, false);
//         //     });
//         // }

//         Vector2 screenTexelSize = new Vector2(1.0f / Mathf.Max(1, srcDesc.width), 1.0f / Mathf.Max(1, srcDesc.height));
//         int i = 0;
//         TextureHandle currentTex = srcCopy;
//         foreach(var bbox in nprFrameData.bboxes)
//         {
//             if (bbox.box.width <= 0 || bbox.box.height <= 0)
//                 continue;

//             if(!bbox.currentTex.IsValid())
//                 continue;

//             TextureHandle outTex = renderGraph.CreateTexture(new TextureDesc(srcDesc)
//             {
//                 name = $"_NprCompositeOut_{i++}",
//                 colorFormat = srcDesc.graphicsFormat,
//                 clearBuffer = false,
//                 filterMode = FilterMode.Point
//             });

//             using (var builder = renderGraph.AddRasterRenderPass($"Composite Pass ({bbox.box})", out PassData passData))
//             {
//                 passData.srcTex = currentTex;
//                 passData.bboxTex = bbox.currentTex;
//                 passData.mat = Object.Instantiate(_mat);
//                 passData.rect = bbox.box;
//                 passData.screenTexelSize = screenTexelSize;

//                 builder.UseTexture(passData.srcTex, AccessFlags.Read);
//                 builder.UseTexture(passData.bboxTex, AccessFlags.Read);

//                 builder.SetRenderAttachment(outTex, 0, AccessFlags.Write);

//                 builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
//                 {
//                     data.mat.SetTexture(SourceTexID, data.srcTex);
//                     data.mat.SetTexture(BBoxTexId, data.bboxTex);
//                     data.mat.SetVector(RectId, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
//                     data.mat.SetVector(ScreenTexelSizeId, data.screenTexelSize);

//                     CoreUtils.DrawFullScreen(ctx.cmd, data.mat, shaderPassId: 0);
//                 });
//             }

//             currentTex = outTex;
//         }

//         // final blit to camera
//         using (var builder = renderGraph.AddRasterRenderPass("Composite Blit", out CopyData finalData))
//         {
//             builder.AllowPassCulling(false);

//             builder.SetRenderAttachment(frameData.activeColorTexture, 0, AccessFlags.Write);
//             builder.UseTexture(currentTex, AccessFlags.Read);

//             finalData.srcTex = currentTex;

//             builder.SetRenderFunc(static (CopyData data, RasterGraphContext ctx) =>
//             {
//                 Blitter.BlitTexture(ctx.cmd, data.srcTex, new Vector4(1, 1, 0, 0), 0, false);
//             });
//         }
//     }
// }