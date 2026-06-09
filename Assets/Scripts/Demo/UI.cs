using TMPro;
using UnityEngine;

public class UI : MonoBehaviour
{
    public static UI Instance;

    [Header("UI Elements")]
    public TMP_Text modeText;
    public TMP_Text styleText;
    public TMP_Text fpsText;
    public TMP_Text crosshair;

    float fpsTimer;
    int frameCount;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        Debug.Log($"UI active: {Instance != null}");
    }

    void Update()
    {
        UpdateFPS();
    }

    void UpdateFPS()
    {
        frameCount++;
        fpsTimer += Time.unscaledDeltaTime;

        if (fpsTimer >= 0.5f)
        {
            float fps = frameCount / fpsTimer;

            if (fpsText != null)
                fpsText.text = $"FPS: {fps:0}";

            frameCount = 0;
            fpsTimer = 0;
        }
    }

    public void SetMode(string mode)
    {
        if (modeText != null)
            modeText.text = $"Mode: {mode}";
    }

    public void SetStyle(string style)
    {
        if (styleText != null)
            styleText.text = $"Style: {style}";
    }
}