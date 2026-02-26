// using UnityEngine;
// using UnityEngine.Experimental.Rendering;
// using UnityEngine.Rendering;
// using UnityEngine.Rendering.RenderGraphModule;
// using UnityEngine.Rendering.Universal;

// [System.Serializable]
// public class SourcePrepass : ScriptableRenderPass//, INprPass
// {
//     class PassData
//     {
//         public TextureHandle src;
//     }

//     public SourcePrepass()
//     {
//         renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
//     }

//     public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
//     {
//         UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
//         UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();

//         NprFrameData nprFrameData;
//             if (frameContext.Contains<NprFrameData>())
//                 nprFrameData = frameContext.Get<NprFrameData>();
//             else
//                 nprFrameData = frameContext.Create<NprFrameData>();

//         // copy frame into a texture
//         RenderTextureDescriptor srcDesc = cameraData.cameraTargetDescriptor;
//         srcDesc.depthBufferBits = 0;
//         srcDesc.msaaSamples = 1;
//         srcDesc.sRGB = false;

//         TextureHandle srcCopy = renderGraph.CreateTexture(new TextureDesc(srcDesc)
//         {
//             name = "_NprSourceCopy",
//             colorFormat = srcDesc.graphicsFormat,
//             clearBuffer = false,
//             filterMode = FilterMode.Point
//         });
//         nprFrameData.sourceTexture = srcCopy;
//         nprFrameData.currentColour = srcCopy;

//         // blit frame into a copy for sampling in dithering pass
//         using (var builder = renderGraph.AddRasterRenderPass("NPR Frame Copy Pass", out PassData copyData))
//         {
//             builder.SetRenderAttachment(srcCopy, 0, AccessFlags.Write);
//             builder.UseTexture(frameData.activeColorTexture, AccessFlags.Read);

//             copyData.src = frameData.activeColorTexture;

//             builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
//             {
//                 Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), 0, false);
//             });
//         }
//     }
// }