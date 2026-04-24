using UnityEngine;

public class CoverageController : MonoBehaviour
{
    [SerializeField] private Material coverageMaterial;
    [SerializeField] public bool useAsOccluder = false;

    [Header("Debug")]
    [SerializeField, Range(0f, 100f)]
    private float debugCoveragePercent = 0f;

    [SerializeField]
    private bool applyInEditor = true;

    GameObject quad;
    Camera cam;

    float camWidth, camHeight;

    void Awake()
    {
        CreateOrResetQuad();
        UpdateCoverage(debugCoveragePercent);
    }

    void OnValidate()
    {
        if (!applyInEditor)
            return;

        CreateOrResetQuad();
        UpdateCoverage(debugCoveragePercent);
    }

    private void CreateOrResetQuad()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("CoverageController: No main camera found.");
            return;
        }

        cam.orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;

        // Reuse existing quad if one already exists
        if (quad == null)
        {
            GameObject existing = GameObject.Find("CoverageQuad");
            if (existing != null)
                quad = existing;
        }

        // Remove duplicates
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var obj in allObjects)
        {
            if (obj.name == "CoverageQuad" && obj != quad)
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
            quad.name = "CoverageQuad";
            quad.transform.SetParent(null, true);

            if (!useAsOccluder)
                quad.AddComponent<StylisedTag>();
        }

        Renderer r = quad.GetComponent<Renderer>();
        if (r != null && coverageMaterial != null)
            r.sharedMaterial = coverageMaterial;

        camHeight = cam.orthographicSize * 2f;
        camWidth = camHeight * cam.aspect;

        quad.transform.rotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(camWidth, camHeight, 1f);
    }

    public void LoadScene()
    {
        CreateOrResetQuad();
        UpdateCoverage(debugCoveragePercent);
    }

    public void UpdateCoverage(float coveragePercent)
    {
        Debug.Log($"[Coverage] {coveragePercent}% → pos {quad.transform.position.x}");

        if (quad == null)
        {
            Debug.LogError("CoverageController: quad is null.");
            return;
        }

        float t = Mathf.Clamp01(coveragePercent / 100.0f);

        quad.transform.rotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(camWidth, camHeight, 1f);

        // Horizontal wipe: 0% = fully off-screen left, 100% = fully covering screen.
        float targetX = Mathf.Lerp(-camWidth, 0f, t);

        quad.transform.position = new Vector3(targetX, 0f, 0f);

        Debug.Log(
            $"CoverageController: coverage={coveragePercent}% | t={t} | " +
            $"targetX={targetX} | pos={quad.transform.position} | " +
            $"scale={quad.transform.localScale} | camWidth={camWidth} | camHeight={camHeight}"
        );
    }
}