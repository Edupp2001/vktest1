using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public class BenchmarkReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public SystemInfoDto System { get; set; } = new();
    public BenchmarkPair Cpu { get; set; } = new();
    public BenchmarkPair Gpu { get; set; } = new();
    public ValidationDto Validation { get; set; } = new();

    public class SystemInfoDto
    {
        public string Cpu { get; set; } = "";
        public string Gpu { get; set; } = "";
        public string GpuDriver { get; set; } = "";
        public string Ram { get; set; } = "";
        public string VideoMem { get; set; } = "";
    }

    public class BenchmarkPair
    {
        public BenchmarkResult Json { get; set; } = new();
        public MemoryFpsStats Memory { get; set; } = new();
    }

    public class ValidationDto
    {
        public double CpuAvgDelta { get; set; }
        public double CpuP5Delta { get; set; }
        public double GpuAvgDelta { get; set; }
        public double GpuP5Delta { get; set; }
    }

    public string SaveToFile(string? outputPath = null)
    {
        outputPath ??= Path.Combine(AppContext.BaseDirectory,
            $"benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(this, options));
        return outputPath;
    }
}