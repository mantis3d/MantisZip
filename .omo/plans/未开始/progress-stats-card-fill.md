# 进度窗「完整」档：统计卡行改为底纹进度条

> **状态**: 📋 待实施 | **创建日期**: 2026-11-27 | **最近修正**: 2026-11-27
> **关联计划**: issue #81（进度窗口信息冗余）、`progress-stats-cards-three-row.md`（✅ 已完成的 9 张三行统计卡，本计划在它的容器上加底纹）、`progress-bar-segments.md`（按压缩包状态分段着色，本计划**不做**，已排 v0.5.10）
> **范围（2026-11-27 扩为三件）**: ① 完整档统计卡行改底纹进度条 ② 状态面板 7 行 → 3 行 ③ **列表档卡行改成可点统计卡做过滤**（§2.9）
> **前置**: 无（纯 Avalonia UI 层，规则 11）
> **决策来源**: 2026-11-27 与用户讨论逐条拍板（见 §八）

## TL;DR

把「完整」档（`InfoDensity = Full`）统计卡行的那块 `Border` 背景改成**底纹进度条**：进度直接铺在 9 张统计卡后面，不再占用独立一行。多态配色 —— 进行中按 `PercentComplete` 截断、**不确定态（准备 / 扫描）填充淡黄**、完成转绿 / 失败转红 / 取消转灰（判定统一走 `ProgressStatusKind`）。

**同一批顺带做四件事（前三件把状态面板从 7 行压到 3 行）**：

1. 视图切换 **两行 → 一行五档**（极简 / 标准 / 详细 / 完整 / 列表，修掉「简约 vs 精简」词冲突与 `IsMinimalDensity` 无内容绑定的缺陷）
2. 9 张卡 **压进一行**（卡片 `MinWidth` 80 → 72、间距 24 → 8 ⇒ 912px → 712px）
3. 段 2（并行 / 速度 / 压缩后）+ 百分比 + 时间 **合并成底部一行**
4. **列表档的卡行换成可点统计卡**（§2.9）—— 五张：已完成 / 跳过 / 出错 / 已覆盖 / 全部，尺寸与完整档十张卡完全一致，点卡即过滤列表、再点回「全部」。同时补齐「已完成」的计数（`ProgressViewModel` 现在只有跳过 / 出错 / 已覆盖三个累加器）

配套：卡片行从横向 `StackPanel` 改 `WrapPanel`（窄窗自然换行而不是被 `ClipToBounds` 硬切），独立总进度条改成「完整档时隐藏」。

HTML 原型：`docs/prototypes/progress-stats-card-fill.html`（底纹与紧凑化：浅/深 × 七状态 × 880/560 窗口）、`docs/prototypes/progress-filter-cards.html`（列表档可点卡：主题 × 档位 × 合并前后 × 总大小卡 四个开关）。

## 一、背景

`issue #81` 反馈进度窗口「同一个 40% 出现 4 次」「三根进度条粗细圆角各不相同」。当前 `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`（1441 行）在「完整」档同时出现：

| 位置 | 控件 | 行号 |
|---|---|---|
| Row 3 统计卡行 | 9 张三行统计卡（`StatsCardsPanel`） | `:701-705` 容器 / `:711` 卡片行 |
| Row 4 百分比 | `PercentComplete` 12px Bold | `:1183-1186` |
| Row 4 总进度条 | `ProgressBar` + `IsIndeterminate` 绑定 | `:1190-1195` |
| Row 5 时间行 | 已用 / 剩余 | `:1197-1208` |

独立总进度条与卡片行上下并列，意义重复；且卡片行本身已经溢出：

- 卡片 `MinWidth="80"`（模板内 10 处）
- 卡片间距 `Spacing="{DynamicResource SpacingLg}"`，Normal 档 = **24**（`src/MantisZip.UI.Avalonia/App.Theming.cs:186-188`：Compact 16 / Normal 24 / Loose 32）
- ⇒ **9 × 80 + 8 × 24 = 912px**，而窗口 `MinWidth="500"`、默认宽 800，`RootGrid` 带 `ClipToBounds="True"`（`:50`）

现在没暴露，是因为大多数包只上报 2-4 张卡（其余按 `HasScanCount` / `HasStatsArchive` 之类绑定隐藏，见 `:720` `:749`）；自适应 ZIP 且跳过 / 出错 / 已压缩 / 仅存储全上报时会被硬切。

## 二、方案

