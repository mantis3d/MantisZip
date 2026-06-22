# 拖拽直接解压 — 放弃 CF_HDROP，Drop 后检测目标窗口路径直接提取

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/9)
> **分支**: `avalonia-port`

---

## TL;DR

彻底放弃 WPF OLE 那套 `CF_HDROP` + `IDataObject` 延迟渲染方案。在 Avalonia 分支上，用 `WindowFromPoint` + `ShellWindows` COM 检测拖拽松手后的鼠标位置所对应的文件系统路径，直接将压缩包内容解压到该目录。不需要临时文件，不需要操心 Explorer 的 OLE bridge bug。

**核心流程**：
```
用户按住拖拽 → DoDragDropAsync(自定义格式) 立即响应
    → 拖拽途中：独立 Win32 线程覆盖层实时高亮目标窗口
    → 用户松手 → DoDragDropAsync 返回
    → 获取鼠标位置 (GetCursorPos)
    → WindowFromPoint → ShellWindows COM / UIA → 取得目标路径
    → 如果是桌面路径 / 检测失败 → 弹文件夹选择对话框
    → ProgressWindow 显示解压进度
    → 直接提取到目标目录
    → 完成
```

**涉及文件**（全部在 `src/MantisZip.UI.Avalonia/` 下）:

| 文件 | 操作 |
|------|------|
| `Services/DropTargetDetector.cs` | 🆕 新增 — 目标路径检测（含 UIA 扩展） |
| `Services/DragDropService.cs` | 🆕 新增 — 拖拽编排 |
| `Services/DragDropItemExpander.cs` | 🆕 新增 — 多选展开 + 路径裁剪 |
| `Services/DragOverlayWindow.cs` | 🆕 新增 — Win32 覆盖层窗口（独立线程） |
| `Views/MainWindow.axaml.cs` | 🔧 修改 — 替换现有拖拽代码 |
| `ViewModels/MainWindowViewModel.cs` | 🔧 修改 — 补充拖拽相关状态 |
| `Models/ArchiveItemModel.cs` | 🔧 修改 — 补充 ToCoreItem 转换 |
| `MantisZip.UI.Avalonia.csproj` | 🔧 修改 — ADD SHDocVw COM 引用 |
| `Themes/Light.xaml` | 🔧 修改 — 新增覆盖层颜色键 |
| `Themes/Dark.xaml` | 🔧 修改 — 新增覆盖层颜色键 |

**运行时依赖变化**: 新增 SHDocVw COM 引用（Windows 内置，无需分发）

> **⏳ 9 项 Avalonia 待定决策**：本计划中部分步骤依赖 Avalonia 分支的具体实现
> （ViewModel 结构、DataGrid API、主题颜色值等），需在 Avalonia 移植完成前重新确认。
> 详见末尾「Avalonia 移植待定事项」章节。已在该章节和相关 Task 中用 `[⏳]` 标注。

---

## 决策记录（用户确认）

### 已确认决策

| 决策 | 选择 |
|------|------|
| 拖拽光标策略 | 放弃 Avalonia 自定义光标，采用纯 Win32 独立线程覆盖层 |
| Explorer 路径检测 | SHDocVw COM 引用 + UIA 备用（#32770 对话框） |
| 多选拖拽 | 支持（移植 `ExpandDragItems` + `GetDragExtractPath`） |
| 文件冲突处理 | 复用 `AppSettings.FileConflictAction`（string 类型，非枚举） |
| 进度展示 | ProgressWindow（用户确认偏好这个体验） |
| 视觉反馈 | 绿色=直接解压，红色=需确认，灰色=无效区域 |

### ⏳ 待定决策（需 Avalonia 移植后决定）

