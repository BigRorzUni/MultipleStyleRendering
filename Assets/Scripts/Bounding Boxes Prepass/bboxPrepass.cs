using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System.Collections;

[System.Serializable]
public class bboxPrepass : ScriptableRenderPass
{
    public int testStyleCount = 0;
    public bool _testModeEnabled;

    class PassData
    {
        public TextureHandle src;
        public TextureHandle dst;
        public RectInt rect;
        public Vector2 srcTexelSize;
    }

    public bboxPrepass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public bboxPrepass(int testCount, bool testModeEnabled)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        testStyleCount = testCount;
        _testModeEnabled = testModeEnabled;
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

                BoundingBox bbox = new BoundingBox((uint)tag.imageEffects, screenBox);

                nprFrameData.presentImageBits |= tag.imageEffects;

                // add test effect to bbox mask
                if(NprTestingConfig.TestMode)
                {
                    nprFrameData.presentTestStyles |= tag.testEffects;
                    bbox.testMask = tag.testEffects;
                }
                    //Debug.Log($"Getting test mask from object, it has {bbox.testMask} applied");

                nprFrameData.bboxes.Add(bbox);
            }
        }
        // need to check that the object in the bbox is actually visible


        // merge bboxes for optimality


        // initialise source texture
        RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
        camDesc.depthBufferBits = 0;
        camDesc.msaaSamples = 1; 

        nprFrameData.sourceTexture = renderGraph.CreateTexture(new TextureDesc(camDesc.width, camDesc.height)
        {
            name = "_NprSourceCopy",
            colorFormat = camDesc.graphicsFormat,   
            clearBuffer = false,
            filterMode = FilterMode.Point
        });
    }
}

        // ----- allocate the source textures in each bounding box ------
        // if (nprFrameData.bboxes == null || nprFrameData.bboxes.Count == 0)
        //     return;

        // UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
        // TextureHandle camColour = frameData.activeColorTexture;
        
