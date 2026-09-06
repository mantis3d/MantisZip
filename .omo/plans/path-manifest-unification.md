# 路径清单统一（Path Manifest / A-B 数据集）Implementation Plan

> **状态**: ✅ 已实施（Step 7 交互验证清单待用户人工确认） | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜] (8/8)
> **前置依赖**: 无（在 extract-path-unification.md / result-preview-panel.md 已实施的基础上演进）

---

## 问题

`ResultTreeView` 结果预览面板的目的：在压缩/解压**之前**展示将要生成的所有文件路径，理论上预览 = 实际。但当前存在三类不一致：

### Bug 1：解压时文件过滤无效

链路（已逐行确认）：

```
ExtractSettingsWindow.GetFilteredEntryKeys()   // 返回匹配条目的 FullPath 列表
  → MainWindow.axaml.cs:95  evm.FilteredEntryKeys
  → MainWindowViewModel.ExtractArchive(1665)    filteredKeys
  → ExtractFlow.ExtractAsync(46)
      if (filteredKeys is { Count: > 0 })
          → engine.ExtractEntriesAsync(...)     // 只解压匹配项
      else
          → ExtractService.ExtractAsync(...)    // 全量解压！
  → ZipEngine.ExtractEntriesAsync(360-363)
      entries.Where(e => entryKeys.Contains(e.Key))
```

两个根因：

- **候选 A（逻辑缺陷）**：`ExtractFlow.cs:46` 判断 `filteredKeys is { Count: > 0 }`。当过滤条件匹配不到任何文件时返回空列表 → 走 else 分支**全量解压**。用户看到"过滤无效，全部解压"。
- **候选 B（key 不匹配，仅 ZipEngine）**：`GetFilteredEntryKeys()` 返回归一化 `e.FullPath`（`\`→`/`），而 `ZipEngine.ExtractEntriesAsync` 用 `entryKeys.Contains(e.Key)` 匹配 **SharpCompress 原始 key**（大小写敏感）。压缩包内路径用 `\` 或大小写不一致 → 全部匹配失败 → 过滤后 0 文件。
  - 已核实 `SevenZipEngine.cs:649` 已用 `HashSet<string>(entryKeys.Select(k => ArchivePath.Normalize(k)), StringComparer.OrdinalIgnoreCase)` 归一化匹配 ✅ —— 候选 B 只存在于 ZipEngine。

### Bug 2：压缩选"各自生成独立压缩包"路径不对

两侧各写一套路径计算，行为不一致：

| | 预览 `ComputeArchiveName`（ResultPreviewService.cs:246） | 实际 `ComputeSeparateOutputPath`（CompressService.cs:329） |
|---|---|---|
| **目录源** | `Path.GetFileName(dir)` → **完整目录名** | `Path.GetFileNameWithoutExtension(dir)` → **剥离点后缀** |
| 文件源 | `keepOriginalExt ? GetFileName : GetFileNameWithoutExtension` | 相同 ✓ |

当源是含点目录（如 `D:\Data\project.v2`）时：预览显示 `project.v2.zip`，实际生成 `project.zip`。

### Bug 3（新发现）：压缩侧过滤被算两次，语义分裂

**过滤逻辑存在两套执行路径，语义完全不同**：

| | 预览侧 | 执行侧 |
|---|---|---|
| 代码 | `BuildCompressPreview` → `BuildDirectoryNode`（ResultPreviewService.cs:451） | `CompressFlow.BuildRequest` → `FileFilterHelper.ApplyFilter` |
| 语义 | 递归枚举目录，**保留完整树**，不匹配节点标 `IsFilteredOut`（灰显） | **把目录展开**成匹配文件的绝对路径列表（`result.AddRange(matched)`，FileFilterHelper.cs:42），`SourcePaths` 从目录变文件列表 |

后果（10 目录 / 1 万文件 / 过滤剩 1000）：

| 模式 | 预览显示 | 实际执行 |
|---|---|---|
| Separate | 10 个压缩包（每目录一包，含完整树） | **1000 个压缩包**（每文件一个包！） |
| Manual/Combined | 1 个包，内含 10 目录完整树（9000 灰显） | 1000 个文件平铺进一包，目录层级取决于 commonRoot |

### 结构性根因

- **压缩侧**：预览与实际**各算各的**——`ComputeArchiveName`（UI）vs `ComputeSeparateOutputPath`（Core）；预览标记灰显 vs 执行 `ApplyFilter` 展开目录。无共享计算层、无共享数据集 ❌
- **解压侧**：`ExtractPathResolver`（Core/Utils）已是单一事实来源，`SelectedItemsExtractService` 经 `pathOverrides` 喂引擎 ✅；`ExtractSettingsWindow` 过滤解压的预览（`preserveFullPath: true` 完整路径）与实际（引擎 `GetSafePath` 完整路径）语义一致 ✅ —— 过滤分支**不需要** pathOverrides，Bug 1 只需修候选 A + B
- `CompressFlow.BuildRequest:55` 的 `KeepOriginalExtension` 读磁盘 AppSettings，预览读对话框 VM；`MainWindow.ShowCompressSettingsDialog` 未回传该字段 → 输入不同步 ❌

---

## 目标

1. 建立 **A/B 数据集**：A = 过滤前路径数据集，B = 过滤后路径数据集。**过滤只算一次**，预览与执行消费同一份 B，杜绝 Bug 3 类漂移
2. 消灭压缩侧两套路径计算（Bug 2 根因）：输出包路径在生成 B 时一次性算好**存入 B**，预览/执行只读 B，不再重算
3. 修解压过滤空匹配降级全量 + ZipEngine key 匹配不一致（Bug 1）
4. **时序一致性门禁**：预览构建期间禁用"压缩/解压"确定按钮（`IsBuildPending`），保证点击时 B 必为最新输入的结果
5. Phase 1 范围：**包内条目路径 = 同公式保证**（预览/执行各自推导但公式相同），完整闭环（entryName 进 B + 引擎消费）留 Phase 2

---

## 设计

### 核心概念：A/B 数据集

```
A（过滤前的路径数据集）                  B（过滤后的路径数据集）
SelectedPaths = 10 目录        →      CompressPlan {
                                          Items = [
                                              (SourcePath: D:\Data\1,
                                               OutputArchivePath: D:\Data\1.zip,
                                               IncludedFiles: [匹配文件绝对路径...] 或 null=全部),
                                              ...
                                          ]
                                      }