| 待定决策 | 依赖项 | 参考位置 |
|---------|--------|---------|
| UIA 库取舍 | Avalonia 项目的依赖策略 & .csproj 构型 | Task 2.1 |
| 对话框/设置类 API | Avalonia 分支的 `ProgressWindow`、`OpenFolderDialog`、`AppSettings` 实现 | Task 4.1 |
| `MainWindowViewModel` 接口签名 | Avalonia 分支的 ViewModel 定义（属性名称、类型） | Task 5.1 ~ 5.2 |
| `_allRawItems` 可见性策略 | Avalonia 分支的封装设计习惯 | Task 5.2 |
| DataGrid `SelectedItems` API 签名 | Avalonia DataGrid 控件的具体 API | Task 5.3 |
| `ArchiveItemModel` 字段定义 | Avalonia 分支的 Model 层设计 | Task 5.4 |
| 拖入保护判断逻辑 | Avalonia 分支的 `DataTransfer.Formats` API | Task 6.2 |
| 覆盖层颜色值 | Avalonia 最终主题系统（`Theme_Status*` 颜色键） | Task 7.2 |
| 所有集成测试场景 | 可运行的 Avalonia 分支 | Task 8 |

| 决策 | 选择 |
|------|------|
| 拖拽光标策略 | 放弃 Avalonia 自定义光标，采用纯 Win32 独立线程覆盖层 |
| Explorer 路径检测 | SHDocVw COM 引用 + UIA 备用（#32770 对话框） |
| 多选拖拽 | 支持（移植 `ExpandDragItems` + `GetDragExtractPath`） |
| 文件冲突处理 | 复用 `AppSettings.FileConflictAction`（string 类型，非枚举） |
| 进度展示 | ProgressWindow（用户确认偏好这个体验） |
| 视觉反馈 | 绿色=直接解压，红色=需确认，灰色=无效区域 |

---

## 设计

### 架构总览

```
MainWindow.axaml.cs
    │ PointerPressed → 记录起始点 + 选中项
    │ PointerMoved → 超过阈值启动 DoDragDropAsync
    │ DoDragDropAsync(自定义 DataTransfer) → 阻塞 UI 线程
    │    ├─ 独立 Win32 线程启动 DragOverlayWindow
    │    │   └─ 实时跟踪鼠标位置 → 高亮目标窗口
    │    │   └─ 颜色状态：绿/红/灰
    │    └─ 用户松手 → DoDragDropAsync 返回
    └──────────────────────────────────────────────┐
                                                   ▼
DragDropService.DetectAndExtractAsync()
    │ 1. 销毁 DragOverlayWindow
    │ 2. GetCursorPos() → 获取松手时的鼠标坐标
    │ 3. DropTargetDetector.GetPathFromHwnd(hWnd)
    │    ├─ ShellWindows COM → Explorer 路径
    │    ├─ UIA → #32770 对话框路径
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
    │   └─ return (DesktopPath, Success)
    │
    ├─ GetClassName → "CabinetWClass"
    │   └─ ShellWindows 枚举
    │       ├─ ie.HWND == hWnd → IShellFolderViewDual
    │       │   └─ Folder.Self.Path → return (path, Success)
    │       └─ 无匹配 → return (null, None)
    │
    ├─ GetClassName → "#32770"
    │   └─ UIA 提取路径
    │       ├─ 找到文件路径控件 → return (path, Warning)
    │       └─ 提取失败 → return (null, Warning)
    │
    └─ 全部失败 → return (null, None)
```

### DragOverlayWindow 状态机

```
Hidden ──→ Showing ──→ Tracking ──→ Hiding ──→ Hidden
              │            │
              ↓            ↓
         淡入动画      实时位置更新
                        ├─ 绿：成功识别 + 取到路径
                        ├─ 红：识别到窗口但取路径失败
                        └─ 灰：无效区域（桌面/任务栏）
```

### DragDropService 状态机

