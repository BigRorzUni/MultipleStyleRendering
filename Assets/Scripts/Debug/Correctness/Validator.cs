using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class Validator : MonoBehaviour
{
    [Header("Run Validation")]
    public bool trigger = false;

    private bool isRunning = false;

    private NprStylesRendererFeature n;


    [Header("Capture")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private int warmupFrames = 5;

    // tolerances to overlook noise
    [SerializeField] private float tolerance = 3.0f / 255.0f;
    [SerializeField] private float passDifferentPercent = 0.1f;
    [SerializeField] private float passMeanDifference = 1.0f / 255.0f;

    [Header("Output")]
    [SerializeField] private string outputFolder = "VisualValidation";


    private struct ValidationVariant
    {
        public NprRenderMode renderMode;
        public bool useMerging;
        public bool useOcclusion;
    }

    private struct CompareResult
    {
        public float differentPercent;
        public bool passed;
    }

    void Update()
    {
        if (!trigger)
            return;

        trigger = false;

        if (!Application.isPlaying)
        {
            Debug.LogWarning("Must be in play mode to run visual validation.");
            return;
        }

        if (isRunning)
        {
            Debug.LogWarning("Visual validation is already running.");
            return;
        }

        StartCoroutine(RunValidation());
    }

    private IEnumerator RunValidation()
    {
        isRunning = true;
        NprTestingConfig.IsValidationRunning = true;

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
        {
            Debug.LogError("No target camera found.");
            NprTestingConfig.IsValidationRunning = false;
            isRunning = false;
            yield break;
        }

        string outputPath = GetOutputPath();
        Directory.CreateDirectory(outputPath);

        List<ValidationVariant> variants = BuildVariants();
        Dictionary<string, Texture2D> captures = new();

        foreach (ValidationVariant variant in variants)
        {
            yield return ApplyVariant(variant);

            string label = GetVariantLabel(variant);

            Texture2D captured = CaptureCamera(targetCamera);
            captures[label] = captured;

            SaveTexture(captured, $"{label}.png");
        }

        string referenceLabel = GetVariantLabel(variants[0]);
        Texture2D reference = captures[referenceLabel];

        foreach (var pair in captures)
        {
            if (pair.Key == referenceLabel)
                continue;

            CompareResult result = CompareTextures(reference, pair.Value, tolerance);

            Debug.Log($"Visual validation: {referenceLabel} vs {pair.Key} | " + $"diff%={result.differentPercent:F6}, " + $"passed={result.passed}");

            SaveDiffImage(reference, pair.Value, $"{referenceLabel}_vs_{pair.Key}_diff.png");
        }

        // cleanup
        foreach (var tex in captures.Values)
            Destroy(tex);

        NprTestingConfig.IsValidationRunning = false;
        isRunning = false;

        Debug.Log($"Visual validation complete. Output saved to: {GetOutputPath()}");
    }

    private string GetVariantLabel(ValidationVariant variant)
    {
        return $"{variant.renderMode}_Merge{variant.useMerging}_Occ{variant.useOcclusion}";
    }

    private List<ValidationVariant> BuildVariants()
    {
        List<ValidationVariant> variants = new()
        {
            // Fullscreen capture (reference)
            new ValidationVariant { renderMode = NprRenderMode.Fullscreen, useMerging = false, useOcclusion = false },

            // CPU captures
            new ValidationVariant { renderMode = NprRenderMode.CPU, useMerging = false, useOcclusion = false },
            new ValidationVariant { renderMode = NprRenderMode.CPU, useMerging = true, useOcclusion = false },
            new ValidationVariant { renderMode = NprRenderMode.CPU, useMerging = false, useOcclusion = true },
            new ValidationVariant { renderMode = NprRenderMode.CPU, useMerging = true, useOcclusion = true },

            // GPU captures
            new ValidationVariant { renderMode = NprRenderMode.GPU, useMerging = false, useOcclusion = false },
            new ValidationVariant { renderMode = NprRenderMode.GPU, useMerging = true, useOcclusion = false },
            new ValidationVariant { renderMode = NprRenderMode.GPU, useMerging = false, useOcclusion = true },
            new ValidationVariant { renderMode = NprRenderMode.GPU, useMerging = true, useOcclusion = true },

            // Tiling capture (no merging or occlusion)
            new ValidationVariant { renderMode = NprRenderMode.Tiling, useMerging = false, useOcclusion = false }
        };

        return variants;
    }

    private IEnumerator ApplyVariant(ValidationVariant variant)
    {
        NprTestingConfig.DebugBBoxes = false;
        NprTestingConfig.DebugID = false;

        NprTestingConfig.RenderMode = variant.renderMode;
        NprTestingConfig.UseMerging = variant.useMerging;
        NprTestingConfig.UseOcclusion = variant.useOcclusion;

        RebuildRendererFeature();

        for (int i = 0; i < warmupFrames; i++)
            yield return null;
    }

    private void RebuildRendererFeature()
    {
        // find renderer feature 
        if (n == null)
        {
            ScriptableRenderer renderer = UniversalRenderPipeline.asset.GetRenderer(0);

            FieldInfo field = typeof(ScriptableRenderer).GetField("m_RendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
            IList list = field.GetValue(renderer) as IList;

            foreach (Object f in list)
            {
                if (f is NprStylesRendererFeature r)
                    n = (NprStylesRendererFeature)f;
            }

            if (n == null)
            {
                Debug.Log("no renderer feature found");
                return;
            }
        }

        n.Create();
    }
    private Texture2D CaptureCamera(Camera cam)
    {
        int width = cam.pixelWidth;
        int height = cam.pixelHeight;

        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);

        RenderTexture previousTarget = cam.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        cam.targetTexture = previousTarget;
        RenderTexture.active = previousActive;

        rt.Release();
        Destroy(rt);

        return tex;
    }

    private CompareResult CompareTextures(Texture2D reference, Texture2D candidate, float tolerance)
    {
        if (reference.width != candidate.width || reference.height != candidate.height)
        {
            Debug.LogError("Texture sizes do not match.");
            return new CompareResult { passed = false };
        }

        Color32[] a = reference.GetPixels32();
        Color32[] b = candidate.GetPixels32();

        int differentPixels = 0;
        double totalDifference = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            float dr = Mathf.Abs(a[i].r - b[i].r) / 255.0f;
            float dg = Mathf.Abs(a[i].g - b[i].g) / 255.0f;
            float db = Mathf.Abs(a[i].b - b[i].b) / 255.0f;

            float diff = Mathf.Max(dr, dg, db);

            totalDifference += diff;

            if (diff > tolerance)
                differentPixels++;
        }

        float differentPercent = 100.0f * differentPixels / a.Length;
        float meanDifference = (float)(totalDifference / a.Length);

        bool passed = differentPercent <= passDifferentPercent || meanDifference <= passMeanDifference;

        return new CompareResult
        {
            differentPercent = differentPercent,
            passed = passed
        };
    }

    private void SaveTexture(Texture2D tex, string filename)
    {
        string path = Path.Combine(GetOutputPath(), filename);
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }

    private void SaveDiffImage(Texture2D reference, Texture2D candidate, string filename)
    {
        int width = reference.width;
        int height = reference.height;

        Color32[] a = reference.GetPixels32();
        Color32[] b = candidate.GetPixels32();
        Color32[] diff = new Color32[a.Length];

        for (int i = 0; i < a.Length; i++)
        {
            byte r = (byte)Mathf.Abs(a[i].r - b[i].r);
            byte g = (byte)Mathf.Abs(a[i].g - b[i].g);
            byte bl = (byte)Mathf.Abs(a[i].b - b[i].b);

            diff[i] = new Color32(r, g, bl, 255);
        }

        Texture2D diffTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        diffTex.SetPixels32(diff);
        diffTex.Apply();

        SaveTexture(diffTex, filename);
        Destroy(diffTex);
    }

    private string GetOutputPath()
    {
        return Path.Combine(Application.dataPath, "..", outputFolder);
    }
}