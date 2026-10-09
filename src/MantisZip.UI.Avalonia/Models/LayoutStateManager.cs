using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 内容区布局快照持久化管理器。
/// 将目录树/文件列表列宽与预览面板各位置尺寸保存到 %LOCALAPPDATA%\MantisZip\layout.json
/// （便携模式：exe 旁 Data/layout.json）。
///
/// 与 WindowStateManager（关闭时自动保存）不同：本管理器【只在用户手动点击「保存布局」时写盘】，
/// 绝不随窗口关闭自动写，保证「调坏了 → 重新打开软件即可恢复上次保存的布局」的手动快照语义。
/// </summary>
internal static class LayoutStateManager
{
    private static readonly string BaseDir = AppSettings.DataDir;

    private static readonly string ConfigFile =
        Path.Combine(BaseDir, "layout.json");

    /// <summary>布局 JSON 选项：注册 GridLength 转换器（对象形态 {"value":N,"unit":N}；读取兼容旧版裸数字）。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new GridLengthJsonConverter() }
    };

    /// <summary>
    /// 从 JSON 加载上次保存的布局快照；无文件或损坏时返回 null。
    /// </summary>
    public static LayoutSnapshot? Load()
    {
        if (!File.Exists(ConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<LayoutSnapshot>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            App.DebugLog($"LayoutStateManager.Load: failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将布局快照序列化到 JSON（仅手动调用，不在窗口关闭时自动写）。
    /// </summary>
    public static void Save(LayoutSnapshot snapshot)
    {
        try
        {
            if (!Directory.Exists(BaseDir))
                Directory.CreateDirectory(BaseDir);

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception ex)
        {
            App.DebugLog($"LayoutStateManager.Save: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 布局快照。列宽为像素值（null 表示从未拖过、维持默认星号比例）；
    /// PreviewSizeByPosition 为预览面板各位置（1=底部, 2=目录树下方, 3=文件列表下方, 4=右侧）
    /// 的记忆尺寸，保留单元类型（Star=随窗口比例伸缩 / Pixel=固定宽高）。
    /// </summary>
    public sealed class LayoutSnapshot
    {
        public double? TreeColumnWidth { get; set; }
        public double? FileListColumnWidth { get; set; }
        public Dictionary<int, GridLength> PreviewSizeByPosition { get; set; } = new();
    }

    /// <summary>
    /// GridLength 的 JSON 转换器。写入对象形态 {"value":N,"unit":N}（unit: 0=Auto, 1=Pixel, 2=Star）；
    /// 读取同时兼容旧版 layout.json 的裸数字（视为 Pixel），保证旧快照可加载。
    /// </summary>
    private sealed class GridLengthJsonConverter : JsonConverter<GridLength>
    {
        public override GridLength Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // 旧版格式：裸像素值
            if (reader.TokenType == JsonTokenType.Number)
                return new GridLength(reader.GetDouble(), GridUnitType.Pixel);

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Unexpected token parsing GridLength: {reader.TokenType}");

            double value = 0;
            var unit = GridUnitType.Pixel;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                var property = reader.GetString();
                reader.Read();
                switch (property?.ToLowerInvariant())
                {
                    case "value":
                        value = reader.GetDouble();
                        break;
                    case "unit":
                        unit = (GridUnitType)reader.GetInt32();
                        break;
                }
            }
            return new GridLength(value, unit);
        }

        public override void Write(Utf8JsonWriter writer, GridLength value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", value.Value);
            writer.WriteNumber("unit", (int)value.GridUnitType);
            writer.WriteEndObject();
        }
    }
}