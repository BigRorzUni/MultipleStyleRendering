using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum StylePattern
{
    SameStyle,
    RandomSingleStyle,
}

public class Spawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject prefab;
    [SerializeField] private Camera targetCamera;

    [Header("Spawn Settings")]
    [SerializeField] private float spawnDepth = 5f;

    [Header("Coverage")]
    [SerializeField, Range(0f, 1f)] private float targetCoverageFraction = 0.5f;

    [Header("Overlap")]
    [SerializeField, Range(0f, 1f)] private float overlapFraction = 0f;

    [Header("Scale Test")]
    [SerializeField] private float objectScaleFactor = 1f;


    [Header("Editor Test")]
    [SerializeField] private int editorObjectCount = 100;
    [SerializeField] private bool regenerate = false;
    [SerializeField] private bool clearAll = false;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnValidate()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        targetCoverageFraction = SnapCoverageFraction(targetCoverageFraction);
        overlapFraction = Mathf.Clamp01(overlapFraction);
        objectScaleFactor = Mathf.Max(0f, objectScaleFactor);
        editorObjectCount = Mathf.Max(0, editorObjectCount);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (clearAll)
            {
                clearAll = false;
                EditorApplication.delayCall += () => { if (this) ClearSpawnedObjects(); };
            }

            if (regenerate)
            {
                regenerate = false;
                EditorApplication.delayCall += () => { if (this) Regenerate(editorObjectCount); };
            }
        }
#endif
    }

    private float SnapCoverageFraction(float f)
    {
        float percent = Mathf.Clamp(f * 100f, 0f, 100f);
        percent = Mathf.Round(percent / 10f) * 10f;
        return percent / 100f;
    }

    public void Regenerate(int objectCount)
    {
        ClearSpawnedObjects();
        SpawnObjects(objectCount);
    }

    public void Regenerate(int objectCount, float coverageFraction, float overlap)
    {
        targetCoverageFraction = SnapCoverageFraction(coverageFraction);
        overlapFraction = Mathf.Clamp01(overlap);

        ClearSpawnedObjects();
        SpawnObjects(objectCount);
    }

    public void Regenerate(int objectCount, float coverageFraction, float overlap = 0f, float scaleFactor = 1f)
    {
        targetCoverageFraction = SnapCoverageFraction(coverageFraction);
        overlapFraction = Mathf.Clamp01(overlap);
        objectScaleFactor = scaleFactor;

        ClearSpawnedObjects();
        SpawnObjects(objectCount);
    }

    public void ClearSpawnedObjects()
    {
        var children = GetSpawnedObjects();

        foreach (var obj in children)
        {
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }


    private void SpawnObjects(int objectCount)
    {
        if (!ValidateSetup() || objectCount <= 0)
            return;

        GetScreenBounds(out float minX, out float maxX, out float minY, out float maxY);

        float screenWidth = maxX - minX;
        float screenHeight = maxY - minY;
        float screenArea = screenWidth * screenHeight;

        float targetArea = screenArea * targetCoverageFraction;

        // make sure there are no overlaps
        float baseRegionHeight = targetArea / screenWidth;
        baseRegionHeight = Mathf.Clamp(baseRegionHeight, 0f, screenHeight);

        float aspect = screenWidth / screenHeight;
        int cols = Mathf.CeilToInt(Mathf.Sqrt(objectCount * aspect));
        cols = Mathf.Max(1, cols);

        int fullRows = objectCount / cols;
        int remainder = objectCount % cols;
        int rows = fullRows + (remainder > 0 ? 1 : 0);
        rows = Mathf.Max(1, rows);

        float baseRowHeight = baseRegionHeight / rows;
        float regionMinY = -baseRegionHeight * 0.5f;

        Vector3 overlapCentre = new Vector3(0f, 0f, spawnDepth);

        int spawned = 0;

        for (int r = 0; r < rows && spawned < objectCount; r++)
        {
            bool lastRow = (r == rows - 1 && remainder > 0);
            int colsThisRow = lastRow ? remainder : cols;

            float cellWidth = screenWidth / colsThisRow;
            float rowHeight = baseRowHeight;

            for (int c = 0; c < colsThisRow && spawned < objectCount; c++)
            {
                float x = minX + (c + 0.5f) * cellWidth;
                float y = regionMinY + (r + 0.5f) * rowHeight;

                Vector3 gridPos = new Vector3(x, y, spawnDepth);
                Vector3 finalPos = Vector3.Lerp(gridPos, overlapCentre, overlapFraction);

                var obj = Instantiate(prefab, finalPos, Quaternion.identity, transform);

                obj.name = $"Obj_{spawned}";
                obj.transform.localScale = new Vector3(cellWidth * objectScaleFactor, rowHeight * objectScaleFactor, 1f); // apply scale for tiling tests

                SetupStylisedTag(obj);
                spawned++;
            }
        }

        float realisedObjectArea = 0f;
        foreach (Transform t in transform)
            realisedObjectArea += t.localScale.x * t.localScale.y;

        Debug.Log($"Coverage area: {realisedObjectArea / screenArea:P4} (target {targetCoverageFraction:P0}) | " + $"Overlap: {overlapFraction:P0}");
    }

    private List<GameObject> GetSpawnedObjects()
    {
        var result = new List<GameObject>();

        for (int i = 0; i < transform.childCount; i++)
            result.Add(transform.GetChild(i).gameObject);

        return result;
    }

    private bool ValidateSetup()
    {
        if (prefab == null || targetCamera == null)
            return false;

        if (!targetCamera.orthographic)
            Debug.LogWarning("Spawner assumes orthographic camera.");

        return true;
    }

    private void GetScreenBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        float h = targetCamera.orthographicSize * 2f;
        float w = h * targetCamera.aspect;

        minX = -w * 0.5f;
        maxX = w * 0.5f;
        minY = -h * 0.5f;
        maxY = h * 0.5f;
    }

    private void SetupStylisedTag(GameObject obj)
    {
        StylisedTag tag = obj.GetComponent<StylisedTag>() ?? obj.AddComponent<StylisedTag>();
        tag.UseRuntimeTestEffects();
    }
}