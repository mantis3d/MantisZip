# 解压时递归解开内层压缩包（Nested Archive Extraction）

> **状态**: 📋 待实施（设计已与用户逐项定稿） | **创建**: 2026-11-05 | **优先级**: P2 | **预估工时**: 16-22h
> **分支**: 产品代码落 `alpha`；本文件与 `docs/PLAN.md` 的改动可在 `plans` 分支完成（规则 19）
> **目标**: 用户解压一个「套娃」压缩包时，把它们内部包含的压缩包也一并解开，不必手动二次解压

## 背景

真实场景：从网盘/论坛下载的分发包常是 `项目.zip` 里套 `源码.zip` + `数据.7z`；备份导出也常见「整包内含子包」。现在用户必须解完外层 → 进目录 → 逐个再解，次数一多就漏。

现有代码**没有任何嵌套解压逻辑**（全仓搜索 `nested` / 嵌套 / 递归解压均无实现），属于全新功能。

解压侧现状只有一条主线：`IExtractSession`（唯一执行器）+ `ExtractFlow`（单包执行）+ 三个引擎；对话框路径、CLI、拖拽、双击全部收敛到 `ExtractSession`，但**「原地解压」三条 legacy 路径**与 **CLI 直解**仍直接调 `ExtractService` / `engine.ExtractAsync`，是本次必须单独接线的位置（见 §4.2）。

## 1. 已定决策（2026-11-05 与用户逐项确认）

| # | 维度 | 决策 | 理由 / 备注 |
|---|------|------|------------|
| 1 | 内层落点 | 解到 `<内层包所在目录>\<包名去扩展名>\`，**原包保留** | 布局可预测、绝不覆盖外层文件、冲突概率最低 |
| 2 | 层级 | **递归**，深度可配置，默认 3，范围 1-10 | 套娃/多层备份一般在 3 层内；上限防失控 |
| 3 | 识别范围 | **只认常见压缩包扩展名**：`.zip .7z .rar .tar .gz .tgz .tar.gz`；不含 `.jar .war .ear .apk .iso`，**不做魔数猜测** | `.docx/.xlsx/.pptx/.epub` 文件头就是 ZIP，魔数扫描会把 Office 文档解开铺一地 |
| 4 | 内层密码 | 外层密码 → 密码库匹配 → 仍有解不开的包时**弹一次**密码窗（该密码应用到剩余所有内层包）→ 仍失败跳过并标记 | 批量场景不逐包打断；CLI 无交互，只走前两步 |
| 5 | 内层冲突 | **沿用本次冲突策略**，包括 `ask` 也弹窗 | 语义与主流程一致，不静默改用户选择 |
| 6 | 失败处理 | 跳过并标记，**整体仍算成功**；结果里列「N 个内层包未解压」及原因 | 外层文件已落地，不能因一个坏内层包让整次解压显示失败 |
| 7 | 入口覆盖 | **全覆盖**：解压对话框、三条原地解压、拖拽（含「解压选中项到」）、CLI | 否则同一个包双击解开了、右键没解开，会被当 bug 报 |
| 8 | 开关形态 | **全局设置（默认关）+ 对话框复选框（本次可改）** | 与「解压后打开目录 / 删除压缩包」完全同构；无对话框入口只能靠全局默认值 |
| 9 | 安全上限 | 深度设置 + **包数 500 / 累计写入 100GB** 双硬上限，超限停止嵌套阶段并标记 | 防 zip bomb 与失控递归把磁盘写满 |

## 2. 行为定义

### 2.1 基本规则

1. 外层解压**照常完成**（路径契约 `ExtractPathResolver` 不变），嵌套阶段在**同一进度窗、同一取消令牌**内串行追加。
2. 嵌套阶段扫描目标目录下、**本次解压之后出现**（`LastWriteTimeUtc >= runStartUtc - 2s`）且扩展名在白名单内的文件，按**目录深度/发现顺序**逐个解压。
3. 每个内层包的目标 = `Path.Combine(它所在目录, Path.GetFileNameWithoutExtension(包名))`。
4. 解出的新文件再次按同样规则扫描 → 递归，直到达到深度上限。
5. 同一物理路径（`Path.GetFullPath` 归一）在一次运行内只处理一次，防环。
6. 源压缩包（本次入口传入的 `ArchivePaths`）永不参与嵌套。
7. 后置动作（打开目录 / 删除原包）**只对外层生效**；不删除任何内层包。

### 2.2 示例

外层 `D:\out\project.zip` 内含 `docs\a.pdf`、`sub\inner.zip`（内含 `inner\a.txt`、`b.txt`），`inner.zip` 内又含 `pack.7z`：

```
D:\out\
  docs\a.pdf
  sub\inner.zip                 ← 保留
  sub\inner\inner\a.txt        ← 内层包解到同名目录
  sub\inner\b.txt
  sub\inner\pack.7z             ← 保留
  sub\inner\pack\...            ← 深度 2（若深度上限 ≥ 2）