> **2026-11-27 第二轮追加（用户：「挺好，状态面板里面能再紧凑一些吧？我希望一行就能排下」）**：本计划在底纹之外再收三件事 —— ①**视图切换从两行合并成一行五档**（§2.7）②**9 张卡压进一行**（卡片最小宽 80 → 72、间距 24 → 8，§2.4 改写）③把状态面板的总高度从 **7 行压到 3 行**（§2.8 高度账）。三件事都已在 `docs/prototypes/progress-stats-card-fill.html` 原型里验证过。

### 2.1 底纹落在哪

`StatsCardsPanel` 是 `Border`（`:701-705`），已有 `Background` / `CornerRadius` / `Padding`。把 `Background` 从静态 `{DynamicResource ThemeSurfaceBgBrush}` 改成 `MultiBinding`，输出一个 `LinearGradientBrush`：

```
GradientStops:
  [0]          填充色
  [offset]     填充色        ← offset = PercentComplete / 100
  [offset]     Transparent
  [1]          Transparent
```

写法直接照抄 `src/MantisZip.UI.Avalonia/Converters/BatchStatusConverters.cs:67-102` 的 `ProgressStatusToBackgroundConverter`（批处理行已经在用同一套 4-stop 渐变做行底纹，alpha = `0x59` ≈ 35%）。差别只有两点：它按 `BatchItemStatus` 选色，我们要按「不确定 / 确定 / 完成 / 失败」选色。

**为什么不用做法 B（卡片行下面叠一条细条）**：那是第三根进度条，不解决冗余。

### 2.2 多态配色

**判定依据用 `StatusKind` 而不是布尔标志**。`ProgressViewModel.cs:450-451` 有 `[ObservableProperty] private ProgressStatusKind _statusKind`，枚举定义在同文件 `:19-33`：

```csharp
public enum ProgressStatusKind { Preparing, Running, Paused, Completed, Failed, Cancelled }
```

它带变更通知（`partial void OnStatusKindChanged` 在 `:474`），可以直接绑。**不要绑 `HasFailures`**（`:900` 是只读计算属性，没有对应的 `OnPropertyChanged`，值变了 UI 不会刷新）。

| 状态 | 判定 | 填充色 | 宽度 |
|---|---|---|---|
| 不确定（准备 / 扫描） | `IsIndeterminate == true` | `ThemeStatusWarning` 的 **15% 透明变体** | 满宽 |
| 进行中 | `StatusKind == Running` | `ThemeProgressFillBrush` | `PercentComplete / 100` |
| 已暂停 | `StatusKind == Paused` | `ThemeProgressFillBrush` 降到 50% alpha | `PercentComplete / 100` |
| 完成 | `StatusKind == Completed` | `ThemeStatusSuccess` 15% 透明 | 满宽 |
| 失败 | `StatusKind == Failed` | `ThemeStatusError` 15% 透明 | 满宽 |
| 已取消 | `StatusKind == Cancelled` | `ThemeTextSecondaryBrush` 15% 透明 | 满宽 |

- `IsIndeterminate` 优先于 `StatusKind`：扫描阶段（`:1157-1158`）会同时置 `StatusKind = Running` 和 `IsIndeterminate = true`，此时要显示黄色而不是蓝色。
- 暂停态用「同色降 alpha」而不是换色，避免和「完成绿 / 失败红」抢注意力。

- 色值来源（已有）：`ThemeStatusWarning` 浅色 `#FFFFE082` / 深色 `#FF5C4A1E`；`ThemeStatusSuccess` 浅 `#FF4CAF50` / 深 `#FF2E7D32`；`ThemeStatusError` 浅 `#FFF44336` / 深 `#FFC62828`；`ThemeProgressFillBrush` 浅 `#FF2196F3` / 深 `#FF64B5F6`。
- **不用纯黄 `#FFFFEB3B`**：底纹上太刺眼，且主题里已有语义化 warning 色，自造会漂。
- **与「真警告」分层**：`ThemeStatusWarningBrush`（不透明）现在用于「磁盘空间不足」「同名文件冲突」这类真警告；底纹只取 15% 透明底，视觉上不抢。

### 2.3 不确定态为什么改成颜色而不是沿用滚动动画

`ProgressViewModel.cs` 有两处置 `IsIndeterminate = true`：

- `:1025` —— 准备态（开窗那一下）
- `:1158` —— 扫描阶段（`ArchivePhase.Scanning`，注释原文「总数未知：进度条滚动而非僵在 0%」），`:1162` 离开扫描阶段时置回 false

