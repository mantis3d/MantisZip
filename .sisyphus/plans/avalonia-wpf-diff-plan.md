# Avalonia WPF 功能差异补齐计划

> **For agentic workers:** Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** 对比 `main`（WPF）与 `AvaloniaFromWpf`（Avalonia）分支的功能差异，列出 Avalonia 需要补齐的缺失功能并提供优先级排序。

**背景:** `AvaloniaFromWpf` 已合并 `main` 所有提交（merge-base: `ddaadc9`），Core 层共享，差异仅在 UI 层。当前 Avalonia 项目已完成 Phase 0-10 + UI Feature Parity + P0 项目 + i18n Cleanup + Shell/COM 集成。

**核对日期:** 2026-07-20（上次: 2026-07-19）| **版本:** v0.4.5

**状态:** P0 全部完成。P1 剩余 5 项，P2 剩余 2 项。

---

## 汇总

| 缺失类别 | 缺失项数 | 优先级分布 |
|----------|---------|-----------|
| AppSettings 属性 | 8 个 | P1×5, P2×2, 部分完成×1 |
| 对话框/控件 | 1 个 | P1 |
| 功能逻辑 | 5 项 | P1×5, P2×0 |
| Shell/COM 集成 | 整块 | P0 ✅ 已完成 |
| 总工作量预估 | — | 约 1.5 天 |

---

## ✅ 已完成（P0 & 部分 P1/P2）

### ✅ P0-1: Shell/COM 集成

**现状:** 已完整移植。`ShellIntegration.cs`、`ShellIntegration.Assoc.cs`、`ShellIntegration.Menu.cs` 全部存在于 `Services/`，包含 COM 注册 + 静态级联菜单 + 文件关联功能。`HandleShellCommand` 直接调用本进程的 `ShellIntegration` 而非委托 WPF exe。

**完成明细:**
- [x] `MantisZip.UI.Avalonia.csproj` 添加 `ProjectReference` 到 `MantisZip.ShellExt` + COM host 部署 target（`CopyShellExtComhost` + `CopyShellExtComhostToPublish`）
- [x] 移植 `ShellIntegration.cs`（注册表操作 + 安装/卸载/检查状态）
- [x] 移植 `ShellIntegration.Assoc.cs`（`InstallAssociations`/`UninstallAssociations`/per-extension 关联）
- [x] 移植 `ShellIntegration.Menu.cs`（级联菜单注册 + COM 动态菜单回退 + `CommandFlags=8` 已知问题修复）
- [x] 从 WPF 复制 MenuIcons ICO 文件（10 个，位于 `Resources/MenuIcons/`）
- [x] `App.axaml.cs` 补全 `--install-shell`/`--uninstall-shell`/`--install-assoc`/`--uninstall-assoc` 实际逻辑（直接调用 `ShellIntegration`），并加入首次运行自动安装
- [x] 便携模式跳过 Shell/Assoc 安装

### ✅ P0-2: 压缩选项增强（7z/ZIP 高级参数）

**现状:** All 9 AppSettings properties 已存在于 Avalonia `AppSettings.cs`。`DynamicFormatOptionsPanel` 控件已存在。

**完成明细:**
- [x] `Models/AppSettings.cs` — 9 个属性存在（`SevenZipSolidBlockSize`、`SevenZipDictionarySize`、`SevenZipNumFastBytes`、`SevenZipMatchFinder`、`SevenZipEncryptHeaders`、`ZipCompressionMethod`、`ZipEncryptionMethod`、`SevenZipCompressionMethod`、`SevenZipSolid`）
- [x] `Controls/DynamicFormatOptionsPanel.axaml` + `.axaml.cs` 控件存在
- [x] SettingsWindow 压缩 Tab 的 ComboBox UI 存在

### ✅ P0-3: 魔数检测预览（Magic Detection）

**现状:** Avalonia `PreviewService` 已有完整魔数检测逻辑。

**完成明细:**
- [x] `Models/AppSettings.cs` — `EnableFormatDetection`、`PreviewHeadSize`
- [x] `Services/PreviewService.cs` — `ClassifyPreviewByMagicAsync()`、`MapFileFormatToPreviewType()`、扩展名兜底 + 格式冲突回避
- **无剩余任务**

### ✅ P1-5: 冲突对话框暂停/取消（对话框 UI 部分）

