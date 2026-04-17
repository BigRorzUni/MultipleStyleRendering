using System;
using System.Collections;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine;
using Unity.Profiling;
using System.IO;
using UnityEngine.Rendering.Universal;
using System.Reflection;
using System.Collections.Generic;

public static class NprTestingConfig
{
    public static NprRenderMode RenderMode = NprRenderMode.CPU;

    public static GpuMergeMethod GPUMergeMethod = GpuMergeMethod.BucketedUnion;
    public static TestEffect CurrentTestEffect = TestEffect.Dummy;

    public static bool UseMerging;
    public static bool UseOcclusion;
    public static bool TestMode = false;

    public static int N = 0; // total styles
    public static int K = 0; // actice styles in scene
    public static int StylesPerObject = 0; // max styles per object

    public static string SceneName = ""; // scene to test
    public static bool IsBenchmarkRunning = false;
    public static bool DebugBBoxes = false;
    public static bool DebugID = false;
}

public enum TestEffect
{
    Dummy,
    Heavy
}

public enum GpuMergeMethod
{
    PairwiseIterative,
    BucketedUnion
}

// what the test will be changing
public enum TestVariable
{
    N,
    K,
    StylesPerObject,
    ObjectCount,
    Coverage,
    SpawnAreaScale
}

[Serializable]
public enum TestEffectAssignmentMode
{
    Inspector,
    Runtime
}

[Serializable]
public class NprTestCase
{
    public string name;

    public string scene;
    public TestVariable variable; // variable to change in test
    public int[] values;

    // parameters
    public int N = 32;
    public int K = 1;
    public int stylesPerObject = 1;

    public int objectCount = 0;
    public float spawnAreaScale = 1.0f;

    public bool useMerging = false;
    public bool useOcclusion = false;
    public GpuMergeMethod gpuMergeMethod = GpuMergeMethod.BucketedUnion;
    public TestEffect testEffect = TestEffect.Dummy;
    public NprRenderMode[] renderModes;

    public TestEffectAssignmentMode effectMode = TestEffectAssignmentMode.Runtime;
}

[Serializable]
public class PassTimingCapture
{
    public string passName;
    public double[] cpuMs;
    public double[] gpuMs;

    public PassTimingCapture(string passName, int frameCount)
    {
        this.passName = passName;
        cpuMs = new double[frameCount];
        gpuMs = new double[frameCount];
    }
}

public class TestRunner : MonoBehaviour
{
    [SerializeField] private int startupFrames = 500;
    [SerializeField] private int framesToCapture = 1000;
    NprStylesRendererFeature n;

    [Header("pipeline config")]
    public NprRenderMode setRenderMode = NprRenderMode.CPU;
    public bool setUseMerging = true;
    public GpuMergeMethod setGpuMergeMethod = GpuMergeMethod.BucketedUnion;
    public bool setUseOcclusion = true;

    public bool setRendererTestmode = false;
    public bool setRuntimeTestEffectsInEditor = false;
    public bool setDebugBBoxes = false;
    public bool setDebugId = false;
    private string logDir = null;

    private ProfilerRecorder cpuFrameRec;
    private ProfilerRecorder gpuFrameRec;

