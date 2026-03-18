using UnityEngine;

public class CoverageController : MonoBehaviour
{
    GameObject quad;
    Camera cam;

    float camWidth, camHeight;

    void Awake()
    {
        cam = Camera.main;

        // make sure camera is orthographic, at origin and facing forward
        cam.orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;

        // instanstiate a quad with stylised tag
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.AddComponent<StylisedTag>();


        // make sure quad covers the whole view
        camHeight = cam.orthographicSize * 2f;
        camWidth = camHeight * cam.aspect;

        quad.transform.localScale = new Vector3(camWidth, camHeight, 1f);
    }

    public void LoadScene()
    {
        cam = Camera.main;

        // make sure camera is orthographic, at origin and facing forward
        cam.orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;

        // instanstiate a quad with stylised tag
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.AddComponent<StylisedTag>();


        // make sure quad covers the whole view
        camHeight = cam.orthographicSize * 2f;
        camWidth = camHeight * cam.aspect;

        quad.transform.localScale = new Vector3(camWidth, camHeight, 1f);
    }

    public void UpdateCoverage(float coveragePercent)
    {
        // move quad horizontally so that it covers the specified percentage of the view from the left edge
        float targetX = (coveragePercent / 100.0f - 1.0f) * camWidth;

        quad.transform.position = new Vector3(targetX, 0f, 0f);
    }
}
