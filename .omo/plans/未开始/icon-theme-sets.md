# 图标主题系统 — 计划 3：IconGen 多套 + C/D/E/F/G 内容生产

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **优先级**: P3 | **预估工时**: 22-32h（含人工校对与补齐；2026-10-21 复核后由 20-30h 上调：E 套决策门、双许可声明、D 套暗色可见性）
> **来源**: [docs/superpowers/specs/2026-10-15-icon-theme-system-design.md](../../docs/superpowers/specs/2026-10-15-icon-theme-system-design.md)（已批准，§5）
> **前置**: **计划 1**（`icon-theme-infra.md`）必须已完成（IconGen A 套管线、`IconView`、`GetIconThemeProvider`/`ResolveIconThemeUri` 骨架）。
> **与计划 2 关系**: **无硬代码依赖**，可并行；本文只产出「内置套内容与生成管线」，不碰导入/覆盖/设置 UI。两处可选复用（不满足也可独立完成）：① 归一化算法若被计划 2 下沉到 `MantisZip.Core` 则共用，否则 IconGen 自持最小实现；② `IconCatalog` 若已落地则复用，否则直接用 `IconTestViewModel.Icons`。**注意**：归一化逻辑**不得**引用计划 2 的 `Services/SvgIconParser.cs`（UI 项目、依赖 Avalonia；`tools/` 现有工程仅引用 `MantisZip.Core`，见 §4.3）。

## 1. 目标与交付标准

把内置风格从「A 现状」扩到 **6 套（A/C/D/E/F/G）**，并让 IconGen 从单套（A）升级为多套产出：

1. 扩展 `tools/IconGen` 读「源库 SVG + 语义映射表」，确定性产出 `Icons.C/D/E/F/G.axaml`。
2. 116 键 × 5 新套的几何/画刷生产，缺口手绘补齐；**覆盖率契约**对照注册表（**`Distinct()` 后** 116 键；注册表含 19 条重复）测试锁定。
3. `ResolveIconThemeUri` 支持 `"C"…"G"`；开源图标库署名**三处同步**（AboutWindow「致谢」Tab / `THIRD-PARTY-NOTICES.md` / README）。
4. 6 套 × Light/Dark × 选中态手动截图走查。

**DoD**：6 字典 × 116 键全存在；`SelectedIconTheme` 可切 C-G 且即时生效；构建 0 错误；新增测试全绿。

## 2. 现状核实

| 项 | 事实 | 位置 |
|----|------|------|
| A 套管线 | 计划 1 交付：`IconGen` 读 `AppIcons.axaml`（**116** 个 `<Geometry>`，`:4-119`）→ `Icons.A.axaml`（单部件 `GeometryDrawing` 无 Brush） | `tools/IconGen/`（现不存在，计划 1 新增） |
| 键集契约 | 116 键 `IconPascalCase`；`IconTestViewModel` 注册表 = 覆盖率契约。**注意注册表有 136 条 `Add()`（19 条重复 + 1 条 null）→ 任何比对必须 `Distinct()`** | `ViewModels/IconTestViewModel.cs:14/383` |
| 主题装配 | 计划 1 的 `GetIconThemeProvider(string id)` + `ResolveIconThemeUri(string? id)`（当前仅映射 A）；本文只扩 `ResolveIconThemeUri` 的 `"C"…"G"` 分支 | `App.axaml.cs`（`icon-theme-infra.md:139/147`） |
| 画刷语义 | `IconView`：部件 `Brush==null` → `Foreground`；非空 → baked。`ThemeAccentBrush` 为强调色资源键 | 计划 1 `IconView.cs` |
| tools 现状 | `tools/` 仅 `CorruptProbe`（`net10.0` Exe，**只引用 `MantisZip.Core`**）+ `dsh-plugin-mantiszip` + `measure-startup.ps1`；**无任何工程引用 UI 项目** | `tools/CorruptProbe/CorruptProbe.csproj:21` |
| 署名载体 | 三处都要改：`THIRD-PARTY-NOTICES.md`（现仅 7z.dll `:7`、Fira Code `:17`，末尾「最后更新」`:27`）、`README.md`「字体与图标资源」表、`Dialogs/AboutWindow.axaml`「致谢」Tab（`:351-430`）。**AboutWindow 在 `Dialogs/` 不在 `Views/`** | 见左 |
| 第三方协议 | Phosphor **MIT**（2023，Duotone 同许可）/ Lucide **ISC**（2026）**+ 约 120 个 Feather 衍生图标 MIT**（Cole Bemis）/ Tabler **MIT**（2020-2026） | 三库 LICENSE 实读（2026-10-21） |
| 渐变先例 | 全仓 `.axaml` **零** `LinearGradientBrush`/`RadialGradientBrush` → F 套是首例，无既有写法可抄，须 headless 验证 | `src/**/*.axaml` grep |