```
Idle ──→ Dragging ──→ Detecting ──→ Extracting ──→ Done
              │            │               │
              ↓            ↓               ↓
         DragOverlay   DropTargetDetector  ProgressWindow
         (Win32线程)   (WindowFromPoint)   (提取到目标)
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

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(nint hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "CreateWindowExW")]
    public static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out RECT pvAttribute, uint cbAttribute);

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint hObject);

    [DllImport("kernel32.dll")]
    public static extern nint GetModuleHandle(string? lpModuleName);

    // 常量
    public const uint WS_EX_LAYERED = 0x80000;
    public const uint WS_EX_TRANSPARENT = 0x20;
    public const uint WS_EX_NOACTIVATE = 0x8000000;
    public const uint WS_EX_TOOLWINDOW = 0x80;
    public const uint WS_POPUP = 0x80000000;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint LWA_ALPHA = 0x2;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly nint HWND_TOPMOST = new nint(-1);
    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
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
/// 拖拽目标检测结果。
/// </summary>
public enum DropTargetStatus
{
    /// <summary>无法识别或无效区域。</summary>
    None,
    /// <summary>成功识别窗口并提取到有效路径。</summary>
    Success,
    /// <summary>识别到窗口但无法提取路径（如另存为对话框）。</summary>
    Warning
}

/// <summary>
/// 通过鼠标位置检测拖拽目标路径。
/// 支持：Explorer 窗口、桌面、#32770 对话框（UIA）。
/// </summary>
internal static class DropTargetDetector
{
    /// <summary>
    /// 从鼠标当前位置检测目标目录路径。
    /// </summary>
    /// <returns>(目录路径, 状态)。路径为 null 表示无法检测。</returns>
    public static (string? Path, DropTargetStatus Status) DetectTargetDirectory()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
            return (null, DropTargetStatus.None);

        var hWnd = NativeMethods.WindowFromPoint(pt);
        if (hWnd == nint.Zero)
            return (null, DropTargetStatus.None);

        // 先检查桌面
        var desktopPath = TryGetDesktopPath(hWnd);
        if (desktopPath != null)
            return (desktopPath, DropTargetStatus.Success);

        // 检查 Explorer 窗口
        var (explorerPath, explorerStatus) = TryGetExplorerPath(hWnd);
        if (explorerPath != null)
            return (explorerPath, explorerStatus);

        return (null, DropTargetStatus.None);
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

    private static (string? Path, DropTargetStatus Status) TryGetExplorerPath(nint hWnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
        var className = sb.ToString();

        // 标准 Explorer 窗口
        if (className == "CabinetWClass")
        {
            try
            {
                var shellWindows = new ShellWindows();
                foreach (InternetExplorer ie in shellWindows)
                {
                    if ((nint)ie.HWND != hWnd)
                        continue;

                    dynamic? doc = ie.Document;
                    if (doc != null)
                    {
                        string? path = doc.Folder?.Self?.Path;
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            return (path, DropTargetStatus.Success);
                    }
                    break;
                }
            }
            catch
            {
                // COM 异常：某些 Explorer 窗口可能无法访问
            }
            return (null, DropTargetStatus.Warning);
        }

        // #32770 对话框（另存为 / 打开）
        if (className == "#32770")
        {
            var path = TryGetDialogPathViaUIA(hWnd);
            return (path, path != null ? DropTargetStatus.Warning : DropTargetStatus.None);
        }

        return (null, DropTargetStatus.None);
    }

    /// <summary>
    /// 通过 UIA 提取 #32770 对话框中的文件路径。
    /// </summary>
    private static string? TryGetDialogPathViaUIA(nint hWnd)
    {
        // [⏳] UIA 库取舍：System.Windows.Automation 是 WPF 库。Avalonia 分支需评估：
        //   1. 是否引入该依赖（过重则降级为 #32770 统一返回 Warning）
        //   2. 或者使用自定义 COM UIA 接口（无外部依赖但实现复杂）
        //   3. 或者通过 Win32 API 枚举子窗口获取 Edit 控件文本（user32.dll 即可）
        // TODO: 实施时根据 Avalonia 分支的依赖策略选择方案
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
        var (targetDir, status) = DropTargetDetector.DetectTargetDirectory();

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

        // Step 3: 读取冲突处理策略（string 类型，非枚举）
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

                    // 冲突处理（AppSettings.FileConflictAction 是 string）
                    if (File.Exists(outputPath))
                    {
                        switch (conflictAction)
                        {
                            case "skip":
                                continue;
                            case "rename":
                                outputPath = GetUniquePath(outputPath);
                                break;
                            case "overwrite":
                                File.Delete(outputPath);
                                break;
                            // case "ask": 拖拽场景不宜弹窗中断，默认覆盖
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

- [ ] **4.2 确认 using 补全 [⏳]**

需要确认：
- `Process` 命名空间：`using System.Diagnostics;`
- `OpenFolderDialog`：[⏳] Avalonia 自带 `Avalonia.Controls.OpenFolderDialog`，但分支可能用不同的对话框方案
- `AppSettings.Default`：[⏳] Avalonia 分支的 `Models/AppSettings.cs`，需确认命名空间和单例模式
- `FileConflictAction`：[⏳] 检查 Core 中枚举定义 —— 当前 `AppSettings` 字段是 string 类型，代码中已做字符串匹配；若分支增加了真正枚举，可切换回枚举

### Task 5: 修改 MainWindow.axaml.cs — 替换拖拽代码

**Files:**
- Modify: `Views/MainWindow.axaml.cs`

当前 Avalonia 分支的拖拽代码在 `MainWindow.axaml.cs` 的构造函数内（`PointerPressed` + `PointerMoved` 事件处理）。需要改造为：

1. 拖拽时不再急切提取 → 只记录选中项 + 启动 `DoDragDropAsync(自定义格式)`
2. `DoDragDropAsync` 返回后 → 调用 `DragDropService.ExecuteAfterDropAsync`

- [ ] **5.1 移除当前急切提取代码 [⏳]**

> [⏳] 以下代码中 `MainWindowViewModel` 的属性名（`SelectedEntry`、`CurrentArchivePath`、`StatusMessage`）
> 以及 `ArchiveFormatHelper`、`DataTransfer` 等 API 均取决于 Avalonia 分支的具体实现。
> 实施时需对照 Avalonia 分支的实际代码调整。

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

    // ═══ 启动 Win32 覆盖层（独立线程，不阻塞 Avalonia UI）═══
    var overlay = new DragOverlayWindow();
    overlay.Show();

    await DragDrop.DoDragDropAsync(triggerEvent, data, DragDropEffects.Copy);

    // ═══ 拖拽结束，销毁覆盖层 ═══
    overlay.Close();
    overlay.Dispose();

    _isOwnDrag = false;
    vm2.StatusMessage = "检测目标位置...";

    // ═══ 核心改造：DoDragDropAsync 返回后，异步检测并解压 ═══
    var service = new DragDropService(archivePath, format, password, this);
    await service.ExecuteAfterDropAsync(selectedItems, allItems, vm2);
};
```

