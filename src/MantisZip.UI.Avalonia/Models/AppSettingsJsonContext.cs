using System.Text.Json.Serialization;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// AppSettings 的 System.Text.Json 源生成上下文（stj-source-gen-appsettings）：
/// 编译期生成序列化元数据，避免冷启动首次 <see cref="AppSettings.Load"/> 的反射元数据构建开销，
/// 并为后续 Trim/AOT（自包含体积优化）铺路。
/// <c>WriteIndented</c> 与既有 settings.json 格式保持一致。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
