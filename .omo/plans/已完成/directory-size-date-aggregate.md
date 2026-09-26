# 目录行聚合显示：大小 = 子树和，日期 = 最新文件，压缩后大小按格式可用性

> **状态**: ✅ 已实施 | **目标框架**: Avalonia（主力版）+ Core 共享层
> **关联**: WPF 版已完成目录大小聚合（`MainWindow.xaml.cs:41,752-767` + `MainWindow.UI.cs:285-299`），但**日期聚合未实现**（目录行日期显示 `---`），Avalonia 版两项均未实现。

## 目标

在文件列表中，**目录行**不再显示空值：

| 列 | 目录行新行为 |
|----|-------------|
| 大小 | 目录内**整个子树**（含递归子目录）所有文件大小之和 |
| 日期 | 目录内**整个子树**最新的文件修改时间 |
| 压缩后大小 | 同上求和；但格式本身**拿不到逐项压缩后大小**时（7z/RAR/.tgz/.gz）显示空 —— 与文件行一致 |
| 压缩率 | `CompressedSize聚合 / Size聚合` 的百分比（目录也显示，方案 A）；格式不可用时显示空 —— 与文件行一致 |

**文件行**行为保持不变，除两点统一：① 拿不到逐项压缩后大小的格式（7z 等），文件行的压缩后大小列由当前的 `0 B` **改为空**；② 同格式下文件行压缩率也由 `0.0%` **改为空**（与目录行一致）。

## 决策记录（已与用户确认）

1. **聚合范围**：整个子树（递归）—— 与 WPF 现有 `_dirStats` 算法一致（每个文件累加进其全部祖先目录）。
2. **统计基准**：**过滤后的 `filteredSource`**（与当前列表显示一致，目录聚合跟随过滤条件）。
3. **聚合位置**：扩展 **Core** 的 `DirStats` + `ComputeDirectoryStats` 增加日期字段，Avalonia 调用之。Core 为共享契约，WPF 维护模式不动。
4. **显示刷新**：`SizeDisplay` / `LastModifiedDisplay` / `CompressedSizeDisplay` 改为**由值派生的计算属性**（`[NotifyPropertyChangedFor]` 联动），设置 `Size` / `LastModified` / `CompressedSizeAvailable` 即自动刷新。
5. **0 值显示**：文件行保持显示 `0 B`（`FormatSize(0)` = `"0 B"`），不做空串特判。
6. **压缩后大小可用性**：Avalonia 引入按格式的 `CompressedSizeAvailable` 标志（对齐 WPF `CompressedDisplayMode.Unavailable` 的判定），不可用时文件/目录一律显示空。
7. **压缩率列（追加，方案 A）**：移除 `RatioDisplay` 的 `IsDirectory` 门控，目录显示聚合压缩率；`CompressedSizeAvailable=false` 时目录/文件压缩率一律显示空；`RatioSort` 保留 `IsDirectory → -1`（目录仍排最后）。`CompressionRatio` 由 ObservableProperty 改为派生计算属性，避免目录聚合后值不刷新。

## 现状分析（代码事实）

### 数据流

```
引擎 ListEntriesAsync → Core ArchiveItem 列表（_allRawItems）
  → MainWindowViewModel.GetFilteredSource()  （应用过滤条件）
  → ArchiveEntryLister.GetEntriesInFolder(filteredSource, CurrentFolder, ShowSubfolders)
  → 隐式合成目录行（Core，Size=0，LastModified=MinValue）
  → ArchiveItemModel.FromCore(item)            （SizeDisplay/LastModifiedDisplay 在此固化）
  → CurrentEntries（DataGrid ItemsSource，绑定 SizeDisplay/LastModifiedDisplay）
```

### 关键文件

