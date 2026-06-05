# 压缩流程统一化（CompressService）

## 冲突处理只是表象，真正的重复是整个压缩流程

压缩时目标文件已存在的冲突处理逻辑散落在多处，每处各自实现 `CompressConflictDialog` 弹窗 + switch 分支 + ApplyToAll 记忆逻辑，导致：

1. **重复代码** — 相同的冲突处理模式写了至少 3 遍
2. **维护风险** — 改一处容易漏其他处（本次 ApplyToAll 就是例子）
3. **行为不一致** — 各路径对 Rename/Add/Overwrite 的后续处理细节可能微妙不同

## 现状盘点

### 压缩冲突对话框 (`CompressConflictDialog`)

```csharp
CompressConflictDialog(string filePath, bool canAdd, string? suggestedName)
```

输出：`ResultAction` (Overwrite / Add / Rename / Cancel) + `ApplyToAll` (bool)

### 所有调用点

#### A. GUI 路径 — CompressSettingsWindow

| 方法 | 循环？ | 位置（实际行号） | 已修 ApplyToAll？ |
|---|---|---|---|---|
| `RunManualCompressAsync` | ❌ 单次 | `CompressSettingsWindow.xaml.cs:822` (冲突在 835) | 不需要 |
| `RunSeparateCompressAsync` | ✅ 循环 | `CompressSettingsWindow.xaml.cs:932` (冲突在 1014) | ✅ |
| `RunCombinedCompressAsync` | ❌ 单次 | `CompressSettingsWindow.xaml.cs:1128` (冲突在 1162) | 不需要 |

#### B. CLI 路径 — App.Cli.cs

| 方法 | 循环？ | 位置（实际行号） | 已修 ApplyToAll？ |
|---|---|---|---|
| `RunCompressSeparateBatch` | ✅ 循环 | `App.Cli.cs:129` (冲突在 176) | ✅ (第二轮) |
| `RunCompressCombined` | ❌ 单次 | `App.Cli.cs:349` (冲突在 406) | 不需要 |
| `HandleCompressQuick` | ❌ 单次 | `App.Cli.cs:1186` (冲突在 1226) | 不需要 |

#### C. 提取/错误处理（对照参考）

提取冲突 `ConflictDialog` + `ErrorDialog` **已经集中化**，都在 `App.xaml.cs`：

```csharp
// 居中 — CreateExtractOptions 内的 ConflictResolver 闭包
internal static ArchiveOptions CreateExtractOptions()
{
    bool applyToAll = false;
    FileConflictAction? chosenAction = null;
    return new ArchiveOptions
    {
        ConflictAction = GetConflictActionFromSettings(),
        ConflictResolver = info => { /* 统一弹窗逻辑 */ }
    };
}

// 居中 — CreateCompressOptions 内的 ErrorResolver 闭包
internal static ArchiveOptions CreateCompressOptions()
{
    bool applyToAll = false;
    FileErrorAction? chosenAction = null;
    return new ArchiveOptions
    {
        ErrorResolver = info => { /* 统一弹窗逻辑 */ }
    };
}
```

引擎层通过 `FileConflictHelper.ResolvePath` 调用这些回调，调用方只需 `options.ConflictResolver`，不需要自己写 switch。

### 三路重复的代码模式

每个压缩冲突调用点都重复了这段结构：

```
if (File.Exists(outputPath))
    if (applyToAll && chosenAction.HasValue)
        直接用记忆的 action
    else
        弹 CompressConflictDialog
        如果 ApplyToAll 则记忆
        switch (action):
            Overwrite → 继续压缩
            Rename → 改路径后压缩
            Add → 追加到已有压缩包
            Cancel → 跳过/退出
```

## 主方案：统一 CompressService

### 为什么提取端不需要动——架构对比

压缩端之所以需要大改，是因为它和提取端的架构差距巨大：