```

### 2.3 深度定义

`深度 1` = 外层包直接解出的压缩包；`深度 N` = 第 N 层解出的压缩包。`MaxDepth = 3` 表示最多处理到第 3 层，第 4 层及更深的包原样保留、不报错、在结果里记一条 `SkippedDepthLimit`。

### 2.4 与「选中条目 / 文件过滤」叠加

嵌套阶段只处理**本次实际解出来的文件**（mtime 判据天然满足）；过滤掉的内层包不会出现在磁盘上，因此不会触发。选中条目路径走 `SelectedItemsExtractService` 时同理。

## 3. 架构方案（为什么不做在引擎层）

| 方案 | 做法 | 结论 |
|------|------|------|
| A. 引擎内递归 | 在 `SevenZipEngine.ExtractAsync` 里解完自己再扫自己 | ❌ 引擎没有密码库/冲突弹窗/进度窗语义；三个引擎都要改；Core 依赖 UI 概念 |
| B. 塞进 `ExtractService` | 解压 wrapper 里顺手扫目录再解 | ❌ `ExtractService` 只拿得到 options/password，拿不到冲突弹窗与密码端口；且与「唯一落地者」原则冲突 |
| C. 独立 Runner + 显式接线（选定） | 新服务负责扫描/递归/密码/冲突/上限/失败记录，各入口在解压完成后调用一次 | ✅ 可单测、职责单一、失败隔离；接线点多但每处只有一次调用 |

### 3.1 新增 Core 侧纯逻辑（可无 UI 测试）

`src/MantisZip.Core/Utils/NestedArchivePolicy.cs`：

```csharp
public static class NestedArchivePolicy
{
    public const int DefaultDepth = 3;
    public const int MinDepth = 1;
    public const int MaxDepth = 10;
    public const int MaxNestedArchives = 500;
    public const long MaxNestedBytes = 100L * 1024 * 1024 * 1024; // 100GB

    /// 扩展名白名单判定（大小写不敏感；.tar.gz 需先判复合扩展名）
    public static bool IsNestedArchive(string fileName);

    /// 内层包的解压目标目录：<所在目录>/<包名去扩展名>
    public static string GetNestedDestination(string archivePath);

    public static int ClampDepth(int depth);
}
```

`src/MantisZip.Core/Utils/NestedArchiveScanner.cs`：`FindCandidates(string destinationPath, DateTime runStartUtc, IReadOnlyCollection<string> excludedPaths, CancellationToken ct)` → 返回按「目录浅→深、同层按文件名」排序的候选路径列表；含扫描文件数上限（20 万）与 `EnumerationOptions { RecurseSubdirectories = true }`。

### 3.2 新增 UI 侧编排

`src/MantisZip.UI.Avalonia/Services/NestedExtractRunner.cs`：

```csharp
public sealed record NestedExtractRequest(
    string DestinationPath,
    int MaxDepth,
    IReadOnlyCollection<string> SourceArchives,   // 永不参与
    string ConflictAction,
    Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? ConflictDialog,
    Func<string, string?, string?>? PasswordResolver, // (archivePath, parentPassword) => password
    Action<string>? OnCandidateFound);            // 供进度窗写「正在解压内层包 N/M：x」与批处理行