```

- **A = 过滤前的路径数据集**：用户选择的原始源列表（目录粒度保留）
- **B = 过滤后的路径数据集**：`CompressPlanItem(SourcePath, OutputArchivePath, IncludedFiles)` 三元组
  - `OutputArchivePath`：输出压缩包绝对路径（Separate 每源一个；Manual/Combined 共享一个），生成 B 时由 `CompressPathPlanner` 算好**存入 B，执行不再重算**
  - `IncludedFiles`：该源下匹配过滤条件的文件绝对路径清单；**null = 全部**（未开过滤 → 语义等价"B 直接复制 A"）
- **预览树同时获得 A 和 B**：预览树始终持有完整结构 + `IsFilteredOut` 标记（A 数据），`ResultTreeView.ShowFilteredGhosts`（已存在，默认 false）切换显示全部（A 视图）或只显示匹配（B 视图）—— UI 层已有现成开关
- **压缩/解压只获得 B**：执行消费构建时生成的 B（不重新 ApplyFilter、不重新 ComputeArchiveName）

**原则**：
1. **公式唯一**：输出包路径计算只存在于 `CompressPathPlanner`；包内相对路径（Phase 1）预览/执行各自推导但公式相同
2. **执行读 B 不重算**：UI 场景执行消费构建时缓存的 B（按钮门禁保证 B 不过期）；CLI 无 B（无预览构建）→ 用同一 planner 重算，公式同源结果一致
3. **动态决策不进入 B**：冲突重命名（Rename）、添加到已有包（Add）、覆盖/跳过是执行期用户决策，保留在执行侧

### A. 新增类型（Core）

```csharp
// Core/Abstractions/CompressPlan.cs
/// <summary>过滤后的压缩计划项：一个源 → 输出包路径 + 包含文件清单。</summary>
public sealed record CompressPlanItem(
    string SourcePath,                       // 源（目录或文件）
    string OutputArchivePath,                // 输出压缩包绝对路径（Separate 每源一个；Manual/Combined 共享）
    IReadOnlyList<string>? IncludedFiles);   // 匹配文件绝对路径；null = 全部（未开过滤）