| 方面 | 压缩端 ❌ | 提取端 ✅ |
|---|---|---|
| 执行循环数量 | **3 个**（`RunSeparateCompressAsync`、`RunCompressSeparateBatch`、Manual/Combined 各一个） | **1 个**（`HandleExtractBatchCore`） |
| GUI 路径 | 自己写完整的压缩循环 + 冲突弹窗 | 只收集参数（写 AppSettings），执行走统一方法 |
| CLI 路径 | 又写一套完整的压缩循环 + 冲突弹窗 | 同样是走 `HandleExtractBatchCore` |
| 冲突处理 | 各路径手动写 `CompressConflictDialog` + switch | 集中化 `CreateExtractOptions()` + `ConflictResolver` 回调 |
| 参数传递 | 各路径从各自来源取值（控件 / AppSettings） | 统一写入 AppSettings，执行时读取 |

提取端的调用链：

```
GUI (ExtractSettingsWindow)
  │  ExtractButton_Click: 只写 AppSettings
  │  DialogResult = true
  │
  └──→ HandleExtractBatchCore ←── CLI 所有 --extract-* 路径
           ↓
        唯一的循环，唯一的冲突回调
        for i in allPaths
            engine.ExtractAsync(...)
```

压缩端的目标就是改成和提取端一样的结构——GUI/CLI 只是不同参数的入口，运行的代码是同一份。

### 核心思路

不仅冲突处理要集中，整个压缩流程都应该统一。GUI 和 CLI 只是不同入口，给同一个 `CompressService` 提供不同参数。

```
GUI (CompressSettingsWindow)           CLI (App.Cli)
  │  从控件取值构造 Request               │  从 AppSettings 构造 Request
  │  包装冲突回调                        │  包装冲突回调
  ├──→ CompressService.CompressAsync() ──┤
  │                                     │
   └── 收到进度 → 更新 UI                └── 收到进度 → 更新 UI
      this.Close()                          app.Shutdown()
```

### ⚠️ 行为变化（实施前必须确认）

由于 Service 统一了压缩流程，以下行为会发生变化：

#### B1. ProgressWindow 创建时机提前（Manual/Combined）

当前流程：
```
冲突弹窗 → [Add: 新建 PW → add → 关闭 PW] → [OW/RN: 新建 PW → compress → 关闭 PW]
         → [Cancel: 不创建 PW，直接 return]
```

改后（Service 模式）：
```
调用方先创建 PW → 调 Service → Service 内处理冲突
  → [Cancel: 返回 → 调用方关 PW]
  → [Add/OW/RN: Service 复用同一个 PW → 返回 → 调用方关 PW]
```

影响：
- Manual/Combined 的 Cancel 路径现在也会短暂显示 PW（创建后立即关闭）
- Add 路径不再开第二个 PW，统一复用传入的 PW
- 这是更简洁的 UX，但行为有变化

#### B2. Add 路径 ProgressWindow 统一化

当前 Manual/Combined 的 Add（line 853/1180）创建**独立** PW + 独立 CancellationToken；Separate 的 Add 复用主 PW。改后**所有路径的 Add 都复用传入的同一个 PW**。

#### B3. `KeepOriginalExtension` 行为统一

当前 GUI `RunSeparateCompressAsync` 始终忽略 `KeepOriginalExtension`（用 `Path.GetFileNameWithoutExtension`），CLI `RunCompressSeparateBatch` 则遵守 `KeepOriginalExtension`。Service 统一后会**修复这个不一致**——GUI 路径也将遵守 `KeepOriginalExtension`。如果之前用户通过 GUI Separate 压缩时依赖了"始终去掉扩展名"的行为，会发生变化。

#### B4. Cancel 计数方式统一

GUI `RunSeparateCompressAsync` 将 Cancel 记为 `fail++`（最终显示 "N 成功, N 失败"），CLI `RunCompressSeparateBatch` 记为 `skipped++`（跳过不显示为失败）。Service 统一记为 `Skipped++`，修复此不一致。

#### B5. 不在范围：`MainWindow.CompressAsync` 和 `AddFilesToCurrentArchiveAsync`

