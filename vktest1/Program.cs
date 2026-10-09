using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

Console.OutputEncoding = Encoding.UTF8;

string gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Black Myth Wukong Benchmark Tool";
string iniPath = Path.Combine(gamePath, @"b1\Saved\Config\Windows\GameUserSettings.ini");
string exePath = Path.Combine(gamePath, @"b1\Binaries\Win64\b1-Win64-Shipping.exe");

var config = new GameConfigManager(iniPath);
var history = new BenchmarkHistoryReader();
config.Backup();

try
{
    // === CPU ===
    Console.WriteLine("===== CPU-ТЕСТ =====");
    config.ApplyCpuSettings();
    var cpuResult = await RunOneBenchmark(exePath, history);
    Console.WriteLine($"[CPU] {cpuResult}\n");
    KillAll();
    await Task.Delay(5000);

    // === GPU ===
    Console.WriteLine("===== GPU-ТЕСТ =====");
    config.ApplyGpuSettings();
    var gpuResult = await RunOneBenchmark(exePath, history);
    Console.WriteLine($"[GPU] {gpuResult}\n");
    KillAll();

    // === ИТОГ ===
    PrintReport(cpuResult, gpuResult);
}
finally
{
    config.Restore();
    KillAll();
}

// ===================== ЛОКАЛЬНЫЕ ФУНКЦИИ =====================

static async Task<BenchmarkResult> RunOneBenchmark(string exePath, BenchmarkHistoryReader history)
{
    var lastTimestamp = history.GetLatestFileTimestamp();
    Console.WriteLine($"Последний файл истории: {lastTimestamp:u}");

    Console.WriteLine("Запуск бенчмарка...");
    Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        Arguments = "-benchmark",
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(exePath)!
    });

    Console.WriteLine("Ожидание результатов (до 5 минут)...");
    var file = history.WaitForNewFile(lastTimestamp, TimeSpan.FromMinutes(5));
    // подождём секунду, чтобы файл дописался на диск
    await Task.Delay(1000);

    return history.Read(file);
}

static void PrintReport(BenchmarkResult cpu, BenchmarkResult gpu)
{
    Console.WriteLine("\n=============== ИТОГОВЫЙ ОТЧЁТ ===============");
    Console.WriteLine($"Характеристики ПК:");
    Console.WriteLine($"  CPU: {cpu.CpuModel}");
    Console.WriteLine($"  GPU: {cpu.GpuModel} ({cpu.VideoMem}, драйвер {cpu.GpuDriver})");
    Console.WriteLine($"  RAM: {cpu.SysMem}");
    Console.WriteLine();
    Console.WriteLine($"CPU-тест (разрешение {cpu.Resolution}, QualityLevel={cpu.QualityLevel}, Rtx={cpu.Rtx}, Dlss={cpu.Dlss})");
    Console.WriteLine($"  {cpu}");
    Console.WriteLine();
    Console.WriteLine($"GPU-тест (разрешение {gpu.Resolution}, QualityLevel={gpu.QualityLevel}, Rtx={gpu.Rtx}, Dlss={gpu.Dlss})");
    Console.WriteLine($"  {gpu}");
    Console.WriteLine("==============================================");
}

static void KillAll()
{
    foreach (var name in new[] { "b1-Win64-Shipping", "b1_benchmark", "EpicWebHelper" })
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(entireProcessTree: true); Console.WriteLine($"Убит {name} PID={p.Id}"); }
            catch { }
        }
    }
}