/// <summary>过滤后的压缩计划（B 数据集）。</summary>
public sealed record CompressPlan(
    CompressOutputMode Mode,
    string? OutputPath,                      // Manual/Combined 总输出路径；Separate = null
    IReadOnlyList<CompressPlanItem> Items);
```

**Manual/Combined 多源共享包**：所有 `CompressPlanItem.OutputArchivePath` = 同一个 `OutputPath`，`Items` 每源一项（各自 `IncludedFiles`），执行合并白名单。

### B. CompressPathPlanner（Core，Bug 2 根因收敛）

**新增** `src/MantisZip.Core/Utils/CompressPathPlanner.cs`：

```csharp
/// <summary>压缩路径规划器（Single Source of Truth）。输出包路径唯一实现。</summary>
public static class CompressPathPlanner
{
    /// <summary>计算压缩包文件名（目录源用完整目录名；文件源按 keepOriginalExt 决定去扩展名）。</summary>
    public static string ComputeArchiveName(string sourcePath, string format, bool keepOriginalExt);

    /// <summary>计算输出路径（源 → 目标压缩包绝对路径）。目录源去尾分隔符取父目录。</summary>
    public static string ComputeOutputPath(string sourcePath, string format, bool keepOriginalExt);

    /// <summary>批量规划 Separate 模式输出路径（Bug 2 语义：目录源用完整目录名）。</summary>
    public static IReadOnlyList<CompressPlanItem> PlanSeparate(
        IReadOnlyList<string> sourcePaths, string format, bool keepOriginalExt);

    /// <summary>批量规划 Manual/Combined 模式（单输出包 + 全部源）。</summary>
    public static IReadOnlyList<CompressPlanItem> PlanSingle(
        IReadOnlyList<string> sourcePaths, string outputPath, string format);
}
```

**语义（与历史逐字一致，仅收敛实现）**：
- 目录源：`Path.GetFileName(sourcePath.TrimEnd('/', '\\'))`（完整目录名，忽略 keepOriginalExt）
- 文件源：`keepOriginalExt ? Path.GetFileName : Path.GetFileNameWithoutExtension`
- 扩展名：`format == "tar.gz" ? ".tar.gz" : "." + format`
- 父目录：目录源 `Path.GetDirectoryName(TrimEnd(...))`；文件源 `Path.GetDirectoryName`

**改造点**：

| # | 文件 | 改动 |
|---|------|------|
| 1 | `Core/Utils/CompressPathPlanner.cs` | 新增，唯一实现 |
| 2 | `Core/Services/CompressService.cs` | `ComputeSeparateOutputPath` 删除本地实现，改调 planner（或直接删除，由 B 驱动） |
| 3 | `UI.Avalonia/Services/ResultPreviewService.cs` | `ComputeArchiveName` 删除本地实现，改调 planner |

### C. B 生成（预览构建双产物，一次枚举）

`ResultPreviewService.BuildCompressPreview` 签名改为**同时产出树和 B**：

```csharp
public static (PreviewTreeNode Root, CompressPlan Plan) BuildCompressPreview(
    IReadOnlyList<string> sourcePaths, ...)
