# 主窗口文件列表复选框多选（file-list-checkbox-selection）

> **档位**: A 档 · v0.5.6（文件列表体验）
> **工时**: 8-12h
> **关联 issue**: #37（压缩添加多选不方便 —— 其中「与主窗口文件列表统一」这一半本计划补上）
> **状态**: 未开始
> **执行方式**: 实现阶段尽量拆多个子代理并行（每个只碰约定文件），构建与测试由父 agent 统一跑（避免 `obj/bin` 互锁）。

## 背景

#37 里用户提的三件事，v0.5.5 做了前两件（选择器双模式 + 内嵌进压缩设置窗），第三件「与主窗口文件列表统一」只做了一半 —— **交互范式统一了，但主窗口列表本身没有复选框列**。

主窗口现在的多选能力：

- `Controls/ArchiveContentView.axaml:251` 的 `DataGrid x:Name="FileListGrid"` **没有显式 `SelectionMode`**，Avalonia DataGrid 默认 `Extended` ⇒ Ctrl 点选、Shift 范围选本来就能用。
- `Views/MainWindow.axaml.cs:1448-1457` 的 `FileListGrid_SelectionChanged` 把 `SelectedItems` 灌进 `MainWindowViewModel.SelectedEntries`（`List<ArchiveItemModel>`，`MainWindowViewModel.cs:560`）。
- 已经吃多选的：复制名称 / 复制路径（`MainWindowViewModel.cs:1348` / `:1369` 按 `SelectedEntries.Count > 1` 走多行）、「解压选中项到」（`MainWindowViewModel.Extract.cs:225` 的 `GetSelectedEntriesForExtract()`）。
- **没吃多选的**：`MainWindowViewModel.Compress.cs:104-122` 的 `DeleteFiles` 仍只看 `SelectedEntry` 单条。

所以缺的不是「能不能多选」，而是两件事：

1. **不用按键也能多选**：Ctrl/Shift 需要一只手按键盘，且滚动、切目录、点别处后选择就丢了；复选框是「持久累积」的语义。
2. **批量操作的落点**：删除还只支持单条，且状态栏不告诉你有多少项被勾选。

## 目标（用户可见）

1. 列表可以切换到「复选框模式」：每行前面出现复选框，勾选状态**跨滚动、跨切目录保持**，不依赖 Ctrl/Shift。
2. 关闭复选框模式时行为与今天**完全一致**（默认关闭）。
3. 勾选后能做的事：批量删除（一次确认、删完刷新）、批量解压（已支持）、批量复制路径（已支持）。
4. 状态栏显示「已选 N 项 · 共 X」，全选可见 / 清空勾选一键完成。

## 范围外

- 不做「拖拽已勾选条目到资源管理器」。
- 不改预览树（`ResultTreeView`）的选择模型。
- 不做「勾选态持久化到磁盘」（关掉压缩包就清空，与 `SelectedEntries` 同生命周期）。
- 不改 `DeleteEntriesAsync` 引擎契约（v0.5.4 已支持多路径 + 递归 + 零命中报错）。

## 设计

### 状态归属

- **唯一事实源仍是 `MainWindowViewModel.SelectedEntries`**（`List<ArchiveItemModel>`），勾选只是它的另一个入口，不新开一份集合 —— 否则复制路径 / 解压选中 / 删除三处要各自维护，迟早不一致。
- `Models/ArchiveItemModel.cs` 新增 `[ObservableProperty] private bool _isChecked;`（现在 16 个通知属性里没有它）。注释写明「仅主窗口列表复选框列使用；预览树复用本模型但不绑该属性」。
- VM 新增：
  - `public bool IsCheckboxMode { get; set; }`（持久化到 `AppSettings.FileListCheckboxMode`，默认 `false`）
  - `public int CheckedCount => SelectedEntries.Count`
  - `public string CheckedSummary => $"已选 {CheckedCount} 项 · 共 {FormatUtil.FormatSize(...)}"`（走 i18n key，不在 VM 里拼中文）
  - `[RelayCommand] CheckAllVisible()` / `ClearChecked()` / `InvertChecked()`
- 勾选/取消勾选的同步走一个 `_syncingSelection` 闸门（选择器控件 `CustomFilePickerControl` 已踩过这个坑：双向同步不加闸门会死循环）。

### 复选框列

- `Controls/ArchiveContentView.axaml` 在 22px 图标列（`:297`）**之前**插入 `DataGridTemplateColumn Width="30" Tag="Check"`，单元格 = `<CheckBox IsChecked="{Binding IsChecked, Mode=TwoWay}" />`。
- 列显隐：Avalonia 的 `DataGridColumn.IsVisible` **是否可绑定需要开工时核实**；若不可绑定，改由 code-behind 在 `IsCheckboxMode` 变化时切 `Column.Width = 0 / 30` 并配合 `IsVisible`。计划以「不可绑定也能落地」为准，不赌 API。
- 行高沿用 `RowHeight="{DynamicResource ControlHeightMd}"`（规则 7）；复选框尺寸不写死像素，用 `Width/Height="16"` 与列表图标一致。
- **点复选框不能触发预览加载**：勾选只改集合与高亮，`SelectedEntry` 的预览联动（`MainWindowViewModel.cs:846-849`）不动。

### 交互语义

| 操作 | 复选框模式 | 普通模式（今天） |
|---|---|---|
| 单击行 | 高亮 + 预览（**不自动勾选**） | 高亮 + 预览 |
| 双击行 | 行为不变（目录进入 / 文件解压） | 同 |
| 点复选框 | 加入 / 移出 `SelectedEntries` + 同步高亮 | 无此控件 |
| Ctrl / Shift 点选 | 仍然可用，且**同步勾选**（选中即勾选） | 只高亮 |
| 全选可见 | 当前列表里所有行置勾选 | — |
| 清空勾选 | 清空集合与勾选态 | — |

