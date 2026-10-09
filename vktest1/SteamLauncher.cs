using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

/// Запускает игру через Steam-клиент: steam.exe -applaunch <appid> <args>.
/// Steam сам передаст аргументы и не будет показывать диалог подтверждения.

public static class SteamLauncher
{
    ///Ищет steam.exe через реестр, libraryfolders.vdf и типовые пути.</summary>
    public static string? FindSteamExePath()
    {
        foreach (var p in EnumerateSteamCandidates())
        {
            var exe = Path.Combine(p, "steam.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateSteamCandidates()
    {
        // HKCU (основной) и HKLM (для системных установок)
        foreach (var (hive, sub) in new[]
        {
            (Registry.CurrentUser,  @"Software\Valve\Steam"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam"),
        })
        {
            string? path = null;
            try
            {
                using var key = hive.OpenSubKey(sub);
                path = key?.GetValue("SteamPath") as string
                    ?? key?.GetValue("InstallPath") as string;
            }
            catch { /* нет доступа — идём дальше */ }

            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                yield return path!.Replace('/', '\\');
        }

        // Типовые пути на всех фиксированных дисках
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "Program Files (x86)", "Steam");
            yield return Path.Combine(root, "Program Files", "Steam");
            yield return Path.Combine(root, "Steam");
            yield return Path.Combine(root, "SteamLibrary");
        }
    }

    /// Запускает игру через Steam. Возвращает true, если удалось стартовать steam.exe.
    /// Сама игра стартует асинхронно, прицепку к процессу делает MemoryFpsMonitor.
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
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = cmdArgs,
                UseShellExecute = false
            });
            return proc != null;
        }
        catch (Exception ex)
        {
            Log.Error($"[STEAM] Ошибка запуска: {ex.Message}");
            return false;
        }
    }
}