```

实现：
1. `CompressPathPlanner.PlanSeparate/PlanSingle` 先算出全部 `OutputArchivePath`（纯字符串，O(源数)）
2. `BuildDirectoryNode` 递归枚举目录时**顺手收集**匹配文件绝对路径 → 填入 `CompressPlanItem.IncludedFiles`（与树构建同一次 IO）
3. 预览树节点照常构建（保留 `IsFilteredOut` 标记 = A 数据）

**关键**：B 从树构建派生（一次枚举），不是第二次遍历目录——1 万文件只读一次磁盘。

### D. 执行侧消费 B

**`CompressFlow.BuildRequest`（UI 场景）** 改为从 VM 持有的 B 构建 request：

```csharp
// 不再 ApplyFilter 展开目录、不再 ComputeSeparateOutputPath、不再读磁盘 KeepOriginalExtension
var plan = vm.GetPlanForExecution();   // 返回构建时缓存的 B（按钮门禁保证不过期）
request.SourcePaths = plan.Items.Select(i => i.SourcePath).ToList();   // 目录粒度保留！
request.OutputPath = plan.OutputPath;
// 白名单：每源 IncludedFiles（null = 全量），经 ArchiveOptions 透传引擎
```

**`ArchiveOptions` 新增白名单字段**：

```csharp
/// <summary>压缩文件白名单（绝对路径）；null = 全量压缩。Separate 模式单源有效。</summary>
public IReadOnlySet<string>? FileWhitelist { get; set; }
```

- `CompressSeparateAsync`：逐源遍历 `plan.Items`，每源输出 `OutputArchivePath`，白名单 = 该项 `IncludedFiles` → `engine.CompressAsync(new[]{ source }, outputPath, options with whitelist, ...)`
- `CompressSingleAsync`：合并全部 `IncludedFiles` 为总白名单 → 单包

**引擎白名单支持**（Bug 3 根因修复，必做）：

| 引擎 | 改动 |
|---|---|
| `FileScanner.CollectFiles`（Core/Utils，internal） | 加 `IReadOnlySet<string>? whitelist` 参数，目录枚举时 `if (whitelist != null && !whitelist.Contains(file)) continue;` |
| `ZipEngine.CompressAsync` / `TarGzEngine.CompressAsync` | 从 `options.FileWhitelist` 透传（各一处调用） |
| `SevenZipEngine.CompressAsync` | 单目录 `CompressDirectory` **无法排除文件** → 过滤激活（whitelist 非空）时改走 `ExpandSourcePaths` + 白名单过滤 + `CompressFilesEncrypted` 展开路径；未过滤走原路径 |

**CLI 场景**：`--compress-separate` 手工构建 request 无 B、无 whitelist → `FileWhitelist = null` → 全量压缩（既有行为，Bug 3 不显现——CLI 无预览树）；输出路径由 planner 计算（与 UI 同公式）。

### E. 时序门禁 IsBuildPending（按钮门禁）

现状（两侧对称）：

| | 压缩侧 | 解压侧 |
|---|---|---|
| 加载覆层绑定 | `IsLoading="{Binding IsPreviewBuilding}"`（axaml:515）✅ | `IsLoading="{Binding IsPreviewBuilding}"`（axaml:118）✅ |
| `IsPreviewBuilding` 语义 | 仅慢构建（≥250ms）置 true（VM:552） | 仅慢构建（≥250ms）置 true（VM:189） |
| 确定按钮 | `CanExecuteStartCompress`（VM:997）不检查构建状态 ❌ | `ExtractCommand` 不检查构建状态 ❌ |

**新增 `IsBuildPending`**（严格构建中，快慢构建均置位）：
- 构建开始时（`++_previewBuildVersion` 之后）置 `true`
- 构建完成（`PreviewRoot`/B 写入后）/ 失败 / 提前返回时置 `false`
- 与 `IsPreviewBuilding` 分工：后者只管加载覆层显隐（保留快构建不闪 UX），前者管按钮门禁

**按钮接线**（Avalonia Button 有 Command 时 IsEnabled 由 CanExecute 决定，须走命令）：
- 压缩侧：`CanExecuteStartCompress()` 加 `&& !IsBuildPending`；`IsBuildPending` 状态变化处调用 `StartCompressCommand.NotifyCanExecuteChanged()`
- 解压侧：`ExtractCommand` 的 CanExecute 加 `&& !IsBuildPending`；状态变化处 `NotifyCanExecuteChanged()`

```
| 状态 | 加载覆层(IsPreviewBuilding) | 确定按钮(IsBuildPending) |
| 快速构建中 | 不闪 (false)             | 禁用 (true)            |
| 慢速构建中 | 显示 (true)              | 禁用 (true)            |
| 构建完成   | 隐藏 (false)             | 启用 (false)           |
```

### F. 解压侧 Bug 1 修复

| # | 文件 | 改动 |
|---|------|------|
| 6 | `UI.Avalonia/Services/ExtractFlow.cs` | 候选 A：`if (filteredKeys is { Count: > 0 })` → `if (filteredKeys != null)`。语义：null = 未启用过滤 → 全量（正确）；空列表 = 启用过滤但无匹配 → `ExtractEntriesAsync` 解压 0 个（引擎对空列表无害），**不再降级全量**。可选：无匹配时提示"无匹配文件" |
| 7 | `Core/Engines/ZipEngine.cs` | 候选 B：`ExtractEntriesAsync` 过滤匹配与 pathOverrides 查字典**两处**归一化：<br>① `entryKeys.Contains(e.Key)` → `entryKeys.Contains(ArchivePath.Normalize(e.Key))`<br>② `outputPathOverrides?.GetValueOrDefault(entryKey)` → `outputPathOverrides?.GetValueOrDefault(ArchivePath.Normalize(entryKey))`<br>（`ArchivePath.Normalize` 已存在于 `Core/Utils/ArchivePathExtensions.cs` = `Replace('\\','/')`） |

> 注：解压侧**不需要**新的 A/B 结构——`FilteredEntryKeys` 天然就是解压侧 B（过滤后条目清单），执行已消费它（`ExtractEntriesAsync(entryKeys)`）。缺的只是候选 A/B 两个 bug 修复。`SelectedItemsExtractService` 已消费 `ExtractPathResolver` 作为单一事实来源，本次**不**做重构。

### G. 范围边界：包内条目路径（Phase 2）

当前引擎 `CompressAsync` 内部自行决定**包内相对路径**（commonRoot 推导、PreserveDirectoryRoot、加密分支丢根等），预览 `BuildDirectoryNode` 也有一套。Phase 1 两者各自推导但公式相同（源目录名 + 相对路径），**不把 entryName 存进 B、不改引擎包内结构**——避免 SevenZipEngine 的 SharpSevenZip 库无法透传 entryName 映射（需临时目录 staging 复制大文件）的大改。

**范围**：Phase 1 接受"包内条目 = 同公式保证"。完整闭环（entryName 进 B + 引擎消费）另立 Phase 2 计划。

---

## 影响范围

### 文件清单（Phase 1）

| # | 文件 | 项目 | 改动类型 |
|---|------|------|---------|
| 1 | `src/MantisZip.Core/Abstractions/CompressPlan.cs` | Core | ➕ 新增（CompressPlanItem/CompressPlan） |
| 2 | `src/MantisZip.Core/Utils/CompressPathPlanner.cs` | Core | ➕ 新增 |
| 3 | `src/MantisZip.Core/Services/CompressService.cs` | Core | ✏️ `ComputeSeparateOutputPath` 改调 planner；Separate/Single 消费 B（白名单 + OutputArchivePath） |
| 4 | `src/MantisZip.Core/Utils/FileScanner.cs` | Core | ✏️ 加 whitelist 参数 |
| 5 | `src/MantisZip.Core/Engines/ZipEngine.cs` | Core | ✏️ 压缩透传 whitelist；解压 key 归一化（两处） |
| 6 | `src/MantisZip.Core/Engines/TarGzEngine.cs` | Core | ✏️ 压缩透传 whitelist |
| 7 | `src/MantisZip.Core/Engines/SevenZipEngine.cs` | Core | ✏️ 过滤激活（whitelist 非空）时改走展开路径 |
| 8 | `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | Core | ✏️ `ArchiveOptions` 加 `FileWhitelist` |
| 9 | `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs` | Avalonia | ✏️ `BuildCompressPreview` 返回 (tree, plan)；`ComputeArchiveName` 改调 planner |
| 10 | `src/MantisZip.UI.Avalonia/Services/CompressFlow.cs` | Avalonia | ✏️ `BuildRequest` 消费 B（不 ApplyFilter、不重算、KeepOriginalExtension 读 VM） |
| 11 | `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs` | Avalonia | ✏️ 缓存 B + `GetPlanForExecution()` + `IsBuildPending` 门禁 |
| 12 | `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs` | Avalonia | ✏️ `IsBuildPending` 门禁 |
| 13 | `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs` | Avalonia | ✏️ 空匹配不降级全量（一行判断） |
| 14 | `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs` | Avalonia | ✏️ 拷贝列表补 `KeepOriginalExtension`（输入同步） |
| 15 | `docs/PROGRESS.md` | docs | 提交前更新 |