- [ ] **5.2 补充 ViewModel 需要的接口 [⏳]**

> [⏳] 以下接口完全依赖 Avalonia 分支的 ViewModel 架构。
> 需等分支的 `MainWindowViewModel` 定义完成后再实施。
> 重点关注：`_sessionPasswords` 字典是否存在、`_allRawItems` 的命名与类型。

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

- [ ] **5.3 处理多选状态保存 [⏳]**

> [⏳] Avalonia DataGrid 的 `SelectedItems` API 签名可能与 WPF 不同。
> 需确认 Avalonia 分支使用的是什么 DataGrid 实现（内置 DataGrid、第三方、或自定义列表）。
> 如果分支使用的不是 DataGrid，需改为对应控件的多选 API。

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

- [ ] **5.4 补充 ArchiveItemModel → ArchiveItem 转换 [⏳]**

> [⏳] `ArchiveItemModel` 的字段定义取决于 Avalonia 分支的 Model 层设计。
> 需等分支的 `Models/ArchiveItemModel.cs` 确定后再实施 `ToCoreItem()`。
> 注意 Core 的 `ArchiveItem` 可能也在变化（两边的字段可能不对齐）。

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

- [ ] **6.2 更新 Window_Drop / DragOver 保护 [⏳]**

> [⏳] Avalonia 的 `DragEventArgs.DataTransfer` API 与 WPF 的 `DragEventArgs.Data` 完全不同。
> 需等分支的拖入事件处理代码定义好后，再判断是否需要调整。
> 当前 `Window_DragOver` 和 `Window_Drop` 使用 `e.DataTransfer.Formats.Contains(DataFormat.File)` 来判断是否接受拖入。新格式是自定义字符串，需要更新判断逻辑，或者保留原样（拖入功能不变——从 Explorer 拖文件到 MantisZip 窗口）。

### Task 7: 拖拽视觉反馈（Win32 覆盖层）

**Files:**
- Create: `Services/DragOverlayWindow.cs`
- Modify: `Themes/Light.xaml` — 新增覆盖层颜色键
- Modify: `Themes/Dark.xaml` — 新增覆盖层颜色键