### 批量删除（补缺口）

`MainWindowViewModel.Compress.cs` 的 `DeleteFiles` 改成：`var targets = SelectedEntries.Count > 0 ? SelectedEntries : (SelectedEntry is null ? [] : [SelectedEntry])`；确认框文案按条数切（1 条沿用 `Confirm_DeleteFileFmt` / `Confirm_DeleteDirectoryFmt`，多条用新 key `Confirm_DeleteMultipleFmt`：「将删除选中的 {0} 项（共 {1}），此操作不可撤销」）；确认后一次性 `DeleteEntriesAsync(paths)` —— 引擎侧已支持多路径与「任一不存在就报错且不动包」。

## 任务分解

| # | 任务 | 文件 | 工时 |
|:--:|---|---|:--:|
| T1 | 模型与状态：`IsChecked` + VM 的 `IsCheckboxMode` / `CheckedCount` / `CheckedSummary` + `_syncingSelection` 闸门 + `PopulateEntries` 时订阅/退订行 `PropertyChanged` | `Models/ArchiveItemModel.cs`、`ViewModels/MainWindowViewModel.cs`、`ViewModels/MainWindowViewModel.Navigation.cs` | 1-2h |
| T2 | 复选框列：模板列 + 显隐切换（先核实 `DataGridColumn.IsVisible` 是否可绑定）+ 主题色 + 点复选框不触发预览 | `Controls/ArchiveContentView.axaml(.cs)` | 2-3h |
| T3 | 交互与摘要：勾选同步 `SelectedEntries` 与 `SelectedItems`、全选可见 / 清空 / 反选、状态栏摘要 | `ViewModels/MainWindowViewModel.cs`、`Controls/ArchiveContentView.axaml` | 2h |
| T4 | 批量删除接线 + 右键菜单项（「勾选 / 取消勾选」「全选可见」「清空勾选」）+ 新 i18n key | `ViewModels/MainWindowViewModel.Compress.cs`、`Controls/ArchiveContentView.axaml`、三份 `Localization/strings.*.json` | 2h |
| T5 | 持久化与入口：`AppSettings.FileListCheckboxMode` + 列表工具条 `ToggleButton`（图标按规则 8 登记）+ 设置页开关（规则 23 的 `settingswiring` 守卫要能过） | `Models/AppSettings.cs`、`Controls/ArchiveContentView.axaml`、`Views/SettingsWindow.axaml`、`ViewModels/SettingsWindowViewModel.cs` | 1h |
| T6 | 测试与验证：无头用例（勾选入集合、全选可见只作用当前列表、多选删除走批量、显隐切换后不残留）+ 真机冒烟 | `tests/MantisZip.UI.Avalonia.Tests/` | 1-2h |

**依赖**：T1 → T2 → T3 → T4；T5 可与 T4 并行；T6 最后。

## 验收口径

1. 默认（未开启复选框模式）与今天完全一致：单击高亮+预览、双击解压/进入、Ctrl/Shift 多选、复制路径多行。
2. 开启后：勾选跨滚动与切目录保持；「全选可见」只作用于当前列表；「清空勾选」一次清干净。
3. 勾选 3 项 → 右键删除 → **只弹一次确认**、文案显示「3 项」→ 删完列表刷新、勾选态清空。
4. 勾选后点另一行只是高亮+预览，**不会**把预览切到被勾选的行。
5. 关闭复选框模式后重新打开压缩包，勾选态不残留。
6. `-Task check` 全绿（版本六处 / 三语 key / 8 条守卫）；`-Task test -Target ui-tests`、`-Target tests` 全绿；`-Task build -Target avalonia -c Release --no-incremental` 0 警告。

## 风险与坑

| 风险 | 处置 |
|---|---|
| `DataGridColumn.IsVisible` 不可绑定（Avalonia 12 API 差异） | 开工第一步核实；不可绑定就用 code-behind 切 `Width`（`0` / `30`），不赌绑定 |
| 勾选与 `SelectedItems` 双向同步死循环 | 单一 `_syncingSelection` 闸门，进出都判（选择器里已踩过） |
| 勾选触发预览重载（每次勾选都解包一次） | 勾选路径不碰 `SelectedEntry`；预览联动只在行选择变化时跑 |
| `ArchiveItemModel` 被预览树复用，加字段可能被误绑 | 只加 `IsChecked` 并注释；预览树模板不引用该属性 |
| 10000+ 条目勾选卡顿 | 集合用 `List` 但同步走「只处理变化项」；DataGrid 虚拟化默认开启，不额外遍历全量 |
| 删除多选时部分路径不存在 | 引擎已是「任一不存在就整批不动并报错」，UI 只需把异常文案透出（v0.5.4 已做 `ArchiveEntryNotFoundException` 本地化） |

## 与其它计划的关系

- 与 `file-list-thumbnails.md`（v0.5.6 缩略图）**同改 `ArchiveContentView`**：谁先做谁先占，后做的要 rebase 列布局（复选框列插在图标列之前，缩略图模式只影响行模板）。
- 与 `compress-job-queue.md`（v0.5.8）无交集：那边改压缩入口，这边改主窗口列表。
- 与 `custom-columns.md`（列自定义）有潜在冲突：若列显隐将来做成可配置，复选框列应作为固定列不参与自定义。
