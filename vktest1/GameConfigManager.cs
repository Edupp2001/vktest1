using IniParser;
using IniParser.Model;
using System.Text.RegularExpressions;
using System;
using System.IO;
public class GameConfigManager
{
    private readonly string _configPath;
    private readonly string _backupPath;

    // ПРАВИЛЬНОЕ имя секции (с ведущим слэшем!)
    private const string SectionName = "/Script/GSGameSettings.GSGameUserSettings";

    public GameConfigManager(string configPath)
    {
        _configPath = configPath;
        _backupPath = configPath + ".backup";
    }

    public void Backup()
    {
        if (File.Exists(_configPath))
            File.Copy(_configPath, _backupPath, true);
    }

    public void Restore()
    {
        if (File.Exists(_backupPath))
            File.Copy(_backupPath, _configPath, true);
    }

    private string ReplaceSetting(string input, string key, string value)
    {
        if (string.IsNullOrEmpty(input))
            throw new Exception($"Не удалось прочитать UISettingData. Проверь имя секции в INI-файле!");

        // Паттерн: ("KeyName", "любое_значение")
        string pattern = $@"\(""{Regex.Escape(key)}"", ""[^""]*""\)";
        string replacement = $"(\"{key}\", \"{value}\")";
        return Regex.Replace(input, pattern, replacement);
    }

    public void ApplyCpuSettings()
    {
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(_configPath);

        // 1. Понижаем разрешение
        data[SectionName]["ResolutionSizeX"] = "1280";
        data[SectionName]["ResolutionSizeY"] = "720";

        // 2. Выключаем тяжелую графику
        data["ScalabilityGroups"]["sg.ResolutionQuality"] = "50";
        data["ScalabilityGroups"]["sg.ShadowQuality"] = "0";
        data["ScalabilityGroups"]["sg.RayTracingQuality"] = "0";
        data["ScalabilityGroups"]["sg.GlobalIlluminationQuality"] = "0";
        data["ScalabilityGroups"]["sg.ReflectionQuality"] = "0";
        data["ScalabilityGroups"]["sg.PostProcessQuality"] = "0";
        data["ScalabilityGroups"]["sg.TextureQuality"] = "0";
        data["ScalabilityGroups"]["sg.EffectsQuality"] = "0";
        data["ScalabilityGroups"]["sg.FoliageQuality"] = "0";

        // 3. Выключаем RTX
        data["RayTracing"]["r.RayTracing.EnableInGame"] = "False";

        // 4. Правим UISettingData
        string uiSettings = data[SectionName]["UISettingData"];
        uiSettings = ReplaceSetting(uiSettings, "QualityLevel", "0");
        uiSettings = ReplaceSetting(uiSettings, "Rtx", "0");
        uiSettings = ReplaceSetting(uiSettings, "RtxLevel", "0");
        uiSettings = ReplaceSetting(uiSettings, "Dlss", "0");
        uiSettings = ReplaceSetting(uiSettings, "ViewDistance", "0");
        data[SectionName]["UISettingData"] = uiSettings;

        parser.WriteFile(_configPath, data);
        Log.Debug("Настройки для CPU-теста применены.");
        
    }

    public void ApplyGpuSettings()
    {
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(_configPath);

        data[SectionName]["ResolutionSizeX"] = "3840";
        data[SectionName]["ResolutionSizeY"] = "2160";

        data["ScalabilityGroups"]["sg.ResolutionQuality"] = "100";
        data["ScalabilityGroups"]["sg.ShadowQuality"] = "4";
        data["ScalabilityGroups"]["sg.RayTracingQuality"] = "3";
        data["ScalabilityGroups"]["sg.GlobalIlluminationQuality"] = "4";
        data["ScalabilityGroups"]["sg.ReflectionQuality"] = "4";
        data["ScalabilityGroups"]["sg.PostProcessQuality"] = "4";
        data["ScalabilityGroups"]["sg.TextureQuality"] = "4";
        data["ScalabilityGroups"]["sg.EffectsQuality"] = "4";
        data["ScalabilityGroups"]["sg.FoliageQuality"] = "4";

        data["RayTracing"]["r.RayTracing.EnableInGame"] = "True";

        string uiSettings = data[SectionName]["UISettingData"];
        uiSettings = ReplaceSetting(uiSettings, "QualityLevel", "6");
        uiSettings = ReplaceSetting(uiSettings, "Rtx", "1");
        uiSettings = ReplaceSetting(uiSettings, "RtxLevel", "3");
        uiSettings = ReplaceSetting(uiSettings, "Dlss", "1");
        uiSettings = ReplaceSetting(uiSettings, "ViewDistance", "5");
        data[SectionName]["UISettingData"] = uiSettings;

        parser.WriteFile(_configPath, data);
        Log.Debug("Настройки для GPU-теста применены.");
    }
}