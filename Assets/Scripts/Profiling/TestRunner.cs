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

    public static GpuMergeMethod GPUMergeMethod = GpuMergeMethod.PairwiseIterative;

    public static bool UseMerging;
    public static bool UseOcclusion;
    public static bool TestMode = false;
    public static bool BoundingBoxes = true;

    public static int N = 0; // total styles
    public static int K = 0; // actice styles in scene
    public static int StylesPerObject = 0; // max styles per object

    public static string SceneName = ""; // scene to test
    public static bool IsBenchmarkRunning = false;
    public static bool DebugBBoxes = false;
    public static bool DebugID = false;
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
    Coverage
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

    public TestEffectAssignmentMode effectMode = TestEffectAssignmentMode.Runtime;
}

public class TestRunner : MonoBehaviour
{

    [SerializeField] private int startupFrames = 500;
    [SerializeField] private int framesToCapture = 1000;
    NprStylesRendererFeature n;

    [Header("pipeline config")]
    public NprRenderMode setRenderMode = NprRenderMode.CPU;
    public bool setUseMerging = true;
    public GpuMergeMethod setGpuMergeMethod = GpuMergeMethod.PairwiseIterative;
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
        //     name = "TotalStylesScaling",
        //     scene = "TestScene1",
        //     variable = TestVariable.N,
        //     values = new [] {0,1,2,4,8,16,32},
        //     K = 0,
        //     stylesPerObject = 0,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "StackedStylesScaling",
        //     scene = "TestScene2",
        //     variable = TestVariable.StylesPerObject,
        //     values = new [] {0,1,2,4,8,16,32},
        //     K = 32,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        new NprTestCase
        {
            name="AreaScaling1Style",
            scene = "TestScene3",
            variable = TestVariable.Coverage,
            values = new [] {0,5,10,20,40,60,80,100},
            N=1,
            K = 1,
            stylesPerObject = 1,
             effectMode = TestEffectAssignmentMode.Runtime,
        },
        new NprTestCase
        {
            name="AreaScaling32Style",
            scene = "TestScene3",
            variable = TestVariable.Coverage,
            values = new [] {0,5,10,20,40,60,80,100},
            N = 32,
            K = 32,
            stylesPerObject = 32,
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
        if(n == null)
        {
            ScriptableRenderer renderer = UniversalRenderPipeline.asset.GetRenderer(0);

            FieldInfo field = typeof(ScriptableRenderer).GetField("m_RendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
            IList list = field.GetValue(renderer) as IList;

            foreach (UnityEngine.Object f in list)
            {
                if (f is NprStylesRendererFeature r)
                    n = (NprStylesRendererFeature)f;
            }

            if(n == null)
            {
                Debug.Log("No renderer feature found");
                return;
            }
        }

        string[] args = Environment.GetCommandLineArgs();

        // is testing enabled?
        if(!args.Contains("-runTests"))
            return;

        for(int i = 0; i < args.Length-1; i++)
        {
            switch(args[i])
            {
                case "-frames":
                    if(int.TryParse(args[i+1], out int frames))
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
        if(includeInactive)
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
                Debug.Log($"adding style {style}");
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
        if(coverageController == null)
        {
            Debug.LogError("No CoverageController found in scene");
            return;
        }

        coverageController.UpdateCoverage(coveragePercent);
    }

    public void OnValidate()
    {
        if(n == null)
        {
            ScriptableRenderer renderer = UniversalRenderPipeline.asset.GetRenderer(0);

            FieldInfo field = typeof(ScriptableRenderer).GetField("m_RendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
            IList list = field.GetValue(renderer) as IList;

            foreach (UnityEngine.Object f in list)
            {
                if (f is NprStylesRendererFeature feature)
                    n = feature;
            }

            if(n == null)
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
                ConfigureTagsForTestMode(TestEffectAssignmentMode.Inspector, includeInactive: true);
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
        foreach(var test in tests)
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

                Debug.Log($"Loaded scene: {test.scene}");

                // test with bboxes on and bboxes off
                bool[] bboxModes = { true, false }; // extend this for more modes
                foreach (bool bboxMode in bboxModes)
                {
                    // get the current run from test config
                    int curN = test.N;
                    int curK = test.K;
                    int curS = test.stylesPerObject;
                    bool curUseBBoxes = bboxMode;

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

                        case TestVariable.Coverage:
                            Debug.Log("Updating coverage to " + v + "%");
                            UpdateCoverage(v);
                            break;

                        default:
                            break;
                    }

                    // store into global current config
                    NprTestingConfig.SceneName = test.scene;
                    NprTestingConfig.TestMode = true;
                    NprTestingConfig.BoundingBoxes = curUseBBoxes;
                    NprTestingConfig.N = curN;
                    NprTestingConfig.K = curK;
                    NprTestingConfig.StylesPerObject = curS;

                    n.EnableTestMode(curN);

                    ConfigureTagsForTestMode(test.effectMode);

                    if (test.effectMode == TestEffectAssignmentMode.Runtime)
                    {
                        Debug.Log($"Clearing runtime styles before testing (BB={curUseBBoxes})");
                        var tags = FindObjectsByType<StylisedTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                        foreach (var tag in tags)
                        {
                            tag.ClearRuntimeTestEffects();
                            tag.Apply();
                        }

                        if (curK > 0 && curS > 0)
                        {
                            Debug.Log("Applying test styles");
                            ApplyTestStylesToScene(curK, curS);
                        }
                    }

                    Debug.Log($"Running {test.name} | {test.variable}={v} | BB={curUseBBoxes}");
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

                    Directory.CreateDirectory(logDir);
                    string bboxLabel = curUseBBoxes ? "bboxes" : "fullscreen";
                    string path = Path.Combine(logDir, $"{test.name}_{test.variable}_{v}_{bboxLabel}.csv");

                    using (StreamWriter sw = new StreamWriter(path, false))
                    {
                        sw.WriteLine("frame,cpu_ms,gpu_ms");
                        for (int i = 0; i < framesToCapture; i++)
                        {
                            sw.Write(i);
                            sw.Write(",");
                            sw.Write(cpuTimings[i]);
                            sw.Write(",");
                            sw.Write(gpuTimings[i]);
                            sw.WriteLine();
                        }
                    }

                    Debug.Log($"Timings saved at {path}");
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

        if(!NprTestingConfig.IsBenchmarkRunning)
            return;
        
        Directory.CreateDirectory(logDir);
        Debug.Log($"Starting tests. logDir = {logDir}, startupFrames = {startupFrames}, frames = {framesToCapture}");

        cpuFrameRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Total Frame Time");
        gpuFrameRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");

        if(!cpuFrameRec.Valid)
        {
            Debug.LogError("CPU recorder is not valid");
            return;
        }

        if(!gpuFrameRec.Valid)
        {
            Debug.LogError("GPU recorder is not valid?");
            return;
        }

        NprTestingConfig.DebugBBoxes = false;

        StartCoroutine(RunAllTests());
    }
}