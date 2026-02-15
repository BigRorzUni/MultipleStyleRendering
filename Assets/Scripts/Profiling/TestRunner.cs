using System;
using System.Collections;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine;

public class TestRunner : MonoBehaviour
{
    [SerializeField] public string[] testScenes; 

    [SerializeField] private int startupFrames = 100;
    [SerializeField] private int framesToCapture = 500;

    private bool testFlags = false;
    private bool testing = false;

    private void Awake()
    {
        // there can be only one...
        if (FindObjectsByType(typeof(TestRunner), FindObjectsSortMode.None).Length > 1)
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);

        string[] args = Environment.GetCommandLineArgs();

        // is testing enabled?
        if(!args.Contains("-runTests"))
            return;

        for(int i = 0; i < args.Length-1; i++)
        {
            switch(args[i])
            {
                case "-frames":
                    if(int.TryParse(args[i+1], out int frames))
                        framesToCapture = frames;
                    continue;
                
                default:
                    continue;
            }
        }
            

        testFlags = true;

        // turn off vsync for testin
        QualitySettings.vSyncCount = 0;

        // uncap framerate
        Application.targetFrameRate = -1;
    }

    private IEnumerator RunAllTests()
    {
        foreach(string sceneName in testScenes)
        {
            // profiling tag so i can see what test scene is being executed when using XCode for GPU data
            UnityEngine.Profiling.Profiler.BeginSample($"******SCENE {sceneName}******");

            // run profiling here
            Debug.Log($"Finding Scene {sceneName}\n");
            SceneManager.LoadScene(sceneName);
            Debug.Log($"Loaded scene: {sceneName}");

            // run startup frames - skip them
            for (int i = 0; i < startupFrames; i++)
                yield return null;

            // run 500 frames
            for(int i = 0; i < framesToCapture; i++)
                yield return null;

                // measure these frames

                // upload to csv

            UnityEngine.Profiling.Profiler.EndSample();

            // reload scene and run again for averaging??
        }

        Application.Quit();
    }

    private void Start()
    {
        if(testing || !testFlags)
            return;

        testing = true;
        Debug.Log("Starting tests");
        StartCoroutine(RunAllTests());
    }
}