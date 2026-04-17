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
        double[] cpuTotalFrameTimings,
        double[] gpuFrameTimings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogError("CsvWriter.WriteFrameTimings: path was null or empty.");
            return;
        }

        if (cpuTotalFrameTimings == null)
        {
            Debug.LogError("CsvWriter.WriteFrameTimings: cpuTotalFrameTimings was null.");
            return;
        }

        if (gpuFrameTimings == null)
        {
            Debug.LogError("CsvWriter.WriteFrameTimings: gpuFrameTimings was null.");
            return;
        }

        if (cpuTotalFrameTimings.Length != gpuFrameTimings.Length)
        {
            Debug.LogError($"CsvWriter.WriteFrameTimings: array length mismatch. CPU Total Frame={cpuTotalFrameTimings.Length}, GPU Frame={gpuFrameTimings.Length}");
            return;
        }

        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8);

        sw.WriteLine("frame,cpu_total_frame_ms,gpu_frame_ms");

        for (int i = 0; i < cpuTotalFrameTimings.Length; i++)
        {
            sw.Write(i.ToString(CsvCulture));
            sw.Write(",");
            sw.Write(cpuTotalFrameTimings[i].ToString("F6", CsvCulture));
            sw.Write(",");
            sw.Write(gpuFrameTimings[i].ToString("F6", CsvCulture));
            sw.WriteLine();
        }
    }

    public static void AppendSummaryRow(
        string path,
        NprTestCase test,
        int value,
        NprRenderMode renderMode,
        int curN,
        int curK,
        int curS,
        double[] cpuTotalFrameTimings,
        double[] gpuFrameTimings)
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

        if (cpuTotalFrameTimings == null || cpuTotalFrameTimings.Length == 0)
        {
            Debug.LogError("CsvWriter.AppendSummaryRow: cpuTotalFrameTimings was null or empty.");
            return;
        }

        if (gpuFrameTimings == null || gpuFrameTimings.Length == 0)
        {
            Debug.LogError("CsvWriter.AppendSummaryRow: gpuFrameTimings was null or empty.");
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
                "mean_cpu_total_frame_ms,median_cpu_total_frame_ms,p95_cpu_total_frame_ms,p99_cpu_total_frame_ms,max_cpu_total_frame_ms,std_cpu_total_frame_ms," +
                "mean_gpu_frame_ms,median_gpu_frame_ms,p95_gpu_frame_ms,p99_gpu_frame_ms,max_gpu_frame_ms,std_gpu_frame_ms");
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

            BenchmarkStats.Mean(cpuTotalFrameTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Median(cpuTotalFrameTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(cpuTotalFrameTimings, 95).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(cpuTotalFrameTimings, 99).ToString("F6", CsvCulture),
            BenchmarkStats.Max(cpuTotalFrameTimings).ToString("F6", CsvCulture),
            BenchmarkStats.StdDev(cpuTotalFrameTimings).ToString("F6", CsvCulture),

            BenchmarkStats.Mean(gpuFrameTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Median(gpuFrameTimings).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(gpuFrameTimings, 95).ToString("F6", CsvCulture),
            BenchmarkStats.Percentile(gpuFrameTimings, 99).ToString("F6", CsvCulture),
            BenchmarkStats.Max(gpuFrameTimings).ToString("F6", CsvCulture),
            BenchmarkStats.StdDev(gpuFrameTimings).ToString("F6", CsvCulture)
        };

        sw.WriteLine(string.Join(",", fields));
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