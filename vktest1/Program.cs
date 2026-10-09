using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// ===================== АРГУМЕНТЫ КОМАНДНОЙ СТРОКИ =====================
bool verbose = args.Any(a =>
    a.Equals("-log", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--log", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("-v", StringComparison.OrdinalIgnoreCase));

Log.Verbose = verbose;
Console.OutputEncoding = Encoding.UTF8;

// ===================== КОНФИГУРАЦИЯ =====================
const int STEAM_APP_ID = 3132990;

string gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Black Myth Wukong Benchmark Tool";
string iniPath = Path.Combine(gamePath, @"b1\Saved\Config\Windows\GameUserSettings.ini");
string exePath = Path.Combine(gamePath, @"b1\Binaries\Win64\b1-Win64-Shipping.exe");

Log.Debug("=== Параметры запуска ===");
Log.Debug($"  GamePath : {gamePath}");
Log.Debug($"  IniPath  : {iniPath}");
Log.Debug($"  ExePath  : {exePath}");
Log.Debug($"  AppID    : {STEAM_APP_ID}");
Log.Debug($"  Verbose  : {verbose}");
Log.Debug("");

// ===================== ИНИЦИАЛИЗАЦИЯ =====================
var config = new GameConfigManager(iniPath);
var history = new BenchmarkHistoryReader();
config.Backup();

// Регистрируем cleanup ДО запуска игры. Теперь любой способ закрытия
// приложения корректно остановит игру и восстановит конфиг.
GracefulShutdown.Register(() =>
{
    Log.Warn("Останавливаем игру и восстанавливаем GameUserSettings.ini...");
    KillAll();
    try { config.Restore(); } catch { }
});

try
{
    // ===================== CPU-ТЕСТ =====================
    Log.Info("========== CPU-ТЕСТ ==========");
    config.ApplyCpuSettings();
    var (cpuJson, cpuMem) = await RunOneBenchmark(exePath, STEAM_APP_ID, history);
    KillAll();
    await Task.Delay(5000);

    // ===================== GPU-ТЕСТ =====================
    Log.Info("");
    Log.Info("========== GPU-ТЕСТ ==========");
    config.ApplyGpuSettings();
    var (gpuJson, gpuMem) = await RunOneBenchmark(exePath, STEAM_APP_ID, history);
    KillAll();

    // ===================== ИТОГОВЫЙ ОТЧЁТ =====================
    Log.Info("");
    PrintReport(cpuJson, cpuMem, gpuJson, gpuMem);
}
catch (Exception ex)
{
    Log.Error($"Критическая ошибка: {ex.Message}");
    Environment.ExitCode = 1;
}

// ============================================================
//                  ЛОКАЛЬНЫЕ ФУНКЦИИ
// ============================================================

static async Task<(BenchmarkResult json, MemoryFpsStats mem)> RunOneBenchmark(
    string exePath, int steamAppId, BenchmarkHistoryReader history)
{
    var lastTimestamp = history.GetLatestFileTimestamp();
    Log.Debug($"Последний файл истории до старта: {lastTimestamp:u}");

    Log.Info("Запуск бенчмарка...");

    bool viaSteam = steamAppId > 0 && SteamLauncher.LaunchGame(steamAppId, "-benchmark");

    if (!viaSteam)
    {
        // Fallback: прямой запуск .exe. Здесь попап Steam ГАРАНТИРОВАННО появится.
        Log.Warn("Steam-launch не удался, fallback на прямой запуск .exe");
        Log.Warn("Ожидается диалог Steam — будет закрыт автоматически");
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "-benchmark",
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!
        });

        // Ждём и закрываем попап только в этом сценарии.
        await SteamPopupHandler.DismissAsync(TimeSpan.FromSeconds(20));
    }
    else
    {
        Log.Debug("[STEAM] Запуск через -applaunch: диалог не ожидается");
    }

    // Фоновой монитор памяти.
    using var monitor = new MemoryFpsMonitor("b1-Win64-Shipping");
    monitor.Start();
    Log.Debug("[MEM] Сэмплирование запущено");

    Log.Info("Ожидание результатов бенчмарка...");
    var file = history.WaitForNewFile(lastTimestamp, TimeSpan.FromMinutes(5));
    await Task.Delay(1500);

    monitor.Stop();
    var memStats = monitor.GetStats();
    var jsonResult = history.Read(file);

    Log.Debug($"Файл результатов: {file.Name}");
    return (jsonResult, memStats);
}