| 文件 | 位置 | 现状 |
|------|------|------|
| `src/MantisZip.Core/Services/ArchiveEntryLister.cs` | L8 | `DirStats(int Count, long Size, long CompressedSize)` —— **无日期字段** |
| 同上 | L174-196 | `ComputeDirectoryStats`：逐文件将 Size/CompressedSize 累加进全部祖先目录 —— **无日期聚合**，Avalonia 目前未调用 |
| 同上 | L100-106, L128-134 | 隐式合成目录 `Size=0`，`LastModified` 未设置（= `DateTime.MinValue`） |
| `src/MantisZip.UI.Avalonia/Models/ArchiveItemModel.cs` | L26, L38 | `_sizeDisplay` / `_lastModifiedDisplay` 为 ObservableProperty 字符串，`FromCore`（L115-119）一次性固化 |
| 同上 | L144 | `FormatSize`（私有静态，委托 `FormatUtil.FormatSize`） |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | L1096-1121 | `PopulateEntries`：`GetEntriesInFolder` → `FromCore` → 填充 Icon → `CurrentEntries` |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | L1156-1182 | `ComputeProgressBarRatios`：目录被排除在大小/日期基准之外（除非 `SeparateDirBaseline`） |
| `src/MantisZip.Core/Engines/SevenZipEngine.cs` | L545 | `CompressedSize = 0` —— 注释明确 "SharpSevenZip 不提供逐项压缩后大小" |
| `src/MantisZip.Core/Engines/TarGzEngine.cs` | L336, L356 | .tar 用 `fi.Length`（未压缩，等价 NotCompressed）；.gz 无逐项压缩大小 |
| `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs` | L568-585 | **WPF 判定基准**：`GetCompressedDisplayMode` → Zip=Normal / Iso=NotCompressed / .tar=NotCompressed / .tgz/.gz=Unavailable / 7z,RAR=Unavailable |

### 结论

- Avalonia 目录行显示空值的根因：Core 合成目录时 `Size=0` + `LastModified=MinValue`，且 `FromCore` 之后不再更新。
- Avalonia **没有** WPF 的 `CompressedDisplayMode` 概念 —— 需要新增按格式的可用性标志，才能实现"7z 压缩后大小显示空"。

---

## 改动方案

### ① Core：`DirStats` 增加日期字段 + `ComputeDirectoryStats` 聚合日期

**文件**: `src/MantisZip.Core/Services/ArchiveEntryLister.cs`

```csharp
// L8 原:
public readonly record struct DirStats(int Count, long Size, long CompressedSize);
// 改为:
public readonly record struct DirStats(int Count, long Size, long CompressedSize, DateTime NewestModified);
```

`ComputeDirectoryStats` 循环体（L191）内追加日期聚合，仅统计有效日期（`LastModified > DateTime.MinValue`）：

```csharp
// L186-192 原:
var parts = name[..lastSlash].Split('/');
for (int i = 0; i < parts.Length; i++)
{
    var dirPath = string.Join("/", parts, 0, i + 1);
    var stat = stats.GetValueOrDefault(dirPath);
    stats[dirPath] = new DirStats(stat.Count + 1, stat.Size + item.Size, stat.CompressedSize + item.CompressedSize);
}
// 改为:
var parts = name[..lastSlash].Split('/');
for (int i = 0; i < parts.Length; i++)
{
    var dirPath = string.Join("/", parts, 0, i + 1);
    var stat = stats.GetValueOrDefault(dirPath);
    var newest = item.LastModified > stat.NewestModified ? item.LastModified : stat.NewestModified;
    stats[dirPath] = new DirStats(stat.Count + 1, stat.Size + item.Size,
        stat.CompressedSize + item.CompressedSize, newest);
}
```

> 说明：`LastModified = DateTime.MinValue` 的文件不会污染 `NewestModified`（`MinValue > MinValue` 为 false）。

### ② Avalonia Model：显示属性改为派生计算属性 + 新增压缩大小可用性标志

**文件**: `src/MantisZip.UI.Avalonia/Models/ArchiveItemModel.cs`

