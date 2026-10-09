using System.Collections.Immutable;
using System.Runtime.InteropServices;

public static partial class SteamPopupHandler
{
    private const uint BM_CLICK = 0x00F5;
    private const int PollIntervalMs = 300;
    private const int MaxWindowTextLength = 256;

    private static readonly ImmutableArray<string> WindowTitles = ImmutableArray.Create(
        "Запустить игру с пользовательскими параметрами",
        "Launch game with custom parameters",
        "启动游戏自定义参数",
        "Spiel mit benutzerdefinierten Parametern starten"
    );

    private static readonly ImmutableArray<string> ContinueButtonTexts = ImmutableArray.Create(
        "Продолжить", "Continue", "继续", "Fortfahren", "OK"
    );

    private static readonly EnumWindowsProc s_enumProc = OnEnumChildWindow;

    [ThreadStatic] private static char[]? t_textBuffer;
    [ThreadStatic] private static IntPtr t_foundButton;

    public static async Task DismissAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            Log.Warn($"[STEAM] DismissAsync: timeout={timeout} <= 0 — выходим");
            return;
        }

        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryDismissOnce())
            {
                Log.Debug("[STEAM] Диалог подтверждения закрыт");
                return;
            }

            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        Log.Warn($"[STEAM] Диалог не найден за {timeout.TotalSeconds:F0}с — возможен нестандартный сценарий");
    }

    private static bool TryDismissOnce()
    {
        foreach (var title in WindowTitles)
        {
            var hwnd = FindWindow(null, title);
            if (hwnd == IntPtr.Zero) continue;

            if (ClickContinueButton(hwnd)) return true;
        }
        return false;
    }

    private static bool ClickContinueButton(IntPtr parentHwnd)
    {
        t_foundButton = IntPtr.Zero;

        if (!EnumChildWindows(parentHwnd, s_enumProc, IntPtr.Zero))
        {
            Log.Debug($"[STEAM] EnumChildWindows failed, lastError={Marshal.GetLastPInvokeError()}");
            return false;
        }

        if (t_foundButton == IntPtr.Zero) return false;

        var result = SendMessage(t_foundButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Log.Debug($"[STEAM] BM_CLICK sent, result={result}");
        return true;
    }

    private static bool OnEnumChildWindow(IntPtr hWnd, IntPtr lParam)
    {
        var buffer = t_textBuffer ??= new char[MaxWindowTextLength];

        int len = GetWindowText(hWnd, buffer, buffer.Length);
        if (len <= 0) return true;

        string text = new string(buffer, 0, len);

        foreach (var candidate in ContinueButtonTexts)
        {
            if (string.Equals(text, candidate, StringComparison.OrdinalIgnoreCase))
            {
                t_foundButton = hWnd;
                return false;
            }
        }
        return true;
    }

    // ───────────────────── P/Invoke (source-generated) ─────────────────────

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumChildWindows(
        IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    private static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}