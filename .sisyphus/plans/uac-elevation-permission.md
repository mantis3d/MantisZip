# CLI/右键菜单提权（权限不足时自动提权）

> CLI/右键菜单模式（`--extract-here`, `--extract-to-name`, `--extract-smart`, `--compress-quick`, `--compress-separate`, `--compress-combined`）在执行操作前检测目标目录的可写性；若不可写且当前进程未提权，弹窗询问用户是否以管理员身份重启。

## 涉及文件

| 文件 | 改动 |
|------|------|
| `src/MantisZip.UI/AppPartials/App.Extract.cs` | `HandleExtractBatchCore` 头部插入权限检测 + 提权弹窗 |
| `src/MantisZip.UI/AppPartials/App.Compress.cs` | `RunCompressSeparateBatch` 头部插入检测；`HandleCompressQuick` 头部插入检测 |
| `src/MantisZip.UI/AppPartials/App.Open.cs` | `HandleCompressQuick` 内的检测逻辑 |
| `src/MantisZip.UI/App.cs` 或新建 `App.Elevation.cs` | 新增辅助方法：`IsDirectoryWritable()`, `IsElevated()`, `RelaunchAsAdmin()` |
| `src/MantisZip.UI/Dialogs/ElevationDialog.xaml` + `.cs` | 提权确认对话框 |
| `src/MantisZip.UI/Localization/strings.zh.json` + `en.json` | 提权对话框文本 |
| `docs/PLAN.md` | 添加引用条目 |
| `docs/PROGRESS.md` | 完成时记录 |

## 架构

### 执行流程

```
CLI 入口（HandleExtractBatchCore / RunCompressSeparateBatch / HandleCompressQuick）
    │
    ├─ 1. 预计算所有输出目录（归档路径 → 目标路径）
    ├─ 2. 逐个检查可写性（IsDirectoryWritable）
    │
    ├─ 全部可写 → 正常执行（现有流程）
    │
    └─ 有不可写的 → 检查 IsElevated()
         │
         ├─ IsElevated == true → 日志记录"提权后仍不可写"，继续执行
         │    （让现有错误处理机制接管，不会无限弹窗）
         │
         └─ IsElevated == false → 弹 ElevationDialog
              ├─ "以管理员身份运行" → RelaunchAsAdmin(当前完整 args) → 旧进程 Shutdown
              └─ "取消" → Shutdown
```

### 提权后仍不可写的情况

如果提权后 `IsDirectoryWritable()` 仍然返回 false，说明问题不是权限不足，而是：

- 路径不存在且无法创建
- 磁盘已满
- 文件被其他进程锁定
- 路径格式非法
- 网络路径不可用
- 实际 ACL 策略拒绝（极少见，如域控下管理员也被禁止）

此时不再弹提权对话框（避免死循环），改为弹一个**独立的错误对话框**，明确告知用户"即使以管理员身份运行也无法写入"，并列出所有出问题的目录。这样用户不会困惑为什么 UAC 弹了两次，也不会看到通用的"解压失败"错误。

**ElevationFailedDialog**：

```
┌──────────────────────────────────────────┐
│  ❌ 无法写入目标位置                       │
│                                          │
│  即使以管理员身份运行，仍无法写入以下位置：│
│                                          │
│  • C:\Program Files\ProtectedApp         │
│  • D:\CorruptedDir                       │
│                                          │
│  可能原因：磁盘已满、路径不可用、          │
│  文件被占用或目录结构损坏。                │
│                                          │
│  ┌──────────┐                            │
│  │  确定     │                            │
│  └──────────┘                            │
└──────────────────────────────────────────┘
```

用户点确定后，进程正常退出（或由调用方继续处理其他可写的归档）。

### 可写性检测

