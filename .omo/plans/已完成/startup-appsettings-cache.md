# 启动优化三期：AppSettings 缓存（singleton 化）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消除启动路径上 `AppSettings.Load()`（72 处调用点）反复全量读盘 + STJ 反序列化的开销，使每进程只读盘一次。

**Architecture:** `AppSettings.Load() / Save()` 增加进程级静态缓存（lock + `_cached`），`Save()` 成功后把被保存实例写回缓存；测试复位 API `ResetCacheForTests()`（internal，沿 `StartupTimer.ResetForTest()` 的 InternalsVisibleTo 惯例）。零调用点改造（72 处调用全走同一静态方法）。

**Tech Stack:** .NET 10 / Avalonia 12 / xUnit（UI.Avalonia.Tests）

---

## 背景（二期定罪结论，2026-10-19）

`startup-theme-dedup-trace` 二期已拆解 `Init.Theme` 段黑盒：

- ApplyTheme 同主题跳过路径实测 **0.3ms**（修复生效，无关）
- 段内余量 **76.8ms** = `AppSettings.Load()` 反复全量读盘 + STJ 反序列化：`MantisZip.UI.Avalonia/Models/AppSettings.cs:271-305`
  - 每次 `Load()`：`File.Exists` + `File.ReadAllText` + `JsonSerializer.Deserialize` + 迁移逻辑 + `GetDefaultAdaptiveOverrides`（空时）
  - 冷启动首调 ~60-77ms（STJ 反射元数据 + 磁盘 IO），暖调 2-16ms/次
  - 全仓 **72 个调用点**、20+ 文件（`MainWindowViewModel` 12、`PreviewViewModel` 10、`App` 19 等），启动路径至少 4 次串行调用
- `Init.Settings` 段暖态对照仅 2.3ms 佐证「一次 Load 成本大半是冷反序列化」

## 目标与诚实预期

- **门控**：A/B 后 fixed 侧 `ThemeSpan` 中位数 < 10ms（76.8ms → ~0.3-2ms 量级：ApplyTheme 内 Load 走缓存命中）
- **TOTAL**：预期收益 10-40ms（后续 3 次暖 Load 各省 2-16ms）；受环境噪声波动，只记录不强求、不作门控
- **首调冷成本（60-77ms）不消失**（必须付一次读盘+反序列化）；消灭它需 STJ source-gen（`JsonSerializerContext`），属另一个独立技术（YAGNI，本期不做，见「明确不做」）

## 行为变化说明（如实登记）

旧行为：每次 `Load()` 都读盘 → 运行期用外部编辑器手工改 `settings.json` 会在下一次 `Load()`（如重开某设置窗）时被拾取。
新行为：进程内缓存 → 启动后手工编辑的外部变更**不再被运行期拾取**；但所有由应用自身发起的修改（SettingsWindowViewModel 保存 → `Save()`）成功后缓存即时同步，用户可见行为不变。外部编辑文件且期待运行期热更属未受支持场景，接受此变化。

## 边界情况

1. **首调即失败**（settings.json 损坏抛异常）：catch 分支返回 `new AppSettings()`，**不写缓存**（下次 Load 重试读盘；避免把异常态固化）
2. **settings.json 不存在**：`CreateWithDefaults()` 结果**写缓存**（避免每次重新生成默认规则表）
3. **Save 失败**：不动缓存（缓存仍代表最后一次成功落盘值）
4. **多线程**：lock 全覆盖读与写（Load 非热路径，锁开销可忽略）
5. **测试隔离**：测试不真实调用 `Save()`（会覆盖真实用户 settings.json——`SettingsFile` 为 static readonly 不可注入，AGENTS.md 污染教训）；Save→缓存同步语义经 Task 3 GUI 验收覆盖（TDD 例外，沿用二期 Task 3 先例）

---

### Task 1: 基线快照（当前 HEAD 的 Release 构建）

**Files:**
- Create: `%TEMP%\opencode\startup-ab\baseline-v3\*`（构建产物快照，不入 git）

- [ ] **Step 1: Release 构建**

