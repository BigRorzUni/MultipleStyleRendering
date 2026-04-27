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
    public static TileSize CurrentTileSize = TileSize.Size32;


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

public enum TileSize
{
    Size8 = 8,
    Size16 = 16,
    Size32 = 32,
    Size64 = 64
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
    Overlap
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
    public float coverageFraction = 1.0f;
    public float overlapFraction = 0.0f;
    public float spawnAreaScale = 1.0f;
    public StylePattern stylePattern = StylePattern.SameStyle;

    public bool useMerging = false;
    public bool useOcclusion = false;
    public bool useOcclusionCoverageController = false;
    public GpuMergeMethod gpuMergeMethod = GpuMergeMethod.BucketedUnion;
    public TestEffect testEffect = TestEffect.Dummy;
    public TileSize tileSize = TileSize.Size32;  
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
        // Style scaling, same style, 0% coverage
        new NprTestCase
        {
            name = "StyleScaling_SameStyle_Cov0",
            scene = "TestScene_Spawner",
            variable = TestVariable.StylesPerObject,
            values = new[] { 1, 2, 4, 8, 16, 32 },
            N = 32,
            K = 32,
            stylesPerObject = 1,
            objectCount = 1,
            coverageFraction = 0.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        // Style scaling, same style, 25% coverage
        new NprTestCase
        {
            name = "StyleScaling_SameStyle_Cov25",
            scene = "TestScene_Spawner",
            variable = TestVariable.StylesPerObject,
            values = new[] { 1, 2, 4, 8, 16, 32 },
            N = 32,
            K = 32,
            stylesPerObject = 1,
            objectCount = 1,
            coverageFraction = 0.25f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        // Style scaling, same style, 50% coverage
        new NprTestCase
        {
            name = "StyleScaling_SameStyle_Cov50",
            scene = "TestScene_Spawner",
            variable = TestVariable.StylesPerObject,
            values = new[] { 1, 2, 4, 8, 16, 32 },
            N = 32,
            K = 32,
            stylesPerObject = 1,
            objectCount = 1,
            coverageFraction = 0.5f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        // Style scaling, same style, 75% coverage
        new NprTestCase
        {
            name = "StyleScaling_SameStyle_Cov75",
            scene = "TestScene_Spawner",
            variable = TestVariable.StylesPerObject,
            values = new[] { 1, 2, 4, 8, 16, 32 },
            N = 32,
            K = 32,
            stylesPerObject = 1,
            objectCount = 1,
            coverageFraction = 0.75f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        // Style scaling, same style, 100% coverage
        new NprTestCase
        {
            name = "StyleScaling_SameStyle_Cov100",
            scene = "TestScene_Spawner",
            variable = TestVariable.StylesPerObject,
            values = new[] { 1, 2, 4, 8, 16, 32 },
            N = 32,
            K = 32,
            stylesPerObject = 1,
            objectCount = 1,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
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

    private void PrepareSceneTagsForRuntime()
    {
        var tags = FindObjectsByType<StylisedTag>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (var tag in tags)
        {
            if (!tag) continue;

            tag.SetTestEffectCount(n.TestEffectCount);
            tag.UseRuntimeTestEffects();
        }
    }

    private void ApplySameMaskToScene(int stylesPerObject)
    {
        StylisedTag[] tags = FindObjectsByType<StylisedTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        if (tags == null || tags.Length == 0)
        {
            Debug.LogWarning("ApplySameMaskToScene: No StylisedTag found in scene.");
            return;
        }

        int count = Mathf.Clamp(stylesPerObject, 0, n.TestEffectCount);

        List<int> styles = new(count);
        for (int i = 0; i < count; i++)
            styles.Add(i);

        foreach (var tag in tags)
        {
            if (!tag) continue;

            tag.UseRuntimeTestEffects();
            tag.ClearRuntimeTestEffects();
            tag.SetRuntimeTestEffects(styles);
            tag.Apply();
        }

        Debug.Log($"Applied same mask with {count} styles to {tags.Length} objects.");
    }

    private void ApplyRandomSingleStylesToScene(int totalStyles)
    {
        StylisedTag[] tags = FindObjectsByType<StylisedTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        if (tags == null || tags.Length == 0)
        {
            Debug.LogWarning("ApplyRandomSingleStylesToScene: No StylisedTag found in scene.");
            return;
        }

        int k = Mathf.Clamp(totalStyles, 1, n.TestEffectCount);

        for (int objIndex = 0; objIndex < tags.Length; objIndex++)
        {
            StylisedTag tag = tags[objIndex];
            if (!tag) continue;

            int style = objIndex % k;

            tag.UseRuntimeTestEffects();
            tag.ClearRuntimeTestEffects();
            tag.SetRuntimeTestEffects(new List<int> { style });
            tag.Apply();
        }

        Debug.Log($"Applied rotating single-style masks across {k} styles to {tags.Length} objects.");
    }
    // HELPER FUNC TO SPAWN OBJECTS WITH A GIVEN AREA OF SCREEN TAKEN UP\
    public void UpdateCoverage(float coveragePercent)
    {
        OcclusionController occlusionController = FindFirstObjectByType<OcclusionController>();
        if (occlusionController == null)
        {
            Debug.LogError("No Occlusion Controller found in scene");
            return;
        }

        occlusionController.UpdateCoverage(coveragePercent);
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
    
    private IEnumerator RegenerateSpawnedScene(int objectCount, float coverageFraction, float overlapFraction = 0f)
    {
        Spawner spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null)
        {
            Debug.LogError("No Spawner found in scene");
            yield break;
        }

        spawner.Regenerate(objectCount, coverageFraction, overlapFraction);

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

            // List<int> shuffledValues = test.values.ToList();
            // System.Random rng = new(12345); // fixed seed 

            // for (int i = shuffledValues.Count - 1; i > 0; i--)
            // {
            //     int j = rng.Next(i + 1);
            //     (shuffledValues[i], shuffledValues[j]) = (shuffledValues[j], shuffledValues[i]);
            // }

            // Debug.Log($"Shuffled testing order {string.Join(", ", shuffledValues)}");

            foreach (int v in test.values.ToList())
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

                NprRenderMode[] renderModes = test.renderModes != null && test.renderModes.Length > 0 ? test.renderModes : new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling };

                foreach (var renderMode in renderModes)
                {
                    int curN = test.N;
                    int curK = test.K;
                    int curS = test.stylesPerObject;

                    // set var for this run
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
                    }

                    // configure runtime before styles
                    NprTestingConfig.SceneName = test.scene;
                    NprTestingConfig.TestMode = true;
                    NprTestingConfig.RenderMode = renderMode;
                    NprTestingConfig.N = curN;
                    NprTestingConfig.K = curK;
                    NprTestingConfig.StylesPerObject = curS;
                    NprTestingConfig.CurrentTestEffect = test.testEffect;
                    NprTestingConfig.CurrentTileSize = test.tileSize;
                    NprTestingConfig.UseMerging = test.useMerging;
                    NprTestingConfig.UseOcclusion = test.useOcclusion;

                    if (renderMode == NprRenderMode.GPU && test.useMerging)
                        NprTestingConfig.GPUMergeMethod = test.gpuMergeMethod;

                    n.EnableTestMode(curN);

                    // scene setup depending on variable being tested
                    switch (test.variable)
                    {
                        case TestVariable.ObjectCount:
                            yield return RegenerateSpawnedScene(v, test.coverageFraction, 0f);
                            break;

                        case TestVariable.Coverage:
                            if (test.useOcclusionCoverageController)
                                {
                                    yield return RegenerateSpawnedScene(test.objectCount, test.coverageFraction, test.overlapFraction);

                                    yield return null;
                                    UpdateCoverage(v);
                                    yield return null;
                                }
                                else
                                {
                                    yield return RegenerateSpawnedScene(test.objectCount, v / 100f, test.overlapFraction);
                                }
                                break;

                        case TestVariable.Overlap:
                            yield return RegenerateSpawnedScene(test.objectCount, test.coverageFraction, v / 100f);
                            break;

                        case TestVariable.StylesPerObject:
                            yield return RegenerateSpawnedScene(test.objectCount, test.coverageFraction, 0f);
                            break;

                        case TestVariable.N:
                            yield return RegenerateSpawnedScene(test.objectCount, test.coverageFraction, 0f);
                            break;

                        default:
                            // for non-spawner-driven tests
                            break;
                    }

                    yield return ForceCleanup();

                    PrepareSceneTagsForRuntime();

                    if (test.stylePattern == StylePattern.SameStyle)
                    {
                        ApplySameMaskToScene(curS);
                    }
                    else if (test.stylePattern == StylePattern.RandomSingleStyle)
                    {
                        ApplyRandomSingleStylesToScene(curK);
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

                    string framesPath = CsvWriter.CombinePath(logDir, $"{test.name}_{test.variable}_{v}_{renderMode}_frames.csv");

                    CsvWriter.WriteFrameTimings(framesPath, cpuTimings, gpuTimings);

                    string summaryPath = CsvWriter.CombinePath(logDir, "summary.csv");

                    CsvWriter.AppendSummaryRow(summaryPath, test, v, renderMode, curN, curK, curS, cpuTimings, gpuTimings);

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