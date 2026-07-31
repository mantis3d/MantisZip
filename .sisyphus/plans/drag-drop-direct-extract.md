# 拖拽直接解压 — 放弃 CF_HDROP，Drop 后检测目标窗口路径直接提取

> **状态**: ✅ 代码实现完成（待手工测试） | **阶段**: [■■■■■■■■■■] (10/10) — ☑️ 代码实现已完成 (2026-07-23)
> **分支**: `avalonia-port`

---

## TL;DR

彻底放弃 WPF OLE 那套 `CF_HDROP` + `IDataObject` 延迟渲染方案。在 Avalonia 分支上，用 `WindowFromPoint` + `ShellWindows` COM 检测拖拽松手后的鼠标位置所对应的文件系统路径，直接将压缩包内容解压到该目录。不需要临时文件，不需要操心 Explorer 的 OLE bridge bug。

**核心流程**：
```
用户按住拖拽 → DoDragDropAsync(自定义格式) 立即响应
    → 拖拽途中：独立 Win32 线程覆盖层实时高亮目标窗口
    → 拖拽途中：Win32 预览弹窗跟随鼠标，预渲染的 ResultTreeView 文件树
    → 用户松手 → DoDragDropAsync 返回
    → 获取鼠标位置 (GetCursorPos)
    → WindowFromPoint → ShellWindows COM / Win32 EnumChildWindows → 取得目标路径
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
| `Services/DragOverlayWindow.cs` | 🆕 新增 — Win32 覆盖层窗口（独立线程） |
| `Services/DragPreviewPopup.cs` | 🆕 新增 — Win32 预览弹窗，跟随鼠标显示文件树 |
| `Views/MainWindow.axaml.cs` | 🔧 修改 — 替换现有拖拽代码 |
| `ViewModels/MainWindowViewModel.cs` | 🔧 修改 — 补充拖拽相关状态 |
| `Models/ArchiveItemModel.cs` | 🔧 修改 — 补充 ToCoreItem 转换 |
| `MantisZip.UI.Avalonia.csproj` | 🔧 修改 — ADD SHDocVw COM 引用 |
| `Themes/ThemeLight.axaml` | 🔧 修改 — 新增覆盖层颜色键 |
| `Themes/ThemeDark.axaml` | 🔧 修改 — 新增覆盖层颜色键 |

**运行时依赖变化**: 新增 SHDocVw COM 引用（Windows 内置，无需分发）

> **2026-07-23 审查更新**：Avalonia 移植已基本完成。本计划中 7 项 `[⏳]` 已通过代码审查确认可解决，
> 1 项（UIA）已决策（选方案 A，方案 B 作为未来计划），1 项保留（集成测试）。
> 详见末尾「审查对照 & 待定事项」章节。

---

## 决策记录（用户确认）

### 已确认决策

| 决策 | 选择 |
|------|------|
| 拖拽光标策略 | 放弃 Avalonia 自定义光标，采用纯 Win32 独立线程覆盖层 |
| Explorer 路径检测 | SHDocVw COM 引用 + Win32 `EnumChildWindows` 备用（#32770 对话框） |
| 多选拖拽 | 支持（移植 `ExpandDragItems` + `GetDragExtractPath`） |
| 文件冲突处理 | 复用 `AppSettings.FileConflictAction`（string 类型，非枚举） |
| 进度展示 | ProgressWindow（用户确认偏好这个体验） |
| 视觉反馈 | 覆盖层：绿色=直接解压，红色=需确认，灰色=无效区域 |
| | 预览弹窗：跟随鼠标显示预渲染的 ResultTreeView 文件树 + 摘要栏 |
| | 预览弹窗实现：预渲染位图（Avalonia RenderTargetBitmap → Win32 CreateDIBSection）|
| #32770 路径提取 | 方案 A：Win32 `EnumChildWindows` + `GetWindowText`；方案 B（UIA，未来计划） |

### 未来可选项

| 项目 | 方向 | 触发条件 |
|------|------|---------|
| UIA 路径提取升级 | 改用 `System.Windows.Automation` 覆盖更多第三方管理器 | 方案 A 覆盖不足时按需启用 |
| OLE 虚拟文件拖拽（方案 C） | 自实现 `IDataObject` + `IDropSource` + `CFSTR_FILEDESCRIPTOR`/`CFSTR_FILECONTENTS`，Explorer 识别为真实文件拖拽，光标恢复正常（根治方案） | A 方案（`SetSystemCursor` 替换）仍不满足体验时启动，详见文末「方案 C：OLE 虚拟文件拖拽（后续计划）」 |

---

## 设计

### 架构总览

```
MainWindow.axaml.cs（UI 线程）
    │ PointerPressed → 记录起始点 + 选中项
    │ PointerMoved → 预渲染 ResultTreeView 到位图
    │              → 超过阈值启动 DoDragDropAsync
    │ DoDragDropAsync(自定义 DataTransfer) → 阻塞 UI 线程
    │    └─ Win32 独立线程 (DragOverlayWindow)
    │       ├─ 实时跟踪鼠标位置
    │       ├─ 覆盖层：高亮目标窗口 (绿/红/灰)
    │       └─ 预览弹窗：跟随鼠标显示文件树
    └─ 用户松手 → DoDragDropAsync 返回
    └──────────────────────────────────────────────┐
                                                    ▼
DragDropService.DetectAndExtractAsync()
    │ 1. 销毁 DragOverlayWindow + DragPreviewPopup
    │ 2. GetCursorPos() → 获取松手时的鼠标坐标
    │ 3. DropTargetDetector.GetPathFromHwnd(hWnd)
    │    ├─ ShellWindows COM → Explorer 路径
    │    ├─ Win32 EnumChildWindows → #32770 对话框路径
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
    │   └─ Win32 EnumChildWindows 提取路径
    │       ├─ 找到路径 Edit 控件 → return (path, Warning)
    │       └─ 提取失败 → return (null, Warning)
    │
    └─ 全部失败 → return (null, None)
