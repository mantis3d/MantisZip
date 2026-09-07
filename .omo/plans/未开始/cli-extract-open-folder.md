# Avalonia CLI `--extract-here` / `--extract-to-name` 打开文件夹对齐

> **状态**: 📋 待实施 | **创建**: 2026-08-20 | **优先级**: P2 | **预估工时**: 2-3h
> **来源**: [avalonia-wpf-diff-plan.md](avalonia-wpf-diff-plan.md) 待决策 #3

## 背景

WPF CLI `--extract-here` / `--extract-to-name` 批处理解压成功后**会打开资源管理器到实际解压位置**（`App.Extract.cs:614-619`）：单文件模式且 `OpenFolderAfterExtract` 开启时，经 `ResolveSmartOpenPathAsync` 智能判断公共根目录后 `OpenInExplorerStatic`。

Avalonia 对应 CLI 流程（`App.axaml.cs` `RunCliDirectExtractBatchAsync`，多文件批处理）**只解压不打开文件夹**——行为不一致。

> 注：应用内 `ExtractArchiveHere`/`ExtractArchiveToName` 两项目均不开文件夹（已核对一致），本计划仅对齐 **CLI 路径**。

## 现状核实（2026-08-20）

| 路径 | WPF | Avalonia |
|------|-----|---------|
| CLI `--extract-here`（单文件） | 走 `RunExtractCliAsync` 共用流程 | `RunExtractCliAsync`（`App.axaml.cs:220`）——解压后 shutdown，不打开 |
| CLI `--extract-here`（多文件批处理） | `App.Extract.cs:614-619` 解压完成后智能打开 | `RunCliDirectExtractBatchAsync`（mode=`here`）——只解压不打开 |
| CLI `--extract-to-name`（单文件） | 同上 | `RunExtractCliAsync`（:238）——不打开 |
| CLI `--extract-to-name`（多文件） | 同上 | `RunCliDirectExtractBatchAsync`（mode=`toname`）——不打开 |
| CLI `--extract`（弹窗） | WPF 打开？需确认 | Avalonia `RunExtractDialogCliAsync`——不打开 |
| CLI `--extract-smart` | WPF 打开？需确认 | Avalonia `RunExtractSmartCliAsync`——不打开 |

> ⚠ **实施前需确认**：WPF 的 `--extract`（弹窗模式）和 `--extract-smart` 是否也打开文件夹。`App.Extract.cs` 的打开逻辑位于批处理完成后的 `else` 分支（单文件模式 `allPaths.Count == 1`），`--extract-smart` 是否走同一 `HandleExtractBatch` 需读 WPF 源码确认——若 WPF 不开，则 Avalonia 对齐时也只改 `--extract-here`/`--extract-to-name`。

## 方案

对齐 WPF 语义（**仅单文件模式 + `OpenFolderAfterExtract` 开启时**打开，多文件批处理不打开——避免连续弹多个资源管理器窗口）：

### Avalonia 侧

1. **`RunExtractCliAsync`**（单文件）：解压成功后，若 `settings.OpenFolderAfterExtract` 且解压成功，调用 `SmartOpenPathResolver.ResolveSmartOpenPathAsync(archivePath, targetDir, password)` 然后打开文件夹。
   - 需要拿到解压使用的 password（`TryExtractArchiveAsync` 返回解压结果）——需读 `TryExtractArchiveAsync` 确认返回值是否含密码/成功标志
   - 打开方式：WPF 用 `OpenInExplorerStatic`，Avalonia 用 `Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"" })`（对齐 `MainWindowViewModel.OpenExtractedFolderAsync` 的既有实现）
2. **`RunCliDirectExtractBatchAsync`**：按 WPF 语义**不打开**（多文件批处理），保持现状——需在文档中明确此边界
3. 若 `--extract-smart`/`--extract` 确认 WPF 打开，则对齐时一并处理（取决于 WPF 源码核实结果）

### 与既有基础的关系

- `SmartOpenPathResolver`（2026-08-20 新增）已是 3 处 UI 接线的公共 helper，CLI 接线复用同一 helper，保证语义统一
- `AppSettings.OpenFolderAfterExtract` 设置已存在（Avalonia AppSettings 有该字段）
- 打开资源管理器的方式与 `MainWindowViewModel.OpenExtractedFolderAsync` 完全一致（`explorer.exe` + 引号路径）

## 涉及文件

- `MantisZip.UI.Avalonia/App.axaml.cs`（`RunExtractCliAsync` + 可能 `RunCliDirectExtractBatchAsync` 边界注释）
- 不改 WPF（Avalonia-first，规则 11）

## 验证

- Avalonia 构建 0 错误
- 手动测试：`--extract-here` 单文件解压一个 `my_project/` 内含文件的 zip → 打开 `dest/my_project/`（公共根）；解压无公共根文件 → 打开 `dest/`
- 开关关闭（`OpenFolderAfterExtract=false`）→ 不打开
- 多文件批处理 → 不打开
- 加密包：解压成功后 `ResolveSmartOpenPathAsync` 同密码 `ListEntriesAsync` 必然成功

## 边界

- 仅 CLI 路径；应用内 `ExtractArchiveHere`/`ExtractArchiveToName` 保持不开（已核对 WPF 一致）
- 不做 CLI `--compress` 侧任何变更
- 多文件批处理保持不开文件夹（与 WPF `allPaths.Count == 1` 条件对齐）