# Draft: ProgressWindow 增强改造

## 需求确认

1. **文件信息三行化**：当前路径/文件名混在一行，改为路径（目录部分）、文件名、文件计数三行独立显示
2. **统计栏**：在进度条区域增加实时统计（已处理/跳过/出错计数）
3. **每包摘要**：批处理列表每个压缩包显示自己的处理摘要（已处理/跳过/出错/总数）

---

## 一、布局改动（ProgressWindow.xaml）

### 当前 Grid 行

| Row | 控件 | 说明 |
|-----|------|------|
| 0 | BatchFileList | 批处理文件列表（默认隐藏） |
| 1 | PasswordSection | 密码匹配区（默认隐藏） |
| 2 | FileNameText | 当前文件名（路径+文件名混合） |
| 3 | 文件进度条 | FileProgressBar + FilePercentText |
| 4 | 总进度条 | TotalProgressBar + PercentText |
| 5 | FileCountText | 压缩包计数（压缩包 3/10） |
| 6 | ErrorSummaryBox | 错误摘要（默认隐藏） |
| 7 | 弹性填充 | 把按钮压到底部 |
| 8 | 按钮行 | 📌保持打开 / 暂停 / 取消 |

### 改动后 Grid 行

| Row | 控件 | 说明 |
|-----|------|------|
| 0 | BatchFileList | 批处理文件列表（DataTemplate 扩展两行摘要） |
| 1 | PasswordSection | 密码匹配区 |
| 2 | DirPathText | **新增** 目录路径（渐变色截断） |
| 3 | FileNameText | 纯文件名 |
| --- | *分隔间距* | |
| 4 | 文件进度条 | FileProgressBar + FilePercentText |
| 5 | FileProgressCount | **新增** 文件级计数（文件 50/200） |
| 6 | 总进度条 | TotalProgressBar + PercentText |
| 7 | StatsBar | **新增** 统计栏（✅ 已处理 N ⏭ 跳过 N ❌ 出错 N） |
| 8 | FileCountText | 压缩包计数（保持不变） |
| 9 | ErrorSummaryBox | 错误摘要 |
| 10 | 弹性填充 | |
| 11 | 按钮行 | |

### 文件列表 DataTemplate 扩展

当前：
```
[状态图标] [文件名 + 进度条底色] [进度%]
```

改后：
```
[状态图标] [文件名 + 进度条底色] [进度%]
            [小字] 已处理 45/200  ⏭跳过 3  ❌出错 1
```

---

## 二、数据模型改动

### 2.1 ArchiveProgress 新增字段

```csharp
// Core/Abstractions/ArchiveEngine.cs
public class ArchiveProgress
{
    // 现有字段不变
    public string CurrentFile { get; set; } = string.Empty;
    // ...

    // 新增字段
    public int SkippedFiles { get; set; }
    public int FailedFiles { get; set; }
}
```

### 2.2 ArchiveOptions 新增回调

```csharp
// Core/Abstractions/ArchiveEngine.cs
public class ArchiveOptions
{
    // 现有字段不变
    public FileConflictAction ConflictAction { get; set; } = FileConflictAction.Overwrite;
    public Func<FileConflictInfo, FileConflictAction>? ConflictResolver { get; set; }
    // ...

    // 新增：FileConflictHelper 在决定跳过/覆盖后通知调用方最终行动
    public Action<FileConflictAction>? ConflictActionCallback { get; set; }
}
```

### 2.3 ExtractResult 新增字段

```csharp
// Core/Abstractions/ArchiveEngine.cs
public class ExtractResult
{
    public int SucceededEntries { get; init; }
    public int FailedEntries { get; init; }
    public bool HasFailures => FailedEntries > 0;

    // 新增
    public int SkippedEntries { get; init; }
}
```

### 2.4 BatchItem 新增摘要字段

```csharp
// Core/Models/ProgressBatchItem.cs
public class BatchItem : INotifyPropertyChanged
{
    // 现有字段不变
    public string Name { get; set; } = string.Empty;
    public BatchItemStatus Status { get; set; }
    public double Progress { get; set; }

    // 新增：每包的处理统计摘要
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int FailedFiles { get; set; }

    // 新增：摘要显示文本（绑定用）
    public string SummaryText => $"已处理 {ProcessedFiles}/{TotalFiles}  ⏭跳过 {SkippedFiles}  ❌出错 {FailedFiles}";
}
```

---

## 三、FileConflictHelper 改动

```csharp
// Core/Utils/FileConflictHelper.cs
// 在 ResolvePath 中，计算出最终 action 后，调用回调通知调用方
public static string? ResolvePath(string outputPath, ArchiveOptions? options,
    DateTime? entryModified = null, long? entrySize = null)
{
    // ... 现有逻辑：检查文件是否存在，返回 outputPath 如果不存在

    var action = options?.ConflictAction ?? FileConflictAction.Overwrite;

    // 处理 Ask 弹窗
    if (action == FileConflictAction.Ask && options?.ConflictResolver != null)
    {
        action = options.ConflictResolver(new FileConflictInfo { ... });
    }

    // ==== 新增：回调 ====
    options?.ConflictActionCallback?.Invoke(action);

    // 现有逻辑
    return ResolveByAction(outputPath, action, entryModified, entrySize);
}
```

