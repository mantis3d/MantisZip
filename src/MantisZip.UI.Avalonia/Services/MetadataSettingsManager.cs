using System;
using System.IO;
using System.Text.Json;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 元数据面板配置的持久化管理。与 AppSettings 共享同一目录（便携模式：exe 旁 Data/），独立文件。
/// </summary>
public static class MetadataSettingsManager
{
    private static readonly string SettingsDir = AppSettings.DataDir;
    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "metadata-panel.json");

    private static MetadataPanelSettings? _cached;

    /// <summary>元数据面板设置变更时触发。ViewModel 可订阅以刷新绑定。</summary>
    public static event Action? SettingsChanged;

    /// <summary>
    /// 出厂默认模板的嵌入资源名（见 csproj 的 <c>LogicalName</c>）。
    /// 内容 = 2026-11-08 用户实际配置（horizontal 字段布局 + 各类型字段的位置/顺序/行号）。
    /// </summary>
    internal const string DefaultTemplateResourceName =
        "MantisZip.UI.Avalonia.Resources.Defaults.metadata-panel.json";

    public static MetadataPanelSettings Load()
    {
        if (_cached != null) return _cached;

        try
        {
            if (!File.Exists(SettingsFile))
            {
                _cached = CreateDefault();
                return _cached;
            }

            var json = File.ReadAllText(SettingsFile);
            _cached = JsonSerializer.Deserialize<MetadataPanelSettings>(json, JsonDefaults.CaseInsensitive) ?? new MetadataPanelSettings();
            InitializeDefaultConfig(_cached);
            return _cached;
        }
        catch
        {
            _cached = CreateDefault();
            return _cached;
        }
    }

    public static bool Save(MetadataPanelSettings settings)
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            _cached = settings;
            SettingsChanged?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ResetCache()
    {
        _cached = null;
    }

    /// <summary>
    /// 创建出厂默认配置：读内置模板（<see cref="DefaultTemplateResourceName"/>）再补齐注册表里
    /// 新增类型的字段。模板缺失/损坏时回落代码生成的最小默认值，保证功能不因此不可用。
    /// </summary>
    internal static MetadataPanelSettings CreateDefault()
    {
        var settings = LoadDefaultTemplate();
        InitializeDefaultConfig(settings);
        return settings;
    }

    private static MetadataPanelSettings LoadDefaultTemplate()
    {
        try
        {
            using var stream = typeof(MetadataSettingsManager).Assembly
                .GetManifestResourceStream(DefaultTemplateResourceName);
            if (stream is null)
            {
                return new MetadataPanelSettings();
            }

            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<MetadataPanelSettings>(reader.ReadToEnd(), JsonDefaults.CaseInsensitive)
                   ?? new MetadataPanelSettings();
        }
        catch
        {
            return new MetadataPanelSettings();
        }
    }

    /// <summary>
    /// 确保所有注册的类型都有默认配置。
    /// 新增的元数据类型会自动获得默认 FieldConfig。
    /// </summary>
    private static void InitializeDefaultConfig(MetadataPanelSettings settings)
    {
        var registry = MetadataRegistry.GetAllTypeKeys();

        foreach (var typeKey in registry)
        {
            if (!settings.Types.TryGetValue(typeKey, out var typeConfig))
            {
                typeConfig = new TypeMetadataConfig();
                settings.Types[typeKey] = typeConfig;
            }

            var registeredFields = MetadataRegistry.GetFields(typeKey);
            foreach (var fieldDef in registeredFields)
            {
                if (!typeConfig.Fields.ContainsKey(fieldDef.Key))
                {
                    typeConfig.Fields[fieldDef.Key] = new FieldConfig
                    {
                        Position = typeKey == "common" ? "infoPanel" : "infoPanel",
                        Order = registeredFields.Length * 10
                    };
                }
            }
        }
    }
}