    List<NprTestCase> tests = new()
    {
        // new NprTestCase
        // {
        //     name="AreaScaling",
        //     scene = "TestScene3",
        //     variable = TestVariable.Coverage,
        //     values = new [] {0,5,10,20,40,60,80,100},
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "StackedStylesScaling",
        //     scene = "TestScene2",
        //     variable = TestVariable.StylesPerObject,
        //     values = new[] { 0, 1, 2, 4, 8, 16, 32 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1,  10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_CPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_CPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_GPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_GPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     useMerging = true,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        
        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_CPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_CPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_GPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_GPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     useMerging = true,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        new NprTestCase
        {
            name = "BBoxCountScaling_SameStyle_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.ObjectCount,
            values = new[] { 1, 10, 50, 100, 500, 1000 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 0,
            spawnAreaScale = 1.0f,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxCountScaling_SameStyle_CPU_NoMerge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.ObjectCount,
            values = new[] { 1, 10, 50, 100, 500, 1000 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 0,
            spawnAreaScale = 1.0f,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxCountScaling_SameStyle_CPU_Merge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.ObjectCount,
            values = new[] { 1, 10, 50, 100, 500, 1000 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 0,
            spawnAreaScale = 1.0f,
            useMerging = true,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxCountScaling_SameStyle_GPU_NoMerge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.ObjectCount,
            values = new[] { 1, 10, 50, 100, 500, 1000 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 0,
            spawnAreaScale = 1.0f,
            useMerging = false,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxCountScaling_SameStyle_GPU_Merge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.ObjectCount,
            values = new[] { 1, 10, 50, 100, 500, 1000 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 0,
            spawnAreaScale = 1.0f,
            useMerging = true,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxAreaScaling_SameStyle_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.SpawnAreaScale,
            values = new[] { 100, 80, 60, 40, 20, 10 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 500,
            spawnAreaScale = 1.0f,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxAreaScaling_SameStyle_CPU_NoMerge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.SpawnAreaScale,
            values = new[] { 100, 80, 60, 40, 20, 10 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 500,
            spawnAreaScale = 1.0f,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxAreaScaling_SameStyle_CPU_Merge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.SpawnAreaScale,
            values = new[] { 100, 80, 60, 40, 20, 10 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 500,
            spawnAreaScale = 1.0f,
            useMerging = true,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxAreaScaling_SameStyle_GPU_NoMerge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.SpawnAreaScale,
            values = new[] { 100, 80, 60, 40, 20, 10 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 500,
            spawnAreaScale = 1.0f,
            useMerging = false,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "BBoxAreaScaling_SameStyle_GPU_Merge_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.SpawnAreaScale,
            values = new[] { 100, 80, 60, 40, 20, 10 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 500,
            spawnAreaScale = 1.0f,
            useMerging = true,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            testEffect = TestEffect.Heavy,
            effectMode = TestEffectAssignmentMode.Runtime,
        },
    };

    private void Awake()
    {
        // more readable debug outputs
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);

        // there can be only one...
        if (FindObjectsByType(typeof(TestRunner), FindObjectsSortMode.None).Length > 1)
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);

        // find the renderer feature
        if (n == null)
        {
            ScriptableRenderer renderer = UniversalRenderPipeline.asset.GetRenderer(0);

            FieldInfo field = typeof(ScriptableRenderer).GetField("m_RendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
            IList list = field.GetValue(renderer) as IList;

            foreach (UnityEngine.Object f in list)
            {
                if (f is NprStylesRendererFeature r)
                    n = (NprStylesRendererFeature)f;
            }

            if (n == null)
            {
                Debug.Log("No renderer feature found");
                return;
            }
        }

        string[] args = Environment.GetCommandLineArgs();

        // is testing enabled?
        if (!args.Contains("-runTests"))
            return;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "-frames":
                    if (int.TryParse(args[i + 1], out int frames))
                        framesToCapture = frames;
                    break;
                case "-warmup":
                    if (int.TryParse(args[i + 1], out int warmup))
                        startupFrames = warmup;
                    break;
                default:
                    break;
            }
        }

        NprTestingConfig.IsBenchmarkRunning = true;

        // turn off vsync for testin
        QualitySettings.vSyncCount = 0;

        // uncap framerate
        Application.targetFrameRate = -1;

        // initialise log dir path
        string appPath = Application.dataPath;
        string buildFolder = Directory.GetParent(appPath).Parent.Parent.FullName;

        logDir = Path.Combine(buildFolder, "ProfilingLogs");
    }

    private void ConfigureTagsForTestMode(TestEffectAssignmentMode mode, bool includeInactive = false)
    {
        StylisedTag[] tags;
        if (includeInactive)
            tags = FindObjectsByType<StylisedTag>(FindObjectsSortMode.None);
        else
            tags = FindObjectsByType<StylisedTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var tag in tags)
        {
            if (!tag) continue;

            tag.SetTestEffectCount(n.TestEffectCount);

            if (mode == TestEffectAssignmentMode.Inspector)
            {
                tag.UseInspectorTestEffects();
            }
            else
            {
                tag.UseRuntimeTestEffects();
                tag.ClearRuntimeTestEffects();
            }

            tag.Apply();
        }
    }

    private void ClearRuntimeTestStylesInScene()
    {
        var tags = FindObjectsByType<StylisedTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var tag in tags)
        {
            if (!tag)
                continue;

            tag.ClearRuntimeTestEffects();
            tag.Apply();
        }
    }

    private void ApplyTestStylesToScene(int k, int stylesPerObject)
    {
        StylisedTag[] tags = FindObjectsByType<StylisedTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        if (tags == null || tags.Length == 0)
        {
            Debug.LogWarning("TestRunner.ApplyTestStylesToScene: No StylisedTag found in scene.");
            return;
        }

        int s = Mathf.Clamp(stylesPerObject, 1, k);

        for (int objIndex = 0; objIndex < tags.Length; objIndex++)
        {
            StylisedTag tag = tags[objIndex];
            if (!tag) continue;

            int baseStyle = objIndex % k;
            List<int> styles = new();

            for (int t = 0; t < s; t++)
            {
                int style = (baseStyle + t) % k;
                // Debug.Log($"adding style {style}");
                styles.Add(style);
            }

            tag.UseRuntimeTestEffects();
            tag.SetRuntimeTestEffects(styles);
            tag.Apply();
        }

        Debug.Log($"Applied K={k}, stylesPerObject={s}, objects={tags.Length}");
    }

    // HELPER FUNC TO SPAWN OBJECTS WITH A GIVEN AREA OF SCREEN TAKEN UP\
    public void UpdateCoverage(float coveragePercent)
    {
        CoverageController coverageController = FindFirstObjectByType<CoverageController>();
        if (coverageController == null)
        {
            Debug.LogError("No CoverageController found in scene");
            return;
        }

        coverageController.UpdateCoverage(coveragePercent);
    }

    private IEnumerator ForceCleanup()
    {
        yield return null;
        yield return null;

        yield return Resources.UnloadUnusedAssets();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        yield return null;
    }

    private IEnumerator RegenerateSpawnedScene(int objectCount, int totalStyles, int stylesPerObject, float areaScale)
    {
        Spawner spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null)
        {
            Debug.LogError("No Spawner found in scene");
            yield break;
        }

        spawner.Regenerate(
            objectCount,
            StylePattern.SameStyle,
            totalStyles,
            stylesPerObject,
            12345,
            areaScale,
            0
        );

        yield return null;
        yield return null;
    }
    public void OnValidate()
    {
        if (n == null)
        {
            ScriptableRenderer renderer = UniversalRenderPipeline.asset.GetRenderer(0);

            FieldInfo field = typeof(ScriptableRenderer).GetField("m_RendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
            IList list = field.GetValue(renderer) as IList;

            foreach (UnityEngine.Object f in list)
            {
                if (f is NprStylesRendererFeature feature)
                    n = feature;
            }

            if (n == null)
            {
                Debug.Log("No renderer feature found");
                return;
            }
        }

        NprTestingConfig.TestMode = setRendererTestmode;
        NprTestingConfig.DebugBBoxes = setDebugBBoxes;
        NprTestingConfig.DebugID = setDebugId;
        NprTestingConfig.GPUMergeMethod = setGpuMergeMethod;

        NprTestingConfig.RenderMode = setRenderMode;
        NprTestingConfig.UseMerging = setUseMerging;
        NprTestingConfig.UseOcclusion = setUseOcclusion;

        if (NprTestingConfig.TestMode)
        {
            n.EnableTestMode(32);

            if (setRuntimeTestEffectsInEditor)
            {
                ConfigureTagsForTestMode(TestEffectAssignmentMode.Runtime, includeInactive: true);
                foreach (var tag in FindObjectsByType<StylisedTag>(FindObjectsSortMode.None))
                {
                    tag.SetRuntimeTestEffects(Enumerable.Range(0, n.TestEffectCount));
                    tag.Apply();
                }
            }
            else
            {
                ConfigureTagsForTestMode(TestEffectAssignmentMode.Inspector, includeInactive: true);
            }
        }
        else
        {
            n.DisableTestMode();

            foreach (var tag in FindObjectsByType<StylisedTag>(FindObjectsSortMode.None))
                tag.Apply();
        }
    }

    private IEnumerator RunAllTests()
    {
        foreach (var test in tests)
        {
            if (test == null || string.IsNullOrEmpty(test.scene))
            {
                Debug.LogWarning("Skipping null/invalid test case.");
                continue;
            }

            // run test for each value of the variable thats changing
            if (test.values == null || test.values.Length == 0)
            {
                Debug.LogWarning($"'{test.name}' has no values. Skipping.");
                continue;
            }

            List<int> shuffledValues = test.values.ToList();
            System.Random rng = new(12345); // fixed seed 

            for (int i = shuffledValues.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffledValues[i], shuffledValues[j]) = (shuffledValues[j], shuffledValues[i]);
            }

            Debug.Log($"Shuffled testing order {string.Join(", ", shuffledValues)}");

            foreach (int v in shuffledValues)
            {
                Debug.Log($"Testing value of {v}");

                // load the test scene
                Debug.Log($"Loading scene '{test.scene}' for '{test.name}'");
                AsyncOperation op = SceneManager.LoadSceneAsync(test.scene, LoadSceneMode.Single);
                if (op == null)
                {
                    Debug.LogError($"Could not load scene '{test.scene}'. Is it in Build Settings?");
                    yield break;
                }
                while (!op.isDone) yield return null;

                yield return ForceCleanup();

                Debug.Log($"Loaded scene: {test.scene}");

                NprRenderMode[] renderModes = test.renderModes != null && test.renderModes.Length > 0
                    ? test.renderModes
                    : new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU };

                foreach (var renderMode in renderModes)
                {
                    int curN = test.N;
                    int curK = test.K;
                    int curS = test.stylesPerObject;

                    switch (test.variable)
                    {
                        case TestVariable.N:
                            curN = Mathf.Clamp(v, 0, 32);
                            break;
                        case TestVariable.K:
                            curK = Mathf.Clamp(v, 0, 32);
                            break;
                        case TestVariable.StylesPerObject:
                            curS = Mathf.Clamp(v, 0, 32);
                            break;
                        case TestVariable.ObjectCount:
                            yield return RegenerateSpawnedScene(v, curN, curS, test.spawnAreaScale);
                            break;
                        case TestVariable.Coverage:
                            UpdateCoverage(v);
                            break;
                        case TestVariable.SpawnAreaScale:
                            yield return RegenerateSpawnedScene(test.objectCount, curN, curS, v / 100.0f);
                            break;
                    }

                    yield return ForceCleanup();

                    NprTestingConfig.SceneName = test.scene;
                    NprTestingConfig.TestMode = true;
                    NprTestingConfig.RenderMode = renderMode;
                    NprTestingConfig.N = curN;
                    NprTestingConfig.K = curK;
                    NprTestingConfig.StylesPerObject = curS;
                    NprTestingConfig.CurrentTestEffect = test.testEffect;

                    NprTestingConfig.UseMerging = test.useMerging;
                    NprTestingConfig.UseOcclusion = test.useOcclusion;

                    if (renderMode == NprRenderMode.GPU && test.useMerging)
                        NprTestingConfig.GPUMergeMethod = test.gpuMergeMethod;

                    n.EnableTestMode(curN);

                    if (test.variable != TestVariable.ObjectCount)
                        ConfigureTagsForTestMode(test.effectMode);

                    if (test.effectMode == TestEffectAssignmentMode.Runtime)
                    {
                        if (test.variable != TestVariable.ObjectCount)
                        {
                            ClearRuntimeTestStylesInScene();

                            if (curK > 0 && curS > 0)
                                ApplyTestStylesToScene(curK, curS);
                        }
                    }

                    Debug.Log($"Running {test.name} | {test.variable}={v} | mode={renderMode} | merging={test.useMerging} | effect={test.testEffect}");

                    Debug.Log($"{startupFrames} warmup frames...");
                    for (int i = 0; i < startupFrames; i++)
                        yield return null;

                    double[] cpuTimings = new double[framesToCapture];
                    double[] gpuTimings = new double[framesToCapture];

                    Debug.Log("Capturing frames...");
                    for (int i = 0; i < framesToCapture; i++)
                    {
                        yield return null;
                        cpuTimings[i] = cpuFrameRec.LastValue / 1_000_000.0;
                        gpuTimings[i] = gpuFrameRec.LastValue / 1_000_000.0;
                    }

                    CsvWriter.EnsureDirectoryExists(logDir);

                    string framesPath = CsvWriter.CombinePath(
                        logDir,
                        $"{test.name}_{test.variable}_{v}_{renderMode}_frames.csv");

                    CsvWriter.WriteFrameTimings(framesPath, cpuTimings, gpuTimings);

                    string summaryPath = CsvWriter.CombinePath(logDir, "summary.csv");

                    CsvWriter.AppendSummaryRow(
                        summaryPath,
                        test,
                        v,
                        renderMode,
                        curN,
                        curK,
                        curS,
                        cpuTimings,
                        gpuTimings);

                    Debug.Log($"Timings saved at {framesPath}");
                }
            }
        }

        Debug.Log("Testing done!");
        NprTestingConfig.IsBenchmarkRunning = false;
        Application.Quit();
    }

    private void Start()
    {
        // TEMPORARY FOR TESTING
        //ApplyTestStylesToScene(32, 1);

        if (!NprTestingConfig.IsBenchmarkRunning)
            return;

        CsvWriter.EnsureDirectoryExists(logDir);
        Debug.Log($"Starting tests. logDir = {logDir}, startupFrames = {startupFrames}, frames = {framesToCapture}");

        cpuFrameRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Total Frame Time");
        gpuFrameRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");

        if (!cpuFrameRec.Valid)
        {
            Debug.LogError("CPU recorder is not valid");
            return;
        }

        if (!gpuFrameRec.Valid)
        {
            Debug.LogError("GPU recorder is not valid?");
            return;
        }

        NprTestingConfig.DebugBBoxes = false;

        StartCoroutine(RunAllTests());
    }
}