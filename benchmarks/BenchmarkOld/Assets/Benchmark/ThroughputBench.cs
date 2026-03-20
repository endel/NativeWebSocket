using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using NativeWebSocket;

public class ThroughputBench
{
    public IEnumerator Run(string serverUrl, int durationSec, int payloadSize, BenchmarkLogger logger,
        bool runSend = true, bool runReceive = true, bool runBidirectional = true)
    {
        if (runSend)
        {
            yield return RunSendThroughput(serverUrl, durationSec, payloadSize, logger);
            yield return new WaitForSeconds(0.5f);
        }

        if (runReceive)
        {
            yield return RunReceiveThroughput(serverUrl, durationSec, payloadSize, logger);
            yield return new WaitForSeconds(0.5f);
        }

        if (runBidirectional)
        {
            yield return RunBidirectionalThroughput(serverUrl, durationSec, payloadSize, logger);
        }
    }

    private IEnumerator ConnectAndWait(WebSocket ws, System.Action onConnected)
    {
        bool connected = false;
        bool failed = false;
        ws.OnOpen += () => connected = true;
        ws.OnError += (err) => { UnityEngine.Debug.LogError("ThroughputBench error: " + err); failed = true; };

        _ = ws.Connect();

        float timeout = 0;
        while (!connected && !failed && timeout < 10f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }

        if (connected) onConnected();
    }

    private IEnumerator CloseAndWait(WebSocket ws)
    {
        bool closed = false;
        ws.OnClose += (code) => closed = true;
        _ = ws.Close();

        float timeout = 0;
        while (!closed && timeout < 5f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator RunSendThroughput(string serverUrl, int durationSec, int payloadSize, BenchmarkLogger logger)
    {
        var ws = new WebSocket(serverUrl);
        bool connected = false;
        bool closed = false;
        bool sinkReady = false;
        string sinkSessionId = Guid.NewGuid().ToString("N");
        string sinkReadyMessage = string.Format("sink_ready:{0}", sinkSessionId);

        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) =>
        {
            if (IsExactTextMessage(data, sinkReadyMessage))
            {
                sinkReady = true;
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

        _ = ws.SendText(string.Format("sink:start:{0}", sinkSessionId));
        timeout = 0;
        while (!sinkReady && timeout < 5f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!sinkReady)
        {
            UnityEngine.Debug.LogError("ThroughputBench: failed to enter sink mode");
            yield break;
        }

        byte[] payload = new byte[payloadSize];
        new System.Random(42).NextBytes(payload);

        int sent = 0;
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalSeconds < durationSec)
        {
            for (int i = 0; i < 100 && sw.Elapsed.TotalSeconds < durationSec; i++)
            {
                _ = ws.Send(payload);
                sent++;
            }
            ws.DispatchMessageQueue();
            yield return null;
        }

        sw.Stop();
        double sendRate = sent / sw.Elapsed.TotalSeconds;
        double mbPerSec = (sent * (double)payloadSize) / (1024 * 1024) / sw.Elapsed.TotalSeconds;

        logger.LogResult("Throughput_Send", payloadSize, "msgs_per_sec", sendRate);
        logger.LogResult("Throughput_Send", payloadSize, "total_sent", sent);
        logger.LogResult("Throughput_Send", payloadSize, "MB_per_sec", mbPerSec);
        UnityEngine.Debug.Log(string.Format("Send throughput: {0:F0} msgs/sec ({1} total in {2:F1}s)",
            sendRate, sent, sw.Elapsed.TotalSeconds));

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

    private IEnumerator RunReceiveThroughput(string serverUrl, int durationSec, int payloadSize, BenchmarkLogger logger)
    {
        var ws = new WebSocket(serverUrl);
        bool connected = false;
        bool closed = false;
        int floodStopSignals = 0;
        int received = 0;
        int totalReceived = 0;
        bool measuring = false;

        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) =>
        {
            if (IsExactTextMessage(data, "flood_stopped"))
            {
                floodStopSignals++;
                return;
            }

            totalReceived++;
            if (measuring)
            {
                received++;
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

        _ = ws.SendText(string.Format("flood_continuous:{0}", payloadSize));

        float warmup = 0f;
        while (warmup < 0.2f)
        {
            ws.DispatchMessageQueue();
            warmup += Time.unscaledDeltaTime;
            yield return null;
        }

        _ = ws.SendText("flood_stop");
        timeout = 0;
        while (floodStopSignals < 1 && timeout < 1f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return DrainIncomingMessages(ws, () => totalReceived, 0.25f);

        received = 0;
        measuring = true;
        _ = ws.SendText(string.Format("flood_continuous:{0}", payloadSize));

        var sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalSeconds < durationSec)
        {
            ws.DispatchMessageQueue();
            yield return null;
        }

        sw.Stop();
        measuring = false;

        _ = ws.SendText("flood_stop");
        timeout = 0;
        while (floodStopSignals < 2 && timeout < 1f)
        {
            ws.DispatchMessageQueue();
            timeout += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return DrainIncomingMessages(ws, () => totalReceived, 0.25f);

        double recvRate = received / sw.Elapsed.TotalSeconds;
        double mbPerSec = (received * (double)payloadSize) / (1024 * 1024) / sw.Elapsed.TotalSeconds;

        logger.LogResult("Throughput_Recv", payloadSize, "msgs_per_sec", recvRate);
        logger.LogResult("Throughput_Recv", payloadSize, "total_received", received);
        logger.LogResult("Throughput_Recv", payloadSize, "MB_per_sec", mbPerSec);
        UnityEngine.Debug.Log(string.Format("Receive throughput: {0:F0} msgs/sec ({1} total in {2:F1}s)",
            recvRate, received, sw.Elapsed.TotalSeconds));

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

    private IEnumerator RunBidirectionalThroughput(string serverUrl, int durationSec, int payloadSize, BenchmarkLogger logger)
    {
        var ws = new WebSocket(serverUrl);
        bool connected = false;
        bool closed = false;
        int sent = 0;
        int received = 0;

        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) => received++;
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

        byte[] payload = new byte[payloadSize];
        new System.Random(42).NextBytes(payload);

        var sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalSeconds < durationSec)
        {
            // Send batch — echoes come back as receives
            for (int i = 0; i < 50 && sw.Elapsed.TotalSeconds < durationSec; i++)
            {
                _ = ws.Send(payload);
                sent++;
            }
            ws.DispatchMessageQueue();
            yield return null;
        }

        sw.Stop();
        double sendRate = sent / sw.Elapsed.TotalSeconds;
        double recvRate = received / sw.Elapsed.TotalSeconds;

        logger.LogResult("Throughput_Bidir", payloadSize, "send_msgs_per_sec", sendRate);
        logger.LogResult("Throughput_Bidir", payloadSize, "recv_msgs_per_sec", recvRate);
        UnityEngine.Debug.Log(string.Format("Bidirectional: send={0:F0} recv={1:F0} msgs/sec",
            sendRate, recvRate));

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

    private IEnumerator DrainIncomingMessages(WebSocket ws, Func<int> getCount, float quietPeriodSec)
    {
        int lastCount = getCount();
        float quietTime = 0f;

        while (quietTime < quietPeriodSec)
        {
            ws.DispatchMessageQueue();

            int currentCount = getCount();
            if (currentCount != lastCount)
            {
                lastCount = currentCount;
                quietTime = 0f;
            }
            else
            {
                quietTime += Time.unscaledDeltaTime;
            }

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
