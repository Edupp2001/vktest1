using System.Diagnostics;
using System.Globalization;

public static class Log
{
    public static bool Verbose { get; set; }

    private static readonly Stopwatch _sw = Stopwatch.StartNew();

    static Log()
    {
        // Форсируем InvariantCulture, чтобы F2 давал "0.01", а не "0,01".
        // Это критично для парсинга чисел в отчётах и сравнения строк.
        // Program.cs полагается на эту инициализацию.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
    }

    private static string P() => $"[{_sw.Elapsed.TotalSeconds,7:F2}s]";

    public static void Info(string msg) => Console.WriteLine($"{P()} {msg}");

    public static void Debug(string msg)
    {
        if (Verbose) Console.WriteLine($"{P()} {msg}");
    }

    public static void Debug(Func<string> msgFactory)
    {
        if (Verbose) Console.WriteLine($"{P()} {msgFactory()}");
    }

    public static void Warn(string msg) => Console.WriteLine($"{P()} [WARN] {msg}");
    public static void Error(string msg) => Console.WriteLine($"{P()} [ERROR] {msg}");
    public static void Raw(string msg = "") => Console.WriteLine(msg);
}