public sealed record NestedExtractItemResult(string ArchivePath, int Depth, NestedExtractOutcomeKind Kind, string? Reason, long WrittenBytes);

public sealed class NestedExtractRunner
{
    public NestedExtractRunner(
        Func<string, IArchiveEngine?>? getEngine = null,       // 默认真实工厂（与 ExtractSession 同款端口注入）
        Func<PasswordService>? passwordServiceFactory = null);

    public Task<NestedExtractResult> RunAsync(
        NestedExtractRequest request,
        IProgress<ArchiveProgress> progress,
        CancellationToken ct);
}
```

职责（按顺序）：BFS 队列按深度分层 → 扫描候选 → 过滤（源包/重复/深度/包数/体积）→ 取密码 → `ExtractFlow.ExtractAsync`（复用现有单包执行，冲突弹窗与实时策略原样传入）→ 记录结果 → 继续下一层。

**密码解析顺序**：`request.PasswordResolver` 由调用方提供（对话框/拖拽用的是同一份取密策略）；默认实现 = 外层密码 → `PasswordManager.FindMatchingPasswords` + `PasswordService.QuickVerifyPasswordEx`。密码窗只在 GUI 宿主提供 `Func<...>` 时出现（CLI 传 `null` → 跳过）。

### 3.3 接线点（5 处，每处 ≤ 10 行）

| # | 位置 | 覆盖入口 |
|---|------|---------|
| 1 | `src/MantisZip.UI.Avalonia/Services/ExtractSession.cs:296`（⑧ 三分支合流之后、⑨ 回读开关之前） | 解压对话框、CLI `--extract`、拖拽兜底设置窗、双击解压（`MainWindow.axaml.cs:238/287`）、测试菜单 |
| 2 | `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs:73 RunSelectedItemsExtractionAsync` 尾部 | 拖拽直投（目标已检测）、右键「解压选中项到」 |
| 3 | `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Extract.cs:61 / 108 / 342` | 原地解压三条 legacy 路径（解压到此处 / 解压到指定名 / 智能解压） |
| 4 | `src/MantisZip.UI.Avalonia/App.CliExtract.cs:639 / 859`（`TryExtractArchiveAsync` / `TryExtractSmartAsync` 的 `engine.ExtractAsync` 之后） | CLI `--extract-here` / `--extract-to-name` / `--extract-smart` |

所有接线点的开关判定统一为：

```csharp
var settings = AppSettings.Load();
if (!settings.ExtractNestedArchives) return;
await new NestedExtractRunner().RunAsync(new NestedExtractRequest(
    dest, NestedArchivePolicy.ClampDepth(settings.NestedExtractDepth),
    sourceArchives, conflictAction, conflictDialog, passwordResolver, progress: progress, ct: ct), ...);