Run: `dotnet build -c Release src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 error。

- [ ] **Step 2: 复制产物到快照目录**

```powershell
New-Item -ItemType Directory -Force "$env:TEMP\opencode\startup-ab\baseline-v3" | Out-Null
Copy-Item -Recurse -Force "src\MantisZip.UI.Avalonia\bin\Release\net10.0\*" "$env:TEMP\opencode\startup-ab\baseline-v3\"
Test-Path "$env:TEMP\opencode\startup-ab\baseline-v3\MantisZip.UI.Avalonia.exe"
```
Expected: `True`（完整目录，供 Task 3 交错 A/B 基线）。

（无 commit——产物不入库）

### Task 2: AppSettings 缓存（TDD）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Models/AppSettings.cs:271-321`（Load/Save + 新字段）
- Test: `tests/MantisZip.UI.Avalonia.Tests/AppSettingsCacheTests.cs`（新建）

**TDD 例外说明**：Save→缓存同步语义不写单测（Save 直接写真实用户 settings.json，`SettingsFile` static readonly 无法沙箱注入；测试纪律「不改写真实设置」，沿镜像断言惯例）。该语义由 Task 3 GUI 验收覆盖：设置改主题→保存→重开设置窗仍显示新值。

- [ ] **Step 1: 写失败测试**

新建 `tests/MantisZip.UI.Avalonia.Tests/AppSettingsCacheTests.cs`：

```csharp
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// AppSettings 进程级缓存（startup-appsettings-cache 三期）：
/// Load 同实例复用、ResetCacheForTests 复位后重新读盘。
/// Save→缓存同步语义不在此测（Save 写真实用户文件，SettingsFile 不可注入），
/// 由 Task 3 GUI 验收覆盖。
/// </summary>
public class AppSettingsCacheTests
{
    [Fact]
    public void Load_IsCached_ReturnsSameInstanceAcrossCalls()
    {
        var a = AppSettings.Load();
        var b = AppSettings.Load();
        Assert.Same(a, b);
    }

    [Fact]
    public void ResetCacheForTests_NextLoadIsNewInstance_WithMirrorValues()
    {
        var before = AppSettings.Load();

        AppSettings.ResetCacheForTests();
        var after = AppSettings.Load();

        Assert.NotSame(before, after);
        Assert.Equal(before.DefaultFormat, after.DefaultFormat);
        Assert.Equal(before.Theme, after.Theme);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test -c Release tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~AppSettingsCacheTests"`
Expected: 编译失败（`AppSettings.ResetCacheForTests` 不存在）。

- [ ] **Step 3: 实现缓存（最小实现）**

`src/MantisZip.UI.Avalonia/Models/AppSettings.cs`：

(a) 紧贴类内属性区适当位置（建议 `Load()` 方法上方）加字段：

```csharp
    // ── 进程级缓存（startup-appsettings-cache：启动路径 72 处 Load 全量读盘+STJ 反序列化，冷 60-77ms/暖 2-16ms 每次重复支付）──
    private static AppSettings? _cached;
    private static readonly object SyncRoot = new();
```

(b) `Load()`（:271-305）整体替换为：

```csharp
    public static AppSettings Load()
    {
        lock (SyncRoot)
        {
            // 缓存命中（startup-appsettings-cache：每进程只读盘一次）
            if (_cached != null) return _cached;
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    var defaults = CreateWithDefaults();
                    _cached = defaults;   // 不存在时缓存默认值，避免每次重新生成默认规则表
                    return defaults;
                }
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateWithDefaults();
                // 向后兼容：从旧 AdaptiveCompressionMode 枚举迁移为三个独立开关
                if (settings.AdaptiveCompressionMode != AdaptiveCompressionMode.Disabled)
                {
                    settings.AdaptiveCompression = true;
                    settings.AdaptiveSmartDetect = settings.AdaptiveCompressionMode == AdaptiveCompressionMode.SmartDetect;
                    settings.MultiThreadedCompression = settings.AdaptiveCompressionMode == AdaptiveCompressionMode.MultiThreaded;
                    settings.AdaptiveCompressionMode = AdaptiveCompressionMode.Disabled;
                }
                // 首次安装时填充默认规则
                if (settings.AdaptiveOverrides.Count == 0)
                {
                    settings.AdaptiveOverrides = GetDefaultAdaptiveOverrides();
                }
                // 首次安装时填充默认仅存储格式（多线程模式用）
                if (settings.MultiThreadedStoreFormatIds.Count == 0)
                {
                    settings.MultiThreadedStoreFormatIds = GetDefaultMultiThreadedStoreFormatIds();
                }
                // 并行解压智能线程数越界 clamp 到 [1,16]（边界情况 #8）
                settings.SmartSsdDegree = Math.Clamp(settings.SmartSsdDegree, 1, 16);
                settings.SmartHddDegree = Math.Clamp(settings.SmartHddDegree, 1, 16);
                _cached = settings;
                return settings;
            }
            catch
            {
                // 异常态不缓存：下次 Load 重试读盘，避免把损坏文件态固化
                return new AppSettings();
            }
        }
    }
```

