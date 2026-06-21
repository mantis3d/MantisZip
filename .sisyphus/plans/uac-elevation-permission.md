# UAC 提权 + 压缩包内逐条目权限跳过

## 已实现功能（v0.4.2）

### CLI 提权双模式

CLI 入口（`--extract-here`, `--extract-to-name`, `--extract-smart`, `--compress-quick`, `--compress-separate`, `--compress-combined`）在执行中遇到 `UnauthorizedAccessException` 时的行为：

- **默认行为**（`AllowElevation=false`）：弹 `ElevationInfoDialog` 列出权限不足的目录，用户点确定后继续处理后续压缩包
- **允许提权**（`AllowElevation=true`）：弹 `ElevationDialog` 询问是否以管理员身份重启
- **已提权仍失败**：弹 `ElevationFailedDialog` 提示，继续处理

### 弹窗规则

- 首次遇到权限不足 → 弹窗一次
- 后续（同次 batch 内）→ 静默跳过，标记 Failed，不弹窗
- 仅用户点击「以管理员身份运行」时才重启旧进程
- 三种弹窗均不调用 `app.Shutdown()`（提权重启除外），batch 继续处理后续压缩包

### 架构变迁

```
v0.4.2 之前：事前预检（扫描所有目标目录可写）→ 弹窗 → Shutdown
v0.4.2 之后：删除预检 → 解压中 catch(UnauthorizedAccessException) 响应式拦截
v0.4.2 当前：batch 级别 catch，失败后继续下一个压缩包
v0.4.3 计划：engine 内部逐条目 try-catch，跳过失败条目继续解压同一压缩包内其余文件
```

## 正在做：逐条目权限跳过（v0.4.3）

### 问题

当前 `ExtractAsync` 是**一次性调用**，任何一个条目写入失败（`File.Create` 抛 `UnauthorizedAccessException`），整个 `foreach` 循环退出，压缩包内其余条目全部丢失。

### 方案

在每个 engine 的 `ExtractAsync` 逐条目循环中包 `try-catch`，捕获 `UnauthorizedAccessException` 后：
- 记录 `CoreLog.Info` 日志
- 跳过该条目
- 继续下一条
- 返回 `ExtractResult` 告知调用方失败数量

### ExtractResult 数据结构

```csharp
public class ExtractResult
{
    public int SucceededEntries { get; init; }
    public int FailedEntries { get; init; }
    public int TotalEntries => SucceededEntries + FailedEntries;
    public bool HasFailures => FailedEntries > 0;
}
```

### Engine 改动

| Engine | 当前循环方式 | 改动 |
|--------|-------------|------|
| **ZipEngine** | `foreach` `archive.Entries` 无 try-catch | 包 try-catch `UnauthorizedAccessException`，`continue` |
| **SevenZipEngine** | `for` `allEntries` + `ExtractFile(index, stream)` | 包 try-catch `UnauthorizedAccessException`，`continue` |
| **TarGzEngine** | `foreach` 逐 reader 条目 | 包 try-catch `UnauthorizedAccessException`，`continue` |

### 调用方改动（App.Extract.cs）

```csharp
var result = await engine.ExtractAsync(archivePath, dest, password, progress, ct, batchOptions);

if (result.HasFailures)
{
    // 部分条目失败：标记"部分成功"
    // 仍记为 succeeded（压缩包已处理完），但用户可以看日志
}
```

### 验证清单

| # | 场景 | 预期 |
|---|------|------|
| 1 | ZIP 内某条目权限不足，其余可写 | 跳过失败条目，其余正常解压，日志记录 |
| 2 | 7z 内某条目权限不足 | 同上 |
| 3 | TarGz 内某条目权限不足 | 同上 |
| 4 | 所有条目都权限不足 | 全部跳过，ExtractAsync 正常返回（无异常）|
| 5 | 非权限异常（压缩包损坏） | 仍然抛异常，由外层 catch 处理 |
| 6 | 首次权限不足弹窗 | 弹出对应对话框（ElevationInfo/Elevation/Failed）|
| 7 | 后续权限不足 | 静默跳过（permissionDialogShown 机制）|

