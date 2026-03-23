using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

public static class BenchmarkBatchRunner
{
    public static void RunSyncContextBenchmarks()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var gameObject = new GameObject("BenchmarkRunner");
        var runner = gameObject.AddComponent<BenchmarkRunner>();

        runner.serverUrl = GetArg("-benchmarkServer", runner.serverUrl);
        runner.implementationName = GetArg("-benchmarkImplementation", runner.implementationName);
        runner.outputFileName = GetArg("-benchmarkOutput", runner.outputFileName);
        runner.runLatency = false;
        runner.runThroughput = true;
        runner.runThroughputSend = false;
        runner.runThroughputReceive = true;
        runner.runThroughputBidirectional = false;
        runner.runGCAlloc = false;
        runner.runFrameTime = true;
        runner.runBurst = true;
        runner.throughputDurationSec = GetIntArg("-benchmarkThroughputDuration", 5);
        runner.burstMessageCount = GetIntArg("-benchmarkBurstCount", runner.burstMessageCount);
        runner.exitWhenDone = true;

        string payloadSizesArg = GetArg("-benchmarkPayloadSizes", null);
        if (!string.IsNullOrEmpty(payloadSizesArg))
        {
            string[] parts = payloadSizesArg.Split(',');
            int[] payloadSizes = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                payloadSizes[i] = int.Parse(parts[i]);
            }
            runner.payloadSizes = payloadSizes;
        }

        EditorSceneManager.SaveScene(scene, "Assets/BenchmarkBatchScene.unity");
        EditorApplication.EnterPlaymode();
    }

    private static string GetArg(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static int GetIntArg(string name, int fallback)
    {
        string value = GetArg(name, null);
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        int parsed;
        if (int.TryParse(value, out parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
#endif