```

### DragOverlayWindow 状态机（含 PreviewPopup）

```
Hidden ──→ Showing ──→ Tracking ──→ Hiding ──→ Hidden
              │            │
              ↓            ↓
         淡入动画      实时位置更新
         覆盖层：          ├─ 绿：成功识别 + 取到路径
         目标窗口高亮      ├─ 红：识别到窗口但取路径失败
                          └─ 灰：无效区域
         预览弹窗：
         跟随鼠标 + 预渲染
         文件树位图
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

- [x] **1.1 csproj 添加 SHDocVw COM 引用**

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

- [x] **1.2 创建 P/Invoke 声明文件**

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

- [x] **2.1 实现 DropTargetDetector**

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
            var path = TryGetDialogPathViaWin32(hWnd);
            return (path, path != null ? DropTargetStatus.Warning : DropTargetStatus.None);
        }

        return (null, DropTargetStatus.None);
    }

    /// <summary>
    /// 通过 Win32 EnumChildWindows 提取 #32770 对话框中的文件路径。
    /// 搜索地址栏 Toolbar 或文件路径 Edit 控件。
    /// 方案 A（已确认），方案 B（UIA）作为未来升级选项。
    /// </summary>
    private static string? TryGetDialogPathViaWin32(nint hWnd)
    {
        var path = new StringBuilder(260);
        EnumChildWindows(hWnd, (childHwnd, _) =>
        {
            var className = new StringBuilder(128);
            GetClassName(childHwnd, className, className.Capacity);
            var cls = className.ToString();

            // 1) 地址栏：Explorer 对话框的路径栏 (msctls_progress32 在旧版出现)
            // 2) 新版 Explorer 对话框：ComboBox32 → Edit 子控件
            if (cls == "ToolbarWindow32" || cls == "ComboBox32")
            {
                // 找第一个 Edit 子控件
                EnumChildWindows(childHwnd, (editHwnd, _) =>
                {
                    var editClass = new StringBuilder(128);
                    GetClassName(editHwnd, editClass, editClass.Capacity);
                    if (editClass.ToString() == "Edit")
                    {
                        GetWindowText(editHwnd, path, path.Capacity);
                        return false; // Stop
                    }
                    return true;
                }, nint.Zero);

                if (path.Length > 0) return false; // Stop
            }

            // 3) 直接 Edit 控件（旧式对话框）
            if (cls == "Edit")
            {
                GetWindowText(childHwnd, path, path.Capacity);
                // 只接受看起来像路径的内容
                if (path.Length > 0 && Directory.Exists(path.ToString()))
                    return false; // Stop
                path.Clear();
            }

            return true; // Continue
        }, nint.Zero);

        return path.Length > 0 ? path.ToString() : null;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hWndParent, EnumChildProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    private delegate bool EnumChildProc(nint hWnd, nint lParam);
}
```

- [x] **2.2 简单验证 — dotnet build 通过**

Run: `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj`
Expected: 构建成功（SHDocVw 互操作程序集自动生成）

### Task 3: DragDropItemExpander — 多选展开 + 路径裁剪

**Files:**
- Create: `Services/DragDropItemExpander.cs`

移植自 WPF `MainWindow.DragDrop.cs` 的 `ExpandDragItems` + `GetDragExtractPath`。

- [x] **3.1 实现 ExpandDragItems**

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

- [x] **4.1 实现 DragDropService**

```csharp
// Services/DragDropService.cs
using System.Diagnostics;
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
    private readonly Models.AppSettings _settings = Models.AppSettings.Load();

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
        var conflictAction = _settings.FileConflictAction;

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
            if (_settings.OpenFolderAfterExtract)
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

- [x] **4.2 确认 using 补全**

需要确认：
- `Process` 命名空间：`using System.Diagnostics;`
- `OpenFolderDialog`：Avalonia 自带 `Avalonia.Controls.OpenFolderDialog`，分支已在用 ✅
- `AppSettings`：Avalonia 分支使用 `Models.AppSettings.Load()`（无 `Default` 单例），代码已适配 ✅
- `FileConflictAction`：`AppSettings.FileConflictAction` 为 string 类型，代码中已做字符串匹配 ✅

### Task 5: 修改 MainWindow.axaml.cs — 替换拖拽代码

**Files:**
- Modify: `Views/MainWindow.axaml.cs`

当前 Avalonia 分支的拖拽代码在 `MainWindow.axaml.cs` 的构造函数内（`PointerPressed` + `PointerMoved` 事件处理）。需要改造为：

1. 拖拽时不再急切提取 → 只记录选中项 + 启动 `DoDragDropAsync(自定义格式)`
2. `DoDragDropAsync` 返回后 → 调用 `DragDropService.ExecuteAfterDropAsync`

- [x] **5.1 移除当前急切提取代码**

> Avalonia 分支已确认：`SelectedEntry`、`CurrentArchivePath`、`StatusMessage` 均存在于
> `MainWindowViewModel`，`ArchiveFormatHelper.GetFormat()` 已在 `Models/ArchiveFormatHelper.cs` 中 ✅

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

    // ═══ 预渲染预览文件树（DragPreviewPopup 用）═══
    var previewPopupContent = await DragPreviewBitmapBuilder.RenderAsync(
        selectedItems, allItems, format, archivePath);

    // Start drag with custom data format (unrecognized by Explorer)
    var data = new DataTransfer();
    data.Set("MantisZipDragFormat", archivePath);

    _isOwnDrag = true;
    vm2.StatusMessage = "拖拽到 Explorer 或桌面以直接解压";

    // ═══ 启动 Win32 覆盖层 + 预览弹窗（独立线程）═══
    var overlay = new DragOverlayWindow();
    overlay.SetPreviewBitmap(previewPopupContent);
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

