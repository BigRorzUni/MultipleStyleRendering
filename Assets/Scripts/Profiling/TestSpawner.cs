using UnityEngine;
using System.Collections;

public class TestSpawner : MonoBehaviour
{
    [Header("Object to Spawn")]
    public GameObject prefab;

    [Header("Test Style")]
    public int testStyleIndex = 0;

    // temporary IEnumerator until i sort out spawning with cmd argss
    IEnumerator Start()
    {
        Debug.Log("TestSpawner.Start() fired");
        yield return null; // let renderer feature run

        // Debug.Log($"TestModeEnabled = {NprStylesRendererFeature.TestModeEnabled}");

        // if (!NprStylesRendererFeature.TestModeEnabled)
        // {
        //     Debug.Log("Test mode disabled. Not spawning test object.");
        //     yield break;
        // }

        // SpawnObject();
    }   

    void SpawnObject()
    {
        if (prefab == null)
        {
            Debug.LogError("No prefab assigned to TestSpawner.");
            return;
        }

        GameObject obj = Instantiate(prefab, Vector3.zero, Quaternion.identity);

        StylisedTag tag = obj.GetComponent<StylisedTag>();
        if (tag == null)
            tag = obj.AddComponent<StylisedTag>();

        tag.AddTestEffect(2); // rebuild mask + update MPB
        tag.AddTestEffect(1);

        Debug.Log("Spawned test object with style index 1");
    }
}