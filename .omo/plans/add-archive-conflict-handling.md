
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `AddToArchiveAsync`（添加到压缩包）增加重名条目冲突处理，复用现有解压冲突的 `FileConflictAction` 全动作集（覆盖/跳过/重命名/覆盖旧文件/覆盖小文件/Ask 弹窗）与 `ConflictDialog` 弹窗。

**Architecture:** Core 层新增 `AddConflictHelper`（条目名级冲突解析，镜像 `FileConflictHelper.ResolvePathAsync` 结构但语义方向相反）；`ZipEngine.AddToArchiveAsync` 在 copy-mode 用 `keepEntryNames` 排除被覆盖条目 + `addEntries` 应用最终名，legacy 路径 Phase 2 应用解析结果；`SevenZipEngine.AddToArchiveAsync` 用 `SharpSevenZipExtractor.ArchiveFileData` 收集现有条目 → 覆盖条目经 `ModifyArchive`(index→null) 删除 → `CompressFileDictionary` 追加。Avalonia 侧 `MainWindowViewModel.AddFiles` 复用 `SelectedItemsExtractService.CreateExtractOptions` 的 Ask+ApplyToAll 记忆闭包，弹窗复用 `ExtractFlow.ShowConflictDialogAsync`（加 titleKey 参数区分「添加冲突」标题）。

**Tech Stack:** .NET 9, SharpCompress 0.48.1 (ZIP), SharpSevenZip 2.0.45 (7z), xUnit, Avalonia + CommunityToolkit.Mvvm

---

## 语义映射（关键设计决策，勿偏离）

解压场景 `FileConflictInfo` 与添加场景**方向相反**，`AddConflictHelper` 必须使用自己的比较逻辑，**不能**直接调用 `FileConflictHelper.ShouldOverwriteByTime/Size`（它们是 private 且方向相反）：

| 场景 | Entry（压缩包条目） | Existing（磁盘文件） | OverwriteIfOlder 覆盖条件 | OverwriteIfSmaller 覆盖条件 |
|------|--------------------|--------------------|--------------------------|----------------------------|
| 解压 | **新数据**（要写入） | **旧数据**（已存在） | 条目比磁盘新 → 覆盖 | 条目比磁盘大 → 覆盖 |
| 添加 | **旧数据**（已存在） | **新数据**（要写入） | 磁盘新文件比条目新 → 覆盖 | 磁盘新文件比条目大 → 覆盖 |

统一规则：**新数据比旧数据更新/更大 → 覆盖**。添加场景下新数据在 `Existing*` 侧（磁盘文件）。

`FileConflictInfo` 字段映射（添加场景）：
- `FilePath` = 目标条目名（含 entryBasePath 前缀，如 `docs/hello.txt`）
- `EntrySize`/`EntryModified` = 压缩包内已有同名条目的大小/修改时间
- `ExistingSize`/`ExistingModified` = 磁盘新文件的大小/修改时间
- `SuggestedName` = 自动唯一条目名的文件名部分（对话框预填）
- `CustomName` = 用户自定义文件名（仅文件名部分，合成时保留目录前缀）

**弹窗列显示天然正确**：ConflictDialog 的「磁盘文件」(Existing*) 显示新文件、「压缩包文件」(Entry*) 显示已有条目，无需改动对话框内容，仅标题需区分（新 key `AddConflict_Title`）。

---

## 文件结构

| 文件 | 责任 | 动作 |
|------|------|------|
| `src/MantisZip.Core/Utils/AddConflictHelper.cs` | 条目名级冲突解析（唯一新文件） | Create |
| `tests/MantisZip.Tests/Utils/AddConflictHelperTests.cs` | AddConflictHelper 纯单元测试 | Create |
| `src/MantisZip.Core/Engines/ZipEngine.cs` | `AddToArchiveAsync` 接线：旧条目收集→解析→copy-mode keepEntryNames/legacy Phase 2 | Modify (:830-1247) |
| `src/MantisZip.Core/Engines/SevenZipEngine.cs` | `AddToArchiveAsync` 接线：现有条目收集→解析→ModifyArchive 删除→Append | Modify (:960-1036) |
| `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` | ZIP 冲突集成测试 + 删除探针 | Modify |
| `tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs` | 7z 冲突集成测试 + 删除 4 个探针 | Modify |
| `src/MantisZip.UI.Avalonia/Dialogs/ConflictDialog.axaml.cs` | ctor 加 `titleKey` 参数，`WinTitle` 读字段 | Modify (:37, :56-111) |
| `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs` | `ShowConflictDialogAsync` 加 `string titleKey` 参数 | Modify (:74-122) |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 新委托 `ShowAddFileConflictDialogAsync`；`AddFiles` 构建冲突 options | Modify (:92 附近, :2364-2393) |
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs` | 接线 `ShowAddFileConflictDialogAsync` | Modify (:305 附近) |
| `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` | 新 key `AddConflict_Title` | Modify |
| `src/MantisZip.UI.Avalonia/Localization/strings.en.json` | 新 key `AddConflict_Title` | Modify |
| `docs/PROGRESS.md` | 三轨制追加条目（提交前，规则 3） | Modify |

**范围边界**：仅改 Avalonia（规则 11）+ Core 共享层。WPF `MainWindow.DragDrop.cs` 虽调用 `AddToArchiveAsync` 但不传冲突 options（保持默认 Overwrite = 现有行为），不修改。`CompressService` 调用同样保持默认。TarGz `AddToArchiveAsync` 抛 `NotSupportedException`（无添加能力），不涉及。

---

## Task 1: 清理探针测试 + 提交 entryBasePath 前置修复

**背景**：工作区已有未提交的 entryBasePath 修复（`SevenZipEngine.cs` +49、`MainWindowViewModel.cs` +4、`SevenZipEngineTests.cs` +61、`ZipEngineTests.cs` 探针、`MantisZip.Tests.csproj` +SharpSevenZip）。其中 5 个探针是**故意失败**的诊断测试（`Assert.True(false, ...)`），必须先删除再提交。

**Files:**
- Modify: `tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs`
- Modify: `tests/MantisZip.Tests/Engines/ZipEngineTests.cs`

- [ ] **Step 1: 删除 7z 探针（4 个）**

删除 `tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs` 中以下方法的**完整方法体**（含方法上的 `// ===== ... =====` 注释块）：

