# 拖拽直接解压 — 放弃 CF_HDROP，Drop 后检测目标窗口路径直接提取

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜] (0/8)
> **分支**: `avalonia-port`

---

## TL;DR

彻底放弃 WPF OLE 那套 `CF_HDROP` + `IDataObject` 延迟渲染方案。在 Avalonia 分支上，用 `WindowFromPoint` + `ShellWindows` COM 检测拖拽松手后的鼠标位置所对应的文件系统路径，直接将压缩包内容解压到该目录。不需要临时文件，不需要操心 Explorer 的 OLE bridge bug。

**核心流程**：
```
用户按住拖拽 → DoDragDropAsync(自定义格式) 立即响应
    → 拖拽途中（光标可能显示"禁止"）
    → 用户松手 → DoDragDropAsync 返回
    → 获取鼠标位置 (GetCursorPos)
    → WindowFromPoint → ShellWindows COM → 取得 Explorer 路径
    → 如果是桌面路径 / 检测失败 → 弹文件夹选择对话框
    → ProgressWindow 显示解压进度
    → 直接提取到目标目录
    → 完成
```

**涉及文件**（全部在 `src/MantisZip.UI.Avalonia/` 下）:

| 文件 | 操作 |
|------|------|
| `Services/DropTargetDetector.cs` | 🆕 新增 — 目标路径检测 |
| `Services/DragDropService.cs` | 🆕 新增 — 拖拽编排 |
| `Services/DragDropItemExpander.cs` | 🆕 新增 — 多选展开 + 路径裁剪 |
| `Views/MainWindow.axaml.cs` | 🔧 修改 — 替换现有拖拽代码 |
| `ViewModels/MainWindowViewModel.cs` | 🔧 修改 — 补充拖拽相关状态 |
| `Models/ArchiveItemModel.cs` | 🔧 修改 — 补充 ToCoreItem 转换 |
| `MantisZip.UI.Avalonia.csproj` | 🔧 修改 — ADD SHDocVw COM 引用 |

**运行时依赖变化**: 新增 SHDocVw COM 引用（Windows 内置，无需分发）

---

## 决策记录（用户确认）

| 决策 | 选择 |
|------|------|
| 拖拽光标策略 | 自定义视觉 + 不可识别格式（不传临时文件） |
| Explorer 路径检测 | SHDocVw COM 引用 |
| 多选拖拽 | 支持（移植 `ExpandDragItems` + `GetDragExtractPath`） |
| 文件冲突处理 | 复用 `AppSettings.FileConflictAction` |
| 进度展示 | ProgressWindow（用户确认偏好这个体验） |

---

## 设计

### 架构总览

```
MainWindow.axaml.cs
    │ PointerPressed → 记录起始点 + 选中项
    │ PointerMoved → 超过阈值启动 DoDragDropAsync
    │ DoDragDropAsync(自定义 DataTransfer) → 立即返回
    └──────────────────────────────────────────────┐
                                                   ▼
DragDropService.DetectAndExtractAsync()
    │ 1. GetCursorPos() → 获取松手时的鼠标坐标
    │ 2. WindowFromPoint() → 获取 hWnd
    │ 3. DropTargetDetector.GetPathFromHwnd(hWnd)
    │    ├─ ShellWindows COM → Explorer 路径
    │    ├─ 桌面 (Progman/WorkerW) → 桌面路径
    │    └─ 其他/失败 → 弹 OpenFolderDialog
    │ 4. 如果有多个文件→展开目录
    │ 5. ProgressWindow 显示解压进度
    │ 6. ArchiveEntryExtractor.ExtractEntryAsync → 逐个提取到目标
    │ 7. 完成 → 打开目标文件夹
    └──────────────────────────────────────────────┘
```

### DropTargetDetector 内部

```
DropTargetDetector.GetPathFromHwnd(hWnd)
    │
    ├─ GetClassName → "Progman" / "WorkerW"
    │   └─ return DesktopPath
    │
    ├─ ShellWindows 枚举
    │   ├─ ie.HWND == hWnd → IShellFolderViewDual
    │   │   └─ Folder.Self.Path → return path
    │   └─ 无匹配 → continue
    │
    └─ 全部失败 → return null → 弹选择文件夹对话框
```