**线程安全**：每个引擎的提取循环是顺序的，批处理也是逐个处理压缩包，不存在并发问题。`Interlocked.Increment` 可选。

---

## 四、引擎改动（三引擎统一模式）

每个引擎的 `ExtractAsync` 中：

```csharp
int skippedCount = 0;
var localOptions = options != null ? new ArchiveOptions
{
    // 复制现有选项
    ConflictAction = options.ConflictAction,
    ConflictResolver = options.ConflictResolver,
    // ...

    // 包装回调累加跳过计数
    ConflictActionCallback = action =>
    {
        if (IsSkipAction(action))
            Interlocked.Increment(ref skippedCount);
        // 透传原始回调（如果有）
        options.ConflictActionCallback?.Invoke(action);
    }
} : options;

// 提取循环中原有的 ResolvePath 调用 ->
// 如果返回 null，continue（原有逻辑不变）
// 回调已在 ResolvePath 内部触发

// 返回时带上跳过数
return new ExtractResult
{
    SucceededEntries = successCount,
    FailedEntries = failCount,
    SkippedEntries = skippedCount
};
```

`IsSkipAction` 逻辑：
```csharp
private static bool IsSkipAction(FileConflictAction action) => action switch
{
    FileConflictAction.Skip => true,
    FileConflictAction.OverwriteIfOlder => true,
    FileConflictAction.OverwriteIfSmaller => true,
    _ => false
};
```

**受影响的引擎文件：**
- `ZipEngine.cs` — `ExtractAsync` 方法
- `SevenZipEngine.cs` — `ExtractAsync` 方法
- `TarGzEngine.cs` — `ExtractAsync` 方法

---

## 五、提取 ProgressDisplayCalculator（Core 层，迁移关键）

**动机**：当前 `SetProgress` 里既有计算逻辑又有 WPF 控件赋值，迁移到 Avalonia 时计算逻辑必须重写。把计算逻辑提到 Core 层，UI 层只做绑定。

### 5.1 ProgressDisplayCalculator

```csharp
// Core/Utils/ProgressDisplayCalculator.cs
namespace MantisZip.Core.Utils;

/// <summary>
/// 从 ArchiveProgress 和批处理状态计算出所有显示值。
/// 纯计算，无 UI 依赖。WPF 和 Avalonia 共用。
/// </summary>
public class ProgressDisplayCalculator
{
    /// <summary>从当前文件路径分离目录和文件名</summary>
    public static (string dirPath, string fileName) SplitFilePath(string currentFile)
    {
        if (string.IsNullOrEmpty(currentFile))
            return ("", "");
        var dir = Path.GetDirectoryName(currentFile);
        var name = Path.GetFileName(currentFile);
        return (dir ?? "", name);
    }

    /// <summary>计算总进度（支持批处理权重）</summary>
    public static double CalculateOverallPercent(
        ArchiveProgress p, bool isBatchMode,
        int currentBatchIndex, int batchCount)
    {
        if (isBatchMode && batchCount > 1)
        {
            double completedWeight = currentBatchIndex > 0
                ? (double)currentBatchIndex / batchCount * 100
                : 0;
            double currentWeight = p.PercentComplete / batchCount;
            return completedWeight + currentWeight;
        }
        return p.PercentComplete;
    }

    /// <summary>格式化统计栏文本</summary>
    public static string FormatStatsText(
        int processed, int total, int skipped, int failed)
    {
        var parts = new List<string>();
        if (total > 0)
            parts.Add($"✅ 已处理 {processed}/{total}");
        if (skipped > 0)
            parts.Add($"⏭跳过 {skipped}");
        if (failed > 0)
            parts.Add($"❌出错 {failed}");
        return parts.Count > 0 ? string.Join("  ", parts) : "";
    }

    /// <summary>格式化文件级计数</summary>
    public static string FormatFileCount(int processed, int total)
        => total > 0 ? $"文件 {processed}/{total}" : "";
}
```

这样 WPF 的 `SetProgress` 变成：