`MainWindow.xaml.cs:796` 有一个 `CompressAsync` 方法，由"文件→压缩"菜单触发（MainWindow.Menu.cs:87）。它使用 `SaveFileDialog` 选择输出路径（自带冲突处理），然后直接调 `engine.CompressAsync()`。**此路径不涉及 `CompressConflictDialog` 和冲突处理，不在本计划范围。**

`MainWindow.DragDrop.cs:84` 的 `AddFilesToCurrentArchiveAsync` 是对已打开压缩包的追加操作（拖拽添加），同样不涉及冲突对话框。**不在本计划范围。**

### 设计决策（新增）

#### D1. `CompressOutputMode` 枚举

当前 `OutputMode` 是 `CompressSettingsWindow` 的 `private enum`（Manual/Separate/Combined）。需要新建公共类型放到 Core：

```csharp
// 新增文件: Core/Abstractions/CompressEnums.cs
public enum CompressOutputMode { Manual, Separate, Combined }
```

替换掉 `CompressSettingsWindow.OutputMode` 的所有引用。

#### D2. `CompressRequest.Format` 用 `string` 而非 `ArchiveFormat`

引擎层用 `ArchiveEngineFactory.GetEngineByExtension(outputPath)` 根据文件扩展名选引擎，`CompressSettingsWindow` 的 `FormatComboBox` 也是存字符串。保持 `string` 更简单，调用方直接传 `"zip"` / `"7z"` / `"tar.gz"`。

#### D3. 密码管理器——放在调用方

`GetActivePassword()` 和 `SavePasswordAfterCompress()` 与 UI 控件和 `PasswordManager` 耦合较深：
- `GetActivePassword` 内部调 `PasswordManager.TryMatchPassword` 自动匹配密码库
- `SavePasswordAfterCompress` 调用 `PasswordManager.AddPassword` 保存到库

**决定：`CompressRequest` 只携带 `Password`，密码匹配在调用方构造 Request 时完成。Service 不接触密码管理器。**

#### D4. 注释分配——Service 内部处理

Service 接收 `Comment` + `CommentDistribution`，在循环内部按索引分配。调用方只需传原始注释文本和策略，不再自己预计算 `perLineComments` 数组。

#### D5. `CanAdd` 检查统一化——使用 `IArchiveEngine.CanAdd()`

当前所有调用点通过 `engine is not null and not TarGzEngine` 判断是否支持 Add（重复 6 次）。`IArchiveEngine` 已有 `CanAdd(ArchiveFormat)` 默认接口方法，TarGzEngine 未覆盖（返回 false），ZipEngine/SevenZipEngine 覆盖返回 true。

**决定：Service 使用 `engine.CanAdd(ArchiveEngineFactory.GetFormatByExtension(outputPath))` 判断，不再硬编码 `not TarGzEngine`。**

#### D6. Cancel 计数统一化

当前 GUI `RunSeparateCompressAsync` 将 Cancel 记为 `fail++`，CLI `RunCompressSeparateBatch` 将 Cancel 记为 `skipped++`（不显示为失败）。

**决定：Service 统一将 Cancel 记为 `Skipped++`，调用方按需决定如何展示。**

```csharp
// Service 内部的注释分配逻辑（所有路径统一）
// GetLineByIndex 是 Service 的 private helper：按换行符分割取第 i 行
string? resolvedComment = request.CommentDistribution switch
{
    CommentDistribution.AllSame => request.Comment,
    CommentDistribution.FirstOnly => i == 0 ? request.Comment : null,
    CommentDistribution.PerLine when request.Comment != null => 
        GetLineByIndex(request.Comment, i),
    _ => request.Comment
};

private static string? GetLineByIndex(string text, int index)
{
    var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    return index < lines.Length ? lines[index] : null;
}
```

### CompressService 接口

