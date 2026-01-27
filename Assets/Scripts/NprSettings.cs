using UnityEngine;

[System.Serializable]
public class NprSettings
{
    public Color outlineColor = Color.black;
    public float outlineThickness = 0.03f;

    public bool debugStylisedID;
    public bool debugNormals;
}

public interface INprPass
{
    void ApplySettings(NprSettings settings);
}