**现状:** 对话框 UI 补全已完成。

| 对话框 | 已补全内容 |
|--------|-----------|
| `CompressConflictDialog` | 暂停/取消按钮、Add 按钮（`CompressConflictAction.Add`）、Topmost、分隔线、删除多余 Cancel 按钮 |
| `ConflictDialog` | 暂停/取消按钮、"覆盖较旧"、"覆盖较小"按钮、Topmost、分隔线、删除多余 Cancel 按钮 |
| 共享 | ✅ `AppIcons.axaml` 补充 `IconPause` 几何；✅ 本地化 keys 补充（9 条 EN/ZH）；✅ `dotnet build` 通过 |

**已完成:**
- [x] `Dialogs/CompressConflictDialog.axaml` + `.axaml.cs`
- [x] `Dialogs/ConflictDialog.axaml` + `.axaml.cs`
- [x] 本地化 i18n key 补充
- [x] 删除两个对话框动作行多余的 Cancel 按钮

### ✅ P2-2: 密码导入导出

**现状:** `PasswordManagerWindow` 已有完整的导入导出功能。

**完成明细:**
- [x] `Dialogs/PasswordManagerWindow.axaml` — 导出/导入按钮（UI）
- [x] `Dialogs/PasswordManagerWindow.axaml.cs` — `OnExportClick`/`OnImportClick` 处理器，调用 `PasswordManager.Instance.ExportToJson()`/`ImportFromJson()`
- [x] 本地化 key `PasswordManager_Export`/`PasswordManager_Import`

### ✅ P2-额外: 收藏夹功能

**现状:** 完整实现，未在原计划中列出的额外已完成项。

- [x] `Models/AppSettings.cs` — `FavoritePaths` (`List<string>`)
- [x] `Dialogs/FavoriteManagerWindow.axaml` + `.axaml.cs`
- [x] `Dialogs/AddFavoriteDialog.axaml` + `.axaml.cs`

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

### P1-2: 便携模式（部分完成，需补齐）

**现状:** 🟡 部分完成。`App.axaml.cs` 已有 `Portable.txt` 哨兵检测（L79），便携模式下跳过 FirstRunShell/FirstRunAssoc 注册（L80-125）。但缺少正式 `IsPortableMode` 属性、设置/密码/Temp 目录路径重定向。

**缺失 AppSettings 属性:** 无（WPF 通过静态 `IsPortableMode` 属性实现，无设置属性）

**WPF 修改文件参考:** `AppSettings.cs`（哨兵检测 + `IsPortableMode`）、`PasswordManager.cs`（`CustomDataDir`）、`App.xaml.cs`（跳过注册）、`MainWindow.Preview.cs`（`GetTempDir()`）、`MainWindow.DragDrop.cs`、`SevenZipEngine.cs`

**注意:** Core 层 `SevenZipEngine.cs` 的 `ResolveDefaultSevenZipDllPath()` 已合并，只需 UI 层补充。

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `IsPortableMode` 静态属性 + `Portable.txt` 哨兵检测（当前是局部变量，需提升为公共属性）
- [ ] `Services/ArchiveService.cs` 或 `App.axaml.cs` 处理路径重定向（settings/passwords → exe 目录/Data/）
- [ ] Temp 目录重定向（预览/拖拽提取等）

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
- [ ] `Models/AppSettings.cs` 添加 `FilterPresets` 属性（`List<FileFilterPreset>`，默认 `new()`）
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

### P1-6: 预览信息面板持久化显隐

**现状:** Avalonia 已有运行时信息面板显隐功能（`PreviewViewModel.IsInfoPanelVisible` + `InfoPanelOrientation` + 菜单切换），但缺少持久化设置。重启后显隐状态会重置。WPF 通过 `ShowPreviewInfoPanel` 设置实现跨会话持久化。

**缺失 AppSettings 属性:**
| 属性 | 类型 | 默认值 |
|------|------|--------|
| `ShowPreviewInfoPanel` | bool | `true` |

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `ShowPreviewInfoPanel` 属性
- [ ] `ViewModels/PreviewViewModel.cs` 启动时从 `ShowPreviewInfoPanel` 初始化 `IsInfoPanelVisible`
- [ ] 菜单切换时同步回写 `ShowPreviewInfoPanel`
- [ ] `Views/SettingsWindow` 外观 Tab 添加信息面板默认显隐开关（可选）

