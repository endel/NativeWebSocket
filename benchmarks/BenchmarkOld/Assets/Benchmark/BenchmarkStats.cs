using System;
using System.Collections.Generic;

public class BenchmarkStats
{
    public double Min { get; private set; }
    public double Max { get; private set; }
    public double Average { get; private set; }
    public double Median { get; private set; }
    public double P95 { get; private set; }
    public double P99 { get; private set; }
    public double StdDev { get; private set; }
    public int Count { get; private set; }

    public BenchmarkStats(List<double> samples)
    {
        Count = samples.Count;
        if (Count == 0) return;

        var sorted = new List<double>(samples);
        sorted.Sort();

        Min = sorted[0];
        Max = sorted[Count - 1];

        double sum = 0;
        for (int i = 0; i < Count; i++) sum += sorted[i];
        Average = sum / Count;

        Median = Percentile(sorted, 50);
        P95 = Percentile(sorted, 95);
        P99 = Percentile(sorted, 99);

        double sumSquares = 0;
        for (int i = 0; i < Count; i++)
        {
            double diff = sorted[i] - Average;
            sumSquares += diff * diff;
        }
        StdDev = Math.Sqrt(sumSquares / Count);
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        double index = (percentile / 100.0) * (sorted.Count - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        double frac = index - lower;
        return sorted[lower] * (1 - frac) + sorted[upper] * frac;
    }

    public override string ToString()
    {
        return string.Format("n={0} min={1:F3} avg={2:F3} med={3:F3} p95={4:F3} p99={5:F3} max={6:F3} stddev={7:F3}",
            Count, Min, Average, Median, P95, P99, Max, StdDev);
    }
}
