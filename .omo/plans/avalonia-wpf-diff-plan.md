# Avalonia WPF 功能差异补齐计划

> **For agentic workers:** Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** 对比 `main`（WPF）与 `AvaloniaFromWpf`（Avalonia）分支的功能差异，列出 Avalonia 需要补齐的缺失功能并提供优先级排序。

**背景:** `AvaloniaFromWpf` 已合并 `main` 所有提交（merge-base: `ddaadc9`），Core 层共享，差异仅在 UI 层。当前 Avalonia 项目已完成 Phase 0-10 + UI Feature Parity + P0 项目 + i18n Cleanup + Shell/COM 集成。

**核对日期:** 2026-08-06（上次: 2026-07-20）| **版本:** v0.4.5

**状态:** P0 全部完成。P1 剩余 1 项（P1-7 智能打开路径），P2 剩余 0 项。P1-6 已被 [metadata-panel-configurable.md](metadata-panel-configurable.md) 取代并实施（2026-08-06）；P1-2 便携模式路径重定向已于 2026-08-08 实施（`AppSettings.IsPortableMode`/`DataDir`，settings/password/recent/window/metadata/log 全量重定向）。

---

## 汇总

| 缺失类别 | 缺失项数 | 优先级分布 |
|----------|---------|-----------|
| AppSettings 属性 | 0 个 | —（便携模式已实施） |
| 对话框/控件 | 0 个 | — |
| 功能逻辑 | 1 项 | P1（智能打开路径） |
| Shell/COM 集成 | 整块 | P0 ✅ 已完成 |
| 总工作量预估 | — | 约 0.5 天 |

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

### ✅ P1-1: 双击行为 + 删除原压缩包设置

**现状:** WPF 支持 `DoubleClickAction`（打开/原地解压/智能解压/解压到…）和 `DeleteArchiveAfterExtract`（解压后删除原文件）。Avalonia 完全缺失。

**✅ 核实结论（2026-08-06）:** 已完整实现。
- [x] `Models/AppSettings.cs` — `DoubleClickAction`/`DoubleClickOpenThreshold`/`DeleteArchiveAfterExtract` 3 个属性存在
- [x] `App.axaml.cs` — `--open` 分支按 `DoubleClickAction` 分发（L171-175）
- [x] `MainWindowViewModel` — 双击处理（阈值检查 + 密码/格式提示）
- [x] `App.axaml.cs` — `TryDeleteArchiveAfterExtract`（回收站 + 3 次重试，L700）

---

### ✅ P1-2: 便携模式（已实施）

**现状:** ✅ 已完成（2026-08-08）。`Models/AppSettings.cs` 新增静态构造检测 + `IsPortableMode`/`DataDir` 静态属性（`Portable.txt` → exe 旁 `Data/` 目录重定向），便携时 `PasswordManager.CustomDataDir = DataDir`；`RecentFilesManager`（recent.json）/`WindowStateManager`（window.json）/`MetadataSettingsManager`（metadata-panel.json）/debug.log×3 统一改用 `AppSettings.DataDir`。

**缺失 AppSettings 属性:** 无（WPF 通过静态 `IsPortableMode` 属性实现，无设置属性）

**WPF 修改文件参考:** `AppSettings.cs`（哨兵检测 + `IsPortableMode`）、`PasswordManager.cs`（`CustomDataDir`）、`App.xaml.cs`（跳过注册）、`MainWindow.Preview.cs`（`GetTempDir()`）、`MainWindow.DragDrop.cs`、`SevenZipEngine.cs`

**注意:** Core 层 `SevenZipEngine.cs` 的 `ResolveDefaultSevenZipDllPath()` 已合并，只需 UI 层补充。

**任务:**
- [ ] `Models/AppSettings.cs` 添加 `IsPortableMode` 静态属性 + `Portable.txt` 哨兵检测（当前是局部变量，需提升为公共属性）
- [ ] `Services/ArchiveService.cs` 或 `App.axaml.cs` 处理路径重定向（settings/passwords → exe 目录/Data/）
- [ ] Temp 目录重定向（预览/拖拽提取等）

---

### ✅ P1-3: 文件过滤控件（FileFilterEditor）

**现状:** WPF 有 `Controls/FileFilterEditor.xaml(.cs)` + `FileFilterHelper.cs` + `FileFilterPreset` 模型（Core，共享）。Avalonia 完全缺失 UI 控件。

**✅ 核实结论（2026-08-06）:** 已完整实现。
- [x] `Controls/FileFilterEditor.axaml(.cs)` 存在（全 i18n）
- [x] 集成到 `CompressSettingsWindow` 和 `ExtractSettingsWindow`（`GetFilter()`/`FilterChanged`）
- [x] `Models/AppSettings.cs` — `FilterPresets` (`List<FileFilterPreset>`) 存在

---

### ✅ P1-4: 默认路径优先级

