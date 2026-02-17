using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using System.IO;

public class TestRunner : MonoBehaviour
{
    [SerializeField] public string[] testScenes; 

    [SerializeField] private int startupFrames = 100;
    [SerializeField] private int framesToCapture = 500;

    private bool testFlags = false;
    private bool testing = false;

    private string logDir = null;

    private ProfilerRecorder cpuFrameRec;
    private ProfilerRecorder gpuFrameRec;

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
                    break;

                default:
                    break;
            }
        }
            

        testFlags = true;

        // turn off vsync for testin
        QualitySettings.vSyncCount = 0;

        // uncap framerate
        Application.targetFrameRate = -1;

        // initialise log dir path
        string appPath = Application.dataPath;
        string buildFolder = Directory.GetParent(appPath).Parent.Parent.FullName;

        logDir = Path.Combine(buildFolder, "ProfilingLogs");
    }

    private IEnumerator RunAllTests()
    {
        foreach(string sceneName in testScenes)
        {
            // run profiling here
            Debug.Log($"Finding Scene {sceneName}\n");

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.LogError($" Could not load scene, is '{sceneName}' in build settings?");
                yield break;
            }

            while (!op.isDone) yield return null; // wait until a scene is loaded before continuing

            Debug.Log($"Loaded scene: {sceneName}");

            // run startup frames - skip them
            Debug.Log("Warmup frames...");
            for (int i = 0; i < startupFrames; i++)
                yield return null;

            // run 500 frames
            //Profiler.BeginSample($"******SCENE {sceneName}******"); // may use later

            double[] cpuTimings = new double[framesToCapture];
            double[] gpuTimings = new double[framesToCapture];

            Debug.Log("Capturing frames...");
            for(int i = 0; i < framesToCapture; i++)
            {   
                yield return null;
                
                double cpuMs = cpuFrameRec.LastValue / 1_000_000.0;
                double gpuMs = gpuFrameRec.LastValue / 1_000_000.0;

                cpuTimings[i] = cpuMs;
                gpuTimings[i] = gpuMs;

                // store to array
                //Debug.Log($"Frame {i}: CPU {cpuMs:F3} ms, GPU {gpuMs:F3} ms");
            }

            string path = Path.Combine(logDir, $"{sceneName}.csv");

            using StreamWriter sw = new StreamWriter(path, false);
            sw.WriteLine("frame num, cpu frame (ms), gpu frame (ms)");
            for(int i = 0; i < cpuTimings.Length; i++)
            {
                sw.Write(i.ToString());
                sw.Write(",");
                sw.Write(cpuTimings[i].ToString());
                sw.Write(",");
                sw.Write(gpuTimings[i].ToString());
                sw.WriteLine();
            }

            Debug.Log($"Timings saved at {path}");


            //Profiler.EndSample();

            // reload scene and run again for averaging??
        }

        // send arrays to csv

        Debug.Log("Testing done!");
        Application.Quit();
    }


    private void Start()
    {
        if(testing || !testFlags)
            return;

        // output all profiling markers
        // foreach (ProfilerCategory cat in Enum.GetValues(typeof(ProfilerCategory)))
        // {
        //     var handles = new System.Collections.Generic.List<ProfilerRecorderHandle>();
        //     ProfilerRecorderHandle.GetAvailable(handles);
        //     foreach (var h in handles)
        //     {
        //         var d = ProfilerRecorderHandle.GetDescription(h);
        //         Debug.Log($"{d.Name}");
        //     }
        // }

        // var handles = new List<ProfilerRecorderHandle>();
        // ProfilerRecorderHandle.GetAvailable(handles);
        // foreach (var h in handles)
        // {
        //     var name = ProfilerRecorderHandle.GetDescription(h).Name;
        //     if (name.Contains("Frame") || name.Contains("GPU") || name.Contains("CPU"))
        //         Debug.Log(name);
        // }

        testing = true;
        
        Directory.CreateDirectory(logDir);
        Debug.Log($"Starting tests. logDir = {logDir}, startupFrames = {startupFrames}, frames = {framesToCapture}");


        cpuFrameRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Total Frame Time");
        gpuFrameRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");

        if(!cpuFrameRec.Valid)
        {
            Debug.LogError("CPU recorder is not valid");
            return;
        }

        if(!gpuFrameRec.Valid)
        {
            Debug.LogError("GPU recorder is not valid?");
            return;
        }

        StartCoroutine(RunAllTests());
    }
}