### DragDropService 状态机

```
Idle ──→ Dragging ──→ Detecting ──→ Extracting ──→ Done
                         │               │
                         ↓               ↓
                      DetectTarget   ProgressWindow
                      (WindowFrom    (提取到目标)
                       Point+COM)
```

### 多选 + 路径展开逻辑

移植自 WPF `MainWindow.DragDrop.cs`：

```
选中项 = [文件A, 目录B, 文件C]
    │
    ▼ ExpandDragItems()
展开 = [文件A, 目录B/子文件1, 目录B/子文件2, 文件C]
    │
    ▼ GetDragExtractPath(每个文件, 选中目录, 目标路径)
输出路径 = [目标目录/文件A, 目标目录/子文件1, 目标目录/子文件2, 目标目录/文件C]
```

**路径裁剪规则**（与 WPF v0.3.8 一致）：
- 如果文件在选中的目录内：`selectedDir/child/file.txt` → `目标目录/child/file.txt`
- 如果文件不在选中目录内（独立选中）：`file.txt` → `目标目录/file.txt`

---

## 任务清单

### Task 1: SHDocVw COM 引用 + NativeMethods P/Invoke

**Files:**
- Modify: `MantisZip.UI.Avalonia.csproj` — ADD SHDocVw COM reference
- Create: `Services/NativeMethods.cs` — P/Invoke 声明

**步骤**:

- [ ] **1.1 csproj 添加 SHDocVw COM 引用**

```xml
<!-- 在 MantisZip.UI.Avalonia.csproj 的 <ItemGroup> 中添加 -->
<ItemGroup>
  <COMReference Include="SHDocVw">
    <Guid>{EAB22AC0-30C1-11CF-A7EB-0000C05BAE0B}</Guid>
    <VersionMajor>1</VersionMajor>
    <VersionMinor>1</VersionMinor>
    <Lcid>0</Lcid>
    <WrapperTool>tlbimp</WrapperTool>
    <Isolated>false</Isolated>
  </COMReference>
</ItemGroup>
```

- [ ] **1.2 创建 P/Invoke 声明文件**

```csharp
// Services/NativeMethods.cs
using System.Runtime.InteropServices;

namespace MantisZip.UI.Avalonia.Services;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT Point);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetClassName(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
}
```

### Task 2: DropTargetDetector — 目标路径检测

**Files:**
- Create: `Services/DropTargetDetector.cs`

- [ ] **2.1 实现 DropTargetDetector**

```csharp
// Services/DropTargetDetector.cs
using System.Text;
using SHDocVw;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 通过鼠标位置检测拖拽目标路径。
/// 支持：Explorer 窗口、桌面、其他（返回 null 由调用方弹对话框）。
/// </summary>
internal static class DropTargetDetector
{
    /// <summary>
    /// 从鼠标当前位置检测目标目录路径。
    /// </summary>
    /// <returns>目录路径，或 null（无法检测）</returns>
    public static string? DetectTargetDirectory()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
            return null;

        var hWnd = NativeMethods.WindowFromPoint(pt);
        if (hWnd == nint.Zero)
            return null;

        // 先检查桌面
        var desktopPath = TryGetDesktopPath(hWnd);
        if (desktopPath != null)
            return desktopPath;

        // 检查 Explorer 窗口
        var explorerPath = TryGetExplorerPath(hWnd);
        if (explorerPath != null)
            return explorerPath;

        return null;
    }

    private static string? TryGetDesktopPath(nint hWnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
        var className = sb.ToString();
        if (className == "Progman" || className == "WorkerW")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
        return null;
    }

    private static string? TryGetExplorerPath(nint hWnd)
    {
        try
        {
            var shellWindows = new ShellWindows();
            foreach (InternetExplorer ie in shellWindows)
            {
                if ((nint)ie.HWND != hWnd)
                    continue;

                // 用 dynamic 避免对 Shell32 互操作程序集的依赖
                dynamic? doc = ie.Document;
                if (doc != null)
                {
                    string? path = doc.Folder?.Self?.Path;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        return path;
                }

                break; // HWND 匹配到了就停止
            }
        }
        catch
        {
            // COM 异常：某些 Explorer 窗口可能无法访问
        }
        return null;
    }
}
```

