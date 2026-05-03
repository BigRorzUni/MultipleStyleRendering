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

public enum StylePattern
{
    SameStyle,
    RandomSingleStyle,
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

// what the test will be changing
public enum TestVariable
{
    N,
    K,
    StylesPerObject,
    ObjectCount,
    Coverage,
    Overlap,
    ObjectScale
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
    public int N = 32; // number of queued styles
    public int K = 1; // number of applied styles in scene
    public int stylesPerObject = 1; // number of styles per each object

    public int objectCount = 0;
    public float coverageFraction = 1.0f;
    public float overlapFraction = 0.0f;
    public float objectScaleFactor = 1.0f;
    public StylePattern stylePattern = StylePattern.SameStyle;

    public bool useMerging = false;
    public bool useOcclusion = false;
    public bool useOcclusionCoverageController = false;
    public TestEffect testEffect = TestEffect.Dummy;
    public TileSize tileSize = TileSize.Size32;  
    public NprRenderMode[] renderModes;

    public TestEffectAssignmentMode effectMode = TestEffectAssignmentMode.Runtime;
}

public class TestRunner : MonoBehaviour
{
    private int startupFrames = 500;
    private int framesToCapture = 500;
    NprStylesRendererFeature n;

    private string logDir = null;

    private readonly List<int> _styleIndices = new();
    private readonly List<int> _singleStyleIndex = new(1);

    private ProfilerRecorder cpuFrameRec;
    private ProfilerRecorder gpuFrameRec;

    List<NprTestCase> tests = new()
    {
        new NprTestCase
        {
            name = "OcclusionScaling_SameStyle_CPU_Occlusion_Obj100",
            scene = "TestScene_Occlusion",
            variable = TestVariable.Coverage,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 32,
            K = 32,
            stylesPerObject = 32,
            objectCount = 100,
            coverageFraction = 0.5f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = true,
            useOcclusionCoverageController = true,
            renderModes = new[] { NprRenderMode.CPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov50_SameStyle_GPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 16, 128, 512, 1024, 2048, 4096, 8192},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov50_SameStyle_GPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov50_SameStyle_CPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 16, 128, 512, 1024, 2048, 4096},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov50_SameStyle_CPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 16, 128, 512, 1024, 2048, 4096},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "TilingCountScaling_Cov50_Tile8",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 8192 * 2 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size8,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "TilingCountScaling_Cov50_Tile16",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 8192 * 2},
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size16,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "TilingCountScaling_Cov50_Tile32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 8192 * 2},
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "TilingCountScaling_Cov50_Tile64",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 8192 * 2 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size64,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov50_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 16384 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 1,
        //     coverageFraction = 0.5f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,


        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.GPU, NprRenderMode.Tiling},
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
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
        string buildFolder = Directory.GetParent(appPath).Parent.FullName;

        logDir = Path.Combine(buildFolder, "ProfilingLogs");
    }

    private void PrepareSceneTagsForRuntime()
    {
        foreach (var tag in StylisedTag.ActiveTags)
        {
            if (!tag) continue;

            tag.SetTestEffectCount(n.TestEffectCount);
            tag.UseRuntimeTestEffects();
        }
    }

    private void ApplySameMaskToScene(int stylesPerObject)
    {
        if (StylisedTag.ActiveTags == null || StylisedTag.ActiveTags.Count == 0)
        {
            Debug.LogWarning("ApplySameMaskToScene: No StylisedTag found in scene.");
            return;
        }

        int count = Mathf.Clamp(stylesPerObject, 0, n.TestEffectCount);

        _styleIndices.Clear();
        for (int i = 0; i < count; i++)
            _styleIndices.Add(i);

        int applied = 0;

        foreach (var tag in StylisedTag.ActiveTags)
        {
            if (!tag) continue;

            tag.UseRuntimeTestEffects();
            tag.ClearRuntimeTestEffects();
            tag.SetRuntimeTestEffects(_styleIndices);
            tag.Apply();

            applied++;
        }

        Debug.Log($"Applied same mask with {count} styles to {applied} objects.");
    }

    private void ApplyRandomSingleStylesToScene(int totalStyles)
    {
        if (StylisedTag.ActiveTags == null || StylisedTag.ActiveTags.Count == 0)
        {
            Debug.LogWarning("ApplyRandomSingleStylesToScene: No StylisedTag found in scene.");
            return;
        }

        int k = Mathf.Clamp(totalStyles, 1, n.TestEffectCount);

        int objIndex = 0;
        int applied = 0;

        foreach (var tag in StylisedTag.ActiveTags)
        {
            if (!tag) continue;

            int style = objIndex % k;

            _singleStyleIndex.Clear();
            _singleStyleIndex.Add(style);

            tag.UseRuntimeTestEffects();
            tag.ClearRuntimeTestEffects();
            tag.SetRuntimeTestEffects(_singleStyleIndex);
            tag.Apply();

            objIndex++;
            applied++;
        }

        Debug.Log($"Applied rotating single-style masks across {k} styles to {applied} objects.");
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
    
    private IEnumerator RegenerateSpawnedScene(int objectCount, float coverageFraction, float overlapFraction = 0f, float objectScaleFactor = 1f)
    {
        Spawner spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null)
        {
            Debug.LogError("No Spawner found in scene");
            yield break;
        }

        spawner.Regenerate(objectCount, coverageFraction, overlapFraction, objectScaleFactor);

        yield return null;
        yield return null;
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

            foreach (int v in test.values)
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
                    int curObjectCount = test.objectCount;

                    // set var for this run
                    switch (test.variable)
                    {
                        case TestVariable.N:
                            curN = Mathf.Clamp(v, 0, 32);
                            break;
                        case TestVariable.K:
                            curK = Mathf.Clamp(v, 0, 32);
                            break;
                        case TestVariable.ObjectCount:
                            curObjectCount = v;
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

                    // if (renderMode == NprRenderMode.GPU && test.useMerging)
                    //     NprTestingConfig.GPUMergeMethod = GpuMergeMethod.BucketedUnion; // not benchmarking the other one as it is not correct

                    n.EnableTestMode(curN);

                    // scene setup depending on variable being tested
                    switch (test.variable)
                    {
                        case TestVariable.Coverage:
                            if (test.useOcclusionCoverageController)
                                {
                                    yield return RegenerateSpawnedScene(curObjectCount, test.coverageFraction, test.overlapFraction, test.objectScaleFactor);

                                    yield return null;
                                    UpdateCoverage(v);
                                    yield return null;
                                }
                                else
                                {
                                    yield return RegenerateSpawnedScene(curObjectCount, v / 100f, test.overlapFraction, test.objectScaleFactor);
                                }
                                break;

                        case TestVariable.Overlap:
                            yield return RegenerateSpawnedScene(curObjectCount, test.coverageFraction, v / 100f, test.objectScaleFactor);
                            break;

                        case TestVariable.ObjectScale:
                            yield return RegenerateSpawnedScene(curObjectCount, test.coverageFraction, test.overlapFraction, 1.0f / v);
                            break;

                        default:
                            yield return RegenerateSpawnedScene(curObjectCount, test.coverageFraction, 0f, test.objectScaleFactor);
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