### 不影响

- `SevenZipEngine.cs` 解压侧 — 不改（keySet 已归一化 ✅）
- `SelectedItemsExtractService.cs` — 不改（已消费 ExtractPathResolver）
- `ExtractPathResolver.cs` — 不改（解压侧已是单一事实来源）
- `FileConflictHelper.cs` — 不改（planner 消费它）
- WPF 项目（`MantisZip.UI`）— 维护模式，不动（规则 11）；Core 层改动对 WPF 透明（其压缩预览同样受益于 planner 修 Bug 2）

### CLI 影响面（全部共用同一服务，逐命令核实）

| CLI 命令 | 路径 | 影响 |
|---|---|---|
| `--extract`（弹窗+过滤） | `App.axaml.cs:990` → `ExtractFlow.ExtractAsync`（`i == 0 ? filteredEntryKeys : null`） | **受影响（修复）**：空匹配不再降级全量。过滤仅对第一个压缩包生效的语义保持不变；`filteredEntryKeys` 为 null（无过滤）时行为不变 |
| `--compress-separate` | `App.axaml.cs:1513` `RunCompressSeparate` 手工构建 request（**不经 BuildRequest**，硬编码 `KeepOriginalExtension=false`/`PreserveDirectoryRoot=true`）→ `CompressSeparateAsync` → `ComputeSeparateOutputPath` | **行为不变**：无 B、无 whitelist → 全量压缩（Bug 3 不显现——CLI 无预览树）；`ComputeSeparateOutputPath` 改调 planner 后保持逐字一致（Bug 2 语义对齐：目录源用完整目录名）。硬编码 `KeepOriginalExtension=false` 为既有行为，不在本次范围 |
| `--compress-combined` | `App.axaml.cs:1587` 输出路径自行计算（`FindCommonParent`）→ `CompressSingleAsync` | **无影响**：不走 planner / `ComputeSeparateOutputPath` |
| `--compress`（弹窗） | `App.axaml.cs:1397` → `CompressFlow.BuildRequest` | **修复**：`KeepOriginalExtension` 改读 VM 后，弹窗内用户勾选值才生效（此前读磁盘 settings） |
| `--compress-quick` 等直接模式 | 无预览构建 | **无影响** |

