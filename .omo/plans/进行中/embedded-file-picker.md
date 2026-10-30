# 文件选择器去弹窗化 — CustomFilePickerControl 内嵌改造

> **状态**: 📋 待实施 | **创建**: 2026-10-08 | **档位**: **A 档 · v0.5.5**（2026-10-29 升入 A 档；原「接缝之后做」的前提已于 2026-10-26 满足） | **预估工时**: 1-2 天
> **来源**: 主窗口/压缩/解压三处选择器弹窗交互优化讨论
> **原型**: `docs/prototypes/embedded-file-picker.html`（完整交互版，2026-10-29 新增：三种入口 + 简单/高级 + 双主题 + **输出设置放首屏** + **右侧预览树随选择实时变化**）、`docs/prototypes/compress-embedded-picker.html`（早期压缩窗局部版）
> **当前核实**: `CustomFilePickerDialog`（`Dialogs/CustomFilePickerDialog.axaml(.cs)`）承担全部选择逻辑，经静态入口 `ShowOpenItemsAsync`/`ShowExtractFolderAsync`/`ShowSaveFileAsync`/`ShowFolderAsync` 被 6+ 处调用，全部为模态弹窗
> **输出目录手势（2026-10-29 原型确认）**：单击目录行＝选中为目标（行高亮 + 写该字段），双击＝进入；↑ / 面包屑 / 收藏只切换浏览位置、不改目标。**不提供「使用当前目录」按钮** —— 目标字段必须独立于浏览位置（AppSettings.ExtractDestination 的 last 会记住上次目标，跟随浏览会把它冲掉）。压缩窗「输出目录」同此手势，两个窗口一套操作。**文件夹模式只列目录**：文件行不出现（选了也没意义），只在列表下方留一行「本目录下另有 N 个文件」的灰色提示，用于确认「是不是这个目录」；目标目录尚不存在时（解压的常见情况）提示追加「（尚不存在，确定时新建）」。**输出设置页也带左侧 QuickPathControl**（收藏 chips + 目录树 + 面包屑 + ↑ / 🏠）——「选文件」与「选目录」用同一套导航控件，只是列表内容与选择语义不同，避免同一个窗口里两套左右布局。**原型按 `Controls/QuickPathControl.axaml` 1:1 还原**：Row0 = 4 个方形 ToggleButton（收藏书签 / 历史 / 窗口 / 目录树，选中＝强调色底 + 白图标）+ 搜索框（`*` 占满剩余宽度，**目录树 Tab 下隐藏**）；Row1 = 两行式列表（名称 + 灰色完整路径，`MaxWidth` 截断）或盘符树（「本地磁盘 (C:)」+ 惰性展开）。搜索框输入时**跨收藏/历史/窗口三来源聚合过滤**并在行尾显示来源标签（⭐/🕐/🪟），与控件里的 `ShowSourceTag` 行为一致。

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
- `AGENTS.md` 规则 16（文件/目录选择）模式选择表新增「设置窗内嵌选择」行 —— 规则编号在 2026-10 的规则增补中已从 15 变成 16，本轮已按 16 落地

### 验证

- `dotnet build` 三个项目 0 错误；`lsp_diagnostics` 无新增 error
- 手动：简单模式（单击/Shift/双击目录清空进入/覆盖语义）；高级模式（跨目录记忆+恢复）；「已选源文件」与 picker 双向同步；压缩窗输出路径内嵌选取；解压窗三种入口行为一致（指定条目/全量/追加包）
- `git grep` 确认原生 StorageProvider 无新增豁免外调用

### 边界

- 本计划不动 `CustomFilePickerDialog` 的系统浏览逃生通道（规则 15 唯一豁免）
- CLI 路径不变；`--compress` IPC 等不受影响
- 不改 `ResultTreeView` / `QuickPathControl` / `ExtractPathResolver` 契约
## 实施记录（2026-10-30）

### 第一步：抽取控件（已完成，commit `cf4b159a`）

