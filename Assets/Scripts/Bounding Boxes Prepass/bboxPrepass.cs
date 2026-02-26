using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class bboxPrepass : ScriptableRenderPass
{
    // readonly FilteringSettings _filteringSettings;
    static readonly int RectId = Shader.PropertyToID("_Rect");
    static readonly int SrcTexelSizeId = Shader.PropertyToID("_SrcTexelSize");
    Material _mat;

    class PassData
    {
        public TextureHandle src;
        public TextureHandle dst;
        public Material copyMat;
        public RectInt rect;
        public Vector2 srcTexelSize;
    }

    public bboxPrepass(Shader shader)
    {
        if (shader != null)
            _mat = CoreUtils.CreateEngineMaterial(shader);
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
        Camera camera = cameraData.camera; // extend to multiple cameras?

        if(camera == null)
            return;

        // get/create NPR frame data
        NprFrameData nprFrameData;
        if (frameContext.Contains<NprFrameData>())
            nprFrameData = frameContext.Get<NprFrameData>();
        else
            nprFrameData = frameContext.Create<NprFrameData>();

        if (nprFrameData.bboxes == null) 
            nprFrameData.bboxes = new List<BoundingBox>();
        else 
            nprFrameData.bboxes.Clear();


        // get all active tagged objects using the attached StylisedTag component
        StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
        foreach (var tag in tags)
        {
            GameObject obj = tag.gameObject;
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

            foreach(Renderer renderer in renderers)
            {
                // check that the object wants an image effect applied and is visible, otherwise skip
                if(renderer == null || (renderer.renderingLayerMask & (uint)StyleBits.ImageSpaceBit) == 0)
                    continue;
                
                Bounds rendererBounds = renderer.bounds;
                Vector3 centre = rendererBounds.center;
                Vector3 ext = rendererBounds.extents;

                // corner of the renderer boudns
                Vector3[] corners = new Vector3[8]
                {
                    centre + new Vector3(-ext.x, -ext.y, -ext.z),
                    centre + new Vector3(ext.x, -ext.y, -ext.z),
                    centre + new Vector3(-ext.x, ext.y, -ext.z),
                    centre + new Vector3(ext.x, ext.y, -ext.z),
                    centre + new Vector3(-ext.x, -ext.y, ext.z),
                    centre + new Vector3(ext.x, -ext.y, ext.z),
                    centre + new Vector3(-ext.x, ext.y, ext.z),
                    centre + new Vector3(ext.x, ext.y, ext.z)
                };

                // get min and max of screen coordinates for the bounding box
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;

                foreach (var corner in corners)
                {
                    Vector3 screenPoint = camera.WorldToScreenPoint(corner);

                    // ignore points behind camera
                    if (screenPoint.z <= 0f)
                        continue;

                    // clamp to screen
                    float x = Mathf.Clamp(screenPoint.x, 0f, camera.pixelWidth);
                    float y = Mathf.Clamp(screenPoint.y, 0f, camera.pixelHeight);

                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }

                RectInt screenBox = new RectInt((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
                //Debug.Log($"Screen bounding box for {renderer.name}: {screenBox}");

                BoundingBox bbox = new BoundingBox((uint)tag.imageEffects, screenBox);


                nprFrameData.bboxes.Add(bbox);
            }
        }

        // Debug.Log("BBOX LIST:");
        // foreach (BoundingBox bb in nprFrameData.bboxes)
        // {
        //     Debug.Log($"BBox: {bb.box}, styles: {bb.styles}");
        // }

        // ----- allocate the source textures in each bounding box ------
        // if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
        //     return;

        // UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        // TextureHandle camColour = frameData.activeColorTexture;
        
        // // copy frame into a texture
        // RenderTextureDescriptor srcDesc = cameraData.cameraTargetDescriptor;
        // srcDesc.depthBufferBits = 0;
        // srcDesc.msaaSamples = 1;
        // srcDesc.sRGB = false;

        // // for each bounding box
        // foreach (var bbox in nprFrameData.bboxes)
        // {
        //     if (bbox.box.width <= 0 || bbox.box.height <= 0)
        //         continue;

        //     bbox.desc = new TextureDesc(bbox.box.width, bbox.box.height)
        //     {
        //         name = $"BBoxSrc_{bbox.box.x}_{bbox.box.y}",
        //         colorFormat = srcDesc.graphicsFormat,
        //         clearBuffer = false,    
        //         filterMode = FilterMode.Point
        //     };

        //     using (var builder = renderGraph.AddRasterRenderPass($"BBox Source Copy ({bbox.box})", out PassData passData))
        //     {
        //         passData.src = frameData.activeColorTexture;
        //         passData.dst = renderGraph.CreateTexture(bbox.desc);
        //         passData.copyMat = Object.Instantiate(_mat);
        //         passData.rect = bbox.box;

        //         var camDesc = cameraData.cameraTargetDescriptor;
        //         passData.srcTexelSize = new Vector2(1.0f / camDesc.width, 1.0f / camDesc.height);

        //         builder.UseTexture(passData.src, AccessFlags.Read);
        //         builder.SetRenderAttachment(passData.dst, 0, AccessFlags.Write);
        //         builder.AllowGlobalStateModification(true);

        //         builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
        //         {
        //             data.copyMat.SetVector(RectId, new Vector4(data.rect.x, data.rect.y, data.rect.width, data.rect.height));
        //             data.copyMat.SetVector(SrcTexelSizeId, data.srcTexelSize);

        //             Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), data.copyMat, 0);
        //         });

        //         // store on bbox so later passes can use it
        //         bbox.currentTex = passData.dst;
        //     }
        // }   
    }
}