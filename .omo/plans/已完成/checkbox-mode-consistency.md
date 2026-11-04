# 复选框模式一致性（文件列表 ⇄ 压缩/解压选择器）

> **状态**: ✅ 已完成（2026-11-03 归档，随 v0.5.6；实施记录见文末）
> **档位**: A 档 · v0.5.6 追加项
> **工时**: 4-6h

## 一、背景

用户 2026-11-03 提出两条（原文）：

1. 文件列表里切换复选框状态时，要把当前的选择状态转换为对应的目标状态；
2. 把压缩和解压里用复选框选择文件的交互，改成跟文件列表里面一致。

第一条的问题在于：`OnIsCheckboxModeChanged` 原本只保存设置、关闭时清空勾选，**没有任何状态转换**。
打开复选框模式时原来的蓝底选区不会变成勾选，关闭时勾选被直接清空而不是退回选区 —— 用户看着一堆高亮行打开开关，结果一个勾都没有。

第二条的问题在于：选择器用 `IsSimpleMode` 做「简单/高级」二选一，高级模式是「单击行即勾选、只加不减」，
没有 Ctrl/Shift 连勾、没有整组切换、没有右键菜单。同一个软件里两套勾选手势，用户得分别学。

## 二、与用户确认的四点（2026-11-03）

| # | 问题 | 采纳 |
|---|---|---|
| 1 | 关闭复选框模式时怎么处理勾选 | 当前目录内的勾选 → 选区；其它目录的勾选丢弃 |
| 2 | 打开复选框模式时怎么处理选区 | 选区并入勾选（保留原有勾选） |
| 3 | 选择器那个模式开关叫什么、默认什么 | 改名「复选框模式」、默认关（等于原来的简单模式） |
| 4 | 选择器里单击行的语义 | 与文件列表一致：点行只高亮，点复选框才勾 |

## 三、实现

| # | 任务 | 落点 |
|---|---|---|
| T1 | 打开复选框模式：选区并入勾选 | `ViewModels/MainWindowViewModel.cs` 的 `OnIsCheckboxModeChanged` → `SetChecked(SelectedEntries.ToList(), true)` |
| T2 | 关闭复选框模式：勾选退回当前目录选区 | 同处：`CheckedEntries.Where(CurrentEntries.Contains)` → `ApplySelection(...)` |
| T3 | 新增 `ApplySelection`（先 SelectedEntry 再填 SelectedEntries） | `MainWindowViewModel.cs`；顺序是硬约束，见「偏差 1」 |
| T4 | 视图侧回填 DataGrid 选区 | `Views/MainWindow.axaml.cs`：`ApplyVmSelectionToGridCore` + 闸门由 `OnCheckboxVmPropertyChanged` 负责 |
| T5 | 选择器属性改名并反转语义 | `Controls/CustomFilePickerControl.axaml.cs`：`IsSimpleMode` → `IsCheckboxMode`（默认 false），全文件各判定点逐一反写 |
| T6 | 选择器手势与主窗口统一 | 新增 `FileList_PointerPressed`（Tunnel）/ `CheckRange` / `ApplyChecked` / `UpdateAccumulatedCore`，以及右键三项 `CheckAllVisible_Click` / `InvertChecked_Click` / `ClearChecked_Click` |
| T7 | 宿主窗与 i18n | 压缩窗、解压窗、独立模态选择器三处接入；`Extract_PickerAdvancedMode` / `Compress_PickerAdvancedMode` 合并为 `Picker_CheckboxMode` |
| T8 | 回归测试 | `CustomFilePickerCheckboxModeTests`（6 例，真实指针）、两个内嵌选择器测试文件同步改写 |

## 四、偏差与踩坑

1. **`ApplySelection` 的赋值顺序是硬约束**：先 `SelectedEntry`、后填 `SelectedEntries`。反过来写时，`SelectedEntry` 的
   双向绑定会触发一次「单选回写」，把刚填好的三行选区压成一行（当时真机现象：关闭复选框模式后只剩第一行被选中）。
2. **视图闸门要覆盖「列显隐 + 选区回填」整段**：`OnCheckboxVmPropertyChanged` 里增减列、改 `SelectedItems` 都会重置行选区，
   闸门只罩回填那一步不够。
3. **选择器 `IsCheckboxMode` 的 setter 去掉了「同值早退」**：初始值就是 false，早退会让 XAML 里挂上的右键菜单在非复选框模式下
   继续存在（标题空白、点了没反应）。现在总是同步一次。
4. **无头测试必须自己挂主题**：测试宿主只配了裸 `Application`，ListBox 模板来自主题，不挂 FluentTheme 就一个
   `ListBoxItem` 都不生成（表现为 `rows.Count == 0`）。
5. **测试里要显式导航**：初始目录由 `AppSettings.DefaultPathOrder` 决定，测试环境的顺序里未必有 `context`，
   所以 `Initialize(initialPath: dir)` 之后要再 `TryNavigate(dir)`。

## 五、验证

- 构建：`-Task build -Target avalonia -ExtraArgsJson '["-c","Release","--no-incremental"]'` → 0 警告。
- 测试：`ui-tests` 全绿（新增 6 例）；`tests` 15.1s 全绿；`-Task i18n` **1437** key；`-Task check` 8 条守卫全绿。
- 真机：Ctrl 选 3 行 → 打开复选框模式 → 三行全部勾上、蓝底保留、状态栏「已选 3 项 · 共 33 B」；
  再切回普通模式 → 三行恢复蓝底选中（bug 修复前只剩一行）。
- 选择器：由 `CustomFilePickerCheckboxModeTests` 的真实指针用例覆盖（关掉时点行即结果、打开后点行只高亮、
  点复选框才累积、Shift 连勾整段、右键菜单只在复选框模式下挂上）。

## 六、留白

- 选择器里「点行只高亮」与「ListBox 的 Ctrl/Shift 扩展选区」是两套语义：后者仍在，只是不再影响勾选集合；
  真机上如果用户先 Ctrl 选一片再点复选框写 `CheckBox` 的视觉反馈，需要下一轮实测再决定是否要给复选框列加「整组切换」提示。
- 详情布局的「蓝底选区」与「复选框」目前分离（2026-11-03 同日解耦），压缩/解压窗沿用同一模型；将来若引入
  「勾选即删除目标」的隐式语义，需要重新评估选区的作用域。