using UnityEngine;

public class BBoxDebugOverlay : MonoBehaviour
{
    public bool drawLabels = true;
    public float thickness = 2f;

    Texture2D _whiteTex;

    void Awake()
    {
        _whiteTex = Texture2D.whiteTexture;
    }

    void OnGUI()
    {
        var rects = BBoxDebugStore.Rects;
        if (rects == null || rects.Count == 0)
            return;

        foreach (var entry in rects)
        {
            DrawRectOutline(entry.rect, entry.color, thickness);

            if (drawLabels && !string.IsNullOrEmpty(entry.label))
            {
                GUI.color = entry.color;
                GUI.Label(new Rect(entry.rect.xMin + 4, Screen.height - entry.rect.yMax + 4, 300, 20), entry.label);
            }
        }

        if(NprTestingConfig.UseOcclusionCulling)
        {
            var occluded = BBoxOcclusionDebugStore.Rects;
            if (occluded != null && occluded.Count > 0)
            {
                foreach (var entry in occluded)
                {
                    DrawRectOutline(entry.rect, Color.red, thickness);

                    if (drawLabels && !string.IsNullOrEmpty(entry.label))
                    {
                        GUI.color = Color.red;
                        GUI.Label(new Rect(entry.rect.xMin + 4, Screen.height - entry.rect.yMax + 4, 300, 20), entry.label);
                    }
                }
            }

        }

        GUI.color = Color.white;
    }

    void DrawRectOutline(RectInt rect, Color color, float t)
    {
        // convert from bottom-left screen coords to OnGUI top-left coords
        Rect guiRect = new Rect(
            rect.xMin,
            Screen.height - rect.yMax,
            rect.width,
            rect.height
        );

        Color old = GUI.color;
        GUI.color = color;

        // top
        GUI.DrawTexture(new Rect(guiRect.xMin, guiRect.yMin, guiRect.width, t), _whiteTex);
        // bottom
        GUI.DrawTexture(new Rect(guiRect.xMin, guiRect.yMax - t, guiRect.width, t), _whiteTex);
        // left
        GUI.DrawTexture(new Rect(guiRect.xMin, guiRect.yMin, t, guiRect.height), _whiteTex);
        // right
        GUI.DrawTexture(new Rect(guiRect.xMax - t, guiRect.yMin, t, guiRect.height), _whiteTex);

        GUI.color = old;
    }
}