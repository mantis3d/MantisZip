# 树视图切换按钮选中态背景对比度修正

> **状态**: ✅ 已完成（2026-10-26）| **阶段**: [✅✅✅✅✅] (5/5)
> **前置依赖**: 无。与 2026-10-26 完成的图标归一化相邻（是那次验收时发现的），但不依赖其任何代码。

## TL;DR

> `ResultTreeView` 的「紧凑模式」「显示筛选条件」两个 ToggleButton 本地写死了 `Background="{DynamicResource ThemeButtonBgBrush}"` 与 `BorderBrush`，局部值优先级高于样式 setter，把 `App.axaml` 里 `ToggleButton:checked` 的强调色背景压掉了。于是选中时背景不变色、而里面的 `PathIcon` 被 `ToggleButton:checked PathIcon` 置白 —— 浅色主题下变成浅灰底 + 白图标，对比度不足。
>
> **交付内容**: `Controls/ResultTreeView.axaml` 删除两处本地覆盖 + 全仓同型排查 + 双主题目视
> **估算工时**: 1–2h | **难度**: 🟢低 | **并行**: 串行（改动面只有 2 个控件）

---

## Context

### 现状

- `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml`：
  - `CompactToggle`（:27-36）—— 紧凑模式开关
  - `FilterToggle`（:60-68）—— 显示筛选条件开关
  - 两者都写了 `Background="{DynamicResource ThemeButtonBgBrush}"` + `BorderBrush="{DynamicResource ThemeBorderBrush}"`，内含 16×16 `PathIcon`（`Classes="ToolbarButtonIcon"`）
- 结果：`App.axaml` 的 `ToggleButton:checked` 背景 setter **被局部值压掉** ⇒ 选中态背景始终是 `ThemeButtonBgBrush`（与未选中态一样）；与此同时 `ToggleButton:checked PathIcon` 把图标置白 ⇒ 浅色主题下浅灰底 + 白图标。
- 这是**改动前就存在**的老问题，不是 2026-10-26 图标归一化引入的（那次只是给图标挂了 `Classes="ToolbarButtonIcon"`，没有碰这两个 ToggleButton 的背景）。
- 参照物：`Controls/MainFilterBar.axaml`、`Controls/ArchiveContentView.axaml` 里的同类 ToggleButton **不写**本地 `Background`，选中态按主题强调色正常。

### 用户决策

| 决策项 | 选择 |
|--------|------|
| 处理方式 | 按建议修：删掉本地覆盖，让 `:checked` 的强调色生效（不采用「保留本地背景、让图标不置白」的备选方案） |

### 技术要点

- 规则 4：控件颜色必须走主题画刷键；选中态背景的**唯一入口**是 `App.axaml` 的 `ToggleButton:checked`。
- Avalonia 属性优先级：局部（local）值 > 样式 setter ⇒ 本地 setter 必须**删掉**，而不是改成另一个颜色（改成别的颜色同样压掉强调色）。
- 不许新增第二条 `:checked` 样式，也不许给这两个 `PathIcon` 加本地 `Foreground`（会压掉 checked 时的白色）。

---

## Work Objectives

### Core Objective

让 `ResultTreeView` 的紧凑/筛选切换按钮在选中时使用主题强调色背景（与工具栏其它 ToggleButton 一致），浅色与深色主题下图标都清晰可读。

### Concrete Deliverables

1. `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml` —— 两个 ToggleButton 删除本地 `Background` / `BorderBrush`（保留 `Padding`、`ToolTip`、`Command`、`Classes` 等）。
2. 全仓同型组合排查结论（`ToggleButton` 带本地 `Background="{DynamicResource ThemeButtonBgBrush}"` 且内含 `PathIcon`）。
3. `docs/progress-avalonia-detail.md` 细节条目一条。

### Must Have / Must Not Have

- **Must**：浅色主题选中态背景与工具栏其它 ToggleButton 表现一致。
- **Must Not**：不新增 `:checked` 状态样式；不改紧凑度资源键数值；不动 `App.axaml` 里 `PathIcon.ToolbarButtonIcon` 与 `ToggleButton:checked PathIcon` 的**定义顺序**（顺序是选中态白色的前提）。

---

## Tasks

### T1 去掉本地覆盖

删除 `CompactToggle`、`FilterToggle` 的本地 `Background` / `BorderBrush`，并加一行中文注释说明「局部值会压掉 `:checked` 的强调色背景，故这里不写」。

**验收**：`git diff` 只有删行 + 注释；`PathIcon` 的 `Classes="ToolbarButtonIcon"` 保持不变。

### T2 全仓同型排查

`rg -n 'Background="\{DynamicResource ThemeButtonBgBrush\}"' src/MantisZip.UI.Avalonia --glob '*.axaml'`，逐条判断是否为「ToggleButton/Button + 本地背景 + 内含 PathIcon」组合；把命中与结论写进实施记录（预期只有这两处）。

**验收**：命中清单完整，没有漏改的同类组合。

### T3 双主题真机目视

浅色、深色各跑一次，目视「紧凑」「筛选」两个按钮的**选中态**背景是否为强调色、图标是否白得清楚；未选中态与改动前一致。

**方法**（记录用）：临时把 `%LOCALAPPDATA%\MantisZip\settings.json` 的 `"Theme"` 改成 `Dark` 重启应用，拍完还原为 `System`。

**验收**：两张截图均通过目视；无「看不见的按钮」。