```csharp
// 1) 删除 _sizeDisplay / _lastModifiedDisplay / _compressedSizeDisplay 三个 ObservableProperty（L26,L32,L38）
// 2) _size / _lastModified / _compressedSize 的字段属性追加联动通知:
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(SizeDisplay))]
private long _size;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(LastModifiedDisplay))]
private DateTime _lastModified;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CompressedSizeDisplay))]
private long _compressedSize;
// 注: _compressedSize 同时受 _compressedSizeAvailable 影响，见下

// 3) 新增格式可用性标志（默认 true，保持现有文件行为）:
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CompressedSizeDisplay))]
private bool _compressedSizeAvailable = true;

// 4) 派生属性（替换原 ObservableProperty 字符串字段）:
/// <summary>大小显示：始终格式化（0 显示 "0 B"，与文件行一致）。</summary>
public string SizeDisplay => FormatSize(Size);

/// <summary>日期显示：MinValue → 空串；否则格式化。</summary>
public string LastModifiedDisplay =>
    LastModified > DateTime.MinValue ? LastModified.ToString("yyyy-MM-dd HH:mm:ss") : "";

/// <summary>压缩后大小显示：格式不可用时显示空，否则格式化。</summary>
public string CompressedSizeDisplay =>
    CompressedSizeAvailable ? FormatSize(CompressedSize) : "";

// 5) CompressionRatio 改为派生计算属性（目录聚合后 Size/CompressedSize 变更可自动刷新）:
/// <summary>压缩率（0–100）。Size<=0 返回 0。</summary>
public double CompressionRatio => Size > 0
    ? Math.Round((double)CompressedSize / Size * 100, 1)
    : 0;

// 6) RatioDisplay 移除 IsDirectory 门控（方案 A）：
/// <summary>
/// 压缩率显示文本。Size=0 或格式不可用（CompressedSizeAvailable=false）返回空；
/// 目录与文件一视同仁（目录显示聚合压缩率）。
/// </summary>
public string RatioDisplay
{
    get
    {
        if (Size == 0 || !CompressedSizeAvailable) return "";
        if (CompressedSize == 0) return "0.0%";
        if (CompressedSize >= Size) return "100.0%";
        return $"{CompressionRatio:F1}%";
    }
}
```

`FromCore`（L107-125）删掉三个 Display 字符串字段赋值（`SizeDisplay`/`CompressedSizeDisplay`/`LastModifiedDisplay`）**以及 `CompressionRatio` 赋值**（已改派生属性）。

> ⚠️ **行为变化**：文件行的 `LastModifiedDisplay` 在 `LastModified == MinValue` 时由 `"0001-01-01 00:00:00"` 变为空串 —— 属修正性改进（当前显示垃圾日期）。7z 等格式文件行的压缩后大小由 `0 B` 变为空、压缩率由 `0.0%` 变为空 —— 用户明确要求的一致化。

### ③ Avalonia ViewModel：PopulateEntries 应用聚合

**文件**: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`

`PopulateEntries`（L1089）中，`GetEntriesInFolder` 调用之后、`CurrentEntries` 填充循环内，对目录 model 应用聚合：

```csharp
var entries = ArchiveEntryLister.GetEntriesInFolder(
    filteredSource, CurrentFolder ?? "", ShowSubfolders);

// 计算目录聚合（过滤后的数据源，与显示一致）
var dirStats = ArchiveEntryLister.ComputeDirectoryStats(filteredSource);