```csharp
public class CompressRequest
{
    public List<string> SourcePaths { get; init; }           // 源文件列表
    public CompressOutputMode Mode { get; init; }            // Manual / Separate / Combined
    public string Format { get; init; }                      // "zip" / "7z" / "tar.gz"
    public int CompressionLevel { get; init; }               // 1–9
    public string? Password { get; init; }                   // 加密密码（调用方已做好密码库匹配）
    public long SplitSize { get; init; }                     // 分卷大小
    public string? Comment { get; init; }                    // 原始注释文本（由 Service 按策略分配）
    public CommentDistribution CommentDistribution { get; init; }
    public bool Encrypt { get; init; }
    public string? OutputPath { get; init; }                 // Manual/Combined 模式：由调用方预计算的输出路径
    public bool KeepOriginalExtension { get; init; }         // Separate 模式：保留源文件扩展名
    public bool PreserveDirectoryRoot { get; init; } = true; // 仅 SevenZipEngine 单目录时有效
}

public class CompressResult
{
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }          // Cancel 或无效源文件导致的跳过
}

public class CompressService
{
    public async Task<CompressResult> CompressAsync(
        CompressRequest request,
        Func<CompressConflictInfo, CompressConflictResolution>? conflictResolver,
        IProgress<ArchiveProgress> progress,
        CancellationToken ct)
    {
        // 唯一的一份压缩逻辑
        // 1. 按 Mode 决定循环/单次：
        //    - Separate → 遍历 SourcePaths，每个 item 独立计算 outputPath
        //    - Manual/Combined → 单次执行（SourcePaths 整体作为 sourcePaths 传入引擎）
        //      outputPath = request.OutputPath（Combined 由调用方预计算）
        //
        // 2. 对每个 item（或唯一一次）:
        //    a. 验证源文件/目录存在（不存在 → Skipped++ → continue）
        //    b. 计算 outputPath：
        //       - Manual: request.OutputPath
        //       - Combined: request.OutputPath（调用方已预计算：FindCommonParent + 处理跨盘符）
        //       - Separate: per-item, 用 KeepOriginalExtension 决定文件名
        //         扩展名: format == "tar.gz" ? ".tar.gz" : "." + format
        //    c. 构造 CompressConflictInfo：
        //       OutputPath = 当前 outputPath
        //       CanAdd = engine.CanAdd(GetFormatByExtension(outputPath))
        //       SuggestedName = null（让对话框 fallback 到 Path.GetFileName）
        //       // 注：不预计算 GetUniquePath，因 Core 的版本不支持 tar.gz 双扩展名
        //    d. 检测文件冲突 → 调 conflictResolver 回调
        //       如果 conflictResolver 为 null → 直接覆盖（不弹窗）
        //    e. 如果 resolution.Action == Rename → 用 CustomName 重新计算 outputPath
        //        → 重新获取引擎（扩展名可能变了）
        //    f. 构建 ArchiveOptions：
        //       new ArchiveOptions {
        //           CompressionLevel = request.CompressionLevel,
        //           Encrypt = request.Encrypt,
        //           Password = request.Password,
        //           SplitSize = resolution.Action == CompressConflictAction.Add
        //                       ? 0 : request.SplitSize,   // Add 忽略分卷
        //           Comment = resolvedComment,
        //           CommentDistribution = request.CommentDistribution,
        //           PreserveDirectoryRoot = request.PreserveDirectoryRoot,
        //       }
        //    g. 根据 resolution.Action 分支：
        //       - Overwrite / Rename → engine.CompressAsync(sourcePaths, outputPath, options, progress, ct)
        //       - Add → engine.AddToArchiveAsync(outputPath, sourcePaths, options, progress, ct)
        //              → continue（跳过正常压缩）
        //       - Cancel / no handler → Skipped++ → continue
        //    h. 报告整体进度：
        //       progress.Report(new ArchiveProgress {
        //           PercentComplete = 当前进度百分比,
        //           FilePercentComplete = null,
        //           CurrentFile = Path.GetFileName(sourcePath),
        //       });
        //
        // 3. 返回 CompressResult { Succeeded, Failed, Skipped }
    }
}
```

