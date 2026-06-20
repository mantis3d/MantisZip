# CLI/右键菜单提权（权限不足时自动提权）

> CLI/右键菜单模式（`--extract-here`, `--extract-to-name`, `--extract-smart`, `--compress-quick`, `--compress-separate`, `--compress-combined`）在执行操作前检测目标目录的可写性。若不可写：
> - **默认行为**：仅弹提示框列出权限不足的目录，用户点"确定"后退出（不提升权限）
> - **允许提权时**（设置 → 高级 → 允许提升权限）：弹窗询问用户是否以管理员身份重启

## 涉及文件

| 文件 | 改动 |
|------|------|
| `src/MantisZip.UI/AppPartials/App.Extract.cs` | `HandleExtractBatchCore` 头部插入权限检测 |
| `src/MantisZip.UI/AppPartials/App.Compress.cs` | `RunCompressSeparateBatch` 头部插入检测；`HandleCompressQuick` 头部插入检测 |
| `src/MantisZip.UI/AppPartials/App.Open.cs` | `HandleCompressQuick` 内的检测逻辑 |
| `src/MantisZip.UI/App.cs` 或新建 `App.Elevation.cs` | 新增辅助方法：`IsDirectoryWritable()`, `IsElevated()`, `RelaunchAsAdmin()` |
| `src/MantisZip.UI/AppSettings.cs` | 新增 `AllowElevation` 属性（默认 false） |
| `src/MantisZip.UI/Dialogs/ElevationDialog.xaml` + `.cs` | 提权确认对话框（仅 `AllowElevation=true` 时弹出） |
| `src/MantisZip.UI/Dialogs/ElevationInfoDialog.xaml` + `.cs` | **新建** — 权限不足提示对话框（默认行为，仅 OK 按钮） |
| `src/MantisZip.UI/Dialogs/ElevationFailedDialog.xaml` + `.cs` | 提权后仍不可写时的错误对话框 |
| `src/MantisZip.UI/Dialogs/SettingsWindow.xaml` | 高级标签页新增"权限提升"GroupBox |
| `src/MantisZip.UI/Dialogs/SettingsWindow.xaml.cs` | `LoadSettings` + `SaveSettings` 读写 `AllowElevation` |
| `src/MantisZip.UI/Resources/strings.zh.json` + `en.json` | 新增对话框文本 + 设置项文本 |
| `docs/PLAN.md` | 添加引用条目 |

## 架构

### 双模式执行流程

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
         ├─ IsElevated == true → 日志记录"提权后仍不可写"
         │    弹 ElevationFailedDialog → 进程退出
         │    （让现有错误处理机制接管，不会无限弹窗）
         │
         └─ IsElevated == false → 检查 AppSettings.Instance.AllowElevation
              │
              ├─ AllowElevation == false（默认）
              │    → 弹 ElevationInfoDialog（纯提示，仅"确定"按钮）
              │    → 用户点确定 → Shutdown
              │
              └─ AllowElevation == true
                   → 弹 ElevationDialog
                   ├─ "以管理员身份运行" → RelaunchAsAdmin(当前 args) → 旧进程 Shutdown
                   └─ "取消" → Shutdown
```

### 权限不足提示对话框（默认行为）

**ElevationInfoDialog** — 当 `AllowElevation == false`（默认）时弹出：

```
┌──────────────────────────────────────────┐
│  ⚠ 权限不足                              │
│                                          │
│  以下位置没有写入权限，操作已取消：        │
│                                          │
│  • C:\Program Files\ProtectedApp         │
│  • D:\SystemDir                          │
│                                          │
│  如需自动提升权限，请在                    │
│  设置 → 高级 → 允许提升权限 中开启。      │
│                                          │
│  ┌──────────┐                            │
│  │  确定     │                            │
│  └──────────┘                            │
└──────────────────────────────────────────┘
```

- 只有一个"确定"按钮
- 点确定后 `app.Shutdown()`，进程退出
- 标题："权限不足"
- 消息：列出所有不可写入的目录
- 底部提示：引导用户去设置里开启提权功能
- 主题绑定：全部使用 `Theme_*` 动态资源

### 提权对话框（用户已开启提权时）

**ElevationDialog** — 当 `AllowElevation == true` 时弹出，与原设计方案一致：

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

- 两个按钮：提权 / 取消
- 点提权 → `RelaunchAsAdmin` → 旧进程 Shutdown
- 点取消 → Shutdown

### 提权后仍不可写的情况

**ElevationFailedDialog** — `IsElevated == true` 但 `IsDirectoryWritable()` 仍返回 false：

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

用户点确定后，进程正常退出。

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
    var args = string.Join(" ", originalArgs.Select(a => $"\"{a}\""));

    Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        Arguments = args,
        Verb = "runas",
        UseShellExecute = true,
    });
}
```

