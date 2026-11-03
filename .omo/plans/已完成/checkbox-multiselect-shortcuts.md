# 卡片视图复选框 + 视图菜单布局子菜单 + 快捷多选

> **状态**: ✅ 已完成（2026-11-03 归档，随 v0.5.6；实施记录见文末）
> **档位**: A 档 · v0.5.6 追加项（收尾追加）
> **创建**: 2026-11-03
> **来源**: 用户三条反馈

## 一、背景

v0.5.6 的复选框多选只在**详情（表格）**视图提供，存在两个体验缺口：

1. 平铺 / 内容行两种卡片视图里没有复选框，用户切到卡片布局后无法累积勾选；
2. 只能**逐条**点复选框，没有 Shift 连选 / Ctrl 增减这类快捷多选，成批挑选仍然费手。

同时布局切换只有工具栏一个「循环切换」按钮，想直接跳到某个布局得点两次，菜单里缺少直达入口。

## 二、用户诉求（原文）

> 1，我希望两个带缩略图的视图模式也有复选框。2菜单里面加上切换缩略图的子菜单。3，复选框多选只能一个一个添加，请增加快捷多选。

澄清问答的两个结论：

| 问题 | 用户选择 |
|------|---------|
| 「快捷多选」要哪些手势 | **Shift + 点击连选**、**Ctrl + 点击逐项增减**（明确不要 Ctrl+A、不要鼠标拖动框选、不要「勾选目录连带子树」） |
| 卡片视图里点一下卡片 | **点整张卡片即切换勾选**（不是只有点复选框才勾） |

## 三、设计

### 3.1 勾选算法独立成纯函数

`Services/ThumbnailSelectionController.cs` 与选区控制共用同一控制器（三种布局行为一致），新增：

```csharp
public sealed record CheckResult(
    ArchiveItemModel? Primary,
    IReadOnlyList<ArchiveItemModel> ToCheck,
    IReadOnlyList<ArchiveItemModel> ToUncheck,
    ArchiveItemModel? Anchor);

public static CheckResult HandleCheckPointerPressed(
    ArchiveItemModel? pressedItem, bool shift,
    IReadOnlyList<ArchiveItemModel> orderedItems, ArchiveItemModel? anchor)
```

- Shift：锚点到该行**区间全部勾上**（只加不减），锚点不变（连续 Shift 以首次点击为起点扩缩）；锚点失效则退化为「只勾这一行」；
- 无修饰键 / Ctrl：切换该行（已勾→取消，未勾→勾上）。

控制器只算「要变什么」，不改模型、不碰 UI ⇒ 可单测，且卡片视图与表格视图共用。

### 3.2 卡片视图

- 平铺项模板与内容行模板各加一个复选框（`IsHitTestVisible="False"`，点击交给整卡热区，避免与卡片各切换一次互相抵消），可见性绑 `IsCheckboxMode`；
- 卡片视图的 `PointerPressed` 在复选框模式下改为「整卡即勾选」，并维护独立的 `_thumbnailCheckAnchor`（与选区锚点是两套状态）；
- 勾选后同步卡片高亮（卡片高亮源是 `IsSelected`，须按 `SelectedEntries` 回填）。

### 3.3 表格视图的 Shift 连勾

DataGrid 自带的 Shift 只扩**行选区**、不扩勾选，所以在拖拽接线的 Tunnel `PointerPressed` 里拦截：命中复选框列的 CheckBox 且按住 Shift 时，直接走勾选控制器。

判断「是否命中复选框列」不能靠列标记（Avalonia 的 `DataGridCell` **不公开** `OwningColumn`，编译报 `CS1061`），改为自命中元素上溯：先遇到 `DataGridRow` = 别的列；先遇到 `CheckBox`（`DataContext` 为 `ArchiveItemModel`）= 复选框列。

### 3.4 视图菜单

`Controls/Menus/ViewMenuItems.axaml` 顶部新增「列表布局」子菜单（详情 / 平铺 / 内容，当前项用 `ToggleIconBox` 亮起）与「复选框多选」开关；`LocalizedStrings[...]` 绑定必须在 `UpdateLocalizedStrings` 的 keys 数组里登记，否则菜单文字空白。

## 四、任务与实现

| # | 任务 | 文件 | 状态 |
|---|------|------|------|
| T1 | 勾选控制器 `CheckResult` / `HandleCheckPointerPressed` | `Services/ThumbnailSelectionController.cs` | ✅ |
| T2 | 卡片视图复选框 + 整卡勾选 + 高亮同步 | `Controls/ArchiveContentView.axaml`、`Views/MainWindow.Thumbnails.cs` | ✅ |
| T3 | 表格视图 Shift 连勾（命中判定 + 拦截） | `Views/MainWindow.axaml.cs` | ✅ |
| T4 | 视图菜单布局子菜单 + 复选框开关 | `Controls/Menus/ViewMenuItems.axaml`、`ViewModels/MainWindowViewModel.cs` | ✅ |
| T5 | 直接指定布局的命令 `SetDetailsLayout/SetTileLayout/SetContentLayout`、`ToggleCheckboxMode` | `ViewModels/MainWindowViewModel.cs` | ✅ |
| T6 | i18n `Menu_ListLayout` / `Menu_CheckboxMode`（三语 + keys 登记） | 三份 `strings.*.json`、`MainWindowViewModel.cs` | ✅ |
| T7 | 单测 7 例 | `tests/MantisZip.UI.Avalonia.Tests/ThumbnailTests.cs` | ✅ |
| T8 | 真机验证（三布局 + 菜单 + Shift 连勾） | — | ✅ |