> **关于 Add 路径**：在循环中 "添加到已有压缩包" 不是先删再重建，而是直接调 `AddToArchiveAsync`（单独的引擎方法）。Service 的循环逻辑需要处理这种"执行完毕后不继续正常压缩"的分支。这与 `ConflictDialog` 的 Add 行为不同（提取端的 Add 是追加同名文件到目录），压缩端的 Add 是**追加文件到已有压缩包**。
>
> **关于 SplitSize**：分卷设置仅对 `CompressAsync` 有意义，Add 路径忽略 SplitSize。
>
> **关于引擎重获取**：每次 outputPath 变化（Rename 后或初始计算）都通过 `ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine())` 重新获取引擎，确保扩展名变更后引擎正确。
>
> **关于无效源文件**：`SourcePaths` 中不存在的文件/目录路径会被跳过（Skipped++），与当前 `RunSeparateCompressAsync` (line 999-1003) 行为一致。
>
> **关于 ArchiveOptions**：Service 内部统一构造 `ArchiveOptions`，调用方无需再传。`CompressRequest` 包含了所有需要的参数。

### 冲突回调（与提取端模式一致）

```csharp
// Core/Abstractions/
public record CompressConflictInfo(
    string OutputPath,
    bool CanAdd,
    string? SuggestedName);

public record CompressConflictResolution(
    CompressConflictAction Action,
    string? CustomName);

public delegate CompressConflictResolution CompressConflictResolver(
    CompressConflictInfo info);
```

### 调用方示例

```csharp
// GUI — CompressSettingsWindow.RunSeparateCompressAsync
var result = await CompressService.CompressAsync(
    request,
    conflictResolver: info =>
    {
        // 闭包内记忆 ApplyToAll
        if (applyToAll && chosenAction.HasValue)
            return new CompressConflictResolution(chosenAction.Value, null);

        var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
        dlg.ShowDialog();
        if (dlg.ApplyToAll) { applyToAll = true; chosenAction = dlg.ResultAction; }
        return new CompressConflictResolution(dlg.ResultAction, dlg.CustomName);
    },
    progress,
    ct);
// 完成后窗口管理由 GUI 自行处理
this.Close();

// CLI — App.Cli.RunCompressSeparateBatch（注意：需要在 UI 线程弹窗）
var dispatcher = progressWindow.Dispatcher; // 或 app.Dispatcher
var result = await CompressService.CompressAsync(
    request,
    conflictResolver: info =>
    {
        // 回调可能在后台线程调用 → 必须 dispatch 到 UI 线程弹窗
        return dispatcher.Invoke(() =>
        {
            var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
            dlg.ShowDialog();
            return new CompressConflictResolution(dlg.ResultAction, dlg.CustomName);
        });
    },
    progress,
    ct);
app.Shutdown();
```

> **⚠️ Dispatcher 要求**：`conflictResolver` 回调可能在后台线程被调用（CLI 路径用 `Task.Run` 执行循环）。回调内如果涉及 UI 操作（弹 `CompressConflictDialog`），必须 `dispatcher.Invoke` 调度到 UI 线程。GUI 路径（CompressSettingsWindow 的方法）在主线程运行，不需要额外的 Dispatch。

### 差异点处理