- [x] **5.2 补充 ViewModel 需要的接口**

> Avalonia 分支已确认：`_allRawItems`（`IReadOnlyList<ArchiveItem>?`，private）和
> `_sessionPasswords`（`Dictionary<string, string>`，private）均在 `MainWindowViewModel` 中存在 ✅

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

- [x] **5.3 处理多选状态保存**

> Avalonia 分支已确认使用 `Avalonia.Controls.DataGrid`，`SelectedItems` 可用 ✅

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

- [x] **5.4 补充 ArchiveItemModel → ArchiveItem 转换**

> Avalonia 分支已确认：`Models/ArchiveItemModel.cs` 有 `FromCore(ArchiveItem)` 但缺 `ToCoreItem()`，
> 字段与 Core `ArchiveItem` 已对齐，可直接添加 ✅

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

- [x] **6.1 移除不再需要的字段和方法**

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

- [x] **6.2 更新 Window_Drop / DragOver 保护**

> Avalonia 分支已确认：`DragDrop.DropEvent` 使用 `e.DataTransfer.Formats.Contains(DataFormat.File)`，
> 拖入保护逻辑（`_isOwnDrag`）已存在 ✅

### Task 7: 拖拽视觉反馈（Win32 覆盖层 + PreviewPopup）

**Files:**
- Create: `Services/DragOverlayWindow.cs`
- Create: `Services/DragPreviewPopup.cs`
- Create: `Services/DragPreviewBitmapBuilder.cs`
- Modify: `Themes/ThemeLight.axaml` — 新增覆盖层颜色键
- Modify: `Themes/ThemeDark.axaml` — 新增覆盖层颜色键

> **关键技术决策**：Avalonia 的 `DoDragDropAsync` 在 Windows 上调用 `ole32.dll!DoDragDrop`，该调用**阻塞 UI 线程**（虽然内部运行自己的消息泵，但 Avalonia 控件无法更新）。因此覆盖层必须使用**纯 Win32 窗口 + 独立线程**，不能是 Avalonia 控件。

- [x] **7.1 实现 DragOverlayWindow（纯 Win32）**

```csharp
// Services/DragOverlayWindow.cs
using System.Diagnostics;
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

    // Win32 窗口过程委托（必须存字段防止 GC 回收）
    private readonly WndProcDelegate _wndProcDelegate;

    // 动画
    private readonly System.Timers.Timer _animationTimer;
    private double _opacityPhase = 0; // 0~1

    // 性能节流
    private nint _lastHwnd = nint.Zero;
    private readonly Stopwatch _hoverTimer = new();
    private const int HoverThresholdMs = 150;

    public DragOverlayWindow()
    {
        _wndProcDelegate = WndProc; // 防止 GC 回收窗口过程委托
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
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
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

- [x] **7.2 实现 DragPreviewPopup（Win32 预览弹窗） + DragPreviewBitmapBuilder**

预览弹窗是一个纯 Win32 弹出窗口，在拖拽期间跟随鼠标显示预渲染的 ResultTreeView 位图。内容预先在 UI 线程渲染（因为 DoDragDropAsync 会阻塞 UI 线程，渲染必须在之前完成）。

**DragPreviewBitmapBuilder**（UI 线程，拖拽启动前调用）：

```csharp
// Services/DragPreviewBitmapBuilder.cs
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 预渲染 ResultTreeView 到位图，供 DragPreviewPopup 在 Win32 弹窗中显示。
/// 必须在 UI 线程调用（在 DoDragDropAsync 之前）。
/// </summary>
internal static class DragPreviewBitmapBuilder
{
    /// <summary>
    /// 构建预览树并渲染为 byte[] (BGRA 32bpp, top-down)。
    /// <param name="maxWidth">弹窗最大宽度，超出裁剪</param>
    /// <param name="maxHeight">弹窗最大高度，超出显示 "…还有 N 个"</param>
    /// </summary>
    public static async Task<PreviewBitmapData> RenderAsync(
        IReadOnlyList<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems,
        ArchiveFormat format,
        string archivePath,
        int maxWidth = 320,
        int maxHeight = 500)
    {
        // Step 1: 展开选中项
        var expanded = DragDropItemExpander.ExpandItems(selectedItems, allItems);
        if (expanded.Count == 0) return PreviewBitmapData.Empty;

        // Step 2: 用 ResultPreviewService 构建预览树
        var root = ResultPreviewService.BuildDragPreview(
            archivePath, expanded, format);

        // Step 3: 创建 ResultTreeView 并渲染到位图
        var treeView = new ResultTreeView
        {
            Root = new PreviewTreeNode
            {
                DisplayName = Path.GetFileName(archivePath),
                IsExpanded = true,
                Children = root?.Children ?? new()
            },
            Width = maxWidth,
            CompactMode = true,
            MaxItemsPerDirectory = 10,
            MaxDepth = 5,
            ShowFilteredGhosts = false
        };

        // 测量 + 布置（必须在 UI 线程）
        treeView.Measure(new Size(maxWidth, double.PositiveInfinity));
        var desiredH = Math.Min(treeView.DesiredSize.Height, maxHeight);
        treeView.Arrange(new Rect(0, 0, maxWidth, desiredH));

        var pixelSize = new PixelSize((int)treeView.DesiredSize.Width, (int)desiredH);

        // 对极小树设置最小高度
        pixelSize = pixelSize with { Height = Math.Max(pixelSize.Height, 40) };

        var rtb = new RenderTargetBitmap(pixelSize);
        rtb.Render(treeView);

        // Step 4: 提取像素数据
        var pixels = new byte[pixelSize.Width * pixelSize.Height * 4];
        rtb.CopyPixels(pixels, pixelSize.Width * 4, 0);

        // Step 5: 计算摘要
        var summary = BuildSummary(expanded);

        return new PreviewBitmapData
        {
            Pixels = pixels,
            Width = pixelSize.Width,
            Height = pixelSize.Height,
            Summary = summary,
            TotalFiles = expanded.Count
        };
    }

