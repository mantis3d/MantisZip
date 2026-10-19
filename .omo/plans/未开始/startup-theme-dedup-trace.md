# 启动优化二期：ApplyTheme 双重加载修复 + dotnet-trace 黑盒定罪

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **状态**: 📋 未开始（前置 `startup-preview-defer` 阶段一/二已完成，基线 TOTAL≈2160ms）

## 背景

`startup-preview-defer`（已完成）交付后，启动路径剩余三个黑盒段（见该计划末尾「待定后续候选」章节）：

| 段 | 阶段一实测 | 说明 |
|---|---|---|
| `Avalonia.InitDone` | 597ms（Top1，29%） | App.axaml 资源解析 / 框架初始化 / 进程启动 JIT，内部构成未知 |
| `Win.Visible` | 316ms（Top3，15%） | Show + 布局测量内部构成未知 |
| `Init.Theme` | 116ms | `ApplyTheme` 内部构成未知 |

本轮两个前期结论：

1. **`Init.Theme` 已从「黑盒」转为「确定有肉」**：通读代码发现 `ApplyTheme` 存在双重加载——
   `App.axaml:10-12` 启动时已静态合并 `ThemeLight.axaml`（13KB）+ `AppIcons.axaml`（**85KB**，数百个 PathIcon 几何），
   而 `App.axaml.cs:516-550` 的 `ApplyTheme()` 首先无条件 `Resources.MergedDictionaries.Clear()`，
   随后用 `AvaloniaXamlLoader.Load(uri)` **运行时重新解析**主题字典与图标字典。

   按启动方式拆解浪费：
   - **亮色用户（默认路径）**：两份字典各解析两遍，第二遍（计入 `Init.Theme` 116ms）纯浪费
   - **暗色用户**：App.axaml 静态加载的 `ThemeLight` 被丢弃后换成 `ThemeDark`（Light 解析浪费），AppIcons 同样解析两遍
   - **System 模式**：`ColorValuesChanged` 每次系统主题变化都走同样的 Clear + 双 Load（图标字典与主题无关，却跟着重解析）

   另有一处顺带硬化点：现有代码 `Clear()` **先于** `Load`，若 `Load` 抛异常则合并字典整体清空、无兜底；
   修复方案改为「先 Load 成功再替换」，异常时保留旧字典。

2. **WebView2 已排除**：`NativeWebView` 不在任何 XAML 中声明（`PreviewPanel.axaml:762-778` 注释），
   惰性创建（`EnsureWebViewForHtml`）+ PreviewPanel 本身已延迟到首帧后 0.5s，启动关键路径零触碰，与启动慢无关。

## 目标

- 修复 `ApplyTheme` 双重加载：`Init.Theme` 116ms → 个位数毫秒（冷启动 TOTAL 预期 −100ms 量级）
- 用 `dotnet-trace` 对 Release 冷/暖启动采样，把 `Avalonia.InitDone` 与 `Win.Visible` 拆解为
  「JIT / XAML 解析 / 布局渲染 / 框架固定成本」四类，产出 R2R（ReadyToRun）是否立项的数据依据
- **不做** splash（独立计划 `startup-native-splash.md`，两者互补可并行）

## 方案一：ApplyTheme 缓存 + 条件跳过

### 根因复述（代码级）

```
启动时的实际加载序列：
App.axaml 静态合并   → [ThemeLight.axaml, AppIcons.axaml]      ← 计入 Avalonia.InitDone
ApplyTheme()        →  Clear() 全部丢弃
                       Load 主题字典（Light 用户再次解析 Light；Dark 用户丢弃 Light 解析 Dark）
                       Load AppIcons.axaml（85KB 再解析一遍）   ← 计入 Init.Theme 116ms
```

### 设计要点

1. App 类新增两个字段：
   ```csharp
   // 当前已合并的主题字典 URI。初值 = LightThemeUri：App.axaml 静态合并的默认主题。
   // ⚠ 契约：若修改 App.axaml MergedDictionaries 的默认主题，必须同步此初值。
   private Uri? _loadedThemeUri = LightThemeUri;

   // 图标字典缓存（与主题无关，跨主题切换复用，避免 85KB AppIcons 反复解析）
   private IResourceProvider? _cachedIconProvider;
   ```

