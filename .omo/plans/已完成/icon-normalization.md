# 图标归一化（消除「图标太丑」，不做主题系统）

## TL;DR

> **Quick Summary**: 全仓 117 个图标混了**三套坐标系**——68 个是 24 网格 Fluent 向量，38 个是从图标字体里抠出来的字形（笔画明显更粗），11 个是手绘简化。三族混排在工具栏/菜单里就是用户说的「图标太丑」。本计划把 49 个异类替换成同族 24 网格路径，并收敛显示尺寸与前景色，**只改 `AppIcons.axaml` 一个文件**，不引入主题系统。
>
> **Deliverables**:
> - `src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml` — 49 条 `Geometry` 替换为 24 网格同族路径（键名全部不变）
> - `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` / `AboutWindow.axaml` / `ProgressWindow.axaml` 等 — 14px 显示位统一到 16px
> - `src/MantisZip.UI.Avalonia/App.axaml` — 补齐 PathIcon 的 Foreground 覆盖（含修掉 `ToolbarButtonIcon` 类名指向 TextBlock 的空挂问题）
> - `scripts/render-icon-montage.ps1` — ✅ 已就位，评审/自检工具
> - `src/MantisZip.UI.Avalonia/ViewModels/IconTestViewModel.cs` + `Views/IconTestWindow.axaml` — 新增「三档尺寸 × 双主题」矩阵页
>
> **Estimated Effort**: 1.5–2 天（10–14h）
> **Parallel Execution**: NO（单文件串行修改，避免冲突）
> **Critical Path**: T0 图标源 → T1 映射表 → T2 替换 → T3 尺寸/语义 → T4 前景色 → T5 矩阵页 → T6 验收

---

## Context

### Original Request

> 用户反馈汇总（2026-10-24）：
> - 「不是你真正想做的卖点，但是很多用户反馈现在的图标太丑，所以是不是有什么折中的方案。」
> - 决策：「图标折中方案做成计划文档。」

背景约束（同一次讨论确定）：
- **目标是公开用户**（不是自用工具）→ 首屏观感直接影响下载转化。
- **总体策略是「少而稳」** → 不投入 72–96h 的图标主题系统（`icon-theme-infra/packs/sets` 三份计划继续冻结），只做能直接消掉「丑」的最小动作。

### 审计证据（2026-10-24，`scripts/render-icon-montage.ps1 -Check` 可复现）

117 个图标键按**坐标网格**分族：

| 族 | 数量 | 判据 | 典型示例 |
|---|:--:|---|---|
| **A 24 网格（Fluent 系）** | 68 | 最大坐标 ≤26 且带大量高精度小数 | `IconFolder` `IconSearch` `IconAdd` `IconArchive` `IconSettings` |
| **B 字体字形（1000/2048 em）** | 38 | 最大坐标 >26，典型值 755–976 | `IconBookmark`(876) `IconFontIncrease`(976) `IconEye`(75.8) `IconSubtract`(75.8) |
| **C 手绘/低精度** | 11 | 最大坐标 ≤26 但几乎无小数位 | `IconDrive` `IconMagnet` `IconHtml` `IconChevronDownUp` |

**肉眼结论（有拼版截图为证）**：B 组和 C 组的笔画明显比 A 组粗、视觉重量更大。同一行里并排放着「细线 Fluent 图标」和「粗体字形图标」（例如 `ArrowUp` 与 `Eye`、`Add` 与 `Subtract`、`Star` 与 `Magnet`），这就是「不齐、丑」的根因——**不是风格不够个性化**。

### Key Decisions

1. **不做主题系统**。`icon-theme-infra`（DrawingImage 迁移 + IconView）本身「A 套零视觉差异」，22–28h 换不来任何观感改善；②③ 再各 26–34h / 24–34h。用户明确表示不是卖点。**三份计划继续冻结**，本计划不动它们的任何前置设计（不迁移 Geometry 契约）。
2. **只替换异类，不整体换库**。A 组 68 个已经是干净的 24 网格，全部替换等于白做 68 次改动 + 68 次回归风险。只动 49 个。
3. **键名全部不变**。`IconTestViewModel.LoadAllIcons()`（规则 8 守卫）与 224 处 `PathIcon` 引用因此**零改动**。
4. **只改一个文件**。226/227 处 `PathIcon` 全部通过 `{StaticResource IconXxx}` 引用 `AppIcons.axaml`（实测仅 `ResultTreeView.axaml:201` 等个别站点走绑定），因此替换是纯数据操作。
5. **选 Fluent 24 Regular 作为唯一目标族**。A 组 68 个即 Fluent 系（`IconAdd` = `M12 3.25C12.4142...` 与 fluentui-system-icons 的 Add24Regular 一致），继续用同族才能视觉一致；不换 Lucide/Phosphor（那是主题系统的选项）。