1. `Probe_AddToArchive_DuplicateName_Behavior`（~:243-272）
2. `Probe_ModifyArchive_ReplaceSemantics`（~:274-338）
3. `Probe_ModifyArchive_DeleteSemantics`（~:340-375）
4. `Probe_OverwriteViaDeleteThenAppend`（~:377-425）

保留 `AddToArchiveAsync_RespectsEntryBasePath`（~:220-241 的真实测试）。

- [ ] **Step 2: 删除 ZIP 探针（1 个）**

删除 `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` 中 `Probe_AddToArchive_DuplicateName_CopyMode`（~:246-273）的完整方法体及注释块。

- [ ] **Step 3: 运行 Core 测试验证探针删除后全绿**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true
```
Expected: 全部通过（探针不再失败；entryBasePath 相关新测试通过）。

- [ ] **Step 4: 更新 PROGRESS.md（规则 3，提交前）**

在 `docs/PROGRESS.md` 的 **共享层（Core / ShellExt / 构建）** 线索顶部添加：

```markdown
#### v0.5.0 (2026-08-18)
- 添加文件到压缩包时保留浏览目录前缀（entryBasePath）：7z 改用 `CompressFileDictionary` 精确控制条目名；ZIP 传入 entryBasePath
```

在 **MantisZip.UI.Avalonia（主力版）** 线索顶部添加：

```markdown
**2026-08-18** — 添加文件到压缩包时保留浏览目录前缀（entryBasePath），7z 改用 `CompressFileDictionary` 精确控制条目名
```

- [ ] **Step 5: 提交前置修复**

```powershell
git add src/MantisZip.Core/Engines/SevenZipEngine.cs src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs tests/MantisZip.Tests/Engines/ZipEngineTests.cs tests/MantisZip.Tests/MantisZip.Tests.csproj docs/PROGRESS.md
git commit -m "feat(core,avalonia): 添加文件到压缩包时保留浏览目录前缀（entryBasePath）"
```

---

## Task 2: AddConflictHelper（Core 层）+ 单元测试（TDD）

**Files:**
- Create: `src/MantisZip.Core/Utils/AddConflictHelper.cs`
- Create: `tests/MantisZip.Tests/Utils/AddConflictHelperTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `tests/MantisZip.Tests/Utils/AddConflictHelperTests.cs`：

```csharp
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.Tests.Utils;

public class AddConflictHelperTests
{
    private static HashSet<string> Occupied(params string[] names) =>
        new(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task ResolveEntryNameAsync_NoConflict_ReturnsSameName()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "new.txt", null, null, null, DateTime.Now, 100, occupied);
        Assert.Equal("new.txt", result);
        Assert.Contains("new.txt", occupied); // 最终名加入已占用集合
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Overwrite_ReturnsSameName()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Skip_ReturnsNull()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Skip },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Rename_ReturnsUniqueName()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Rename },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello (1).txt", result);
        Assert.Contains("hello (1).txt", occupied);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Rename_PreservesDirectoryPrefix()
    {
        var occupied = Occupied("docs/hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "docs/hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Rename },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("docs/hello (1).txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfOlder_NewerDiskWins()
    {
        var occupied = Occupied("hello.txt");
        // 磁盘新文件比条目新 → 覆盖（添加场景方向）
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfOlder },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfOlder_OlderDiskSkipped()
    {
        var occupied = Occupied("hello.txt");
        // 磁盘新文件比条目旧 → 跳过
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfOlder },
            DateTime.Now, 10, DateTime.Now.AddDays(-1), 20, occupied);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfSmaller_LargerDiskWins()
    {
        var occupied = Occupied("hello.txt");
        // 磁盘新文件更大 → 覆盖（"覆盖较小"：大文件覆盖小条目）
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfSmaller },
            DateTime.Now, 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfSmaller_SmallerDiskSkipped()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfSmaller },
            DateTime.Now, 10, DateTime.Now, 5, occupied);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Ask_ResolverReturnsRenameWithCustomName()
    {
        var occupied = Occupied("hello.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "renamed.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("renamed.txt", result);
        Assert.Contains("renamed.txt", occupied);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Ask_CustomNamePreservesDirectoryPrefix()
    {
        var occupied = Occupied("docs/hello.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "renamed.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "docs/hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("docs/renamed.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Ask_ResolverReturnsSkip()
    {
        var occupied = Occupied("hello.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => Task.FromResult(FileConflictAction.Skip),
        };
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Null(result);
    }

    [Fact]
    public void GetUniqueEntryName_TarGz_DoubleExtension()
    {
        var occupied = Occupied("docs/archive.tar.gz");
        Assert.Equal("docs/archive (1).tar.gz", AddConflictHelper.GetUniqueEntryName("docs/archive.tar.gz", occupied));
    }

    [Fact]
    public void GetUniqueEntryName_Sequential()
    {
        var occupied = Occupied("file.txt", "file (1).txt");
        Assert.Equal("file (2).txt", AddConflictHelper.GetUniqueEntryName("file.txt", occupied));
    }
}
```

