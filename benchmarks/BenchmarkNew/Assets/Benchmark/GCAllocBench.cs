using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;

public class GCAllocBench
{
    public IEnumerator Run(string serverUrl, int payloadSize, BenchmarkLogger logger)
    {
        var ws = new WebSocket(serverUrl);
        bool connected = false;
        bool closed = false;
        int receivedCount = 0;

        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) => receivedCount++;
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
        int iterations = 500;

        // --- Warmup ---
        for (int i = 0; i < 50; i++) { _ = ws.Send(payload); }
        yield return null;
        ws.DispatchMessageQueue();
        yield return new WaitForSeconds(0.5f);
        ws.DispatchMessageQueue();

        // --- Measure Send allocations ---
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long memBefore = GC.GetTotalMemory(false);

        for (int i = 0; i < iterations; i++) { _ = ws.Send(payload); }

        long memAfter = GC.GetTotalMemory(false);
        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);

        double sendAllocPerOp = (double)(memAfter - memBefore) / iterations;
        logger.LogResult("GCAlloc_Send", payloadSize, "bytes_per_op", sendAllocPerOp);
        logger.LogResult("GCAlloc_Send", payloadSize, "gen0_collections", gen0After - gen0Before);
        logger.LogResult("GCAlloc_Send", payloadSize, "gen1_collections", gen1After - gen1Before);
        logger.LogResult("GCAlloc_Send", payloadSize, "gen2_collections", gen2After - gen2Before);

        // Wait for echoes to arrive
        yield return new WaitForSeconds(2f);

        // --- Measure Receive/Dispatch allocations ---
        // Ask server to flood, then wait for messages to queue up
        receivedCount = 0;
        _ = ws.SendText(string.Format("flood:{0}:{1}", iterations, payloadSize));
        yield return new WaitForSeconds(2f);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        gen0Before = GC.CollectionCount(0);
        memBefore = GC.GetTotalMemory(false);
        int countBefore = receivedCount;

        // Dispatch queued messages
        ws.DispatchMessageQueue();

        memAfter = GC.GetTotalMemory(false);
        gen0After = GC.CollectionCount(0);
        int dispatched = receivedCount - countBefore;

        if (dispatched > 0)
        {
            double dispatchAllocPerOp = (double)(memAfter - memBefore) / dispatched;
            logger.LogResult("GCAlloc_Dispatch", payloadSize, "bytes_per_op", dispatchAllocPerOp);
            logger.LogResult("GCAlloc_Dispatch", payloadSize, "messages_dispatched", dispatched);
            logger.LogResult("GCAlloc_Dispatch", payloadSize, "gen0_collections", gen0After - gen0Before);
        }

        UnityEngine.Debug.Log(string.Format("GC: send={0:F0} bytes/op, dispatch={1} bytes/op (n={2})",
            sendAllocPerOp,
            dispatched > 0 ? ((double)(memAfter - memBefore) / dispatched).ToString("F0") : "N/A",
            dispatched));

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
