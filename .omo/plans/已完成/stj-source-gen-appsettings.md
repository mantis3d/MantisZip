# AppSettings 序列化改用 System.Text.Json 源生成（source-gen）

> **状态**: ✅ 已完成（2026-10-20；门控未达但保留，详见实施结果） | **创建**: 2026-10-20 | **优先级**: P2 | **预估工时**: 1.5–2.5h
> **来源**: 启动优化三/四期收尾讨论——三期已消除 `AppSettings.Load()` 的重复读盘，但**首次调用**仍有 60–77ms 的「STJ 反射元数据构建」成本；`ThemeSpan` 余量主要归属此处（详见 `startup-appsettings-cache.md` 归档）。
> **目标**: 把 STJ 元数据构建移到编译期，削减冷启动首次 `Load()` 成本，并为「自包含体积优化」的 Trim/AOT 铺路。

## 背景

- `Models/AppSettings.cs:287` 使用 `JsonSerializer.Deserialize<AppSettings>(json)`（反射重载），`:326` 使用 `JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })`。
- `AppSettings` 含 6 个 `List<T>`、3 个嵌套自定义类型（`FormatDefinition` / `AdaptiveOverrideRule` / `FileFilterPreset`）、多个枚举，以及一个自定义转换器 `LegacyIntToSizeStringConverter`（`[JsonConverter]` 标注在 `SevenZipDictionarySize` 上）。
- 无 `object` / `Dictionary<string, object>` / 运行期多态字段——source-gen 完全支持。

## 方案

1. 新增 `src/MantisZip.UI.Avalonia/Models/AppSettingsJsonContext.cs`：

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext { }
```

2. `Load()` 改 `JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings)`。
3. `Save()` 改 `JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings)`（去掉每次新建 `JsonSerializerOptions` 的小开销）。
4. 保留 `LegacyIntToSizeStringConverter`（源生成器支持 `[JsonConverter]` 特性挂载，需无参构造，已满足）。

## 测试

新增 `tests/MantisZip.UI.Avalonia.Tests/AppSettingsJsonContextTests.cs`：

- **往返一致**：构造含嵌套列表/枚举/自定义转换器字段的实例 → source-gen 序列化 → 反序列化 → 逐字段相等。
- **旧格式兼容**：`SevenZipDictionarySize` 为旧 int 形式（如 `268435456`）的 JSON 经 context 反序列化仍得到 `"256m"`（转换器未被绕过）。
- **输出与反射路径一致**：同一实例分别用 context 与反射重载序列化，字符串逐字相等（锁定缩进/顺序/大小写行为）。
- **未知字段忽略**：含未来字段的 JSON 不抛异常。

## 验证

- `dotnet build -c Release src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`：0 警告 0 错误。
- `dotnet test -c Release tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`：无新增失败。
- **启动 A/B**（安静环境）：`tools/measure-startup.ps1` 各 n=5，对比 `Init.Settings` / `ThemeSpan`；门控 `Init.Settings` 中位下降 ≥15ms。不达标则保留改动（无害且利于 Trim），如实记录数据。
- 冒烟：改设置 → 保存 → 重启，值保留；旧 `settings.json`（含旧 int 字典大小）可正常加载。

## 边界

- 不改任何设置字段语义、不迁移文件格式、不改三语 key。
- 不动其他 `JsonSerializer` 调用点（本计划仅 AppSettings）。
- 不做 Trim 配置（归 `selfcontained-size-optimization`）。
- 若源生成器对某字段报 `SYSLIB1030`/`SYSLIB1034` 等不支持错误，则记录并回退该计划（一行回退到反射重载）。

## 实施结果（2026-10-20 回填）

**交付**
- 新增 `src/MantisZip.UI.Avalonia/Models/AppSettingsJsonContext.cs`（`[JsonSourceGenerationOptions(WriteIndented = true)]` + `[JsonSerializable(typeof(AppSettings))]`）。
- `Models/AppSettings.cs`：`Load()` 改 `JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings)`；`Save()` 改 `JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings)`（去掉每次新建 `JsonSerializerOptions`）。
- 测试：新增 `AppSettingsJsonContextTests` 4 条（嵌套集合/枚举往返、旧 int 字典大小 → `"256m"`、与反射路径输出逐字一致、未知字段忽略）全部通过；Avalonia 全量 **335 总 / 332 过 / 3 跳过 / 0 失败**。

**A/B 实测（交错 n=10，基线 = HEAD worktree 独立 Release 构建）**

| 指标（中位） | 基线（反射） | 改后（source-gen） | 差值 |
|---|---|---|---|
| `Init.Settings` 段 | 1.7ms | 1.7ms | 0 |
| TOTAL | 1420.1ms | 1413.5ms | −6.6ms（噪声级，轮 5/6 有明显负载尖峰） |

**门控结论：未达（要求 ≥15ms）**。本机 `settings.json` 体积小，STJ 对 `AppSettings` 的反射元数据构建成本远低于立项估计——`Init.Settings` 段本身只有 ~1.7ms；立项时引用的 60–77ms 含磁盘 IO/环境/JIT 等混合因素，不能归因于元数据构建。

**决策（按计划边界执行）**：**保留改动**——零语义变化、测试全绿，且为 `selfcontained-size-optimization` 的 Trim/AOT 铺路（反射解析器是修剪器的主要阻碍之一）；**不宣称启动收益**，不写入 RELEASE_NOTES。

**偏差**
- 未改其它 `JsonSerializer` 调用点；未做 Trim 配置；未改版本号。
- 归档时计划从「进行中」直接移入「已完成」——实施已完成，门控未达属**已记录结论**而非遗留工作。