- 新增 `src/MantisZip.UI.Avalonia/Controls/CustomFilePickerControl.axaml`（459 行）+ `.axaml.cs`（1280 行）：地址行（`Grid.addrRow` + `PathBreadcrumb`）、浏览器区（左 QuickPathControl + 右列表）、文件名区、选项行全部搬入。
- `Dialogs/CustomFilePickerDialog.axaml` 缩到 56 行、`.axaml.cs` 缩到 329 行（只剩 `PickerMode` / `FileBrowserItem` / `FileTypeOption` 三个类型、5 个静态入口、确定/取消、`SystemBrowse_Click` 的系统浏览豁免、4 个测试透传成员）。
- 13 条非机械改动：系统浏览原生调用留在壳层（控件只抛 `SystemBrowseRequested`）；`Ok_Click` 拆成控件 `TryConfirm()` + 壳层写回；`TryConfirmFile` → `ConfirmRequested` 事件；`OnClosing` → `PersistUiState()`；`_mode` → `Mode`；`GetSaveFileName` 私有转公开；`AddFavorite_Click` 用 `OwnerWindow`。
- 保真审计：49 个方法体中 30 个逐字相同，其余全属上述归类；XAML 428 vs 430 行除注释外逐字相同。
- 附带修改：`tests/MantisZip.UI.Avalonia.Tests/PickerOptionsRegionLayoutTests.cs` 的源码断言路径改指 `Controls/CustomFilePickerControl.axaml`；`Dialogs/PickerOptionItem.cs` 注释同步。

### 控件能力增强（已完成，commit `3a730678` / `5a427311`）

- `IsSimpleMode` 从「摆设」变成真生效：`CanCheck` 随模式变、切目录清空选择、列表选中即累积（`SyncAccumulatedFromListSelection`）。
- 新增 `public void SetSelectedPaths(IEnumerable<string>?)`：宿主把「已选」列表回填给控件（删除已选项后同步）。
- 新增 `public event EventHandler<string>? PathChanged`：内嵌宿主据此回填「输出目录」。
- 新增 `_syncingSelection` 防抖，避免「控件选中 → 宿主重建列表 → 列表 SelectedItems 变更 → 又回灌控件」的死循环。

### 与计划的偏差

1. **没有实现「按目录缓存选中」的 `Dictionary<string, List<string>>`**：简单模式的定义是「当前目录的选中项就是结果」，切目录即清空（避免用户看不见的旧选择混进结果）；高级模式才跨目录累积，用单一 `_accumulatedPaths` 列表 + `IsSelected` 标记实现，等价且少一层字典。
2. **`IsSimpleMode` 默认 `false`**：模态壳层（`CustomFilePickerDialog`）显式设 `false` 保持原有「复选框 + 跨目录累积」行为；内嵌宿主按需设 `true`。
3. **新增 `PathChanged` 事件**（计划未列）：输出目录回填需要，否则宿主只能轮询。
4. **`TryConfirmFile` 改为 `ConfirmRequested` 事件**：控件不再直接弹窗，由宿主决定是否走「确认选中文件」语义。
### 第二步：压缩设置窗接入（已完成）

- `Dialogs/CompressSettingsWindow.axaml` 整文件重写为 6 Tab：**输出设置**（`Compress_TabOutput`，输出模式 3 个 RadioButton + `OutputPicker`（`Mode="PickFolder"` `IsSimpleMode="True"`）+ 文件名 + 冲突策略）、**添加文件**（`Compress_TabAddFiles`，`SourcePicker`（`Mode="PickItems"`）+ 高级模式开关 + 已选源文件列表 + 移除按钮）、**压缩设置**（`Compress_TabCompress`，格式/级别 + `DynamicFormatOptionsPanel` + 分卷）、**密码**、**注释**、**文件过滤**。右侧预览树 / 分割器 / 底部按钮原样保留。
- 双向同步只用一个闸门 `private bool _syncingSelection`（code-behind）：`InitializePickers` / `OnSourcePickerSelectionChanged` / `SyncPickerFromSelection` 都是「置 true → 干活 → finally 复位」，事件处理首行 `if (_syncingSelection) return;`。选择器 → VM 用 `SelectedPaths.ToList()` 整体重建（不逐项 diff）；VM → 选择器在 `RemoveSelected_Click` 后调 `SetSelectedPaths`；初始化也在闸门内，否则 `SourcePicker.Initialize(preselected:)` 抛出的 `SelectionChanged` 会立刻把 VM 清空重建一次。
- 删除的成员：VM 的 `BrowseOutput` / `PickFiles` 两个 `Func` 与 `BrowseOutputPath` / `AddFiles` 两个命令；code-behind 的 `BrowseOutputPathAsync(Window?, string?)`、两处回调赋值、`OutputPathPicker.BrowseAction`；XAML 的 `QuickPathPicker x:Name="OutputPathPicker"` 与 `Command="{Binding AddFilesCommand}"` 按钮。
- 连带删测试：`tests/MantisZip.UI.Avalonia.Tests/CompressSettingsViewModelTests.cs` 的 `BrowseOutputCommand_CallsCallback`（它断言的回调与命令已不存在；`git grep BrowseOutput` 现只剩 code-behind 一行注释）。
- 新增 5 个 key（已合并进三份 JSON，1376 → 1381）：`Compress_TabOutput` / `Compress_TabAddFiles` / `Compress_TabCompress` / `Compress_PickerAdvancedMode` / `Compress_SelectedSources`。