**现状:** WPF 支持 `DefaultPathPriority`（场景相关/资源管理器/最近使用/桌面 4 种策略）。Avalonia 缺失。

**✅ 核实结论（2026-08-06）:** 已实现且演进为更先进形态 —— 单一 `DefaultPathPriority` string 已升级为可排序的 `DefaultPathOrder` (List<string>) + `CustomDefaultPath`。
- [x] `Models/AppSettings.cs` — `DefaultPathOrder` (List，默认 {context, explorer, recent, custom}) + `CustomDefaultPath` 存在
- [x] `Dialogs/CustomFilePickerDialog` — `ResolveDefaultPath` 等价物（按优先级链遍历，L372-387）
- [x] `SettingsWindowViewModel` — `PathPriorityItemModel` 可排序 UI（L1297/1305）

---

### ✅ P1-6: 预览信息面板持久化显隐（已被 metadata-panel-configurable 取代并实施）

**现状:** Avalonia 已有运行时信息面板显隐功能（`PreviewViewModel.IsInfoPanelVisible` + `InfoPanelOrientation` + 菜单切换），但缺少持久化设置。重启后显隐状态会重置。WPF 通过 `ShowPreviewInfoPanel` 设置实现跨会话持久化。

**✅ 处理结论（2026-08-06）:** 该需求已被 [metadata-panel-configurable.md](metadata-panel-configurable.md) 重构取代——新系统将信息面板改为可配置系统（内容/字段/位置/区域排序持久化到独立 `metadata-panel.json`，`InfoPanelOrientation` 已入 AppSettings）。原单一 `ShowPreviewInfoPanel` bool 在新系统粒度下反而倒退。

**补充实施（2026-08-06）:** 按决策补做持久化全局显隐入口：
- [x] `Models/AppSettings.cs` 添加 `ShowPreviewInfoPanel` 属性（默认 `true`）
- [x] `ViewModels/PreviewViewModel.cs` 新增用户偏好 `ShowInfoPanel` + 合并可见性 `IsInfoPanelEffectiveVisible`（= 内容驱动 `IsInfoPanelVisible` && 用户偏好 `ShowInfoPanel`），启动时从 `ShowPreviewInfoPanel` 初始化
- [x] 菜单切换时同步回写 `ShowPreviewInfoPanel`（`MainWindowViewModel.ToggleInfoPanelVisibility`）
- [x] `Views/MainWindow.axaml` 预览菜单增加"显示信息面板"开关项（`Menu_ShowInfoPanel`，IconPanelRight）
- [x] `Views/SettingsWindow` 预览 → 通用 Tab 添加"显示信息面板"开关（`Settings_Preview_ShowInfoPanel`，与菜单入口同步持久化到同一 `ShowPreviewInfoPanel`）

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

**✅ 核实结论（2026-08-06）:** 3 个开关全部存在。
- [x] `Models/AppSettings.cs` — `EnableCompressMenu` 已存在
- [x] `Models/AppSettings.cs` — `EnableExtractMenu` 已存在
- [x] `Models/AppSettings.cs` — `EnableQuickCompress` 已存在

---

### ✅ P2-4: AllowElevation 设置

**现状:** WPF 有 `AllowElevation` 设置（控制是否允许提权操作）。Avalonia 缺失。

**✅ 核实结论（2026-08-06）:** 已存在。`Models/AppSettings.cs` — `AllowElevation`（默认 `false`，文档原记默认 `true` 有误）。

---

## 文件比较统计

| 指标 | WPF (main) | Avalonia (AvaloniaFromWpf) |
|------|-----------|---------------------------|
| UI 项目 .cs 文件 | 64 | 73 |
| Dialogs | 17 个 dialog .cs | 19 个 dialog .cs |
| Controls | 3 个 | 4 个（含 InfoPanel、ResultTreeView） |
| Converters | 3 个 | 8 个 |
| Shell 文件 | 3 个 | 3 个 ✅（已移植） |
| AppSettings 属性 | 75 实例 + 1 静态 | 约 60（缺 1 个：便携路径重定向相关） |

---

## i18n key 差距

> 注: Avalonia i18n 补齐已完成 19/19，WPF 全量 key 对齐已确认为不需要（425/426 代码引用已覆盖）。

无额外 i18n 工作，仅在上述新增功能添加对应 key。

---

## 实现优先级建议

1. **Phase 1（P1 核心）** — ~~双击行为/删除原包 + 文件过滤控件 + 默认路径优先级 + 便携模式补齐~~（已实现）→ 完成
2. **Phase 2（P1 次要 + P2）** — ~~预览信息面板持久化 + Enable 设置 + AllowElevation~~（已实现/已取代）→ 仅剩智能打开路径（~0.5 天）

---

*核对方法: 对比 AppSettings 属性数量（WPF ~76 vs Avalonia ~60）、Shell 文件存在与否（已移植）、FileFilterEditor 控件存在与否。*
