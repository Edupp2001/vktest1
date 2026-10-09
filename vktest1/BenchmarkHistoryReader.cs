using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class BenchmarkHistoryReader
{
    private readonly string _historyFolder;
    private readonly string _gameProcessName;

    public BenchmarkHistoryReader(string gameProcessName = "b1-Win64-Shipping")
    {
        _gameProcessName = gameProcessName;
        _historyFolder = Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");
        Log.Debug($"Папка истории: {_historyFolder}");
    }

    public DateTime GetLatestFileTimestamp()
    {
        if (!Directory.Exists(_historyFolder)) return DateTime.MinValue;

        var latest = new DirectoryInfo(_historyFolder)
            .GetFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        return latest?.LastWriteTimeUtc ?? DateTime.MinValue;
    }

    /// <summary>
    /// Ждёт появления нового файла результатов.
    /// Выход: новый файл, смерть процесса + 5с форы, или общий таймаут.
    /// </summary>
    public async Task<FileInfo> WaitForNewFileAsync(
        DateTime previousTimestamp,
        TimeSpan? overallTimeout = null,
        CancellationToken cancellationToken = default)
    {
        long deadline = overallTimeout.HasValue
            ? Environment.TickCount64 + (long)overallTimeout.Value.TotalMilliseconds
            : long.MaxValue;

        int missingProcessCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException(
                    $"Файл результатов не появился за {overallTimeout!.Value.TotalSeconds:F0}с");

            var newest = FindNewestFile();
            if (newest != null && newest.LastWriteTimeUtc > previousTimestamp)
            {
                Log.Debug($"[HIST] Новый файл результатов: {newest.Name}");
                await WaitForFileStableAsync(newest, cancellationToken);
                return newest;
            }

            bool gameAlive = Process.GetProcessesByName(_gameProcessName).Length > 0;
            if (!gameAlive)
            {
                missingProcessCount++;
                if (missingProcessCount >= 10)
                    throw new InvalidOperationException(
                        "Игровой процесс завершился, но файл результатов не появился");
            }
            else
            {
                missingProcessCount = 0;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForFileStableAsync(FileInfo file, CancellationToken ct)
    {
        long lastSize = -1;
        int stableCount = 0;
        long deadline = Environment.TickCount64 + 15_000; // защита от вечного цикла

        while (stableCount < 3)
        {
            ct.ThrowIfCancellationRequested();

            if (Environment.TickCount64 >= deadline)
            {
                Log.Warn($"[HIST] Файл {file.Name} не стабилизировался за 15с, читаем как есть");
                return;
            }

            try
            {
                long size = new FileInfo(file.FullName).Length;
                if (size == lastSize && size > 0) stableCount++;
                else stableCount = 0;
                lastSize = size;
            }
            catch (IOException) { /* файл ещё не готов */ }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    private FileInfo? FindNewestFile()
    {
        if (!Directory.Exists(_historyFolder)) return null;
        return new DirectoryInfo(_historyFolder)
            .GetFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    public BenchmarkResult Read(FileInfo file)
    {
        try
        {
            using var stream = File.OpenRead(file.FullName);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            return new BenchmarkResult
            {
                Timestamp = GetInt64(root, "TimeStamp"),
                AvgFps = GetDouble(root, "FPSAvg"),
                MaxFps = GetDouble(root, "FPSMax"),
                MinFps = GetDouble(root, "FPSMin"),
                P95Fps = GetDouble(root, "FPS95"),
                CpuAvg = GetDouble(root, "CPUAvg"),
                GpuAvg = GetDouble(root, "GPUAvg"),
                CpuModel = GetString(root, "CPUModel")?.Trim() ?? "",
                GpuModel = GetString(root, "GPUModel") ?? "",
                GpuDriver = GetString(root, "GpuDriverVer") ?? "",
                SysMem = GetString(root, "SysMem") ?? "",
                VideoMem = GetString(root, "VideoMemSize") ?? "",
                Resolution = GetString(root, "ScreenResolution") ?? "",
                QualityLevel = GetInt32(root, "QualityLevel"),
                Rtx = GetInt32(root, "Rtx"),
                Dlss = GetInt32(root, "Dlss"),
                MotionBlur = GetInt32(root, "MotionBlur"),
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Некорректный JSON в {file.Name}: {ex.Message}", ex);
        }
    }

    private static long GetInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var r) ? r : 0;
    }

    private static int GetInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var r) ? r : 0;
    }

    private static double GetDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind != JsonValueKind.Number) return 0;
        return v.TryGetDouble(out var r) ? r : 0;
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}

public class BenchmarkResult
{
    public long Timestamp { get; set; }
    public double AvgFps { get; set; }
    public double MaxFps { get; set; }
    public double MinFps { get; set; }
    public double P95Fps { get; set; }
    public double CpuAvg { get; set; }
    public double GpuAvg { get; set; }
    public string CpuModel { get; set; } = "";
    public string GpuModel { get; set; } = "";
    public string GpuDriver { get; set; } = "";
    public string SysMem { get; set; } = "";
    public string VideoMem { get; set; } = "";
    public string Resolution { get; set; } = "";
    public int QualityLevel { get; set; }
    public int Rtx { get; set; }
    public int Dlss { get; set; }
    public int MotionBlur { get; set; }
}