```

对话框路径的开关来自载荷 `ExtractSettings.ExtractNestedArchives`（本次可改），非对话框路径读 `AppSettings`。

## 4. 数据模型与设置

### 4.1 `ExtractSettings`（`Models/ExtractSettings.cs:69`，规则 20 不可变载荷）

新增两个 init-only 字段（快照，不在执行期回读窗口/设置）：

```csharp
public bool ExtractNestedArchives { get; init; }
public int NestedExtractDepth { get; init; } = NestedArchivePolicy.DefaultDepth;
```

`ExtractSettingsViewModel.BuildSnapshot()`（`ViewModels/ExtractSettingsViewModel.cs:1197` 附近）在确认事务内写入：开关取 VM 属性，深度取 `AppSettings.NestedExtractDepth` 快照。

### 4.2 `AppSettings`（`Models/AppSettings.cs` 解压段 89-119）

```csharp
/// 解压时递归解开内层压缩包（默认关）
public bool ExtractNestedArchives { get; set; } = false;
/// 递归层数上限（1-10，默认 3）
public int NestedExtractDepth { get; set; } = 3;
```

⚠️ 规则 23：两个属性必须**同一次提交内**接上运行时消费者（`ExtractSession` / 各接线点），否则 `-Task guards` 的 `settingswiring` 直接失败。

### 4.3 对话框 UI

`Dialogs/ExtractSettingsWindow.axaml:92` 之后（「删除压缩包」复选框下方）新增：

```xml
<!-- 解压时同时解开内层压缩包（本次生效，不写回全局设置） -->
<CheckBox IsChecked="{Binding ExtractNestedArchives}">
  <TextBlock Text="{Binding LocalizedStrings[Settings_Extract_NestedArchives]}" TextWrapping="Wrap" />
</CheckBox>
```

`ExtractSettingsViewModel`：新增 `[ObservableProperty] private bool _extractNestedArchives;`，构造函数读 `AppSettings.ExtractNestedArchives`；`LocalizedStrings` keys 数组（line 305-342）**必须登记**新 key，否则显示空白。

### 4.4 设置页 UI

`Views/SettingsWindow.axaml:556`（「解压后删除压缩包」之后）新增复选框 + 深度数字输入（仅开关打开时可见，规则 6）：

```xml
<CheckBox Content="{Binding ExtractNestedArchivesText}" IsChecked="{Binding ExtractNestedArchives}" />
<!-- 最大递归层数：受控数字输入（规则 22），1-10 -->
<StackPanel Orientation="Horizontal" Spacing="8" IsVisible="{Binding ExtractNestedArchives}">
  <TextBlock Text="{Binding ExtractNestedDepthText}" VerticalAlignment="Center" />
  <NumericUpDown Classes="intOnly" Minimum="1" Maximum="10"
                 Value="{Binding NestedExtractDepth, Mode=TwoWay}" />