注意：`originalArgs` 是来自 `Environment.GetCommandLineArgs().Skip(1)` 的原始参数，保持原样传递。新进程启动后会走完整 `OnStartup` → 重新解析 CLI → 再次进入对应入口 → 再次检测 → 这次已提权通过。

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
    if (unwritable.Count > 0)
    {
        if (IsElevated())
        {
            // 提权后仍然不可写 → 弹专用错误对话框
            ShowElevationFailedDialog(unwritable);
            app.Shutdown();
            return;
        }

        if (!AppSettings.Instance.AllowElevation)
        {
            // 默认行为：仅提示不可写目录
            ShowElevationInfoDialog(unwritable);
            app.Shutdown();
            return;
        }

        // 用户开启了提权设置 → 弹提权对话框
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
    // ===== [新增] 权限检测结束 =====

    // 以下是原有代码（创建 ProgressWindow 等）
    ...
}
```

#### 注入点 2：`RunCompressSeparateBatch`

位置：`App.Compress.cs` 约行 168，在 `progressWindow` 显示后、`Task.Run` 之前。

此处已有 `outputPaths = CompressService.GetOutputPaths(request)`，直接取每个 outputPath 的父目录即可。

```csharp
var outputDirs = outputPaths
    .Select(p => Path.GetDirectoryName(p))
    .Where(d => !string.IsNullOrEmpty(d))
    .Distinct()
    .ToList();