```csharp
public void SetProgress(ArchiveProgress p)
{
    // ---- 计算（无 UI 依赖） ----
    var (dirPath, fileName) = ProgressDisplayCalculator.SplitFilePath(p.CurrentFile);
    var overallPct = ProgressDisplayCalculator.CalculateOverallPercent(
        p, _isBatchMode, _currentBatchIndex, _batchItems?.Count ?? 0);
    var statsText = ProgressDisplayCalculator.FormatStatsText(
        p.ProcessedFiles, p.TotalFiles, p.SkippedFiles, p.FailedFiles);
    var fileCountText = ProgressDisplayCalculator.FormatFileCount(
        p.ProcessedFiles, p.TotalFiles);

    // ---- 赋值（WPF 特有，迁移时替换为 Avalonia 绑定） ----
    DirPathText.Text = dirPath;
    FileNameText.Text = fileName;
    TotalProgressBar.Value = overallPct;
    PercentText.Text = $"{overallPct:F1}%";
    StatsBarText.Text = statsText;
    FileProgressCountText.Text = fileCountText;

    // ... 文件进度条、批处理项更新等
}
```

Avalonia 迁移时，这部分只需替换赋值部分（或者改用 ViewModel 绑定），计算逻辑不动。

### 5.2 XAML 新增控件

```xml
<!-- Row 2：目录路径 -->
<TextBlock x:Name="DirPathText" Grid.Row="2"
           Text="" FontSize="12"
           Foreground="{StaticResource Theme_TextSecondary}"
           TextTrimming="PathEllipsis"
           Margin="0,0,0,2"/>

<!-- Row 3：纯文件名 -->
<TextBlock x:Name="FileNameText" Grid.Row="3" ... />
<!-- 原来 FileNameText 从 Row 2 移到 Row 3 -->

<!-- Row 5：文件级计数 -->
<TextBlock x:Name="FileProgressCountText" Grid.Row="5"
           Text="" FontSize="12"
           Foreground="{StaticResource Theme_TextSecondary}"
           Margin="0,0,0,4"/>

<!-- Row 7：统计栏 -->
<Border x:Name="StatsBar" Grid.Row="7"
        Background="{StaticResource Theme_SurfaceBg}"
        CornerRadius="4" Padding="8,4" Margin="0,4,0,4"
        Visibility="Visible">
    <TextBlock x:Name="StatsBarText" Text=""
               FontSize="12"
               Foreground="{StaticResource Theme_TextPrimary}"/>
</Border>
```

### 5.3 其他 Grid Row 编号调整

原有控件下移：
- 总进度条：Row 4 → Row 6
- FileCountText：Row 5 → Row 8
- ErrorSummaryBox：Row 6 → Row 9
- 弹性填充：Row 7 → Row 10
- 按钮行：Row 8 → Row 11

---

## 六、改动文件清单

### Core 层（6 文件，1 新增）

| 文件 | 改动 |
|------|------|
| `Core/Utils/ProgressDisplayCalculator.cs` | **新增** 显示值计算工具类（纯计算，无 UI 依赖，WPF/Avalonia 共享） |
| `Core/Abstractions/ArchiveEngine.cs` | `ArchiveProgress` 加 `SkippedFiles`/`FailedFiles`；`ArchiveOptions` 加 `ConflictActionCallback`；`ExtractResult` 加 `SkippedEntries` |
| `Core/Utils/FileConflictHelper.cs` | `ResolvePath` 里在调用 `ResolveByAction` 前插入回调 |
| `Core/Models/ProgressBatchItem.cs` | `BatchItem` 加 `TotalFiles`/`ProcessedFiles`/`SkippedFiles`/`FailedFiles` + `SummaryText` |
| `Core/Engines/ZipEngine.cs` | `ExtractAsync` 加 `skippedCount` + 包装回调 + 返回 `SkippedEntries` |
| `Core/Engines/SevenZipEngine.cs` | 同上 |
| `Core/Engines/TarGzEngine.cs` | 同上 |

### UI 层（2 文件）

| 文件 | 改动 |
|------|------|
| `UI/Dialogs/ProgressWindow.xaml` | Grid 行调整 + 新增 DirPathText/FileProgressCountText/StatsBar + BatchFileList DataTemplate 扩展摘要行 |
| `UI/Dialogs/ProgressWindow.xaml.cs` | `SetProgress` 调用 `ProgressDisplayCalculator` 获取显示值，然后赋给控件（最薄的一层） |

### 调用方（1 文件）

| 文件 | 改动 |
|------|------|
| `UI/AppPartials/App.Extract.cs` | 批处理完成后，通过 `ExtractResult.SkippedEntries` 更新 UI 统计 |

---

## 七、边界情况与注意事项

1. **非批处理模式也要显示统计栏**——统计栏不依赖批处理模式，单包解压/压缩时同样显示
2. **压缩操作的统计**——压缩时也有 `ProcessedFiles`/`TotalFiles`，跳过和失败在压缩场景基本不出现，统计栏只显示已处理即可
3. **跳过回调在 `Ask` 弹窗场景下触发时机正确**——弹窗在 `ConflictResolver` 中阻塞，拿到用户选择后才走到回调
4. **压缩场景不设 `ConflictActionCallback`**——压缩时 `ArchiveOptions.ConflictActionCallback` 默认为 null，不影响
5. **`Path.GetDirectoryName` 对根路径的行为**——Windows 路径要处理 `C:\` 和 `\\server\share\` 等边界
