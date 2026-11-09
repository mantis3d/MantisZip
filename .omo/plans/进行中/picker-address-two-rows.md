# 文件选择器地址区拆两行（按钮行 / 面包屑行）

> **状态**：已完成并验证（2026-11-05，`beta` 分支），待用户确认 `RELEASE_NOTES.md` 范围后归档
> **关联代码**：`Controls/CustomFilePickerControl.axaml`（唯一改动点）、`App.axaml`（`Grid.addrRow` 注释）、`AGENTS.md` 规则 4

## 目标

地址区从「一行挤 6 个方形按钮 + 面包屑」改成两行：**导航按钮行**（左：后退 / 前进 / 向上 / 系统浏览；右：新建文件夹 / 添加收藏）、**面包屑行**（`PathBreadcrumb` 独占整宽）。

## 背景与数字

`Grid.addrRow > Button` 是 `ControlHeightMd` 见方（28/32/38 三档），间距 `SpacingXxs` = 2px。改造前 6 个按钮占 202–238px，面包屑在内嵌 620px 列里只剩 ≈400px，窗口拉到 `MinWidth 900`（左列 520px）时只剩 ≈300px，路径被折成 `E:\ … \Controls`。拆行后面包屑 ≈504px，代价是垂直多占 36–46px。

## 设计（已与用户确认）

```
[←][→][↑][📂]                                        [📁+][☆]
[ E:\github\MantisZip\src\MantisZip.UI.Avalonia\Controls          ]
```

- 按钮行仍挂 `Classes="addrRow"`，列定义 `Auto,Auto,Auto,Auto,*,Auto,Auto`（`*` 空列把右侧动作组推到行尾）。
- 面包屑行是独立的 `Grid`（不挂 `addrRow`，无按钮），`PathBreadcrumb` 不再写 `Grid.Column`。
- 用户明确要求：**新建文件夹与添加收藏放在按钮行的右端**（不是面包屑行）。

## 非目标

- 不改 `addrRow` 样式本身（尺寸 / 三色 / Padding）。
- 不动其它持有 `PathBreadcrumb` 的宿主（`QuickPathPicker`、`ArchiveContentView`、`UiTestWindow`）—— 它们各有自己的布局。
- 不新增 i18n key、不新增图标。
- 不处理 KI-014（内嵌「系统浏览」死按钮），本计划只搬位置不改接线。

## 任务

- [x] **T1** `CustomFilePickerControl.axaml` 拆行：Row 0 改为「外层 `StackPanel`（`Spacing=SpacingXxs`）→ 按钮行 `Grid`（`Classes="addrRow"`）→ `PathBreadcrumb` 整宽行」。
  - 实施偏差：**没有**改外层 `RowDefinitions`，也没迁移 `Grid.Row` —— 用「Row 0 内部套一个 StackPanel」表达两行，`BrowserGrid` / `FileNameArea` / `OptionsRow` 保持 1/2/3，少改三处、少一个错位风险。
- [x] **T2** 新增 `tests/MantisZip.UI.Avalonia.Tests/CustomFilePickerAddressLayoutTests.cs`（2 例）：面包屑 ≥ 控件宽度 80%；整行按钮在面包屑上方且新建 / 收藏在行尾。旧布局实测 392/620 = 63%，先红后绿。
- [x] **T3** 文档同步：`App.axaml` 的 `Grid.addrRow` 注释、`AGENTS.md` 规则 4 的 `addrRow` 表格行与用法条目、`CustomFilePickerControl.axaml` 头部注释。
- [x] **T4** 验证与归档：`build -Target avalonia` 0 警告、`test -Target ui-tests` 46.8s 全绿、`-Task check`、`-Task guards` 8 条；`docs/progress-avalonia-detail.md`（2026-11-05）、`docs/PROGRESS.md`（并入 11-04 条目）、`docs/PLAN.md` 同步。
  - 待用户定：`RELEASE_NOTES.md` 是否更新及范围（规则 18）。

## 决策记录

| # | 决策 | 依据 |
|---|------|------|
| 1 | 拆两行而不是「把按钮收进溢出菜单」 | 用户 2026-11-05 选 A：路径宽度翻倍是最直接的收益，溢出菜单会让两个新入口不可发现 |
| 2 | 新建 / 添加收藏放按钮行右端，而不是面包屑行右端 | 用户 2026-11-05 明确要求（我原本建议的 C 方案被否） |
| 3 | 面包屑行不挂 `addrRow` | 该行没有按钮；挂了也不会命中 `Grid.addrRow > Button`，但会误导下一个维护者 |

## 风险与边界

- **垂直成本**：+36–46px（三档紧凑度）。解压窗默认 760 高影响可接受；拉到 `MinHeight 560` 时文件列表更矮 —— 只能真机目视，headless 测不出「矮得难受」。
- **不新增 `ControlHeight` 资源**：行高沿用 `addrRow` 的 `ControlHeightMd`，面包屑高度由 `PathBreadcrumb` 自身决定，两行间距用 `SpacingXxs`/`SpacingXs`，不引入魔法数字。