底纹是渐变，没有「滚动」可言。改用**满宽淡黄**表达「还没开始算进度」：

- 扫描阶段有动态计数文本兜底 —— `:1285` 会填 `ScanCountText = T("Progress_FileCount_Scanning", p.TotalFiles)`，卡片区「扫描」卡（`:720` 绑 `HasScanCount`）会实时变化，用户在文字上能看到「在动」。
- 准备态很短（就是开窗那一下），静止可接受。

**不做滚动动画**（省 1-2h）。如果实测觉得扫描期「一直黄着不动」像卡死，再单独加一个 `Animation` 循环扫光，不阻塞本计划。

### 2.4 卡片行改 WrapPanel 且压进一行

`ProgressWindow.axaml:711-713`：

```xml
<StackPanel Orientation="Horizontal"
            Spacing="{DynamicResource SpacingLg}"
            HorizontalAlignment="Center">
```

改成：

```xml
<WrapPanel Orientation="Horizontal"
           HorizontalAlignment="Center">
```

**同时把卡片挤进一行**（这是「状态面板一行排下」的核心）。当前 9 × 80 + 8 × 24 = 912px 放不进 800 窗口，两个方向同时收：

| 项 | 现状 | 改后 | 节省 |
|---|---|---|---|
| 卡片 `MinWidth` | 80（模板内 10 处硬编码） | **72** | 9 × 8 = 72px |
| 卡片间距 | `SpacingLg` = 24（Normal 档） | **8**（`SpacingXs` 附近的值，逐卡时按紧凑度档缩放） | 8 × 16 = 128px |
| **合计宽** | **912px** | **712px** | **−200px** |

712px 在默认 800 窗口里宽松排得下；560px 窄窗才换行。卡片内部也在同一批里收一档字号，否则 72px 装不下「已压缩 / 12 字」这类标签：

- 图标 16 → **14**
- 标签 `.r1 .lab` 11 → **10.5**
- 数值 `.r2` 15 → **13**（加 `line-height:1.3`）
- 副值 `.r3` 13 → **11.5**（加 `line-height:1.3`）

**注意**：`WrapPanel` 没有 `Spacing` 属性（Avalonia 与 WPF 都没有），间距要落到子项的 `Margin` 上。做法二选一：

- **方案 i**：给 9 张卡的模板外层容器统一加 `Margin="{DynamicResource SpacingSmThk}"`（会产生左右各一份，视觉间距是 `SpacingSm` 的两倍，需要按实际值调）
- **方案 ii**：在 `Window.Styles` 里写 `Style Selector="WrapPanel > Border"` 加 `Margin`，避免逐卡改

倾向 **ii**（一处改完，且不影响其他 `Border`）。改完要同步 `Padding` 抵消首尾多出的 margin。

> **72px 的可行边界要真机验**：原型里用 HTML 量的是近似值，Avalonia 的字号与 `MinWidth` 实际表现需在 500 / 800 / 1200 三档窗口宽度下各看一次。若 72px 下「已压缩」这类三字标签仍被挤到换行，退一步到 76px（此时 9 × 76 + 8 × 8 = 748px，800 窗口仍放得下）。

### 2.5 独立总进度条的去留

`StatsCardsPanel` 只在 `IsFullDensity` 时存在，极简 / 标准 / 详细三档没有底纹，**仍必须靠独立总进度条**。所以：

- `:1190-1195` 那根不能删
- 改成 `IsVisible="{Binding IsFullDensity, Converter={StaticResource BoolInvertConverter}}"`（`BoolInvertConverter` 已在 `:39` 注册）
- 由此得到：完整档只有底纹，其他三档只有独立条，**任何一档都只有一根进度条**

### 2.6 百分比行

`:1183-1186` 的 `PercentComplete` 12px Bold 保留（底纹上不写数字，避免 9 卡行又添一处重复）。它是「进度条」的标签，与底纹配套，不算重复。

**但这一行不单独占行**：与段 2（并行 / 速度 / 压缩后）和时间行合并成底部一行（§2.8）。

### 2.7 视图切换：两行合并成一行五档

现状是**两套互不相干的枚举各占一行**：

| 行 | 控件 | 绑定 | 取值 |
|---|---|---|---|
| 内容模式 | `RadioButton` × 3（`:296-320`） | `ProgressViewModel.ContentMode` | `ProgressContentMode { Simple, Detailed, List }` |
| 信息密度 | `RadioButton` × 3（`:645-651`） | `ProgressViewModel.InfoDensity` | `ProgressInfoDensity { Minimal, Medium, Full }` |

