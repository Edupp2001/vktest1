using System.Text.Json;
using System.Text.Json.Serialization;

public class BenchmarkReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public SystemInfoDto System { get; set; } = new();
    public BenchmarkPair Cpu { get; set; } = new();
    public BenchmarkPair Gpu { get; set; } = new();
    public ValidationDto Validation { get; set; } = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() }
    };

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
        outputPath ??= Path.Combine(
            AppContext.BaseDirectory,
            $"benchmark_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(outputPath, JsonSerializer.Serialize(this, s_jsonOptions));
        return outputPath;
    }
}

/// <summary>
/// Сериализует DateTime как ISO 8601 UTC со суффиксом Z, чтобы читатель
/// однозначно понимал, что это UTC (иначе теряется Kind).
/// </summary>
internal sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToUniversalTime().ToString("o"));
}