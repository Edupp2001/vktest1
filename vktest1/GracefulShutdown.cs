using System;
using System.Runtime.InteropServices;

/// <summary>
/// Перехватывает все сигналы завершения процесса и запускает cleanup-действие.
/// Покрывает: Ctrl+C, Ctrl+Break, закрытие окна (X), logoff, shutdown, unhandled exception.
/// Cleanup гарантированно выполняется один раз (idempotent).
/// </summary>
public static class GracefulShutdown
{
    private static readonly object _lock = new();
    private static bool _cleanupDone;
    private static Action? _cleanup;

    // ================= WinAPI =================
    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    private const uint CTRL_C_EVENT = 0;
    private const uint CTRL_BREAK_EVENT = 1;
    private const uint CTRL_CLOSE_EVENT = 2;
    private const uint CTRL_LOGOFF_EVENT = 5;
    private const uint CTRL_SHUTDOWN_EVENT = 6;

    public static void Register(Action cleanup)
    {
        _cleanup = cleanup;

        // 1. Ctrl+C / Ctrl+Break — позволяем нам сначала убраться.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // отменяем немедленное завершение
            Log.Warn("Ctrl+C, останавливаем игру и восстанавливаем конфиг...");
            RunCleanup();
            Environment.Exit(130); // стандартный код для SIGINT
        };

        // 2. Нормальный выход / Environment.Exit / падение Main.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunCleanup();

        // 3. Закрытие окна консоли (X), logoff, shutdown.
        SetConsoleCtrlHandler(ctrlType =>
        {
            Log.Warn($"Сигнал ОС (type={ctrlType}), завершаем работу...");
            RunCleanup();
            return false; // пусть система тоже сделает своё дело
        }, true);

        // 4. Необработанные исключения — ловим и всё равно делаем cleanup.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error($"Необработанное исключение: {e.ExceptionObject}");
            RunCleanup();
        };
    }

    private static void RunCleanup()
    {
        lock (_lock)
        {
            if (_cleanupDone) return;
            _cleanupDone = true;
        }

        try { _cleanup?.Invoke(); }
        catch (Exception ex) { Console.Error.WriteLine($"Ошибка cleanup: {ex.Message}"); }
    }
}