这是 issue #81 第 2 条的核心病灶 —— 「简约」（ContentMode）与「精简」（InfoDensity）两个近义词各表示一次"少"，用户无法区分；且 `IsMinimalDensity` 全 XAML 里只出现在它自己那个 `RadioButton.IsChecked`（`:655`），**没有任何内容绑定它**，所以「精简」档实质上不改变任何东西。

**改成一个 5 档控件**，内部映射两维度：

| 档位 | ContentMode | InfoDensity | 用户看到什么 |
|---|---|---|---|
| 极简 | `Simple` | `Minimal` | 只有压缩包名 + 总体进度 |
| 标准 | `Simple` | `Medium` | + 中等单行 + 批列表 8 行 |
| **详细**（新增，用户提议） | `Detailed` | `Medium` | 详细列表 + 批列表 8 行 |
| 完整 | `Detailed` | `Full` | + 9 张统计卡（底纹版） + 批列表 11 行 |
| 列表 | `List` | `Full` | 纯列表视图 + 9 张卡 |

**五档是单调递增的阶梯**，用户不必理解两个维度。

**迁移**：

- 新增 `AppSettings.ProgressDetailLevel`（枚举 `ProgressDetailLevel { Minimal, Standard, Detailed, Full, List }`）。
- 两个旧字段 `ProgressContentMode` / `ProgressInfoDensity` **保留不删**（老配置读得到；写完新字段后旧字段不再被读）。
- 读不到 `ProgressDetailLevel` 时按旧两字段反推：`Minimal→极简` / `(Simple,Medium)→标准` / `(Detailed,Full)→完整` / `(List,*)→列表`；被丢掉的 4 种组合（`Simple+Full` / `Detailed+Minimal` / `List+Minimal` / `List+Medium`）映射到相邻档。
- 存量测试里若有断言这两个字段的，需同步。

**宽度风险（真机必量）**：5 个单选 + 标签在一行约 450-520 逻辑像素，而窗口 `MinWidth="500"`（`:11`）正好卡边缘。三档兜底：①区间收紧（`Spacing` 16 → 12）②标签缩成两字（「极简」→「少」、「完整」→「全」）③改图标分段控件。真量后选哪档写进实施记录。

### 2.8 状态面板高度账：7 行 → 3 行

| 块 | 改造前 | 改造后 |
|---|---|---|
| 视图切换 | 2 行（内容模式 + 信息密度） | **1 行**（五档，§2.7） |
| 统计卡 | 1 行（但 912px 溢出被 `ClipToBounds` 硬切） | **1 行**（712px 完整可见，§2.4） |
| 段 2（并行 / 速度 / 压缩后） | 1 行 | **0**（并入底部一行） |
| 百分比 | 1 行 | **0**（并入底部一行） |
| 时间（已用 / 剩余） | 1 行 | **0**（并入底部一行） |
| 合并后的底部一行 | — | **1 行** |
| 独立总进度条 | 1 行 | **0**（进底纹，§2.5） |
| **合计** | **7 行** | **3 行** |

底部合并行的布局（原型已验证）：

```
左：[⚙ 4] · [⚡ 6.8 MB/s] · [🗜 11.76 MB (28%)]
右：                                31% · 已用 00:12 · 剩余 00:26
```

- 元信息用图标代替文字标签（「并行」→ `⚙`、「压缩后」→ `🗜`），ToolTip 补全语义。
- 压缩汇总项（`🗜 … (率%)`）只在 `HasOverallCompression` 为真时显示（真机仅 ZIP 且完成/失败时），与现状一致。
- 扫描态的「已发现 N 个文件」原本占百分比行左侧，合并后位置被挤 ⇒ 移到左侧元信息区（`🔍 1284`），或扫描期临时替换百分比位置。**这一条待真机定**（原型里暂让它消失，因为「扫描」卡已经在显示计数）。

### 2.9 列表档：把统计卡行换成「可点统计卡」做过滤

**问题**：`InfoDensity = Full` 时「完整」与「列表」两档都显示卡行，而列表档的卡行与列表面板的逐条目列表冲突 ——
用户要「只看出错的文件」（issue #81 之外的独立需求，用户 2026-11-27 提出），
而卡行里的「跳过 3 / 出错 1 / 已覆盖 1」既是统计、又是天然的过滤入口。同一串数字在两处重复出现，正是 issue #81 批的冗余。