## 3. 六套美术方向（对齐 §5 表）

| 套 | 源 | 部件与画刷 | 上下文着色 | 选中态 |
|----|----|-----------|-----------|--------|
| A 现状 | `AppIcons.axaml`（已有） | 单部件 / null | ✅ 全保留 | 转白 |
| C 粗圆角 | Phosphor **Bold** 按语义映射 | 单部件 / null | ✅ 全保留 | 转白 |
| D 双色 | Phosphor **Duotone** 分层（base + accent 双 path） | 底块 baked **中性半透明灰**（`#40808080`，Light/Dark 都可见；**不可用黑 @20%**）+ 线框 null | ✅ 线框保留 | 线框转白，底块保持 |
| E 彩色 | 彩色源经 **§4.5 决策门**；无合适源 → 「C 几何 + 双色程序化着色」兜底 | 全 baked 原色 | ⚠️ 不跟随 Foreground | 保持彩色（已批准接受项） |
| F 渐变 | 复用 C 的几何，只换画刷（蓝→紫 `LinearGradientBrush`） | 单部件 / baked 渐变 | ⚠️ 不跟随 Foreground | 保持渐变 |
| G 强调色 | 同 D 分层，底块画刷 = 主题强调色 | 底块引强调色 + 线框 null | ✅ 线框保留 | 保持底块，线框转白 |

## 4. IconGen 多套管线

### 4.1 目录布局

```
tools/IconGen/
├── IconGen.csproj
├── Program.cs
├── mapping.json                 # IconPascalCase -> 各源库图标名 / 内联 path
└── sources/
    ├── LICENSE-phosphor.txt      # 随源提交的许可原文（MIT）
    ├── LICENSE-lucide.txt        # ISC + Feather MIT（若用到 Lucide）
    ├── LICENSE-tabler.txt        # MIT（若用到 Tabler）
    ├── phosphor-bold/*.svg       # C
    ├── phosphor-duotone/*.svg    # D（base+accent 双层）
    ├── color/*.svg               # E
    └── manual/*.svg              # 缺口手绘（全部套通用兜底）
```

- **`sources/` 必须随仓库提交（vendored）**：`IconGen` 是构建期工具，离线/无网必须可重跑并产出 git 无 diff 的结果；不允许「运行时去 GitHub 拉」。`Program.cs` 不得联网。

### 4.2 `mapping.json`（示例节选，实际 116 键）

```json
{
  "_comment": "JSON 不允许注释，此字段仅作说明；实际文件为 116 个 IconPascalCase 键",
  "IconFolder": { "c": "phosphor-bold/folder.svg", "d": "phosphor-duotone/folder.svg", "e": "color/folder.svg", "manual": null },
  "IconSearch": { "c": "phosphor-bold/magnifying-glass.svg", "d": "phosphor-duotone/magnifying-glass.svg", "e": "color/magnifying-glass.svg", "manual": null }
}
```

- 未命中映射键 → 回退 `manual/IconXxx.svg`；仍缺 → `IconGen` 报错并列出缺键（构建期硬失败，防漏）。
- F 无条件复用 C 的几何（仅换画刷），无需单独映射。

### 4.3 产出规则

