# Avalonia WPF 功能差异补齐计划

> **For agentic workers:** Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** 对比 `main`（WPF）与 `AvaloniaFromWpf`（Avalonia）分支的功能差异，列出 Avalonia 需要补齐的缺失功能并提供优先级排序。

**背景:** `AvaloniaFromWpf` 已合并 `main` 所有提交（merge-base: `ddaadc9`），Core 层共享，差异仅在 UI 层。当前 Avalonia 项目已完成 Phase 0-10 + UI Feature Parity + i18n Cleanup，剩余 Shell/COM 集成未开始。

**核对日期:** 2026-07-15 | **版本:** v0.4.5

---

## 汇总

| 缺失类别 | 缺失项数 | 优先级分布 |
|----------|---------|-----------|
| AppSettings 属性 | 21 个 | P0×9, P1×12 |
| 对话框/控件 | 1 个 | P1 |
| 功能逻辑 | 8 项 | P0×3, P1×3, P2×2 |
| Shell/COM 集成 | 整块 | P0 |
| 总工作量预估 | — | 约 3-5 天 |

---

## P0 — 核心功能缺失（阻塞性）

### P0-1: Shell/COM 集成

**现状:** `HandleShellCommand` 委托给 WPF exe 执行（`FindWpfExe()`），Avalonia 自身无 ShellIntegration。`MantisZip.ShellExt` 未在 csproj 中引用，无 COM host 部署，MenuIcons 目录为空。

**WPF 源文件:**
- `src/MantisZip.UI/Shell/ShellIntegration.cs` — 基础类
- `src/MantisZip.UI/Shell/ShellIntegration.Assoc.cs` — 文件关联
- `src/MantisZip.UI/Shell/ShellIntegration.Menu.cs` — 右键菜单
- `src/MantisZip.UI/Resources/MenuIcons/*.ico` — 11 个图标

**Avalonia 已有计划:** `.sisyphus/plans/avalonia-shell-com-integration.md`（📋 Planned，未启动）

**任务:**
- [ ] `MantisZip.UI.Avalonia.csproj` 添加 `ProjectReference` 到 `MantisZip.ShellExt` + COM host 部署 target
- [ ] 移植 `ShellIntegration.cs`（注册表操作部分直接移植）
- [ ] 移植 `ShellIntegration.Assoc.cs`（InstallAssociations/UninstallAssociations）
- [ ] 移植 `ShellIntegration.Menu.cs`（动词注册逻辑 + CommandFlags=8 已知问题修复）
- [ ] 从 WPF 复制 MenuIcons ICO 文件
- [ ] App.axaml.cs 补全 `--install-assoc`/`--uninstall-assoc` 实际逻辑（当前委托给 WPF exe）
- [ ] 验证构建 + COM host 输出

---

### P0-2: 压缩选项增强（7z/ZIP 高级参数）

**现状:** WPF 支持 7z 字典/固实块/匹配器/Word Size/ZIP 方法/加密方式 等高级选项。Avalonia AppSettings 缺少 8 个压缩相关属性，`DynamicFormatOptionsPanel` 已存在但缺少对应设置绑定。

**缺失 AppSettings 属性:**
| 属性 | 类型 | 默认值 | WPF 用途 |
|------|------|--------|----------|
| `SevenZipSolidBlockSize` | string | `"global"` | 7z 固实块大小 |
| `SevenZipDictionarySize` | string | `"default"` | 7z 字典大小 |
| `SevenZipNumFastBytes` | string | `"default"` | 7z Word Size |
| `SevenZipMatchFinder` | string | `"default"` | 7z 匹配器 |
| `SevenZipEncryptHeaders` | bool | `false` | 7z 加密文件头 |
| `ZipCompressionMethod` | string | `"Deflate"` | ZIP 压缩方法 |
| `ZipEncryptionMethod` | string | `"AES256"` | ZIP 加密方式 |
| `SevenZipCompressionMethod` | string | `"LZMA2"` | 7z 压缩方法 |
| `SevenZipSolid` | bool | `true` | 7z 固实压缩 |

