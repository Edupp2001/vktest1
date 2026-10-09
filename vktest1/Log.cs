using System;
using System.Diagnostics;

/// <summary>
/// Централизованный логгер с двумя уровнями:
///   Info  — всегда выводится (этапы пайплайна, итоговый отчёт).
///   Debug — только при флаге -log (детали: пути, attach, сэмплы, попапы).
/// </summary>
public static class Log
{
    public static bool Verbose { get; set; }
    private static readonly Stopwatch _sw = Stopwatch.StartNew();

    private static string P() => $"[{_sw.Elapsed.TotalSeconds,7:F2}s]";

    public static void Info(string msg) => Console.WriteLine($"{P()} {msg}");
    public static void Debug(string msg) { if (Verbose) Console.WriteLine($"{P()} {msg}"); }
    public static void Warn(string msg) => Console.WriteLine($"{P()} [WARN] {msg}");
    public static void Error(string msg) => Console.WriteLine($"{P()} [ERROR] {msg}");

    /// <summary>Вывод без префикса — для итоговой таблицы.</summary>
    public static void Raw(string msg = "") => Console.WriteLine(msg);

}