### T4 构建 + 测试

```powershell
pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia -ExtraArgsJson '["-c","Release"]'
pwsh -File scripts\verify-repo.ps1 -Task test  -Target ui-tests -ExtraArgsJson '["-c","Release"]'
pwsh -File scripts\verify-repo.ps1 -Task check
```

**验收**：三条退出码均为 0，且无新增警告（既有 xUnit1051/xUnit2012 不计）。

### T5 文档与提交

`docs/progress-avalonia-detail.md` 加条目；`git commit`（建议 `fix(avalonia): 树视图切换按钮选中态背景改用主题强调色`）；PROGRESS 不需要（非里程碑、无发布影响，按规则 18 也不进 RELEASE_NOTES——这是 2026-10-26 之前就存在的老样式问题，上个版本用户可见，故**是否进 RELEASE_NOTES 需向用户确认**）。

---

## 备选改法（不采用，留档）

**方案 B**：保留本地 `Background`，改为让图标在选中态不置白（给这两个 `PathIcon` 按状态显式设色）。

不采用的原因：与「不按状态改色」原则冲突；要为两个图标各写一条本地值，比删两行更脆；且背景与工具栏其它 ToggleButton 仍不一致。

---

## 验证策略

视觉类改动无法写断言 ⇒ 证据只有两种：

1. 浅色/深色双主题截图目视（T3）；
2. 既有 ui-tests 全绿（`ResultTreeView` 没有直接测试；`IconTestWindowMatrixTests` 只覆盖图标测试窗）。

---

## 风险与回滚

- **风险**：极低。只删本地 setter。
- **回滚**：`git revert` 单提交即可。
- **需要留意的点**：若某套主题没定义 `ToggleButton:checked` 背景，会退化为系统默认色 —— 本仓只有 `ThemeLight` / `ThemeDark` 两套，且 `App.axaml` 的 `:checked` 用 `DynamicResource`，两套都覆盖。

---

## 实施记录（2026-10-26）

### 代码落地

| 文件 | 改动 |
|------|------|
| `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml` | `CompactToggle`（原 :27-36）、`FilterToggle`（原 :60-68）删除本地 `Background` / `BorderBrush`，各加中文注释（计划项，原样落地） |
| `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` | 同型问题：`KeepOpenToggle` :1138、`OpenFolderToggle` :1149、`DeleteArchiveToggle` :1161 删除本地 `Background` / `BorderBrush`（保留 `BorderThickness="1"`） |
| `src/MantisZip.UI.Avalonia/Controls/QuickPathControl.axaml` | 同型问题：`FavoritesTab` / `HistoryTab` / `WindowsTab` / `TreeTab` 删除本地 `Background` / `Foreground` / `BorderBrush`；内部 4 个 `PathIcon` 的本地 `Foreground` 换成 `Classes="ToolbarButtonIcon"`（本地 `Foreground` 同样会压掉 checked 白色） |
| `src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml` | 同型问题：`AutoExpandTree` ToggleButton 内的 `PathIcon` 去本地 `Foreground` → `Classes="ToolbarButtonIcon"` |
| `src/MantisZip.UI.Avalonia/App.axaml` | 仅注释：:135-137 / :141-145 两段过时说明（「PathIcon 不继承父控件 Foreground」）改成与修订后规则 4 一致的表述；**样式定义与顺序未动** |

### 与计划的偏差

- **范围扩大**：计划只写 `ResultTreeView` 两处，T2 排查发现 `ProgressWindow`（3 个）与 `QuickPathControl`（4 个标签）是同一病根（本地 `Background` 压掉 `:checked`），一并修掉 —— 否则「全仓同型排查」这条验收无法通过。
- **图标本地 `Foreground`**：`QuickPathControl` / `ArchiveContentView` 的 ToggleButton 内 `PathIcon` 写成 `Classes="ToolbarButtonIcon"` 而不是「显式设置主题画刷」，因为显式色会压掉 `ToggleButton:checked PathIcon` 的白色。

### 验证

- 同型排查（改后）：`ToggleButton` 标签内带 `Background="{DynamicResource ThemeButtonBgBrush}"` → **0 命中**；ToggleButton 内 `PathIcon` 带本地 `Foreground` → **0 命中**。
- 构建 `-Task build -Target avalonia`（Release）→ ✅ 20.2s / 0 警告。
- 测试 `-Task test -Target ui-tests`（Release）→ ✅ 34.4s / 28 条既有 xUnit1051 / xUnit2012 警告，无新增；`-Task check` ✅。
- 真机双主题目视（Release 版 + 用户桌面）：浅色见 QuickPathControl「收藏」标签（`crop-tabs.png`）与 `ResultTreeView` 的 `CompactToggle`（`crop-rtv2.png`）；深色见 `dark-tabs.png` / `dark-rtv-toggles.png` / `dark-progress-toggles2.png`（进度窗「打开目录」）。三处宿主选中态均为强调蓝底 + 白图标，未选中态与原观感一致。
- 现场已清理：临时改的 `%LOCALAPPDATA%\MantisZip\settings.json`（`Theme=Dark` / `AutoCloseDelaySeconds=60`）已还原为 `System` / `3`，进程已退出。

## 决策记录

| 日期 | 决策 | 理由 |
|------|------|------|
| 2026-10-26 | 立项，走方案 A（删本地覆盖） | 与工具栏其它 ToggleButton 保持同一种写法，改动最小；方案 B 违背「不按状态改色」 |
