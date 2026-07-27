using System;
using System.IO;
using System.Text.Json;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 元数据面板配置的持久化管理。与 AppSettings 共享同一目录，独立文件。
/// </summary>
public static class MetadataSettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "metadata-panel.json");

    private static MetadataPanelSettings? _cached;

    /// <summary>元数据面板设置变更时触发。ViewModel 可订阅以刷新绑定。</summary>
    public static event Action? SettingsChanged;

    public static MetadataPanelSettings Load()
    {
        if (_cached != null) return _cached;

        try
        {
            if (!File.Exists(SettingsFile))
            {
                _cached = new MetadataPanelSettings();
                InitializeDefaultConfig(_cached);
                return _cached;
            }

            var json = File.ReadAllText(SettingsFile);
            _cached = JsonSerializer.Deserialize<MetadataPanelSettings>(json) ?? new MetadataPanelSettings();
            InitializeDefaultConfig(_cached);
            return _cached;
        }
        catch
        {
            _cached = new MetadataPanelSettings();
            InitializeDefaultConfig(_cached);
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