| 套 | 输出 |
|----|------|
| C | 每键 `<DrawingImage><GeometryDrawing Geometry=… /><…>`（无 Brush） |
| D | 每键 `<GeometryDrawing Geometry=base Fill=半透明色 /><GeometryDrawing Geometry=accent />`（accent 线框无 Brush → Foreground） |
| E | 每键 2+ `<GeometryDrawing>` 全带 baked `SolidColorBrush` |
| F | C 几何 + 单部件 `GeometryDrawing Brush=LinearGradientBrush(蓝→紫)` |
| G | D 几何，base 部件画刷 = 强调色（见 4.4），accent 部件无 Brush |

- **确定性**：键顺序 = `AppIcons.axaml` 顺序；无时间戳/随机；重复运行 git 无 diff。
- **归一化归属**：所有源 SVG 归一化到 24×24。优先由**计划 2 把归一化算法下沉 `MantisZip.Core`**（纯几何、无 Avalonia 依赖），IconGen 经 `ProjectReference` 复用；若计划 2 未下沉 → IconGen **自持最小实现**（`viewBox` 解析 + 缩放/平移 → path 变换）。**禁止引用 `MantisZip.UI.Avalonia`**（会拖入 Avalonia；`tools/` 现有工程只引用 Core，无 tools→UI 先例）。两条路径只允许存在一份最终算法；T1 用同一 `viewBox="0 0 48 48"` 样本断言两边产出一致（若共享）。
- **F 渐变**：全仓 `.axaml` 当前**零** `LinearGradientBrush` 先例。用显式 `StartPoint`/`EndPoint`（`RelativePoint` 百分比）定义，避免依赖默认值；T2 headless 断言渐变两端色值正确且渲染非空。

### 4.4 G 套强调色画刷（**决策门**，需实测）

规格 §5 要求 G 底块画刷为 `DynamicResource`（随主题换色）。但 `GeometryDrawing` 位于 `ResourceDictionary` 中、非 `IResourceHost`，`{DynamicResource ThemeAccentBrush}` 能否解析**不确定**。

- **首选**：`Icons.G.axaml` 中 `<GeometryDrawing Geometry="…" Brush="{DynamicResource ThemeAccentBrush}" />`；headless 测试断言渲染像素随 Light/Dark 强调色变化。
- **回退（若首选不可解析）**：定义哨兵画刷 `IconView.AccentMarkerBrush`（静态 `IBrush` 实例）；G 的 base 部件 `Brush` 设为该哨兵；`IconView.Render` 遇哨兵替换为内部订阅的 `ThemeAccentBrush`。语义等价，无 DynamicResource 依赖。
- 采用哪种以 T3 的实测测试结果为准，并在计划文件「决策记录」追加一行。

### 4.5 E 套彩色源（**决策门**，需先调研）

DoD 要求 E 套 116 键齐全，但「彩色图标库」尚无确定来源，且**手绘 116 个图标**成本不可接受。T4 前必须先走决策门：

1. **候选调研**（限 MIT / ISC / Apache-2.0 / CC0 等宽松许可，且允许修改与再分发）：逐库核对许可 + 是否覆盖本项目 116 个语义键。
2. **判定**：
   - 有合适源（覆盖率 ≥ 90%，缺口 ≤ 12 键可手绘）→ 采用，缺口进 `sources/manual/`；
   - 无合适源 → **降级方案**：复用 C 套几何，按「主色 + 辅色」两部件程序化着色（主色取图标语义分组固定色板，辅色半透明），产出 `Icons.E.axaml`（全 baked）。视觉上仍是「彩色」，与 D 的差别是 D 用主题无关的双层灰度、E 用固定色板。
3. **不允许**：为了凑 116 键而引入许可不明/禁止修改的图标集（如 CC-BY-NC、品牌素材库）。
4. 结论写入「决策记录」，并同步 `THIRD-PARTY-NOTICES.md` 实际用到的库（未用到的不要列）。

## 5. 任务分解（TDD；每任务一 commit）

- [ ] **T0 — 116 键映射盘点**
  - 导出 `AppIcons.axaml`（`:4-119`）116 键清单 → 生成 `mapping.json` 骨架（键全列出、源留空）。一次性脚本或 `XDocument` 读 `<Geometry x:Key>` 即可，**不要**为此给 IconGen 加常驻 dump 模式。
  - **产出**：`mapping.json` + 盘点表（键 / 语义 / 拟用源 / 是否需手绘）。
  - 人工填 Phosphor Bold/Duotone 映射；未命中键标记手绘。commit。