### Research Findings（实测，2026-10-24）

- `AppIcons.axaml`：117 个 `x:Key="Icon*"`，均为 `<Geometry x:Key="...">路径数据</Geometry>` 单行格式。
- `<PathIcon` 出现 **227** 次（`App.axaml` 之外 25 个 axaml + C# 动态站点）；其中 211 处 `Data="{StaticResource ...}"` 内联、16 处换行书写，**没有一处写死内联路径**。
- **32 处 `PathIcon` 没有显式 `Foreground`**（`MainWindow.axaml` 10 / `UiTestWindow.axaml` 9 / `ProgressWindow.axaml` 5 / `CustomFilePickerDialog.axaml` 3 / `ResultTreeView.axaml` 2 / `SettingsWindow.axaml` 2 / `ArchiveContentView.axaml` 1）。与 AGENTS 规则 4「PathIcon 不继承父控件 Foreground，必须显式设置」冲突，且 `guards` 不校验这一项。
- `App.axaml:140` 是全局唯一的 PathIcon 选择器（`ToggleButton:checked PathIcon`）。
- `App.axaml:173` 的 `<Style Selector="TextBlock.ToolbarButtonIcon">` 设了 Foreground，但 `MainWindow.axaml:91` 等处写的是 `<PathIcon Classes="ToolbarButtonIcon" />` —— **类名挂在 PathIcon 上，样式却只匹配 TextBlock**，该 class 对图标完全空挂。
- 显示尺寸现状：`ProgressWindow.axaml:231` 用 14px，`MatchedPasswordDialog.axaml:32` 用 16px，工具栏用 22px。Fluent 24 网格图标在 14px 下发虚（1.5px 线宽缩到 ~0.9px）。
- 语义问题：`IconOrientation` 是「矩形 + 斜线」，读起来像「图裂/禁用」；`IconGrid` 与 `IconApps` 视觉几乎重复；`IconChevronUpDown`（B 组）与 `IconChevronDownUp`（C 组）是同一含义的两个键。

---

## Work Objectives

### Core Objective

让 117 个图标在视觉上属于同一族（同网格、同笔画权重、同光学尺寸），并消除 14px 发虚与前景色空挂，为公开用户的第一印象服务。**不引入任何主题/换肤能力。**

### Concrete Deliverables

- `src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml` — 49 条 Geometry 替换为 24 Regular 路径
- `src/MantisZip.UI.Avalonia/App.axaml` — 新增 `PathIcon.ToolbarButtonIcon`（或等价）前景色覆盖
- 显示尺寸调整：`ProgressWindow.axaml` 等 14px 站点 → 16px
- `src/MantisZip.UI.Avalonia/ViewModels/IconTestViewModel.cs` + `Views/IconTestWindow.axaml` — 尺寸矩阵评审页
- 保留 `scripts/render-icon-montage.ps1` 作为常驻工具（已提交）

### Definition of Done

- [ ] `pwsh -File scripts\render-icon-montage.ps1 -Check` 退出码 **0**（B/C 组清零）
- [ ] `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia` 通过
- [ ] `pwsh -File scripts\verify-repo.ps1 -Task guards` 通过（规则 8 图标登记未破坏）
- [ ] 浅色 + 深色主题各截一张主窗口工具栏图，肉眼确认无发虚、无低对比、无尺寸跳变
- [ ] `git grep -c "StreamGeometry\|F0 M"` 无新增异常路径数据（解析错误为 0）

### Must Have

