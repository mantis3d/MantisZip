# 图标主题系统 — 计划 3：IconGen 多套 + C/D/E/F/G 内容生产

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **优先级**: P3 | **预估工时**: 20-30h（含人工校对与补齐）
> **来源**: [docs/superpowers/specs/2026-10-15-icon-theme-system-design.md](../../docs/superpowers/specs/2026-10-15-icon-theme-system-design.md)（已批准，§5）
> **前置**: **计划 1**（`icon-theme-infra.md`）必须已完成（IconGen A 套管线、`IconView`、`ResolveIconThemeUri` 骨架）。
> **与计划 2 关系**: 互不依赖，可并行；本文只产出「内置套内容与生成管线」，不碰导入/覆盖/设置 UI。

## 1. 目标与交付标准

把内置风格从「A 现状」扩到 **6 套（A/C/D/E/F/G）**，并让 IconGen 从单套（A）升级为多套产出：

1. 扩展 `tools/IconGen` 读「源库 SVG + 语义映射表」，确定性产出 `Icons.C/D/E/F/G.axaml`。
2. 116 键 × 5 新套的几何/画刷生产，缺口手绘补齐；**覆盖率契约**（对照 `IconTestViewModel` 注册表）测试锁定。
3. `ResolveIconThemeUri` 支持 `"C"…"G"`；About 页保留开源图标库署名。
4. 6 套 × Light/Dark × 选中态手动截图走查。

**DoD**：6 字典 × 116 键全存在；`SelectedIconTheme` 可切 C-G 且即时生效；构建 0 错误；新增测试全绿。

## 2. 现状核实

| 项 | 事实 | 位置 |
|----|------|------|
| A 套管线 | 计划 1 交付：`IconGen` 读 `AppIcons.axaml`(116 `<Geometry>`) → `Icons.A.axaml`（单部件 `GeometryDrawing` 无 Brush） | `tools/IconGen/` |
| 键集契约 | 116 键 `IconPascalCase`；`IconTestViewModel` 注册表 = 覆盖率契约 | `ViewModels/IconTestViewModel.cs` |
| 主题装配 | 计划 1 的 `ResolveIconThemeUri(string?)`（当前仅映射到 A） | `App.axaml.cs` |
| 画刷语义 | `IconView`：部件 `Brush==null` → `Foreground`；非空 → baked。`ThemeAccentBrush` 为强调色资源键 | 计划 1 `IconView.cs` |
| 第三方协议 | Phosphor MIT / Lucide MIT / Tabler MIT（仅需署名） | §5 |

## 3. 六套美术方向（对齐 §5 表）

