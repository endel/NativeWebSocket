using System.Collections;
using System.IO;
using UnityEngine;

public class BenchmarkRunner : MonoBehaviour
{
    [Header("Server")]
    public string serverUrl = "ws://localhost:3000";

    [Header("Implementation")]
    public string implementationName = "new";

    [Header("Output")]
    public string outputFileName = "benchmark_results.csv";

    [Header("Benchmarks to Run")]
    public bool runLatency = true;
    public bool runThroughput = true;
    public bool runThroughputSend = true;
    public bool runThroughputReceive = true;
    public bool runThroughputBidirectional = true;
    public bool runGCAlloc = true;
    public bool runFrameTime = true;
    public bool runBurst = true;

    [Header("Automation")]
    public bool exitWhenDone = false;

    [Header("Parameters")]
    public int latencyIterations = 1000;
    public int throughputDurationSec = 10;
    public int burstMessageCount = 100;
    public int[] payloadSizes = new int[] { 32, 256, 1024, 8192, 65536 };

    private BenchmarkLogger logger;

    void Start()
    {
        string outputPath = Path.Combine(Application.persistentDataPath, outputFileName);
        logger = new BenchmarkLogger(implementationName, outputPath);
        Debug.Log("Results will be written to: " + outputPath);
        StartCoroutine(RunAllBenchmarks());
    }

    IEnumerator RunAllBenchmarks()
    {
        Debug.Log(string.Format("=== Starting benchmarks for [{0}] ===", implementationName));
        yield return new WaitForSeconds(1f);

        if (runLatency)
        {
            var bench = new LatencyBench();
            foreach (int size in payloadSizes)
            {
                Debug.Log(string.Format("--- Latency benchmark (payload={0}B) ---", size));
                yield return StartCoroutine(bench.Run(serverUrl, latencyIterations, size, logger));
                yield return new WaitForSeconds(1f);
            }
        }

        if (runThroughput)
        {
            var bench = new ThroughputBench();
            foreach (int size in payloadSizes)
            {
                Debug.Log(string.Format("--- Throughput benchmark (payload={0}B) ---", size));
                yield return StartCoroutine(bench.Run(
                    serverUrl,
                    throughputDurationSec,
                    size,
                    logger,
                    runThroughputSend,
                    runThroughputReceive,
                    runThroughputBidirectional));
                yield return new WaitForSeconds(1f);
            }
        }

        if (runGCAlloc)
        {
            var bench = new GCAllocBench();
            foreach (int size in payloadSizes)
            {
                Debug.Log(string.Format("--- GC Allocation benchmark (payload={0}B) ---", size));
                yield return StartCoroutine(bench.Run(serverUrl, size, logger));
                yield return new WaitForSeconds(1f);
            }
        }

        if (runFrameTime)
        {
            var bench = new FrameTimeBench();
            Debug.Log("--- Frame Time benchmark ---");
            yield return StartCoroutine(bench.Run(serverUrl, logger));
            yield return new WaitForSeconds(1f);
        }

        if (runBurst)
        {
            var bench = new BurstBench();
            Debug.Log(string.Format("--- Burst benchmark (count={0}) ---", burstMessageCount));
            yield return StartCoroutine(bench.Run(serverUrl, burstMessageCount, logger));
            yield return new WaitForSeconds(1f);
        }

        Debug.Log(string.Format("=== All benchmarks complete for [{0}] ===", implementationName));
        logger.Close();

#if UNITY_EDITOR
        if (exitWhenDone && Application.isBatchMode)
        {
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
        }
#endif
    }
}