</StackPanel>
```

`SettingsWindowViewModel`：`ExtractNestedArchives` / `NestedExtractDepth` 属性 + 两个 `XxxText` 文案属性 + `OnCultureChanged` 刷新 + 保存段（line ~2003 附近）写回，保存前 `NestedArchivePolicy.ClampDepth`。

### 4.5 本地化（规则 13，三语成对）

| Key | zh-CN | en | zh-TW |
|-----|-------|----|-------|
| `Settings_Extract_NestedArchives` | 同时解压内层压缩包 | Also extract nested archives | 同時解壓內層壓縮檔 |
| `Settings_Extract_NestedDepth` | 最大递归层数 | Max recursion depth | 最大遞迴層數 |
| `Status_NestedExtracting` | 正在解压内层包（{0}/{1}）：{2} | Extracting nested archive ({0}/{1}): {2} | 正在解壓內層壓縮檔（{0}/{1}）：{2} |
| `Status_NestedDone` | 已解开 {0} 个内层压缩包 | Extracted {0} nested archive(s) | 已解開 {0} 個內層壓縮檔 |
| `Status_NestedSkipped` | {0} 个内层包未解压：{1} | {0} nested archive(s) skipped: {1} | {0} 個內層壓縮檔未解壓：{1} |
| `Status_NestedDepthLimit` | 已达到 {0} 层上限，更深的压缩包已保留 | Depth limit {0} reached; deeper archives kept | 已達 {0} 層上限，更深的壓縮檔已保留 |
| `Status_NestedNeedPassword` | 内层压缩包需要密码：{0} | Nested archive needs a password: {0} | 內層壓縮檔需要密碼：{0} |
| `Status_NestedQuota` | 内层解压已达上限（{0} 个 / {1}），已停止 | Nested extraction quota reached ({0} archives / {1}) | 內層解壓已達上限（{0} 個 / {1}） |

## 5. 进度与结果呈现

- **同一进度窗**：嵌套阶段调用 `ProgressWindow.SetStatus("Status_NestedExtracting")` + `SetOperationNote(相对路径)`；窗口自动关闭逻辑不受影响。
- **总进度**：嵌套阶段字节不并入外层百分比（避免外层跑到 100% 又回退），进度条在嵌套阶段显示「当前内层包」的计划进度，右侧信息列照常。
- **结果**：外层完成文案后追加一行（若 `Succeeded>0` 或 `Skipped>0`），如「已解开 3 个内层压缩包；1 个未解压：corrupt.zip（CRC 错误）」。有跳过项时 `SetComplete` 同时保留窗口（📌），与测试失败同款。
- **取消**：共享同一 `CancellationToken`；取消后已完成的内层包保留，剩余项标记 `Cancelled`，整体结局仍为 `Cancelled`。
- **CLI**：逐行 `Console.WriteLine`，`--extract-here` 等直解路径在结束时打印嵌套汇总。

## 6. 安全与边界

| 项 | 处理 |
|----|------|
| 路径穿越 | 复用 `ExtractPathResolver` + `FileConflictHelper.GetSafePath`，不另写路径拼接 |
| 自嵌套（`inner.zip` 内含 `inner.zip`） | 深度上限 + 归一化路径去重，最多 3 层后保留 |
| 同名目录已存在 | 直接合并，按本次冲突策略处理 |
| 分卷包（`a.7z.001`、`a.part1.rar`、`z01`） | 扩展名不在白名单 → 不参与；文档里明确「内层分卷包需手动解」 |
| Office / EPUB / JAR | 按扩展名白名单天然排除；不做魔数识别 |
| 磁盘写满 | `IOException` 记为该内层包失败并继续；仍受 100GB 上限保护 |
| 扫描成本 | `EnumerationOptions.RecurseSubdirectories` + 20 万文件上限；超限停止扫描并记录日志 |
| 误扫（运行期间恰好下载完成的包） | mtime 判据 + 排除源包；启用嵌套时日志记录每个候选，便于排查 |
| 加密内层包无密码 | 跳过并标记，不中断 |
| CLI 无交互 | 不弹密码窗，只用已知密码 + 密码库 |

## 7. 测试计划

**Core 单测**（`tests/MantisZip.Tests`）
1. 白名单：`.zip/.7z/.rar/.tar/.gz/.tgz/.tar.gz` 命中；`.docx/.jar/.apk/.iso/.zip.001` 不命中；大小写不敏感。
2. `GetNestedDestination`：普通路径、去扩展名、尾部分隔符、UNC 路径。
3. `ClampDepth`：0 → 1，11 → 10，3 → 3。
4. `NestedArchiveScanner`：只返回 mtime 新于 runStart 的文件、排除源包、排序稳定。

**UI 单测**（`tests/MantisZip.UI.Avalonia.Tests`，用假引擎端口）
5. 深度 1：只解一层，第二层不处理。
6. 深度 3 + 三层套娃：三层全解、第 4 层保留。
7. 自嵌套 `a.zip` 内含 `a.zip`：不无限递归，命中深度上限。
8. 失败跳过：假引擎抛异常 → 结果 `Failed` 计数 + 其余继续 + 整体 `Completed`。
9. 包数上限 500 / 体积上限触发 → 停止并标记，不抛异常。
10. 取消：中途取消 → 剩余标记 `Cancelled`，不写残留。
11. 冲突重命名：内层包被 `overwrite→rename` 成 `inner (1).zip` 时，仍能按新名字找到并解压到 `inner (1)\`。

**手工验收矩阵**
| 入口 | 用例 |
|------|------|
| 对话框 | 开关默认取设置值；关→只解外层；开→按深度递归；深度 1/3/超限 |
| 三条原地解压 | 全局开 → 生效；全局关 → 不生效 |
| 拖拽 | 目标已检测（直投）+ 兜底设置窗两条路径都生效 |
| CLI | `--extract-here` / `--extract-smart` 生效；加密内层包跳过不弹窗 |
| 过滤 | 只解选中/过滤条目时，未解出的内层包不触发 |
| 异常 | 损坏内层包、密码错、磁盘满、取消 |

**门禁**：`pwsh -File scripts\verify-repo.ps1 -Task guards`（settingswiring + numericbinding）、`-Task i18n`、`-Task build -Target avalonia`、`-Task test -Target tests`、`-Task test -Target ui-tests`。

## 8. 涉及文件

**新增**
- `src/MantisZip.Core/Utils/NestedArchivePolicy.cs`
- `src/MantisZip.Core/Utils/NestedArchiveScanner.cs`
- `src/MantisZip.UI.Avalonia/Services/NestedExtractRunner.cs`
- `tests/MantisZip.Tests/NestedArchivePolicyTests.cs`
- `tests/MantisZip.Tests/NestedArchiveScannerTests.cs`
- `tests/MantisZip.UI.Avalonia.Tests/NestedExtractRunnerTests.cs`

**修改**
- `src/MantisZip.UI.Avalonia/Models/ExtractSettings.cs`（+2 字段）
- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（+2 字段）
- `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs`（属性 + keys 数组 + 快照）
- `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml`（复选框）
- `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` + `ViewModels/SettingsWindowViewModel.cs`（开关 + 深度）
- `src/MantisZip.UI.Avalonia/Services/ExtractSession.cs`（接线 1）
- `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs`（接线 2）
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Extract.cs`（接线 3）
- `src/MantisZip.UI.Avalonia/App.CliExtract.cs`（接线 4）
- `src/MantisZip.UI.Avalonia/Localization/strings.{zh-CN,en,zh-TW}.json`（8 个 key × 3）