var unwritable = outputDirs.Where(d => !IsDirectoryWritable(d!)).Select(d => d!).ToList();
if (unwritable.Count > 0)
{
    if (IsElevated())
    {
        ShowElevationFailedDialog(unwritable);
        app.Shutdown();
        return;
    }

    if (!AppSettings.Instance.AllowElevation)
    {
        ShowElevationInfoDialog(unwritable);
        app.Shutdown();
        return;
    }

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
```

#### 注入点 3：`HandleCompressQuick`

位置：`App.Open.cs` 约行 44，在 `progressWindow` 显示后、`Task.Run` 之前。

此处 `outputPath` 已计算好（行 78），取 `Path.GetDirectoryName(outputPath)` 检查。检测逻辑同上，但目标目录只有一个。

### 压缩侧：`--compress`（交互模式）的特殊处理

`--compress`（不带 quick/separate/combined 后缀）会弹出 `CompressSettingsWindow`（交互对话框）让用户选择格式/级别/密码等，此时权限问题由用户自己把控（用户选了一个系统目录作为输出是他自己的选择），不做自动检测和提权。

如果用户点了压缩后因为权限失败，现有的错误处理（`ErrorDialog` 重试/跳过/中止）会接管。

### 设置 UI 改动

在 SettingsWindow → **高级**（🧰 标签），临时文件管理 GroupBox 下方新增：

```
┌─ 权限提升 ──────────────────────────────┐
│  ☐ 允许提升权限                          │
│                                          │
│  默认关闭。开启后，CLI 模式下遇目标目录   │
│  无写入权限时，会弹出 UAC 提权窗口         │
│  以管理员身份重试。                       │
└──────────────────────────────────────────┘
```

## AppSettings 新增属性

```csharp
// ===== 高级 =====
public string SevenZipPath { get; set; } = "";
public bool CleanTempOnStartup { get; set; } = true;
/// <summary>CLI 模式下遇到权限不足时，是否弹提权窗口（默认 false = 仅提示不可写目录）</summary>
public bool AllowElevation { get; set; } = false;
```

## 实施步骤

### Step 1：AppSettings 新增属性

**文件**: `src/MantisZip.UI/AppSettings.cs`

在 `// ===== 高级 =====` 区域新增 `AllowElevation` 属性，默认 `false`。

验证：编译通过。

### Step 2：新建工具方法（App.Elevation.cs）

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

### Step 3：新建权限不足提示对话框（ElevationInfoDialog）

**文件**:
- `src/MantisZip.UI/Dialogs/ElevationInfoDialog.xaml`
- `src/MantisZip.UI/Dialogs/ElevationInfoDialog.xaml.cs`

- XAML 接受 `IReadOnlyList<string> UnwritableDirectories` 参数
- 显示不可写入的路径列表
- 底部引导文字："如需自动提升权限，请在设置 → 高级 → 允许提升权限 中开启。"
- 一个按钮：确定
- 标题："权限不足"
- 主题绑定：全部使用 `Theme_*` 动态资源
- `.cs` 公开 `ShowDialog(owner)`，无返回值

验证：能在 ProgressWindow 上模态弹出。

### Step 4：新建提权对话框（ElevationDialog）

**文件**:
- `src/MantisZip.UI/Dialogs/ElevationDialog.xaml`
- `src/MantisZip.UI/Dialogs/ElevationDialog.xaml.cs`

- XAML 接受 `IReadOnlyList<string> UnwritableDirectories` 参数
- 显示不可写入的路径列表（第一行即可，多路径时显示"等 N 个位置"）
- 两个按钮：`ElevateBtn` 和 `CancelBtn`
- 标题："需要管理员权限"
- 主题绑定：全部使用 `Theme_*` 动态资源
- `.cs` 公开 `ShowDialog(owner)`，返回 `bool?`

验证：能在 ProgressWindow 上模态弹出。

### Step 5：新建提权失败对话框（ElevationFailedDialog）

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

### Step 6：解压侧注入（HandleExtractBatchCore）

**文件**: `src/MantisZip.UI/AppPartials/App.Extract.cs`

在 `HandleExtractBatchCore` 开头插入权限检测逻辑：
- 预计算所有目标目录（循环 allPaths，按 mode 计算）
- 调用 `IsDirectoryWritable` 批量检测
- 有不可写 → 检查 `IsElevated()`
  - 已提权 → `ShowElevationFailedDialog` + Shutdown
  - 未提权 → 检查 `AllowElevation`
    - false → `ShowElevationInfoDialog` + Shutdown
    - true → `ShowElevationDialog` → 提权或取消

验证：
1. 编译通过
2. `--extract-here` 到受保护目录（AllowElevation=false）→ 弹出 ElevationInfoDialog，点确定后退出
3. 设置 AllowElevation=true → 弹出 ElevationDialog
4. 选择"取消" → 进程退出
5. 选择"以管理员身份运行" → UAC 弹窗 → 提权后正常执行

### Step 7：压缩侧注入（RunCompressSeparateBatch）

**文件**: `src/MantisZip.UI/AppPartials/App.Compress.cs`

在 `RunCompressSeparateBatch` 中，`outputPaths` 计算完成后（行 194），`Task.Run` 之前插入。检测逻辑同 Step 6。

验证：
1. `--compress-separate` 到受保护目录（AllowElevation=false）→ ElevationInfoDialog
2. AllowElevation=true → ElevationDialog → 提权后正常压缩

### Step 8：压缩侧注入（HandleCompressQuick）

**文件**: `src/MantisZip.UI/AppPartials/App.Open.cs`

在 `HandleCompressQuick` 中，`outputPath` 计算后（行 79），`progressWindow` 显示后，`Task.Run` 之前插入。检测逻辑同上，目标目录只有一个。

验证：`--compress-quick` 到受保护目录 → 根据 AllowElevation 显示对应对话框。

### Step 9：设置 UI 改动

**文件**:
- `src/MantisZip.UI/Dialogs/SettingsWindow.xaml`
- `src/MantisZip.UI/Dialogs/SettingsWindow.xaml.cs`

XAML：在高级标签页的临时文件管理 GroupBox 下方，新增"权限提升" GroupBox，包含 AllowElevationCheck。

代码：`LoadSettings` 中读取 `s.AllowElevation`，`SaveSettings` 中写入 `s.AllowElevation`。

### Step 10：本地化文本

**文件**:
- `src/MantisZip.UI/Resources/strings.zh.json`
- `src/MantisZip.UI/Resources/strings.en.json`

新增键：
```
Settings_Advanced_ElevationGroupHeader = "权限提升"
Settings_Advanced_AllowElevation = "允许提升权限"
Settings_Advanced_AllowElevationHint = "默认关闭。开启后，CLI 模式下遇目标目录无写入权限时，会弹出 UAC 提权窗口以管理员身份重试。"

ElevationInfoDialog_Title = "权限不足"
ElevationInfoDialog_Message = "以下位置没有写入权限，操作已取消："
ElevationInfoDialog_Hint = "如需自动提升权限，请在设置 → 高级 → 允许提升权限 中开启。"
ElevationInfoDialog_Ok = "确定"

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

### Step 11：编译 + 功能验证

```powershell
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
```

完整验证清单：

| # | 场景 | 预期结果 |
|---|------|----------|
| 1 | `--extract-here` 到可写目录 | 不弹窗，正常执行 |
| 2 | `--extract-here` 到受保护目录，AllowElevation=false（默认） | 弹出 ElevationInfoDialog（仅提示），点确定后退出 |
| 3 | 同上，但 AllowElevation=true | 弹出 ElevationDialog（提权/取消） |
| 4 | 场景 3 中点"取消" | 进程退出 |
| 5 | 场景 3 中点"以管理员身份运行" | UAC 弹窗 → 提权后正常解压 |
| 6 | `--extract-to-name` 到受保护目录 | 同 2-5 |
| 7 | `--extract-smart` 到受保护目录 | 同 2-5 |
| 8 | `--compress-quick` 到受保护目录 | 同 2-5 |
| 9 | `--compress-separate` 到受保护目录 | 同 2-5 |
| 10 | `--compress-combined` 到受保护目录 | 同 2-5 |
| 11 | 先以管理员身份运行，再解压到受保护目录 | 不弹窗（已提权），正常执行 |
| 12 | 提权后目录仍不可写（磁盘满/路径不存在） | 弹出 ElevationFailedDialog，点确定后退出 |
| 13 | 正常 UI 模式（双击 exe 打开主窗口）解压 | 不受影响，无变化 |

## 注意事项

- **`UseShellExecute = true` 是 `runas` 的前提条件**：`RelanchAsAdmin` 必须设置此标志，否则 `Verb = "runas"` 会被忽略。
- **原始参数保留**：`Environment.GetCommandLineArgs().Skip(1)` 保留了原始 CLI 参数的原样，直接传递给提权后的新进程。
- **`--extract`（交互模式）不做自动检测**：用户在 `ExtractSettingsWindow` 中主动选择了目标路径，如果选了系统目录应该是用户意愿，不自动干预。
- **提权设置仅在 CLI 模式生效**：`AllowElevation` 只影响 CLI/右键菜单路径。UI 交互模式（--extract/双击主窗口）不受影响。
- **默认安全**：`AllowElevation` 默认 `false`，用户不会意外看到 UAC 弹窗，只会看到温和的提示。
- **Smart 模式的目录检测**：只检查 `Path.GetDirectoryName(archivePath)` 而不调用 `ResolveSmartDestAsync`，避免提前开销。
- **多路径去重**：`directoriesToCheck` 用 `Distinct()` 去重，避免同一个不可写目录被多次弹窗。
- **并发安全**：检测发生在主线程（UI 线程），无并发问题。