---

## 迁移步骤

### Step 1: 新增 CompressPlan + CompressPathPlanner（Core）

- `Core/Abstractions/CompressPlan.cs`：`CompressPlanItem` / `CompressPlan` records
- `Core/Utils/CompressPathPlanner.cs`：`ComputeArchiveName` / `ComputeOutputPath` / `PlanSeparate` / `PlanSingle`，逻辑从 `ResultPreviewService.ComputeArchiveName` 与 `CompressService.ComputeSeparateOutputPath` 逐字收敛（目录源完整名 + 文件源 keepOriginalExt 语义）

验证：`dotnet build src\MantisZip.Core\MantisZip.Core.csproj`

### Step 2: ArchiveOptions 加 FileWhitelist + FileScanner 白名单

- `ArchiveOptions.FileWhitelist`（`IReadOnlySet<string>?`）
- `FileScanner.CollectFiles` 加 `whitelist` 参数，目录枚举时跳过非白名单文件
- `ZipEngine` / `TarGzEngine` 压缩透传；`SevenZipEngine` 过滤激活（whitelist 非空）时改走展开路径

验证：`dotnet build src\MantisZip.Core\MantisZip.Core.csproj`

### Step 3: 预览构建双产物（B 从树派生）

- `ResultPreviewService.BuildCompressPreview` 返回 `(PreviewTreeNode, CompressPlan)`
- 先 `PlanSeparate/PlanSingle` 算 OutputArchivePath，再 `BuildDirectoryNode` 枚举时收集 `IncludedFiles`
- `ComputeArchiveName` 删除本地实现改调 planner
- `CompressSettingsViewModel.BuildCompressPreviewCoreAsync` 缓存 B