## 9. 分批交付

| 阶段 | 内容 | 估时 |
|------|------|------|
| Phase 1 | Core 策略/扫描器 + Runner + 单测；`ExtractSession` 接线 + 对话框复选框 | 9-12h |
| Phase 2 | 另外 4 处接线（ExtractFlow / legacy VM / CLI）+ 设置页开关与深度 | 4-6h |
| Phase 3 | 进度文案/结果汇总打磨 + 手工验收矩阵 + 文档（PROGRESS / 细节 / RELEASE_NOTES 按规则 18 确认） | 3-4h |

## 10. 边界（本计划不做）

- 不做引擎层改造，不改 `IArchiveEngine` 契约。
- 不识别无扩展名/魔数包（`.bin`、`.001`），不处理分卷内层包。
- 不做「解开后删除内层包」选项（决策 1 明确保留原包）。
- 不把内层包计入批处理列表行（Phase 3 可选打磨，不阻塞）。
- 不改内层解压的后置动作（打开目录/删包只对外层）。
- 不新增 CLI 参数（CLI 读全局设置；如需 `--nested/--no-nested` 另开小计划）。

## 11. 风险

1. **扫描误伤**：目标目录恰有其他新压缩包时会被解开。缓解：mtime + 排除源包 + 日志；Phase 2 可改为「进度事件收集」精确方案（`ArchiveProgress.EntryKey`）。
2. **体积翻倍**：嵌套解压磁盘占用可能明显上升，100GB 上限是兜底不是预期。
3. **接线分散**：5 处调用点容易漏一处导致「某些入口不生效」。缓解：统一走 `NestedExtractRunner` + 手工验收矩阵逐项勾。
4. **进度语义**：同一窗口跑两阶段，若处理不好会让用户以为卡住。缓解：状态行显式写「正在解压内层包 2/7」。
