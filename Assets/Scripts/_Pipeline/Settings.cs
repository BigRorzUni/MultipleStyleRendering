using UnityEngine;

[System.Serializable]
public class Settings
{
    public NprDebugView debugView = NprDebugView.None;

    [Header("Outlines")]
    public OutlinesSettings outlines = new OutlinesSettings();

}

[System.Serializable]
public class OutlinesSettings
{
    public Color colour = Color.black;
    public float thickness = 1f;

    public float depthThreshold = 0.02f;
    public float depthStrength = 1.0f;
    public float normalThreshold = 0.2f;
    public float normalStrength = 1.0f;
}