**做法**：卡行**在列表档换成可点版本**，尺寸与结构完全一致，只多边框 / hover / 选中态：

| | 完整档（Detailed + Full） | 列表档（List + Full） |
|---|---|---|
| 卡片数 | 10 张只读 | 5 张可点 |
| 内容 | 扫描 / 压缩包 / 已处理 / 暂存 / 压缩后 / 仅存储 / 跳过 / 出错 / 已覆盖 / 总大小 | 已完成 / 跳过 / 出错 / 已覆盖 / 全部 |
| 度量 | `MinWidth=72`、三行同构 | **完全一致**（否则切档会跳） |
| 边框 | 无 | 1px `ThemeBorderBrush` |
| hover | 无（`IsHitTestVisible=false`） | 提亮 |
| 选中 | 不适用 | 强调色边框 + 淡底 |

**为什么去掉另外五张**（全部有实测依据）：

| 卡 | 数据源 | 列表档为什么不要 |
|---|---|---|
| 🔍 扫描 | `_scanFoundFiles = p.TotalFiles`（`ProgressViewModel.cs:1283`） | 与「全部」重复 |
| 📦 压缩包 | `_statsArchiveCurrent` / `_statsArchiveTotal` | 批处理区表头已有 `BatchCountText`（`ProgressWindow.axaml:78`） |
| ✅ 已处理 | `_statsProcessed = p.ProcessedFiles`（`:1468`） | **含跳过与出错**，语义与「已完成」重叠 |
| 📥 暂存 | `_statsStagedBytes`（仅 7z solid 批量解压） | 解压专属，压缩流程恒为空 |
| 🗜 压缩后 / 💾 仅存储 | `_statsCompressed` / `_statsStoreOnly`（仅自适应 ZIP） | 引擎内部分类，切回完整档就能看 |
| 📏 总大小 | `_statsTotalBytes = p.TotalBytes`（`:1478`） | **保留** —— 挪成「全部」卡的第三行（`41.76 MB`） |

**卡片顺序与交互**：

```
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
│ ✅ 已完成│ │ ⏭ 跳过  │ │ ❌ 出错  │ │ ↻ 已覆盖 │ │ Σ 全部  │
│ 1,233  │ │   3    │ │   1    │ │   1    │ │ 1,248  │
│ 23.8 MB│ │  20 KB │ │ 1.2 GB │ │ 860 KB │ │41.76 MB│
└────────┘ └────────┘ └────────┘ └────────┘ └────────┘
```

- 「全部」放**最右**：它本质是「取消过滤」，放末尾读起来像「以上这些之和」。
- 点已选中的卡再点一次 → 回到「全部」（与「全部」卡并被两个入口，避免用户找不到出口）。

**数据源盘点（关键结论：只有一处缺口）**：

| 卡 | 计数 | 大小 |
|---|---|---|
| 跳过 | `StatsSkippedCount`（`:851`，已有） | `StatsSkippedSize`（已有） |
| 出错 | `StatsFailedCount`（`:854`，已有） | `StatsFailedSize`（已有） |
| 已覆盖 | `StatsOverwrittenCount`（`:857`，已有） | `StatsOverwrittenSize`（已有） |
| 全部 | `EntryItems.Count`（`:613`） | `StatsTotalSizeValue`（`:1478`，已有） |
| **已完成** | **缺** | **缺** |

补齐方式（照抄 `AccumulateTerminalBytes` 的既有模式）：

1. 新增字段 `private long _statsCompleted;` 与 `private long _statsCompletedBytes;`
2. `AccumulateTerminalBytes(state, size)` 加 `case EntryRowState.Completed:`（`ProgressViewModel.cs:1827`）
3. `UpdateEntryStatus` 的「只计一次」守卫（`:1805`）加 `or EntryRowState.Completed`
4. **`ActivateEntryByName` 里另补一处累加** —— 它在 `:1884` 直接把上一行置 `Completed`，**绕过了 `UpdateEntryStatus`**，漏了会少计最后一行

**HTML 原型**：`docs/prototypes/progress-filter-cards.html`
（顶部四个开关：主题浅/深 × 档位完整/列表 × 对比合并后/合并前 × 总大小卡保留/去掉）。
前两版被取代的原型：`progress-entry-filter.html`（胶囊三种形态对比）、`progress-filter-merge.html`（胶囊 vs 卡行合并）。

## 三、实现清单

