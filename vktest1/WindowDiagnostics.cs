using System;
using System.Diagnostics;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

public static class WindowDiagnostics
{
    public static void DumpAllWindowsOfProcesses(string[] processNames)
    {
        Console.WriteLine($"\n=== ВСЕ ОКНА ПРОЦЕССОВ: {string.Join(", ", processNames)} ===");
        try
        {
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

            var pids = processNames
                .SelectMany(n => Process.GetProcessesByName(n))
                .Select(p => p.Id)
                .ToHashSet();

            foreach (var w in windows)
            {
                try
                {
                    int pid = w.Properties.ProcessId.Value;
                    if (!pids.Contains(pid)) continue;
                    Console.WriteLine($"PID={pid} | Title='{w.Name}' | Class='{w.ClassName}'");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
    }

    public static void DumpDeepWindowTree(string processName, int maxDepth = 5)
    {
        Console.WriteLine($"\n=== ГЛУБОКОЕ ДЕРЕВО ПРОЦЕССА {processName} ===");
        try
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0) { Console.WriteLine("Процесс не найден"); return; }

            var app = FlaUI.Core.Application.Attach(procs[0].Id);
            using var automation = new UIA3Automation();
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
            if (window == null) { Console.WriteLine("Главное окно не найдено"); return; }

            Console.WriteLine($"Главное окно: Title='{window.Name}', Class='{window.ClassName}', ControlType={window.ControlType}");

            // Явно приводим к AutomationElement
            FlaUI.Core.AutomationElements.AutomationElement rootElement = window;
            DumpElement(rootElement, 0, maxDepth);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
    }

    private static void DumpElement(FlaUI.Core.AutomationElements.AutomationElement el, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        string indent = new string(' ', depth * 2);
        try
        {
            Console.WriteLine($"{indent}[{el.ControlType}] Class='{el.ClassName}' Name='{el.Name}'");
            var children = el.FindAllChildren();
            foreach (var c in children)
                DumpElement(c, depth + 1, maxDepth);
        }
        catch { }
    }
}