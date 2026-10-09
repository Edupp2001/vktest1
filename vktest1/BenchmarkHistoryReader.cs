using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public class BenchmarkHistoryReader
{
    private readonly string _historyFolder;

    public BenchmarkHistoryReader()
    {
        // %TEMP%\b1\BenchMarkHistory\Tool
        _historyFolder = Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");
        Console.WriteLine($"Папка истории: {_historyFolder}");
    }

    /// <summary>
    /// Возвращает timestamp самого свежего файла до запуска бенчмарка.
    /// Нужно чтобы потом понять, что появился новый файл.
    /// </summary>
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
    /// Ждёт появления нового файла в папке истории, но не дольше timeout.
    /// </summary>
    public FileInfo WaitForNewFile(DateTime previousTimestamp, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var newest = new DirectoryInfo(_historyFolder)
                .GetFiles("*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest != null && newest.LastWriteTimeUtc > previousTimestamp)
            {
                Console.WriteLine($"[+] Новый файл результатов: {newest.Name}");
                return newest;
            }
            System.Threading.Thread.Sleep(2000);
        }
        throw new TimeoutException($"Новый файл результатов не появился за {timeout.TotalSeconds} сек");
    }

    /// <summary>
    /// Читает нужные поля из JSON. Использует JsonDocument для lazy-доступа —
    /// массив Records с тысячами кадров не парсится, если его не трогать.
    /// </summary>
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

    public override string ToString() =>
        $"Avg={AvgFps:F1} | Max={MaxFps:F1} | Min={MinFps:F1} | P95={P95Fps:F1} " +
        $"| CPU={CpuAvg:F1}% | GPU={GpuAvg:F1}%";
}