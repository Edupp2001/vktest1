using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

public class BenchmarkHistoryReader
{
    private readonly string _historyFolder;
    private readonly string _gameProcessName;

    public BenchmarkHistoryReader(string gameProcessName = "b1-Win64-Shipping")
    {
        _historyFolder = Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");
        _gameProcessName = gameProcessName;
        Log.Debug($"Папка истории: {_historyFolder}");
    }

    public DateTime GetLatestFileTimestamp()
    {
        if (!Directory.Exists(_historyFolder)) return DateTime.MinValue;
        var latest = new DirectoryInfo(_historyFolder)
            .GetFiles("*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return latest?.LastWriteTimeUtc ?? DateTime.MinValue;
    }

    /// <summary>
    /// Ждёт появления нового файла результатов. Без фиксированного таймаута:
    /// выход происходит по появлению файла ИЛИ по смерти игрового процесса.
    /// </summary>
    public async Task<FileInfo> WaitForNewFileAsync(DateTime previousTimestamp)
    {
        int missingProcessCount = 0;

        while (true)
        {
            var newest = FindNewestFile();
            if (newest != null && newest.LastWriteTimeUtc > previousTimestamp)
            {
                Log.Debug($"[HIST] Новый файл результатов: {newest.Name}");
                await WaitForFileStableAsync(newest);
                return newest;
            }

            // Проверяем, жив ли ещё процесс игры. Если игра упала — нет смысла ждать.
            bool gameAlive = Process.GetProcessesByName(_gameProcessName).Length > 0;
            if (!gameAlive)
            {
                missingProcessCount++;
                // Даём 5 секунд форы: файл может появиться с задержкой после смерти процесса.
                if (missingProcessCount >= 10)
                    throw new Exception("Игровой процесс завершился, но файл результатов не появился");
            }
            else
            {
                missingProcessCount = 0;
            }

            await Task.Delay(500);
        }
    }

    /// <summary>
    /// Дожидается, пока файл перестанет расти. Заменяет фиксированный Sleep(1500).
    /// </summary>
    private static async Task WaitForFileStableAsync(FileInfo file)
    {
        long lastSize = -1;
        int stableCount = 0;

        while (stableCount < 3)
        {
            try
            {
                long size = new FileInfo(file.FullName).Length;
                if (size == lastSize && size > 0) stableCount++;
                else stableCount = 0;
                lastSize = size;
            }
            catch { /* файл ещё не готов к чтению */ }

            await Task.Delay(200);
        }
    }

    private FileInfo? FindNewestFile()
    {
        if (!Directory.Exists(_historyFolder)) return null;
        return new DirectoryInfo(_historyFolder)
            .GetFiles("*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    public BenchmarkResult Read(FileInfo file)
    {
        using var stream = File.OpenRead(file.FullName);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        return new BenchmarkResult
        {
            Timestamp = root.TryGetProperty("TimeStamp", out var ts) ? ts.GetInt64() : 0,
            AvgFps = root.TryGetProperty("FPSAvg", out var a) ? a.GetDouble() : 0,
            MaxFps = root.TryGetProperty("FPSMax", out var mx) ? mx.GetDouble() : 0,
            MinFps = root.TryGetProperty("FPSMin", out var mn) ? mn.GetDouble() : 0,
            P95Fps = root.TryGetProperty("FPS95", out var p) ? p.GetDouble() : 0,
            CpuAvg = root.TryGetProperty("CPUAvg", out var c) ? c.GetDouble() : 0,
            GpuAvg = root.TryGetProperty("GPUAvg", out var g) ? g.GetDouble() : 0,
            CpuModel = root.TryGetProperty("CPUModel", out var cm) ? cm.GetString()?.Trim() ?? "" : "",
            GpuModel = root.TryGetProperty("GPUModel", out var gm) ? gm.GetString() ?? "" : "",
            GpuDriver = root.TryGetProperty("GpuDriverVer", out var gd) ? gd.GetString() ?? "" : "",
            SysMem = root.TryGetProperty("SysMem", out var sm) ? sm.GetString() ?? "" : "",
            VideoMem = root.TryGetProperty("VideoMemSize", out var vm) ? vm.GetString() ?? "" : "",
            Resolution = root.TryGetProperty("ScreenResolution", out var r) ? r.GetString() ?? "" : "",
            QualityLevel = root.TryGetProperty("QualityLevel", out var q) ? q.GetInt32() : 0,
            Rtx = root.TryGetProperty("Rtx", out var rt) ? rt.GetInt32() : 0,
            Dlss = root.TryGetProperty("Dlss", out var dl) ? dl.GetInt32() : 0,
            MotionBlur = root.TryGetProperty("MotionBlur", out var mb) ? mb.GetInt32() : 0,
        };
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