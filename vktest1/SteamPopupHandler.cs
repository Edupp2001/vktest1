using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Автоматически закрывает диалог Steam "Запустить игру с пользовательскими параметрами",
/// который появляется при прямом запуске .exe игры с аргументами.
/// </summary>
public static class SteamPopupHandler
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint BM_CLICK = 0x00F5;

    private static readonly string[] WindowTitles = {
        "Запустить игру с пользовательскими параметрами",
        "Launch game with custom parameters",
        "启动游戏自定义参数",
        "Spiel mit benutzerdefinierten Parametern starten"
    };

    private static readonly string[] ContinueButtonTexts = {
        "Продолжить", "Continue", "继续", "Fortfahren", "OK"
    };

    public static async Task DismissAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var title in WindowTitles)
            {
                var hwnd = FindWindow(null, title);
                if (hwnd == IntPtr.Zero) continue;

                if (ClickContinueButton(hwnd))
                {
                    Log.Debug("[STEAM] Диалог подтверждения закрыт (fallback-режим)");
                    return;
                }
            }
            await Task.Delay(300);
        }

        // Сюда мы попадаем, только если за 20 секунд попап так и не нашёлся.
        // Это странно — значит, либо Steam повёл себя нестандартно,
        // либо пользователь вручную закрыл диалог до нас.
        Log.Warn($"[STEAM] Диалог не найден за {timeout.TotalSeconds:F0}с — возможен нестандартный сценарий");
    }

    private static bool ClickContinueButton(IntPtr parentHwnd)
    {
        IntPtr button = IntPtr.Zero;

        EnumChildWindows(parentHwnd, (hWnd, _) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            string text = sb.ToString();

            foreach (var candidate in ContinueButtonTexts)
            {
                if (text.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    button = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (button == IntPtr.Zero) return false;

        SendMessage(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return true;
    }
}