> **关键技术决策**：Avalonia 的 `DoDragDropAsync` 在 Windows 上调用 `ole32.dll!DoDragDrop`，该调用**阻塞 UI 线程**（虽然内部运行自己的消息泵，但 Avalonia 控件无法更新）。因此覆盖层必须使用**纯 Win32 窗口 + 独立线程**，不能是 Avalonia 控件。

- [ ] **7.1 实现 DragOverlayWindow（纯 Win32）**

```csharp
// Services/DragOverlayWindow.cs
using System.Runtime.InteropServices;
using System.Threading;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 拖拽目标高亮覆盖层。纯 Win32 实现，独立线程运行，不依赖 Avalonia UI 线程。
/// </summary>
public class DragOverlayWindow : IDisposable
{
    private nint _hwnd = nint.Zero;
    private Thread? _uiThread;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEvent _windowCreated = new(false);
    private readonly ManualResetEvent _windowClosed = new(false);

    // 状态
    private DropTargetStatus _status = DropTargetStatus.None;
    private NativeMethods.RECT _targetRect;
    private string? _targetPath;
    private readonly object _stateLock = new();

    // 动画
    private readonly System.Timers.Timer _animationTimer;
    private double _opacityPhase = 0; // 0~1

    // 性能节流
    private nint _lastHwnd = nint.Zero;
    private readonly Stopwatch _hoverTimer = new();
    private const int HoverThresholdMs = 150;

    public DragOverlayWindow()
    {
        _animationTimer = new System.Timers.Timer(50); // 20fps
        _animationTimer.Elapsed += (_, _) => UpdateAnimation();
    }

    /// <summary>
    /// 在独立线程上创建并显示覆盖层窗口。
    /// </summary>
    public void Show()
    {
        _cts = new CancellationTokenSource();
        _uiThread = new Thread(RunWindowLoop)
        {
            IsBackground = true,
            Name = "DragOverlayThread"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _windowCreated.WaitOne(5000); // 等待窗口创建
    }

    /// <summary>
    /// 关闭覆盖层窗口。
    /// </summary>
    public void Close()
    {
        _cts?.Cancel();
        if (_hwnd != nint.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
        _windowClosed.WaitOne(2000);
    }

    public void Dispose()
    {
        Close();
        _animationTimer?.Dispose();
        _cts?.Dispose();
        _windowCreated?.Dispose();
        _windowClosed?.Dispose();
        _hoverTimer?.Stop();
    }

    private void RunWindowLoop()
    {
        try
        {
            // 注册窗口类
            var hInstance = NativeMethods.GetModuleHandle(null);
            var wndClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate<WndProcDelegate>(WndProc),
                hInstance = hInstance,
                hCursor = nint.Zero,
                hbrBackground = nint.Zero,
                lpszClassName = "MantisZipDragOverlay"
            };
            NativeMethods.RegisterClassEx(ref wndClass);

            // 创建窗口
            _hwnd = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
                "MantisZipDragOverlay",
                "",
                NativeMethods.WS_POPUP,
                0, 0, 0, 0,
                nint.Zero, nint.Zero, hInstance, nint.Zero);

            if (_hwnd == nint.Zero) return;

            // 设置初始透明度
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);

            _windowCreated.Set();
            _animationTimer.Start();

            // 消息循环
            while (!_cts!.IsCancellationRequested)
            {
                if (PeekMessage(out var msg, nint.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                else
                {
                    Thread.Sleep(1);
                }

                // 更新目标检测（性能节流：仅 HWND 变化或停留超过阈值时更新）
                UpdateTargetDetection();
            }
        }
        finally
        {
            _animationTimer?.Stop();
            _windowClosed.Set();
        }
    }

    private void UpdateTargetDetection()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;
        var hWnd = NativeMethods.WindowFromPoint(pt);

        // 性能节流：只有 HWND 变化时才重置计时器
        if (hWnd != _lastHwnd)
        {
            _lastHwnd = hWnd;
            _hoverTimer.Restart();
            return; // 等待停留阈值
        }

        if (_hoverTimer.ElapsedMilliseconds < HoverThresholdMs)
            return;

        // 获取目标窗口边界
        if (!NativeMethods.GetWindowRect(hWnd, out var rect))
            return;

        // 检测路径和状态
        var (path, status) = DropTargetDetector.DetectTargetDirectory();

        lock (_stateLock)
        {
            _targetRect = rect;
            _targetPath = path;
            _status = status;
        }

        // 更新窗口位置和大小
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void UpdateAnimation()
    {
        if (_hwnd == nint.Zero) return;

        _opacityPhase += 0.05;
        if (_opacityPhase > 1) _opacityPhase = 0;

        // 计算呼吸效果：0.15 ~ 0.45
        var breathAlpha = (byte)(40 + Math.Sin(_opacityPhase * Math.PI * 2) * 25);

        // 根据状态选择颜色
        uint borderColor;
        uint fillColor;
        lock (_stateLock)
        {
            switch (_status)
            {
                case DropTargetStatus.Success:
                    borderColor = 0xFF4CAF50; // 绿色
                    fillColor = 0x664CAF50;   // 半透明绿
                    break;
                case DropTargetStatus.Warning:
                    borderColor = 0xFFF44336; // 红色
                    fillColor = 0x66F44336;   // 半透明红
                    break;
                default:
                    borderColor = 0xFF808080; // 灰色
                    fillColor = 0x22808080;   // 极透明灰
                    break;
            }
        }

        // 使用 UpdateLayeredWindow 更新（性能更好，但实现复杂）
        // 简化方案：SetLayeredWindowAttributes 只支持整体透明度
        // 要实现颜色 + 透明度，需要双缓冲位图 + UpdateLayeredWindow
        // 这里先用简化版：SetLayeredWindowAttributes 设置整体透明度
        // TODO: 若性能不足，改用 UpdateLayeredWindow 实现带颜色的半透明

        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, breathAlpha, NativeMethods.LWA_ALPHA);
        NativeMethods.UpdateWindow(_hwnd);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case 0x0002: // WM_DESTROY
                return 0;
            case 0x0083: // WM_NCCALCSIZE
                return 0; // 移除非客户区（无边框）
            case 0x0084: // WM_NCHITTEST
                return -1; // HTTRANSPARENT：鼠标穿透
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // P/Invoke 委托和辅助方法
    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public NativeMethods.POINT pt;
    }
}
```