```csharp
private static bool IsDirectoryWritable(string dirPath)
{
    try
    {
        if (!Directory.Exists(dirPath))
            Directory.CreateDirectory(dirPath); // 尝试创建

        var testFile = Path.Combine(dirPath, Path.GetRandomFileName());
        using (var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
        return true;
    }
    catch (UnauthorizedAccessException) { return false; }
    catch (IOException) { return false; }
}
```

说明：
- `FileOptions.DeleteOnClose` 确保测试文件在句柄关闭时自动删除，不残留
- 先尝试 `CreateDirectory`（目标目录可能不存在，需要先创建权限）
- 只捕获 IO 相关异常，其他异常继续抛出（避免吞掉真正的问题）

### 提权检测

```csharp
using System.Security.Principal;

private static bool IsElevated()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}
```

说明：
- UAC 抬高前（split token 的 filtered 状态）：`IsInRole(Administrator)` 返回 false
- "以管理员身份运行"后：返回 true
- 精确匹配"是否需要提权"这个判断

### 重启提权

```csharp
private static void RelaunchAsAdmin(string[] originalArgs)
{
    var exePath = Process.GetCurrentProcess().MainModule.FileName;
    // 保留原始 CLI 参数（例如 --extract-here "C:\a.7z" "C:\b.7z"）
    var args = string.Join(" ", originalArgs.Select(a => $"\"{a}\""));

    Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        Arguments = args,
        Verb = "runas",
        UseShellExecute = true,  // runas 要求 UseShellExecute=true
    });
}
```

注意：`originalArgs` 是来自 `Environment.GetCommandLineArgs().Skip(1)` 的原始参数，保持原样传递。新进程启动后会走完整 `OnStartup` → 重新解析 CLI → 再次进入对应入口 → 再次检测 → 这次已提权通过。

### 对话框设计

**ElevationDialog.xaml** — 参照已有 `ErrorDialog` 风格：

```
┌──────────────────────────────────────────┐
│  🔒 需要管理员权限                        │
│                                          │
│  目标位置需要管理员权限才能写入：          │
│  C:\Program Files\SomeApp                │
│                                          │
│  是否以管理员身份重新运行 MantisZip？      │
│  重新运行后自动执行当前操作。              │
│                                          │
│  ┌──────────────────┐ ┌──────────┐       │
│  │ 以管理员身份运行   │ │  取消    │       │
│  └──────────────────┘ └──────────┘       │
└──────────────────────────────────────────┘
```

- 窗口标题：`ElevationDialog`
- 属主窗口：ProgressWindow（CLI 模式下的主窗口）
- 返回值：`true`（提权）/ `false`（取消）
- 主题色：绑定 `DynamicResource Theme_*`，遵循项目主题规范

### 目标目录计算

#### 解压侧（HandleExtractBatchCore 内）

| mode | 目标目录 | 计算方式 |
|------|----------|----------|
| `here` | `Path.GetDirectoryName(archivePath)` | 纯路径计算 |
| `toname` | `Path.Combine(Path.GetDirectoryName(archivePath), Path.GetFileNameWithoutExtension(archivePath))` | 纯路径计算 |
| `manual` | `manualDest` | 从上一步传过来的用户指定路径 |
| `smart` | `Path.GetDirectoryName(archivePath)`（不做解包分析，只检查父目录） | 纯路径计算 |

说明：smart 模式不调用 `ResolveSmartDestAsync` 做提前分析，因为 smart 的目标要么是 `dir`（直接解压），要么是 `dir/name`（解压到子目录），两种情况都只需要确保父目录可写。避免在进度窗口启动前额外读包。

#### 压缩侧

| 入口 | 输出目录 | 计算方式 |
|------|----------|----------|
| `HandleCompressQuick` | `Path.GetDirectoryName(outputPath)` | 从 `CompressService.ComputeSeparateOutputPath` 或路径拼接得到 |
| `RunCompressSeparateBatch` | 每个源文件对应的 `GetOutputPaths()` 结果的父目录 | 调用 `CompressService.GetOutputPaths(request)` 后取每个路径的父目录 |
| `HandleCompressCombined` / `RunCompressCombined` | 输出路径（单个）的父目录 | 同上 |