- [ ] **2.2 简单验证 — dotnet build 通过**

Run: `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj`
Expected: 构建成功（SHDocVw 互操作程序集自动生成）

### Task 3: DragDropItemExpander — 多选展开 + 路径裁剪

**Files:**
- Create: `Services/DragDropItemExpander.cs`

移植自 WPF `MainWindow.DragDrop.cs` 的 `ExpandDragItems` + `GetDragExtractPath`。

- [ ] **3.1 实现 ExpandDragItems**

```csharp
// Services/DragDropItemExpander.cs
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 展开目录选择，计算提取目标路径。
/// 移植自 WPF MainWindow.DragDrop.cs 的 ExpandDragItems + GetDragExtractPath。
/// </summary>
internal static class DragDropItemExpander
{
    /// <summary>
    /// 展开选中项：目录展开为其包含的所有文件（递归），非目录保持不变。
    /// 去重（按 FullPath 排除同名项）。
    /// </summary>
    public static IReadOnlyList<ArchiveItem> ExpandItems(
        IEnumerable<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems)
    {
        var selectedDirs = selectedItems.Where(i => i.IsDirectory)
            .Select(d => d.FullPath.Replace('\\', '/').TrimEnd('/') + "/")
            .ToList();

        var selectedFiles = selectedItems.Where(i => !i.IsDirectory)
            .Select(f => f.FullPath.Replace('\\', '/'))
            .ToHashSet();

        var result = new List<ArchiveItem>();
        var seen = new HashSet<string>();

        foreach (var item in allItems)
        {
            if (item.IsDirectory)
                continue; // 只展开文件

            var normalized = item.FullPath.Replace('\\', '/');

            // 是否在选中的目录内？
            var inSelectedDir = selectedDirs.Any(d => normalized.StartsWith(d, StringComparison.Ordinal));
            // 或者是选中的独立文件？
            var isSelectedFile = selectedFiles.Contains(normalized);

            if (inSelectedDir || isSelectedFile)
            {
                if (seen.Add(normalized))
                    result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// 计算每个文件在目标目录下的输出路径。
    /// 对选中目录内的文件：裁剪父路径，只保留目录名+子结构。
    /// </summary>
    public static string GetExtractPath(
        ArchiveItem item,
        IReadOnlyList<ArchiveItem> selectedDirs,
        string targetDirectory)
    {
        var normalized = item.FullPath.Replace('\\', '/');
        var relative = normalized;

        foreach (var dir in selectedDirs)
        {
            var dirPath = dir.FullPath.Replace('\\', '/').TrimEnd('/');
            var prefix = dirPath + "/";
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                var lastSlash = dirPath.LastIndexOf('/');
                relative = lastSlash >= 0
                    ? normalized[(lastSlash + 1)..]
                    : normalized;
                break;
            }
        }

        // 清理路径防 Zip Slip
        var safePath = SanitizeRelativePath(relative);
        return Path.GetFullPath(Path.Combine(targetDirectory, safePath));
    }

    private static string SanitizeRelativePath(string relativePath)
    {
        // 替换反斜杠，移除空段，防路径穿越
        var parts = relativePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "..")
            .ToArray();
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }
}
```

### Task 4: DragDropService — 拖拽编排

**Files:**
- Create: `Services/DragDropService.cs`

- [ ] **4.1 实现 DragDropService**

