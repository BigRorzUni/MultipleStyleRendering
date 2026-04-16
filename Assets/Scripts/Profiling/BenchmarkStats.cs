using System;
using System.Linq;

public static class BenchmarkStats
{
    public static double Mean(double[] values) => values.Average();

    public static double Median(double[] values) => Percentile(values, 50);

    public static double Percentile(double[] values, double percentile)
    {
        if (values == null || values.Length == 0)
            return 0.0;

        double[] sorted = values.OrderBy(x => x).ToArray();

        double pos = (percentile / 100.0) * (sorted.Length - 1);
        int lower = (int)Math.Floor(pos);
        int upper = (int)Math.Ceiling(pos);

        if (lower == upper)
            return sorted[lower];

        double weight = pos - lower;
        return sorted[lower] * (1.0 - weight) + sorted[upper] * weight;
    }

    public static double Max(double[] values) => values.Max();

    public static double StdDev(double[] values)
    {
        if (values == null || values.Length == 0)
            return 0.0;

        double mean = Mean(values);
        double variance = values.Select(v => (v - mean) * (v - mean)).Average();
        return Math.Sqrt(variance);
    }
}