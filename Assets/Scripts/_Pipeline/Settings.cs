using UnityEngine;

[System.Serializable]
public class Settings
{
    [Header("Pipeline")]
    public NprRenderMode renderMode = NprRenderMode.CPU;
    public TestEffect currentTestEffect = TestEffect.Dummy;
    public TileSize currentTileSize = TileSize.Size32;

    public bool useMerging;
    public bool useOcclusion;
    public bool testMode;

    public bool debugBBoxes;
    public bool debugID;

}