## 五、验收口径

1. 平铺 / 内容行视图在复选框模式下每张卡左上出现复选框，切布局后勾选仍保持；
2. 点整张卡片 = 切换该卡勾选，点复选框本身也生效（不会切换两次）；
3. Shift + 点击：从上次点击的行连选到当前行（只加不减）；
4. Ctrl + 点击 / 普通点击：切换单行；
5. 「视图 → 列表布局」三个子项可直接切换，当前布局有勾选标记；「复选框多选」菜单项等价于工具栏开关；
6. 关闭复选框模式后行为与引入前完全一致。

## 六、实施记录（2026-11-03）

### 偏差

1. **Shift 连勾的命中判定改用视觉树**：原计划按 `DataGridCell.OwningColumn.Tag` 判断，实际 Avalonia 不公开该属性（`CS1061`），改为「上溯时先遇到 Row 还是 CheckBox」判定。
2. **卡片勾选与选区拆成两套锚点**：`_thumbnailCheckAnchor` 与 `_thumbnailSelectionAnchor` 分开，否则 Shift 连勾会把选区一起扩出去。
3. **未加「勾选目录连带子树」**：用户明确不要。

### 验证

- Release `--no-incremental` 构建 0 警告；`-Task i18n` = 1430 key；`-Task check` 8 条守卫全绿；`-Task test -Target ui-tests` 45.3s 全绿（含新增 7 例）。
- 真机（`%TEMP%\mz-cb2.zip` 8 个文件，平铺布局）：点第 1 张卡 → 勾上；Shift 点第 4 张 → 1–4 全勾、状态栏「已选 4 项 · 共 44 B」；切到详情布局 → 4 行仍勾选；在详情里点 file5、Shift 点 file8 → 8 项全勾、「已选 8 项 · 共 88 B」；「视图」菜单显示「列表布局 ›（详情 / 平铺 / 内容，当前项打勾）」与「复选框多选 ✓」。

## 七、追加调整（2026-11-03，同日，用户追加要求）

**要求（原文）**：「我希望加上我按 shift 或 ctrl 多选之后，点击复选框，那么我多选的那些复选框状态跟着一起变，你能明白吗？」
**定案（用户选 A）**：行选择（蓝底高亮）与勾选**彻底分成两套状态** —— Ctrl/Shift 只改选区，点其中任意一行的复选框则把整组一起切换成与被点行相反的状态。

### 改动

| # | 内容 | 文件 |
|---|---|---|
| A1 | 新增 `CheckedEntries`（勾选投影，跨目录累积），`SelectedEntries` 退回「选区」单一职责；删除 `CheckedSelectionChanged` 事件与 `SyncCheckedFromSelection()` | `ViewModels/MainWindowViewModel.cs` |
| A2 | 新增 `SetChecked(items, value)` / `ToggleCheckedForGroup(clicked, group)`（选区 ≥ 2 且包含被点项 → 整组取反，返回 true） | 同上 |
| A3 | `FileListGrid_SelectionChanged` 去掉「复选框模式直接 return」与 `_isApplyingCheckboxSelection` 闸门（选区两种模式下都跟随 DataGrid）；新增 `SelectedGridItems()` 供成组切换取当前行选区 | `Views/MainWindow.axaml.cs` |
| A4 | 表格 Tunnel 拦截：无修饰键 + 命中复选框 + 成组成功 → `Handled`（阻止 DataGrid 把选区折叠成单选） | 同上 |
| A5 | 卡片视图 `ApplyCheckPointer(pressedItem, shift, ctrl, vm)` 三路径：Shift 区间连勾 / Ctrl 切换该卡并入出选区 / 无修饰键先试成组、否则单选并 `ApplyCardSelection` | `Views/MainWindow.Thumbnails.cs` |
| A6 | 批量删除 `targets` 改读 `CheckedEntries` | `ViewModels/MainWindowViewModel.Compress.cs` |

### 验证

- Release `--no-incremental` 0 警告；`-Task test -Target ui-tests` 39.8s 全绿、`-Task test -Target tests` 17.1s 全绿；`-Task check` 8 条守卫全绿。
- 真机详情视图：Ctrl 点 file2 / file3 / file4 → 点 file3 的复选框 → 三行一起勾、「已选 3 项 · 共 33 B」；再点一次 → 三行一起取消。
- 真机平铺视图：Ctrl 点三张卡 → 点第二张 → 三卡一起勾（选区保留），再点一次 → 三卡一起取消而蓝框仍在（`%TEMP%\g6-3cards.png` / `g7-groupuncheck.png` / `g8-groupcheck.png`）。