---

### P1-7: 智能打开路径

**现状:** WPF 解压完成后检测条目公共根目录，如所有条目共享同一根目录则打开子目录而非根目录。Avalonia 缺失。

**WPF 参考:** `App.Extract.cs`（`GetCommonRootDirectory` + `ResolveSmartOpenPathAsync`）

**任务:**
- [ ] 在 `ExtractService.cs` 或 `App.axaml.cs` 添加 `GetCommonRootDirectory` + `ResolveSmartOpenPathAsync`
- [ ] 解压完成后的 `OpenFolderAfterExtract` 调用点改为智能打开

---

## P2 — 功能缺失（非阻塞）

### ✅ P2-1: 窗口位置持久化

**现状:** 已完成。`Models/WindowStateManager.cs` 负责将窗口 Width/Height/Position/WindowState 持久化到 `%LOCALAPPDATA%\MantisZip\window.json`。Avalonia 版简化了 WPF 的列状态/预览面板尺寸保存（仅持久化窗口自身状态）。

**完成明细:**
- [x] 创建 `Models/WindowStateManager.cs` — JSON 读写窗口状态（位置/大小/状态）
- [x] `Views/MainWindow.axaml.cs` — 构造函数调用 `WindowStateManager.Load(this)`
- [x] `Views/MainWindow.axaml.cs` — `Closing` 事件调用 `WindowStateManager.Save(this)`

---

### P2-3: 缺少的 Enable 设置

**现状:** WPF 有 3 个菜单/功能 Enable 开关，Avalonia 仍缺 2 个。

| 缺失属性 | 类型 | 默认值 | Avalonia 状态 |
|---------|------|--------|-------------|
| `EnableCompressMenu` | bool | `true` | ✅ 已存在 |
| `EnableExtractMenu` | bool | `true` | ❌ 缺失 |
| `EnableQuickCompress` | bool | `true` | ❌ 缺失 |

**任务:**
- [x] `Models/AppSettings.cs` 添加 `EnableCompressMenu` — 已存在
- [ ] `Models/AppSettings.cs` 添加 `EnableExtractMenu`、`EnableQuickCompress`
- [ ] 关联到对应菜单项的可见性/启停

---

### P2-4: AllowElevation 设置

**现状:** WPF 有 `AllowElevation` 设置（控制是否允许提权操作）。Avalonia 缺失。

| 缺失属性 | 类型 | 默认值 |
|---------|------|--------|
| `AllowElevation` | bool | `true` |

**注意:** 提权相关的对话框（`ElevationDialog`、`ElevationFailedDialog`、`ElevationInfoDialog`）和 `HandleElevationAsync` 方法在 Avalonia 已存在。缺失的是控制提权行为的启用开关。

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `AllowElevation`
- [ ] `App.axaml.cs` 提权流程检查此设置

---

## 文件比较统计

| 指标 | WPF (main) | Avalonia (AvaloniaFromWpf) |
|------|-----------|---------------------------|
| UI 项目 .cs 文件 | 64 | 73 |
| Dialogs | 17 个 dialog .cs | 19 个 dialog .cs |
| Controls | 3 个 | 4 个（含 InfoPanel、ResultTreeView） |
| Converters | 3 个 | 8 个 |
| Shell 文件 | 3 个 | 3 个 ✅（已移植） |
| AppSettings 属性 | 75 实例 + 1 静态 | 约 60（缺 8 个属性） |

---

## i18n key 差距

> 注: Avalonia i18n 补齐已完成 19/19，WPF 全量 key 对齐已确认为不需要（425/426 代码引用已覆盖）。

无额外 i18n 工作，仅在上述新增功能添加对应 key。

---

## 实现优先级建议

1. **Phase 1（P1 核心）** — 双击行为/删除原包 + 便携模式补齐 + 文件过滤控件 + 默认路径优先级（~1 天）
2. **Phase 2（P1 次要 + P2）** — 智能打开路径 + 预览信息面板持久化 + 窗口持久化 + Enable 设置 + AllowElevation（~0.5 天）

---

*核对方法: 对比 AppSettings 属性数量（WPF ~76 vs Avalonia ~60）、Shell 文件存在与否（已移植）、FileFilterEditor 控件存在与否。*