验证：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

### Step 4: 执行侧消费 B

- `CompressService.CompressSeparateAsync`：遍历 `plan.Items`（每源 `OutputArchivePath` + 白名单）
- `CompressService.CompressSingleAsync`：合并白名单
- `CompressFlow.BuildRequest`：`GetPlanForExecution()` → 构建 request（不 ApplyFilter、不重算、`KeepOriginalExtension` 读 VM）
- `MainWindow.ShowCompressSettingsDialog` 拷贝列表补 `KeepOriginalExtension`

验证：`dotnet build src\MantisZip.Core\MantisZip.Core.csproj && dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

### Step 5: 按钮门禁 IsBuildPending

**压缩侧**（`CompressSettingsViewModel`）：
- 新增 `[ObservableProperty] private bool _isBuildPending;`
- `BuildCompressPreviewCoreAsync`：`++_previewBuildVersion` 后置 `IsBuildPending = true`；所有 return 路径（无路径/路径无效/过期丢弃/catch/finally）置 `false`
- `CanExecuteStartCompress()`：加 `&& !IsBuildPending`
- `IsBuildPending` 变化处（含 setter）调用 `StartCompressCommand.NotifyCanExecuteChanged()`

**解压侧**（`ExtractSettingsViewModel`）：同构改造，`ExtractCommand` 的 CanExecute 加 `&& !IsBuildPending`

验证：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

### Step 6: 解压 Bug 1 修复

- `ExtractFlow.ExtractAsync`：`if (filteredKeys is { Count: > 0 })` → `if (filteredKeys != null)`（空列表 → 解压 0 个，不降级全量）
- `ZipEngine.ExtractEntriesAsync`：过滤匹配 + pathOverrides 查字典**两处**归一化（`ArchivePath.Normalize`）

验证：`dotnet build src\MantisZip.Core\MantisZip.Core.csproj && dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

### Step 7: 功能验证

> 实施批次已完成**静态链路审计**（全解决方案 0 错误 + 253/253 测试通过），下列交互项需人工运行 GUI 确认：

- [ ] 压缩 Separate：目录名含点（`project.v2`）→ 预览 `project.v2.zip`，实际生成 `project.v2.zip`（已静态核实：`CompressPathPlanner` 目录源完整目录名语义唯一实现）
- [ ] 压缩 Separate：KeepOriginalExtension 勾选/取消 → 预览与实际同步变化（已静态核实：`MainWindow` 拷贝块补 `cvm.KeepOriginalExtension` + `BuildRequest` 读 VM）
- [ ] 压缩过滤（10 目录/1 万文件/过滤剩 1000）：
  - [ ] 预览树：10 个压缩包节点（B 视图只显示匹配文件）
  - [ ] 实际压缩：**10 个压缩包**（不是 1000 个！），每包只含匹配文件（已静态核实：`BuildRequest` 保留目录粒度 + `FileWhitelist` 引擎过滤）
  - [ ] Manual/Combined：1 个包，只含匹配文件（已静态核实：`CompressSingleAsync` 合并白名单）
  - [ ] A/B 切换：ShowFilteredGhosts 开关显示全部/只显示匹配（复用既有显示开关，未改动）