(c) `Save()`（:307-321）整体替换为：

```csharp
    public bool Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            // 落盘成功后缓存同步为被保存实例（外部可见值与内存一致）
            lock (SyncRoot)
            {
                _cached = this;
            }
            return true;
        }
        catch
        {
            // 失败不动缓存：缓存仍代表最后一次成功落盘值
            return false;
        }
    }
```

(d) `ResetCacheForTests`（加在 `Save()` 之后）：

```csharp
    /// <summary>测试钩子：清空进程级缓存（internal，经 InternalsVisibleTo；沿 StartupTimer.ResetForTest 惯例）。</summary>
    internal static void ResetCacheForTests()
    {
        lock (SyncRoot)
        {
            _cached = null;
        }
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test -c Release tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~AppSettingsCacheTests"`
Expected: 2 通过，0 失败。

- [ ] **Step 5: 全量构建 + Avalonia 测试套（无新增失败）**

Run: `dotnet build -c Release src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 error。
Run: `dotnet test -c Release tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`
Expected: 与二期同基线（306 总 / 300 过 / 3 跳过）+ 新增 2 过 = 308 总 / 302 过 / 3 跳过 / 3 失败（预存语料缺失）；**新增失败 = 0**。
> ⚠ 全量测试启动会写临时 trace/debug —— 无真实文件污染（测试无 Save 调用）。

- [ ] **Step 6: Release 冒烟（真实场景缓存行为）**

Run（8 秒后杀进程）:

```powershell
$exe = "src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe"
$log = "$env:LOCALAPPDATA\MantisZip\debug.log"
$before = (Get-Item $log -ErrorAction SilentlyContinue).Length
$p = Start-Process $exe -ArgumentList "--test" -PassThru
Start-Sleep -Seconds 8
Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($log)[$before..((Get-Item $log).Length-1)]) | Out-String
```
Expected: `[Theme] ApplyTheme theme=... 无 ERROR 行`、`--test 自检成功`。

- [ ] **Step 7: Commit**

```powershell
git add src/MantisZip.UI.Avalonia/Models/AppSettings.cs tests/MantisZip.UI.Avalonia.Tests/AppSettingsCacheTests.cs
git commit -m "perf(avalonia): AppSettings 进程级缓存消重复读盘，启动路径 72 处 Load 复用单例"
```

### Task 3: Release A/B 测量 + 归档验收

**Files:**
- Modify: `.omo/plans/未开始/startup-appsettings-cache.md`（回填 A/B 结果后随归档移入已完成）

- [ ] **Step 1: 复制修复后产物为 fixed 快照**

```powershell
New-Item -ItemType Directory -Force "$env:TEMP\opencode\startup-ab\fixed-v3" | Out-Null
Copy-Item -Recurse -Force "src\MantisZip.UI.Avalonia\bin\Release\net10.0\*" "$env:TEMP\opencode\startup-ab\fixed-v3\"
```

- [ ] **Step 2: 交错 A/B ×5（baseline-v3 = 本计划前 HEAD，fixed-v3 = 缓存后）**

```powershell
$exes = "$env:TEMP\opencode\startup-ab\baseline-v3\MantisZip.UI.Avalonia.exe|$env:TEMP\opencode\startup-ab\fixed-v3\MantisZip.UI.Avalonia.exe"
$env:AB_EXES = $exes; $env:AB_SCRIPT = "E:\GitHub\MantisZip\tools\measure-startup.ps1"
powershell -ExecutionPolicy Bypass -File "$env:TEMP\opencode\startup-ab\run-ab.ps1"
```
（run-ab.ps1 为二期遗留的临时启动器，读 AB_EXES 管道分隔符拆数组；已验证可用）

Expected（门控）：fixed 侧 `ThemeSpan` 中位数 < 10ms；TOTAL 差值记录（预期 −10~40ms，不作门控）。

- [ ] **Step 3: GUI 验收（Save→缓存同步，TDD 例外项）**

1. 设置窗口改主题 → 保存 → **重开设置窗口**显示新值（Save 后缓存同步）
2. 改两项设置（如紧凑度 + 预览字号）→ 保存 → 重开窗口逐项确认（`Save()` 后 `_cached=this` 生效）
3. 二期遗留回归清单（Light/Dark/System 三态、System 联动、Deep/Light 冷启动、HelpDialog 样式）

- [ ] **Step 4: 结果回填计划文档 + 归档移档**

A/B 结果小节追加 to 本文件 Task 3 之下 →
```powershell
Move-Item ".omo\plans\未开始\startup-appsettings-cache.md" ".omo\plans\已完成\startup-appsettings-cache.md"
```

#### A/B 结果（2026-10-19 实测回填，安静环境：UnrealEditor/Baidu/Epic 关闭后重测）

| 指标（中位数 n=5） | baseline-v3（无缓存） | fixed-v3（a96db21） | 差值 |
|---|---|---|---|
| TOTAL | 1396.6ms | 1387.8ms | **−8.8ms** |
| `ThemeSpan` | 72.6ms | 72.6ms | 0 |
| `Init.Settings` | 1.5ms | 1.5ms | 0 |

- **门控执行**：两项门控均**未达**——
  1. `ThemeSpan < 10ms`：**不可达**，立项门控设计错误（晋升实施期发现）：ApplyTheme 内的 `Load()` 是全进程**首次调用**，缓存必然先付一次冷读盘+反序列化（~73ms 之后才命中），成本无法由缓存消除——该门控从立项起就在逻辑上不可达，实施前复核该假设
  2. `TOTAL ≥ 50ms 差值`：实测 −8.8ms（干净环境 TOTAL 噪声极小、数据可信），低于预期下沿 −10~40ms——**启动端暖调用基数比预想小**（`Init.Settings` 段仅 1.5ms 佐证：暖态 Load 在实际调用序列中大多为 1-3ms 而非 2-16ms 上限）
- **缓存的价值重估（如实）**：
  - **启动端**：TOTAL −8.8ms（小而真实——两轮 n=5 中位数差稳定，波幅 <2ms）
  - **运行期（主要价值）**：72 处调用点（每次重开设置窗/打开压缩/解压对话框/右键菜单触发路径等）从每次 2-16ms 读盘+反序列化 → **0ms**；未来任何新增 `Load()` 调用点位零成本
  - 与二期修复组合后，`Init.Theme` 段的总开销为 ApplyTheme 内首调 Load 的 ~73ms；彻底消除它只能靠 STJ source-gen（本期「明确不做」，另行评估）
- **污染样本记录**：首次测量在重负载（UnrealEditor/BaiduNetdisk/EpicGamesLauncher 占 CPU）下进行，TOTAL 出现 4-25s 量级样本、全部作废；用户关闭重负载程序后重测，数据如上
- GUI 验收（Save→缓存同步 + 二期清单 5 项）待用户手动确认

### Task 4: PLAN/PROGRESS/detail 同步（规则 1/3）

- [ ] **Step 1: PLAN.md** — P2 区本期登记行改为已完成语义（含 ThemeSpan 前后数值）
- [ ] **Step 2: PROGRESS.md 2026-10 组** — 追加里程碑首行（`- **10-19** — AppSettings 进程级缓存……`）
- [ ] **Step 3: progress-avalonia-detail.md** — 顶部追加完成条目（数据 + commit SHA）
- [ ] **Step 4: Commit**

```powershell
git add docs/PLAN.md docs/PROGRESS.md docs/progress-avalonia-detail.md .omo/plans
git commit -m "docs: startup-appsettings-cache 计划完成归档(PLAN/PROGRESS/detail 同步)"
```

---

## 明确不做

- **STJ source-gen（`JsonSerializerContext`）**：能压缩首调冷成本（60-77ms → 预计 ~10-20ms），但需给 AppSettings 全字段写 context + 注意 JsonConverter（`LegacyIntToSizeStringConverter`）!!、字段规模 387 行高迁移风险 → 另行评估，本期 YAGNI
- **后台预加载线程**：首调必然在 Main 线程（ApplyTheme 依赖 Theme 值），并行化收益小、竞态风险大，不做
- **运行期文件监听（FileSystemWatcher 热更）**：旧行为也不保证运行期拾取（只有再次 Load 才读到），新增监听器超出本期范围

## 与已归档计划的关系

- 前置：`startup-theme-dedup-trace`（二期，已完成）——QoE 结论与 measure 工具直接复用
- 后续候选（来自二期发现，另行立项）：`Kernel32.WriteFile` 日志写盘 (~0.6%)、`XamlIlPopulate` 装配拆解、splash 计划（`startup-native-splash.md`——仍建议在缓存落地后启动，铁板覆盖更纯粹）
