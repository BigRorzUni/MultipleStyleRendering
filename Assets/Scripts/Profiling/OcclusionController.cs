using UnityEngine;

public class OcclusionController : MonoBehaviour
{
    [SerializeField] private Material occlusionMaterial;
    [SerializeField] public bool useAsOccluder = false;

    [Header("Debug")]
    [SerializeField, Range(0f, 100f)]
    private float debugOcclusionPercent = 0f;

    [SerializeField]
    private bool applyInEditor = true;

    GameObject quad;
    Camera cam;

    float camWidth, camHeight;

    void Awake()
    {
        CreateOrResetQuad();
        UpdateCoverage(debugOcclusionPercent);
    }

    void OnValidate()
    {
        if (!applyInEditor)
            return;

        CreateOrResetQuad();
        UpdateCoverage(debugOcclusionPercent);
    }

    private void CreateOrResetQuad()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("No main camera found for Occlusion Controller");
            return;
        }

        cam.orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;

        // Reuse existing quad if one already exists
        if (quad == null)
        {
            GameObject existing = GameObject.Find("OcclusionQuad");
            if (existing != null)
                quad = existing;
        }

        // Remove duplicates
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var obj in allObjects)
        {
            if (obj.name == "OcclusionQuad" && obj != quad)
            {
                if (Application.isPlaying)
                    Destroy(obj);
                else
                    DestroyImmediate(obj);
            }
        }

        if (quad == null)
        {
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "OcclusionQuad";
            quad.transform.SetParent(null, true);

            if (!useAsOccluder)
                quad.AddComponent<StylisedTag>();
        }

        Renderer r = quad.GetComponent<Renderer>();
        if (r != null && occlusionMaterial != null)
            r.sharedMaterial = occlusionMaterial;

        camHeight = cam.orthographicSize * 2f;
        camWidth = camHeight * cam.aspect;

        quad.transform.rotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(camWidth, camHeight, 1f);
    }

    public void LoadScene()
    {
        CreateOrResetQuad();
        UpdateCoverage(debugOcclusionPercent);
    }

    public void UpdateCoverage(float coveragePercent)
    {
        if (quad == null)
        {
            Debug.LogError("Occlusion quad is null.");
            return;
        }

        float t = Mathf.Clamp01(coveragePercent / 100.0f);

        quad.transform.rotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(camWidth, camHeight, 1f);

        float targetX = Mathf.Lerp(-camWidth, 0f, t);

        quad.transform.position = new Vector3(targetX, 0f, 0f);
    }
}