# NativeWebSocket Benchmarks

Compares the **old** (master branch) vs **new** (universal branch) WebSocket implementations across latency, throughput, GC allocations, frame time impact, and burst handling.

## Quick Start

### 1. Start the benchmark server

```bash
cd benchmarks/server
npm install
node index.js
```

Server listens on `ws://localhost:3000`. Set `PORT` env var to change.

### 1a. Run the console benchmark harness

```bash
cd benchmarks/ConsoleRunner
dotnet run -c Release
```

This runs the old vs new comparison without opening Unity and is useful for quick iteration on the benchmark harness.

### 1c. Aggregate and compare multiple CSV runs

```bash
python3 benchmarks/compare_results.py \
  /path/to/benchmark_old_sync.csv \
  /path/to/benchmark_old_sync_2.csv \
  /path/to/benchmark_new_sync.csv \
  /path/to/benchmark_new_sync_2.csv \
  --baseline old \
  --candidate new
```

Helpful filters:
- `--benchmarks Throughput_Recv FrameTime_Dispatch Burst`
- `--metrics msgs_per_sec avg_ms p95_ms p99_ms total_time_ms`
- `--payloads 32 256 1024 8192 65536`

### 1b. Run targeted Unity sync-context benchmarks in batchmode

The benchmark assets now include `BenchmarkBatchRunner.RunSyncContextBenchmarks`, which creates an empty scene, runs receive throughput + frame time + burst tests, writes a CSV, and exits Unity in batchmode.

Useful command-line args:
- `-benchmarkImplementation old|new`
- `-benchmarkOutput benchmark_results.csv`
- `-benchmarkThroughputDuration 5`
- `-benchmarkPayloadSizes 32,256,1024,8192,65536`

### 2. Run BenchmarkOld (master implementation)

1. Create a new Unity project (2019.4+)
2. Copy `BenchmarkOld/Assets/` contents into your project's `Assets/` folder
3. Create an empty scene and add an empty GameObject with the `BenchmarkRunner` component
4. Configure parameters in the Inspector (defaults are reasonable)
5. Ensure `implementationName` is set to `"old"`
6. Press Play

### 3. Run BenchmarkNew (universal implementation)

1. Create a new Unity project (2019.4+)
2. Copy `BenchmarkNew/Assets/` contents into your project's `Assets/` folder
3. Create an empty scene and add an empty GameObject with the `BenchmarkRunner` component
4. Ensure `implementationName` is set to `"new"`
5. Press Play

### 4. Compare results

CSV files are written to `Application.persistentDataPath/benchmark_results.csv`. The path is logged to the Unity Console at startup.

CSV columns: `timestamp, implementation, benchmark, metric, value, payload_size`

## What's Measured

| Benchmark | What | Key Metrics |
|-----------|------|-------------|
| **Latency** | Round-trip time (send → echo → receive) | min/avg/median/p95/p99/max/stddev (ms) |
| **Throughput_Send** | Messages sent per second | msgs/sec, MB/sec |
| **Throughput_Recv** | Messages received per second (server floods) | msgs/sec, MB/sec |
| **Throughput_Bidir** | Simultaneous send + receive | send & recv msgs/sec |
| **GCAlloc_Send** | Heap allocation per Send() call | bytes/op, GC collections |
| **GCAlloc_Dispatch** | Heap allocation per DispatchMessageQueue() | bytes/op, GC collections |
| **FrameTime_Baseline** | Dispatch overhead with no traffic | avg/p99/max/jitter (ms) |
| **FrameTime_Dispatch** | Dispatch overhead under 50k-message flood | avg/median/p95/p99/max/jitter (ms) |
| **FrameTime_UnderLoad** | Total frame time during flood | avg/p99/max/jitter (ms) |
| **Burst** | Process 100 messages sent in <10ms | total time, peak/frame, peak frame time |

Each benchmark runs across payload sizes: 32B, 256B, 1KB, 8KB, 64KB (configurable).

The Unity `BenchmarkRunner` can also selectively enable send / receive / bidirectional throughput runs through:
- `runThroughputSend`
- `runThroughputReceive`
- `runThroughputBidirectional`

## Expected Differences (universal vs master)

| Area | Old (master) | New (universal) | Expected Impact |
|------|-------------|-----------------|-----------------|
| **Receive path** | Always creates MemoryStream per message | Single-frame fast path (avoids MemoryStream) | Lower GC alloc per receive |
| **Dispatch** | Copies list (`new List<>(m_MessageList)`) | Swaps two lists (zero-copy) | Lower dispatch alloc + faster |
| **Send** | `Monitor.TryEnter` + `.Wait()` (blocking) | Fully async with `ConfigureAwait(false)` | Less frame time jitter |
| **Event dispatch** | Manual `DispatchMessageQueue()` required | Auto via `SynchronizationContext` | Dispatch is a no-op in Unity |
| **OnOpen threading** | Fires directly from Connect (thread-unsafe) | Fires via SyncContext (thread-safe) | No functional impact on benchmark |

## Server Commands

The benchmark server responds to:
- **Binary messages**: Echoed back immediately
- `flood:<count>:<size>`: Send `count` binary messages of `size` bytes
- `flood_continuous:<size>`: Continuously flood binary messages of `size` bytes until stopped
- `flood_stop`: Stop a continuous flood and send a `flood_stopped` text signal
- `sink:start:<sessionId>`: Stop echoing binary traffic for that connection (used by send-throughput tests)
- `burst:<count>:<size>`: Send `count` messages + `"burst_done"` text signal
- **Text messages**: Echoed back

## Project Structure

```
benchmarks/
├── server/              # Node.js WebSocket echo/flood server
│   ├── package.json
│   └── index.js
├── BenchmarkOld/        # Old implementation (master branch)
│   └── Assets/
│       ├── WebSocket/
│       │   └── WebSocket.cs         # Monolithic 848-line file from master
│       └── Benchmark/
│           ├── BenchmarkRunner.cs   # Orchestrator MonoBehaviour
│           ├── LatencyBench.cs
│           ├── ThroughputBench.cs
│           ├── GCAllocBench.cs
│           ├── FrameTimeBench.cs
│           ├── BurstBench.cs
│           ├── BenchmarkStats.cs    # Percentile/stats calculator
│           └── BenchmarkLogger.cs   # CSV + console output
├── BenchmarkNew/        # New implementation (universal branch)
│   └── Assets/
│       ├── NativeWebSocket/
│       │   ├── IWebSocket.cs
│       │   ├── WebSocket.cs
│       │   └── WebSocketTypes.cs
│       └── Benchmark/
│           └── ... (identical scripts)
└── README.md
```

The benchmark C# scripts are **identical** in both projects. Only the WebSocket implementation differs.