### 注入点详情

#### 注入点 1：`HandleExtractBatchCore`

位置：`App.Extract.cs` 约行 296，方法体开头，在 `new ProgressWindow()` / `progressWindow.Show()` 之后、`Task.Run` 之前。

伪代码：
```csharp
private static void HandleExtractBatchCore(List<string> allPaths, string mode, Application app, string? manualDest)
{
    // ===== [新增] 权限检测开始 =====
    var directoriesToCheck = new List<string>();
    foreach (var archivePath in allPaths)
    {
        var dir = mode switch
        {
            "here" => Path.GetDirectoryName(archivePath),
            "toname" => Path.Combine(
                Path.GetDirectoryName(archivePath) ?? "",
                Path.GetFileNameWithoutExtension(archivePath) ?? ""),
            "manual" => manualDest,
            "smart" => Path.GetDirectoryName(archivePath),
            _ => Path.GetDirectoryName(archivePath)
        };
        if (!string.IsNullOrEmpty(dir) && !directoriesToCheck.Contains(dir))
            directoriesToCheck.Add(dir);
    }

    var unwritable = directoriesToCheck.Where(d => !IsDirectoryWritable(d)).ToList();
    if (unwritable.Count > 0 && !IsElevated())
    {
        // 弹提权对话框
        var result = ShowElevationDialog(unwritable);
        if (result == true)
        {
            RelaunchAsAdmin(Environment.GetCommandLineArgs().Skip(1).ToArray());
            app.Shutdown();
            return;
        }
        else
        {
            app.Shutdown();
            return;
        }
    }
    else if (unwritable.Count > 0 && IsElevated())
    {
        // 提权后仍然不可写 → 弹专用错误对话框，列出所有出问题的目录
        ShowElevationFailedDialog(unwritable);
        app.Shutdown();
        return;
    }
    // ===== [新增] 权限检测结束 =====

    // 以下是原有代码（创建 ProgressWindow 等）
    ...
}
```

#### 注入点 2：`RunCompressSeparateBatch`

位置：`App.Compress.cs` 约行 168，在 `progressWindow` 显示后、`Task.Run` 之前。

此处已有 `outputPaths = CompressService.GetOutputPaths(request)`，直接取每个 outputPath 的父目录即可。

#### 注入点 3：`HandleCompressQuick`

位置：`App.Open.cs` 约行 44，在 `progressWindow` 显示后、`Task.Run` 之前。

此处 `outputPath` 已计算好（行 78），取 `Path.GetDirectoryName(outputPath)` 检查。

### 压缩侧：`--compress`（交互模式）的特殊处理

`--compress`（不带 quick/separate/combined 后缀）会弹出 `CompressSettingsWindow`（交互对话框）让用户选择格式/级别/密码等，此时权限问题由用户自己把控（用户选了一个系统目录作为输出是他自己的选择），不做自动检测和提权。

如果用户点了压缩后因为权限失败，现有的错误处理（`ErrorDialog` 重试/跳过/中止）会接管。

## 实施步骤

### Step 1：新建工具方法（`App.Elevation.cs`）

**文件**: `src/MantisZip.UI/AppPartials/App.Elevation.cs`（新建）

包含三个静态私有方法：
- `IsDirectoryWritable(string dirPath) → bool`
- `IsElevated() → bool`
- `RelaunchAsAdmin(string[] originalArgs) → void`

```csharp
namespace MantisZip.UI;

public partial class App : Application
{
    // 方法实现见上方设计
}
```

验证：编译通过。

### Step 2：新建提权对话框（ElevationDialog）

**文件**:
- `src/MantisZip.UI/Dialogs/ElevationDialog.xaml`
- `src/MantisZip.UI/Dialogs/ElevationDialog.xaml.cs`