- 49 个 B/C 组图标全部替换为 24 网格路径，**键名不变**
- 替换后每个图标在 16px 与 22px 两档都清晰可辨（不放大的情况下）
- 32 处缺失 Foreground 的站点要么补齐、要么有明确继承兜底并写注释
- `IconOrientation` 换成语义正确的图标（不再像「损坏/禁用」）
- `IconGrid` 与 `IconApps` 至少一个换成可区分的造型

### Must NOT Have (Guardrails)

- **不迁移** `Geometry` → `DrawingImage` 契约（那是 `icon-theme-infra`，已冻结）
- **不新增**图标主题/换肤/导入包能力（`icon-theme-packs` / `icon-theme-sets` 已冻结）
- **不更换**图标库到 Lucide/Phosphor/Tabler（换族会让 68 个 A 组图标一起返工）
- **不修改**任何图标键名（改键 = 改 224 处引用 + 规则 8 守卫全红）
- **不引入**新 NuGet 依赖、不 vendor 整个图标仓库（只需 49 条 path 字符串）
- **不动**尺寸体系资源键（`ControlHeightSm` 等），只改图标的显式 Width/Height

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** — 除最后一次「有没有变好看」的主观确认外，其余验证全部由 agent 执行。

### Test Decision

- **Infrastructure exists**: YES（`tests/MantisZip.UI.Avalonia.Tests`）
- **Automated tests**: 无法对「视觉一致性」写断言。改为**可执行的可视化证据**：
  1. `scripts/render-icon-montage.ps1` 的 `-Check` 退出码（族纯净度的机器判据）
  2. 拼版 PNG 人工过目（脚本现场生成，附在评审记录里）
  3. 依赖已有测试保证「改数据不破引用」：`AboutWindowTests`（三语）、`MainWindowKeyBindingsTests`、`HelpDialogRenderTests` 不受影响即说明没碰到结构性契约
- **Framework**: xUnit（仅跑既有用例做回归）

### QA Policy

**不允许**用「我觉得现在好看了」当作验收。证据只有两种形式：

- **机器判据**：`-Check` 退出码 + 每次替换后的族计数（A/B/C 数量）
- **目视证据**：拼版 PNG 由脚本现场生成后直接看过，**不落库存二进制**

> 注：shell 沙箱对工作区子目录的写入受限（实测 `docs/`、`scripts/`、`.omo/` 下的 pwsh 写入被拒，仅仓库根目录可写；文件读写工具不受限）。因此不要把「导出证据文件到某目录」写成验收项——一律现生成现看。

---

## Execution Strategy

### Waves

```
Wave 1（准备，串行）:
└── T0 取得 Fluent 24 Regular 图标源（离线，只提取用到的 49 个）

Wave 2（映射，串行）:
└── T1 生成 49 条「旧键 → Fluent 图标名 → 24 网格路径」映射表并逐条确认语义

Wave 3（改数据，串行、单文件）:
├── T2 替换 AppIcons.axaml 中的 49 条 Geometry
└── T3 尺寸档位收敛 + 3 个语义错图标修正

Wave 4（改样式，可与 Wave 3 并行）:
└── T4 PathIcon 前景色收敛（32 处 + class 空挂）

Wave 5（固化工具）:
└── T5 IconTestWindow 增加「三档尺寸 × 双主题」矩阵页

Wave 6（验收）:
└── T6 全量验证 + 截图留证
```

### Dependency Matrix

- **T0**: 无前置 → 阻塞 T1
- **T1**: 依赖 T0 → 阻塞 T2
- **T2**: 依赖 T1 → 阻塞 T3、T6
- **T3**: 依赖 T2 → 阻塞 T6
- **T4**: 依赖 T0（无技术依赖，仅排期） → 阻塞 T6
- **T5**: 依赖 T2 → 阻塞 T6
- **T6**: 依赖 T2/T3/T4/T5

---

## TODOs

- [ ] **T0. 取得 Fluent 24 Regular 图标源（离线）**

  **What to do**:
  - 下载 `microsoft/fluentui-system-icons`（MIT），只取 24px / Regular / Products 或通用分类下的 SVG
  - 放临时目录，不入仓库、不加依赖
  - 写一次性提取脚本（不必入库）：从 SVG 的 `<path d="...">` 提取 path 数据，转 Avalonia `StreamGeometry` 可解析的写法（保持 `M/L/C/A/Z` 指令，`fill-rule="evenodd"` → 前缀 `F0`）

  **Must NOT do**:
  - 不 vendor 整个图标仓库进 git（会增加 20MB+ 且和「少而稳」冲突）
  - 不引入 SVG 解析库到产品代码

  **Acceptance Criteria**:
  - 49 个目标图标名在本地都能找到对应 SVG，缺失的记下来（缺失项在 T1 换近义名）

