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
        #region COMPLETED TESTS
        // INCREASING AREA COVERAGE

        // new NprTestCase
        // {
        //     name="AreaScaling",
        //     scene = "CoverageTest",
        //     variable = TestVariable.Coverage,
        //     values = new [] {0,5,10,20,40,60,80,100},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // STACKED STYLES

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

        // OCCLUSION LEVEL TESTS

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_CPU_NoOcclusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_CPU_Occlusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = true,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_GPU_NoOcclusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_GPU_Occlusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = true,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_CPU_NoOcclusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_CPU_Occlusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = true,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_GPU_NoOcclusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_GPU_Occlusion",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = true,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // INCREASING BBOXES, SINGLE STYLE (BEST CASE FOR MERGING)

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1,  10, 50, 100, 500, 1000, 2000, 5000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     renderModes = new[] { NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Fullscreen, NprRenderMode.Tiling },
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

        // INCREASING BBOXES WITH AREA SCALING, SINGLE STYLE (BEST CASE FOR MERGING)
        
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

        // INCREASING OBJECT COUNT WITH MANY STYLE VARIATIONS (WORST CASE FOR MERGING)
        #region Object Count Scaling with Many Styles

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_CPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_CPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_GPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_GPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        #endregion

        // // INCREASING AREA COVERAGE WITH MANY STYLE VARIATIONS (WORST CASE FOR MERGING)
        #region Area Scaling with Many Styles

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_RandomSingleStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_RandomSingleStyle_CPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_RandomSingleStyle_CPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_RandomSingleStyle_GPU_NoMerge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_RandomSingleStyle_GPU_Merge",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        #endregion

        // HEAVY VARIATIONS

        #region Heavy Variations

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_Heavy",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     spawnAreaScale = 1.0f,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU },
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_CPU_NoMerge_Heavy",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     spawnAreaScale = 1.0f,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_CPU_Merge_Heavy",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     spawnAreaScale = 1.0f,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_GPU_NoMerge_Heavy",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     spawnAreaScale = 1.0f,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_GPU_Merge_Heavy",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     spawnAreaScale = 1.0f,
        //     useMerging = true,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_Heavy",
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
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_CPU_NoMerge_Heavy",
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
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_CPU_Merge_Heavy",
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
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_GPU_NoMerge_Heavy",
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
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_GPU_Merge_Heavy",
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
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        #endregion
    
        // TILING TESTS

        // INCREASING AREA COVERAGE

        // // STACKED STYLES
        // new NprTestCase
        // {
        //     name = "StackedStylesScaling_Tiling32",
        //     scene = "TestScene2",
        //     variable = TestVariable.StylesPerObject,
        //     values = new[] { 0, 1, 2, 4, 8, 16, 32 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // // OCCLUSION / COVERAGE STYLE TEST
        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_Tiling32",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        
        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_Tiling32",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // // INCREASING BBOX/TILE COUNT, SINGLE STYLE
        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // // INCREASING BBOX/TILE AREA DENSITY, SINGLE STYLE
        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // // INCREASING OBJECT COUNT, MANY STYLE VARIATIONS
        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // // INCREASING AREA DENSITY, MANY STYLE VARIATIONS
        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_RandomSingleStyle_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // // HEAVY SINGLE-STYLE OBJECT COUNT
        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_Heavy_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     spawnAreaScale = 1.0f,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // // HEAVY SINGLE-STYLE AREA DENSITY
        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_Heavy_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     testEffect = TestEffect.Heavy,
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // TILING WITH DIFFERENT TILE SIZES;
        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_Tiling8",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size8,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_Tiling16",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size16,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_SameStyle_Tiling64",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size64,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_Tiling8",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     tileSize = TileSize.Size8,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_Tiling16",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     tileSize = TileSize.Size16,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     tileSize = TileSize.Size32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxAreaScaling_SameStyle_Tiling64",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.SpawnAreaScale,
        //     values = new[] { 100, 80, 60, 40, 20, 10 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     tileSize = TileSize.Size64,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_Tiling8",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size8,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_Tiling16",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size16,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_Tiling32",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_RandomSingleStyle_Tiling64",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size64,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_Tiling8",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size8,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_Tiling16",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size16,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_Tiling32",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OcclusionScaling_SameStyle_Tiling64",
        //     scene = "TestScene_Occlusion",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     spawnAreaScale = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     tileSize = TileSize.Size64,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "AreaScaling_SameStyle_Tiling8",
        //     scene = "CoverageTest",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 5, 10, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size8,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "AreaScaling_SameStyle_Tiling16",
        //     scene = "CoverageTest",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 5, 10, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size16,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "AreaScaling_SameStyle_Tiling32",
        //     scene = "CoverageTest",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 5, 10, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "AreaScaling_SameStyle_Tiling64",
        //     scene = "CoverageTest",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 5, 10, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     tileSize = TileSize.Size64,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_Tiling8",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size8,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_Tiling16",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size16,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_Tiling32",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size32,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "BBoxCountScaling_RandomSingleStyle_Tiling64",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 1,
        //     stylePattern = StylePattern.RandomSingleStyle,
        //     tileSize = TileSize.Size64,
        //     renderModes = new[] { NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
    
        // new NprTestCase
        // {
        //     name = "CountScaling_Cov25_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000, 2000, 5000},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     coverageFraction = 0.25f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov25_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] {  1000, 2000, 5000},
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 0,
        //     coverageFraction = 0.25f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov75_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000, 2000, 5000},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     coverageFraction = 0.75f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "CountScaling_Cov75_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000, 2000, 5000},
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 0,
        //     coverageFraction = 0.75f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov50_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000, 2000, 5000},
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 0,
        //     coverageFraction = 0.5f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        

        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov100_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000, 2000, 5000},
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 0,
        //     coverageFraction = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CountScaling_Cov100_SameStyle",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.ObjectCount,
        //     values = new[] { 1, 10, 50, 100, 500, 1000, 2000, 5000},
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 0,
        //     coverageFraction = 1.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj1",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 1,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj1",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 1,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj100",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 100,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj100",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 100,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj2000",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 2000,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "CoverageScaling_Obj2000",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Coverage,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 2000,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj10",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 10,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj10",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 10,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj100",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 100,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj100",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 100,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj5000",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 32,
        //     K = 32,
        //     stylesPerObject = 32,
        //     objectCount = 5000,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        // new NprTestCase
        // {
        //     name = "OverlapScaling_Obj5000",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 5000,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU, NprRenderMode.Tiling },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },
        #endregion
        // new NprTestCase
        // {
        //     name = "OverlapScaling_SameStyle_CPU_NoMerge_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OverlapScaling_SameStyle_CPU_Merge_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     renderModes = new[] { NprRenderMode.CPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OverlapScaling_SameStyle_GPU_NoMerge_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = false,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },

        // new NprTestCase
        // {
        //     name = "OverlapScaling_SameStyle_GPU_Merge_Obj500",
        //     scene = "TestScene_Spawner",
        //     variable = TestVariable.Overlap,
        //     values = new[] { 0, 20, 40, 60, 80, 100 },
        //     N = 1,
        //     K = 1,
        //     stylesPerObject = 1,
        //     objectCount = 500,
        //     coverageFraction = 1.0f,
        //     overlapFraction = 0.0f,
        //     stylePattern = StylePattern.SameStyle,
        //     useMerging = true,
        //     useOcclusion = false,
        //     gpuMergeMethod = GpuMergeMethod.BucketedUnion,
        //     renderModes = new[] { NprRenderMode.GPU },
        //     effectMode = TestEffectAssignmentMode.Runtime,
        // },



        // 100 OBJECTS 
        // 1 style per object
        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_CPU_NoMerge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_CPU_Merge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = true,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_GPU_NoMerge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_GPU_Merge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = true,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },
        
        // 32 styles per object
        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_CPU_NoMerge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 32,
            K = 32,
            stylesPerObject = 32,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_CPU_Merge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 32,
            K = 32,
            stylesPerObject = 32,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = true,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_GPU_NoMerge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 32,
            K = 32,
            stylesPerObject = 32,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_GPU_Merge_Obj100",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 32,
            K = 32,
            stylesPerObject = 32,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = true,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            effectMode = TestEffectAssignmentMode.Runtime,
        },

        // 1 heavy style per object
        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_CPU_NoMerge_Obj100_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            effectMode = TestEffectAssignmentMode.Runtime,
            testEffect = TestEffect.Heavy,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_CPU_Merge_Obj100_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = true,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.CPU },
            effectMode = TestEffectAssignmentMode.Runtime,
            testEffect = TestEffect.Heavy,
        },

        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_GPU_NoMerge_Obj100_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = false,
            useOcclusion = false,
            renderModes = new[] { NprRenderMode.GPU },
            effectMode = TestEffectAssignmentMode.Runtime,
            testEffect = TestEffect.Heavy,
        },
        
        new NprTestCase
        {
            name = "OverlapScaling_SameStyle_GPU_Merge_Obj100_Heavy",
            scene = "TestScene_Spawner",
            variable = TestVariable.Overlap,
            values = new[] { 0, 20, 40, 60, 80, 100 },
            N = 1,
            K = 1,
            stylesPerObject = 1,
            objectCount = 100,
            coverageFraction = 1.0f,
            overlapFraction = 0.0f,
            stylePattern = StylePattern.SameStyle,
            useMerging = true,
            useOcclusion = false,
            gpuMergeMethod = GpuMergeMethod.BucketedUnion,
            renderModes = new[] { NprRenderMode.GPU },
            effectMode = TestEffectAssignmentMode.Runtime,
            testEffect = TestEffect.Heavy,
        }
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

        int count = Mathf.Clamp(stylesPerObject, 1, n.TestEffectCount);

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

                NprRenderMode[] renderModes = test.renderModes != null && test.renderModes.Length > 0
                    ? test.renderModes
                    : new[] { NprRenderMode.Fullscreen, NprRenderMode.CPU, NprRenderMode.GPU };

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
                            yield return RegenerateSpawnedScene(test.objectCount, v / 100.0f, test.overlapFraction);
                            break;

                        case TestVariable.Overlap:
                            yield return RegenerateSpawnedScene(test.objectCount, test.coverageFraction, v / 100.0f);
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