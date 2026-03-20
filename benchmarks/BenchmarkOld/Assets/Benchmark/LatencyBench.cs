using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using NativeWebSocket;

public class LatencyBench
{
    public IEnumerator Run(string serverUrl, int iterations, int payloadSize, BenchmarkLogger logger)
    {
        var samples = new List<double>(iterations);
        var ws = new WebSocket(serverUrl);
        var sw = new Stopwatch();

        bool connected = false;
        bool closed = false;
        bool responseReceived = false;

        ws.OnOpen += () => { connected = true; };
        ws.OnMessage += (data) =>
        {
            if (data.Length >= 8)
            {
                long sentTicks = BitConverter.ToInt64(data, 0);
                double rttMs = (sw.ElapsedTicks - sentTicks) * 1000.0 / Stopwatch.Frequency;
                samples.Add(rttMs);
            }
            responseReceived = true;
        };
        ws.OnError += (err) => { UnityEngine.Debug.LogError("LatencyBench error: " + err); };
        ws.OnClose += (code) => { closed = true; };

        _ = ws.Connect();

        float timeout = 0;
        while (!connected && !closed && timeout < 10f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!connected)
        {
            UnityEngine.Debug.LogError("LatencyBench: Failed to connect");
            yield break;
        }

        sw.Start();

        byte[] payload = new byte[Math.Max(payloadSize, 8)];

        for (int i = 0; i < iterations; i++)
        {
            responseReceived = false;

            // Embed current ticks in first 8 bytes
            Buffer.BlockCopy(BitConverter.GetBytes(sw.ElapsedTicks), 0, payload, 0, 8);
            _ = ws.Send(payload);

            timeout = 0;
            while (!responseReceived && timeout < 5f)
            {
                ws.DispatchMessageQueue();
                timeout += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!responseReceived)
            {
                UnityEngine.Debug.LogWarning(string.Format("LatencyBench: Timeout on iteration {0}", i));
            }
        }

        sw.Stop();

        if (samples.Count > 0)
        {
            var stats = new BenchmarkStats(samples);
            logger.LogResult("Latency", payloadSize, "min_ms", stats.Min);
            logger.LogResult("Latency", payloadSize, "avg_ms", stats.Average);
            logger.LogResult("Latency", payloadSize, "median_ms", stats.Median);
            logger.LogResult("Latency", payloadSize, "p95_ms", stats.P95);
            logger.LogResult("Latency", payloadSize, "p99_ms", stats.P99);
            logger.LogResult("Latency", payloadSize, "max_ms", stats.Max);
            logger.LogResult("Latency", payloadSize, "stddev_ms", stats.StdDev);
            logger.LogSummary("Latency", payloadSize, stats);
        }

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
