using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

/// <summary>
/// Запускает игру через Steam-клиент: steam.exe -applaunch <appid> <args>.
/// Steam сам передаст аргументы и не будет показывать диалог подтверждения,
/// потому что запуск идёт "изнутри" клиента.
/// </summary>
public static class SteamLauncher
{
    /// <summary>
    /// Ищет steam.exe через реестр, потом по типовым путям.
    /// </summary>
    public static string? FindSteamExePath()
    {
        // 1. Реестр — самый надёжный
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamExe") as string;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;
        }
        catch { /* игнорируем — попробуем ниже */ }

        // 2. Стандартные пути установки
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Steam\steam.exe",
            @"C:\Program Files\Steam\steam.exe",
            @"D:\Steam\steam.exe",
            @"D:\Program Files (x86)\Steam\steam.exe",
            @"E:\Steam\steam.exe"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Запускает игру через Steam. Возвращает true, если удалось стартовать steam.exe.
    /// Сама игра стартует асинхронно, прицепку к процессу делает MemoryFpsMonitor.
    /// </summary>
    public static bool LaunchGame(int appId, string arguments = "")
    {
        var steamExe = FindSteamExePath();
        if (steamExe == null)
        {
            Log.Warn("[STEAM] steam.exe не найден, fallback на прямой запуск");
            return false;
        }

        string cmdArgs = $"-applaunch {appId}";
        if (!string.IsNullOrWhiteSpace(arguments))
            cmdArgs += " " + arguments;

        Log.Debug($"[STEAM] {steamExe} {cmdArgs}");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = cmdArgs,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[STEAM] Ошибка запуска: {ex.Message}");
            return false;
        }
    }
}