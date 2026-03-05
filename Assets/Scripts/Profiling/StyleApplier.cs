using UnityEngine;

public class StyleApplier : MonoBehaviour
{
    [Header("K Active Styles")]
    [Range(1, 32)]
    public int K = 3;

    [Header("Stacking")]
    [Range(1, 32)]
    public int stylesPerObject = 1;

    StylisedTag[] tags;

    void Awake()
    {
        tags = FindObjectsByType<StylisedTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    }

    void Start()
    {
        // Clamp stacking to available styles
        int k = Mathf.Clamp(K, 1, 32);
        int s = Mathf.Clamp(stylesPerObject, 1, k);

        if (tags == null || tags.Length == 0)
        {
            Debug.LogWarning("AssignTestStyles: No StylisedTag found in scene.");
            return;
        }

        for (int objIndex = 0; objIndex < tags.Length; objIndex++)
        {
            Debug.Log("adding styles to object");
            var tag = tags[objIndex];
            if (!tag) continue;

            tag.ClearTestEffects();
            int baseStyle = objIndex % k;

            // Add stacked styles
            for (int t = 0; t < s; t++)
            {
                int style = (baseStyle + t) % k;
                tag.AddTestEffect(style);
            }
        }

        Debug.Log($"AssignTestStyles: Applied K={k}, stylesPerObject={s}, objects={tags.Length}");
    }

}