- [ ] **T1 — IconGen 多套化（结构）**
  - `Program.cs` 支持 `--set A|C|D|E|F|G`；读 `mapping.json` + `sources/`；按 4.3 产出。**全程离线**（`sources/` 已 vendored，不联网）。
  - **测试**：给定最小 `mapping.json` + 1 个 mock SVG → 产出 C/D/E/F/G 各 1 键；缺映射 → 报错含缺键；`viewBox="0 0 48 48"` 归一化结果与运行时算法一致（若已下沉 Core）。commit。

- [ ] **T2 — C/F 套生成**
  - 把 Phosphor Bold（MIT）**vendored 到 `sources/phosphor-bold/` 并提交**（附 `sources/LICENSE-phosphor.txt`）；生成 `Icons.C.axaml`；F = C 几何 + 渐变。
  - **测试**：C 键集 == 116 且全无 Brush；F 键集 == 116 且全含 `LinearGradientBrush`（显式 `StartPoint`/`EndPoint`，headless 断言两端色值）；重复运行 `git diff` 为空。commit。

- [ ] **T3 — D/G 套生成 + G 强调色决策门**
  - 把 Phosphor Duotone **vendored 到 `sources/phosphor-duotone/` 并提交**；生成 `Icons.D.axaml`（base `#40808080` 中性半透明 + accent null）；G 同几何 + 强调色 base。
  - **实测**：headless 断言 G base 像素随 Light/Dark 变化（首选 DynamicResource）；若失败 → 切 4.4 回退方案并复测。
  - **测试**：D/G 键集 == 116；D 每个图标 ≥ 2 部件且恰好 1 个 null 线框；**D base 在 Light/Dark 两种主题下都渲染出可见非背景像素**（防黑 @20% 在暗色消失）；G 语义同上。commit。

- [ ] **T4 — E 套生成（先过 §4.5 决策门）**
  - 先按 §4.5 调研并记录结论；采用外部彩色源 → vendored 到 `sources/color/` + 许可原文；降级 → 用 C 几何 + 语义分组固定色板程序化着色。
  - 生成 `Icons.E.axaml`（全 baked）。
  - **测试**：E 键集 == 116 且全部件有 Brush（无 null）。commit。

- [ ] **T5 — 覆盖率契约测试（6 × 116）**
  - 测试：`Icons.{A,C,D,E,F,G}.axaml` 键集全部 == `IconTestViewModel.Icons` 的 `ResourceKey` **`.Distinct()`** = 116（若计划 2 的 `IconCatalog` 已落地则复用，属可选复用，不构成依赖）；缺键/多键失败并打印差异。
  - **注意**：注册表有 136 条 `Add()`（19 条重复 + 1 条 null），不去重必然假失败。
  - 该测试即「缺键测试期报警」实现（规格 §5）。commit。

- [ ] **T6 — 接线 `ResolveIconThemeUri` + 署名三处**
  - `App.axaml.cs`：`ResolveIconThemeUri` 的 `"C"…"G"` → `Icons.{X}.axaml`（avares URI）；未知 → A。**只改 URI 映射，不动 `GetIconThemeProvider` 的双来源结构**（导入包分支由计划 2 加）。
  - 署名三处同步：① `Dialogs/AboutWindow.axaml`「致谢」Tab（`:351-430`，建议插在 `:365` AI 致谢之后）；② `THIRD-PARTY-NOTICES.md` 新增图标库章节（现仅 7z.dll `:7`、Fira Code `:17`，更新「最后更新」`:27`）；③ `README.md`「字体与图标资源」表加行。
  - 许可文本必须准确：Phosphor MIT / Tabler MIT / **Lucide ISC + Feather 衍生 MIT（两份声明）**；库名为专有名词走规则 13 豁免（在代码/文档注释注明理由）。
  - **测试（headless）**：`SelectedIconTheme="D"` → `ApplyTheme()` 后 `IconFolder` 解析为 D 版本（与 A 不同）。commit。

