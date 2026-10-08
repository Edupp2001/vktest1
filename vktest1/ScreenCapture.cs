using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FlaUI.UIA3;

public static class ScreenCapture
{
    public static string CaptureGameWindow(string processName)
    {
        var procs = System.Diagnostics.Process.GetProcessesByName(processName);
        if (procs.Length == 0) throw new Exception($"Процесс {processName} не найден");

        var app = FlaUI.Core.Application.Attach(procs[0].Id);
        using var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
        if (window == null) throw new Exception("Окно не найдено");

        var rect = window.BoundingRectangle;
        Console.WriteLine($"Окно: X={rect.X}, Y={rect.Y}, W={rect.Width}, H={rect.Height}");

        var bitmap = new Bitmap(rect.Width, rect.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(rect.X, rect.Y, 0, 0, new Size(rect.Width, rect.Height));
        }

        string path = Path.Combine(AppContext.BaseDirectory, $"screenshot_{DateTime.Now:HHmmss}.png");
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"Скриншот сохранён: {path}");
        return path;
    }
}