| # | 任务 | 落点 | 估时 |
|---|---|---|---|
| T1 | 新建 `Converters/ProgressFillBrushConverter.cs`（`IMultiValueConverter`）：入参 `[PercentComplete, IsIndeterminate, StatusKind]` → 4-stop `LinearGradientBrush`；六态见 §2.2 | 🆕 `src/MantisZip.UI.Avalonia/Converters/` | 1h |
| T2 | `StatsCardsPanel.Background` 改 `MultiBinding`，注册转换器 | `Dialogs/ProgressWindow.axaml:701-705` / `:19-40` | 0.5h |
| T3 | 卡片行改 `WrapPanel` + 间距落 `Style`，回收首尾多余 margin | `Dialogs/ProgressWindow.axaml:711-713` | 1h |
| T4 | 独立总进度条加 `IsVisible` 取反（`BoolInvertConverter`） | `Dialogs/ProgressWindow.axaml:1190-1195` | 0.2h |
| T5 | 卡片压进一行：`MinWidth` 80 → 72（模板内 10 处）+ 四档字号下调（§2.4） | `Dialogs/ProgressWindow.axaml` 卡片模板 | 1h |
| T6 | 视图切换合并成五档：新增 `ProgressDetailLevel` 枚举 + `AppSettings` 字段 + 旧两字段反推 + XAML 换成一组 5 单选 | `Models/AppSettings.cs` / `ProgressDisplayMode.cs` / `ViewModels/ProgressViewModel.cs` / `Dialogs/ProgressWindow.axaml:296-320` + `:645-655` | 2-3h |
| T7 | 底部合并一行：段 2 / 百分比 / 时间三处删掉，改一处 `Grid` 两栏（左元信息 · 右百分比+时间） | `Dialogs/ProgressWindow.axaml:1183-1208` | 1-1.5h |
| T8 | 单测：六态配色 / 0% 与 100% 边界 / `IsIndeterminate` 优先于 `StatusKind` / 五档与两旧字段的映射 | 新增 `ProgressFillBrushConverterTests.cs` + 扩 `ProgressViewModel` 相关测试 | 1-1.5h |
| T9 | 真机六态截图（准备 / 扫描 / 进行中 / 暂停 / 完成 / 失败）+ 9 卡全上报不溢出 + 500/800/1200 三档窗口宽 | 手工 | 1h |

| T10 | 抽卡片 `ControlTheme` / `DataTemplate`，让只读卡与可点卡共用同一套三行度量（`MinWidth=72`），只靠 `IsHitTestVisible` 区分 | `Dialogs/ProgressWindow.axaml` 卡片模板 + 新增 `Resources/` 主题 | 1.5h |
| T11 | 新增 `_statsCompleted` / `_statsCompletedBytes` + `AccumulateTerminalBytes` 加 `case Completed` + `UpdateEntryStatus` 守卫加 `or Completed` + **`ActivateEntryByName` 补一处累加**（§2.9 缺口的四点） | `ViewModels/ProgressViewModel.cs:1805` / `:1827` / `:1884` | 1.5h |
| T12 | 列表档卡行换成五张可点卡（`IsListMode` 分支），含选中态 / hover / 再点回「全部」；`EntryViewItems` 投影集合 + 过滤时机挂 `State` 变化 | `Dialogs/ProgressWindow.axaml` + `ViewModels/ProgressViewModel.cs` + 新增 `EntryFilter` 相关 | 3-4h |
| T13 | 空状态（过滤后 0 条 → 「没有匹配的条目 + 显示全部」） | `Dialogs/ProgressWindow.axaml` 列表面板 | 0.5h |
**合计约 17-20.5h**（含验证）：底纹本体 4-4.5h + 紧凑化 5.5-7h + 列表档可点卡与过滤 6.5-8h（T10-T13）。

> 2026-11-27 追加 §2.9 后，本计划从「只做底纹与紧凑化」扩为「底纹 + 紧凑化 + 列表档过滤」三件事；`docs/PLAN.md` 的工时需同步。
## 四、验收口径