- [ ] **T1. 生成 49 条映射表并逐条确认**

  **What to do**:
  - 按下表逐条确认「旧键 → Fluent 图标名」。**下面这张表是初稿**，遇到语义不合的以「这个键在 UI 里到底表示什么」为准（去 `grep` 调用点文案）
  - 映射表最终并入本计划文档末尾的「附录 A：49 条映射表」，含每条的 path 数据来源

  | 旧键 | 建议 Fluent 24 Regular | 备注 |
  |---|---|---|
  | `IconBookmark` / `Add` / `Multiple` / `Off` | `Bookmark` / `BookmarkAdd` / `BookmarkMultiple` / `BookmarkOff` | B 组 |
  | `IconEye` / `IconEyeOff` | `Eye` / `EyeOff` | 密码框显隐，**必须两态等重** |
  | `IconSubtract` | `Subtract` | 原为一条 75 单位的粗横线 |
  | `IconImport` / `IconArrowRight` | `ArrowImport` / `ArrowRight` | |
  | `IconArrowFitIn` / `IconArrowMoveInward` | `ArrowFitIn` / `ArrowMinimize` | C 组那条尤其粗 |
  | `IconFontIncrease` / `IconFontDecrease` | `FontIncrease` / `FontDecrease` | |
  | `IconFilter` | `Filter` | 原为实心漏斗，24 Regular 是描边 |
  | `IconAppsSettings` | `AppsList` 或 `SettingsCogMultiple` | 需看调用点语义 |
  | `IconArchiveArrowBack` / `Multiple` / `Settings` | 同名 Fluent 图标 | |
  | `IconArrowAutofitContent` / `Width` / `WidthDotted` / `Height` | 同名 Fluent 图标 | |
  | `IconArrowCircleDownUp` | `ArrowSwap` | |
  | `IconArrowCollapseAll` / `ExpandAll` | 同名 Fluent 图标 | 目前 max=28.2，仅超阈值，属误判但一并规范 |
  | `IconArrowMove` | `ArrowMove` | |
  | `IconCalendarCheckmark` | `CalendarCheckmark` | |
  | `IconChevronUpDown` / `IconChevronDownUp` | 统一为 `ChevronUpDown` | **两个键同义，合并成一个键（需改调用点）或保留双键同路径** |
  | `IconFolderAdd` / `ArrowLeft` / `ArrowRight` / `Hint` / `Multiple` / `Open` | 同名 Fluent 图标；`Hint` → `FolderSearch` | |
  | `IconKeyMultiple` / `IconLockMultiple` / `IconLockClosedKey` | 同名 Fluent 图标 | |
  | `IconLocationCheckmark` / `Settings` | 同名 Fluent 图标 | |
  | `IconDrive` | `HardDrive` | C 组手绘 |
  | `IconArrowCircleDown` / `Up` / `Left` / `Right` | 同名 Fluent 图标 | C 组手绘 |
  | `IconArrowEnter` / `ArrowExit` | 同名 Fluent 图标 | |
  | `IconMagnet` | `Magnet` | C 组，原为粗描边 |
  | `IconHtml` | `Code` 或 `DocumentText` | C 组，原为实心 M 字造型 |
  | `IconOrientation` | `ArrowRotateClockwise` 或 `Table` | **语义修正**：现在读起来像「图裂/禁用」 |

  **Acceptance Criteria**:
  - 49 条全部有对应 Fluent 名，无「找不到就自己画」
  - 映射表里每条都标注它服务的 UI 位置（工具栏/菜单/进度窗/图标测试窗）