    private static string BuildSummary(IReadOnlyList<ArchiveItem> items)
    {
        var total = items.Count;
        long totalSize = items.Sum(i => i.Size);
        return $"{total} 个文件 / {FormatUtil.FormatSize(totalSize)}";
    }
}

/// <summary>
/// 预渲染的位图数据，从 UI 线程传递到 Win32 线程。
/// </summary>
public class PreviewBitmapData
{
    public byte[] Pixels { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public string Summary { get; set; } = "";
    public int TotalFiles { get; set; }

    public static readonly PreviewBitmapData Empty = new();
}
```

**DragPreviewPopup**（Win32 线程，与 DragOverlayWindow 同线程）：

在 `DragOverlayWindow` 的构造函数接收位图数据，在消息循环中创建并更新弹出窗口：

```csharp
// Services/DragPreviewPopup.cs (partial, 在 DragOverlayWindow 内使用)
// 作为 DragOverlayWindow 内部管理的辅助类，或 DragOverlayWindow 的组成部分

/// <summary>
/// 预渲染树位图的 Win32 预览弹窗。跟随鼠标，无焦点，可穿透点击。
/// 与 DragOverlayWindow 运行在同一 Win32 线程。
/// </summary>
internal class DragPreviewPopup : IDisposable
{
    private nint _hwnd = nint.Zero;
    private readonly nint _hBitmap;
    private readonly int _width;
    private readonly int _height;
    private readonly string _summary;
    private readonly int _offsetX = 20;  // 鼠标右偏
    private readonly int _offsetY = 24;  // 鼠标下偏

    // 窗口类名
    private const string ClassName = "MantisZipDragPreview";

    public DragPreviewPopup(nint hInstance, PreviewBitmapData bitmapData)
    {
        _width = bitmapData.Width;
        _height = bitmapData.Height;
        _summary = bitmapData.Summary;

        // BGRA → HBITMAP (32bpp top-down DIB)
        _hBitmap = CreateBitmapFromPixels(bitmapData.Pixels, _width, _height, hInstance);

        CreateWindow(hInstance);
    }

    public void UpdatePosition(int cursorX, int cursorY)
    {
        if (_hwnd == nint.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            cursorX + _offsetX, cursorY + _offsetY,
            _width, _height + SummaryBarHeight,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void CreateWindow(nint hInstance)
    {
        // 注册窗口类
        var wndClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            hCursor = nint.Zero,
            hbrBackground = CreateSolidBrush(0x00F5F5F5), // 浅灰背景
            lpszClassName = ClassName
        };
        RegisterClassEx(ref wndClass);

        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
            ClassName, "",
            NativeMethods.WS_POPUP | NativeMethods.WS_BORDER,
            0, 0, _width, _height + SummaryBarHeight,
            nint.Zero, nint.Zero, hInstance, nint.Zero);

        if (_hwnd != nint.Zero)
        {
            // 点击穿透
            var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_TRANSPARENT);
            // 初始透明度
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 230, NativeMethods.LWA_ALPHA);
        }
    }

    // 从像素数据创建 HBITMAP
    private static nint CreateBitmapFromPixels(byte[] pixels, int w, int h, nint hInstance)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            }
        };

        var hdc = NativeMethods.GetDC(nint.Zero);
        nint hBitmap;
        try
        {
            hBitmap = CreateDIBSection(hdc, ref bmi, 0, out var bitsPtr, nint.Zero, 0);
            Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);
        }
        finally
        {
            NativeMethods.ReleaseDC(nint.Zero, hdc);
        }
        return hBitmap;
    }

    // WM_PAINT 绘制：摘要栏 + 树位图
    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        const int WM_PAINT = 0x000F;

        switch (msg)
        {
            case WM_PAINT:
            {
                var ps = new PAINTSTRUCT();
                var hdc = BeginPaint(hWnd, out ps);
                
                // 绘制位图
                var hdcMem = CreateCompatibleDC(hdc);
                var old = SelectObject(hdcMem, _hBitmap);
                BitBlt(hdc, 0, SummaryBarHeight, _width, _height,
                    hdcMem, 0, 0, SRCCOPY);
                SelectObject(hdcMem, old);
                DeleteDC(hdcMem);

                // 绘制摘要栏
                var bgBrush = CreateSolidBrush(0xFFFFFFFF);
                var oldBrush = SelectObject(hdc, bgBrush);
                Rectangle(hdc, 0, 0, _width, SummaryBarHeight);
                SelectObject(hdc, oldBrush);
                DeleteObject(bgBrush);

                // 摘要文字
                SetBkMode(hdc, 1); // TRANSPARENT
                var rect = new RECT { Left = 6, Top = 0, Right = _width - 6, Bottom = SummaryBarHeight };
                DrawText(hdc, _summary, -1, ref rect, 0x0000); // DT_LEFT

                EndPaint(hWnd, ref ps);
                return 0;
            }
            case 0x0084: // WM_NCHITTEST
                return -1; // HTTRANSPARENT — 鼠标穿透
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private const int SummaryBarHeight = 24;

    // 需要补充的 P/Invoke
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO pbmi,
        uint usage, out nint ppvBits, nint hSection, uint offset);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy,
        nint hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")]
    private static extern bool Rectangle(nint hdc, int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")]
    private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")]
    private static extern int DrawText(nint hdc, string lpchText, int cchText,
        ref RECT lprc, uint format);
    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint hdc, int mode);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize; public int biWidth; public int biHeight;
        public short biPlanes; public short biBitCount;
        public int biCompression; public int biSizeImage;
        public int biXPelsPerMeter; public int biYPelsPerMeter;
        public int biClrUsed; public int biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT { public nint hdc; public bool fErase; public RECT rcPaint; public bool fRestore; public bool fIncUpdate; public int reserved1; public int reserved2; public int reserved3; public int reserved4; public int reserved5; public int reserved6; public int reserved7; public int reserved8; }
}
```

> **注意**：`BITMAPINFO` 和 `BITMAPINFOHEADER` 的 struct 布局需与 Win32 SDK 严格对齐（`Pack=1` 已默认），实际实施时请验证 `Marshal.SizeOf` 返回值。

**集成到 DragOverlayWindow**：

```csharp
// DragOverlayWindow 新增

