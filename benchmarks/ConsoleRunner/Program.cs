using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OldWs = NativeWebSocket.Legacy;
using NewWs = NativeWebSocket;

// ─── Common interface for both implementations ─────────────────────

interface IBenchSocket
{
    event Action OnOpen;
    event Action<byte[]> OnMessage;
    event Action<string> OnError;
    event Action OnClose;
    bool IsOpen { get; }
    Task Connect();
    Task Send(byte[] data);
    Task SendText(string message);
    Task Close();
    void DispatchMessageQueue();
}

class OldSocket : IBenchSocket
{
    private readonly OldWs.WebSocket ws;

    public event Action OnOpen;
    public event Action<byte[]> OnMessage;
    public event Action<string> OnError;
    public event Action OnClose;

    public OldSocket(string url)
    {
        ws = new OldWs.WebSocket(url);
        ws.OnOpen += () => OnOpen?.Invoke();
        ws.OnMessage += (d) => OnMessage?.Invoke(d);
        ws.OnError += (e) => OnError?.Invoke(e);
        ws.OnClose += (_) => OnClose?.Invoke();
    }

    public bool IsOpen => ws.State == OldWs.WebSocketState.Open;
    public Task Connect() => ws.Connect();
    public Task Send(byte[] data) => ws.Send(data);
    public Task SendText(string message) => ws.SendText(message);
    public Task Close() => ws.Close();
    public void DispatchMessageQueue() => ws.DispatchMessageQueue();
}

class NewSocket : IBenchSocket
{
    private readonly NewWs.WebSocket ws;

    public event Action OnOpen;
    public event Action<byte[]> OnMessage;
    public event Action<string> OnError;
    public event Action OnClose;

    public NewSocket(string url)
    {
        ws = new NewWs.WebSocket(url);
        ws.OnOpen += () => OnOpen?.Invoke();
        ws.OnMessage += (d) => OnMessage?.Invoke(d);
        ws.OnError += (e) => OnError?.Invoke(e);
        ws.OnClose += (_) => OnClose?.Invoke();
    }

    public bool IsOpen => ws.State == NewWs.WebSocketState.Open;
    public Task Connect() => ws.Connect();
    public Task Send(byte[] data) => ws.Send(data);
    public Task SendText(string message) => ws.SendText(message);
    public Task Close() => ws.Close();
    public void DispatchMessageQueue() => ws.DispatchMessageQueue();
}

// ─── Stats ─────────────────────────────────────────────────────────

class Stats
{
    public double Min, Max, Avg, Median, P95, P99, StdDev;
    public int Count;

    public Stats(List<double> samples)
    {
        Count = samples.Count;
        if (Count == 0) return;
        var s = new List<double>(samples);
        s.Sort();
        Min = s[0]; Max = s[Count - 1];
        double sum = 0;
        for (int i = 0; i < Count; i++) sum += s[i];
        Avg = sum / Count;
        Median = Pct(s, 50);
        P95 = Pct(s, 95);
        P99 = Pct(s, 99);
        double ss = 0;
        for (int i = 0; i < Count; i++) { double d = s[i] - Avg; ss += d * d; }
        StdDev = Math.Sqrt(ss / Count);
    }

    static double Pct(List<double> s, double p)
    {
        double idx = (p / 100.0) * (s.Count - 1);
        int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
        if (lo == hi) return s[lo];
        double f = idx - lo;
        return s[lo] * (1 - f) + s[hi] * f;
    }
}

// ─── Benchmark Runner ──────────────────────────────────────────────

class Program
{
    const string SERVER_URL = "ws://localhost:3000";

    static async Task Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║          NativeWebSocket Benchmark: OLD vs NEW                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Quick connection test
        Console.Write("Testing server connection... ");
        try
        {
            var test = new NewSocket(SERVER_URL);
            bool ok = false;
            test.OnOpen += () => ok = true;
            _ = test.Connect();
            await PollUntil(() => ok, test, 5000);
            if (!ok) { Console.WriteLine("FAILED - is the server running?"); return; }
            _ = test.Close();
            await Task.Delay(200);
            Console.WriteLine("OK");
        }
        catch { Console.WriteLine("FAILED - start server first: cd benchmarks/server && node index.js"); return; }

        Console.WriteLine();

        int[] sizes = { 32, 256, 1024, 8192, 65536 };

