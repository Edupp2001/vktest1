using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

Console.OutputEncoding = Encoding.UTF8;

string gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Black Myth Wukong Benchmark Tool";
string iniPath = Path.Combine(gamePath, @"b1\Saved\Config\Windows\GameUserSettings.ini");
string exePath = Path.Combine(gamePath, @"b1\Binaries\Win64\b1-Win64-Shipping.exe");
string tessData = Path.Combine(AppContext.BaseDirectory, "tessdata");

var config = new GameConfigManager(iniPath);
var ocr = new OcrReader(tessData);

config.Backup();

try
{
    // === CPU TEST ===
    Console.WriteLine("--- CPU-ТЕСТ ---");
    config.ApplyCpuSettings();

    Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        Arguments = "-benchmark",
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(exePath)!
    });

    Console.WriteLine("Ожидание 210 секунд...");
    await Task.Delay(210_000);

    string shot = ScreenCapture.CaptureGameWindow("b1-Win64-Shipping");
    double cpuFps = ocr.ExtractAverageFps(shot);
    Console.WriteLine($">>> CPU FPS: {cpuFps}");

    KillAll();
    await Task.Delay(5000);

    // === GPU TEST ===
    Console.WriteLine("\n--- GPU-ТЕСТ ---");
    config.ApplyGpuSettings();

    Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        Arguments = "-benchmark",
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(exePath)!
    });

    Console.WriteLine("Ожидание 210 секунд...");
    await Task.Delay(210_000);

    shot = ScreenCapture.CaptureGameWindow("b1-Win64-Shipping");
    double gpuFps = ocr.ExtractAverageFps(shot);
    Console.WriteLine($">>> GPU FPS: {gpuFps}");

    KillAll();

    Console.WriteLine("\n=========== РЕЗУЛЬТАТЫ ===========");
    Console.WriteLine($"CPU: {cpuFps} FPS");
    Console.WriteLine($"GPU: {gpuFps} FPS");
}
finally
{
    config.Restore();
    KillAll();
    Console.WriteLine("Конфиг восстановлен, процессы убиты.");
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