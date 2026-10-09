using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IniParser;
using IniParser.Model;

public class GameConfigManager
{
    private const string SectionName = "/Script/GSGameSettings.GSGameUserSettings";
    private const string ScalabilitySection = "ScalabilityGroups";
    private const string RayTracingSection = "RayTracing";

    private readonly string _configPath;
    private readonly string _backupPath;

    public GameConfigManager(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Путь к конфигу не задан", nameof(configPath));

        _configPath = configPath;
        _backupPath = configPath + ".backup";
    }

    public bool Backup()
    {
        if (!File.Exists(_configPath))
        {
            Log.Warn($"Backup: конфиг не найден ({_configPath})");
            return false;
        }
        File.Copy(_configPath, _backupPath, true);
        Log.Debug($"Бэкап конфига: {_backupPath}");
        return true;
    }

    public void Restore()
    {
        if (!File.Exists(_backupPath))
        {
            Log.Warn($"Restore: бэкап не найден ({_backupPath}) — конфиг не восстановлен");
            return;
        }
        File.Copy(_backupPath, _configPath, true);
        Log.Debug("Конфиг восстановлен из бэкапа.");
    }

    public void ApplyCpuSettings() => ApplyPreset(new Preset
    {
        ResolutionX = "1280",
        ResolutionY = "720",
        Scalability = new()
        {
            ["sg.ResolutionQuality"] = "50",
            ["sg.ShadowQuality"] = "0",
            ["sg.RayTracingQuality"] = "0",
            ["sg.GlobalIlluminationQuality"] = "0",
            ["sg.ReflectionQuality"] = "0",
            ["sg.PostProcessQuality"] = "0",
            ["sg.TextureQuality"] = "0",
            ["sg.EffectsQuality"] = "0",
            ["sg.FoliageQuality"] = "0",
        },
        RayTracingEnableInGame = "False",
        UiSettings = new()
        {
            ["QualityLevel"] = "0",
            ["Rtx"] = "0",
            ["RtxLevel"] = "0",
            ["Dlss"] = "0",
            ["ViewDistance"] = "0",
        },
        LogMessage = "Настройки для CPU-теста применены."
    });

    public void ApplyGpuSettings() => ApplyPreset(new Preset
    {
        ResolutionX = "3840",
        ResolutionY = "2160",
        Scalability = new()
        {
            ["sg.ResolutionQuality"] = "100",
            ["sg.ShadowQuality"] = "4",
            ["sg.RayTracingQuality"] = "3",
            ["sg.GlobalIlluminationQuality"] = "4",
            ["sg.ReflectionQuality"] = "4",
            ["sg.PostProcessQuality"] = "4",
            ["sg.TextureQuality"] = "4",
            ["sg.EffectsQuality"] = "4",
            ["sg.FoliageQuality"] = "4",
        },
        RayTracingEnableInGame = "True",
        UiSettings = new()
        {
            ["QualityLevel"] = "6",
            ["Rtx"] = "1",
            ["RtxLevel"] = "3",
            ["Dlss"] = "1",
            ["ViewDistance"] = "5",
        },
        LogMessage = "Настройки для GPU-теста применены."
    });

    private void ApplyPreset(Preset preset)
    {
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(_configPath);

        if (!data.Sections.ContainsSection(SectionName))
            throw new InvalidDataException(
                $"Секция '{SectionName}' не найдена в {_configPath}. " +
                "Проверь, что запущена именно игра Black Myth: Wukong Benchmark Tool " +
                "и файл GameUserSettings.ini не повреждён.");

        // 1. Разрешение
        data[SectionName]["ResolutionSizeX"] = preset.ResolutionX;
        data[SectionName]["ResolutionSizeY"] = preset.ResolutionY;

        // 2. Scalability groups
        foreach (var (key, value) in preset.Scalability)
            data[ScalabilitySection][key] = value;

        // 3. Ray tracing
        data[RayTracingSection]["r.RayTracing.EnableInGame"] = preset.RayTracingEnableInGame;

        // 4. UISettingData
        string? uiSettings = data[SectionName]["UISettingData"];
        if (string.IsNullOrEmpty(uiSettings))
            throw new InvalidDataException(
                $"UISettingData отсутствует или пуст в секции '{SectionName}'. " +
                "Проверь целостность GameUserSettings.ini.");

        foreach (var (key, value) in preset.UiSettings)
            uiSettings = ReplaceSetting(uiSettings, key, value);

        data[SectionName]["UISettingData"] = uiSettings;

        // Атомарная запись: пишем в .tmp, потом подменяем.
        var tmp = _configPath + ".tmp";
        parser.WriteFile(tmp, data);
        File.Replace(tmp, _configPath, destinationBackupFileName: null);

        Log.Debug(preset.LogMessage);
    }

    private static string ReplaceSetting(string input, string key, string value)
    {
        // Паттерн: ("KeyName", "любое_значение")
        string pattern = $@"\(""{Regex.Escape(key)}"", ""[^""]*""\)";
        string replacement = $"(\"{key}\", \"{value}\")";
        return Regex.Replace(input, pattern, replacement);
    }

    private sealed class Preset
    {
        public string ResolutionX { get; init; } = "";
        public string ResolutionY { get; init; } = "";
        public Dictionary<string, string> Scalability { get; init; } = new();
        public string RayTracingEnableInGame { get; init; } = "";
        public Dictionary<string, string> UiSettings { get; init; } = new();
        public string LogMessage { get; init; } = "";
    }
}