- [ ] **7.2 主题资源补充 [⏳]**

> [⏳] 以下颜色值（`#4CAF50` / `#F44336` / `#808080`）目前为硬编码。
> 需等 Avalonia 分支的主题系统确定后：
> 1. 确认这些色值是否与 `Theme_StatusSuccess` / `Theme_StatusError` 等已有键对齐
> 2. 在 Light.xaml / Dark.xaml 中添加实际资源
> 3. 如果 Avalonia 分支使用不同的主题机制，需适配

在 `Themes/Light.xaml` 和 `Themes/Dark.xaml` 中新增覆盖层颜色键：

```xml
<!-- Light.xaml / Dark.xaml 新增 -->
<!-- 拖拽覆盖层 -->
<SolidColorBrush x:Key="Theme_DragOverlayBorder" Color="#FF4CAF50"/>
<SolidColorBrush x:Key="Theme_DragOverlayFill" Color="#664CAF50"/>
<SolidColorBrush x:Key="Theme_DragOverlayBorderWarning" Color="#FFF44336"/>
<SolidColorBrush x:Key="Theme_DragOverlayFillWarning" Color="#66F44336"/>
<SolidColorBrush x:Key="Theme_DragOverlayBorderNone" Color="#FF808080"/>
<SolidColorBrush x:Key="Theme_DragOverlayFillNone" Color="#22808080"/>
```

> **注意**：这些颜色键供代码中硬编码颜色参考，Win32 覆盖层无法直接绑定 Avalonia 资源。颜色值需与主题保持一致。

- [ ] **7.3 Win11 圆角适配**

使用 `DwmGetWindowAttribute` 获取 `DWMWA_EXTENDED_FRAME_BOUNDS`，给覆盖层窗口设置对应的圆角：

```csharp
// 在 UpdateTargetDetection 中，获取目标窗口的 DWM 边界
if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out var dwmRect, (uint)Marshal.SizeOf<NativeMethods.RECT>()) == 0)
{
    // 使用 dwmRect 替代 GetWindowRect 的结果（更精确的视觉边界）
    rect = dwmRect;
}
```

