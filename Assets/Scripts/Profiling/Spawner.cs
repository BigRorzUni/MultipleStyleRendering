using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum StylePattern
{
    SameStyle,
    RandomSingleStyle,
    RandomMultiStyle
}

public class Spawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject prefab;
    [SerializeField] private Camera targetCamera;

    [Header("Spawn Settings")]
    [SerializeField] private int seed = 12345;
    [SerializeField] private float spawnDepth = 5f;
    [SerializeField, Range(0.05f, 1f)] private float spawnAreaScale = 1f;
    [SerializeField] private Vector3 objectScale = Vector3.one;

    [Header("Style Settings")]
    [SerializeField] private StylePattern stylePattern = StylePattern.SameStyle;
    [SerializeField, Min(1)] private int totalAvailableStyles = 32;
    [SerializeField, Min(1)] private int stylesPerObject = 1;
    [SerializeField, Min(0)] private int sameStyleIndex = 0;

    [Header("Editor Preview")]
    [SerializeField] private int previewObjectCount = 100;
    [SerializeField] private int previewSeed = 12345;
    [SerializeField] private float previewAreaScale = 1f;
    [SerializeField] private StylePattern previewPattern = StylePattern.SameStyle;
    [SerializeField] private int previewTotalStyles = 32;
    [SerializeField] private int previewStylesPerObject = 1;
    [SerializeField] private int previewSameStyleIndex = 0;

    [Header("Editor Triggers")]
    [SerializeField] private bool generatePreview = false;
    [SerializeField] private bool clearPreview = false;
    [SerializeField] private bool reassignStylesPreview = false;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnValidate()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        previewAreaScale = Mathf.Clamp(previewAreaScale, 0.05f, 1f);
        previewTotalStyles = Mathf.Clamp(previewTotalStyles, 1, 32);
        previewStylesPerObject = Mathf.Clamp(previewStylesPerObject, 1, 32);
        previewSameStyleIndex = Mathf.Clamp(previewSameStyleIndex, 0, 31);

        if (clearPreview)
        {
            clearPreview = false;
            RequestClearPreview();
        }

        if (generatePreview)
        {
            generatePreview = false;
            RequestGeneratePreview();
        }

        if (reassignStylesPreview)
        {
            reassignStylesPreview = false;
            RequestReassignStylesPreview();
        }
    }

    public void Regenerate(
        int objectCount,
        StylePattern pattern,
        int totalStyles,
        int stylesPerObj,
        int randomSeed,
        float areaScale,
        int sameStyle = 0)
    {
        stylePattern = pattern;
        totalAvailableStyles = Mathf.Clamp(totalStyles, 1, 32);
        stylesPerObject = Mathf.Clamp(stylesPerObj, 1, 32);
        sameStyleIndex = Mathf.Clamp(sameStyle, 0, totalAvailableStyles - 1);
        seed = randomSeed;
        spawnAreaScale = Mathf.Clamp(areaScale, 0.05f, 1f);

        ClearSpawnedObjects();
        SpawnObjects(objectCount);
    }

    public void ClearSpawnedObjects()
    {
        List<GameObject> children = GetSpawnedObjects();

        for (int i = 0; i < children.Count; i++)
        {
            GameObject obj = children[i];
            if (obj == null)
                continue;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }

    public void ReassignStylesToSpawnedObjects()
    {
        List<GameObject> children = GetSpawnedObjects();

        if (children.Count == 0)
        {
            Debug.LogWarning("Spawner: no spawned objects to reassign styles to.");
            return;
        }

        ApplyPreviewStyleSettings();

        System.Random rng = new System.Random(seed);

        for (int i = 0; i < children.Count; i++)
        {
            GameObject obj = children[i];
            if (obj == null)
                continue;

            SetupStylisedTag(obj, rng, i);
        }

        Debug.Log(
            $"Spawner: reassigned styles | " +
            $"pattern={stylePattern} | stylesPerObject={stylesPerObject} | " +
            $"sameStyleIndex={sameStyleIndex} | totalStyles={totalAvailableStyles} | seed={seed}");
    }

    private void SpawnObjects(int objectCount)
    {
        if (!ValidateSetup())
            return;

        System.Random rng = new System.Random(seed);

        GetSpawnBounds(out float minX, out float maxX, out float minY, out float maxY);

        for (int i = 0; i < objectCount; i++)
        {
            Vector3 position = GetRandomSpawnPosition(rng, minX, maxX, minY, maxY);

            GameObject obj = Instantiate(prefab, position, Quaternion.identity, transform);
            obj.name = $"SpawnedStylisedObject_{i}";
            obj.transform.localScale = objectScale;

            SetupStylisedTag(obj, rng, i);
        }

        Debug.Log(
            $"Spawner: spawned {objectCount} objects | " +
            $"pattern={stylePattern} | stylesPerObject={stylesPerObject} | " +
            $"sameStyleIndex={sameStyleIndex} | areaScale={spawnAreaScale} | seed={seed}");
    }

    private List<GameObject> GetSpawnedObjects()
    {
        List<GameObject> result = new();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null)
                result.Add(child.gameObject);
        }

        return result;
    }

    private bool ValidateSetup()
    {
        if (prefab == null)
        {
            Debug.LogError("Spawner: prefab is not assigned.");
            return false;
        }

        if (targetCamera == null)
        {
            Debug.LogError("Spawner: target camera is not assigned.");
            return false;
        }

        if (!targetCamera.orthographic)
        {
            Debug.LogWarning("Spawner currently assumes an orthographic camera.");
        }

        return true;
    }

    private void GetSpawnBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        float fullHeight = targetCamera.orthographicSize * 2f;
        float fullWidth = fullHeight * targetCamera.aspect;

        float spawnWidth = fullWidth * spawnAreaScale;
        float spawnHeight = fullHeight * spawnAreaScale;

        minX = -spawnWidth * 0.5f;
        maxX = spawnWidth * 0.5f;
        minY = -spawnHeight * 0.5f;
        maxY = spawnHeight * 0.5f;
    }

    private Vector3 GetRandomSpawnPosition(System.Random rng, float minX, float maxX, float minY, float maxY)
    {
        float x = Mathf.Lerp(minX, maxX, (float)rng.NextDouble());
        float y = Mathf.Lerp(minY, maxY, (float)rng.NextDouble());

        return new Vector3(x, y, spawnDepth);
    }

    private void SetupStylisedTag(GameObject obj, System.Random rng, int objectIndex)
    {
        StylisedTag tag = obj.GetComponent<StylisedTag>();
        if (tag == null)
            tag = obj.AddComponent<StylisedTag>();

        List<int> styles = GenerateStyles(rng, objectIndex);

        // match TestRunner behaviour
        tag.SetTestEffectCount(totalAvailableStyles);
        tag.UseRuntimeTestEffects();
        tag.ClearRuntimeTestEffects();
        tag.SetRuntimeTestEffects(styles);
        tag.Apply();

        Debug.Log($"{obj.name}: assigned styles [{string.Join(", ", styles)}]");
    }

    private List<int> GenerateStyles(System.Random rng, int objectIndex)
    {
        switch (stylePattern)
        {
            case StylePattern.SameStyle:
                return GenerateSameStyle();

            case StylePattern.RandomSingleStyle:
                return GenerateRandomSingleStyle(rng);

            case StylePattern.RandomMultiStyle:
                return GenerateRandomMultiStyle(rng);

            default:
                return new List<int> { 0 };
        }
    }

    private List<int> GenerateSameStyle()
    {
        return new List<int>
        {
            Mathf.Clamp(sameStyleIndex, 0, totalAvailableStyles - 1)
        };
    }

    private List<int> GenerateRandomSingleStyle(System.Random rng)
    {
        return new List<int>
        {
            rng.Next(totalAvailableStyles)
        };
    }

    private List<int> GenerateRandomMultiStyle(System.Random rng)
    {
        int count = Mathf.Clamp(stylesPerObject, 1, totalAvailableStyles);

        HashSet<int> uniqueStyles = new();
        while (uniqueStyles.Count < count)
            uniqueStyles.Add(rng.Next(totalAvailableStyles));

        return new List<int>(uniqueStyles);
    }

    private void ApplyPreviewStyleSettings()
    {
        stylePattern = previewPattern;
        totalAvailableStyles = Mathf.Clamp(previewTotalStyles, 1, 32);
        stylesPerObject = Mathf.Clamp(previewStylesPerObject, 1, 32);
        sameStyleIndex = Mathf.Clamp(previewSameStyleIndex, 0, totalAvailableStyles - 1);
        seed = previewSeed;
        spawnAreaScale = Mathf.Clamp(previewAreaScale, 0.05f, 1f);
    }

    private void GeneratePreviewNow()
    {
        ApplyPreviewStyleSettings();

        Regenerate(
            previewObjectCount,
            previewPattern,
            previewTotalStyles,
            previewStylesPerObject,
            previewSeed,
            previewAreaScale,
            previewSameStyleIndex
        );
    }

    private void ClearPreviewNow()
    {
        ClearSpawnedObjects();
    }

    private void ReassignStylesPreviewNow()
    {
        ReassignStylesToSpawnedObjects();
    }

    private void RequestGeneratePreview()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall += DelayedGeneratePreview;
#endif
    }

    private void RequestClearPreview()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall += DelayedClearPreview;
#endif
    }

    private void RequestReassignStylesPreview()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall += DelayedReassignStylesPreview;
#endif
    }

#if UNITY_EDITOR
    private void DelayedGeneratePreview()
    {
        EditorApplication.delayCall -= DelayedGeneratePreview;

        if (this == null)
            return;

        GeneratePreviewNow();
    }

    private void DelayedClearPreview()
    {
        EditorApplication.delayCall -= DelayedClearPreview;

        if (this == null)
            return;

        ClearPreviewNow();
    }

    private void DelayedReassignStylesPreview()
    {
        EditorApplication.delayCall -= DelayedReassignStylesPreview;

        if (this == null)
            return;

        ReassignStylesPreviewNow();
    }
#endif
}