```csharp
// Services/DragDropService.cs
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 拖拽后提取编排：检测目标 → 展开目录 → 提取到目标 → 反馈。
/// </summary>
internal class DragDropService
{
    private readonly string _archivePath;
    private readonly ArchiveFormat _format;
    private readonly string? _password;
    private readonly Window _ownerWindow;

    public DragDropService(
        string archivePath,
        ArchiveFormat format,
        string? password,
        Window ownerWindow)
    {
        _archivePath = archivePath;
        _format = format;
        _password = password;
        _ownerWindow = ownerWindow;
    }

    /// <summary>
    /// 拖拽结束后调用：检测目标、提取文件。
    /// </summary>
    /// <param name="selectedItems">用户选中的原始条目（可能含目录）</param>
    /// <param name="allItems">归档全部条目（用于展开目录）</param>
    /// <param name="vm">ViewModel，用于更新状态消息</param>
    public async Task ExecuteAfterDropAsync(
        IReadOnlyList<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems,
        MainWindowViewModel? vm)
    {
        // Step 1: 检测目标路径
        var targetDir = DropTargetDetector.DetectTargetDirectory();

        // Step 1.5: 如果检测失败，弹文件夹选择对话框
        if (targetDir == null)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择解压目标文件夹"
            };
            var result = await dialog.ShowAsync(_ownerWindow);
            if (string.IsNullOrEmpty(result))
                return; // 用户取消
            targetDir = result;
        }

        // Step 2: 展开选中项（目录→文件）
        var itemsToExtract = DragDropItemExpander.ExpandItems(selectedItems, allItems);
        if (itemsToExtract.Count == 0)
            return;

        // Step 3: 读取冲突处理策略
        var conflictAction = AppSettings.Default.FileConflictAction;

        // Step 4: 获取选中目录用于路径裁剪
        var selectedDirs = selectedItems.Where(i => i.IsDirectory).ToList();

        // Step 5: 用进度窗口解压
        var title = $"正在解压到 {Path.GetFileName(targetDir)}...";
        var pw = new ProgressWindow(title);
        pw.InitCancellation();

        try
        {
            pw.Show();

            if (vm != null)
                vm.StatusMessage = title;

            var progress = ProgressViewModel.CreateBackgroundProgress(pw, p => pw.SetProgress(p));

            await Task.Run(async () =>
            {
                int total = itemsToExtract.Count;
                int completed = 0;

                foreach (var item in itemsToExtract)
                {
                    var ct = pw.CancellationToken;
                    ct.ThrowIfCancellationRequested();

                    var outputPath = DragDropItemExpander.GetExtractPath(item, selectedDirs, targetDir);
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    // 冲突处理
                    if (File.Exists(outputPath))
                    {
                        switch (conflictAction)
                        {
                            case FileConflictAction.Skip:
                                continue;
                            case FileConflictAction.Rename:
                                outputPath = GetUniquePath(outputPath);
                                break;
                            case FileConflictAction.Overwrite:
                                File.Delete(outputPath);
                                break;
                            // case Ask: 暂不弹窗，默认覆盖（拖拽场景不宜弹窗中断）
                            default:
                                if (File.Exists(outputPath))
                                    File.Delete(outputPath);
                                break;
                        }
                    }

                    await ArchiveEntryExtractor.ExtractEntryAsync(
                        _archivePath, item.FullPath, outputPath, _format, _password, ct);

                    completed++;
                    var pct = (int)((double)completed / total * 100);
                    progress.Report(new ArchiveProgress
                    {
                        PercentComplete = pct,
                        CurrentFile = item.Name
                    });
                }
            }, pw.CancellationToken);

            if (vm != null)
            {
                vm.StatusMessage = itemsToExtract.Count == 1
                    ? $"已解压到 {targetDir}"
                    : $"已解压 {itemsToExtract.Count} 个文件到 {targetDir}";
            }

            // 可选：完成后打开目标文件夹
            if (AppSettings.Default.OpenFolderAfterExtract)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetDir,
                        UseShellExecute = true
                    });
                }
                catch { /* 静默 */ }
            }
        }
        catch (OperationCanceledException)
        {
            if (vm != null)
                vm.StatusMessage = "拖拽解压已取消";
        }
        catch (Exception ex)
        {
            if (vm != null)
                vm.StatusMessage = $"解压失败: {ex.Message}";
        }
        finally
        {
            pw.Close();
        }
    }

    private static string GetUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 100; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return path; // 兜底
    }
}
```

