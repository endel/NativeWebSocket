using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using NativeWebSocket;

public class BurstBench
{
    public IEnumerator Run(string serverUrl, int burstCount, BenchmarkLogger logger)
    {
        int payloadSize = 256;
        var ws = new WebSocket(serverUrl);
        bool connected = false;
        bool closed = false;
        int received = 0;
        bool burstDone = false;
        var frameReceiveCounts = new List<int>();
        var frameTimesMs = new List<double>();

        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) =>
        {
            // Check for burst_done signal
            if (data.Length < 20)
            {
                string msg = Encoding.UTF8.GetString(data);
                if (msg == "burst_done")
                {
                    burstDone = true;
                    return;
                }
            }
            received++;
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

        // Warmup
        yield return new WaitForSeconds(0.5f);
        ws.DispatchMessageQueue();

        // Request burst
        received = 0;
        burstDone = false;
        var sw = Stopwatch.StartNew();
        _ = ws.SendText(string.Format("burst:{0}:{1}", burstCount, payloadSize));

        var frameSw = new Stopwatch();

        while (!burstDone && sw.Elapsed.TotalSeconds < 10)
        {
            int beforeCount = received;

            frameSw.Restart();
            ws.DispatchMessageQueue();
            frameSw.Stop();

            int thisFrame = received - beforeCount;
            if (thisFrame > 0)
            {
                frameReceiveCounts.Add(thisFrame);
                frameTimesMs.Add(frameSw.Elapsed.TotalMilliseconds);
            }

            yield return null;
        }

        sw.Stop();

        double totalTimeMs = sw.Elapsed.TotalMilliseconds;
        int peakPerFrame = 0;
        double peakFrameTime = 0;
        for (int i = 0; i < frameReceiveCounts.Count; i++)
        {
            if (frameReceiveCounts[i] > peakPerFrame) peakPerFrame = frameReceiveCounts[i];
        }
        for (int i = 0; i < frameTimesMs.Count; i++)
        {
            if (frameTimesMs[i] > peakFrameTime) peakFrameTime = frameTimesMs[i];
        }

        logger.LogResult("Burst", payloadSize, "total_time_ms", totalTimeMs);
        logger.LogResult("Burst", payloadSize, "messages_received", received);
        logger.LogResult("Burst", payloadSize, "peak_per_frame", peakPerFrame);
        logger.LogResult("Burst", payloadSize, "peak_frame_time_ms", peakFrameTime);
        logger.LogResult("Burst", payloadSize, "frames_to_process", frameReceiveCounts.Count);

        if (frameTimesMs.Count > 0)
        {
            var ftStats = new BenchmarkStats(frameTimesMs);
            logger.LogResult("Burst", payloadSize, "avg_frame_time_ms", ftStats.Average);
            logger.LogResult("Burst", payloadSize, "p99_frame_time_ms", ftStats.P99);
        }

        UnityEngine.Debug.Log(string.Format(
            "Burst: {0}/{1} msgs in {2:F1}ms, peak {3}/frame, peak frame {4:F3}ms",
            received, burstCount, totalTimeMs, peakPerFrame, peakFrameTime));

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
}
