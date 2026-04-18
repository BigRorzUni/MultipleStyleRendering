using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public static class CsvWriter
{
    private static readonly CultureInfo CsvCulture = CultureInfo.InvariantCulture;

    public static void EnsureDirectoryExists(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            Debug.LogError("CsvWriter.EnsureDirectoryExists: directoryPath was null or empty.");
            return;
        }

        Directory.CreateDirectory(directoryPath);
    }

    public static string CombinePath(string directoryPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("directoryPath cannot be null or empty.", nameof(directoryPath));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName cannot be null or empty.", nameof(fileName));

        return Path.Combine(directoryPath, fileName);
    }

    public static void WriteFrameTimings(
        string path,
        double[] cpuTimings,
        double[] gpuTimings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogError("CsvWriter.WriteFrameTimings: path was null or empty.");
            return;
        }

        if (cpuTimings == null)
        {
            Debug.LogError("CsvWriter.WriteFrameTimings: cpuTimings was null.");
            return;
        }

        if (gpuTimings == null)
        {
            Debug.LogError("CsvWriter.WriteFrameTimings: gpuTimings was null.");
            return;
        }

        if (cpuTimings.Length != gpuTimings.Length)
        {
            Debug.LogError($"CsvWriter.WriteFrameTimings: array length mismatch. CPU={cpuTimings.Length}, GPU={gpuTimings.Length}");
            return;
        }

        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8);

        sw.WriteLine("frame,cpu_ms,gpu_ms");

        for (int i = 0; i < cpuTimings.Length; i++)
        {
            sw.Write(i.ToString(CsvCulture));
            sw.Write(",");
            sw.Write(cpuTimings[i].ToString("F6", CsvCulture));
            sw.Write(",");
            sw.Write(gpuTimings[i].ToString("F6", CsvCulture));
            sw.WriteLine();
        }
    }

    public static void AppendSummaryRow(string path, NprTestCase test, int value, NprRenderMode renderMode, int curN, int curK, int curS, double[] cpuTimings, double[] gpuTimings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogError("CsvWriter.AppendSummaryRow: path was null or empty.");
            return;
        }

        if (test == null)
        {
            Debug.LogError("CsvWriter.AppendSummaryRow: test was null.");
            return;
        }

        if (cpuTimings == null || cpuTimings.Length == 0)
        {
            Debug.LogError("CsvWriter.AppendSummaryRow: cpuTimings was null or empty.");
            return;
        }

        if (gpuTimings == null || gpuTimings.Length == 0)
        {
            Debug.LogError("CsvWriter.AppendSummaryRow: gpuTimings was null or empty.");
            return;
        }

        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        bool fileExists = File.Exists(path);

        using StreamWriter sw = new StreamWriter(path, append: true, Encoding.UTF8);

        if (!fileExists)
        {
            sw.WriteLine(
                "test_name,scene,variable,value,render_mode,N,K,styles_per_object," +
                "mean_cpu_ms,median_cpu_ms,p95_cpu_ms,p99_cpu_ms,max_cpu_ms,std_cpu_ms," +
                "mean_gpu_ms,median_gpu_ms,p95_gpu_ms,p99_gpu_ms,max_gpu_ms,std_gpu_ms");
        }

        string[] fields =
        {
            Escape(test.name),
            Escape(test.scene),
            Escape(test.variable.ToString()),
            value.ToString(CsvCulture),
            Escape(renderMode.ToString()),
            curN.ToString(CsvCulture),
            curK.ToString(CsvCulture),
            curS.ToString(CsvCulture),

            BenchmarkStats.Mean(cpuTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Median(cpuTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(cpuTimings, 95).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(cpuTimings, 99).ToString("F6", CsvCulture),
            BenchmarkStats.Max(cpuTimings).ToString("F6", CsvCulture),
            BenchmarkStats.StdDev(cpuTimings).ToString("F6", CsvCulture),

            BenchmarkStats.Mean(gpuTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Median(gpuTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(gpuTimings, 95).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(gpuTimings, 99).ToString("F6", CsvCulture),
            BenchmarkStats.Max(gpuTimings).ToString("F6", CsvCulture),
            BenchmarkStats.StdDev(gpuTimings).ToString("F6", CsvCulture)
        };

        sw.WriteLine(string.Join(",", fields));
    }

    public static void AppendPassSummaryRows(string path, NprTestCase test, int value, NprRenderMode renderMode, int curN, int curK, int curS, Dictionary<string, PassTimingCapture> passCaptures)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogError("CsvWriter.AppendPassSummaryRows: path was null or empty.");
            return;
        }

        if (test == null)
        {
            Debug.LogError("CsvWriter.AppendPassSummaryRows: test was null.");
            return;
        }

        if (passCaptures == null || passCaptures.Count == 0)
        {
            Debug.LogWarning("CsvWriter.AppendPassSummaryRows: no pass captures to write.");
            return;
        }

        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        bool fileExists = File.Exists(path);

        using StreamWriter sw = new StreamWriter(path, append: true, Encoding.UTF8);

        if (!fileExists)
        {
            sw.WriteLine(
                "test_name,scene,variable,value,render_mode,N,K,styles_per_object,pass_name," +
                "mean_cpu_ms,median_cpu_ms,p95_cpu_ms,p99_cpu_ms,max_cpu_ms,std_cpu_ms," +
                "mean_gpu_ms,median_gpu_ms,p95_gpu_ms,p99_gpu_ms,max_gpu_ms,std_gpu_ms");
        }

        foreach (var kvp in passCaptures)
        {
            PassTimingCapture capture = kvp.Value;

            string[] fields =
            {
                Escape(test.name),
                Escape(test.scene),
                Escape(test.variable.ToString()),
                value.ToString(CsvCulture),
                Escape(renderMode.ToString()),
                curN.ToString(CsvCulture),
                curK.ToString(CsvCulture),
                curS.ToString(CsvCulture),
                Escape(capture.passName),

                BenchmarkStats.Mean(capture.cpuMs).ToString("F6", CsvCulture),
                BenchmarkStats.Median(capture.cpuMs).ToString("F6", CsvCulture),
                BenchmarkStats.Percentile(capture.cpuMs, 95).ToString("F6", CsvCulture),
                BenchmarkStats.Percentile(capture.cpuMs, 99).ToString("F6", CsvCulture),
                BenchmarkStats.Max(capture.cpuMs).ToString("F6", CsvCulture),
                BenchmarkStats.StdDev(capture.cpuMs).ToString("F6", CsvCulture),

                BenchmarkStats.Mean(capture.gpuMs).ToString("F6", CsvCulture),
                BenchmarkStats.Median(capture.gpuMs).ToString("F6", CsvCulture),
                BenchmarkStats.Percentile(capture.gpuMs, 95).ToString("F6", CsvCulture),
                BenchmarkStats.Percentile(capture.gpuMs, 99).ToString("F6", CsvCulture),
                BenchmarkStats.Max(capture.gpuMs).ToString("F6", CsvCulture),
                BenchmarkStats.StdDev(capture.gpuMs).ToString("F6", CsvCulture)
            };

            sw.WriteLine(string.Join(",", fields));
        }
    }

    public static string Escape(string value)
    {
        if (value == null)
            return "";

        bool mustQuote =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (!mustQuote)
            return value;

        string escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}