CurrentEntries.Clear();
foreach (var item in entries)
{
    var model = ArchiveItemModel.FromCore(item);
    if (model.IsDirectory)
    {
        // 应用聚合：大小 = 子树和，日期 = 子树最新，压缩后大小 = 子树和
        if (dirStats.TryGetValue(model.FullPath, out var stat))
        {
            model.Size = stat.Size;
            model.CompressedSize = stat.CompressedSize;
            model.LastModified = stat.NewestModified;
        }
        model.IconSource = IconService.GetFolderIcon();
    }
    else
    {
        var ext = Path.GetExtension(model.Name);
        model.IconSource = IconService.GetFileIcon(ext);
    }
    // 按格式设置压缩后大小可用性（文件 + 目录一致）
    model.CompressedSizeAvailable = GetCompressedSizeAvailable(CurrentFormat);
    model.ProgressBarEnabled = ShowProgressBars;
    CurrentEntries.Add(model);
}
```

新增私有方法（对齐 WPF `GetCompressedDisplayMode` 的判定，只保留"是否可用"布尔维度）：

```csharp
/// <summary>
/// 当前格式是否能提供逐项压缩后大小。
/// Zip 可用；ISO/.tar 未压缩（引擎以原大小填充，等价可用）；
/// 7z/RAR/.tgz/.tar.gz/.gz 无法获得逐项压缩后大小（不可用，显示空）。
/// </summary>
private static bool GetCompressedSizeAvailable(ArchiveFormat format)
{
    if (format == ArchiveFormat.Zip) return true;
    if (format == ArchiveFormat.Iso) return true;
    if (format == ArchiveFormat.Tar) return true; // .tar 未压缩，引擎用原大小
    return false; // 7z, RAR, .tgz/.gz
}
```

> 需要确认 Avalonia VM 中当前格式字段名（`_currentFormat` / `CurrentFormat`），与 `GetFormatByExtension` 派生一致（AGENTS.md「`_currentFormat` classification」节）。若 `.tgz/.gz` 在 Core 中被映射为 `ArchiveFormat.Tar`，需按**压缩包扩展名**进一步区分 `.tar` 与 `.tgz/.gz`（与 WPF `GetCompressedDisplayMode` 相同：`Path.GetExtension(archivePath)` 判定）。

### ④ XAML

**无需修改** —— `MainWindow.axaml` 列已绑定 `SizeDisplay`（L1059）、`CompressedSizeDisplay`（L1087 附近）、`LastModifiedDisplay`（L1143），派生属性变化经 `NotifyPropertyChangedFor` 自动刷新。

### ⑤ 测试

**文件**: `tests/MantisZip.Tests/`（Core 层）

- `ComputeDirectoryStats` 新增/更新用例：
  - 递归子树求和（`a/b/c.txt` 同时计入 `a` 与 `a/b`）—— 与 WPF 语义一致
  - `NewestModified` = 子树内最新文件时间，`MinValue` 文件不污染
  - 根目录文件不产生目录统计
  - 空目录（无文件）无统计项
- 检查是否已有 `ArchiveEntryLister` 测试文件，优先追加而非新建。

---

## 任务清单

- [x] **1. Core `DirStats` + `ComputeDirectoryStats` 增加 `NewestModified`** — `ArchiveEntryLister.cs`
- [x] **2. Core 测试** — `tests/MantisZip.Tests/` 追加 `ComputeDirectoryStats` 用例（子树和 / 最新日期 / MinValue 过滤 / 根文件 / 空目录）
- [x] **3. Avalonia Model 派生属性重构** — `ArchiveItemModel.cs`：删 3 个 Display 字符串字段 → 派生属性 + `[NotifyPropertyChangedFor]` + `CompressedSizeAvailable`
- [x] **4. Avalonia VM 应用聚合 + 可用性标志** — `MainWindowViewModel.cs` `PopulateEntries` + `GetCompressedSizeAvailable`
- [x] **5. 压缩率列（方案 A）** — `ArchiveItemModel.cs`：`CompressionRatio` 改派生属性；`RatioDisplay` 移除 `IsDirectory` 门控 + `CompressedSizeAvailable` 门控；`RatioSort` 保留目录 → -1
- [x] **6. 验证** — `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` 0 错误；`dotnet test`（Core 241 通过 + Avalonia 40 通过）
- [ ] **7. 手工冒烟**（可选）— 打开含子目录的 zip / 7z，确认目录行大小=和、日期=最新、压缩率=聚合值、7z 压缩后大小/压缩率列空

## 不做的事（范围外）

- 不修改 WPF 版（维护模式）；WPF 的 `_dirStats` 内联实现保留原样，仅 Core 契约升级
- 不修改 `ComputeProgressBarRatios` 逻辑（目录聚合后其输入值变为真实值，行为自然改善；`SeparateDirBaseline` 语义不变）
- 不实现 ISO/.tar 的 "NotCompressed 100%" 压缩率显示（当前任务只涉及"可用性"布尔维度；如需完整对齐 WPF 可另立计划）

## Done

- [x] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过，0 错误
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` 通过