- [ ] **4.2 确认 using 补全**

需要确认：
- `Process` 命名空间：`using System.Diagnostics;`
- `OpenFolderDialog`：Avalonia 自带 `Avalonia.Controls.OpenFolderDialog`
- `AppSettings.Default`：Avalonia 分支的 `Models/AppSettings.cs`
- `FileConflictAction`：检查 Core 中枚举定义

### Task 5: 修改 MainWindow.axaml.cs — 替换拖拽代码

**Files:**
- Modify: `Views/MainWindow.axaml.cs`

当前 Avalonia 分支的拖拽代码在 `MainWindow.axaml.cs` 的构造函数内（`PointerPressed` + `PointerMoved` 事件处理）。需要改造为：

1. 拖拽时不再急切提取 → 只记录选中项 + 启动 `DoDragDropAsync(自定义格式)`
2. `DoDragDropAsync` 返回后 → 调用 `DragDropService.ExecuteAfterDropAsync`

- [ ] **5.1 移除当前急切提取代码**

找到 `MainWindow.axaml.cs` 中 `PointerMoved` 事件处理内从 `// Create temp directory…` 到 `CleanupDragDropTemp()` 为止的全部代码。（approximate 行范围：当前文件 `PointerMoved` 内部的 `try-catch-finally` 块）

替换为：

```csharp
fileGrid.PointerMoved += async (s, e) =>
{
    if (_dragStartEvent == null) return;

    var pos = e.GetPosition(fileGrid);
    var delta = pos - _dragStartPoint;
    if (Math.Abs(delta.X) < 10 && Math.Abs(delta.Y) < 10)
        return;

    var triggerEvent = _dragStartEvent;
    _dragStartEvent = null; // Prevent re-entry

    var vm2 = DataContext as MainWindowViewModel;
    if (vm2?.SelectedEntry == null) return;
    var archivePath = vm2.CurrentArchivePath;
    if (string.IsNullOrEmpty(archivePath)) return;

    // 保存选中项（支持多选）
    var selectedItems = _dragPreservedSelection ?? new List<ArchiveItem> { vm2.SelectedEntry };
    var allItems = vm2.GetAllRawItems(); // 需要 ViewModel 暴露此方法

    var format = ArchiveFormatHelper.GetFormat(archivePath);
    var password = vm2.GetSessionPassword(archivePath); // 需要 ViewModel 暴露此方法

    // Start drag with custom data format (unrecognized by Explorer)
    var data = new DataTransfer();
    data.Set("MantisZipDragFormat", archivePath);

    _isOwnDrag = true;
    vm2.StatusMessage = "拖拽到 Explorer 或桌面以直接解压";

    await DragDrop.DoDragDropAsync(triggerEvent, data, DragDropEffects.Copy);

    _isOwnDrag = false;
    vm2.StatusMessage = "检测目标位置...";

    // ═══ 核心改造：DoDragDropAsync 返回后，异步检测并解压 ═══
    var service = new DragDropService(archivePath, format, password, this);
    await service.ExecuteAfterDropAsync(selectedItems, allItems, vm2);
};
```

- [ ] **5.2 补充 ViewModel 需要的接口**

在 `MainWindowViewModel.cs` 中添加：

```csharp
// 供 DragDropService 获取归档全部条目
public IReadOnlyList<ArchiveItem> GetAllRawItems()
{
    // Avalonia ViewModel 已有 _allRawItems 但为 private
    // 方案：改为 internal 属性，或添加显式方法
    return _allRawItems ?? Array.Empty<ArchiveItem>();
}

// 供 DragDropService 获取会话密码
public string? GetSessionPassword(string archivePath)
{
    _sessionPasswords.TryGetValue(archivePath, out var pwd);
    return pwd;
}
```

需要同步修改 `_allRawItems` 的可见性（从 `private` 改为 `internal` 或添加 internal 属性）。

- [ ] **5.3 处理多选状态保存**

当前 Avalonia 分支只处理了 `SelectedEntry`（单选）。需要在 `PointerPressed` 时保存 DataGrid 的多选状态，类似 WPF 版本的 `_dragPreservedSelection`。