2. `ApplyTheme()` 改为「目标 == 已加载则跳过；否则先 Load 后替换」：
   ```csharp
   private void ApplyTheme()
   {
       var isDark = /* 现有三态判断不变 */;
       var targetUri = new Uri(isDark ? DarkThemeUri : LightThemeUri);

       // 同主题（启动 Light 用户 / 重复调用）：跳过全部字典操作，只切 Fluent 变体
       if (_loadedThemeUri == targetUri)
       {
           RequestedThemeVariant = isDark ? Dark : Light;
           return;
       }

       try
       {
           // 先 Load 新主题，失败则保留旧字典兜底（修复现有 Clear 先于 Load 的隐患）
           var themeProvider = AvaloniaXamlLoader.Load(targetUri) as IResourceProvider;

           // 图标字典：优先取 App.axaml 静态加载的实例（MergedDictionaries[1]），不重新解析
           // ⚠ 契约：App.axaml MergedDictionaries 顺序 = [主题, 图标]，修改需同步
           _cachedIconProvider ??= Resources.MergedDictionaries.Count >= 2
               ? Resources.MergedDictionaries[1]
               : AvaloniaXamlLoader.Load(new Uri(IconResourcesUri)) as IResourceProvider;

           Resources.MergedDictionaries.Clear();
           if (themeProvider != null) Resources.MergedDictionaries.Add(themeProvider);
           if (_cachedIconProvider != null) Resources.MergedDictionaries.Add(_cachedIconProvider);

           _loadedThemeUri = targetUri;
           RequestedThemeVariant = isDark ? Dark : Light;
       }
       catch (Exception ex)
       {
           DebugLog($"[Theme] ApplyTheme ERROR: {ex.Message}");
       }
   }
   ```

3. **行为不变的路径**：`RefreshTheme()`（设置窗口保存）、`ColorValuesChanged`（System 联动）、
   `PlatformSettings` 判空、三态判断逻辑、`DebugLog` 均保持；仅字典操作从「无条件 Clear+双 Load」
   变为「同主题跳过 / 换主题单 Load + 图标复用」。

4. **重建后的顺序契约**：切换路径重建为 `[主题, 图标]`，与 App.axaml 静态顺序一致，
   `MergedDictionaries[1]` 取法在后续换肤中始终成立。

### 明确的边界情况

| 场景 | 行为 |
|---|---|
| 亮色用户冷启动（默认） | 跳过，`Init.Theme` ≈ 设置 `RequestedThemeVariant` 的耗时（个位数 ms） |
| 暗色用户冷启动 | 仅解析 `ThemeDark` 一次；AppIcons 复用 App.axaml 已加载实例 |
| 设置窗口 Light→Dark | 单 Load Dark + 图标复用 |
| 设置窗口 Dark→Light | 单 Load Light + 图标复用 |
| System 模式系统深浅色变化 | 每次换肤走单 Load 路径；同变体重复通知被 `targetUri` 相等跳过 |
| `Load` 抛异常 | try 内未动字典，旧主题/图标完整保留（优于现状：现状会清空） |

## 方案二：dotnet-trace 定罪采样

### 采集流程

```powershell
# 1) Release 构建
dotnet build -c Release src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# 2) 安装工具（若未装）
dotnet tool install --global dotnet-trace

# 3) launch 模式采集：从进程创建即开始（不漏早期 JIT），10s 覆盖启动→首帧
dotnet-trace collect --profile cpu-sampling --duration 00:00:10 --format nettrace -- `
  src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe
# duration 到期后 trace 已落盘，手动关闭应用窗口，属正常流程

