using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

// ===================== ГЛОБАЛЬНЫЕ НАСТРОЙКИ =====================
try { Console.OutputEncoding = Encoding.UTF8; }
catch { /* на некоторых Win10-консолях бросает — не критично */ }

// ===================== АРГУМЕНТЫ =====================
bool verbose = args.Any(a =>
    a.Equals("-log", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--log", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("-v", StringComparison.OrdinalIgnoreCase));

Log.Verbose = verbose;

// ===================== КОНФИГУРАЦИЯ =====================
const int STEAM_APP_ID = 3132990;
const string GAME_PROCESS = "b1-Win64-Shipping";
const string GAME_FOLDER = "Black Myth Wukong Benchmark Tool";

string? gamePath = ResolveGamePath(args);
if (gamePath == null)
{
    Log.Error("Не удалось найти папку с игрой.");
    Log.Error("Укажите путь одним из способов:");
    Log.Error("  --game-path \"D:\\Games\\Black Myth Wukong Benchmark Tool\"");
    Log.Error("  переменная окружения GAME_PATH");
    Environment.Exit(1);
    return; // unreachable, нужно для компилятора
}

string iniPath = Path.Combine(gamePath, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");
string exePath = Path.Combine(gamePath, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");

// ===================== ПРЕДВАРИТЕЛЬНЫЕ ПРОВЕРКИ =====================
if (!File.Exists(iniPath))
{
    Log.Error($"Не найден конфиг игры: {iniPath}");
    Log.Error("Проверьте, что Black Myth: Wukong Benchmark Tool установлен.");
    Environment.Exit(1);
    return;
}

if (!File.Exists(exePath))
{
    Log.Error($"Не найден исполняемый файл игры: {exePath}");
    Environment.Exit(1);
    return;
}

if (IsProcessRunning(GAME_PROCESS))
{
    Log.Error($"Игра уже запущена ({GAME_PROCESS}). Закройте её и попробуйте снова.");
    Environment.Exit(1);
    return;
}

Log.Debug("=== Параметры запуска ===");
Log.Debug($"  GamePath : {gamePath}");
Log.Debug($"  IniPath  : {iniPath}");
Log.Debug($"  ExePath  : {exePath}");
Log.Debug($"  AppID    : {STEAM_APP_ID}");
Log.Debug($"  Verbose  : {verbose}");
Log.Debug("");

// ===================== ИНИЦИАЛИЗАЦИЯ =====================
var config = new GameConfigManager(iniPath);
var history = new BenchmarkHistoryReader(GAME_PROCESS);

config.Backup();

GracefulShutdown.Register(() =>
{
    Log.Warn("Останавливаем игру и восстанавливаем GameUserSettings.ini...");
    KillAll();
    try { config.Restore(); }
    catch (Exception ex) { Log.Error($"Не удалось восстановить конфиг: {ex.Message}"); }
});

int exitCode = 0;

try
{
    // ===================== CPU-ТЕСТ =====================
    Log.Info("========== CPU-ТЕСТ ==========");
    config.ApplyCpuSettings();
    var (cpuJson, cpuMem) = await RunOneBenchmark(exePath, STEAM_APP_ID, history);
    KillAll();
    await WaitForProcessExitAsync(GAME_PROCESS, TimeSpan.FromSeconds(15));

    // ===================== GPU-ТЕСТ =====================
    Log.Info("");
    Log.Info("========== GPU-ТЕСТ ==========");
    config.ApplyGpuSettings();
    var (gpuJson, gpuMem) = await RunOneBenchmark(exePath, STEAM_APP_ID, history);
    KillAll();

    // ===================== ОТЧЁТ =====================
    var report = new BenchmarkReport
    {
        System = new BenchmarkReport.SystemInfoDto
        {
            Cpu = cpuJson.CpuModel,
            Gpu = cpuJson.GpuModel,
            GpuDriver = cpuJson.GpuDriver,
            Ram = cpuJson.SysMem,
            VideoMem = cpuJson.VideoMem,
        },
        Cpu = new BenchmarkReport.BenchmarkPair { Json = cpuJson, Memory = cpuMem },
        Gpu = new BenchmarkReport.BenchmarkPair { Json = gpuJson, Memory = gpuMem },
        Validation = new BenchmarkReport.ValidationDto
        {
            CpuAvgDelta = Math.Abs(cpuJson.AvgFps - cpuMem.Avg),
            CpuP5Delta = Math.Abs(cpuJson.P95Fps - cpuMem.P5),
            GpuAvgDelta = Math.Abs(gpuJson.AvgFps - gpuMem.Avg),
            GpuP5Delta = Math.Abs(gpuJson.P95Fps - gpuMem.P5),
        }
    };

    Log.Info("");
    PrintReport(cpuJson, cpuMem, gpuJson, gpuMem);

    var reportPath = report.SaveToFile();
    Log.Info($"Отчёт сохранён: {reportPath}");
}
catch (Exception ex)
{
    Log.Error($"Ошибка: {ex.Message}");
    exitCode = ex is FileNotFoundException ? 1 : 2;
}

Environment.Exit(exitCode);


// ============================================================
//                  ЛОКАЛЬНЫЕ ФУНКЦИИ
// ============================================================

static bool IsProcessRunning(string name)
{
    var procs = Process.GetProcessesByName(name);
    try { return procs.Length > 0; }
    finally { foreach (var p in procs) p.Dispose(); }
}

static string? ResolveGamePath(string[] args)
{
    // 1. CLI: --game-path <path>
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals("--game-path", StringComparison.OrdinalIgnoreCase))
        {
            var p = args[i + 1];
            if (Directory.Exists(p)) return p;
            Log.Warn($"Путь из --game-path не существует: {p}");
        }
    }

    // 2. Env
    var env = Environment.GetEnvironmentVariable("GAME_PATH");
    if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        return env;

    // 3. Steam library folders (реестр + libraryfolders.vdf)
    var fromSteam = FindViaSteamLibraries(GAME_FOLDER);
    if (fromSteam != null) return fromSteam;

    // 4. Обход всех фиксированных дисков
    foreach (var drive in DriveInfo.GetDrives())
    {
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

        var root = drive.RootDirectory.FullName;
        string[] candidates =
        {
            Path.Combine(root, "Program Files (x86)", "Steam", "steamapps", "common", GAME_FOLDER),
            Path.Combine(root, "Program Files", "Steam", "steamapps", "common", GAME_FOLDER),
            Path.Combine(root, "Steam", "steamapps", "common", GAME_FOLDER),
            Path.Combine(root, "SteamLibrary", "steamapps", "common", GAME_FOLDER),
            Path.Combine(root, "Games", "Steam", "steamapps", "common", GAME_FOLDER),
        };
        foreach (var c in candidates)
            if (Directory.Exists(c)) return c;
    }

    return null;
}

static string? FindViaSteamLibraries(string gameFolderName)
{
    try
    {
        string? steamPath = null;

        // SteamPath хранится в HKCU. Пишем через RegEx, чтобы не тянуть лишние зависимости.
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            steamPath = key?.GetValue("SteamPath") as string;

        if (string.IsNullOrEmpty(steamPath)) return null;
        steamPath = steamPath.Replace('/', '\\');

        var libraries = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (File.Exists(vdfPath))
        {
            // Простейший парсер VDF: строки вида   "path"		"D:\\SteamLibrary"
            var re = new Regex("^\\s*\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            foreach (var line in File.ReadLines(vdfPath))
            {
                var m = re.Match(line);
                if (m.Success)
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }

        foreach (var lib in libraries)
        {
            var candidate = Path.Combine(lib, "steamapps", "common", gameFolderName);
            if (Directory.Exists(candidate)) return candidate;
        }
    }
    catch (Exception ex)
    {
        Log.Debug($"[STEAM] Не удалось прочитать библиотеки Steam: {ex.Message}");
    }
    return null;
}

static async Task<(BenchmarkResult json, MemoryFpsStats mem)> RunOneBenchmark(
    string exePath, int steamAppId, BenchmarkHistoryReader history)
{
    var lastTimestamp = history.GetLatestFileTimestamp();
    Log.Debug($"Последний файл истории до старта: {lastTimestamp:u}");

    Log.Info("Запуск бенчмарка...");

    bool viaSteam = steamAppId > 0 && SteamLauncher.LaunchGame(steamAppId, "-benchmark");

    if (!viaSteam)
    {
        Log.Warn("Steam-launch не удался, fallback на прямой запуск .exe");
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "-benchmark",
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!
        });
        await SteamPopupHandler.DismissAsync(TimeSpan.FromSeconds(20));
    }
    else
    {
        Log.Debug("[STEAM] Запуск через -applaunch: диалог не ожидается");
    }

    using var monitor = new MemoryFpsMonitor(GAME_PROCESS);
    monitor.Start();
    Log.Debug("[MEM] Сэмплирование запущено");

    Log.Info("Ожидание результатов бенчмарка...");
    var file = await history.WaitForNewFileAsync(lastTimestamp);

    monitor.Stop();
    var memStats = monitor.GetStats();
    var jsonResult = history.Read(file);

    Log.Debug($"Файл результатов: {file.Name}");
    return (jsonResult, memStats);
}

static async Task WaitForProcessExitAsync(string processName, TimeSpan timeout)
{
    long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
    while (Environment.TickCount64 < deadline)
    {
        if (!IsProcessRunning(processName)) return;
        await Task.Delay(250);
    }
    Log.Warn($"Процесс {processName} не завершился за {timeout.TotalSeconds:F0}с");
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
    Log.Raw($"  GPU:  ΔAvg = {Math.Abs(gpuJ.AvgFps - gpuM.Avg):F2} FPS, ΔP5 = {Math.Abs(gpuJ.P95Fps - gpuM.P5):F0} FPS");
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
        var procs = Process.GetProcessesByName(name);
        try
        {
            foreach (var p in procs)
            {
                try { p.Kill(entireProcessTree: true); Log.Debug($"Убит {name} PID={p.Id}"); }
                catch (Exception ex) { Log.Debug($"Не удалось убить {name} PID={p.Id}: {ex.Message}"); }
            }
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }
}