- [ ] 解压过滤：设置"只解压 .jpg"且包内无 .jpg → 提示无匹配，**不**全量解压（已静态核实：`ExtractFlow` `filteredKeys != null` + 版本号守卫）
- [ ] 解压过滤：正常匹配 → 只解压匹配项，路径与预览一致（已静态核实：`ZipEngine` 三处 `ArchivePath.Normalize` + `ExtractPathResolver` 单一事实来源）
- [ ] 解压过滤：压缩包内 `\` 分隔路径（可用 7-Zip 创建验证）→ 过滤仍生效（已静态核实：ZipEngine 归一化已补，TarGz/SevenZip 原已归一化）
- [ ] 解压选中项（右键）→ 行为不变（回归）（已静态核实：`SelectedItemsExtractService` 路径未动）
- [ ] 快速构建（<250ms）期间确定按钮禁用、加载覆层不闪（已静态核实：`IsBuildPending` 入口置位 + 版本守卫 finally 清除）
- [ ] 慢速构建（大目录）期间确定按钮禁用、加载覆层显示
- [ ] 预览树节点路径与实际生成路径逐项一致（包内条目 = 同公式保证，Phase 2 闭环）

### Step 8: 更新文档

- `docs/PROGRESS.md` 追加条目（Avalonia 版 + 共享层双线索）
- 本计划文件状态改为 ✅

---

## 工作量估算

| Step | 内容 | 预计时间 |
|------|------|---------|
| 1 | CompressPlan + CompressPathPlanner 新增 | 35 min |
| 2 | ArchiveOptions + FileScanner 白名单 + 三引擎 | 50 min |
| 3 | 预览构建双产物（B 从树派生） | 45 min |
| 4 | 执行侧消费 B + BuildRequest | 40 min |
| 5 | 按钮门禁 IsBuildPending（两侧） | 40 min |
| 6 | 解压 Bug 1 修复 | 20 min |
| 7 | 功能验证 | 30 min |
| 8 | 文档更新 | 10 min |
| | **合计** | **~4.5 h** |

---

## TODOs

- [x] **1. CompressPlan + CompressPathPlanner（Core）**
  - [x] 1.1 新增 `Core/Abstractions/CompressPlan.cs`（CompressPlanItem/CompressPlan）
  - [x] 1.2 新增 `Core/Utils/CompressPathPlanner.cs`（ComputeArchiveName/ComputeOutputPath/PlanSeparate/PlanSingle）
  - [x] 1.3 Core 编译验证

- [x] **2. 白名单支持**
  - [x] 2.1 `ArchiveOptions.FileWhitelist`
  - [x] 2.2 `FileScanner.CollectFiles` 加 whitelist
  - [x] 2.3 `ZipEngine`/`TarGzEngine` 透传；`SevenZipEngine` 展开路径
  - [x] 2.4 Core 编译验证

- [x] **3. 预览构建双产物**
  - [x] 3.1 `BuildCompressPreview` 返回 (tree, plan)
  - [x] 3.2 B 从树派生（一次枚举收集 IncludedFiles）
  - [x] 3.3 `ComputeArchiveName` 改调 planner
  - [x] 3.4 VM 缓存 B
  - [x] 3.5 编译验证

- [x] **4. 执行侧消费 B**
  - [x] 4.1 `CompressSeparateAsync`/`CompressSingleAsync` 消费 plan
  - [x] 4.2 `BuildRequest` 用 `GetPlanForExecution()`（不 ApplyFilter、不重算）
  - [x] 4.3 `KeepOriginalExtension` 读 VM + 回传
  - [x] 4.4 编译验证

- [x] **5. 按钮门禁 IsBuildPending**
  - [x] 5.1 压缩侧：`IsBuildPending` + CanExecute 门禁 + NotifyCanExecuteChanged
  - [x] 5.2 解压侧：同构改造
  - [x] 5.3 编译验证

- [x] **6. 解压 Bug 1 修复**
  - [x] 6.1 `ExtractFlow` 空匹配不降级（`!= null`）
  - [x] 6.2 `ZipEngine` key 归一化三处（totalBytes/filteredEntries/outputPathOverrides）
  - [x] 6.3 编译验证
  - [x] 6.4 附加：`AddToArchiveAsync`（Zip/SevenZip）按 `FileWhitelist` 过滤（堵「添加至已有压缩包」路径的预览≠实际漏洞）

- [ ] **7. 功能验证**（见 Step 7 清单，静态审计已过、交互项待用户人工确认）

- [x] **8. 文档更新**
  - [x] 8.1 `docs/PROGRESS.md` 追加条目
  - [x] 8.2 本计划状态置 ✅
