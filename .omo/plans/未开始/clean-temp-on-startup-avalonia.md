# CleanTempOnStartup 设置消费方（Avalonia 启动清理临时目录）

> **状态**: 🟡 UI 侧已实施（2026-09-27），Core 层覆盖仍待 `core-temp-root-injectable` | **创建**: 2026-08-20 | **优先级**: P2 | **预估工时**: 1-2h
> **来源**: [avalonia-wpf-diff-plan.md](avalonia-wpf-diff-plan.md) 待决策 #2

## 实施记录（2026-09-27）

UI 侧启动清理已随「设置项无消费者」批量修复落地（与 `settings-unwired-keys.md` 同批）：

- `App.axaml.cs` 新增 `CleanTempOnStartupCore()`（读 `AppSettings.Load().CleanTempOnStartup`，删除 `AppSettings.GetTempDir()`，失败仅 `DebugLog`），`OnFrameworkInitializationCompleted` 中 `_ = Task.Run(CleanTempOnStartupCore)` fire-and-forget（不阻塞启动）
- 与本计划方案的差异：用 `AppSettings.Load()`（Avalonia 无 `AppSettings.Instance`）、抽成独立方法并后台执行，其余语义一致
- **未覆盖**：Core 层 4 处 `%TEMP%\MantisZip` 硬编码（便携模式残留不在 `GetTempDir()` 范围内）→ 仍依赖 [core-temp-root-injectable.md](core-temp-root-injectable.md) 实施后一并清理
- 验证：构建 0 错误；手动验证（残留文件 → 启动删除 / 关开关 → 保留）待 GUI 测试

## 背景

`AppSettings.CleanTempOnStartup`（默认 `true`）在 Avalonia 设置界面「高级」Tab 有开关（`SettingsWindow.axaml:1164`），`SettingsWindowViewModel` 也读写该属性，但**启动时从不消费**——死设置，用户勾选无效。

WPF 有对应逻辑（`App.xaml.cs:141-152`）：启动时删除上次残留的临时目录（死机/崩溃后遗留的预览、拖拽、引擎重建临时文件）。

## 现状核实（2026-08-20）

| 侧 | 属性 | UI 开关 | 消费方 |
|----|------|---------|--------|
| WPF | `AppSettings.cs:118`（默认 true） | ✅ | ✅ `App.xaml.cs:141-152`（启动清理） |
| Avalonia | `AppSettings.cs:66`（默认 true） | ✅ `SettingsWindow.axaml:1164` | ❌ **无** |

## 方案

在 Avalonia `App.OnFrameworkInitializationCompleted` 启动早期（对齐 WPF 位置：日志初始化之后、主窗口创建之前）插入清理：

```csharp
// App.axaml.cs OnFrameworkInitializationCompleted 内
if (AppSettings.Instance.CleanTempOnStartup)
{
    try
    {
        var tempDir = AppSettings.GetTempDir(); // 2026-08-20 新增：便携 → DataDir/Temp，普通 → %TEMP%\MantisZip
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
            App.DebugLog($"OnStartup: cleaned temp dir: {tempDir}");
        }
    }
    catch (Exception ex)
    {
        App.DebugLog($"OnStartup: failed to clean temp dir: {ex.Message}");
    }
}
```

**关键点**：
- 复用已有 `AppSettings.GetTempDir()`（便携感知，与本计划配套的 `core-temp-root-injectable.md` 实施后 Core 层临时目录也纳入清理范围——先清理 UI 层，Core 注入后自动覆盖）
- 注意：`AppSettings.GetTempDir()` 调用 `Path.GetTempPath()` 或 `DataDir`，不会在清理前重新创建目录（`GetTempDir` 只返回路径不创建，创建在消费方）
- 清理失败仅记日志不阻断启动（对齐 WPF 行为，`catch (Exception startupCleanEx)`）
- 顺序：必须在任何引擎/预览使用临时目录**之前**（启动早期），否则可能删掉正在使用的文件
- 清理的对象是「上次残留」——本次启动后创建的新临时目录本次不删（下次启动时删）

## 涉及文件

- `MantisZip.UI.Avalonia/App.axaml.cs`（插入清理逻辑）

## 验证

- Avalonia 构建 0 错误
- 手动测试：在 `%TEMP%\MantisZip` 放一个残留文件 → 启动应用 → 文件被删除；关闭开关 → 启动 → 文件保留
- 便携模式：在 `DataDir/Temp` 放残留文件 → 启动 → 被删除

## 边界

- 不动 `AppSettings` 默认值（`true`，与 WPF 一致）
- 不做启动清理进度/UI（后台静默，失败仅日志）
- 不动 WPF（Avalonia-first，规则 11；WPF 已有实现）