static void PrintReport(BenchmarkResult cpuJ, MemoryFpsStats cpuM,
                        BenchmarkResult gpuJ, MemoryFpsStats gpuM)
{
    Log.Raw("╔══════════════════════════════════════════════════════════════╗");
    Log.Raw("║                    РЕЗУЛЬТАТЫ БЕНЧМАРКА                      ║");
    Log.Raw("╚══════════════════════════════════════════════════════════════╝");
    Log.Raw();

    Log.Raw("Система:");
    Log.Raw($"  CPU:  {cpuJ.CpuModel}");
    Log.Raw($"  GPU:  {cpuJ.GpuModel} ({cpuJ.VideoMem}, driver {cpuJ.GpuDriver})");
    Log.Raw($"  RAM:  {cpuJ.SysMem}");
    Log.Raw();

    Log.Raw("┌─ CPU-ТЕСТ ────────────────────────────────────────────────┐");
    Log.Raw($"│ {cpuJ.Resolution,-12} │ QualityLevel={cpuJ.QualityLevel}  Rtx={cpuJ.Rtx}  Dlss={cpuJ.Dlss}");
    Log.Raw("├───────────────────────────────────────────────────────────┤");
    PrintRow("Avg FPS", cpuJ.AvgFps, cpuM.Avg);
    PrintRow("Max FPS", cpuJ.MaxFps, cpuM.Max);
    PrintRow("Min FPS", cpuJ.MinFps, cpuM.Min);
    PrintRow("P95 / P5", cpuJ.P95Fps, cpuM.P5);
    Log.Raw("└───────────────────────────────────────────────────────────┘");
    Log.Raw();

    Log.Raw("┌─ GPU-ТЕСТ ────────────────────────────────────────────────┐");
    Log.Raw($"│ {gpuJ.Resolution,-12} │ QualityLevel={gpuJ.QualityLevel}  Rtx={gpuJ.Rtx}  Dlss={gpuJ.Dlss}");
    Log.Raw("├───────────────────────────────────────────────────────────┤");
    PrintRow("Avg FPS", gpuJ.AvgFps, gpuM.Avg);
    PrintRow("Max FPS", gpuJ.MaxFps, gpuM.Max);
    PrintRow("Min FPS", gpuJ.MinFps, gpuM.Min);
    PrintRow("P95 / P5", gpuJ.P95Fps, gpuM.P5);
    Log.Raw("└───────────────────────────────────────────────────────────┘");
    Log.Raw();

    Log.Raw("Валидация (JSON vs live memory sampling):");
    Log.Raw($"  CPU:  ΔAvg = {Math.Abs(cpuJ.AvgFps - cpuM.Avg):F2} FPS, ΔP5 = {Math.Abs(cpuJ.P95Fps - cpuM.P5):F0} FPS");
    Log.Raw($"  GPU:  ΔAvg = {Math.Abs(gpuJ.AvgFps - gpuM.Avg):F2} FPS, ΔP5 = {Math.Abs(gpuJ.P95Fps - cpuM.P5):F0} FPS");
}

static void PrintRow(string label, double json, double mem)
{
    double delta = Math.Abs(json - mem);
    Log.Raw($"│ {label,-10} │ JSON: {json,6:F1}  │  MEM: {mem,6:F1}  │  Δ {delta,4:F1}  │");
}

static void KillAll()
{
    foreach (var name in new[] { "b1-Win64-Shipping", "b1_benchmark", "EpicWebHelper" })
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                p.Kill(entireProcessTree: true);
                Log.Debug($"Убит {name} PID={p.Id}");
            }
            catch { }
        }
    }
}