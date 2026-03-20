using System;
using System.IO;
using UnityEngine;

public class BenchmarkLogger
{
    private string implementation;
    private StreamWriter csvWriter;

    public BenchmarkLogger(string implementation, string outputPath)
    {
        this.implementation = implementation;
        csvWriter = new StreamWriter(outputPath, false);
        csvWriter.WriteLine("timestamp,implementation,benchmark,metric,value,payload_size");
    }

    public void LogResult(string benchmark, int payloadSize, string metric, double value)
    {
        string line = string.Format("{0},{1},{2},{3},{4:F6},{5}",
            DateTime.UtcNow.ToString("o"), implementation, benchmark, metric, value, payloadSize);
        csvWriter.WriteLine(line);
        csvWriter.Flush();
        Debug.Log(string.Format("[{0}] {1} (payload={2}B): {3} = {4:F3}",
            implementation, benchmark, payloadSize, metric, value));
    }

    public void LogSummary(string benchmark, int payloadSize, BenchmarkStats stats)
    {
        Debug.Log(string.Format("[{0}] {1} (payload={2}B): {3}",
            implementation, benchmark, payloadSize, stats));
    }

    public void Close()
    {
        if (csvWriter != null)
        {
            csvWriter.Close();
            csvWriter = null;
        }
    }
}