- XAML 接受 `IReadOnlyList<string> UnwritableDirectories` 参数
- 显示不可写入的路径列表（第一行即可，多路径时显示"等 N 个位置"）
- 两个按钮：`ElevateBtn` 和 `CancelBtn`
- 标题："需要管理员权限"
- 主题绑定：`Background`, `Foreground`, `BorderBrush` 全部使用 `Theme_*` 动态资源
- `.cs` 公开 `ShowDialog(owner)`，返回 `bool?`

验证：能在 `ProgressWindow` 上模态弹出。

### Step 3：新建提权失败对话框（ElevationFailedDialog）

**文件**:
- `src/MantisZip.UI/Dialogs/ElevationFailedDialog.xaml`
- `src/MantisZip.UI/Dialogs/ElevationFailedDialog.xaml.cs`

- XAML 接受 `IReadOnlyList<string> FailedDirectories` 参数
- 显示所有不可写入的目录路径（多行列表，每行一个 `•` 前缀）
- 内容区说明："即使以管理员身份运行，仍无法写入以下位置："
- 可能原因：磁盘已满、路径不可用、文件被占用或目录结构损坏
- 一个按钮：`CloseBtn`（"确定"）
- 标题："无法写入目标位置"
- 主题绑定：所有颜色使用 `Theme_*` 动态资源
- `.cs` 公开 `ShowDialog(owner)`，无返回值

验证：能在 ProgressWindow 上模态弹出。

### Step 4：解压侧注入（`HandleExtractBatchCore`）

**文件**: `src/MantisZip.UI/AppPartials/App.Extract.cs`

在 `HandleExtractBatchCore` 开头插入权限检测逻辑：
- 预计算所有目标目录（循环 allPaths，按 mode 计算）
- 调用 `IsDirectoryWritable` 批量检测
- 有不可写且未提权 → 弹 `ElevationDialog`
- 用户选提权 → `RelaunchAsAdmin` + `Shutdown`
- 用户取消 → `Shutdown`
- 提权后仍不可写 → 记录日志，放行让 try-catch 接管

验证：
1. 编译通过
2. `--extract-here C:\Windows\System32\test.7z`（在非系统目录放一个测试包）→ 应该弹出提权对话框
3. 选择"取消" → 进程退出
4. 选择"以管理员身份运行" → UAC 弹窗 → 提权后正常执行

### Step 5：压缩侧注入（`RunCompressSeparateBatch`）

**文件**: `src/MantisZip.UI/AppPartials/App.Compress.cs`

在 `RunCompressSeparateBatch` 中，`outputPaths` 计算完成后（行 194），`Task.Run` 之前：

```csharp
// 检查输出目录可写性
var outputDirs = outputPaths
    .Select(p => Path.GetDirectoryName(p))
    .Where(d => !string.IsNullOrEmpty(d))
    .Distinct()
    .ToList();

var unwritable = outputDirs.Where(d => !IsDirectoryWritable(d!)).Select(d => d!).ToList();
if (unwritable.Count > 0 && !IsElevated())
{
    var result = ShowElevationDialog(unwritable);
    if (result == true)
    {
        RelaunchAsAdmin(Environment.GetCommandLineArgs().Skip(1).ToArray());
        app.Shutdown();
        return;
    }
    else
    {
        app.Shutdown();
        return;
    }
}
else if (unwritable.Count > 0 && IsElevated())
{
    ShowElevationFailedDialog(unwritable);
    app.Shutdown();
    return;
}
```

验证：
1. `--compress-separate C:\Windows\System32\somefile.log` → 应弹出提权对话框
2. 选定提权后正常压缩

### Step 6：压缩侧注入（`HandleCompressQuick`）

**文件**: `src/MantisZip.UI/AppPartials/App.Open.cs`