> 注：Win32 窗口的圆角需通过 `SetWindowRgn` 或 `DwmSetWindowAttribute` (`DWMWA_WINDOW_CORNER_PREFERENCE`) 实现。简单方案：不强制圆角，用矩形框+粗边框即可达到清晰视觉效果。

### Task 8: 手动测试验证 [⏳]

> [⏳] 测试验证需要可运行的 Avalonia 应用。
> 以下测试清单中的预期行为是设计目标，但覆盖层颜色响应、DPI 表现、多选交互均需
> 在实际 Avalonia 环境中验证。实施时建议边构建边测试，不必等到所有 Task 完成。

- [ ] **8.1 场景测试清单**

| 场景 | 预期 |
|------|------|
| 拖拽一个文件到 Explorer 窗口 | 覆盖层变绿 → 检测到目录 → ProgressWindow → 文件出现在该目录 |
| 拖拽到桌面 | 覆盖层变绿 → 检测到桌面路径 → 提取到桌面 |
| 拖拽到非 Explorer 区域（如 Chrome） | 覆盖层变灰/隐藏 → DropTargetDetector 返回 null → 弹出文件夹选择对话框 |
| 拖拽到 #32770 对话框 | 覆盖层变红 → 提示需确认 → 弹出文件夹选择对话框 |
| 拖拽多个文件（Ctrl+点击多选） | 全部解压到目标目录 |
| 拖拽目录（子文件展开） | 目录内所有文件解压到目标，路径裁剪正确 |
| 拖拽加密文件 | 使用会话密码提取（ViewModel 的 GetSessionPassword） |
| 拖拽到一半按 Esc 取消 | DoDragDropAsync 返回 → Overlay 销毁 → StatusMessage 更新，无额外操作 |
| 目标目录已有同名文件 | 根据 AppSettings.FileConflictAction 处理 |
| EnableDragExtract = false | 不启动拖拽 |
| 快速划过多个窗口 | 覆盖层不闪烁（150ms 防抖生效） |
| DPI 缩放 150% | 覆盖层大小和位置正确 |

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| `ShellWindows` COM 在某些 Windows 版本/Explorer 配置下不工作 | 🟡 | 回退到弹文件夹选择对话框 |
| **Avalonia `DoDragDropAsync` 阻塞 UI 线程，无法使用 Avalonia 控件做覆盖层** | 🔴 | ✅ 已解决：使用纯 Win32 独立线程覆盖层 |
| Win32 覆盖层窗口生命周期管理（创建/销毁/泄漏） | 🟡 | 严格的 Dispose 模式 + `CancellationToken` + `ManualResetEvent` 同步 |
| DPI 缩放导致覆盖层位置和大小偏差 | 🟡 | 使用 `GetWindowRect`（物理像素）+ 测试多 DPI 场景 [⏳] |
| 多线程 COM：独立线程需 STA | 🟡 | `Thread.SetApartmentState(ApartmentState.STA)` |
| Explorer HWND 在很多子窗口之间怎么匹配 | 🟡 | ShellWindows 的 HWND 匹配顶层窗口；`WindowFromPoint` 获取的是鼠标下的最上层窗口 |
| 多选时 DataGrid 的 SelectedItems 在拖拽过程中被改变 [⏳] | 🟡 | PointerPressed 时保存到 `_dragPreservedSelection`；但需确认 Avalonia DataGrid 的 `SelectedItems` 行为 |
| 提取大量文件时窗口无响应 | 🟡 | 用了 `Task.Run` + ProgressWindow，UI 线程不阻塞 |
| Process.Start 打开文件夹在某些环境中失败 | 🟢 | try-catch 静默 |
| 用户拖拽后等不及解压完成就切走 | 🟢 | ProgressWindow 在任务完成前一直显示 |
| UIA 提取 #32770 对话框路径实现复杂 [⏳] | 🟡 | 可降级：#32770 统一返回 Warning 状态，不提取具体路径 |
| `MainWindowViewModel` 接口与分支实现不一致 [⏳] | 🟡 | 实施时对照分支代码逐项对齐，必要时调整本计划代码 |

---

## 与现有系统的集成

### 设置项
- `AppSettings.EnableDragExtract` — 控制是否允许从文件列表拖出。取值 false 时 PointerMoved 不启动拖拽（当前 Avalonia 分支已有此逻辑）。
- `AppSettings.FileConflictAction` — 目标文件冲突处理策略（**string 类型**，取值 `"ask"` / `"overwrite"` / `"rename"` / `"skip"`）。
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