| 差异 | 解决方案 |
|---|---|
| GUI 参数来自控件，CLI 来自 AppSettings | 统一到 `CompressRequest` POCO，各自填充 |
| GUI 弹窗用 `Dispatcher.Invoke`，CLI 用 `Dispatcher.InvokeAsync` | 回调在 UI 线程调用，由调用方自行决定线程策略 |
| 窗口管理（Hide/Close vs Shutdown） | Service 不做窗口管理，调用方在 await 前后处理 |
| 注释分配（AllSame/FirstOnly/PerLine） | Service 内部按 `CompressOutputMode` + 循环索引处理 |
| 密码管理器（`GetActivePassword`/`SavePasswordAfterCompress`） | 调用方构造 Request 时完成密码匹配，Service 只接收 `Password`；`SavePasswordAfterCompress` 由调用方在 await 后自行调用 |
| Manual 模式下 `OutputPath` 来自 TextBox | `CompressRequest.OutputPath` 由调用方填充，Service 直接使用 |
| "添加到已有压缩包"（Add）路径 | Service 循环内根据 `conflictResolver` 返回的 Action 分支：Overwrite/Rename 走 `CompressAsync`，Add 走 `AddToArchiveAsync` 并 `continue` |
| 重命名后引擎变更 | Service 每次 outputPath 变更后重新用 `GetEngineByExtension` 获取引擎 |
| `SplitSize` 对 Add 无效 | Service 的 Add 分支忽略 SplitSize（设为 0） |
| `ArchiveOptions` 构造 | Service 内部统一从 `CompressRequest` 构造 |
| 无效源文件路径 | Service 跳过不存在的路径（Skipped++），与当前行为一致 |
| Manual/Combined Add 开独立 PW | **行为变化**：Service 复用传入的 PW，不再另开（见 B1/B2） |
| GUI Separate 忽略 `KeepOriginalExtension` | **行为变化**：Service 统一使用 `KeepOriginalExtension`（见 B3） |
| `CanAdd` 检查方式 | Service 用 `engine.CanAdd(GetFormatByExtension(...))`，不再硬编码 `not TarGzEngine` |
| Cancel 计数 | Service 统一记为 `Skipped++`（GUI 原记为 `fail++`，**行为修复**） |
| 冲突回调线程安全 | 回调可能在后台线程调用，调用方自行处理 Dispatch（**计划已修正示例**） |
| `CompressService` 在 Core 层 | 无法访问 `AppSettings`，`KeepOriginalExtension` 等由调用方通过 `CompressRequest` 传递 |
| `MainWindow.CompressAsync`（第 7 条路径） | **不在范围**— 使用 SaveFileDialog 自带冲突处理，不涉及 CompressConflictDialog |
| `MainWindow.AddFilesToCurrentArchiveAsync` | **不在范围**— 对已加载压缩包追加文件，无冲突概念 |
| Combined 模式输出路径 | 调用方预计算（`FindCommonParent` + 跨盘符处理），通过 `request.OutputPath` 传入 |
| `SuggestedName` 预计算 | Service 不预计算（因 `GetUniquePath` 在 Core 中不支持 tar.gz），对话框 fallback 到 `Path.GetFileName` |
| `conflictResolver` 为 null | Service 直接覆盖已存在的文件（不弹窗，不跳过） |
| `PreserveDirectoryRoot` | 通过 `request.PreserveDirectoryRoot` 传递，Service 映射到 `ArchiveOptions` |
| `tar.gz` 双扩展名 | Service 通过 `format == "tar.gz" ? ".tar.gz" : "." + format` 统一处理 |
| `GetLineByIndex` helper | Service 内部私有方法，按换行分割取第 i 行 |

## 实施步骤

### Phase 1 — Core 层准备（基础设施）

1. **新建 `Core/Abstractions/CompressEnums.cs`**，包含：
   - `CompressConflictAction` 枚举（从 `CompressConflictDialog.xaml.cs` 移过来）
   - `CompressOutputMode` 枚举（`Manual` / `Separate` / `Combined`，替换 `CompressSettingsWindow.OutputMode`）

2. **新建 `Core/Abstractions/CompressConflict.cs`**（冲突回调类型）：
   - `CompressConflictInfo` record（`OutputPath`, `CanAdd`, `SuggestedName`）
   - `CompressConflictResolution` record（`Action`, `CustomName`）
   - `CompressConflictResolver` 委托类型

3. **新建 `CompressService` 类**（放到 `Core/Services/`）：
   - 依赖 `ArchiveEngineFactory`（已有）和 `IArchiveEngine`
   - 内部实现:
     - 按 `CompressOutputMode` 决定循环/单次
     - 每个 item：验证源文件存在 → 计算 outputPath → 获取引擎 → 冲突回调 → 按 resolution 分支执行
     - 引擎重获取：每次 outputPath 变更后重新调用 `GetEngineByExtension(outputPath, new ZipEngine())`
     - `ArchiveOptions` 构造：从 `CompressRequest` 映射（Add 分支忽略 SplitSize）
     - 注释分配：按 `CommentDistribution` + 循环索引（AllSame / FirstOnly / PerLine）
     - 整体进度报告
   - **不引用** UI 层（`Dispatcher`、`Window`、`AppSettings`）
   - 密码管理器不感知
   - 不处理 ProgressWindow 创建/关闭（由调用方在前后管理）