在 `HandleCompressQuick` 中，`outputPath` 计算后（行 79），`progressWindow` 显示后，`Task.Run` 之前插入。

检测逻辑同上，但目标目录只有一个（`Path.GetDirectoryName(outputPath)`）。

验证：`--compress-quick C:\Windows\System32\somefile.log` → 弹提权对话框。

### Step 7：本地化文本

**文件**:
- `src/MantisZip.UI/Localization/strings.zh.json`
- `src/MantisZip.UI/Localization/strings.en.json`

新增键：
```
ElevationDialog_Title = "需要管理员权限"
ElevationDialog_Message = "目标位置需要管理员权限才能写入："
ElevationDialog_MultiMessage = "以下 {0} 个位置需要管理员权限才能写入："
ElevationDialog_Elevate = "以管理员身份运行"
ElevationDialog_Cancel = "取消"
ElevationDialog_Hint = "重新运行后自动执行当前操作，无需再次操作。"

ElevationFailedDialog_Title = "无法写入目标位置"
ElevationFailedDialog_Message = "即使以管理员身份运行，仍无法写入以下位置："
ElevationFailedDialog_Reasons = "可能原因：磁盘已满、路径不可用、文件被占用或目录结构损坏。"
ElevationFailedDialog_Ok = "确定"
```

### Step 8：编译 + 功能验证

```powershell
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
```

完整验证清单：

| # | 场景 | 预期结果 |
|---|------|----------|
| 1 | `--extract-here` 到可写目录 | 不弹窗，正常执行 |
| 2 | `--extract-here` 到受保护目录（如 C:\Program Files） | 弹出提权对话框 |
| 3 | 同一场景，点"取消" | 进程退出 |
| 4 | 同一场景，点"以管理员身份运行" | UAC 弹窗 → 提权后正常解压 |
| 5 | `--extract-to-name` 到受保护目录 | 同 2-4 |
| 6 | `--extract-smart` 到受保护目录 | 同 2-4 |
| 7 | `--compress-quick` 到受保护目录 | 弹出提权对话框 |
| 8 | `--compress-separate` 到受保护目录 | 弹出提权对话框 |
| 9 | `--compress-combined` 到受保护目录 | 弹出提权对话框 |
| 10 | 先以管理员身份运行，再解压到受保护目录 | 不弹窗（已提权），正常执行 |
| 11 | 提权后目录仍不可写（磁盘满/路径不存在） | 弹出 ElevationFailedDialog，列出不可写的目录，点确定后退出 |
| 12 | 正常 UI 模式（双击 exe 打开主窗口）解压 | 不受影响，无变化 |

## 注意事项

- **`UseShellExecute = true` 是 `runas` 的前提条件**：`RelanchAsAdmin` 必须设置此标志，否则 `Verb = "runas"` 会被忽略。
- **原始参数保留**：`Environment.GetCommandLineArgs().Skip(1)` 保留了原始 CLI 参数的原样，直接传递给提权后的新进程。
- **`--extract`（交互模式）不做自动检测**：用户在 `ExtractSettingsWindow` 中主动选择了目标路径，如果选了系统目录应该是用户意愿，不自动干预。
- **解压侧 `manual` 模式**：`--extract` → 用户选 Manual + customDest 的场景，customDest 传给 `HandleExtractBatchCore` 的 `manualDest` 参数，在方法内做检测。
- **Smart 模式的目录检测**：只检查 `Path.GetDirectoryName(archivePath)` 而不调用 `ResolveSmartDestAsync`，避免提前开销。
- **多路径去重**：`directoriesToCheck` 用 `Distinct()` 去重，避免同一个不可写目录被多次弹窗。
- **并发安全**：检测发生在主线程（UI 线程），无并发问题。
- **取消后不要残留**：`Shutdown()` 前确保清理资源（日志写入 etc.），当前已有的 `HandleExtractBatchCore` 的 finally 块已覆盖。
