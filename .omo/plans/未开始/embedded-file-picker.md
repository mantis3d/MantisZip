# 文件选择器去弹窗化 — CustomFilePickerControl 内嵌改造

> **状态**: 📋 待实施 | **创建**: 2026-10-08 | **优先级**: P2 | **预估工时**: 1-2 天
> **来源**: 主窗口/压缩/解压三处选择器弹窗交互优化讨论
> **原型**: `docs/prototypes/compress-embedded-picker.html`
> **当前核实**: `CustomFilePickerDialog`（`Dialogs/CustomFilePickerDialog.axaml(.cs)`）承担全部选择逻辑，经静态入口 `ShowOpenItemsAsync`/`ShowExtractFolderAsync`/`ShowSaveFileAsync`/`ShowFolderAsync` 被 6+ 处调用，全部为模态弹窗

## 背景

三处文件/目录选择均弹独立窗口，操作链路长：

| 调用点 | 当前 | 弹窗 |
|--------|------|------|
| 压缩窗「添加文件」 | `CompressSettingsViewModel.PickFiles` → `CustomFilePickerDialog.ShowOpenItemsAsync` | PickItems 模式 |
| 压缩窗「输出路径」 | `BrowseOutput` → `ShowSaveFileAsync` | SaveFile 模式 |
| 解压窗目标路径 | `DestinationPicker.BrowseAction` → `ShowExtractFolderAsync` | ExtractFolder 模式 |
| 主窗口选中条目「解压到」 | `ShowExtractFolderAsync` | ExtractFolder 模式 |
| 拖拽解压路径兜底 | `DragDropService` → `ShowExtractFolderAsync` | ExtractFolder 模式 |

需求：

1. 选择器**内嵌**进设置窗（压缩/解压各一页），不再弹窗
2. PickItems 模式默认**简单选择**：隐藏复选框、单击即选、Shift 范围多选、覆盖语义（当前列表选择即结果）；跨目录批量选择切**高级选择**（复选框 + 跨目录记忆、切目录不丢选中）
3. 去掉右侧累积面板；「已选源文件」列表置于内嵌 picker 下方，与选择实时双向同步，可逐项删除
4. 内嵌 picker 左侧放 `QuickPathControl`（收藏/历史/窗口/目录树 + 搜索），左右布局
5. 压缩设置窗 Tab 重排：输出设置（最前）/ 添加文件 / 压缩设置（原「高级」改名并吸收原「常规」全部项目）/ 文件过滤
6. 解压设置窗对称改造：「添加压缩包」页（简单模式内嵌 picker）+「输出设置」页（目标目录内嵌浏览器）；拖拽兜底与「解压到」改用统一 `ExtractSettingsWindow`（隐藏「添加压缩包」Tab，指定条目解压语义保留——见方案）

## 方案

### 第一步：抽取 `CustomFilePickerControl`（纯重构，零行为变更）

- 将 `CustomFilePickerDialog.axaml` 主体内容 + code-behind 的导航/选择/Options/预览逻辑抽成 `Controls/CustomFilePickerControl.axaml(.cs)` UserControl
- `CustomFilePickerDialog` 保留为薄壳 Window（标题栏 + 确定/取消/系统浏览），内部托管该控件；`ShowXxxAsync` 静态入口不变，所有现有调用点零改动
- 控件模式枚举复用 `PickerMode`；简单/高级开关（`IsSimpleMode` 默认 true）控制：复选框显隐（`CanCheck`）、跨目录记忆（`Dictionary<string, List<string>>` 按目录缓存选中，`NavigateTo` 前存、后恢复）

### 第二步：压缩设置窗接入

- 「添加文件」Tab：内嵌 `CustomFilePickerControl`（PickItems 模式，简单默认；高级开关切跨目录记忆）+ 下方「已选源文件」ListBox，与 VM `SelectedPaths` 双向同步（选中即增/删）
- 「输出设置」Tab：内嵌控件的 PickFolder 化用法（隐藏文件名区，仅目录）+ `OutputFileName` 文本框 + 其他输出选项
- Tab 重排 + 「压缩设置」Tab 合并原「常规」项目
- `PickFiles`/`BrowseOutput` 回调删除，改为绑定/事件直连

### 第三步：解压设置窗接入

- VM：`SourceItems` 改可追加（加 `AddSourceArchives` 命令）；新增 `HideAddArchivesTab`（构造传入）控制「添加压缩包」Tab 显隐
- 「添加压缩包」Tab：内嵌控件 PickItems 模式 + 「已选压缩包」列表；场景：右键多包打开合并、手动追加
- 「输出设置」Tab：内嵌 PickFolder 化控件 + 冲突处理/并行度等原常规设置项目
- 拖拽解压兜底、「解压到」：改实例化 `ExtractSettingsWindow`，第二个 Tab **按场景替换形态**：
  - 右键全量解压场景：「添加压缩包」（简单模式内嵌 picker + 已选压缩包列表）
  - 「解压到」/拖拽兜底场景：「**选择要解压的文件**」——数据源为当前压缩包的 `ArchiveItem` 条目列表，复用简单/高级交互（点击即选、Shift 范围选、高级=复选框），初始预勾选本次触发的条目集，确认走 `SelectedEntryKeys` 指定条目解压；预览树直接拿这组条目构建
- VM：新增 `SelectedEntryKeys` 构造入参 + `HideAddArchivesTab` 场景标志

### 涉及文件

- 新增 `Controls/CustomFilePickerControl.axaml(.cs)`
- `Dialogs/CustomFilePickerDialog.axaml(.cs)`（薄壳化）
- `Dialogs/CompressSettingsWindow.axaml(.cs)` + `ViewModels/CompressSettingsViewModel.cs`
- `Dialogs/ExtractSettingsWindow.axaml(.cs)` + `ViewModels/ExtractSettingsViewModel.cs`
- `Views/MainWindow.axaml.cs`（场景 2 调用点）
- `Services/DragDropService.cs`（场景 3 调用点）
- 本地化三语 key（`strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`）+ `MainWindowViewModel.UpdateLocalizedStrings` 登记
- `AGENTS.md` 规则 15 模式选择表新增「内嵌选择」行

### 验证

- `dotnet build` 三个项目 0 错误；`lsp_diagnostics` 无新增 error
- 手动：简单模式（单击/Shift/双击目录清空进入/覆盖语义）；高级模式（跨目录记忆+恢复）；「已选源文件」与 picker 双向同步；压缩窗输出路径内嵌选取；解压窗三种入口行为一致（指定条目/全量/追加包）
- `git grep` 确认原生 StorageProvider 无新增豁免外调用

### 边界

- 本计划不动 `CustomFilePickerDialog` 的系统浏览逃生通道（规则 15 唯一豁免）
- CLI 路径不变；`--compress` IPC 等不受影响
- 不改 `ResultTreeView` / `QuickPathControl` / `ExtractPathResolver` 契约