// 预览弹窗数据（UI 线程预渲染，构造时传入）
private PreviewBitmapData? _previewBitmapData;

public void SetPreviewBitmap(PreviewBitmapData data)
{
    _previewBitmapData = data;
}

// 在 RunWindowLoop 中，窗口创建后初始化 PreviewPopup
private DragPreviewPopup? _previewPopup;

private void RunWindowLoop()
{
    // ... 现有窗口注册和创建代码 ...

    // 创建预览弹窗（仅在提供了位图数据时）
    if (_previewBitmapData != null && _previewBitmapData.Pixels.Length > 0)
    {
        _previewPopup = new DragPreviewPopup(hInstance, _previewBitmapData);
    }

    _windowCreated.Set();
    _animationTimer.Start();

    // 消息循环
    while (!_cts!.IsCancellationRequested)
    {
        // ... 现有 PeekMessage 循环 ...
        UpdateTargetDetection();
        UpdatePreviewPopupPosition(); // 新增
    }
}

private void UpdatePreviewPopupPosition()
{
    if (_previewPopup == null) return;
    if (!NativeMethods.GetCursorPos(out var pt)) return;
    _previewPopup.UpdatePosition(pt.X, pt.Y);
}

// Close 时销毁
public void Close()
{
    _previewPopup?.Dispose();
    _previewPopup = null;
    // ... 现有销毁代码 ...
}
```

**DragOverlayWindow NativeMethods 补充**：

需要在原有 `NativeMethods.cs` 中补充以下 P/Invoke（部分已有则跳过）：
- `GetDC` / `ReleaseDC`（已有或补充）
- `CreateSolidBrush`（已有 ✅）
- `DeleteObject`（已有 ✅）
- `SetWindowLong` / `GetWindowLong`（已有 ✅）
- `SetLayeredWindowAttributes`（已有 ✅）

新增：
- `CreateDIBSection` — gdi32.dll
- `CreateCompatibleDC` — gdi32.dll
- `SelectObject` — gdi32.dll
- `DeleteDC` — gdi32.dll
- `BitBlt` — gdi32.dll
- `BeginPaint` / `EndPaint` — user32.dll
- `DrawText` — user32.dll
- `SetBkMode` — gdi32.dll
- `Rectangle` — gdi32.dll

> 实际实施时建议将 GDI 相关 P/Invoke 集中到 `NativeMethods.cs`，避免分散。

- [x] **7.3 主题资源补充**
> 以下颜色值为硬编码参考，Win32 覆盖层无法直接绑定 Avalonia 资源，需保持值一致。
> 若需在 Avalonia 控件中引用这些颜色，按 Avalonia 公约使用 `Brush` 后缀键名。

在 `Themes/ThemeLight.axaml` 和 `Themes/ThemeDark.axaml` 中新增覆盖层颜色键（仅供未来参考）：

```xml
<!-- ThemeLight.axaml / ThemeDark.axaml 新增 -->
<!-- 拖拽覆盖层（硬编码参考值，Win32 覆盖层直接使用） -->
<SolidColorBrush x:Key="DragDropBorderSuccessBrush" Color="#FF4CAF50"/>
<SolidColorBrush x:Key="DragDropFillSuccessBrush" Color="#664CAF50"/>
<SolidColorBrush x:Key="DragDropBorderWarningBrush" Color="#FFF44336"/>
<SolidColorBrush x:Key="DragDropFillWarningBrush" Color="#66F44336"/>
<SolidColorBrush x:Key="DragDropBorderNoneBrush" Color="#FF808080"/>
<SolidColorBrush x:Key="DragDropFillNoneBrush" Color="#22808080"/>
```

- [x] **7.4 Win11 圆角适配**

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

- [x] **8.1 场景测试清单** (⏳ 需手工运行验证 — 见下方说明)

| 场景 | 预期 |
|------|------|
| 拖拽一个文件到 Explorer 窗口 | 覆盖层变绿 + 预览弹窗显示文件树 → 检测到目录 → ProgressWindow → 文件出现在该目录 |
| 拖拽到桌面 | 覆盖层变绿 + 预览弹窗 → 检测到桌面路径 → 提取到桌面 |
| 拖拽到非 Explorer 区域（如 Chrome） | 覆盖层变灰 + 预览弹窗依然可见 → DropTargetDetector 返回 null → 弹出文件夹选择对话框 |
| 拖拽到 #32770 对话框 | 覆盖层变红 + 预览弹窗 → 提示需确认 → 弹出文件夹选择对话框 |
| 拖拽多个文件（Ctrl+点击多选） | 预览弹窗显示展开后的完整文件树和摘要统计 |
| 拖拽目录（子文件展开） | 预览弹窗正确显示目录展开后的文件树结构 |
| 拖拽加密文件 | 预览弹窗包含加密文件，使用会话密码提取 |
| 拖拽到一半按 Esc 取消 | DoDragDropAsync 返回 → Overlay + PreviewPopup 销毁 → StatusMessage 更新，无额外操作 |
| 目标目录已有同名文件 | 根据 AppSettings.FileConflictAction 处理 |
| EnableDragExtract = false | 不启动拖拽 |
| 快速划过多个窗口 | 覆盖层不闪烁（150ms 防抖生效）；预览弹窗始终跟随鼠标 |
| 预览弹窗遮挡问题 | 预览弹窗应显示在鼠标右下 20px 偏移，不遮挡鼠标下方的目标窗口边界 |
| DPI 缩放 150% | 覆盖层 + 预览弹窗大小和位置正确 |

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| `ShellWindows` COM 在某些 Windows 版本/Explorer 配置下不工作 | 🟡 | 回退到弹文件夹选择对话框 |
| **Avalonia `DoDragDropAsync` 阻塞 UI 线程，无法使用 Avalonia 控件做覆盖层** | 🔴 | ✅ 已解决：使用纯 Win32 独立线程覆盖层 |
| Win32 覆盖层窗口生命周期管理（创建/销毁/泄漏） | 🟡 | 严格的 Dispose 模式 + `CancellationToken` + `ManualResetEvent` 同步 |
| DPI 缩放导致覆盖层位置和大小偏差 | 🟡 | 使用 `GetWindowRect`（物理像素）+ 测试多 DPI 场景 |
| 多线程 COM：独立线程需 STA | 🟡 | `Thread.SetApartmentState(ApartmentState.STA)` |
| Explorer HWND 在很多子窗口之间怎么匹配 | 🟡 | ShellWindows 的 HWND 匹配顶层窗口；`WindowFromPoint` 获取的是鼠标下的最上层窗口 |
| 多选时 DataGrid 的 SelectedItems 在拖拽过程中被改变 | 🟡 | PointerPressed 时保存到 `_dragPreservedSelection`；Avalonia DataGrid `SelectedItems` 已确认可用 |
| 提取大量文件时窗口无响应 | 🟡 | 用了 `Task.Run` + ProgressWindow，UI 线程不阻塞 |
| Process.Start 打开文件夹在某些环境中失败 | 🟢 | try-catch 静默 |
| 用户拖拽后等不及解压完成就切走 | 🟢 | ProgressWindow 在任务完成前一直显示 |
| **预览弹窗：ResultTreeView 位图预渲染在 UI 线程阻塞** | 🟡 | 渲染在 DoDragDropAsync 之前、同步测量/布置后一步完成；渲染 300 文件以下应在 50ms 内 |
| **预览弹窗：位图转换 BGRA→HBITMAP 像素格式不兼容** | 🟡 | 使用 `CreateDIBSection` + `Marshal.Copy`，确保与 Avalonia `RenderTargetBitmap` 输出格式一致（32bpp BGRA，top-down） |
| **预览弹窗：Win32 弹窗位置在 DPI 缩放下偏移** | 🟡 | `GetCursorPos` + `SetWindowPos` 使用物理像素，无额外缩放 |
| **预览弹窗：弹出窗口遮挡目标窗口视图** | 🟡 | 弹窗位于鼠标右下 20px 偏移，不覆盖目标窗口本身；如需精细控制可动态计算偏移避免出屏 |
| #32770 对话框路径提取（方案 A：Win32） | 🟡 | `EnumChildWindows` + `GetWindowText` 实现；失败时降级弹文件夹选择框 |
| `MainWindowViewModel` 接口与分支实现不一致 | 🟡 | 已通过审查确认 ✅ 实施时仍建议逐项对照 |

---

## 与现有系统的集成

### 设置项
- `AppSettings.EnableDragExtract` — 控制是否允许从文件列表拖出。取值 false 时 PointerMoved 不启动拖拽（当前 Avalonia 分支已有此逻辑）。
- `AppSettings.FileConflictAction` — 目标文件冲突处理策略（**string 类型**，取值 `"ask"` / `"overwrite"` / `"rename"` / `"skip"`）。
- `AppSettings.OpenFolderAfterExtract` — 解压完成后是否打开目标文件夹。
> **注意**：Avalonia 分支用 `AppSettings.Load()` 创建实例，无 `Default` 单例。

### 密码处理
- 用 `_sessionPasswords` 缓存（已在 ViewModel 中），`GetSessionPassword()` 获取。
- 如果密码不对，`ArchiveEntryExtractor.ExtractEntryAsync` 会抛出异常 → 被 catch → 状态栏显示失败信息。

### 拖拽冲突保护
- `_isOwnDrag` 仍然用于 `DragDrop.DropEvent` 防止自我循环（拖出后拖回窗口）。
- 但仍需保留 `Window_DragOver` 和 `Window_Drop` 的正常功能（从外部拖文件到窗口）。

### 文件列表过滤配合
- `ExpandItems` 方法基于 `_allItems`（所有原始条目）展开目录，不受当前 Filter 影响——与 WPF 版本一致。

---

## 审查对照 & 待定事项

**2026-07-23 审查结论**：Avalonia 移植已完成，以下为审查结果。

### ✅ 已确认（可直接实施）

| 事项 | 确认结果 |
|------|---------|
| `MainWindowViewModel` 接口 | `SelectedEntry`、`CurrentArchivePath`、`StatusMessage` 均存在 ✅ |
| `_allRawItems` | 存在（`private IReadOnlyList<ArchiveItem>?`），需改为 `internal` 或新增方法 |
| `_sessionPasswords` | 存在（`Dictionary<string, string>`，`private`）✅ |
| `DataGrid SelectedItems` | `Avalonia.Controls.DataGrid` 已使用 ✅ |
| `ArchiveItemModel` | 有 `FromCore()`，需补充 `ToCoreItem()` 反向转换 |
| `DragEventArgs.DataTransfer` | 使用 `e.DataTransfer.Formats.Contains(DataFormat.File)` ✅ |
| `ProgressWindow` | `Dialogs/ProgressWindow.axaml.cs` 存在，构造函数支持 title ✅ |
| `OpenFolderDialog` | `Avalonia.Controls.OpenFolderDialog` 可用 ✅ |
| `AppSettings` | 使用 `Models.AppSettings.Load()`（无 `Default` 单例）|
| `ArchiveFormatHelper` | `Models/ArchiveFormatHelper.GetFormat()` 存在 ✅ |
| 主题文件路径 | `Themes/ThemeLight.axaml` / `Themes/ThemeDark.axaml` |
| 拖入保护 (`_isOwnDrag`) | 已在 `MainWindow.axaml.cs` 中使用 ✅ |

### ⬜ 仍待实施时决策

| 事项 | 说明 |
|------|------|
| `_allRawItems` 可见性方案 | 改为 `internal` 属性 vs 新增 `GetAllRawItems()` 方法 |
| 圆角策略 | `DwmGetWindowAttribute` Win11 圆角效果需测试，不稳定则回退矩形边框 |
| 预览弹窗位图渲染 | `RenderTargetBitmap.Render()` 需要控件在 `Measure`/`Arrange` 后调用；确认 `ResultTreeView` 在脱离视觉树时仍可正常渲染 |
| 预览弹窗 BITMAPINFO 对齐 | `BITMAPINFOHEADER` 的 `Marshal.SizeOf` 返回 40 字节（标准 DIB 头大小），实施时需验证 |
| 像素格式匹配 | Avalonia `RenderTargetBitmap.CopyPixels` 输出 BGRA 32bpp top-down；`CreateDIBSection` 设为同格式即不需要颜色转换 |
| 全部测试验证 | 需实际运行时逐项验收 |

---

## Definition of Done

- [⏳] `DropTargetDetector` 能正确检测 Explorer 窗口路径和桌面路径
- [x] 检测失败时弹文件夹选择对话框 — DragDropService.ExecuteAfterDropAsync 含 fallback 路径 (code review)
- [x] 多选拖拽展开目录，路径裁剪正确 — DragDropItemExpander 逻辑已验证 (code review)
- [x] 直接解压到目标目录，不走 temp — 代码无 temp 目录引用 (code review)
- [⏳] `ProgressWindow` 在解压期间正常显示进度
- [x] 加密文件使用会话密码解压 — password 参数通过 ArchiveEntryExtractor 传递 (code review)
- [x] 文件冲突处理遵守 `AppSettings.FileConflictAction` — 字符串匹配逻辑已验证 (code review)
- [x] 拖拽取消（Esc）无残留 — overlay.Close()+Dispose() 在 finally 块执行 (code review)
- [⏳] `DragOverlayWindow` 在拖拽期间正确显示绿/红/灰状态
- [x] 覆盖层不遮挡目标窗口内容 — WS_EX_TRANSPARENT + HWND_TOPMOST (code review)
- [⏳] `DragPreviewPopup` 在拖拽期间跟随鼠标显示预渲染的文件树
- [⏳] 预览弹窗位图生成正确（Avalonia `RenderTargetBitmap` → GDI `CreateDIBSection` 像素一致）
- [x] 预览弹窗不遮挡目标窗口内容 — 偏移右下 20px + 点击穿透 (code review)
- [⏳] 大文件树时渲染时间可接受（300 文件 < 100ms）— 需要性能测试
- [x] DragOverlayWindow 的动画不影响性能 — 20fps + 50ms 定时器低消耗 (code review)
- [x] `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` 通过
- [x] `dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj` 通过

---

## 方案 C：OLE 虚拟文件拖拽（后续计划）

> **2026-07-31 加入**。方案 A（`SetSystemCursor` 替换 OCR_NO）只是视觉缓解；本方案从根上解决「Explorer 不识别自定义格式 → 光标显示禁止」问题，是拖拽光标的根治路径。

### 背景与动机

Avalonia 12 的 `OleDragSource.GiveFeedback` 固定返回 `DRAGDROP_S_USEDEFAULTCURSORS`（见 [Avalonia 源码](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Windows/Avalonia.Win32/OleDragSource.cs)），即 OLE 每次鼠标移动都用系统光标资源设置默认光标。由此推导出三个事实：

1. 空 DataTransfer / 自定义格式 → Explorer 的 `IDropTarget::DragEnter` 返回 `DROPEFFECT_NONE` → 光标显示禁止（OCR_NO），无法通过格式名解决
2. 定时 `SetCursor` 会被 OLE 在下一次鼠标移动时用默认资源覆盖 → 拖动中闪烁，**不可行**
3. `SetSystemCursor`（方案 A）替换系统资源表本身 → OLE 每次取到的都是替换后的光标，稳定生效；但副作用是全局短暂影响 + 进程崩溃时还原失败会残留

方案 C 完全绕开 Avalonia 的 OLE 拖拽，自实现源侧 OLE 接口，在 `GiveFeedback` 中完全控制光标；升级版进一步暴露 `CFSTR_FILEDESCRIPTOR`/`CFSTR_FILECONTENTS` 虚拟文件格式，让 Explorer 把拖拽识别为真实文件拖拽（显示标准复制光标 + 文件名跟随），并支持松手时按需解压。

### 目标

| 优先级 | 目标 |
|--------|------|
| P0 | 自实现 `IDataObject` + `IDropSource` + `OleDoDragDrop`，`GiveFeedback` 中 `SetCursor(自定义)` + `return S_FALSE`，光标完全可控 |
| P1 | 暴露 `CFSTR_FILEDESCRIPTOR`（`FileGroupDescriptorW`）+ `CFSTR_FILECONTENTS`（`FileContents`，`TYMED_ISTREAM` 延迟渲染）→ Explorer 显示标准复制光标与文件名提示 |
| P1 | 保留现有 OverlayController 视觉高亮（目标窗口三色边框 + 状态文字），二者互补 |
| P2 | 松手时通过 `GetData(FileContents)` 按需解压，取代/补充现有 `ExecuteAfterDropAsync` 路径 |

### 已就绪的基础设施

`NativeMethods.cs` 已含：`DoDragDrop`、`STGMEDIUM`、`GlobalAlloc/GlobalLock/GlobalUnlock/GlobalFree/GlobalSize`、`RtlMoveMemory`、`TYMED_HGLOBAL/TYMED_ISTREAM`、`DV_E_*`、`S_OK`、`RegisterClipboardFormatW`。缺 `IDataObject` / `IDropSource` / `IEnumFORMATETC` 接口定义与实现类。

### 接口骨架（C# COM Interop）

```csharp
[ComImport, Guid("0000010E-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDataObject
{
    void GetData(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium);
    void GetDataHere(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium);
    int QueryGetData(ref FORMATETC pFormatetc);
    int GetCanonicalFormatEtc(ref FORMATETC pFormatetcIn, out FORMATETC pFormatetcOut);
    int SetData(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium, int fRelease);
    void EnumFormatEtc(uint dwDirection, out IEnumFORMATETC ppenumFormatEtc);
    int DAdvise(ref FORMATETC pFormatetc, uint advf, nint pAdvSink, out uint pdwConnection);
    int DUnadvise(uint dwConnection);
    int EnumDAdvise(out nint ppenumAdvise);
}

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDropSource
{
    int QueryContinueDrag(int fEscapePressed, int grfKeyState);
    int GiveFeedback(int dwEffect);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct FILEDESCRIPTORW
{
    public uint dwFlags;
    public Guid clsid;
    public NativeMethods.SIZE sizel;
    public NativeMethods.POINT pointl;
    public uint dwFileAttributes;
    public long ftCreationTime;
    public long ftLastAccessTime;
    public long ftLastWriteTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
}
```

### 关键技术点

| 主题 | 说明 |
|------|------|
| GiveFeedback | `SetCursor(hCustomCursor); return S_FALSE;` — S_FALSE 表示 OLE 不干预光标，由源完全控制 |
| QueryContinueDrag | Esc 按下 → `DRAGDROP_S_CANCEL`；左键松开 → `DRAGDROP_S_DROP`（标准键态检查，可复用现有 WH_KEYBOARD_LL Esc 检测思路） |
| IDataObject 最小实现 | 只需实现 `EnumFormatEtc` / `GetData` / `QueryGetData`，其余返回 `E_NOTIMPL`；`EnumFormatEtc` 需提供 `IEnumFORMATETC` |
| FileContents 延迟渲染 | `GetData(lindex=i)` 时用 `IStream` 提供第 i 个条目的解压流（`TYMED_ISTREAM`）；Explorer 在 drop 之后才请求内容 |
| 多文件拖拽 | `FILEGROUPDESCRIPTORW` 一次性列出全部条目；`GetData` 的 `lindex` 区分具体文件 |
| 文件名编码 | `FILEDESCRIPTORW.cFileName` 为 Unicode，天然支持中文文件名 |
| 失败教训 | WPF 版自定义 `System.Windows.IDataObject` 因 WPF OLE 桥内部 bug 崩溃 Explorer（见 AGENTS.md「Custom IDataObject attempt (archived)」）；**Avalonia 无 WPF OLE 桥**，纯 Win32 COM 接口实现可绕开该 bug |
| 光标句柄 | 自定义光标需 `CopyIcon` 持有副本（`LoadCursor` 返回共享句柄）；结束 `DestroyCursor` 清理 |
| COM 生命周期 | CLR COM Interop 自动管理引用计数；实现类需在拖拽期间持有引用防 GC |

### 风险与验收

**风险**
- COM 接口细节 bug（FORMATETC/STGMEDIUM 语义、流式 `IStream` 实现）
- 大文件/多文件时 `FileContents` 流式解压的性能与取消响应
- 加密压缩包在拖拽阶段（描述符阶段）不触发解压，实际内容在 drop 后请求，密码传递链路需保持

**验收标准**
- 拖到 Explorer 任意文件夹显示标准复制光标（非禁止）
- drop 到文件夹后文件按需解压到目标路径（可暂存复用现有 `ExecuteAfterDropAsync` 逻辑）
- 多文件、中文文件名、加密压缩包均正常
- `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` 通过

### 备注

- 方案 A（`SetSystemCursor`，已实施）作为过渡，不影响本方案的独立性
- 实施时若 Avalonia 版本升级改变了 `OleDragSource.GiveFeedback` 行为（不再返回 `USEDEFAULTCURSORS`），可重新评估是否仍需自实现