## 涉及文件

| 文件 | 改动 |
|------|------|
| `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | 新增 `ExtractResult` 类；`IArchiveEngine.ExtractAsync` 返回 `Task<ExtractResult>` |
| `src/MantisZip.Core/Engines/ZipEngine.cs` | ExtractAsync `foreach` 包 try-catch，返回 ExtractResult |
| `src/MantisZip.Core/Engines/SevenZipEngine.cs` | ExtractAsync `for` 包 try-catch，返回 ExtractResult |
| `src/MantisZip.Core/Engines/TarGzEngine.cs` | ExtractAsync 逐条目 try-catch，返回 ExtractResult |
| `src/MantisZip.UI/AppPartials/App.Extract.cs` | 根据 ExtractResult 处理部分成功 |
| `src/MantisZip.UI/AppPartials/App.Password.cs` | 适配新返回值（如用到） |
| `src/MantisZip.UI/AppPartials/App.Elevation.cs` | 不动 |
| `src/MantisZip.UI/Dialogs/Elevation*.xaml/.cs` | 不动 |
| `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` | 适配 ExtractResult 返回值 |
| `tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs` | 同上 |
| `tests/MantisZip.Tests/Engines/TarGzEngineTests.cs` | 同上 |

## 实施步骤

### Step 1：新增 ExtractResult + 改接口

**文件**: `src/MantisZip.Core/Abstractions/ArchiveEngine.cs`

新增类：
```csharp
public class ExtractResult
{
    public int SucceededEntries { get; init; }
    public int FailedEntries { get; init; }
    public bool HasFailures => FailedEntries > 0;
}
```

改接口：
```csharp
Task<ExtractResult> ExtractAsync(...);
```

### Step 2：ZipEngine 改逐条目 try-catch

**文件**: `src/MantisZip.Core/Engines/ZipEngine.cs`

`foreach` 内的文件写入逻辑包 try-catch：

```csharp
int failedEntries = 0;
foreach (var entry in allEntries)
{
    try
    {
        // 现有提取逻辑
    }
    catch (UnauthorizedAccessException uax)
    {
        CoreLog.Info($"ExtractAsync: permission denied for '{entryKey}': {uax.Message}");
        failedEntries++;
        processedBytes += entrySize; // 进度继续推进
    }
}
// 返回 new ExtractResult { SucceededEntries = processedFiles, FailedEntries = failedEntries };
```

### Step 3：SevenZipEngine 改逐条目 try-catch

**文件**: `src\MantisZip.Core/Engines/SevenZipEngine.cs`

`for` 循环内的 `ExtractFile` 包 try-catch（同 ZipEngine 模式）。

### Step 4：TarGzEngine 改逐条目 try-catch

**文件**: `src\MantisZip.Core/Engines/TarGzEngine.cs`

`IReader` 逐条处理循环包 try-catch。

### Step 5：App.Extract.cs 适配返回值

**文件**: `src/MantisZip.UI/AppPartials/App.Extract.cs`

`await engine.ExtractAsync(...)` 后检查返回值：
- `result.HasFailures && result.FailedEntries == result.TotalEntries` → 全部失败，走 failed++
- `result.HasFailures` → 部分成功，still succeeded++ but log warning
- `!result.HasFailures` → 全成功，succeeded++

### Step 6：测试适配

**文件**: 3 个测试文件

Mock/真实调用 `ExtractAsync` 的地方适配 `ExtractResult` 返回值。

## 注意事项

- 不加 `List<string> FailedEntryNames` 到 ExtractResult（避免内存开销和 UI 联动复杂度）
- 不改变非权限异常的抛出行为（压缩包损坏等仍然抛异常）
- `processedBytes` 和 `processedFiles` 在跳过失败条目时仍然更新（进度平滑）
- 7z solid 归档：`ExtractFile(index, stream)` 单个失败不影响后续条目（SharpSevenZip 内部已支持）