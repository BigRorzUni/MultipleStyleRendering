using UnityEngine;

public class CoverageController : MonoBehaviour
{
    [SerializeField] private Material coverageMaterial;

    [SerializeField] public bool useAsOccluder = false;
    GameObject quad;
    Camera cam;

    float camWidth, camHeight;

    void Awake()
    {
        CreateOrResetQuad();
    }

    private void CreateOrResetQuad()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("CoverageController: No main camera found.");
            return;
        }

        // make sure camera is orthographic, at origin and facing forward
        cam.orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;

        if (quad == null)
        {
            // instantiate a quad with stylised tag
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CoverageQuad";
            if(!useAsOccluder)
                quad.AddComponent<StylisedTag>();
        }

        Renderer r = quad.GetComponent<Renderer>();
        if (r != null && coverageMaterial != null)
            r.material = coverageMaterial;

        // make sure quad covers the whole view
        camHeight = cam.orthographicSize * 2f;
        camWidth = camHeight * cam.aspect;

        quad.transform.localScale = new Vector3(camWidth, camHeight, 1f);
    }

    public void LoadScene()
    {
        CreateOrResetQuad();
    }

    public void UpdateCoverage(float coveragePercent)
    {
        if (quad == null)
        {
            Debug.LogError("CoverageController: quad is null.");
            return;
        }

        // move quad horizontally so that it covers the specified percentage of the view from the left edge
        float targetX = (coveragePercent / 100.0f - 1.0f) * camWidth;

        quad.transform.position = new Vector3(targetX, 0f, 0f);
    }
}