**任务:**
- [ ] `Models/AppSettings.cs` 添加上述 9 个属性 + 默认值
- [ ] 验证 `DynamicFormatOptionsPanel` 与 Core `ArchiveOptions` 的绑定通路
- [ ] `SettingsWindow` 压缩 Tab 补充 ComboBox 设置 UI

---

### P0-3: 魔数检测预览（Magic Detection）

**现状:** WPF 已实现 `FileFormatDetector`（Core，共享）+ WPF UI 端魔数路由（`TryMagicPreview`、格式切换按钮、冲突检测）。Avalonia `PreviewService` 仍使用扩展名判定，未接入魔数检测。

**缺失 AppSettings 属性:**
| 属性 | 类型 | 默认值 |
|------|------|--------|
| `EnableFormatDetection` | bool | `true` |
| `PreviewHeadSize` | long | `4096` |

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `EnableFormatDetection`、`PreviewHeadSize`
- [ ] `Services/PreviewService.cs` 添加魔数路由逻辑（调用 `FileFormatDetector.Detect()` → 按魔数结果路由预览格式）
- [ ] 添加扩展名/魔数冲突时的切换 UI（工具栏按钮）
- [ ] 预览信息面板显示魔数检测结果

---

## P1 — 功能缺失（重要）

### P1-1: 双击行为 + 删除原压缩包设置

**现状:** WPF 支持 `DoubleClickAction`（打开/原地解压/智能解压/解压到…）和 `DeleteArchiveAfterExtract`（解压后删除原文件）。Avalonia 完全缺失。

**缺失 AppSettings 属性:**
| 属性 | 类型 | 默认值 |
|------|------|--------|
| `DoubleClickAction` | string | `"open"` |
| `DoubleClickOpenThreshold` | int | `10`（MB, 0=禁用） |
| `DeleteArchiveAfterExtract` | bool | `false` |

**WPF 源文件参考:** `App.xaml.cs`（`--open` 分发按 DoubleClickAction 路由）、`App.Extract.cs`（`TryDeleteArchiveAfterExtract`）、`MainWindow.UI.cs`（双击处理）

**任务:**
- [ ] `Models/AppSettings.cs` 添加 3 个属性
- [ ] `App.axaml.cs` `--open` 分支改为按 `DoubleClickAction` 分发（HandleOpen/HandleExtractHere/HandleExtractSmart/HandleExtract）
- [ ] `Services/ExtractService.cs` 添加 `TryDeleteArchiveAfterExtract`（解压成功后移到回收站）
- [ ] `Views/SettingsWindow` 添加双击行为 + 删除原文件设置 UI
- [ ] 添加 i18n key

---

### P1-2: 便携模式

**现状:** WPF 支持 `Portable.txt` 哨兵文件 → 路径重定向到 exe 目录/Data/。Avalonia 完全缺失。

**缺失 AppSettings 属性:** 无（WPF 通过静态 `IsPortableMode` 属性实现，无设置属性）

**WPF 修改文件参考:** `AppSettings.cs`（哨兵检测 + `IsPortableMode`）、`PasswordManager.cs`（`CustomDataDir`）、`App.xaml.cs`（跳过注册）、`MainWindow.Preview.cs`（`GetTempDir()`）、`MainWindow.DragDrop.cs`、`SevenZipEngine.cs`

**注意:** Core 层 `SevenZipEngine.cs` 的 `ResolveDefaultSevenZipDllPath()` 已在 AvaloniaFromWpf 分支合并，只需 UI 层补充。

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `IsPortableMode` 静态属性 + `Portable.txt` 哨兵检测
- [ ] `Services/ArchiveService.cs` 或 `App.axaml.cs` 处理路径重定向（settings/passwords → exe 目录/Data/）
- [ ] 便携模式下跳过 FirstRunShell/FirstRunAssoc 注册
- [ ] Temp 目录重定向