### 第三步·前一半：解压设置窗接入（已完成）

- `Dialogs/ExtractSettingsWindow.axaml` 重写为 4 页签：输出设置（目标目录 `DestinationPicker` PickFolder + 冲突处理 + 两个后置动作）/ 添加压缩包（`ArchivePicker` PickItems + 已选压缩包列表 + 移除所选）/ 文件过滤 / 并行解压（原「加速」）。
- `Dialogs/ExtractSettingsWindow.axaml.cs`：`_pickersReady` 与 `_syncingArchiveSelection` 双闸；`OnArchivePickerSelectionChanged` 只追加不重建 `SourceArchiveItem`；`SyncArchivePickerFromSourceItems()` 在 `Initialize` 之后回填；`CollapsePickerAccumulatedPanel()` 折叠控件内累积面板。
- `ViewModels/ExtractSettingsViewModel.cs`：新增 `AddSourceArchives(IEnumerable<string>)` / `RemoveSourceArchives(...)` / `RemoveSelectedSourceArchiveCommand` 与 `HasNoSourceArchives` / `HasMultipleArchives`。
- 新增 7 个 key（已合并进三份 JSON，1381 → 1388）：`Extract_TabOutput` / `Extract_TabAddArchives` / `Extract_SelectedArchives` / `Extract_RemoveSelected` / `Extract_OutputPickerHint` / `Extract_AddArchivesPickerHint` / `Extract_AddArchivesEmptyHint`。
- **未做（第三步后半）**：「选择要解压的文件」条目页、`SelectedEntryKeys`、拖拽与「解压到」场景第二页签接线。

### 与计划的偏差（累计 5 条）

1. 未做「按目录缓存选中」字典：简单模式 = 当前目录选中即结果、切目录清空；高级模式才跨目录累积。
2. `IsSimpleMode` 默认 false，模态壳层显式设 false（模态对话框保持原复选框 + 跨目录累积行为）。
3. 新增 `PathChanged` 事件（宿主回填目录）。
4. `TryConfirmFile` 改成 `ConfirmRequested` 事件（控件不直接弹窗）。
5. **新增 `HideAccumulatedPanel` 与 PickItems 扩展名白名单**：内嵌时宿主自己已有「已选」清单，控件内累积面板再显示一遍会重复且把文件列表挤成几十像素；解压窗「添加压缩包」只该列压缩包，故 `MatchesFileFilter` 在 PickItems 模式下按宿主给的 `fileExtensions` 过滤（扩展名清单取 Core 的 `ArchiveExtensionCatalog.EngineExtensions`，不硬编码）。

### 顺带修的三个真 bug（2026-10-30）

1. `CompressSettingsWindow.axaml` 用了 WPF 的 `Checked` / `Unchecked`（Avalonia 的 CheckBox 没有这两个事件）⇒ `AVLN2000`，而**增量构建把它吞成「0 警告」**，只有 `--no-incremental` 重建或跑测试（`XamlLoadException: No precompiled XAML found`）才暴露；改 `IsCheckedChanged`。
2. `CustomFilePickerControl.Initialize` 先写预选路径再 `NavigateTo`，而简单模式的 `NavigateTo` 会清空累积选择 ⇒ 构造时传入的源文件被抹掉；加 `_initializing` 闸。
3. `FileBrowserItem.CanCheck` 是普通自动属性（类继承 `ObservableObject` 却没走 `SetProperty`），行模板 `IsVisible="{Binding CanCheck}"` 不重算 ⇒ 勾了「高级模式」看不到复选框；改成会通知的属性。