        // ── 1. Connection Lifecycle ──
        await RunConnectionBenchmark();

        // ── 2. Latency (RTT) ──
        await RunLatencyBenchmark(sizes);

        // ── 3. Throughput ──
        await RunThroughputBenchmark(sizes);

        // ── 4. GC Allocations ──
        await RunGCBenchmark(sizes);

        // ── 5. Dispatch Timing ──
        await RunDispatchBenchmark();

        // ── 6. Burst Handling ──
        await RunBurstBenchmark();

        Console.WriteLine();
        Console.WriteLine("Done!");
    }

    // ── Connection Lifecycle ───────────────────────────────────────

    static async Task RunConnectionBenchmark()
    {
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  1. CONNECTION LIFECYCLE                                        │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

        int iterations = 20;
        var oldConnect = new List<double>();
        var newConnect = new List<double>();
        var oldClose = new List<double>();
        var newClose = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            oldConnect.Add(await MeasureConnect(url => new OldSocket(url)));
            newConnect.Add(await MeasureConnect(url => new NewSocket(url)));
        }

        var oc = new Stats(oldConnect);
        var nc = new Stats(newConnect);
        Console.WriteLine($"  {"Metric",-20} {"Old avg",10} {"New avg",10} {"Old p99",10} {"New p99",10}");
        Console.WriteLine($"  {"──────────────────",-20} {"──────────",10} {"──────────",10} {"──────────",10} {"──────────",10}");
        Console.WriteLine($"  {"Connect (ms)",-20} {oc.Avg,10:F3} {nc.Avg,10:F3} {oc.P99,10:F3} {nc.P99,10:F3}");
        Console.WriteLine();
    }

    static async Task<double> MeasureConnect(Func<string, IBenchSocket> factory)
    {
        var ws = factory(SERVER_URL);
        bool connected = false;
        bool closed = false;
        ws.OnOpen += () => connected = true;
        ws.OnClose += () => closed = true;

        var sw = Stopwatch.StartNew();
        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        sw.Stop();

        if (connected)
        {
            _ = ws.Close();
            await PollUntil(() => closed, ws, 5000);
        }

        return sw.Elapsed.TotalMilliseconds;
    }

    // ── Latency (RTT) ─────────────────────────────────────────────

    static async Task RunLatencyBenchmark(int[] sizes)
    {
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  2. LATENCY (RTT) — 1000 iterations per payload size            │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
        Console.WriteLine($"  {"Payload",-10} {"Old avg",10} {"New avg",10} {"Old p99",10} {"New p99",10} {"Old max",10} {"New max",10}");
        Console.WriteLine($"  {"────────",-10} {"──────────",10} {"──────────",10} {"──────────",10} {"──────────",10} {"──────────",10} {"──────────",10}");

        foreach (int size in sizes)
        {
            var oldStats = await MeasureLatency(url => new OldSocket(url), 1000, size);
            var newStats = await MeasureLatency(url => new NewSocket(url), 1000, size);
            Console.WriteLine($"  {FormatSize(size),-10} {oldStats.Avg,9:F3}ms {newStats.Avg,9:F3}ms {oldStats.P99,9:F3}ms {newStats.P99,9:F3}ms {oldStats.Max,9:F3}ms {newStats.Max,9:F3}ms");
        }
        Console.WriteLine();
    }

    static async Task<Stats> MeasureLatency(Func<string, IBenchSocket> factory, int iterations, int payloadSize)
    {
        var ws = factory(SERVER_URL);
        var samples = new List<double>(iterations);
        var sw = new Stopwatch();
        bool connected = false, closed = false;
        volatile_bool responseReceived = new volatile_bool();

        ws.OnOpen += () => connected = true;
        ws.OnClose += () => closed = true;
        ws.OnMessage += (data) =>
        {
            if (data.Length >= 8)
            {
                long sent = BitConverter.ToInt64(data, 0);
                double rtt = (sw.ElapsedTicks - sent) * 1000.0 / Stopwatch.Frequency;
                lock (samples) { samples.Add(rtt); }
            }
            responseReceived.Value = true;
        };

        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        if (!connected) return new Stats(samples);

        sw.Start();
        byte[] payload = new byte[Math.Max(payloadSize, 8)];

        for (int i = 0; i < iterations; i++)
        {
            responseReceived.Value = false;
            Buffer.BlockCopy(BitConverter.GetBytes(sw.ElapsedTicks), 0, payload, 0, 8);
            _ = ws.Send(payload);
            await PollUntilTight(() => responseReceived.Value, ws, 5000);
        }
        sw.Stop();

        _ = ws.Close();
        await PollUntil(() => closed, ws, 5000);

        return new Stats(samples);
    }

    // ── Throughput ─────────────────────────────────────────────────

    static async Task RunThroughputBenchmark(int[] sizes)
    {
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  3. THROUGHPUT — 5 seconds per test                             │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

        // Send throughput
        Console.WriteLine("  SEND (msgs/sec):");
        Console.WriteLine($"  {"Payload",-10} {"Old",12} {"New",12} {"Old MB/s",12} {"New MB/s",12}");
        Console.WriteLine($"  {"────────",-10} {"────────────",12} {"────────────",12} {"────────────",12} {"────────────",12}");

        foreach (int size in sizes)
        {
            var (oldMps, oldMBs) = await MeasureSendThroughput(url => new OldSocket(url), 5, size);
            var (newMps, newMBs) = await MeasureSendThroughput(url => new NewSocket(url), 5, size);
            Console.WriteLine($"  {FormatSize(size),-10} {oldMps,12:F0} {newMps,12:F0} {oldMBs,12:F2} {newMBs,12:F2}");
        }
        Console.WriteLine();

        // Receive throughput
        Console.WriteLine("  RECEIVE (msgs/sec):");
        Console.WriteLine($"  {"Payload",-10} {"Old",12} {"New",12} {"Old MB/s",12} {"New MB/s",12}");
        Console.WriteLine($"  {"────────",-10} {"────────────",12} {"────────────",12} {"────────────",12} {"────────────",12}");

        foreach (int size in sizes)
        {
            var (oldMps, oldMBs) = await MeasureRecvThroughput(url => new OldSocket(url), 5, size);
            var (newMps, newMBs) = await MeasureRecvThroughput(url => new NewSocket(url), 5, size);
            Console.WriteLine($"  {FormatSize(size),-10} {oldMps,12:F0} {newMps,12:F0} {oldMBs,12:F2} {newMBs,12:F2}");
        }
        Console.WriteLine();
    }

    static async Task<(double msgsPerSec, double mbPerSec)> MeasureSendThroughput(Func<string, IBenchSocket> factory, int durationSec, int payloadSize)
    {
        var ws = factory(SERVER_URL);
        bool connected = false, closed = false;
        bool sinkReady = false;
        string sessionId = Guid.NewGuid().ToString("N");
        ws.OnOpen += () => connected = true;
        ws.OnClose += () => closed = true;
        ws.OnMessage += (data) =>
        {
            if (!TryParseTextMessage(data, out string message))
            {
                return;
            }

            if (message == $"sink_ready:{sessionId}")
            {
                sinkReady = true;
            }
        };

        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        if (!connected) return (0, 0);

        _ = ws.SendText($"sink:start:{sessionId}");
        await PollUntil(() => sinkReady, ws, 5000);
        if (!sinkReady)
        {
            _ = ws.Close();
            await PollUntil(() => closed, ws, 5000);
            return (0, 0);
        }

        byte[] payload = new byte[payloadSize];
        new Random(42).NextBytes(payload);

        int sent = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < durationSec)
        {
            await ws.Send(payload);
            sent++;
        }
        sw.Stop();

        _ = ws.Close();
        await PollUntil(() => closed, ws, 5000);

        double mps = sent / sw.Elapsed.TotalSeconds;
        double mbps = (sent * (double)payloadSize) / (1024 * 1024) / sw.Elapsed.TotalSeconds;
        return (mps, mbps);
    }

    static async Task<(double msgsPerSec, double mbPerSec)> MeasureRecvThroughput(Func<string, IBenchSocket> factory, int durationSec, int payloadSize)
    {
        var ws = factory(SERVER_URL);
        bool connected = false, closed = false;
        int floodStopSignals = 0;
        int received = 0;
        int totalReceived = 0;
        bool measuring = false;
        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) =>
        {
            if (IsExactTextMessage(data, "flood_stopped"))
            {
                Interlocked.Increment(ref floodStopSignals);
                return;
            }

            Interlocked.Increment(ref totalReceived);

            if (measuring)
            {
                Interlocked.Increment(ref received);
            }
        };
        ws.OnClose += () => closed = true;

        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        if (!connected) return (0, 0);

        _ = ws.SendText($"flood_continuous:{payloadSize}");

        var warmup = Stopwatch.StartNew();
        while (warmup.ElapsedMilliseconds < 200)
        {
            ws.DispatchMessageQueue();
            await Task.Delay(1);
        }

        _ = ws.SendText("flood_stop");
        await PollUntil(() => Volatile.Read(ref floodStopSignals) >= 1, ws, 1000);
        await DrainIncomingMessages(ws, () => Volatile.Read(ref totalReceived), 250);

        Interlocked.Exchange(ref received, 0);
        measuring = true;

        _ = ws.SendText($"flood_continuous:{payloadSize}");

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < durationSec)
        {
            ws.DispatchMessageQueue();
            await Task.Delay(1);
        }
        sw.Stop();
        measuring = false;

        _ = ws.SendText("flood_stop");
        await PollUntil(() => Volatile.Read(ref floodStopSignals) >= 2, ws, 1000);

        await DrainIncomingMessages(ws, () => Volatile.Read(ref totalReceived), 250);

        int finalRecv = Volatile.Read(ref received);
        _ = ws.Close();
        await PollUntil(() => closed, ws, 5000);

        double mps = finalRecv / sw.Elapsed.TotalSeconds;
        double mbps = (finalRecv * (double)payloadSize) / (1024 * 1024) / sw.Elapsed.TotalSeconds;
        return (mps, mbps);
    }

    // ── GC Allocations ────────────────────────────────────────────

    static async Task RunGCBenchmark(int[] sizes)
    {
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  4. GC ALLOCATIONS                                              │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

        Console.WriteLine("  SEND (bytes allocated per Send call):");
        Console.WriteLine($"  {"Payload",-10} {"Old",14} {"New",14} {"Old GC0",10} {"New GC0",10}");
        Console.WriteLine($"  {"────────",-10} {"──────────────",14} {"──────────────",14} {"──────────",10} {"──────────",10}");

        foreach (int size in sizes)
        {
            var (oldBpo, oldGC) = await MeasureSendGC(url => new OldSocket(url), 500, size);
            var (newBpo, newGC) = await MeasureSendGC(url => new NewSocket(url), 500, size);
            Console.WriteLine($"  {FormatSize(size),-10} {oldBpo + " B/op",14} {newBpo + " B/op",14} {oldGC,10} {newGC,10}");
        }
        Console.WriteLine();

        Console.WriteLine("  DISPATCH (bytes allocated per DispatchMessageQueue batch):");
        Console.WriteLine($"  {"Payload",-10} {"Old",14} {"New",14} {"Old msgs",10} {"New msgs",10}");
        Console.WriteLine($"  {"────────",-10} {"──────────────",14} {"──────────────",14} {"──────────",10} {"──────────",10}");

        foreach (int size in new[] { 32, 256, 1024 })
        {
            var (oldBpo, oldN) = await MeasureDispatchGC(url => new OldSocket(url), size);
            var (newBpo, newN) = await MeasureDispatchGC(url => new NewSocket(url), size);
            Console.WriteLine($"  {FormatSize(size),-10} {oldBpo + " B",14} {newBpo + " B",14} {oldN,10} {newN,10}");
        }
        Console.WriteLine();
    }

    static async Task<(string bytesPerOp, int gc0)> MeasureSendGC(Func<string, IBenchSocket> factory, int iterations, int payloadSize)
    {
        var ws = factory(SERVER_URL);
        bool connected = false, closed = false;
        bool sinkReady = false;
        string sessionId = Guid.NewGuid().ToString("N");
        ws.OnOpen += () => connected = true;
        ws.OnClose += () => closed = true;
        ws.OnMessage += (data) =>
        {
            if (TryParseTextMessage(data, out string message) && message == $"sink_ready:{sessionId}")
            {
                sinkReady = true;
            }
        };

        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        if (!connected) return ("N/A", 0);

        _ = ws.SendText($"sink:start:{sessionId}");
        await PollUntil(() => sinkReady, ws, 5000);
        if (!sinkReady)
        {
            _ = ws.Close();
            await PollUntil(() => closed, ws, 5000);
            return ("N/A", 0);
        }

        byte[] payload = new byte[payloadSize];

        // Warmup
        for (int i = 0; i < 50; i++) _ = ws.Send(payload);
        await Task.Delay(500);
        ws.DispatchMessageQueue();

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gc0Before = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < iterations; i++) _ = ws.Send(payload);

        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        int gc0After = GC.CollectionCount(0);

        _ = ws.Close();
        await PollUntil(() => closed, ws, 5000);

        long delta = allocAfter - allocBefore;
        double perOp = (double)delta / iterations;
        return (perOp.ToString("F0"), gc0After - gc0Before);
    }

    static async Task<(string totalBytes, int msgCount)> MeasureDispatchGC(Func<string, IBenchSocket> factory, int payloadSize)
    {
        var ws = factory(SERVER_URL);
        bool connected = false, closed = false;
        int received = 0;
        ws.OnOpen += () => connected = true;
        ws.OnMessage += (_) => received++;
        ws.OnClose += () => closed = true;

        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        if (!connected) return ("N/A", 0);

        int count = 500;
        _ = ws.SendText($"flood:{count}:{payloadSize}");
        await Task.Delay(2000); // let messages queue up

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int beforeCount = received;
        long memBefore = GC.GetTotalMemory(false);

        ws.DispatchMessageQueue();

        long memAfter = GC.GetTotalMemory(false);
        int dispatched = received - beforeCount;

        _ = ws.Close();
        await PollUntil(() => closed, ws, 5000);

        long delta = memAfter - memBefore;
        return (delta.ToString("N0"), dispatched);
    }

    // ── Dispatch Timing ───────────────────────────────────────────

    static async Task RunDispatchBenchmark()
    {
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  5. DISPATCH TIMING (DispatchMessageQueue under load)           │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

        var oldStats = await MeasureDispatchTime(url => new OldSocket(url));
        var newStats = await MeasureDispatchTime(url => new NewSocket(url));

        Console.WriteLine($"  {"Metric",-20} {"Old",12} {"New",12}");
        Console.WriteLine($"  {"──────────────────",-20} {"────────────",12} {"────────────",12}");
        Console.WriteLine($"  {"Avg (ms)",-20} {oldStats.Avg,11:F4}ms {newStats.Avg,11:F4}ms");
        Console.WriteLine($"  {"Median (ms)",-20} {oldStats.Median,11:F4}ms {newStats.Median,11:F4}ms");
        Console.WriteLine($"  {"P95 (ms)",-20} {oldStats.P95,11:F4}ms {newStats.P95,11:F4}ms");
        Console.WriteLine($"  {"P99 (ms)",-20} {oldStats.P99,11:F4}ms {newStats.P99,11:F4}ms");
        Console.WriteLine($"  {"Max (ms)",-20} {oldStats.Max,11:F4}ms {newStats.Max,11:F4}ms");
        Console.WriteLine($"  {"Jitter/StdDev (ms)",-20} {oldStats.StdDev,11:F4}ms {newStats.StdDev,11:F4}ms");
        Console.WriteLine();
    }

    static async Task<Stats> MeasureDispatchTime(Func<string, IBenchSocket> factory)
    {
        var ws = factory(SERVER_URL);
        bool connected = false, closed = false;
        bool floodStopped = false;
        ws.OnOpen += () => connected = true;
        ws.OnMessage += (data) =>
        {
            if (IsExactTextMessage(data, "flood_stopped"))
            {
                floodStopped = true;
            }
        };
        ws.OnClose += () => closed = true;

        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        if (!connected) return new Stats(new List<double>());

        _ = ws.SendText("flood_continuous:256");
        await Task.Delay(200);

        var times = new List<double>();
        var sw = new Stopwatch();
        var total = Stopwatch.StartNew();

        while (total.Elapsed.TotalSeconds < 5)
        {
            sw.Restart();
            ws.DispatchMessageQueue();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            await Task.Delay(1); // simulate frame-like pacing
        }

        _ = ws.SendText("flood_stop");
        await PollUntil(() => floodStopped, ws, 1000);

        _ = ws.Close();
        await PollUntil(() => closed, ws, 5000);

        return new Stats(times);
    }

    // ── Burst Handling ────────────────────────────────────────────

    static async Task RunBurstBenchmark()
    {
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  6. BURST HANDLING — 100 messages sent in <10ms burst           │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");

        var (oldTime, oldPeak, oldFrames) = await MeasureBurst(url => new OldSocket(url), 100);
        var (newTime, newPeak, newFrames) = await MeasureBurst(url => new NewSocket(url), 100);

        Console.WriteLine($"  {"Metric",-25} {"Old",12} {"New",12}");
        Console.WriteLine($"  {"───────────────────────",-25} {"────────────",12} {"────────────",12}");
        Console.WriteLine($"  {"Total time (ms)",-25} {oldTime,11:F2}ms {newTime,11:F2}ms");
        Console.WriteLine($"  {"Peak msgs/dispatch",-25} {oldPeak,12} {newPeak,12}");
        Console.WriteLine($"  {"Dispatches to complete",-25} {oldFrames,12} {newFrames,12}");
        Console.WriteLine();
    }

    static async Task<(double totalMs, int peakPerDispatch, int dispatches)> MeasureBurst(Func<string, IBenchSocket> factory, int burstCount)
    {
        var ws = factory(SERVER_URL);
        bool connected = false, closed = false;
        int received = 0;
        bool burstDone = false;

        ws.OnOpen += () => connected = true;
        ws.OnClose += () => closed = true;
        ws.OnMessage += (data) =>
        {
            if (data.Length < 20)
            {
                string msg = Encoding.UTF8.GetString(data);
                if (msg == "burst_done") { burstDone = true; return; }
            }
            Interlocked.Increment(ref received);
        };

        _ = ws.Connect();
        await PollUntil(() => connected || closed, ws, 10000);
        if (!connected) return (0, 0, 0);

        await Task.Delay(200);
        ws.DispatchMessageQueue();

        received = 0;
        burstDone = false;
        var sw = Stopwatch.StartNew();
        _ = ws.SendText($"burst:{burstCount}:256");

        int peakPerDispatch = 0;
        int dispatches = 0;

        while (!burstDone && sw.Elapsed.TotalSeconds < 10)
        {
            int before = Volatile.Read(ref received);
            ws.DispatchMessageQueue();
            int after = Volatile.Read(ref received);
            int thisRound = after - before;
            if (thisRound > 0)
            {
                dispatches++;
                if (thisRound > peakPerDispatch) peakPerDispatch = thisRound;
            }
            await Task.Delay(1);
        }
        sw.Stop();

        _ = ws.Close();
        await PollUntil(() => closed, ws, 5000);

        return (sw.Elapsed.TotalMilliseconds, peakPerDispatch, dispatches);
    }

    // ── Helpers ────────────────────────────────────────────────────

    static async Task PollUntil(Func<bool> condition, IBenchSocket ws, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            ws.DispatchMessageQueue();
            await Task.Delay(1);
        }
    }

    static async Task PollUntilTight(Func<bool> condition, IBenchSocket ws, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            ws.DispatchMessageQueue();
            Thread.Yield();
        }
    }

    static async Task DrainIncomingMessages(IBenchSocket ws, Func<int> getCount, int quietPeriodMs)
    {
        int lastCount = getCount();
        var quiet = Stopwatch.StartNew();

        while (quiet.ElapsedMilliseconds < quietPeriodMs)
        {
            ws.DispatchMessageQueue();
            await Task.Delay(1);

            int currentCount = getCount();
            if (currentCount != lastCount)
            {
                lastCount = currentCount;
                quiet.Restart();
            }
        }
    }

    static bool TryParseTextMessage(byte[] data, out string message)
    {
        message = null;

        if (data == null || data.Length == 0)
        {
            return false;
        }

        try
        {
            message = Encoding.UTF8.GetString(data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool IsExactTextMessage(byte[] data, string expected)
    {
        if (data == null || expected == null)
        {
            return false;
        }

        int expectedLength = Encoding.UTF8.GetByteCount(expected);
        if (data.Length != expectedLength)
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

    static string FormatSize(int bytes)
    {
        if (bytes >= 1024) return $"{bytes / 1024}KB";
        return $"{bytes}B";
    }
}

class volatile_bool
{
    private int _val;
    public bool Value
    {
        get => Interlocked.CompareExchange(ref _val, 0, 0) != 0;
        set => Interlocked.Exchange(ref _val, value ? 1 : 0);
    }
}