---

### P1-3: 文件过滤控件（FileFilterEditor）

**现状:** WPF 有 `Controls/FileFilterEditor.xaml(.cs)` + `FileFilterHelper.cs` + `FileFilterPreset` 模型（Core，共享）。Avalonia 完全缺失 UI 控件。

**WPF 源文件:**
- `src/MantisZip.UI/Controls/FileFilterEditor.xaml(.cs)`
- `src/MantisZip.UI/FileFilterHelper.cs`

**注意:** Core 层 `FileFilterPreset`、`FileFilterRule`、`ArchiveFilter` 等模型已在共享 Core 中，只需移植 UI 控件。

**任务:**
- [ ] 创建 `MantisZip.UI.Avalonia/Controls/FileFilterEditor.axaml(.cs)` — 移植自 WPF 版
- [ ] 集成到 `CompressSettingsWindow` 和 `ExtractSettingsWindow`
- [ ] 添加 i18n key

---

### P1-4: 默认路径优先级

**现状:** WPF 支持 `DefaultPathPriority`（场景相关/资源管理器/最近使用/桌面 4 种策略）。Avalonia 缺失。

**缺失 AppSettings 属性:**
| 属性 | 类型 | 默认值 |
|------|------|--------|
| `DefaultPathPriority` | string | `"context"` |

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `DefaultPathPriority` 属性
- [ ] 移植 `ResolveDefaultPath()` 静态方法（按优先级链自动选取最佳默认路径）
- [ ] 在 `SettingsWindow` 高级 Tab 添加 4 个 RadioButton
- [ ] 接入 7 个 `QuickPathPreDialog` 调用点

---

### P1-5: 冲突对话框暂停/取消

**现状:** WPF 的 `CompressConflictDialog` 和 `ConflictDialog` 新增底部暂停/取消按钮。暂停收起对话框 → ProgressWindow 暂停态 → 恢复时重新弹出。取消通过 `OperationCanceledException` 终止操作。Avalonia 版本缺失此功能。

**WPF 源文件参考:** `CompressConflictDialog.xaml(.cs)`、`ConflictDialog.xaml(.cs)`、`ProgressWindow.xaml.cs`（`PauseFromConflict()`）、`App.xaml.cs`（循环重入改造）

**任务:**
- [ ] `Dialogs/CompressConflictDialog.axaml` + `.axaml.cs` 添加暂停/取消按钮
- [ ] `Dialogs/ConflictDialog.axaml` + `.axaml.cs` 添加暂停/取消按钮
- [ ] `Dialogs/ProgressWindow.axaml.cs` 添加 `PauseFromConflict()` 方法（`ManualResetEventSlim`）
- [ ] `Services/CompressService.cs` 各压缩循环添加 conflictResolver 暂停/取消支持
- [ ] 添加 i18n key

---

### P1-6: 预览信息面板显隐控制

**现状:** WPF 视图菜单新增"隐藏预览信息"开关 + `ShowPreviewInfoPanel` 设置持久化。Avalonia 的 `PreviewInfoPanel` 已在 Phase 10 实现，但缺少显隐切换。

**缺失 AppSettings 属性:**
| 属性 | 类型 | 默认值 |
|------|------|--------|
| `ShowPreviewInfoPanel` | bool | `true` |

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `ShowPreviewInfoPanel`
- [ ] `ViewModels/MainWindowViewModel.cs` 添加 `ShowPreviewInfoPanel` 可观察属性
- [ ] `Views/MainWindow.axaml` 视图菜单添加"隐藏预览信息"切换项
- [ ] 添加 i18n key

---

### P1-7: 智能打开路径

**现状:** WPF 解压完成后检测条目公共根目录，如所有条目共享同一根目录则打开子目录而非根目录。Avalonia 缺失。