- [ ] **T2. 替换 AppIcons.axaml 中的 49 条 Geometry**

  **What to do**:
  - 逐条替换 `<Geometry x:Key="IconXxx">…</Geometry>` 的路径数据
  - 保持文件既有格式：单行、缩进、`<!-- -->` 分区注释不动
  - 每替换 10 条跑一次 `pwsh -File scripts\render-icon-montage.ps1` 看图，避免一次性换完看不出问题

  **Must NOT do**:
  - 不改键名、不删键、不重排既有条目
  - 不顺手改 A 组的 68 条（除非发现解析错误）

  **Acceptance Criteria**:
  - `-Check` 报告的 B/C 组数量从 49 降到 0（或仅剩已确认的误判项）
  - 拼版 PNG 中不再出现「明显更粗更黑」的图标

- [ ] **T3. 尺寸档位收敛 + 语义修正**

  **What to do**:
  - 把 14px 的图标显示位统一到 **16px**（`ProgressWindow.axaml:231` 等）；16px 以下的站点全部排查一遍
  - `IconGrid` 与 `IconApps` 改其一，使两者可区分
  - 修 `IconOrientation`（见 T1 表的语义修正）
  - 若 `IconChevronUpDown` / `IconChevronDownUp` 合并，同步改调用点（约 2–3 处，`grep IconChevron` 确认）

  **Must NOT do**:
  - 不改紧凑度资源键的数值（那是 `App.axaml.cs` `ApplyCompactness()` 的职责）
  - 不用固定 Height 强行对齐（规则 4 已禁止无理由固定高度）

- [ ] **T4. PathIcon 前景色收敛**

  **What to do**:
  - 处置实测的 **32 处**无 `Foreground` 的 `PathIcon`：
    - 工具栏内（`MainWindow.axaml` 10 处）：补 `App.axaml` 的 `PathIcon.ToolbarButtonIcon` 样式（把现有 `TextBlock.ToolbarButtonIcon` 的 Foreground 规则扩展到 PathIcon）
    - 其余按上下文显式设置 `ThemeTextPrimaryBrush` / `ThemeTextSecondaryBrush`
  - 深色主题下逐屏确认图标对比度（尤其进度窗状态图标、文件选择器）

  **Must NOT do**:
  - 不给全局 `TextBlock` 设 Foreground（规则 4 明令，会压掉 emoji 颜色）
  - 不做「按状态变色」的高级绑定（不在本计划范围内）

  **Acceptance Criteria**:
  - `git grep -c 'PathIcon'` 统计后，未设 Foreground 的站点数为 0，或有注释说明继承来源

- [ ] **T5. IconTestWindow 增加「三档尺寸 × 双主题」矩阵页**

  **What to do**:
  - 在现有 `IconTestWindow` 增加一个视图：每个图标按 16 / 22 / 28px 三档 × 浅色/深色两种前景各渲染一次
  - 目的：以后新增图标先在这一页过一遍，一眼看出「粗了/虚了/大小不一」
  - 新增用户可见文案走三语（规则 13），key 插入三份 JSON 头部

  **Acceptance Criteria**:
  - 打开图标测试窗口能看到矩阵页，117 个图标齐全（`IconTestViewModel.LoadAllIcons()` 新增项必须同步，规则 8）

- [ ] **T6. 全量验证 + 截图留证**

  **What to do**:
  - 跑：`verify-repo.ps1 -Task check` / `-Task build -Target avalonia` / `-Task guards` / `-Task i18n`
  - 跑：`render-icon-montage.ps1 -Check`（期望退出码 0）
  - 截：主窗口工具栏（浅色 + 深色）、进度窗、设置窗各一张，附在评审记录里（现场生成，不落库）
  - 与 T1 之前的拼版做前后对比图

  **Acceptance Criteria**:
  - 全部门禁通过
  - 前后对比图能明显看出「不再是粗细混排」

---

## Rollback

改动集中在 `AppIcons.axaml` + 少量 axaml 样式/尺寸，全部可用 `git checkout` 单文件回滚。**无数据结构/契约变更**，回滚零风险。

---

## 与已冻结计划的关系

- `icon-theme-infra`（①）：若将来解冻，本计划产出的「117 键全在 24 网格」正是它的前置条件，**不会返工**（它迁移的是容器契约，不是路径数据）。
- `icon-theme-packs`（②）`icon-theme-sets`（③）：依赖 ①，与本计划无交集。
- 解冻条件见 `docs/PLAN.md` 的「已冻结」表。
