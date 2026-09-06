# Core 层临时目录根可注入（便携模式延伸）

> **状态**: 📋 待实施 | **创建**: 2026-08-20 | **优先级**: P2 | **预估工时**: 2-3h
> **来源**: [avalonia-wpf-diff-plan.md](avalonia-wpf-diff-plan.md) 待决策 #1

## 背景

便携模式（`Portable.txt` → exe 旁 `Data/`）已把 UI 层全部路径（settings/password/recent/window/metadata/log/temp）重定向到 `DataDir`。但 **Core 层仍有 6 处 `%TEMP%\MantisZip` 硬编码**，便携模式下共享层临时文件仍写系统 TEMP，与「便携 = 不污染系统」的目标不符。

WPF 同样存在此问题（`MainWindow.Preview.cs:168` 的 `GetTempDir()` 只覆盖 UI 层），因此**不属 WPF↔Avalonia diff 差异**，作为独立缺口立项。

## 现状（6 处硬编码）

| # | 文件 | 行 | 用途 |
|---|------|-----|------|
| 1 | `Core/Engines/ZipEngine.cs` | 1017 | `Rebuild` — 复制模式重建临时目录 |
| 2 | `Core/Engines/ZipEngine.cs` | 1390 | `DeleteTemp` — 删除条目前暂存 |
| 3 | `Core/Engines/SevenZipEngine.cs` | 842 | `DeleteTemp` — 删除条目前暂存 |
| 4 | `Core/Engines/SevenZipEngine.cs` | 893 | `DeleteTemp` — 删除条目暂存归档 |
| 5 | `Core/Utils/ArchiveEntryExtractor.cs` | 343 | `HeadExtract` — 元数据优先预览临时提取 |
| 6 | `Core/Utils/FontParser.cs` | 201 | `Fonts` — 字体预览临时目录 |

统一模式：`Path.Combine(Path.GetTempPath(), "MantisZip", "<SubDir>", Guid...)`。

## 方案

Core 是框架无关类库（WPF + Avalonia 共用），不能引用 UI 层 `AppSettings`。参考既有 `CoreLog.RedactOverride` 模式（`Core/Logging` 的 `internal Func<string,string>?` 由 UI 启动时注入），新增**可注入临时目录根**：

### 方案 A（推荐）：`TempPaths` 静态类 + 注入

```csharp
// Core/Utils/TempPaths.cs
namespace MantisZip.Core.Utils;

public static class TempPaths
{
    /// <summary>UI 层注入的临时目录根（便携模式 → DataDir/Temp；普通 → %TEMP%\MantisZip）。null = 默认 Path.GetTempPath()\MantisZip</summary>
    public static Func<string>? TempRootOverride { get; set; }

    public static string GetTempDir(string subDir) =>
        Path.Combine(TempRootOverride?.Invoke() ?? Path.Combine(Path.GetTempPath(), "MantisZip"), subDir);
}
```

- **6 处替换**：`Path.Combine(Path.GetTempPath(), "MantisZip", sub, ...)` → `Path.Combine(TempPaths.GetTempDir(sub), ...)`
- **UI 注入点**：
  - Avalonia：`App.OnFrameworkInitializationCompleted` → `TempPaths.TempRootOverride = () => AppSettings.GetTempDir()`（已有 `GetTempDir()`，2026-08-20 新增）
  - WPF：`App.OnStartup` → `TempPaths.TempRootOverride = () => GetTempDir()`（`MainWindow.Preview.cs:168` 已有，WPF 侧同步收益：修正 WPF 便携模式同样写系统 TEMP 的问题）
- **默认行为不变**：未注入时路径与现状逐字节一致（`%TEMP%\MantisZip\...`）
- 注意：`TempRootOverride` 是 `Func<string>` 而非 `string`，因为便携模式静态初始化时机在 UI 启动早期，惰性求值避免顺序依赖

### 方案 B（不推荐）：每引擎构造函数注入

给 `ZipEngine`/`SevenZipEngine`/`ArchiveEntryExtractor`/`FontParser` 加构造参数或属性。改动面大（工厂/调用链全要改），且静态注入已足够（临时目录是进程级概念，无多实例差异化需求）。

## 涉及文件

- `Core/Utils/TempPaths.cs`（**新增**）
- `Core/Engines/ZipEngine.cs`（2 处）
- `Core/Engines/SevenZipEngine.cs`（2 处）
- `Core/Utils/ArchiveEntryExtractor.cs`（1 处）
- `Core/Utils/FontParser.cs`（1 处）
- `MantisZip.UI.Avalonia/App.axaml.cs`（注入）
- `MantisZip.UI/App.OnStartup` 或 `MainWindow.Preview.cs`（注入，WPF 同步收益，可选）

## 验证

- Core 测试全过（`dotnet test tests\MantisZip.Tests`）
- Avalonia 构建 0 错误
- 便携模式实测：打开带字体/元数据预览的压缩包后，`Data/Temp` 出现 `HeadExtract`/`Fonts` 子目录而非系统 TEMP
- 普通模式实测：路径仍为 `%TEMP%\MantisZip\...`（与改动前逐字节一致）

## 边界

- 不动 `AppSettings` 本身（Core 不引用 UI 类型）
- 不动 `LogRedactor` 等其他已注入机制
- 临时目录**创建**逻辑（`Directory.CreateDirectory`）留在各调用点，不集中