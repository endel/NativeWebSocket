using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using NativeWebSocket;

public class FrameTimeBench
{
    public IEnumerator Run(string serverUrl, BenchmarkLogger logger)
    {
        var ws = new WebSocket(serverUrl);
        bool connected = false;
        bool closed = false;
        bool floodStopped = false;

        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) =>
        {
            if (IsExactTextMessage(data, "flood_stopped"))
            {
                floodStopped = true;
            }
        };
        ws.OnClose += (code) => closed = true;

        _ = ws.Connect();

        float timeout = 0;
        while (!connected && !closed && timeout < 10f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!connected) yield break;

        int payloadSize = 256;
        var sw = new Stopwatch();

        // --- Baseline: dispatch time with no pending messages ---
        var baselineTimes = new List<double>();
        for (int i = 0; i < 300; i++)
        {
            sw.Restart();
            ws.DispatchMessageQueue();
            sw.Stop();
            baselineTimes.Add(sw.Elapsed.TotalMilliseconds);
            yield return null;
        }

        // --- Under load: sustained flood + measure dispatch time ---
        _ = ws.SendText(string.Format("flood_continuous:{0}", payloadSize));

        float warmup = 0f;
        while (warmup < 0.2f)
        {
            ws.DispatchMessageQueue();
            warmup += Time.unscaledDeltaTime;
            yield return null;
        }

        var dispatchTimes = new List<double>();
        var frameTimes = new List<double>();
        var frameSw = new Stopwatch();

        for (int i = 0; i < 600; i++)
        {
            frameSw.Restart();

            sw.Restart();
            ws.DispatchMessageQueue();
            sw.Stop();
            dispatchTimes.Add(sw.Elapsed.TotalMilliseconds);

            frameSw.Stop();
            frameTimes.Add(frameSw.Elapsed.TotalMilliseconds);
            yield return null;
        }

        _ = ws.SendText("flood_stop");
        timeout = 0;
        while (!floodStopped && timeout < 1f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }

        // Log baseline
        var baselineStats = new BenchmarkStats(baselineTimes);
        logger.LogResult("FrameTime_Baseline", 0, "avg_ms", baselineStats.Average);
        logger.LogResult("FrameTime_Baseline", 0, "p99_ms", baselineStats.P99);
        logger.LogResult("FrameTime_Baseline", 0, "max_ms", baselineStats.Max);
        logger.LogResult("FrameTime_Baseline", 0, "jitter_stddev_ms", baselineStats.StdDev);

        // Log dispatch under load
        var dispatchStats = new BenchmarkStats(dispatchTimes);
        logger.LogResult("FrameTime_Dispatch", payloadSize, "avg_ms", dispatchStats.Average);
        logger.LogResult("FrameTime_Dispatch", payloadSize, "median_ms", dispatchStats.Median);
        logger.LogResult("FrameTime_Dispatch", payloadSize, "p95_ms", dispatchStats.P95);
        logger.LogResult("FrameTime_Dispatch", payloadSize, "p99_ms", dispatchStats.P99);
        logger.LogResult("FrameTime_Dispatch", payloadSize, "max_ms", dispatchStats.Max);
        logger.LogResult("FrameTime_Dispatch", payloadSize, "jitter_stddev_ms", dispatchStats.StdDev);
        logger.LogSummary("FrameTime_Dispatch", payloadSize, dispatchStats);

        // Log total frame time under load
        var frameStats = new BenchmarkStats(frameTimes);
        logger.LogResult("FrameTime_UnderLoad", payloadSize, "avg_ms", frameStats.Average);
        logger.LogResult("FrameTime_UnderLoad", payloadSize, "p99_ms", frameStats.P99);
        logger.LogResult("FrameTime_UnderLoad", payloadSize, "max_ms", frameStats.Max);
        logger.LogResult("FrameTime_UnderLoad", payloadSize, "jitter_stddev_ms", frameStats.StdDev);

        UnityEngine.Debug.Log(string.Format(
            "Frame time: baseline avg={0:F3}ms, dispatch under load avg={1:F3}ms p99={2:F3}ms",
            baselineStats.Average, dispatchStats.Average, dispatchStats.P99));

        closed = false;
        _ = ws.Close();
        timeout = 0;
        while (!closed && timeout < 5f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static bool IsExactTextMessage(byte[] data, string expected)
    {
        if (data == null || expected == null)
        {
            return false;
        }

        if (data.Length != Encoding.UTF8.GetByteCount(expected))
        {
            return false;
        }

        try
        {
            return Encoding.UTF8.GetString(data) == expected;
        }
        catch
        {
            return false;
        }
    }
}
