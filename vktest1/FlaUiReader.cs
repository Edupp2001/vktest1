using System;
using System.IO;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

public class FlaUiReader
{
    public static double GetFpsFromScreen(string processName)
    {
        // ЯВНО указываем FlaUI.Core.Application, чтобы не путать с System.Windows.Forms.Application
        var app = FlaUI.Core.Application.Attach(processName);

        using (var automation = new UIA3Automation())
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
            if (window == null) throw new Exception("Окно игры не найдено");

            var textElements = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));

            foreach (var element in textElements)
            {
                string text = element.Name;
                if (string.IsNullOrEmpty(text)) continue;

                if (text.Contains("FPS", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Average", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Найден текст: {text}");

                    var match = Regex.Match(text, @"(\d+[\.,]?\d*)");
                    if (match.Success)
                    {
                        string raw = match.Groups[1].Value.Replace('.', ',');
                        return double.Parse(raw);
                    }
                }
            }
        }
        throw new Exception("Не удалось найти FPS на экране");
    }
}