- [ ] **Step 2: 运行测试验证失败**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true --filter "FullyQualifiedName~AddConflictHelperTests"
```
Expected: FAIL — `AddConflictHelper` 不存在（编译错误）。

- [ ] **Step 3: 实现 AddConflictHelper**

创建 `src/MantisZip.Core/Utils/AddConflictHelper.cs`：

```csharp
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 添加到压缩包场景的条目名冲突处理（与解压场景共用 <see cref="FileConflictAction"/> 与
/// <see cref="ArchiveOptions.ConflictResolver"/> / <see cref="ArchiveOptions.ConflictResolverAsync"/>）。
///
/// 语义映射（与解压相反，勿直接用 <see cref="FileConflictHelper"/> 的方向比较）：
/// - 解压：Entry = 压缩包条目（新数据），Existing = 磁盘文件（旧数据）；
/// - 添加：Entry = 压缩包已有条目（旧数据），Existing = 磁盘新文件（新数据）。
/// 统一规则：新数据比旧数据更新/更大 → 覆盖（OverwriteIfOlder / OverwriteIfSmaller 比较方向与解压相反）。
/// </summary>
public static class AddConflictHelper
{
    /// <summary>
    /// 异步解析条目名冲突。返回最终条目名；null = 跳过该文件。
    /// </summary>
    /// <param name="entryName">提议的条目名（含 entryBasePath 前缀，"/" 分隔）。</param>
    /// <param name="options">压缩选项；ConflictAction 为 Ask 时优先调用 ConflictResolverAsync。</param>
    /// <param name="entryModified">压缩包内已有同名条目的修改时间；无同名条目时传 null。</param>
    /// <param name="entrySize">压缩包内已有同名条目的大小；无同名条目时传 null。</param>
    /// <param name="newFileModified">磁盘新文件的修改时间。</param>
    /// <param name="newFileSize">磁盘新文件的大小。</param>
    /// <param name="occupiedNames">已占用条目名集合（现有条目 + 本批次已解析条目），OrdinalIgnoreCase；最终名会加入此集合。</param>
    public static async Task<string?> ResolveEntryNameAsync(
        string entryName,
        ArchiveOptions? options,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        // 无冲突：条目名不存在 → 直接添加（即使策略是 Skip 也不跳过，无同名条目可跳）
        if (!occupiedNames.Contains(entryName))
        {
            occupiedNames.Add(entryName);
            return entryName;
        }

        var action = options?.ConflictAction ?? FileConflictAction.Overwrite;

        // Ask → 优先异步回调（UI 对话框场景），其次退回到同步回调
        if (action == FileConflictAction.Ask && options != null)
        {
            if (options.ConflictResolverAsync != null)
            {
                var info = BuildConflictInfo(entryName, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
                action = await options.ConflictResolverAsync(info);

                if (action == FileConflictAction.Rename && !string.IsNullOrWhiteSpace(info.CustomName))
                {
                    var final = CombineCustomName(entryName, info.CustomName);
                    occupiedNames.Add(final);
                    return final;
                }
            }
            else if (options.ConflictResolver != null)
            {
                return ResolveEntryName(entryName, options, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
            }
        }

        return ResolveByAction(entryName, action, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
    }

    /// <summary>同步版，供 <see cref="ArchiveOptions.ConflictResolver"/> 回调路径使用。</summary>
    public static string? ResolveEntryName(
        string entryName,
        ArchiveOptions? options,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        if (!occupiedNames.Contains(entryName))
        {
            occupiedNames.Add(entryName);
            return entryName;
        }

        var action = options?.ConflictAction ?? FileConflictAction.Overwrite;

        if (action == FileConflictAction.Ask && options?.ConflictResolver != null)
        {
            var info = BuildConflictInfo(entryName, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
            action = options.ConflictResolver(info);

            if (action == FileConflictAction.Rename && !string.IsNullOrWhiteSpace(info.CustomName))
            {
                var final = CombineCustomName(entryName, info.CustomName);
                occupiedNames.Add(final);
                return final;
            }
        }

        return ResolveByAction(entryName, action, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
    }

    private static string? ResolveByAction(
        string entryName,
        FileConflictAction action,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        var resolved = action switch
        {
            FileConflictAction.Overwrite => entryName,
            FileConflictAction.Skip => null,
            FileConflictAction.Rename => GetUniqueEntryName(entryName, occupiedNames),
            FileConflictAction.OverwriteIfOlder => ShouldOverwriteByTime(entryModified, newFileModified) ? entryName : null,
            FileConflictAction.OverwriteIfSmaller => ShouldOverwriteBySize(entrySize, newFileSize) ? entryName : null,
            _ => entryName
        };
        if (resolved != null)
            occupiedNames.Add(resolved);
        CoreLog.Info($"AddConflictHelper.ResolveByAction: entry='{entryName}', action={action} -> {(resolved ?? "(skip)")}");
        return resolved;
    }

    /// <summary>添加场景：磁盘新文件比压缩包条目更新 → 覆盖（与解压方向相反）。</summary>
    private static bool ShouldOverwriteByTime(DateTime? entryModified, DateTime? newFileModified)
    {
        if (entryModified == null || newFileModified == null)
        {
            CoreLog.Info("AddConflictHelper.OverwriteIfOlder: missing timestamp -> overwrite");
            return true;
        }
        var result = newFileModified.Value > entryModified.Value;
        CoreLog.Info($"AddConflictHelper.OverwriteIfOlder: newFile={newFileModified:yyyy-MM-dd HH:mm:ss}, entry={entryModified:yyyy-MM-dd HH:mm:ss} -> {(result ? "overwrite" : "skip")}");
        return result;
    }

    /// <summary>添加场景：磁盘新文件比压缩包条目大 → 覆盖（与解压方向相反）。</summary>
    private static bool ShouldOverwriteBySize(long? entrySize, long? newFileSize)
    {
        if (entrySize == null || newFileSize == null)
        {
            CoreLog.Info("AddConflictHelper.OverwriteIfSmaller: missing size -> overwrite");
            return true;
        }
        var result = newFileSize.Value > entrySize.Value;
        CoreLog.Info($"AddConflictHelper.OverwriteIfSmaller: newFile={newFileSize}, entry={entrySize} -> {(result ? "overwrite" : "skip")}");
        return result;
    }

    private static FileConflictInfo BuildConflictInfo(
        string entryName,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        var info = new FileConflictInfo
        {
            FilePath = entryName,
            EntrySize = entrySize,
            EntryModified = entryModified,
            ExistingSize = newFileSize,
            ExistingModified = newFileModified,
        };
        // 对话框预填的建议名（仅文件名部分）
        info.SuggestedName = Path.GetFileName(GetUniqueEntryName(entryName, occupiedNames));
        return info;
    }

    /// <summary>
    /// 生成不与其他条目冲突的唯一条目名（file.txt → file (1).txt），正确处理 .tar.gz 双扩展名。
    /// </summary>
    public static string GetUniqueEntryName(string entryName, IReadOnlySet<string> occupiedNames)
    {
        var dir = Path.GetDirectoryName(entryName);
        string bareName, ext;
        if (entryName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            bareName = Path.GetFileName(entryName[..^7]);
            ext = ".tar.gz";
        }
        else
        {
            bareName = Path.GetFileNameWithoutExtension(entryName);
            ext = Path.GetExtension(entryName);
        }

        for (int i = 1; i < 1000; i++)
        {
            var candidateName = $"{bareName} ({i}){ext}";
            var candidate = string.IsNullOrEmpty(dir) ? candidateName : $"{dir.Replace('\\', '/')}/{candidateName}";
            if (!occupiedNames.Contains(candidate))
                return candidate;
        }
        return entryName; // 999 个名字全被占用，直接使用原条目名
    }

    /// <summary>用户自定义名合成最终条目名：保留目录前缀 + 净化文件名（复用 FileConflictHelper.SanitizeFileName）。</summary>
    private static string CombineCustomName(string entryName, string customName)
    {
        var dir = Path.GetDirectoryName(entryName);
        var safeName = FileConflictHelper.SanitizeFileName(customName);
        return string.IsNullOrEmpty(dir) ? safeName : $"{dir.Replace('\\', '/')}/{safeName}";
    }
}
```

注意：`CoreLog` 已在 `namespace MantisZip.Core` 中定义，`FileConflictHelper` 同命名空间直接使用（无需 using）。`FileConflictHelper.SanitizeFileName` 是 `internal`，同程序集可访问。

- [ ] **Step 4: 运行测试验证通过**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true --filter "FullyQualifiedName~AddConflictHelperTests"
```
Expected: 15/15 PASS。

- [ ] **Step 5: 构建 Core 验证**

```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
```
Expected: Build succeeded。

- [ ] **Step 6: 提交**

```powershell
git add src/MantisZip.Core/Utils/AddConflictHelper.cs tests/MantisZip.Tests/Utils/AddConflictHelperTests.cs
git commit -m "feat(core): 新增 AddConflictHelper 条目名级冲突解析（添加场景语义方向反转）"
```

---

## Task 3: ZipEngine.AddToArchiveAsync 冲突接线

**Files:**
- Modify: `src/MantisZip.Core/Engines/ZipEngine.cs:830-1247`
- Modify: `tests/MantisZip.Tests/Engines/ZipEngineTests.cs`

- [ ] **Step 1: 写失败测试（集成，走 copy-mode）**

在 `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` 中 `AddToArchiveAsync_AddsFiles` 之后插入以下测试（复用现有 `_engine`/`TrackFile`/`TrackDir`/`_tempFiles`）：

```csharp
    // ===== 冲突处理集成测试（copy-mode，默认不走加密路径） =====

    private async Task<string> CreateDupFileAsync(string name, string content)
    {
        var file = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content);
        _tempFiles.Add(file);
        return file;
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Overwrite_ReplacesContent()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive()); // hello.txt + subdir/nested.txt
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal("duplicate content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Skip_KeepsOriginal()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Skip });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Rename_AddsUniqueEntry()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Rename });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "hello (1).txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverSkip_KeepsOriginal()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => Task.FromResult(FileConflictAction.Skip),
        };
        await _engine.AddToArchiveAsync(archive, [dupFile], options);

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverCustomName()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "my-rename.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        await _engine.AddToArchiveAsync(archive, [dupFile], options);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "my-rename.txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_Cancel_AbortsOperation()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => throw new OperationCanceledException("用户取消"),
        };
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _engine.AddToArchiveAsync(archive, [dupFile], options));
    }
```

- [ ] **Step 2: 运行测试验证失败**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true --filter "FullyQualifiedName~AddToArchiveAsync_DuplicateName"
```
Expected: 部分 FAIL（Skip 测试失败——当前 copy-mode 重名新内容获胜；Rename 失败——无唯一名逻辑）。

- [ ] **Step 3: 实现接线（多处编辑）**

**Edit 3a — 旧条目收集扩展 + `Task.Run` lambda 改 async**（`ZipEngine.cs:836` 与 `:871-882`）：

`:836` `await Task.Run(() =>` 改为 `await Task.Run(async () =>`（lambda 内需 await 冲突解析）。

将 `:871-882` 的旧条目统计块替换为：

```csharp
            // 计算旧条目信息（使用 SharpCompress IArchive 读取）——同时收集条目名/大小/时间供冲突处理
            int oldEntryCount = 0;
            long oldTotalBytes = 0;
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingRawNames = new List<string>();
            var existingEntryInfo = new Dictionary<string, (long Size, DateTime? Modified)>(StringComparer.OrdinalIgnoreCase);
            using (var archive = OpenArchiveWithEncodingFallback(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    oldTotalBytes += entry.Size;
                    oldEntryCount++;
                    var rawName = entry.Key ?? string.Empty;
                    var normalized = ArchivePath.Normalize(rawName);
                    existingNames.Add(normalized);
                    existingRawNames.Add(rawName);
                    existingEntryInfo[normalized] = (entry.Size, entry.LastModifiedTime);
                }
            }
```

**Edit 3b — 冲突解析块**（插入到上述 using 块之后、`long newTotalBytes` 之前）：

```csharp
            // 解析条目名冲突（复用解压冲突策略；语义方向反转见 AddConflictHelper）
            var occupiedNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var resolvedFiles = new List<(string FullPath, string EntryName)>();
            var overwrittenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fullPath, entryName) in newFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = ArchivePath.Normalize(entryName);
                existingEntryInfo.TryGetValue(normalized, out var existing);
                var fi = new FileInfo(fullPath);
                var finalName = await AddConflictHelper.ResolveEntryNameAsync(
                    normalized, options, existing.Modified, existing.Size, fi.LastWriteTime, fi.Length, occupiedNames);
                if (finalName == null)
                {
                    CoreLog.Info($"AddToArchiveAsync: skipped '{entryName}' (conflict action)");
                    continue;
                }
                if (existingNames.Contains(normalized) && finalName == normalized)
                    overwrittenNames.Add(normalized); // 覆盖：copy-mode 需从 keepEntryNames 排除旧条目
                resolvedFiles.Add((fullPath, finalName));
            }

            if (resolvedFiles.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: all files skipped by conflict handling");
                return;
            }
```

`existingEntryInfo.TryGetValue` 未命中时 `existing` 为 `(0, null)`——仅当条目名不存在时发生，而该分支在 `AddConflictHelper` 中不 consult 这些值（无冲突早退），安全。

**Edit 3c — `newTotalBytes` 改用 resolvedFiles**（原 `:884`）：

```csharp
            long newTotalBytes = resolvedFiles.Sum(f => new FileInfo(f.FullPath).Length);
```

**Edit 3d — copy-mode keepEntryNames + addEntries 用 resolvedFiles**（`ZipEngine.cs:901-926`）：

将 `// Build NewEntry list from source paths with auto-cleanup` 块中的 `foreach (var (fullPath, entryName) in newFiles)` 改为 `foreach (var (fullPath, entryName) in resolvedFiles)`。

将 `RewriteAsync` 调用的 `keepEntryNames: null,  // keep all existing entries` 替换为：

```csharp
                            // 覆盖重名条目时排除旧条目（keepSet 存原始名 + OrdinalIgnoreCase，与 DeleteEntriesAsync 一致）
                            HashSet<string>? keepEntryNames = null;
                            if (overwrittenNames.Count > 0)
                            {
                                keepEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var raw in existingRawNames)
                                    if (!overwrittenNames.Contains(ArchivePath.Normalize(raw)))
                                        keepEntryNames.Add(raw);
                            }

                            var result = ZipBinaryRewriter.RewriteAsync(
                                sourcePath: archivePath,
                                destPath: tempArchiveFast,
                                keepEntryNames: keepEntryNames,
                                addEntries: newEntries,
                                encoding: encoding,
                                comment: options.Comment,  // null = preserve original comment
                                progress: progress,
                                cancellationToken: cancellationToken).GetAwaiter().GetResult();
```

注意：`keepEntryNames` 声明需在 `try` 块内（RewriteAsync 调用处），原 `newEntries`/`streamsToDispose` 声明处一并放。

**Edit 3e — legacy Phase 2 用 resolvedFiles**（`ZipEngine.cs:1064-1072`）：

将 `// === Phase 2: 复制新文件到临时目录 ===` 块的 `foreach (var (fullPath, entryName) in newFiles)` 改为 `foreach (var (fullPath, entryName) in resolvedFiles)`（Skip 已过滤、Rename 已应用最终名；Overwrite 经 `File.Copy(overwrite: true)` 覆盖同名旧条目，与现有行为一致）。

- [ ] **Step 4: 运行测试验证通过**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true --filter "FullyQualifiedName~AddToArchiveAsync_DuplicateName"
```
Expected: 6/6 PASS。

- [ ] **Step 5: 全量 Core 测试回归**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true
```
Expected: 全部通过（含既有 `AddToArchiveAsync_AddsFiles`、`DeleteEntriesAsync` copy-mode 等）。

- [ ] **Step 6: 提交**

```powershell
git add src/MantisZip.Core/Engines/ZipEngine.cs tests/MantisZip.Tests/Engines/ZipEngineTests.cs
git commit -m "feat(core): 添加到 ZIP 压缩包支持重名条目冲突处理（覆盖/跳过/重命名/Ask）"
```

---

## Task 4: SevenZipEngine.AddToArchiveAsync 冲突接线

**Files:**
- Modify: `src/MantisZip.Core/Engines/SevenZipEngine.cs:960-1036`
- Modify: `tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs`

- [ ] **Step 1: 写失败测试（集成，覆盖 = ModifyArchive 删除 + Append 重加）**

在 `tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs` 中 `AddToArchiveAsync_RespectsEntryBasePath` 之后插入：

```csharp
    // ===== 冲突处理集成测试 =====

    private async Task<string> CreateDupFileAsync(string name, string content)
    {
        var file = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_{name}");
        await File.WriteAllTextAsync(file, content);
        TrackFile(file);
        return file;
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Overwrite_ReplacesContent()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal("duplicate content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Skip_KeepsOriginal()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Skip });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Rename_AddsUniqueEntry()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Rename });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "hello (1).txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverCustomName()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "my-rename.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        await _engine.AddToArchiveAsync(archive, [dupFile], options);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "my-rename.txt");
    }
```

注意：`CreateDupFileAsync` 与 ZipEngineTests 的同名 helper 各自定义在各自类中（xUnit 不跨类共享私有方法，勿合并）。若 `SevenZipEngineTests` 已有 `TrackFile` 私有方法签名兼容，直接复用。

- [ ] **Step 2: 运行测试验证失败**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true --filter "FullyQualifiedName~AddToArchiveAsync_DuplicateName&FullyQualifiedName~SevenZipEngineTests"
```
Expected: Overwrite FAIL（Append 静默忽略重名，新内容不生效）；Skip FAIL（新文件被忽略而非跳过，但旧内容保留——实际行为恰好满足断言，可能意外 PASS）；Rename FAIL。

- [ ] **Step 3: 实现接线**

**Edit 3a — `Task.Run` lambda 改 async + 收集现有条目 + 冲突解析**（`SevenZipEngine.cs:974-1020` 区域）：

`:974` `await Task.Run(() =>` 改为 `await Task.Run(async () =>`。

在 `if (fileDict.Count == 0)` 检查**之后**（`:1020` 之后）、`compr.CompressFileDictionary` 之前，插入：

```csharp
            // 收集压缩包现有条目（名称/大小/时间/索引）供冲突处理
            // 注意：加密文件名（EncryptHeaders）的 7z 需密码才能列出条目，与 AddToArchiveAsync 既有约束一致
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingEntryInfo = new Dictionary<string, (int Index, long Size, DateTime Modified)>(StringComparer.OrdinalIgnoreCase);
            using (var extractor = string.IsNullOrEmpty(options.Password)
                       ? new SharpSevenZipExtractor(archivePath)
                       : new SharpSevenZipExtractor(archivePath, options.Password))
            {
                foreach (var e in extractor.ArchiveFileData)
                {
                    if (e.IsDirectory) continue;
                    var normalized = ArchivePath.Normalize(e.FileName);
                    existingNames.Add(normalized);
                    existingEntryInfo[normalized] = (e.Index, (long)e.Size, e.LastWriteTime);
                }
            }

            // 解析条目名冲突（语义方向反转见 AddConflictHelper；覆盖 = 先删旧条目再追加）
            var occupiedNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var finalDict = new Dictionary<string, string>();
            var deleteIndexes = new Dictionary<int, string>();
            foreach (var (entryName, sourcePath) in fileDict)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = ArchivePath.Normalize(entryName);
                existingEntryInfo.TryGetValue(normalized, out var existing);
                var fi = new FileInfo(sourcePath);
                var finalName = await AddConflictHelper.ResolveEntryNameAsync(
                    normalized, options, existing.Modified, existing.Size, fi.LastWriteTime, fi.Length, occupiedNames);
                if (finalName == null)
                {
                    CoreLog.Info($"AddToArchiveAsync: skipped '{entryName}' (conflict action)");
                    continue;
                }
                if (existingNames.Contains(normalized) && finalName == normalized)
                    deleteIndexes[existing.Index] = null!; // 覆盖：ModifyArchive 传 null 值 = 删除该索引条目
                finalDict[finalName] = sourcePath;
            }

            if (finalDict.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: all files skipped by conflict handling");
                return;
            }

            // 覆盖条目先删除（探针验证：ModifyArchive(index→null) 删除有效），再追加
            if (deleteIndexes.Count > 0)
            {
                CoreLog.Info($"AddToArchiveAsync: deleting {deleteIndexes.Count} overwritten entries via ModifyArchive");
                var delCompr = new SharpSevenZipCompressor { ArchiveFormat = OutArchiveFormat.SevenZip };
                delCompr.ModifyArchive(archivePath, deleteIndexes, options.Encrypt ? options.Password ?? "" : "");
            }
```

**Edit 3b — CompressFileDictionary 用 finalDict**（原 `:1021-1024`）：

```csharp
            compr.CompressFileDictionary(
                finalDict,
                archivePath,
                options.Encrypt ? options.Password ?? "" : "");
```

- [ ] **Step 4: 运行测试验证通过**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true --filter "FullyQualifiedName~AddToArchiveAsync_DuplicateName&FullyQualifiedName~SevenZipEngineTests"
```
Expected: 4/4 PASS（7z.dll 可用时；不可用则全部 `return` 跳过，测试仍 PASS）。

- [ ] **Step 5: 全量 Core 测试回归**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true
```
Expected: 全部通过。

- [ ] **Step 6: 提交**

```powershell
git add src/MantisZip.Core/Engines/SevenZipEngine.cs tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs
git commit -m "feat(core): 添加到 7z 压缩包支持重名条目冲突处理（ModifyArchive 删除 + Append 重加）"
```

---

## Task 5: Avalonia UI 接线（弹窗标题 + VM 委托 + AddFiles options + 本地化）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Dialogs/ConflictDialog.axaml.cs`
- Modify: `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`
- Modify: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.en.json`

- [ ] **Step 1: 本地化新 key（规则 13）**

`src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` 文件头 `{` 之后（`:1` 后、`"Preview_AnimatedImage"` 之前）插入：

```json
{
  "AddConflict_Title": "添加冲突",
```

`src/MantisZip.UI.Avalonia/Localization/strings.en.json` 同样位置插入：

```json
{
  "AddConflict_Title": "Add Conflict",
```

保持 UTF-8 无 BOM + CRLF + 2 空格缩进。

- [ ] **Step 2: ConflictDialog 加 titleKey 参数**

`src/MantisZip.UI.Avalonia/Dialogs/ConflictDialog.axaml.cs`：

`:15` 类字段区新增：

```csharp
    private string _titleKey = "Conflict_Title";
```

`:37` 改为：

```csharp
    public string WinTitle => LocalizationManager.T(_titleKey);
```

`:62` ctor 签名改为：

```csharp
    public ConflictDialog(FileConflictInfo info, string? titleKey = null)
```

ctor 第一行（`:63` `InitializeComponent();` 之前）新增：

```csharp
        if (!string.IsNullOrEmpty(titleKey))
            _titleKey = titleKey;
```

- [ ] **Step 3: ExtractFlow.ShowConflictDialogAsync 加 titleKey 参数**

`src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs` `:74-75`：

```csharp
    public static async Task<(FileConflictAction Action, bool ApplyToAll)>
        ShowConflictDialogAsync(Window owner, FileConflictInfo info, string titleKey = "Conflict_Title")
```

`:82` `var dlg = new ConflictDialog(info);` 改为：

```csharp
                var dlg = new ConflictDialog(info, titleKey);
```

- [ ] **Step 4: MainWindowViewModel 新委托 + AddFiles 冲突 options**

`src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`：

在 `ShowExtractFileConflictDialogAsync` 声明（`:92`）之后新增：

```csharp
    /// <summary>
    /// 添加文件冲突对话框回调（添加到压缩包场景）。与解压冲突同签名，标题用「添加冲突」。
    /// </summary>
    public Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? ShowAddFileConflictDialogAsync { get; set; }
```

`AddFiles`（`:2364-2393`）的 `:2382` `var options = new ArchiveOptions { Password = password };` 替换为：

```csharp
                // 复用解压冲突处理：同一 AppSettings.FileConflictAction 策略 + Ask 弹窗回调（标题区分）
                // CreateExtractOptions 返回 null 表示 Overwrite 默认（无冲突处理），回退到仅密码的 options
                var options = SelectedItemsExtractService.CreateExtractOptions(
                        AppSettings.Load().FileConflictAction, ShowAddFileConflictDialogAsync)
                    ?? new ArchiveOptions();
                options.Password = password;
```

确认 `MainWindowViewModel.cs` 顶部已有 `using MantisZip.UI.Avalonia.Models;`（`AppSettings`）与 `using MantisZip.UI.Avalonia.Services;`（`SelectedItemsExtractService`）。若无后者，添加 `using MantisZip.UI.Avalonia.Services;`。

- [ ] **Step 5: MainWindow.axaml.cs 接线**

`src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs` `:305`（`ShowExtractFileConflictDialogAsync` 接线）之后新增：

```csharp
        // 添加文件冲突弹窗：复用解压弹窗循环，仅标题用「添加冲突」
        vm.ShowAddFileConflictDialogAsync = info => ExtractFlow.ShowConflictDialogAsync(this, info, titleKey: "AddConflict_Title");
```

- [ ] **Step 6: 构建 Avalonia 验证**

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj -p:SkipShellExtCopy=true
```
Expected: Build succeeded（若本地化 json 格式错误或 VM 缺 using 会在此暴露）。

- [ ] **Step 7: 提交**

```powershell
git add src/MantisZip.UI.Avalonia/Dialogs/ConflictDialog.axaml.cs src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json src/MantisZip.UI.Avalonia/Localization/strings.en.json
git commit -m "feat(avalonia): 添加到压缩包复用解压冲突弹窗（Ask/覆盖/跳过/重命名/覆盖旧/覆盖小）"
```

---

## Task 6: 全量回归 + PROGRESS.md + 收尾

**Files:**
- Modify: `docs/PROGRESS.md`
- Modify: `docs/PLAN.md`（已在写计划时添加本计划行，无需重复）

- [ ] **Step 1: 全量测试回归**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj -p:SkipShellExtCopy=true
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj -p:SkipShellExtCopy=true
```
Expected: 全部通过（MantisZip.Tests 基数 271；Avalonia.Tests 基数 60/2 跳过）。

- [ ] **Step 2: lsp_diagnostics 检查改动文件**

对 Task 2-5 所有改动文件运行 `lsp_diagnostics`，确认无 error（SharpSevenZip 命名空间报错属环境问题，以 dotnet build 为准）。

- [ ] **Step 3: 更新 PROGRESS.md（规则 3，提交前）**

**共享层（Core / ShellExt / 构建）** 线索顶部（版本号与 Task 1 相同则追加到该条目下）：

```markdown
- 添加到压缩包支持重名条目冲突处理：新增 `AddConflictHelper`（条目名级解析，语义方向与解压相反：新数据更新/更大→覆盖）；ZIP copy-mode `keepEntryNames` 排除被覆盖条目、legacy Phase 2 应用解析结果；7z 覆盖经 `ModifyArchive`(index→null) 删除 + `CompressFileDictionary` Append 重加
```

**MantisZip.UI.Avalonia（主力版）** 线索顶部：

```markdown
**2026-08-18** — 添加到压缩包复用解压冲突处理：同一 `AppSettings.FileConflictAction` 策略 + ConflictDialog 弹窗（新标题 key `AddConflict_Title`「添加冲突」），覆盖/跳过/重命名/覆盖旧文件/覆盖小文件/Ask 全动作集
```

- [ ] **Step 4: 提交收尾**

```powershell
git add docs/PROGRESS.md
git commit -m "docs: PROGRESS.md 记录添加到压缩包冲突处理功能"
```

（若 Task 5 提交时已包含 PROGRESS.md 更新，则本步跳过，改为在 Task 5 的 commit 命令中加入 `docs/PROGRESS.md`。）

---

## Self-Review

**Spec 覆盖：**
- 复用现有文件冲突处理（`FileConflictAction` 全动作集 + `ConflictDialog` 弹窗）→ Task 2（`AddConflictHelper` 纯逻辑）、Task 3（ZIP）、Task 4（7z）、Task 5（Avalonia 弹窗接线）
- ZIP 与 7z 两个引擎的 `AddToArchiveAsync` → Task 3 / Task 4（各自独立 TDD 集成测试）
- 添加场景语义方向反转（新数据更新/更大 → 覆盖）→「语义映射」章节 + `AddConflictHelper.ShouldOverwriteByTime/Size`（Task 2 Step 3）
- TarGz 无添加能力（`AddToArchiveAsync` 抛 `NotSupportedException`）→ 明确排除在范围外（文件结构表「范围边界」）
- WPF 保持默认行为不动（规则 11）→ 范围边界声明；Task 3/4 的 `options.ConflictAction` 缺失时回退 `Overwrite`（`AddConflictHelper` 默认值）

**Placeholder 扫描：** 全部步骤含完整代码 + 精确行号 + 期望输出，无 TBD /「参考 Task N」/「补充错误处理」等占位模式。

**类型一致性：**
- `AddConflictHelper.ResolveEntryNameAsync(entryName, options, entryModified, entrySize, newFileModified, newFileSize, occupiedNames)` 在 Task 2 定义，Task 3（`ZipEngine.cs`）与 Task 4（`SevenZipEngine.cs`）调用参数顺序/类型一致
- `ConflictDialog(FileConflictInfo info, string? titleKey = null)` 在 Task 5 Step 2 定义，Step 3（`ExtractFlow.ShowConflictDialogAsync(owner, info, titleKey)`）与 Step 5（传 `"AddConflict_Title"`）传参一致
- `ShowAddFileConflictDialogAsync` 与既有 `ShowExtractFileConflictDialogAsync` 同为 `Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>`，Task 5 Step 4/5 定义与接线一致
- Task 3 内 `keepEntryNames`/`overwrittenNames`/`resolvedFiles` 与 Task 4 内 `finalDict`/`deleteIndexes` 在各自 Step 3 的 Edit 3a-3e 间引用一致