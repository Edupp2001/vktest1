using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

public sealed class MemoryFpsMonitor : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    // Эти адреса привязаны к конкретной версии b1-Win64-Shipping.exe. 
    // При обновлении игры их нужно пересчитывать. 
    // Считаю, что benchmark tool более обновляться не будет
    private const long MODULE_BASE = 0x140000000L;
    private static readonly long[] FPS_OFFSETS = { 0x1BA02BE8L, 0x1B7164FCL };

    private sealed class AttachedProcess
    {
        public int Pid;
        public IntPtr Handle;
        public bool HasValidFps;
    }

    private readonly string _processName;
    private readonly List<int> _samples = new();
    private readonly object _lock = new();
    private readonly List<AttachedProcess> _attached = new();
    private readonly Stopwatch _watch = new();

    private CancellationTokenSource? _cts;
    private Task? _worker;

    private volatile int _lastFps;
    public int LastFps => _lastFps;

    public int SampleCount { get { lock (_lock) return _samples.Count; } }

    [ThreadStatic] private static byte[]? t_readBuffer;

    public MemoryFpsMonitor(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Имя процесса не задано", nameof(processName));
        _processName = processName;
    }

    public void Start()
    {
        if (_cts != null) throw new InvalidOperationException("Монитор уже запущен");

        _cts = new CancellationTokenSource();
        _watch.Restart();
        _worker = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _worker?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException ex) { Log.Debug($"[MEM] Worker завершился с ошибкой: {ex.InnerException?.Message}"); }
        catch (Exception ex) { Log.Debug($"[MEM] Worker wait failed: {ex.Message}"); }
        _watch.Stop();
    }

    private void RefreshAttached()
    {
        var processes = Process.GetProcessesByName(_processName);
        try
        {
            var aliveIds = new HashSet<int>(processes.Select(p => p.Id));

            for (int i = _attached.Count - 1; i >= 0; i--)
            {
                if (!aliveIds.Contains(_attached[i].Pid))
                {
                    try { CloseHandle(_attached[i].Handle); } catch { }
                    _attached.RemoveAt(i);
                }
            }

            foreach (var proc in processes)
            {
                if (_attached.Any(a => a.Pid == proc.Id)) continue;

                var h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, proc.Id);
                if (h == IntPtr.Zero) continue;

                long realBase;
                try { realBase = proc.MainModule!.BaseAddress.ToInt64(); }
                catch { CloseHandle(h); continue; }

                if (realBase != MODULE_BASE)
                {
                    CloseHandle(h);
                    continue;
                }

                Log.Debug($"[MEM] Прицеплен к PID={proc.Id} (Base=0x{realBase:X})");
                _attached.Add(new AttachedProcess { Pid = proc.Id, Handle = h });
            }
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private bool TryReadFps(out int fps)
    {
        fps = 0;
        var buf = t_readBuffer ??= new byte[4];

        foreach (var ap in _attached)
        {
            foreach (var offset in FPS_OFFSETS)
            {
                try
                {
                    if (!ReadProcessMemory(ap.Handle, (IntPtr)(MODULE_BASE + offset), buf, 4, out var read))
                        continue;
                    if (read.ToInt64() != 4) continue;

                    float v = BitConverter.ToSingle(buf, 0);

                    if (v >= 5 && v < 2000 && !float.IsNaN(v) && !float.IsInfinity(v))
                    {
                        ap.HasValidFps = true;
                        fps = (int)Math.Round(v);
                        return true;
                    }
                }
                catch { /* чтение упало — попробуем следующий оффсет/процесс */ }
            }
        }
        return false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        int tick = 0;
        while (!ct.IsCancellationRequested)
        {
            if (tick % 40 == 0) RefreshAttached();

            try
            {
                if (TryReadFps(out int fps))
                {
                    lock (_lock) _samples.Add(fps);
                    _lastFps = fps;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[MEM] TryReadFps failed: {ex.Message}");
            }

            if (tick % 100 == 0 && tick > 0)
            {
                int active = _attached.Count(a => a.HasValidFps);
                Log.Debug($"[MEM] t={tick / 20}s | процессов: {_attached.Count}, валидных: {active}, сэмплов: {SampleCount}, FPS={LastFps}");
            }

            tick++;
            try { await Task.Delay(50, ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
        }
    }

    public MemoryFpsStats GetStats()
    {
        List<int> samples;
        lock (_lock) samples = _samples.ToList();

        if (samples.Count < 50)
            return new MemoryFpsStats { SampleCount = samples.Count, RawSampleCount = samples.Count };

        int tailSize = Math.Max(20, samples.Count / 20);
        var tail = samples.Skip(samples.Count - tailSize).OrderBy(x => x).ToList();
        double reference = tail[tail.Count / 2];

        double lo = reference * 0.5;
        double hi = reference * 1.5;
        var filtered = samples.Where(x => x >= lo && x <= hi).ToList();

        if (filtered.Count < 20) filtered = samples;

        var sorted = filtered.OrderBy(x => x).ToList();
        var sortedAll = samples.OrderBy(x => x).ToList();

        return new MemoryFpsStats
        {
            SampleCount = sorted.Count,
            RawSampleCount = samples.Count,
            OutliersRemoved = samples.Count - filtered.Count,
            Median = sortedAll[sortedAll.Count / 2],
            Avg = filtered.Average(),
            Min = sorted.First(),
            Max = sorted.Last(),
            P1 = Percentile(sorted, 0.01),
            P5 = Percentile(sorted, 0.05),
            Duration = _watch.Elapsed
        };
    }

    private static int Percentile(List<int> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int idx = (int)Math.Floor((sorted.Count - 1) * p);
        if (idx < 0) idx = 0;
        if (idx >= sorted.Count) idx = sorted.Count - 1;
        return sorted[idx];
    }

    public void Dispose()
    {
        Stop();
        foreach (var ap in _attached)
        {
            try { CloseHandle(ap.Handle); } catch { }
        }
        _attached.Clear();
        _cts?.Dispose();
        _cts = null;
    }
}

public class MemoryFpsStats
{
    public int SampleCount { get; set; }
    public int RawSampleCount { get; set; }
    public int OutliersRemoved { get; set; }
    public double Median { get; set; }
    public double Avg { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int P1 { get; set; }
    public int P5 { get; set; }
    public TimeSpan Duration { get; set; }

    public override string ToString() =>
        $"Avg={Avg:F1} | Min={Min} | Max={Max} | P1={P1} | P5={P5} | Median={Median:F0} " +
        $"| Useful={SampleCount}/{RawSampleCount} (outliers:{OutliersRemoved}) " +
        $"| Dur={Duration.TotalSeconds:F1}s";
}