**WPF 参考:** `App.Extract.cs`（`GetCommonRootDirectory` + `ResolveSmartOpenPathAsync`）

**任务:**
- [ ] 在 `ExtractService.cs` 或 `App.axaml.cs` 添加 `GetCommonRootDirectory` + `ResolveSmartOpenPathAsync`
- [ ] 解压完成后的 `OpenFolderAfterExtract` 调用点改为智能打开

---

## P2 — 功能缺失（非阻塞）

### P2-1: 窗口位置持久化

**现状:** WPF 将窗口大小/位置保存到 `%LOCALAPPDATA%\MantisZip\window.json`（含 `Pixel` 和 `Star` GridLength 类型）。Avalonia 未实现。

**任务:**
- [ ] 创建 `Models/WindowStateManager.cs` — JSON 读写窗口状态
- [ ] `MainWindow.axaml.cs` 启动时 `LoadWindowState()` 恢复大小位置
- [ ] `MainWindow.axaml.cs` 关闭时 `SaveWindowState()` 持久化

---

### P2-2: 密码导入导出

**现状:** WPF 支持密码库导入导出为明文 JSON。Avalonia `PasswordManagerWindow` 缺少此功能。

**任务:**
- [ ] `Dialogs/PasswordManagerWindow.axaml.cs` 添加"导出"/"导入"按钮
- [ ] 调用 Core `PasswordManager.Export()` / `Import()` 方法

---

### P2-3: 缺少的 Enable 设置

**现状:** WPF 有 3 个菜单/功能 Enable 开关在 Avalonia 缺失：

| 缺失属性 | 类型 | 默认值 |
|---------|------|--------|
| `EnableCompressMenu` | bool | `true` |
| `EnableExtractMenu` | bool | `true` |
| `EnableQuickCompress` | bool | `true` |

**任务:**
- [ ] `Models/AppSettings.cs` 添加 3 个属性
- [ ] 关联到对应菜单项的可见性/启停

---

### P2-4: 日志隐私模式设置完整性

**现状:** WPF 有 `AllowElevation` 设置（控制是否允许提权操作）。Avalonia 缺失。

| 缺失属性 | 类型 | 默认值 |
|---------|------|--------|
| `AllowElevation` | bool | `true` |

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `AllowElevation`
- [ ] `App.axaml.cs` 提权流程检查此设置

---

## 文件比较统计

| 指标 | WPF (main) | Avalonia (AvaloniaFromWpf) |
|------|-----------|---------------------------|
| UI 项目 .cs 文件 | ~97 | ~95 |
| Dialogs | 13 对 | 12 对 + CommentDialog |
| Controls | 3 个 | 3 个（含 InfoPanel） |
| Converters | 1 个 | 6 个 |
| Shell 文件 | 3 个 | 0 个 |
| AppSettings 属性 | 74 | 56 |

---

## i18n key 差距

> 注: Avalonia i18n 补齐已完成 19/19，WPF 全量 key 对齐已确认为不需要（425/426 代码引用已覆盖）。

无额外 i18n 工作，仅在上述新增功能添加对应 key。

---

## 实现优先级建议

1. **Phase 1（P0 全部）** — Shell/COM + 压缩选项 + 魔数检测（~2 天）
2. **Phase 2（P1 核心）** — 双击行为/删除原包 + 便携模式 + 文件过滤 + 冲突暂停（~1.5 天）
3. **Phase 3（P1 次要 + P2）** — 默认路径优先级 + 智能打开路径 + 预览信息面板显隐 + 窗口持久化 + 密码导入导出 + Enable 设置（~1 天）

---

*核对方法: WPF 文件列表展开排除 obj/ 后可对比各目录文件数量。关键差异在 AppSettings 属性数量（74 vs 56）、Shell 文件存在与否、FileFilterEditor 控件存在与否。*