1. 「完整」档不再有独立总进度条，进度表现为统计卡行底纹。
2. 极简 / 标准 / 详细三档仍有一根独立总进度条，且外观与今天一致。
3. 开窗准备期与扫描期，底纹为满宽淡黄；扫描期「扫描」卡的数字在变。
4. 进行中底纹按 `PercentComplete` 增长；暂停时同色降 alpha；完成后转绿、失败转红、取消转灰（均满宽）。
5. 构造一次「9 张卡全部上报」的压缩（自适应 ZIP + 跳过 + 出错 + 已压缩 + 仅存储），卡片行在 800px 宽窗口里**一行排得下不换行**、不出现 `ClipToBounds` 硬切；560px 窄窗才换到第二行。
6. 深浅两套主题下底纹都可读（文字压在 15% 透明底上，对比度不低于现状）。
7. **状态面板总高度从 7 行降到 3 行**：视图切换 1 行 + 卡片 1 行 + 底部合并 1 行，独立总条 0 行（完整档）。
8. **视图切换只有一组 5 档单选**（极简 / 标准 / 详细 / 完整 / 列表），不再有「显示：」与「信息量：」两行；选「极简」时内容确实变少（旧版 `IsMinimalDensity` 无内容绑定的缺陷一并修掉）。
9. 五档与两个旧 `AppSettings` 字段的迁移正确：旧配置文件读进来落在合理档位，写完新配置后再次打开保持档位。
10. `-Task build -Target avalonia -c Release` 0 警告；`ui-tests` 全绿。
11. **列表档的卡行是可点版本**：五张（已完成 / 跳过 / 出错 / 已覆盖 / 全部），尺寸与完整档十张卡完全一致，切档不跳。
12. 点「出错」卡后列表只剩失败条目；再点一次同一张卡回到「全部」（或点末尾「全部」卡）。
13. 过滤后 0 条时显示「没有匹配的条目 + 显示全部」，点按钮回到全部。
14. 「已完成」卡的数字与大小随压缩推进增长；**最后一条**（由 `ActivateEntryByName` 置 Completed 的那条）也被计入 —— 验证方式：单文件压缩完成后，「已完成」卡显示 1 而不是 0。
15. 过滤只认 `State`：进行中的行百分比从 40% 涨到 41% 时列表不重算（不闪）。

## 五、范围外

- **按压缩包状态分段着色**（`progress-bar-segments.md` 的 `SegmentProgressBar`）—— 用户 2026-11-27 决定先不做，**已排到 v0.5.10**（见 `docs/PLAN.md`）。
- **不确定态滚动动画** —— 见 §2.3，需要再加。
- **issue #81 的 A（紧急修复）/ B（信息去重）/ D（#76 三卡）三块** —— 各自单独排期；本计划只做底纹、溢出与视图切换（C）。
- **`progress-bar-segments.md` 里「`DragDropService` 失败路径补状态上报」** —— 与本计划无关，留给该计划。
- **5000 条播种上限放宽**（`MaxSeedEntryCount`）—— 超过 5000 条一条行都不建（`CompressFlow.cs:377` / `ExtractFlow.cs:426`），列表与过滤都无从谈起。是否退化成「只播异常项 + 最后 N 条」需单独决策，不在本计划。
- **文件名搜索框 / 按大小排序 / 多选交叉过滤** —— 原型评估时已排除（见 `progress-entry-filter.html` 侧栏末节）。

## 六、风险

| 风险 | 影响 | 应对 |
|---|---|---|
| `WrapPanel` 换行后卡片行高度变化，`SizeToContent="Height"` 的窗口可能跳动 | 中 | 换行阈值取窗口实际可用宽度；真机在 500 / 800 / 1200 三档宽度各看一次 |
| 淡黄在深色主题下偏暗棕 `#FF5C4A1E`，15% 透明后几乎看不见 | 中 | 深色主题的 warning 底色单独提一档 alpha（如 25%），或换用浅色变体 |
| 底纹铺满整块 `Border`，`Padding` 区域也染色，视觉上像卡片区被整体高亮 | 低 | 内层再包一个 `Border` 专门承载渐变，`Padding` 留在外层 |
| 转换器里读主题色需要 `Application.Current.Resources.TryGetResource`，无主题的测试环境会拿到 null | 中 | 回退到硬编码默认色（参考 `DashBrushConverter` 的两级回退写法） |
| **卡片压到 72px 后三字标签被挤换行** | 中 | 退一步到 76px（9 × 76 + 8 × 8 = 748px，800 窗口仍放得下）；或标签缩成两字 |
| **五档控件在 `MinWidth=500` 下放不下** | 中 | 三档兜底：收紧间距 12 → 8 / 标签缩两字 / 改图标分段控件 |
| **新增第五档「详细」(Detailed+Medium) 与原「完整」的差异不够明显** | 低 | 详细 = 详细列表 + 批列表 8 行、无统计卡；完整 = + 统计卡（底纹）。真机对比若说不清，就把「详细」改为「批列表 11 行但无统计卡」 |
| 两档卡行高度算错 | 🟡 中 | 切档时整行跳一下 | T10 抽同一套 `ControlTheme`；单测断言两态 `DesiredSize.Height` 相等 |
| 「已完成」重复计数 | 🟡 中 | 数字虚高 | 沿用 `row.State != state` 守卫；`ActivateEntryByName` 的补点单独加测试 |
| 过滤造成列表滚动位置错乱 | 🟢 低 | 用户找不到刚看的那行 | 切过滤后滚回顶部（`MaxHeight=150` 只有 6 行） |