# 4) 热点报告
dotnet-trace report xx.nettrace topN -n 40
# 需要深挖时再转 speedscope 人工分析
dotnet-trace convert xx.nettrace --format speedscope
```

### 协议

- 采 **3 次有效样本**（启动耗时波动大，取共识热点；单次样本噪声不采信）
- 预热 1 次不计；Debug 构建样本一律作废
- 样本间 TOTAL 差异 >2× 时记录当时的系统状态（杀软扫描/后台负载），重采

### 判读标准（热点 → 结论）

| 热点特征 | 归因 | 结论 |
|---|---|---|
| `ReadyToRunHelper` / `JitInterface` / `MethodDesc` 系列 | 启动 JIT | **R2R 立项依据**（见门控规则） |
| `AvaloniaXamlLoader` / `XamlIl` / XAML 解析栈 | XAML 解析 | App.axaml / 主题字典裁剪方向 |
| `MeasureOverride` / `ArrangeOverride` / Skia 渲染栈 | 布局渲染 | splash 兜底或进一步延迟加载 |
| CoreCLR host 启动 / 程序集加载 / GC 初始化 | 框架固定成本 | 铁板，靠 splash 覆盖，不再立项 |

### R2R 门控规则（预先锁定）

- JIT 相关耗时合计 **≥ 300ms** → 在 `docs/PLAN.md` 登记「ReadyToRun 试点」计划条目（本轮不实施 R2R 本体）
- JIT 相关 < 300ms → 不立项，结论只影响剩余段的处理优先级

## 执行顺序

1. **方案一先行**（代码改动小、确定收益、独立验收）
2. **方案二在方案一合入后采集**（trace 直接对修复后的基线采样，避免数据混入旧主题成本）
3. trace 结论回填 + PLAN.md 登记 R2R（若门控命中）

## 验收标准

### 方案一

1. `tools/measure-startup.ps1` 交错 A/B ×5（修复前 vs 修复后 Release 构建）：`Init.Theme` 段
   中位数从 ~116ms 降至 <10ms；TOTAL 中位数下降量 ≥ 50ms（其余段噪声波动允许 ±30ms；
   若 TOTAL 因环境噪声未达标但 `Init.Theme` 达标，以 `Init.Theme` 为准并记录噪声说明）
   - ⚠ 前置小改：`measure-startup.ps1` 当前聚合不含 `Init.Theme` 指标（New-Sample 仅提取
     Total/Init/XamlSpan/CtorSpan/WinSpan/Eager），需新增 `ThemeSpan` 提取并加进聚合输出
2. 回归清单（全部 GUI 手动）：
   - 设置窗口三态切换 Light/Dark/System 即时生效，样式无残留
   - System 模式下系统深浅色切换联动正常
   - 亮/暗两种设置各冷启动一次：窗口内按钮色/边框/图标列（AppIcons 资源）全部正常
   - 帮助弹窗（HelpDialog）与设置窗口样式正常
3. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 0 error；全量测试无新增失败
4. trace 验证：亮色启动样本经 `measure-startup.ps1` 聚合确认 `ThemeSpan` < 10ms

### 方案二

5. 3 份有效 nettrace 报告落盘（`%LOCALAPPDATA%\MantisZip\startup-trace-report\` 或项目 `tools/` 下，
   nettrace 原始文件不入 git）
6. 结论回填 `startup-preview-defer.md`「待定后续候选」表：三段「段内未知」列改为实测构成
7. R2R 门控判定按上述规则执行并在计划中记录

## 工时预估

| 项 | 工时 |
|---|---|
| 方案一：ApplyTheme 修复 + measure-startup.ps1 加 ThemeSpan 指标 + A/B 测量 + 回归 | 1.5-2h |
| 方案二：trace 采集 + 分析 + 结论回填 | 1-1.5h |
| **合计** | **2.5-3.5h** |

## 明确不做

1. R2R 本体实施（由本轮 trace 门控决定，命中后另立计划）
2. splash 本体（`startup-native-splash.md` 独立计划，与本轮互补不互斥）
3. NativeAOT / PublishTrimmed（既有约束延续）
4. `Win.Visible` 段的进一步拆解优化（除非 trace 明确指向可延迟子项）
5. WebView2 相关改动（已排除与启动的相关性）
6. 不改版本号；不做无关重构

---

## 实施计划（任务级）

### 文件结构总览

| 文件 | 动作 | 职责 |
|---|---|---|
| `src/MantisZip.UI.Avalonia/App.axaml.cs` | 修改 | 2 个字段 + `ApplyTheme` 条件跳过/单 Load 重写 |
| `tools/measure-startup.ps1` | 修改 | 新增 `ThemeSpan` 段指标（A/B 验收依赖） |
| `tests/*` | 不动 | 见 Task 3 的 TDD 例外说明 |
| `.omo/plans/未开始/startup-theme-dedup-trace.md` | 执行时先移入 `进行中`，完成后移入 `已完成` 边回填结果 | 计划本体 |
| `.omo/plans/已完成/startup-preview-defer.md` | 修改（仅方案二） | 「待定后续候选」表回填实测结论 |
| `docs/PLAN.md` | 修改 | 本条目状态更新；R2R 门控命中时新增 ReadyToRun 试点条目 |
| `docs/progress-avalonia-detail.md` | 修改 | 完成/立项进度条目（规则 3） |

**门控决策规则（Task 4/5 检查点，预先锁定）：**
- Task 4：`ThemeSpan` 中位数 < 10ms → 方案一验收通过；TOTAL 差 ≥ 50ms 为加分项（环境噪声时以 ThemeSpan 为准）
- Task 5：JIT 相关热点合计 ≥ 300ms → 在 PLAN.md 登记 ReadyToRun 试点；否则不立项

**执行前动作（规则 1）：** 将本计划文件从 `.omo/plans/未开始` 移入 `.omo/plans/进行中`，再开始 Task 1。

**与 `large-file-split` 计划的衔接注记**：两计划都触碰 `App.axaml.cs`（2026-10-19 detail 条目已核验此耦合）。
任一先行均可行：
- 若本计划先行 → 本计划 Task 3 的行号锚点（:43 / :516-550）在当前未拆分文件上有效；
- 若 large-file-split 先行 → Task 3 锚点按拆分后布局解读（`ApplyTheme` 与主题常量位于 `App.Theming.cs`），
  以方法名 + 邻近常量定位，勿按行号。

---

### Task 1: 基线快照（当前 HEAD 的 Release 构建）

**Files:**
- Create: `%TEMP%\opencode\startup-ab\baseline\*`（构建产物快照，不入 git）

- [ ] **Step 1: Release 构建**

Run: `dotnet build -c Release src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 error。

- [ ] **Step 2: 复制产物到快照目录**

```powershell
New-Item -ItemType Directory -Force "$env:TEMP\opencode\startup-ab\baseline" | Out-Null
Copy-Item -Recurse -Force "src\MantisZip.UI.Avalonia\bin\Release\net10.0\*" "$env:TEMP\opencode\startup-ab\baseline\"
```

Expected: Exe 存在：

Run: `Test-Path "$env:TEMP\opencode\startup-ab\baseline\MantisZip.UI.Avalonia.exe"`
Expected: `True`

> 快照必须是**完整目录**（framework-dependent 依赖 DLL 就地取用），A/B 测量以此为未修复基线。

（无 commit——构建产物不入库）

---

### Task 2: measure-startup.ps1 新增 ThemeSpan 指标

**Files:**
- Modify: `tools/measure-startup.ps1`（`New-Sample`、round 输出行、聚合输出三处）

- [ ] **Step 1: New-Sample 增加 ThemeSpan 提取**

`New-Sample` 函数内（`$firstFrame` 提取行之后）加三行，并把 `ThemeSpan` 写进返回对象：

```powershell
function New-Sample([string]$section) {
    if ($section -notmatch 'TOTAL to last mark = ([\d\.]+)ms') { return $null }
    $total = [double]$Matches[1]
    $init = Get-Cumulative $section 'Avalonia.InitDone'
    $ctorEnter = Get-Cumulative $section 'Win.Ctor.Enter'
    $winXaml = Get-Cumulative $section 'Win.Xaml'
    $ctorExit = Get-Cumulative $section 'Win.Ctor.Exit'
    $firstFrame = Get-Cumulative $section 'Win.FirstFrame'
    $initEncoding = Get-Cumulative $section 'Init.EncodingOle'
    $initTheme = Get-Cumulative $section 'Init.Theme'
    $eager = $null
    if ($section -match 'preview-eager total = ([\d\.]+)ms') { $eager = [double]$Matches[1] }
    return [pscustomobject]@{
        Total    = $total
        Init     = $init
        XamlSpan = if ($null -ne $ctorEnter -and $null -ne $winXaml) { $winXaml - $ctorEnter } else { $null }
        CtorSpan = if ($null -ne $ctorEnter -and $null -ne $ctorExit) { $ctorExit - $ctorEnter } else { $null }
        WinSpan  = if ($null -ne $ctorEnter -and $null -ne $firstFrame) { $firstFrame - $ctorEnter } else { $null }
        ThemeSpan = if ($null -ne $initEncoding -and $null -ne $initTheme) { $initTheme - $initEncoding } else { $null }
        Eager    = $eager
        HasPanel = [bool]($section -match 'Preview\.Panel\.Ctor')
    }
}
```

- [ ] **Step 2: round 输出行加 theme 字段**

```powershell
"round ${i} [$tag]: total $($sample.Total), init $($sample.Init), xaml $($sample.XamlSpan), ctor $($sample.CtorSpan), win $($sample.WinSpan), theme $($sample.ThemeSpan), eager $($sample.Eager), panel=$($sample.HasPanel)"
```

- [ ] **Step 3: 聚合输出加 ThemeSpan 行**

在 `WinSpan` 聚合行之后：

```powershell
    "ThemeSpan        = $(Get-Median @($s | ForEach-Object ThemeSpan)) ms   (Init.EncodingOle -> Init.Theme)"
```

- [ ] **Step 4: 用基线快照验证解析**

Run: `.\tools\measure-startup.ps1 -ExePath "$env:TEMP\opencode\startup-ab\baseline\MantisZip.UI.Avalonia.exe" -Runs 2 -WarmupRuns 0`
Expected: round 行与聚合输出含 `ThemeSpan` ≈ 110-125ms（旧代码启动时 ApplyTheme 全量重解析 116ms 量级）。

- [ ] **Step 5: Commit**

```powershell
git add tools/measure-startup.ps1
git commit -m "chore(avalonia): measure-startup.ps1 新增 ThemeSpan 启动段指标"
```

---

### Task 3: ApplyTheme 双重加载修复

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs`（字段区 :43 后 + `ApplyTheme` :516-550 整体替换）

**TDD 例外说明**：本任务不做单元测试。原因：`ApplyTheme` 为 private 且依赖 `PlatformSettings`、
真实 `%LOCALAPPDATA%\MantisZip\settings.json`（单测会读真实用户设置，有污染教训，参见
`file-assoc-persistence-fix.md` 立项背景）；Avalonia headless 环境构造 App + 合并字典的测试基建
成本不成比例。验收以 Task 4 的 A/B 测量（StartupTimer 客观数据）+ GUI 回归清单替代。

- [ ] **Step 1: 添加字段（:43 `CompressCombinedPipeName` 常量之后）**

```csharp
    // ── 主题字典缓存（startup-theme-dedup-trace：消除启动期 ApplyTheme 双重加载）──

    // 当前已合并的主题字典 URI。初值 = LightThemeUri：App.axaml:10 静态合并的默认主题。
    // ⚠ 契约：若修改 App.axaml MergedDictionaries 的默认主题，必须同步此初值；
    //   若修改其顺序（[主题, 图标]），必须同步 ApplyTheme 内的图标取法（MergedDictionaries[1]）。
    private Uri? _loadedThemeUri = LightThemeUri;

    // 图标字典缓存（与主题无关，跨主题切换复用，避免 85KB AppIcons.axaml 反复解析）
    private IResourceProvider? _cachedIconProvider;
```

- [ ] **Step 2: 替换 ApplyTheme 方法体（:516-550，完整替换）**

```csharp
    /// <summary>
    /// 应用主题（三态）：System=跟随系统，Light=亮色，Dark=暗色。
    /// 替换主题 ResourceDictionary 并设置 RequestedThemeVariant 供 FluentTheme 使用。
    /// 启动优化（startup-theme-dedup-trace）：同主题跳过 Clear+重解析（App.axaml 已静态合并
    /// ThemeLight + AppIcons(85KB)，启动时重复解析两次为纯浪费）；换肤时先 Load 新字典成功后
    /// 再替换，图标字典复用缓存实例（异常时旧字典完整保留，优于旧实现的 Clear 先于 Load）。
    /// </summary>
    private void ApplyTheme()
    {
        if (PlatformSettings is not IPlatformSettings ps) return;
        try
        {
            var settings = AppSettings.Load();
            bool isDark = settings.Theme switch
            {
                "Light" => false,
                "Dark" => true,
                _ => ps.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark,
            };
            DebugLog($"[Theme] ApplyTheme theme={settings.Theme} dark={isDark}");

            var targetUri = new Uri(isDark ? DarkThemeUri : LightThemeUri);

            // ── 同主题（启动默认亮色 / 重复调用）：跳过全部字典操作，只切 Fluent 变体 ──
            if (_loadedThemeUri == targetUri)
            {
                RequestedThemeVariant = isDark
                    ? global::Avalonia.Styling.ThemeVariant.Dark
                    : global::Avalonia.Styling.ThemeVariant.Light;
                return;
            }

            // ── 换肤：先 Load 新主题（失败则进入 catch 保留旧字典），图标复用缓存不重解析 ──
            var themeProvider = AvaloniaXamlLoader.Load(targetUri) as IResourceProvider;

            _cachedIconProvider ??= Resources.MergedDictionaries.Count >= 2
                ? Resources.MergedDictionaries[1]
                : AvaloniaXamlLoader.Load(new Uri(IconResourcesUri)) as IResourceProvider;

            Resources.MergedDictionaries.Clear();
            if (themeProvider != null) Resources.MergedDictionaries.Add(themeProvider);
            if (_cachedIconProvider != null) Resources.MergedDictionaries.Add(_cachedIconProvider);

            _loadedThemeUri = targetUri;

            // ── Set theme variant for FluentTheme ──
            RequestedThemeVariant = isDark
                ? global::Avalonia.Styling.ThemeVariant.Dark
                : global::Avalonia.Styling.ThemeVariant.Light;
        }
        catch (Exception ex)
        {
            DebugLog($"[Theme] ApplyTheme ERROR: {ex.Message}");
        }
    }
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 error。

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`
Expected: 无新增失败。

- [ ] **Step 4: Debug 冒烟**

Run: `dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

Expected（开 Settings 开启 EnableDebugLogging 后查 `%LOCALAPPDATA%\MantisZip\debug.log`）：
- `[Theme] ApplyTheme theme=... dark=False` 无 ERROR 行
- 窗口正常打开：按钮色/边框色（ThemeXxxBrush）/图标列（AppIcons）全部正常

- [ ] **Step 5: Commit**

```powershell
git add src/MantisZip.UI.Avalonia/App.axaml.cs
git commit -m "perf(avalonia): ApplyTheme 同主题跳过重解析+换肤单Load图标缓存，启动省~100ms"
```

---

### Task 4: Release A/B 测量 + GUI 回归（方案一验收）

**Files:**
- Modify: `.omo/plans/未开始/startup-theme-dedup-trace.md`（回填 A/B 结果）

- [ ] **Step 1: 修复后 Release 构建**

Run: `dotnet build -c Release src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 error（产物在 `bin\Release\net10.0\`，与 Task 1 基线快照对应交错测量）。

- [ ] **Step 2: 交错 A/B ×5**

```powershell
.\tools\measure-startup.ps1 -ExePath @(
  "$env:TEMP\opencode\startup-ab\baseline\MantisZip.UI.Avalonia.exe",
  "src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe") -Runs 5 -WarmupRuns 1
```

Expected（门控对照设计「验收标准-方案一」）：
- baseline 侧 `ThemeSpan` 中位数 ≈ 110-125ms；fixed 侧 < 10ms → **通过**
- `TOTAL` 中位数差 ≥ 50ms；若受环境噪声（TOTAL 波动 >2×）影响未达 → 以 ThemeSpan 为准，记录噪声说明

- [ ] **Step 3: GUI 回归清单（手动）**

1. 设置窗口三态切换 Light/Dark/System：即时生效、样式无残留
2. System 模式下系统深浅色切换联动正常（Win10 深浅色或设置改注册表模拟均可）
3. Theme=Dark 保存后冷启动：暗色完整（含图标列/按钮/边框）
4. Theme=Light 保存后冷启动：亮色完整
5. HelpDialog 与设置窗口样式正常（字典复用路径的消费者抽查）

- [ ] **Step 4: 结果回填计划文档（Task 4 章节下新增「A/B 结果」小节）+ Commit**

```powershell
git add .omo/plans/未开始/startup-theme-dedup-trace.md
git commit -m "docs: 启动-theme-dedup 计划回填 A/B 测量结果"
```

---

### Task 5: dotnet-trace 采集与定罪（方案二，依赖 Task 4 合入后的产物）

**Files:**
- Create: `%LOCALAPPDATA%\MantisZip\startup-trace-report\*.nettrace`（原始采样，不入 git）
- Modify: `docs/PLAN.md`（仅当 R2R 门控命中时新增条目）
- Modify: `.omo/plans/已完成/startup-preview-defer.md`（待定后续表回填实测构成）

- [ ] **Step 1: 工具就绪**

Run: `Get-Command dotnet-trace -ErrorAction SilentlyContinue`
缺失则：`dotnet tool install --global dotnet-trace`

- [ ] **Step 2: 预热 + 3 次有效采样（launch 模式，从进程创建即开始）**

对**修复后**的 Release exe（`src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe`）
预热 1 次（正常启动-关闭），然后每轮：

```powershell
dotnet-trace collect --profile cpu-sampling --duration 00:00:10 --format nettrace -- `
  "src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe"
```

- duration 到期 trace 自动落盘，随后 `Stop-Process` 关闭应用窗口
- 输出 nettrace 放 `%LOCALAPPDATA%\MantisZip\startup-trace-report\run-1/2/3.nettrace`
- 协议（设计文档）：Debug 构建样本作废；样本间 TOTAL 差 >2× 记录系统状态并重采

- [ ] **Step 3: 热点报告与判读**

```powershell
dotnet-trace report "$env:LOCALAPPDATA\MantisZip\startup-trace-report\run-1.nettrace" topN -n 40
```

（若 `topN` 参数拼写与版本不符，以 `dotnet-trace report --help` 为准；需要深挖时
`dotnet-trace convert <file> --format speedscope` 人工分析。）

按设计文档「判读标准」四分类归因：
| 热点特征 | 归因 | 结论 |
|---|---|---|
| `ReadyToRunHelper` / `JitInterface` / `MethodDesc` 系列 | 启动 JIT | R2R 立项依据 |
| `AvaloniaXamlLoader` / `XamlIl` / XAML 解析栈 | XAML 解析 | 资源裁剪方向 |
| `MeasureOverride` / `ArrangeOverride` / Skia 渲染栈 | 布局渲染 | splash 兜底或延迟加载 |
| CoreCLR host / 程序集加载 / GC 初始化 | 框架固定成本 | 铁板，靠 splash |

3 份样本**共识热点**（≥2 份命中）才采信，单份噪声不采信。

- [ ] **Step 4: 结论回填与门控执行**

1. 更新 `startup-preview-defer.md`「待定后续候选」表：三段「段内未知」列替换为实测构成
2. R2R 门控：JIT 相关合计 ≥ 300ms → 在 `docs/PLAN.md` 待实现设计方案表（P2 区）新增完整行：

   ```markdown
   | **P2** | 启动优化三期：ReadyToRun 试点 | [startup-readytorun.md](.omo/plans/未开始/startup-readytorun.md) | 🟡中 | 3-4h | 📋 按启动-theme-dedup-trace 计划的 trace 门控立项（实测 JIT 相关合计 xx ms ≥ 300ms）：dotnet publish 加 `-p:PublishReadyToRun=true`，产物体积/发布时长/兼容性（COM+ShellExt）A/B 评估，启动 trace 对照 |
   ```

   （计划本体文件 `startup-readytorun.md` 后续另行立项编写，本轮仅登记；实测毫秒数按 Task 5 数据填入。JIT < 300ms → 不立项，跳过本步。）
3. Commit

```powershell
git add .omo/plans/已完成/startup-preview-defer.md docs/PLAN.md
git commit -m "docs: 待定后续黑盒定罪结论回填（R2R门控判定=<按实测）"
```

---

### Task 6: 计划归档与进度文档同步（规则 1/3）

- [ ] **Step 1: 计划移档**

```powershell
Move-Item ".omo\plans\未开始\startup-theme-dedup-trace.md" ".omo\plans\进行中\startup-theme-dedup-trace.md"   # 执行开始时先移
# 全部任务完成后：
Move-Item ".omo\plans\进行中\startup-theme-dedup-trace.md" ".omo\plans\已完成\startup-theme-dedup-trace.md"
```

- [ ] **Step 2: PLAN.md 条目状态更新**

本条目描述追加完成情况（对照「验收标准」各条实际达成），状态行改已完成语义：
> ✅ 完成（日期）：Init.Theme 116ms → 实测 xx ms；trace 定罪结论……

- [ ] **Step 3: progress-avalonia-detail.md 追加条目**

Avalonia 段以 `**2026-10-XX**` 追加：ApplyTheme 双重加载修复（数据）、ThemeSpan 指标、
trace 定罪结论、（若命中）R2R 登记。

- [ ] **Step 4: 最终 Commit**

```powershell
git add .omo/plans docs/PLAN.md docs/progress-avalonia-detail.md
git commit -m "docs: startup-theme-dedup-trace 计划完成归档"
```
