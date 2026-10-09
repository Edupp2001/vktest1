using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

public class MemoryFpsMonitor : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const long MODULE_BASE = 0x140000000L;
    private static readonly long[] FPS_OFFSETS = { 0x1BA02BE8L, 0x1B7164FCL };

    private class AttachedProcess
    {
        public int Pid;
        public IntPtr Handle;
        public bool HasValidFps;
    }

    private readonly string _processName;
    private readonly List<int> _samples = new();
    private readonly object _lock = new();
    private readonly List<AttachedProcess> _attached = new();

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private DateTime _startedAt;
    private DateTime? _stoppedAt;
    public int LastFps { get; private set; }
    public int SampleCount { get { lock (_lock) return _samples.Count; } }

    public MemoryFpsMonitor(string processName) => _processName = processName;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _startedAt = DateTime.UtcNow;
        _worker = Task.Run(() => Loop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _worker?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        _stoppedAt = DateTime.UtcNow;
    }

    private void RefreshAttached()
    {
        var alive = new HashSet<int>(Process.GetProcessesByName(_processName).Select(p => p.Id));

        for (int i = _attached.Count - 1; i >= 0; i--)
        {
            if (!alive.Contains(_attached[i].Pid))
            {
                try { CloseHandle(_attached[i].Handle); } catch { }
                _attached.RemoveAt(i);
            }
        }

        foreach (var proc in Process.GetProcessesByName(_processName))
        {
            if (_attached.Any(a => a.Pid == proc.Id)) continue;

            var h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, proc.Id);
            if (h == IntPtr.Zero) continue;

            long realBase = 0;
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

    private bool TryReadFps(out int fps)
    {
        fps = 0;
        foreach (var ap in _attached)
        {
            foreach (var offset in FPS_OFFSETS)
            {
                try
                {
                    byte[] buf = new byte[4];
                    if (!ReadProcessMemory(ap.Handle, (IntPtr)(MODULE_BASE + offset), buf, 4, out _))
                        continue;

                    float v = BitConverter.ToSingle(buf, 0);
                    if (v > 0 && v < 2000 && !float.IsNaN(v) && !float.IsInfinity(v))
                    {
                        ap.HasValidFps = true;
                        fps = (int)Math.Round(v);
                        return true;
                    }
                }
                catch { }
            }
        }
        return false;
    }

    private async Task Loop(CancellationToken ct)
    {
        int tick = 0;
        while (!ct.IsCancellationRequested)
        {
            if (tick % 40 == 0)
                RefreshAttached();

            try
            {
                if (TryReadFps(out int fps))
                {
                    lock (_lock) _samples.Add(fps);
                    LastFps = fps;
                }
            }
            catch { }

            if (tick % 100 == 0 && tick > 0)
            {
                var active = _attached.Where(a => a.HasValidFps).ToList();
                Log.Debug($"[MEM] t={tick / 20}s | процессов: {_attached.Count}, валидных: {active.Count}, сэмплов: {SampleCount}, FPS={LastFps}");
            }

            tick++;
            try { await Task.Delay(50, ct); } catch (TaskCanceledException) { break; }
        }
    }

    public MemoryFpsStats GetStats()
    {
        List<int> samples;
        lock (_lock) samples = _samples.ToList();

        if (samples.Count < 50)
            return new MemoryFpsStats { SampleCount = samples.Count, RawSampleCount = samples.Count };

        var sortedAll = samples.OrderBy(x => x).ToList();
        double median = sortedAll[sortedAll.Count / 2];
        double lo = median * 0.4;
        double hi = median * 2.5;
        var filtered = samples.Where(x => x >= lo && x <= hi).ToList();
        if (filtered.Count == 0) filtered = samples;

        var sorted = filtered.OrderBy(x => x).ToList();
        var end = _stoppedAt ?? DateTime.UtcNow;

        return new MemoryFpsStats
        {
            SampleCount = sorted.Count,
            RawSampleCount = samples.Count,
            OutliersRemoved = samples.Count - filtered.Count,
            Median = median,
            Avg = filtered.Average(),
            Min = sorted.First(),
            Max = sorted.Last(),
            P1 = sorted[Math.Max(0, (int)(sorted.Count * 0.01))],
            P5 = sorted[Math.Max(0, (int)(sorted.Count * 0.05))],
            Duration = end - _startedAt
        };
    }

    public void Dispose()
    {
        Stop();
        foreach (var ap in _attached)
        {
            try { CloseHandle(ap.Handle); } catch { }
        }
        _attached.Clear();
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