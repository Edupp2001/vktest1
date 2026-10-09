using System;
using System.Runtime.InteropServices;

/// <summary>
/// Перехватывает сигналы завершения процесса и запускает cleanup-действие один раз.
/// Покрывает: Ctrl+C, Ctrl+Break, закрытие окна (X), logoff, shutdown, unhandled exception.
/// </summary>
public static class GracefulShutdown
{
    private static readonly object _lock = new();
    private static bool _cleanupDone;
    private static Action? _cleanup;

    // Держим делегат в статике — иначе GC может собрать лямбду во время нативного колбэка.
    private static readonly ConsoleCtrlDelegate s_ctrlHandler = OnConsoleCtrl;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    private const uint CTRL_C_EVENT = 0;
    private const uint CTRL_BREAK_EVENT = 1;
    private const uint CTRL_CLOSE_EVENT = 2;
    private const uint CTRL_LOGOFF_EVENT = 5;
    private const uint CTRL_SHUTDOWN_EVENT = 6;

    public static void Register(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        _cleanup = cleanup;

        // 1. Ctrl+C / Ctrl+Break
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Log.Warn("Ctrl+C, останавливаем игру и восстанавливаем конфиг...");
            RunCleanup();
            Environment.Exit(130);
        };

        // 2. Нормальный выход / Environment.Exit / падение Main
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunCleanup();

        // 3. Закрытие окна консоли (X), logoff, shutdown
        if (!SetConsoleCtrlHandler(s_ctrlHandler, true))
            Log.Warn($"[SHUTDOWN] SetConsoleCtrlHandler failed, lastError={Marshal.GetLastPInvokeError()}");

        // 4. Необработанные исключения
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error($"Необработанное исключение: {e.ExceptionObject}");
            RunCleanup();
        };
    }

    private static bool OnConsoleCtrl(uint ctrlType)
    {
        string name = ctrlType switch
        {
            CTRL_C_EVENT => "CTRL_C",
            CTRL_BREAK_EVENT => "CTRL_BREAK",
            CTRL_CLOSE_EVENT => "CTRL_CLOSE (X)",
            CTRL_LOGOFF_EVENT => "CTRL_LOGOFF",
            CTRL_SHUTDOWN_EVENT => "CTRL_SHUTDOWN",
            _ => $"UNKNOWN({ctrlType})"
        };
        Log.Warn($"Сигнал ОС: {name}, завершаем работу...");
        RunCleanup();
        return false; // пусть система тоже сделает своё дело
    }

    private static void RunCleanup()
    {
        lock (_lock)
        {
            if (_cleanupDone) return;
            _cleanupDone = true;
        }

        try { _cleanup?.Invoke(); }
        catch (Exception ex) { Log.Error($"Ошибка cleanup: {ex.Message}"); }
    }
}