在 `MainWindow.axaml.cs` 的 `PointerPressed` 中添加：

```csharp
fileGrid.PointerPressed += (s, e) =>
{
    _dragStartPoint = e.GetPosition(fileGrid);
    _dragStartEvent = e;

    // 保存当前多选状态（DataGrid 的 SelectedItems）
    var grid = s as DataGrid;
    if (grid?.SelectedItems != null && grid.SelectedItems.Count > 1)
    {
        _dragPreservedSelection = grid.SelectedItems
            .OfType<ArchiveItemModel>()
            .Select(m => m.ToCoreItem()) // 需要 ArchiveItemModel 有 ToCoreItem 方法
            .ToList();
    }
    else
    {
        _dragPreservedSelection = null;
    }
};
```

- [ ] **5.4 补充 ArchiveItemModel → ArchiveItem 转换**

`ArchiveItemModel` 需要添加一个从 ViewModel 转为 Core `ArchiveItem` 的方法，以便 `DragDropService` 能处理：

```csharp
// Models/ArchiveItemModel.cs 中添加
public ArchiveItem ToCoreItem()
{
    return new ArchiveItem
    {
        FullPath = FullPath,
        Name = Name,
        Size = Size,
        CompressedSize = CompressedSize,
        LastModified = LastModified,
        IsDirectory = IsDirectory,
        IsEncrypted = IsEncrypted
    };
}
```

### Task 6: 清理旧代码 — 移除 temp 目录相关

**Files:**
- Modify: `Views/MainWindow.axaml.cs`

- [ ] **6.1 移除不再需要的字段和方法**

从 `MainWindow.axaml.cs` 中移除：

```csharp
// 移除这些字段：
private bool _isOwnDrag;          // 不再需要（但暂时保留以防 Window_Drop 判断）
private string? _dragDropTempDir; // ⛔ 不再需要
// _dragStartEvent, _dragStartPoint 保留（拖拽启动所需）

// 移除这些方法：
private void CleanupDragDropTemp()  // ⛔ 不再需要
```

保留 `_isOwnDrag`（仍用于 `DragDrop.DropEvent` 防止自我循环），但作用减弱（因为不再传真实文件路径）。

- [ ] **6.2 更新 Window_Drop / DragOver 保护**

当前 `Window_DragOver` 和 `Window_Drop` 使用 `e.DataTransfer.Formats.Contains(DataFormat.File)` 来判断是否接受拖入。新格式是自定义字符串，需要更新判断逻辑，或者保留原样（拖入功能不变——从 Explorer 拖文件到 MantisZip 窗口）。

### Task 7: 拖拽视觉反馈（自定义光标）

**Files:**
- Modify: `Views/MainWindow.axaml.cs`（或新建 `Services/DragVisualService.cs`）

用户选择了"纯自定义视觉+不可识别格式"。由于 Avalonia 的 `DoDragDropAsync` 在 Windows 上走 OLE，自定义格式时 Explorer 会显示"禁止"光标。

- [ ] **7.1 评估 Avalonia 自定义拖拽图像支持**

搜索 Avalonia 文档/源码确认：是否有 API 可以在拖拽时设置自定义光标图像。

```csharp
// 如果 Avalonia 支持，大致模式如下（伪代码）：
// DragDrop.SetDragImage(visual, offset);
// 此 API 在 Avalonia 12.x 中可能不存在，需要验证
```

如果 Avalonia 不支持，则接受"禁止"光标（功能不受影响）：
1. 用户在 `PointerMoved` 中能看到拖拽启动（虽然光标是"禁止"）
2. 松手后立即弹出 ProgressWindow，用户知道操作开始
3. 提取完成后路径打开，结果可见

- [ ] **7.2 添加 OnDragStarted / OnDragCompleted 状态消息**

用户在拖拽期间至少能看到状态栏提示：

```csharp
vm2.StatusMessage = "拖拽到 Explorer 或桌面以直接解压";
// 拖拽结束后（DoDragDropAsync 返回前）状态栏保持此提示
// 返回后立即切换为 "检测目标位置..."
```