| 套 | 源 | 部件与画刷 | 上下文着色 | 选中态 |
|----|----|-----------|-----------|--------|
| A 现状 | `AppIcons.axaml`（已有） | 单部件 / null | ✅ 全保留 | 转白 |
| C 粗圆角 | Phosphor **Bold** 按语义映射 | 单部件 / null | ✅ 全保留 | 转白 |
| D 双色 | Phosphor **Duotone** 分层（base + accent 双 path） | 底块 baked 半透明 + 线框 null | ✅ 线框保留 | 线框转白，底块保持 |
| E 彩色 | 彩色图标库 / 手绘，每图标 2+ 部件 | 全 baked 原色 | ⚠️ 不跟随 Foreground | 保持彩色（已批准接受项） |
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
    ├── phosphor-bold/*.svg       # C
    ├── phosphor-duotone/*.svg    # D（base+accent 双层）
    ├── color/*.svg               # E
    └── manual/*.svg              # 缺口手绘（全部套通用兜底）
```

### 4.2 `mapping.json`（示例）

```json
{
  "IconFolder":     { "c": "phosphor-bold/folder.svg", "d": "phosphor-duotone/folder.svg", "e": "color/folder.svg", "manual": null },
  "IconSearch":     { "c": "phosphor-bold/magnifying-glass.svg", "d": "phosphor-duotone/magnifying-glass.svg", "manual": null }
  // …116 键
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
- **归一化**：所有源 SVG 归一化到 24×24（复用计划 2 `SvgIconParser` 的归一化逻辑；若计划 2 未先落地，则 IconGen 内置等价最小归一化）。

### 4.4 G 套强调色画刷（**决策门**，需实测）

规格 §5 要求 G 底块画刷为 `DynamicResource`（随主题换色）。但 `GeometryDrawing` 位于 `ResourceDictionary` 中、非 `IResourceHost`，`{DynamicResource ThemeAccentBrush}` 能否解析**不确定**。

- **首选**：`Icons.G.axaml` 中 `<GeometryDrawing Geometry="…" Brush="{DynamicResource ThemeAccentBrush}" />`；headless 测试断言渲染像素随 Light/Dark 强调色变化。
- **回退（若首选不可解析）**：定义哨兵画刷 `IconView.AccentMarkerBrush`（静态 `IBrush` 实例）；G 的 base 部件 `Brush` 设为该哨兵；`IconView.Render` 遇哨兵替换为内部订阅的 `ThemeAccentBrush`。语义等价，无 DynamicResource 依赖。
- 采用哪种以 T3 的实测测试结果为准，并在计划文件「决策记录」追加一行。

## 5. 任务分解（TDD；每任务一 commit）

- [ ] **T0 — 116 键映射盘点**
  - 导出 `AppIcons.axaml` 116 键清单 → 生成 `mapping.json` 骨架（键全列出、源留空）。
  - **产出**：`mapping.json` + 盘点表（键 / 语义 / 拟用源 / 是否需手绘）。
  - 人工填 Phosphor Bold/Duotone 映射；未命中键标记手绘。commit。

- [ ] **T1 — IconGen 多套化（结构）**
  - `Program.cs` 支持 `--set A|C|D|E|F|G`；读 `mapping.json` + `sources/`；按 4.3 产出。
  - **测试**：给定最小 `mapping.json` + 1 个 mock SVG → 产出 C/D/E/F/G 各 1 键；缺映射 → 报错含缺键。commit。

- [ ] **T2 — C/F 套生成**
  - 拉取 Phosphor Bold（MIT）→ `sources/phosphor-bold/`；生成 `Icons.C.axaml`；F = C 几何 + 渐变。
  - **测试**：C 键集 == 116 且全无 Brush；F 键集 == 116 且全含渐变 Brush。commit。

- [ ] **T3 — D/G 套生成 + G 强调色决策门**
  - 拉取 Phosphor Duotone → `sources/phosphor-duotone/`；生成 `Icons.D.axaml`（base baked + accent null）；G 同几何 + 强调色 base。
  - **实测**：headless 断言 G base 像素随 Light/Dark 变化（首选 DynamicResource）；若失败 → 切 4.4 回退方案并复测。
  - **测试**：D/G 键集 == 116；D 至少 1 base baked + 1 null；G 语义同上。commit。

- [ ] **T4 — E 套生成（含手绘补齐）**
  - 彩色源 / 手绘 → `sources/color/`；生成 `Icons.E.axaml`（全 baked）。
  - **测试**：E 键集 == 116 且全部件有 Brush（无 null）。commit。

- [ ] **T5 — 覆盖率契约测试（6 × 116）**
  - 测试：`Icons.{A,C,D,E,F,G}.axaml` 键集全部 == `IconTestViewModel` 注册表 `ResourceKey` 非空集（116）；缺键/多键失败并打印差异。
  - 该测试即「缺键测试期报警」实现（§5）。commit。

- [ ] **T6 — 接线 `ResolveIconThemeUri` + About 署名**
  - `App.axaml.cs`：`"C"…"G"` → `Icons.{X}.axaml`（avares URI）；未知 → A。
  - About 页新增「图标库」署名（Phosphor / Lucide / Tabler，MIT）——库名为专有名词，走规则 13 豁免（或三语成对，择一并在注释注明）。
  - **测试（headless）**：`SelectedIconTheme="D"` → `ApplyTheme()` 后 `IconFolder` 解析为 D 版本（与 A 不同）。commit。

- [ ] **T7 — 文档与人工 QA**
  - `docs/progress-avalonia-detail.md` 追加（规则 3）；`docs/PLAN.md` 状态更新。
  - 手动：6 套 × Light/Dark × 选中态截图走查（§9.8）；确认 D/G 线框在选中态转白、E/F 保持（已批准差异 §10）。commit。

## 6. 涉及文件

**新增**
- `tools/IconGen/mapping.json`、`tools/IconGen/sources/**`
- `src/MantisZip.UI.Avalonia/Resources/Icons/Icons.{C,D,E,F,G}.axaml`
- 测试：IconGen 多套产出 / 覆盖率契约 / 6 套装配

**修改**
- `tools/IconGen/Program.cs`（多套）
- `src/MantisZip.UI.Avalonia/App.axaml.cs`（`ResolveIconThemeUri` 扩 C-G；G 回退时给 `IconView` 加哨兵逻辑）
- `src/MantisZip.UI.Avalonia/Controls/IconView.cs`（仅当采用 4.4 回退时）
- `Views/AboutWindow.axaml`（署名）
- `docs/PLAN.md`、`docs/progress-avalonia-detail.md`

## 7. 验证

```powershell
dotnet run --project tools/IconGen -- --set C   # 幂等：重复运行 git 无 diff
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

- `lsp_diagnostics` 无错误。
- 手动：设置窗口风格下拉逐一预览；6 套 × Light/Dark × 选中态走查。

## 8. 边界 / 非目标

- **不做**：导入包格式/UI（计划 2）；成套预设（主题 × 图标绑定，D7 留口 §11）。
- **不改**：A 套（唯一事实来源仍是 `AppIcons.axaml`）；`IconView` 契约（除 4.4 回退这一处）。
- **不引运行时依赖**：第三方 SVG 仅在 **dev-time** 由 IconGen 处理并入编译期 axaml；运行时不依赖这些库。
- **许可**：仅用 MIT 库（Phosphor/Lucide/Tabler）；E 套若用非 MIT 源需先确认协议，否则手绘。

## 9. 风险

| 风险 | 缓解 |
|------|------|
| 116 键语义映射人工校对量大 | 先批量自动匹配（键名 ↔ 源图标名）再人工复核；生成「未命中」清单集中处理 |
| E 套无合适 MIT 彩色源 | 手绘补齐（架构已被多部件 DrawingImage 吸收）；或找 MIT 彩色库 |
| G 的 `DynamicResource` 在 ResourceDictionary 中不解析 | 4.4 决策门 + 哨兵回退（T3 实测裁定） |
| 6 套全量体积 | 116 × 6 ≈ 700 drawings，path 合计 < 1MB（§5），无按需加载必要 |
| Duotone 双层语义（哪层 base / 哪层 accent）不一致 | 逐图标人工核对；D 与 G 共用同一分层结果 |
