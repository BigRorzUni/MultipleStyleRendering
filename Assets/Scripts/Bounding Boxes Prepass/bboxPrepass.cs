using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;

[System.Serializable]
public class bboxPrepass : ScriptableRenderPass
{
    readonly FilteringSettings _filteringSettings;


    public bboxPrepass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
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

        nprFrameData.bboxes = new List<BoundingBox>(); // maybe preallocate for performance?

        // get all active tagged objects using the attached StylisedTag component
        StylisedTag[] tags = Object.FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
        foreach (var tag in tags)
        {
            
            GameObject obj = tag.gameObject;
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

            foreach(Renderer renderer in renderers)
            {
                // check that the object wants an image effect applied and is visible, otherwise skip
                if(renderer == null || (renderer.renderingLayerMask & (uint)StyleBits.ImageSpaceBit) == 0 || !renderer.isVisible)
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

                BoundingBox bbox = new BoundingBox();
                bbox.styles = (uint)tag.imageEffects;
                bbox.box = screenBox;

                nprFrameData.bboxes.Add(bbox);
            }
        }
        
        Debug.Log("BBOX LIST:");
        foreach (BoundingBox bb in nprFrameData.bboxes)
        {
            Debug.Log($"BBox: {bb.box}, styles: {bb.styles}");
        }
    }
}