### Task 8: 手动测试验证

- [ ] **8.1 场景测试清单**

| 场景 | 预期 |
|------|------|
| 拖拽一个文件到 Explorer 窗口 | 检测到目录 → ProgressWindow → 文件出现在该目录 |
| 拖拽到桌面 | 检测到桌面路径 → 提取到桌面 |
| 拖拽到非 Explorer 区域（如 Chrome） | DropTargetDetector 返回 null → 弹出文件夹选择对话框 |
| 拖拽多个文件（Ctrl+点击多选） | 全部解压到目标目录 |
| 拖拽目录（子文件展开） | 目录内所有文件解压到目标，路径裁剪正确 |
| 拖拽加密文件 | 使用会话密码提取（ViewModel 的 GetSessionPassword） |
| 拖拽到一半按 Esc 取消 | DoDragDropAsync 返回 → StatusMessage 更新，无额外操作 |
| 目标目录已有同名文件 | 根据 AppSettings.FileConflictAction 处理 |
| EnableDragExtract = false | 不启动拖拽 |

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| `ShellWindows` COM 在某些 Windows 版本/Explorer 配置下不工作 | 🟡 | 回退到弹文件夹选择对话框 |
| Avalonia 不支持自定义拖拽图像 | 🟡 | 接受"禁止"光标，功能不受影响，靠状态栏提示 |
| Explorer HWND 在很多子窗口之间怎么匹配 | 🟡 | ShellWindows 的 HWND 匹配顶层窗口；文件名列表可能在子窗口，但 `IShellFolderViewDual` 拿的是当前文件夹路径 |
| 多选时 DataGrid 的 SelectedItems 在拖拽过程中被改变 | 🟡 | PointerPressed 时保存到 `_dragPreservedSelection` |
| 提取大量文件时窗口无响应 | 🟡 | 用了 `Task.Run` + ProgressWindow，UI 线程不阻塞 |
| Process.Start 打开文件夹在某些环境中失败 | 🟢 | try-catch 静默 |
| 用户拖拽后等不及解压完成就切走 | 🟢 | ProgressWindow 在任务完成前一直显示 |

---

## 与现有系统的集成

### 设置项
- `AppSettings.EnableDragExtract` — 控制是否允许从文件列表拖出。取值 false 时 PointerMoved 不启动拖拽（当前 Avalonia 分支已有此逻辑）。
- `AppSettings.FileConflictAction` — 目标文件冲突处理策略。
- `AppSettings.OpenFolderAfterExtract` — 解压完成后是否打开目标文件夹。

### 密码处理
- 用 `_sessionPasswords` 缓存（已在 ViewModel 中），`GetSessionPassword()` 获取。
- 如果密码不对，`ArchiveEntryExtractor.ExtractEntryAsync` 会抛出异常 → 被 catch → 状态栏显示失败信息。

### 拖拽冲突保护
- `_isOwnDrag` 仍然用于 `DragDrop.DropEvent` 防止自我循环（拖出后拖回窗口）。
- 但仍需保留 `Window_DragOver` 和 `Window_Drop` 的正常功能（从外部拖文件到窗口）。

### 文件列表过滤配合
- `ExpandItems` 方法基于 `_allItems`（所有原始条目）展开目录，不受当前 Filter 影响——与 WPF 版本一致。

---

## Definition of Done

- [ ] `DropTargetDetector` 能正确检测 Explorer 窗口路径和桌面路径
- [ ] 检测失败时弹文件夹选择对话框
- [ ] 多选拖拽展开目录，路径裁剪正确（与 WPF v0.3.8 行为一致）
- [ ] 直接解压到目标目录，不走 temp
- [ ] `ProgressWindow` 在解压期间正常显示进度
- [ ] 加密文件使用会话密码解压
- [ ] 文件冲突处理遵守 `AppSettings.FileConflictAction`
- [ ] 拖拽取消（Esc）无残留
- [ ] `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` 通过
- [ ] `dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj` 通过