## ⏳ Avalonia 移植待定事项

以下事项需在 Avalonia 分支移植到一定阶段后才能最终决定。按依赖类型分组：

### 类型 A：依赖 ViewModel / Model 架构

| 事项 | 依赖 | 解决方式 |
|------|------|---------|
| `MainWindowViewModel` 接口签名（Task 5.1 ~ 5.2） | `SelectedEntry`、`CurrentArchivePath`、`StatusMessage`、`_sessionPasswords` 等属性存在且命名一致 | 实施时对照分支代码逐项对齐，必要时调整本计划的代码 |
| `_allRawItems` 可见性（Task 5.2） | ViewModel 中已有 `_allRawItems` 但为 `private` | 改为 `internal` 或新增 `GetAllRawItems()` 方法 |
| `ArchiveItemModel` 字段定义（Task 5.4） | Model 层 `ArchiveItemModel` 的属性集 | 等 Model 定义稳定后实现 `ToCoreItem()` 映射 |

### 类型 B：依赖控件 API

| 事项 | 依赖 | 解决方式 |
|------|------|---------|
| DataGrid `SelectedItems` API（Task 5.3） | Avalonia 分支使用的 DataGrid 实现 | 确认 `SelectedItems` 签名，若行为不同则用其他方式（如 CheckBox 选择模式） |
| `DragEventArgs.DataTransfer` API（Task 6.2） | Avalonia 拖入事件处理代码 | 实施时根据分支的实际 `DragOver` / `Drop` 代码调整保护逻辑 |

### 类型 C：依赖依赖库决策

| 事项 | 依赖 | 解决方式 |
|------|------|---------|
| UIA 库取舍（Task 2.1） | Avalonia 项目是否接受 WPF 的 `System.Windows.Automation` 依赖 | 若不可接受，降级为 #32770 统一返回 Warning；或使用 `EnumChildWindows` 遍历子窗口获取路径 |
| `System.Diagnostics.Stopwatch`（Task 7.1） | 已内置无需额外依赖 | 注意实际代码中确认 `using System.Diagnostics;` |

### 类型 D：依赖最终视觉设计

| 事项 | 依赖 | 解决方式 |
|------|------|---------|
| 覆盖层颜色值（Task 7.2） | `Light.xaml` / `Dark.xaml` 中的 `Theme_StatusSuccess` / `Theme_StatusError` 等颜色键 | 实施时对照主题文件选取与分支风格一致的色值 |
| 圆角策略（Task 7.3） | `DwmGetWindowAttribute` 在 Windows 11 上的一致性 | 测试 Win10 + Win11，若圆角效果不稳定则回退到纯矩形边框 |

### 类型 E：依赖完整运行环境

| 事项 | 依赖 | 解决方式 |
|------|------|---------|
| 全部测试验证（Task 8） | 可运行的 Avalonia 分支 | 边构建边测试，建议按 Task 顺序逐步验证 |
| `dotnet build` / `dotnet test` | `MantisZip.UI.Avalonia.csproj` 及测试项目 | 作为 CI 验收标准 |

---

## Definition of Done

- [ ] `DropTargetDetector` 能正确检测 Explorer 窗口路径和桌面路径
- [ ] 检测失败时弹文件夹选择对话框 [⏳]
- [ ] 多选拖拽展开目录，路径裁剪正确（与 WPF v0.3.8 行为一致）
- [ ] 直接解压到目标目录，不走 temp
- [ ] `ProgressWindow` 在解压期间正常显示进度 [⏳]
- [ ] 加密文件使用会话密码解压 [⏳]
- [ ] 文件冲突处理遵守 `AppSettings.FileConflictAction` [⏳]
- [ ] 拖拽取消（Esc）无残留
- [ ] `DragOverlayWindow` 在拖拽期间正确显示绿/红/灰状态
- [ ] 覆盖层不遮挡目标窗口内容（鼠标穿透 + 低透明度）
- [ ] DragOverlayWindow 的动画不影响性能（20fps + 50ms 定时器足够低消耗）
- [ ] `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` 通过 [⏳]
- [ ] `dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj` 通过 [⏳]