4. **更新 `CompressSettingsWindow.xaml.cs`**：
   - 删除 `private enum OutputMode`，改用 `CompressOutputMode` 引用
   - 更新所有 `_outputMode` 的类型引用

### Phase 2 — 调用方迁移

> **迁移原则**：每个调用方做五件事：
> 1. 创建 `ProgressWindow` 并 Show（**时机提前到冲突处理前**，见 B1）
> 2. 从自身来源（控件 / AppSettings）构造 `CompressRequest`
> 3. 闭包内实现 `CompressConflictResolver`（处理 ApplyToAll 记忆 + 弹窗）
> 4. 调 `CompressService.CompressAsync()`
> 5. await 完成后处理窗口生命周期（Close / Shutdown / SavePassword / 关闭 PW）

| # | 调用方 | 关键变化 |
|---|---|---|
| 3 | `RunCompressSeparateBatch` (App.Cli.cs:129) | 循环逻辑移入 Service；冲突回调改为委托；保留 Dispatcher.InvokeAsync；PW 创建时机不变（已提前创建） |
| 4 | `RunSeparateCompressAsync` (CompressSettingsWindow:932) | 删除循环；PW 创建移到调用 Service 之前；删除注释预计算；密码匹配在构造 Request 时完成 |
| 5 | `RunManualCompressAsync` (CompressSettingsWindow:822) | **PW 创建提前**到冲突处理前；冲突弹窗改为回调；`OutputPath` 在 Request 中传递；Add 路径不复用独立 PW |
| 6a | `RunCombinedCompressAsync` (CompressSettingsWindow:1128) | 同上 |
| 6b | `RunCompressCombined` (App.Cli:349) | 冲突弹窗改为回调；Add 路径不复用独立 PW |
| 6c | `HandleCompressQuick` (App.Cli:1186) | 冲突弹窗改为回调；改用传参的 PW |

### Phase 3 — 清理与验证

7. 删除 `CompressConflictDialog`（如果所有冲突都通过回调 + `CompressConflictResolver`，则此对话框文件可删；如果保留 ProgressWindow 嵌入的选项，则保留作为回调的默认实现）
8. 删除各调用方内联的冲突处理 switch/循环代码
9. 全面回归测试：GUI Manual/Separate/Combined + CLI quick/separate/combined，每种模式验证：
   - 目标文件不存在 → 正常压缩
   - 目标文件存在 → 弹冲突对话框 → Overwrite / Rename / Add / Cancel 各走一遍
   - Separate 模式 + ApplyToAll → 全部应用
   - 密码加密压缩 → 解压验证密码正确
   - 注释分配（AllSame / FirstOnly / PerLine）

## 优先级

- **P0** — Phase 1（基础设施）
- **P1** — Phase 2（迁移所有路径）
- **P2** — Phase 3（清理）

## 验收标准

- [ ] `CompressOutputMode` / `CompressConflictAction` 定义在 Core，不再依赖 UI 命名空间
- [ ] 所有 6 个压缩入口（GUI Manual/Separate/Combined + CLI quick/separate/combined）调用同一个 `CompressService.CompressAsync()`
- [ ] 冲突处理全部通过 `CompressConflictResolver` 回调注入，调用方不再自己写 `if (File.Exists) + switch`
- [ ] Separate 模式下 ApplyToAll 行为在 GUI 和 CLI 路径一致
- [ ] 注释分配（AllSame/FirstOnly/PerLine）在 Service 内部处理，调用方不再预计算 `perLineComments`
- [ ] 密码管理器 `GetActivePassword` / `SavePasswordAfterCompress` 逻辑不进入 Service
- [ ] Manual/Combined 路径的 ProgressWindow 在冲突处理前创建，Cancel 后也能正常关闭
- [ ] Add 路径统一复用传入的 ProgressWindow，不另开独立窗口
- [ ] GUI Separate 路径现在也遵守 `KeepOriginalExtension` 设置（行为修复，见 B3）
- [ ] Service 内部处理：引擎重获取、无效源文件跳过、Add 分支忽略 SplitSize
- [ ] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 通过
- [ ] GUI 路径：挑选文件 → Manual/Separate/Combined → 压缩成功，冲突弹窗正常
- [ ] CLI 路径：`--compress-quick` / `--compress-separate` / `--compress-combined` 行为与改动前一致（除 B1/B2/B3 注明变化外）
- [ ] 所有现有单元测试通过：`dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`