## 七、待讨论（用户说「细节再讨论」）

1. **完成 / 失败 / 取消也变色**（§2.2 后三行）要不要一起做？不做可以省 0.5h。
2. **深色主题**的淡黄 alpha 取多少（15% 起步是否够）。
3. `WrapPanel` 间距走 `Window.Styles` 还是逐卡 `Margin`。
4. 底纹是否要延伸到 `Padding` 外（即「卡片区整体高亮」还是「卡片区内嵌一条」）。
5. 暂停态用「同色降 alpha」还是明确换成黄色/橙色（现在是按前者写的）。
6. **扫描态的「已发现 N 个文件」挪到哪**：底部合并行后它原来占的位置没了。候选：①左侧元信息区（`🔍 1284`）②扫描期临时替换右侧百分比位置 ③干脆不显示（「扫描」卡已经在显示计数）。
7. **五档标签的措辞**：极简 / 标准 / 详细 / 完整 / 列表 —— 「详细」与「完整」在中文里也容易混，是否改成「标准 / 详细 / 完整」之外更区分度高的词（如「精简 / 常规 / 详细 / 完整 / 列表」）。
8. **列表档的「已处理 742」去哪** —— 「全部」卡的第三行现在是总大小（41.76 MB）。要不要改成「已处理 742」？两者只能放一个（卡片只有三行）。
9. **「初始状态是否默认选中「全部」** —— 原型里是（`aria-pressed=true`）。若默认不选任何卡，列表显示全部但没有任何卡高亮，可能让人误以为过滤失效。

## 八、决策记录

2026-11-27 与用户逐轮讨论，拍板如下：

- **分段着色（`progress-bar-segments.md`）暂不做**（用户：「分段这个先不做」）；**同日决定往后推一个版本到 v0.5.10**。
- **底纹方案取代独立总进度条**（用户：「"完整"档底下的进度条，我想改成统计卡行的底纹进度条」→ 我评估可行后确认）。
- **不确定态改用颜色**（用户：「底纹不确定态先更换颜色行不行？比如不确定态就是填充黄色，确定态是正常进度」→ 确认可行，替代原本「不确定态回落到独立 `ProgressBar`」的两套 UI 方案）。
- **先写计划，细节再讨论**（用户：「先写计划吧，然后细节再讨论」）。
- **先出 HTML 原型**（用户：「你先出一个html的原型，放在 docs/prototypes/ 里面我看看」→ `docs/prototypes/progress-stats-card-fill.html`）。
- **段 2 + 百分比 + 时间合并成底部一行**（用户：「"并行 4 压缩后 11.76 MB (28%)" 这些应该跟 "31% 已用 00:12" 那些放在一行就行，现在这个三行太浪费空间」）。
- **状态面板压到一行排下**（用户：「挺好，状态面板里面能再紧凑一些吧？我希望一行就能排下」→ §2.7 视图切换合一行 + §2.4 卡片压一行 + §2.8 高度账）。
- **`progress-bar-segments.md` 往后推一个版本**（用户：「把 progress-bar-segments.md 这个计划往后推一个版本」→ 从 v0.5.9 移到 v0.5.10）。
- **列表档要可过滤**（用户：「我希望"列表"模式的列表可以选择过滤，比如切换到只显示"出错"的文件」）。
- **过滤器与统计卡行合并**（用户：「它是不是跟下面的状态面板功能重叠了？能合并一下吗？」→ 确认重叠：跳过/出错/已覆盖三个数字同源）。
- **胶囊改成两行卡片、与完整档同构**（用户：「就是这个胶囊导致信息变少，胶囊能改成两行的按钮吗？跟完整档显示的信息一致？」→ 定为三行同构卡片，尺寸与完整档完全一致）。
- **「全部」卡放最右**（本质是「取消过滤」，放末尾读作「以上这些之和」；MVP 期间保留，避免过滤后没有可见出口）。

---

## 九、实施记录

（开工后回填：改动清单、偏差、真机记录、验证数据）
