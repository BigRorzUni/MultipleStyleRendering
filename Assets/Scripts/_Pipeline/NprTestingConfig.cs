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
    public static bool IsValidationRunning = false;
    public static bool DebugBBoxes = false;
    public static bool DebugID = false;
}