---

## 备选方案：冲突 UI 嵌入 ProgressWindow

### 思路

不弹独立的 `CompressConflictDialog` 窗口，而是把冲突交互直接嵌入到 `ProgressWindow` 中。冲突发生时进度窗口变为"冲突模式"——暂停进度、显示冲突按钮组，用户选择后恢复进度继续压缩。

### 机制：TaskCompletionSource 替代 ShowDialog

```
压缩线程                          ProgressWindow（UI 线程）
  │                                    │
  │  检测到冲突                        │  正常显示进度
  │  设置暂停状态                      │
  │  Dispatcher.InvokeAsync ──────→    │
  │                                    │  隐藏进度，显示冲突面板
  │  await tcs.Task (等待)             │  用户点按钮
  │                                    │  tcs.SetResult()
  │  ←── 拿到结果 ──────────           │
  │                                    │  显示进度，隐藏冲突面板
  │  继续压缩                          │
```

### ProgressWindow 冲突面板示意

```
┌───────────────────────────────────────┐
│  📦 正在压缩...              [×]      │
│  ████████░░░░ 60%                    │
│  当前文件: photo.jpg                  │
├───────────────────────────────────────┤
│  ── 文件冲突 ──                      │
│  目标文件 "photo.zip" 已存在          │
│                                       │
│  [覆盖] [添加到压缩包] [重命名] [跳过] │
│  ☑ 对后续所有文件使用相同操作         │
├───────────────────────────────────────┤
│  总进度: ██████░░░░░░ 50%            │
└───────────────────────────────────────┘
```

### 优点

1. **UI 统一** — 压缩冲突和提取冲突都在同一个窗口解决，不再弹独立窗口
2. **体验更好** — 用户看到进度上下文，知道停了在等什么
3. **架构更干净** — `CompressConflictDialog` 可以彻底删除
4. **循环路径天然统一** — GUI 和 CLI 都指向同一个 ProgressWindow，冲突处理路径自然归一

### 缺点 & 风险

1. **改动量大** — 涉及 `ProgressWindow` 的布局改造（增加冲突面板、切换逻辑）
2. **进度状态管理** — 冲突时进度条需要"冻结"当前值，解决后恢复
3. **Manual/Combined 模式** — 这些模式下压缩还没开始就发现冲突，此时 ProgressWindow 还没创建。需要调整顺序——先 Show ProgressWindow，再检查冲突
4. **提取冲突也用 ProgressWindow？** — 提取端的 `ConflictResolver` 被引擎层在后台线程调用，目前也是弹独立窗口。如果要统一，提取冲突也得改，范围更大

### 与主方案的关系

主方案是**逻辑统一**（公共 Service + 回调委托），备选方案是**UI 也统一**（嵌入 ProgressWindow）。两个不冲突，但**推荐拆成独立的计划**执行：

| 方面 | 主方案（本计划） | 备选方案（单独计划） |
|---|---|---|
| 范围 | Core + 调用方适配 | ProgressWindow 布局改造 + 线程协调 |
| 风险 | 低—提取端已验证此模式 | 中—涉及 WPF 控件改造 + TaskCompletionSource 死锁风险 |
| 依赖 | 无 | 必须先完成主方案 |
| 是否必须 | ✅ 是—解决核心重复问题 | ❌ 否—纯 UI 优化 |

**建议**：完成本计划后再开单独的 `progress-window-conflict-embed.md` 计划处理 UI 嵌入。
