using UnityEngine;

public sealed class ScreenspaceOutlinesEffect : Effect
{
    public override string Name => "Screenspace Outlines";
    public override StyleBits.ImageSpaceEffect RequiredImageBits => StyleBits.ImageSpaceEffect.Outline;

    public float depthThreshold = 0.02f;
    public float depthStrength = 1.0f;
    public float normalThreshold = 0.2f;
    public float normalStrength = 1.0f;
    public Color outlineColour = Color.black;
    public float outlineThickness = 1f;

    private readonly ScreenspaceOutlinesPass _pass;

    public ScreenspaceOutlinesEffect(Shader shader)
    {
        _pass = new ScreenspaceOutlinesPass(shader, RequiredImageBits);
        _passes.Add(_pass);
    }

}