- [ ] **T7 — 文档与人工 QA**
  - `docs/progress-avalonia-detail.md` 追加（规则 3）；`docs/PLAN.md` 状态更新。
  - 手动：6 套 × Light/Dark × 选中态截图走查（规格 §9.8）；确认 D/G 线框在选中态转白、E/F 保持（已批准差异 §10）；**D 底块在暗色下可见**。commit。

## 6. 涉及文件

**新增**
- `tools/IconGen/mapping.json`、`tools/IconGen/sources/**`（含各库 `LICENSE-*.txt`，全部提交）
- `src/MantisZip.UI.Avalonia/Resources/Icons/Icons.{C,D,E,F,G}.axaml`
- 测试：IconGen 多套产出 / 覆盖率契约 / 6 套装配 / D 套双主题可见性

**修改**
- `tools/IconGen/Program.cs`（多套；离线）
- `src/MantisZip.UI.Avalonia/App.axaml.cs`（`ResolveIconThemeUri` 扩 C-G；G 回退时给 `IconView` 加哨兵逻辑）
- `src/MantisZip.UI.Avalonia/Controls/IconView.cs`（仅当采用 4.4 回退时）
- `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml`（署名；**注意在 `Dialogs/` 不是 `Views/`**）
- `THIRD-PARTY-NOTICES.md`、`README.md`（「字体与图标资源」表）
- `docs/PLAN.md`、`docs/progress-avalonia-detail.md`
- 若归一化下沉 Core：`src/MantisZip.Core/**`（与计划 2 协调，避免两处实现）

## 7. 验证

```powershell
dotnet run --project tools/IconGen -- --set C   # 幂等：重复运行 git 无 diff
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

- `lsp_diagnostics` 无错误。
- 手动：设置窗口风格下拉逐一预览；6 套 × Light/Dark × 选中态走查；暗色下 D 底块可见。

## 8. 边界 / 非目标

- **不做**：导入包格式/UI（计划 2）；成套预设（主题 × 图标绑定，D7 留口 §11）。
- **不改**：A 套（唯一事实来源仍是 `AppIcons.axaml`）；`IconView` 契约（除 4.4 回退这一处）。
- **不引运行时依赖**：第三方 SVG 仅在 **dev-time** 由 IconGen 处理并入编译期 axaml；运行时不依赖这些库。
- **许可**：只用 **MIT / ISC / Apache-2.0 / CC0** 等允许修改与再分发的宽松许可；**Lucide 为 ISC + Feather MIT 双声明**，不得简写为「MIT」；禁止 NC/ND/品牌素材库。E 套来源未定前不得开工（§4.5）。

## 9. 风险

| 风险 | 缓解 |
|------|------|
| 116 键语义映射人工校对量大 | 先批量自动匹配（键名 ↔ 源图标名）再人工复核；生成「未命中」清单集中处理 |
| E 套无合适宽松许可彩色源 | §4.5 决策门：**C 几何 + 固定色板程序化着色**兜底（不手绘 116 个） |
| G 的 `DynamicResource` 在 ResourceDictionary 中不解析 | 4.4 决策门 + 哨兵回退（T3 实测裁定） |
| 6 套全量体积 | 116 × 6 ≈ 700 drawings，path 合计 < 1MB（§5），无按需加载必要 |
| Duotone 双层语义（哪层 base / 哪层 accent）不一致 | 逐图标人工核对；D 与 G 共用同一分层结果 |
| **D 底块在暗色主题不可见**（黑 @20%） | 固定中性半透明灰 `#40808080`；T3 加 Light/Dark 双主题可见性断言 |
| **Lucide 许可写错导致合规风险** | 按 ISC + Feather MIT 两份声明写入 `THIRD-PARTY-NOTICES.md`；T6 核对原文 |
| 归一化两处实现漂移（IconGen vs 运行时） | 下沉 Core 共用一份；未下沉则 T1 断言同一 `viewBox` 样本结果一致 |
| 源 SVG 未 vendored 导致构建不可复现 | `sources/` 全部提交 + `Program.cs` 禁止联网；T2 断言重复运行零 diff |
| F 套渐变全仓首例、无既有写法 | 显式 `StartPoint`/`EndPoint`；T2 headless 断言两端色值 |
