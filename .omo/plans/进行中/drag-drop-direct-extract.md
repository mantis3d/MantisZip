# 拖拽直接解压 — 放弃 CF_HDROP，Drop 后检测目标窗口路径直接提取

> **状态**: 🟡 进行中 —— 拖拽编排主体已完成（Task 1–6，2026-07-23）；**视觉反馈层（Task 7）与手工验收（Task 8）未完成**。
> **阶段**: [■■■■■■□□□□] (6/10) — 2026-11-27 按实测重算：Task 7 的 7.2/7.3/7.4 勾了但没落地（`DragPreviewPopup.cs` 不存在、`DragPreviewBitmapBuilder` 零调用者、`OverlayController.SetPreview` 零调用者、`Dwm` 在覆盖层里零命中），不计入完成。
> **分支**: `alpha` / `beta`（代码改动按规则 19 落地；`plans` 只写文档）
> **2026-11-27 重写**: 视觉反馈层的设计**整体换成「主从式多投放区」** —— 覆盖层 = 一个主区域（目标目录 + 预览树）+ 一条参数条带，鼠标移到某个格子，主区域立即按该格参数重算。**2026-08-06 的「独立 Win32 预览弹窗」方案已废弃**（用户 2026-10-30 与 2026-11-27 两次确认不做弹窗），2026-10-30 的「落点冲突预览」与「双投放区」两节也已废弃。现设计见文末「落点冲突预览与多投放区 —— 主从式覆盖层」，废弃内容见「已废弃设计（存档）」。
> **硬约束**: 拖拽期间 UI 线程被 OLE `DoDragDrop` 同步阻塞（`Services/CustomOleDragDrop.cs:387` 调 `NativeMethods.DoDragDrop`），树与冲突标记必须在拖拽启动前算好，拖拽中只能做 GDI 绘制与内存查表。

---

## TL;DR

彻底放弃 WPF OLE 那套 `CF_HDROP` + `IDataObject` 延迟渲染方案。在 Avalonia 分支上，用 `WindowFromPoint` + `ShellWindows` COM 检测拖拽松手后的鼠标位置所对应的文件系统路径，直接将压缩包内容解压到该目录。不需要临时文件，不需要操心 Explorer 的 OLE bridge bug。

**核心流程**：
```
用户按住拖拽 → DoDragDropAsync(自定义格式) 立即响应
    → 拖拽前：把展开后的条目折成行模型，算好各投放区的顶层项与冲突（一次性）
    → 拖拽途中：Win32 覆盖层实时高亮目标窗口，主区域显示「目标目录 + 预览树」
    → 拖拽途中：鼠标移到参数条带的某一格 ⇒ 主区域按该格参数重算并重绘（内存查表，零 IO）
    → 用户松手 → DoDragDropAsync 返回
    → 取鼠标位置 (GetCursorPos) + 松手时悬停的格 (controller.HoveredZone)
    → WindowFromPoint → ShellWindows COM / Win32 EnumChildWindows → 取得目标路径
    → 悬停的是「打开解压窗口」格 ⇒ 弹解压设置窗（不解压）
    → 检测失败 / 无目标 ⇒ 同样弹解压设置窗
    → 否则：ProgressWindow 显示解压进度 → 直接提取到目标目录 → 完成
```

**涉及文件**（全部在 `src/MantisZip.UI.Avalonia/` 下）:

| 文件 | 操作 |
|------|------|
| `Services/DropTargetDetector.cs` | 🆕 新增 — 目标路径检测 |
| `Services/DragDropService.cs` | 🆕 新增 — 拖拽编排 |
| `Services/DragDropItemExpander.cs` | 🆕 新增 — 多选展开 + 路径裁剪 |
| `Services/DragOverlayWindow.cs` | ⛔ 已废弃 — 重构为 Avalonia Window + `OverlayController`（后台线程 UpdateLayeredWindow） |
| ~~`Services/DragPreviewPopup.cs`~~ | ⛔ **不新增** — 独立 Win32 预览弹窗方案 2026-11-27 废弃，改为主从式覆盖层 |
| `Views/MainWindow.axaml.cs` | 🔧 修改 — 替换现有拖拽代码 |
| `ViewModels/MainWindowViewModel.cs` | 🔧 修改 — 补充拖拽相关状态 |
| `Models/ArchiveItemModel.cs` | 🔧 修改 — 补充 ToCoreItem 转换 |
| `MantisZip.UI.Avalonia.csproj` | 🔧 修改 — ADD SHDocVw COM 引用 |
| ~~`Themes/ThemeLight.axaml`~~ | ⛔ **不新增** — 覆盖层是 Win32 GDI 绘制，绑不了 Avalonia 资源 |
| ~~`Themes/ThemeDark.axaml`~~ | ⛔ **不新增** — 同上 |
| `Services/DragConflictPreview.cs` | 🆕 新增 — 一层冲突判定 + 按结果去重（纯逻辑，可单测） |
| `Services/OverlayController.cs` | 🔧 修改 — 逐行渲染 + 多投放区条带 + 三档降级 + 悬停判定 |
| `Services/ExtractFlow.cs` | 🔧 修改 — `RunSelectedItemsExtractionAsync` 加 `rootMode` 形参 |
| `Views/MainWindow.Thumbnails.cs` | 🔧 修改 — 拖拽入口喂条目/尺寸给覆盖层、松手时传 `HoveredZone` |

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
| | 预览弹窗形态（2026-08-06 确认）：**独立 Win32 弹窗跟随鼠标**（不合成进覆盖层，避免遮挡目标窗口内容）；尺寸 **460×680** + 屏幕边缘自动翻转；渲染时机 **PointerPressed 预取式**（按下即渲染，超阈值时通常已就绪，零延迟启动）；**双阈值降级**（>300 文件 或 渲染超 250ms → 只渲染前 2 层 + 摘要；>2000 文件 或 超 500ms → 纯摘要文字）|
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
    │ PointerMoved → 折成行模型（树 + 各参数格的结果）
    │              → 超过阈值启动 DoDragDropAsync
    │ DoDragDropAsync(自定义 DataTransfer) → 阻塞 UI 线程
    │    └─ Win32 独立线程 (OverlayController)
    │       ├─ 实时跟踪鼠标位置
    │       ├─ 覆盖层：高亮目标窗口 (绿/红/灰)
    │       └─ 覆盖层主区域：目标目录 + 预览树；底部/右侧参数条带
    └─ 用户松手 → DoDragDropAsync 返回
    └──────────────────────────────────────────────┐
                                                    ▼
DragDropService.DetectAndExtractAsync()
    │ 1. 销毁覆盖层（记下松手时悬停的格 controller.HoveredZone）
    │ 2. GetCursorPos() → 获取松手时的鼠标坐标
    │ 3. DropTargetDetector.GetPathFromHwnd(hWnd)
    │    ├─ ShellWindows COM → Explorer 路径
    │    ├─ Win32 EnumChildWindows → #32770 对话框路径
    │    ├─ 桌面 (Progman/WorkerW) → 桌面路径
    │    └─ 其他/失败 → 弹解压设置窗（ExtractSettingsWindow.ShowWithPresetSelectionAsync）
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

### OverlayController 状态机（主从式多投放区）

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

    // ═══ 折成行模型（拖拽前一次算好，拖拽中不再读写压缩包）═══
    var treeRows = DragConflictPreview.BuildRowModel(selectedItems, allItems, vm2.CurrentFolder);

    // Start drag with custom data format (unrecognized by Explorer)
    var data = new DataTransfer();
    data.Set("MantisZipDragFormat", archivePath);

    _isOwnDrag = true;
    vm2.StatusMessage = "拖拽到 Explorer 或桌面以直接解压";

    // ═══ 启动 Win32 覆盖层（独立线程；主从式多投放区）═══
    using var controller = new OverlayController(overlayHwnd, mainHwnd);
    controller.SetContent(treeRows, vm2.CurrentFolder);
    controller.Start();

    await DragDrop.DoDragDropAsync(triggerEvent, data, DragDropEffects.Copy);

    // ═══ 拖拽结束，销毁覆盖层 ═══
    var hovered = controller.HoveredZone;
    // using 自动 Dispose；hovered 交给 DragDropService

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

### Task 7: 拖拽视觉反馈（主从式覆盖层）

> **2026-11-27 重写**：本节原有的 610 行代码块（`DragOverlayWindow` / `DragPreviewPopup` / `DragPreviewBitmapBuilder` 三个完整实现）**是历史草稿，已全部删除**。原因见下。

**为什么删掉旧代码块**：

| 问题 | 实测证据 |
|---|---|
| 代码里调了不存在的 API | 旧 7.2 的 260 行代码块调 `ResultPreviewService.BuildDragPreview(archivePath, expanded, format)` —— 该重载不存在；真实签名是 `BuildExtractPreview(IEnumerable<ArchiveItem> entries, string destDir, ...)` |
| 属性名不存在 | 旧代码用 `PreviewTreeNode.DisplayName`；真实属性是 `Name` / `DisplayLabel` |
| 与真实实现完全分叉 | 实际落地的是 `Services/OverlayController.cs`（717 行，Avalonia `Window` + 后台线程 `UpdateLayeredWindow`），不是 `DragOverlayWindow`（纯 Win32 + 自建消息循环）；`_trackingThread` 在旧代码里 0 命中、真实代码里 3 处 |
| 整段方案已被否决 | 用户 2026-10-30 与 2026-11-27 两次确认**不做独立预览弹窗** ⇒ `DragPreviewPopup` 永远不会有 |

**Task 7 的实际状态**（按实测重算，不再是「已完成」）：

| 子项 | 原标记 | 实际 |
|---|---|---|
| 7.1 覆盖层基础 | `[x]` | ✅ **真的做了** —— 但落点是 `Services/OverlayController.cs`，不是计划里的 `Services/DragOverlayWindow.cs` |
| 7.2 预览弹窗 + 位图构建 | `[x]` | ❌ **未做** —— `DragPreviewPopup.cs` 不存在；`DragPreviewBitmapBuilder.cs` 存在但 **0 调用者**；且方案已废弃 |
| 7.3 主题资源补充 | `[x]` | ⚠️ **名义完成** —— `DragDropBorderSuccessBrush` 等 6 键在 `ThemeLight.axaml` / `ThemeDark.axaml` 各 3 处，但计划自己写着「仅供未来参考」「Win32 覆盖层无法直接绑定 Avalonia 资源」 ⇒ 纯摆设键 |
| 7.4 Win11 圆角适配 | `[x]` | ❌ **未做** —— `OverlayController.cs` 里 `Dwm` 零命中；`NativeMethods.cs:261` 的 `DwmGetWindowAttribute` 声明与 `:323` 的 `DWMWA_EXTENDED_FRAME_BOUNDS = 9` 均无人调用 |

**Task 7 要做的（按新设计，详见文末「落点冲突预览与多投放区 —— 主从式覆盖层」）**：

| # | 子项 | 涉及文件 |
|---|---|---|
| 7.1 | 一层冲突判定 + 按结果去重（纯逻辑） | 🆕 `Services/DragConflictPreview.cs` |
| 7.2 | `OverlayRender` 从「整段画两遍」改成「逐行画 + 逐行设色」，字号改固定档 | 🔧 `Services/OverlayController.cs` |
| 7.3 | 主区域（目标目录 + 树）+ 参数条带 + 悬停判定 + 长宽比自动换边 + 三档降级 | 🔧 `Services/OverlayController.cs` |
| 7.4 | 「打开解压窗口」格 + `DragDropChoice` 从松手处传到 `DragDropService` | 🔧 `Views/MainWindow.Thumbnails.cs`、`Services/DragDropService.cs` |
| 7.5 | `RunSelectedItemsExtractionAsync` 加 `rootMode` 形参（下游 `SelectedItemsExtractService` 已支持 `stripRoot` / `addRoot`） | 🔧 `Services/ExtractFlow.cs` |
| 7.6 | i18n 7 个新 key 三语 | 三份 `Localization/strings.*.json` |
| 7.7 | 逻辑测试 + 结构绊线 | 🆕 `tests/MantisZip.UI.Avalonia.Tests/DragConflictPreviewTests.cs` 等 |

**不再需要的**（旧计划里的，全部作废）：

- ❌ 新建 `Services/DragOverlayWindow.cs`（已被 `OverlayController` 取代）
- ❌ 新建 `Services/DragPreviewPopup.cs`（方案否决）
- ❌ 改造 `Services/DragPreviewBitmapBuilder.cs`（0 调用者，且树改由 GDI 逐行画，不需要位图）
- ❌ 新增 `ThemeLight.axaml` / `ThemeDark.axaml` 的覆盖层颜色键（GDI 绑不了 Avalonia 资源）
- ❌ Win11 圆角（GDI 分层窗口无边框可圆）

> **⚠️ `DragPreviewBitmapBuilder.cs` 的去留**：它现在 0 调用者。按「不用的代码不留」的原则应删；但它 136 行里有可复用的「展开 → `BuildExtractPreview` → 折行」逻辑，`DragConflictPreview.BuildRowModel` 可以从它抄。**实施时先抄逻辑，再删文件**，并在 `docs/KNOWN_ISSUES.md` 或提交信息里注明。

### Task 8: 手动测试验证 [⏳]

> [⏳] 需要可运行的 Avalonia 应用 + 至少一个 Explorer 窗口。覆盖层的三档布局、悬停判定、长宽比换边都**只能在真机上验**（无头测试覆盖不到 Win32 分层窗口）。建议边实现边验证。

- [ ] **8.1 基础拖拽（沿用旧清单，去掉预览弹窗项）**

| 场景 | 预期 |
|------|------|
| 拖一个文件到 Explorer 窗口 | 覆盖层变绿 → 主区域显示目标目录 + 树 → 松手 → ProgressWindow → 文件出现在该目录 |
| 拖到桌面 | 覆盖层变绿 → 检测到桌面路径 → 提取到桌面 |
| 拖到非 Explorer 区域（如 Chrome） | 覆盖层变灰 → `DetectTargetDirectory` 返回 null → **只有「打开解压窗口…」一格可选**（见 8.4） |
| 拖到 #32770 对话框 | 覆盖层变红 → 提示需确认 → 松手走解压设置窗 |
| 拖多个文件（Ctrl+多选） | 主区域树显示展开后的完整结构（受行数上限约束） |
| 拖目录（子文件展开） | 树显示展开后的结构，折叠符号正确 |
| 拖加密压缩包 | 用会话密码提取；密码缺失时走原提示流程 |
| 拖到一半按 Esc | 覆盖层销毁 → StatusMessage 更新，无额外操作 |
| 目标目录已有同名文件 | 按 `AppSettings.FileConflictAction` 处理（与投放区选择无关） |
| `EnableDragExtract = false` | 不启动拖拽 |
| 快速划过多个窗口 | 覆盖层不闪烁（150ms 防抖生效） |
| DPI 150% | 覆盖层大小、位置、字号都正确 |

- [ ] **8.2 三档布局**（改目标窗口尺寸后重新拖）

| 目标窗口尺寸 | 预期布局 |
|---|---|
| 1600×900（常规档） | 竖长条：条带在右侧，主区域树最多 8 行 + 示例路径全显示 |
| 1000×420（常规档，横长） | 横长条：条带在底部，厚度 ≥ 64px |
| 460×340（紧凑档） | 树降到 3 行（只显顶层），条带压成一行纯文字，当前格反色 |
| 300×200（极小档） | 主区域只有一行落点、**不画树**；条带只剩「默认」「打开解压窗口…」两格 |
| 1200×900 → 拖拽中把窗口拉窄到 300 宽 | 覆盖层跟随重排（`UpdatePosition` 每 100ms 重取 `GetWindowRect`） |

- [ ] **8.3 参数条带与悬停**

| 操作 | 预期 |
|---|---|
| 鼠标在**主区域** | 用设置里的默认参数（`ExtractPreserveFullPath` / `ExtractRootMode`） |
| 悬停「保留完整路径」 | 主区域树立即变成 `preserveFullPath=true` 的结构；格子高亮 |
| 悬停「扁平解压」 | 树变成 `preserveFullPath=false` 结构（`currentFolder` 为空时该格**不出现**） |
| 悬停「始终加根」 | 树的顶层变成一个新目录（名字 = 压缩包主名或 `CompressRootName`） |
| 悬停某个格但结果与默认相同 | 该格灰显 + 标注「与默认相同」，**不隐藏**（避免宽度跳变） |
| 在「始终加根」格松手 | 实际落点 = 加根后的路径，与树里显示的一致 |
| 快速在多个格之间移动 | 无闪烁、无「先白后红」（树只在算完后一次性重绘） |

- [ ] **8.4 冲突标记与「打开解压窗口」格**

| 场景 | 预期 |
|---|---|
| 目标目录已有同名顶层项 | 对应行**白字 + 整行半透明红底 + `⚠` 前缀**（不是红字压绿底） |
| 顶层项不存在但更深的子项同名 | **不标冲突**（一层判定即可证明：顶层不存在 ⇒ 整条路径不存在） |
| 目标目录为空 | 全部行无标记 |
| 悬停「打开解压窗口…」格 | 主区域**只写一行说明、不画树**；松手弹出预选了当前条目与目标目录的解压设置窗 |
| 目标检测失败（拖到 Chrome） | 「打开解压窗口…」是**唯一**可选格；松手必弹解压设置窗（不静默丢弃） |
| 拖拽中目标窗口被关闭 | 覆盖层不再高亮，降级为「检测失败」路径 |

- [ ] **8.5 长宽比换边与回退**

| 操作 | 预期 |
|---|---|
| 目标窗口 `w/h ≥ 1.75` | 条带在**底部**，厚度 `max(64, h × 8%)` |
| 目标窗口 `w/h < 1.75` | 条带在**右侧**，厚度 `max(150, w × 12%)` |
| 拖拽中窗口从宽变窄跨过 1.75 | 条带换边一次，无残影（整块重绘） |
| 可用格 > 5（将来参数变多） | 只显示前 5 个，其余不渲染 |

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
- [x] 检测失败时弹解压设置窗 — `DragDropService.ExecuteAfterDropAsync` 的 fallback 走 `ExtractSettingsWindow.ShowWithPresetSelectionAsync`（规则 16，不用原生 StorageProvider）
- [x] 多选拖拽展开目录，路径裁剪正确 — DragDropItemExpander 逻辑已验证 (code review)
- [x] 直接解压到目标目录，不走 temp — 代码无 temp 目录引用 (code review)
- [⏳] `ProgressWindow` 在解压期间正常显示进度
- [x] 加密文件使用会话密码解压 — password 参数通过 ArchiveEntryExtractor 传递 (code review)
- [x] 文件冲突处理遵守 `AppSettings.FileConflictAction` — 字符串匹配逻辑已验证 (code review)
- [x] 拖拽取消（Esc）无残留 — `OverlayController` 在 finally 块 `Dispose()`
- [⏳] `OverlayController` 在拖拽期间正确显示绿/红/灰状态
- [x] 覆盖层不遮挡目标窗口内容 — WS_EX_TRANSPARENT + HWND_TOPMOST (code review)
- [ ] 主区域显示目标目录 + 预览树（常规档 ≤8 行 / 紧凑档 3 行 / 极小档不画树）
- [ ] 逐行渲染正确：每行文字、缩进、颜色独立（`OverlayRender` 已从「整段画两遍」改为「逐行画」）
- [x] 覆盖层不遮挡目标窗口内容 — `WS_EX_TRANSPARENT` + `HWND_TOPMOST`（点击穿透，鼠标命中的是下层真实窗口）
- [ ] 大文件树时行模型构建 < 100ms（300 条目）— 需要性能测试
- [x] `OverlayController` 的动画不影响性能 — 后台线程 + 100ms 追踪循环低消耗
- [x] `dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` 通过
- [x] `dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj` 通过
- [ ] 参数条带三格（保留完整路径 / 扁平解压 / 始终加根）+ 分隔线 + 「打开解压窗口…」格都可用
- [ ] 悬停格子时主区域立即按该格参数重算（切格 0 次文件 IO，内存查表）
- [ ] 长宽比 ≥ 1.75 条带在底部、否则在右侧；三档降级（常规 / 紧凑 / 极小）按尺寸生效
- [ ] 目标检测失败时「打开解压窗口…」是唯一可选格，松手必弹设置窗（不静默丢弃）
- [ ] `dotnet test tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj` 通过（含 `DragConflictPreviewTests`）

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

---

## 落点冲突预览与多投放区 —— 主从式覆盖层（设计定稿 2026-11-27）

> **状态**: 📐 设计已定稿（2026-11-27 用户逐项拍板），**代码未动**。
> **取代关系**：本节**取代** 2026-10-30 的「落点冲突预览」与「双投放区」两节（那两节的设计已被推翻，历史内容见文末「已废弃设计（存档）」）。
> 定稿要点：覆盖层 = **一个主区域 + 一条参数条带**；主区域显示目标目录 + 预览树，鼠标移到条带某一格 ⇒ 主区域**即时按该格参数重算**；冲突判定**只查一层**（可证明）；三档降级应对小窗口。

### 用户口径（2026-11-27 逐项拍板）

| 决策点 | 结论 |
|---|---|
| 整体形态 | **主从式**：主区域最大（目标目录 + 预览树），其余是**小格子**排成一条，用简短文字表示各自的参数 |
| 格子语义 | 一格 = **同一参数的一个取值**；不是「把不同参数混在一起」 |
| 悬停行为 | 鼠标在主区域 ⇒ 用**设置里的默认参数**；鼠标在某个格子 ⇒ 主区域的目标目录与预览树**立即按该格重算** |
| 树的规模 | 保留树，但**只显示少量节点**（顶层层级，最多 8 行） |
| 冲突检测粒度 | **只查一层**（必要时两层）——理由见下「一层足够」的可证明性 |
| 变体来源 | **第一版硬编码**，不做设置项勾选 |
| 格子标题 | 写**参数名**（「保留完整路径 / 扁平解压 / 始终加根」），结果示例路径放下面一行 |
| 长宽比 | **自动切换**条带方向（不新增设置项） |
| 额外格 | 再加一格「**打开解压窗口**」：松手不解压，直接弹解压设置窗 |
| 打开窗格被悬停时 | 主区域**只写一行说明**，不画树、不算冲突 |
| 目标检测失败时 | 「打开解压窗口」格成为**唯一可选格**（替代现在直接弹窗） |
| 窄窗口 | 三档降级；**极小档只保留「默认」和「打开解压窗口」两格** |
| 树溢出到覆盖层外 | ❌ **不做**（用户 2026-11-27 决定「先不加，等功能完成后再看」），见「风险 / 遗留」 |

### 一层足够 —— 可证明，不是经验

**命题**：若顶层项在目标处都不存在，则该批条目**一定不会产生任何冲突**。

**证明**：`ExtractPathResolver.ResolveRelativePath(entryKey, currentFolder, preserveFullPath, stripRoot, addRoot)` 的输出恒为相对路径，拼上目标目录后形如 `<顶层项>\<其余>`。任何文件要落盘，其**每一级父目录都必须先存在**——而顶层项是这条路径的第一级，其余各级都在它下面。所以顶层项不存在 ⇒ 整条路径不存在 ⇒ 无冲突。∎

**推论（省掉的复杂度）**：

- 顶层项存在 ⇒ 里面**必然**要合并，具体撞几个文件**不改变「该不该提醒」这个结论**（用户原话：「第一层目录就有冲突，那么里面文件是否冲突就没那么重要了」）
- 因此**不需要**「40 次检查预算」「500ms 超时」以及由此产生的 `(N+)` / `(?)` **未知态**——判定结果是确定的，不存在「算不出来」
- 计划原先的「宁可知未知也不能假无冲突」（旧 `:2047`）**不再需要**，因为不再有未知

**唯一的例外**：`AlwaysWrap` 时顶层只有 1 项（根目录名），它撞上后「里面撞几个」才有信息量。若该格被悬停且顶层项是目录，**再枚举一层**补计数——撞上的顶层项通常 ≤2 个，成本可忽略。

### 覆盖层布局

```
┌──────────────────────────────────────────────────────────────┐
│ 拖到此处：D:\Projects                                         │  ← 主区域
│                                                               │
│  ├─ sub/                                                      │
│  │  ├─ ⚠ docs/                                                │     （目标目录 + 预览树）
│  │  │  ├─ ⚠ readme.md                                         │
│  │  │  └─ config.json                                         │
│  │  └─ notes.txt                                              │
│  └─ ⚠ src/                                                    │
├─────────────┬─────────────┬─────────────┬───────┬────────────┤
│ 保留完整路径 │ 扁平解压     │ 始终加根     │       │ 打开解压窗口… │  ← 参数条带
│ D:\P\sub\…  │ D:\P\…      │ D:\P\sub\…  │       │            │
└─────────────┴─────────────┴─────────────┴───────┴────────────┘
                                           ↑ 分隔线：右侧不是参数格
```

**第一版硬编码的格（从左到右）**：

| # | 标题 | 参数 | 说明 |
|---|---|---|---|
| 1 | 保留完整路径 | `preserveFullPath = true` | 与设置项同名（复用 `Settings_Extract_PreserveFullPath`） |
| 2 | 扁平解压 | `preserveFullPath = false` | 新 key `DragZone_Flat` |
| 3 | 始终加根 | `RootMode = AlwaysWrap` | 新 key `DragZone_AlwaysWrap` |
| — | （分隔线） | — | 视觉上把「参数格」与「动作格」分开 |
| 4 | 打开解压窗口… | 不走解压 | 新 key `DragZone_OpenDialog`；被悬停时主区域只写一行说明 |

**格子的取舍（按结果去重）**：

`preserveFullPath` 与 `RootMode` 是**不同的参数**，第一版把它们**并列成三个独立的格**（等价于「选了哪个格就只用那个参数」），不叠加成 2×3 网格——叠加会产生 6 个格子且语义交叉。
仍然需要去重：若 `currentFolder` 为空（从压缩包根目录拖出），`preserveFullPath` 不影响任何路径 ⇒ 去掉「扁平解压」格。若某些格算出的顶层项集合与「默认」完全相同 ⇒ 该格灰显并标注「与默认相同」（不隐藏，避免格子宽度跳变）。

### 长宽比自动切换

不新增设置项，纯几何判断（`OverlayController` 已有目标窗口的 `w` / `h`）：

| 条件 | 条带位置 | 条带厚度 |
|---|---|---|
| `w / h ≥ 1.75` | **底部**横条 | `max(64, h × 8%)` |
| `w / h < 1.75` | **右侧**竖条 | `max(150, w × 12%)` |

- 格子数 2 时平分；3 时第 1 格占 40%（标题最长），其余平分；含「打开解压窗口」格时该格固定占 1/3 宽度并被分隔线隔开。
- 超过 5 格不滚动（覆盖层不是真控件），改为只显示前 5 格。

### 三档降级

覆盖层**铺满目标窗口**（`OverlayController.cs:193-204` 用 `GetWindowRect` 的宽高调 `SetWindowPos`），所以目标窗口多小它就有多小。按 `w × h` 分三档：

| 档 | 条件 | 主区域 | 条带 |
|---|---|---|---|
| **常规** | `w ≥ 480 && h ≥ 360` | 路径行 + 树（≤8 行） | 全部格子 + 示例路径 |
| **紧凑** | `w ≥ 320 && h ≥ 240` | 路径行 + 树降到 **3 行**（只显示顶层项） | 压缩成**一行纯文字**，当前悬停格反色标记；不画示例路径 |
| **极小** | 其他 | **只有一行落点**（`解压到 D:\P…`），不画树 | **只有两格**：「默认」「打开解压窗口…」 |

- 极小档的关键决策：**宁可不给选择，也不给看不清的选择**。三个参数格在这种尺寸下每格仅约 90px 宽，拖拽时难以瞄准；而「默认」与「弹窗口」是唯二仍有意义的选择。
- 极小档悬停「打开解压窗口」时，主区域写「在窗口里选择位置」（新 key，需缩写到当前字号放得下）。
- 三档**共用同一套行渲染器**：把要画的内容组织成 `(text, color, isWarning)` 的行数组，极小幅 1 行、紧凑档 4 行、常规档 10 行。这样 `OverlayRender` 只需从「整段画两遍」改成「逐行画」一次，三档都受益。

### 鼠标命中判定（限制：覆盖层是点击穿透的）

覆盖层窗口创建于 `Views/MainWindow.Thumbnails.cs:427-456`，ex-style 为：

```csharp
WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
```

`WS_EX_TRANSPARENT` ⇒ **鼠标命中测试直接穿过覆盖层**，`DropTargetDetector.DetectTargetDirectory()` 的 `WindowFromPoint` 拿到的永远是下层真实窗口。这是刻意的（松手时 Explorer 才是真正的投放目标），但也意味着：

- **格子不是控件**：没有 hover 样式、没有光标变化、没有点击反馈
- 悬停判定必须**自己算**：`GetCursorPos()` → 减去覆盖层矩形原点 → 与各格矩形比较，在 `RunTrackingLoop` 的 100ms tick 里完成（纯算术，零 IO）

### 数据流与性能

**目标目录的枚举只做一次**：落点变化（去抖 150ms）时 `Directory.EnumerateFileSystemEntries(dir)` 一次，得到 `HashSet<(string Name, bool IsDir)>`（Windows 用 `OrdinalIgnoreCase`）。

**所有格子的路径都在内存里算**：

- 拖拽启动前已把 `DragDropItemExpander.ExpandItems(selectedItems, allItems)` 的结果折成行模型（`Views/MainWindow.Thumbnails.cs` 的拖拽入口），**拖拽中不再读写压缩包**
- `preserveFullPath` 是 `ExtractFlow.RunSelectedItemsExtractionAsync` 的既有每-run 形参，直接传值
- `RootMode` 走 `ExtractRootPlan.Resolve(mode, entries, archivePath)`（**纯函数**，entries 已在手）→ 得到 `(stripRoot, addRoot)`
- 各格的顶层项集合在内存里比对上面的 `HashSet` ⇒ **N 个格子的 IO 成本与 1 个格子几乎相同**

**硬指标**：

| 项 | 预算 |
|---|---|
| 落点变化时的目录枚举 | 1 次/落点（去抖 150ms） |
| 格子间切换 | **0 次磁盘 IO**（只查内存 HashSet） |
| 每 100ms 重绘 | 0 次磁盘 IO（只绘制缓存的行数组） |
| 慢盘 / 网络路径 | 无额外超时——枚举本身在后台线程，切格时读缓存；首次枚举未完成时主区域显示「正在检查…」（新 key），不标红也不标绿 |

**避免「先白后红」闪烁**：目标变化或切格时，先只更新路径行（用户此刻最需要的），树与冲突标记等这次枚举/计算完成后**一次性绘制**，不做「逐行补标记」。

### 行渲染的结构性改造

现状 `OverlayRender`（`Services/OverlayController.cs:315`）是**整段文字一次性 `GdiDrawText` 画两遍**（`:455` 阴影、`:459` 主文字），全程单一颜色。要支持「逐行不同颜色」必须改：

1. 把 `string text` 换成 `IReadOnlyList<OverlayLine>`（`Text` / `Color` / `IsWarning`）
2. 逐行 `GdiDrawText`，每行前后 `GdiSetTextColor`
3. 字号：`:418-426` 的「按 `Math.Min(w, h)` 自适应 36/28/24/20/16px」是为**两行路径**设计的，9 行时会被压到不可读 ⇒ 常规档固定 **13-14px**，紧凑档 12px，极小档按档位单独算
4. 行高 = 字号 × 1.5；树行左缩进按层级 × 16px
5. 冲突行：**白字 + 整行半透明红底 + `⚠` 前缀**（不用红字——`OverlayRender` 的 Success 底色是绿 `0x006BD46B`，红字压绿底红绿色盲不可辨；颜色只做辅助，`⚠` 才是主信号）

### 实现清单

1. **新增** `src/MantisZip.UI.Avalonia/Services/DragConflictPreview.cs`（纯逻辑，不依赖 Win32，便于单测）：
   - `static IReadOnlyList<string> ResolveTopLevels(IReadOnlyList<ArchiveItem> expanded, string currentFolder, bool preserveFullPath, RootDirectoryMode rootMode, string archivePath)` —— 走 `ExtractPathResolver.ResolveAll` 取相对路径的首段
   - `static DragConflictResult Compare(IReadOnlyList<string> topLevels, IReadOnlySet<string> existingEntries)` —— 返回冲突的顶层项名与（可选）子项计数
   - 按结果去重的辅助方法
2. **`src/MantisZip.UI.Avalonia/Services/OverlayController.cs`**：
   - `SetContent(IReadOnlyList<OverlayZone> zones, IReadOnlyList<ArchiveItem> expanded, string currentFolder, ...)` —— 拖拽开始前一次传入
   - 新增 `OverlayZone`（`Title` / `Parameters` / `IsAction` / `IsDefault`）
   - `RunTrackingLoop` 里：算光标落在哪个格 → 切格时只在内存里重算行 → 触发重绘
   - 落点变化时在**后台线程**枚举目标目录（不阻塞 100ms 循环）
   - `OverlayRender` 改逐行渲染（见上）
   - 三档降级、长宽比切换、条带绘制、分隔线
3. **`src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs`**：`RunSelectedItemsExtractionAsync` 加 `RootDirectoryMode rootMode = RootDirectoryMode.Original` 形参（`preserveFullPath` 之后），内部 `ExtractRootPlan.Resolve` 后传给 `SelectedItemsExtractService.ExtractEntriesAsync(... stripRoot, addRoot ...)`——**下游已支持这两个形参，只差这一层透传**。
4. **`src/MantisZip.UI.Avalonia/Services/DragDropService.cs`**：`ExecuteAfterDropAsync` 增加 `DragDropChoice? choice = null`（记录松手时悬停的格），据此决定：
   - `choice.IsAction`（打开解压窗口）⇒ 直接走**现有**的兜底分支（`:78-97` 的 `ExtractSettingsWindow.ShowWithPresetSelectionAsync`），**不新增执行路径**
   - 否则把 `choice.PreserveFullPath` / `choice.RootMode` 传给 `ExtractFlow.RunSelectedItemsExtractionAsync`
   - `choice == null`（未进入任何格）⇒ 沿用设置的默认值
5. **`src/MantisZip.UI.Avalonia/Views/MainWindow.Thumbnails.cs`**：拖拽入口把「展开后的条目 + `currentFolder` + 目标窗口尺寸」交给 controller；松手后把 `controller.HoveredZone` 传给 `DragDropService`。
6. **i18n**（三份 json）：
   - 新 key：`DragZone_Flat`（扁平解压 / Flat）、`DragZone_AlwaysWrap`（始终加根 / Always add root）、`DragZone_OpenDialog`（打开解压窗口… / Open Extract dialog…）、`DragZone_OpenDialogHint`（在窗口里选择位置 / Choose the location in the dialog）、`DragZone_SameAsDefault`（与默认相同 / Same as default）、`DragOverlay_Checking`（正在检查… / Checking…）、`DragOverlay_MoreItems`（…另 {0} 项 / …and {0} more）
   - 复用既有：`Settings_Extract_PreserveFullPath`、`DragOverlay_TargetPath`
   - `LocalizedStrings` 白名单（若相关 VM 有）与三语同步
7. **测试**：
   - `tests/MantisZip.Tests/` 或 `MantisZip.UI.Avalonia.Tests/DragConflictPreviewTests.cs`：一层判定（顶层全不存在 ⇒ 无冲突、顶层存在 ⇒ 冲突）、`currentFolder` 非空时的顶层名、`StripRoot` / `AddRoot` 影响、`AlwaysWrap` 只有一个顶层项、按结果去重、`(N)` 计数
   - `OverlayZoneTests`：长宽比阈值命中、三档阈值、格子矩形计算（左右上下各 2 例）
   - 结构级绊线（照仓库既有做法）：`ExtractFlow.RunSelectedItemsExtractionAsync` 必须有 `rootMode` 形参；`DragDropService` 里「打开解压窗口」分支必须调用 `ExtractSettingsWindow.ShowWithPresetSelectionAsync`（钉死「不新增执行路径」）
8. **手工验收**（Task 8 重写）：三档尺寸各验一遍、四个格子的悬停效果、极小档两格、「打开解压窗口」格、目标检测失败时该格为唯一选项、长宽比跨阈值时条带换边。

### 工时重估（2026-11-27）

| 阶段 | 工时 |
|---|---|
| `DragConflictPreview` 纯逻辑 + 单测 | 3-4h |
| `OverlayRender` 逐行渲染改造 | 4-5h |
| 多投放区（条带 / 悬停 / 长宽比 / 三档 / 分隔线） | 6-8h |
| 「打开解压窗口」格 + `DragDropChoice` 透传 | 1-2h |
| `ExtractFlow` 加 `rootMode` 形参 | 1-2h |
| i18n（7 个新 key 三语） | 0.5-1h |
| 测试（逻辑 + 结构绊线） | 4-5h |
| 手工验收 | 2-3h |
| **合计** | **约 22-30h** |

> 原 `docs/PLAN.md` 写 10-14h 是按「覆盖层两行字 + 简单双区」估的；实际含逐行渲染改造、三档降级、四格条带与参数透传，按上表重估。`docs/PLAN.md` 已同步改为 22-30h、阶段工时 29-42h。

### 风险 / 遗留

| 项 | 说明 |
|---|---|
| **树溢出到覆盖层外** | 用户 2026-11-27 明确**先不做**。技术代价见当时的评估：覆盖层矩形被 `SetWindowPos` 钉死在目标窗口上（`OverlayController.cs:193-204`），外扩要重算整块背景填充（`:365-391` 按整个 `w × h` 逐像素填）、重画边框、并处理「光标落在溢出区算哪个窗口」的歧义；且会遮住邻近窗口。**等功能完成后再评估** |
| 覆盖层遮住目标窗口文件名 | 常规档树 ≤8 行（约 150px 高），已有现象，用户接受「够用」 |
| 格子不是控件 | `WS_EX_TRANSPARENT` 决定没有 hover / 点击反馈，只能靠主区域内容变化传达「参数变了」；必要时在条带上方加一行细提示 |
| 条带压住底部投放区 | 条带贴在目标窗口底部时，那里本来就是用户可能想丢东西的位置。缓解：厚度压到 `max(64, h×8%)`；且悬停格子时主区域标题明显变化（`拖到此处：D:\Projects（扁平）`） |
| 目标检测失败时的行为变更 | 从「直接弹解压设置窗」改成「极小档只有『打开解压窗口』一格可选、常规档仍显示全部格但主区域写『无法识别目标目录』」。**不新增执行路径**，仍走 `DragDropService.cs:78-97` 的现有分支 |
| 执行侧与预览解耦 | 实际落盘仍走 `ExtractFlow.RunSelectedItemsExtractionAsync`（Ask 弹冲突框），本次不改执行语义 |

---

## 已废弃设计（存档）

以下三节于 2026-08-06 / 2026-10-30 定稿、**2026-11-27 被上方「主从式覆盖层」取代**。内容保留供追溯，**不要照此实施**。

### 废弃一：落点冲突预览的 2026-10-30 版本

被取代的要点：

- 「覆盖层最多 8 行 + 每轮 IO ≤40 次 + 慢盘 500ms 降级 → `(N+)` / `(?)` 未知态」——**未知态不需要了**，见上方「一层足够」的可证明性
- `Views/MainWindow.axaml.cs:1562`、`ExtractPathResolver.cs:53`、`OverlayController.cs:193-204` 等**行号锚点全部失效**（拖拽代码已移到 `Views/MainWindow.Thumbnails.cs`）；改为按符号名定位
- 单区形态（只有一个目标目录 + 一棵树）⇒ 现在是主区域 + 参数条带

保留有效的部分（已并入上方）：

- 树结构取自 `DragDropItemExpander.ExpandItems(selectedItems, allItems)`，启动前折成行模型、拖拽中不再读写压缩包
- 冲突判定必须走 `ExtractPathResolver.ResolveAll`，与本次 `preserveFullPath` 一致——路径口径不得另写，否则「预览说不冲突、实际覆盖了」
- 「目标处已存在就算冲突」（文件、同名目录都算），与冲突策略无关

### 废弃二：双投放区的 2026-10-30 版本

被取代的要点：

- **「把目标窗口切成两个等分投放区」⇒ 改成「主区域 + 参数条带」**。等分两区要在半宽里塞两层树，可读性差；主从式只有一棵树、占满宽度
- 「非悬停区压暗但只显示标题 + 结果路径」这种折中做法**不再需要**——小格子天然就是「一行标题 + 一行示例」
- 「红字压绿底」的红绿盲问题在格子方案下自然消失（冲突标记只出现在主区域的白字上）
- 「两区各自算一套路径与冲突」⇒ **不再成立**：目标目录只枚举一次，各格只查内存 HashSet，IO 成本与单区持平
- `AppSettings.DragZoneSplitOrientation`（`leftRight` / `topBottom`）**不新增**——改为按长宽比自动切换

保留有效的部分（已并入上方）：

- 「命中判定仍按整个窗口算」——改为按条带矩形 + 光标坐标算（覆盖层点击穿透，没法用控件响应）
- 「扩展位：区模型做成『标题 + 一组参数』」——已实现为 `OverlayZone`
- 「`currentFolder` 为空时两区结果相同 ⇒ 合并成单区」——升级为通用的「按结果去重」

### 废弃三：独立预览弹窗（2026-08-06 版「预览弹窗实施补充」）

原方案：`DragPreviewPopup` + `DragPreviewBitmapBuilder`，用 Avalonia `RenderTargetBitmap` 预渲染 `ResultTreeView`，再以 `UpdateLayeredWindow` 在鼠标右下 20px 弹出 460×680 独立窗口。

**被取代的原因**：

- 该方案需要「目标窗口 + 弹窗」两个分层窗口同时跟随，拖拽中要维护三处坐标（目标、弹窗、鼠标），出屏翻转逻辑复杂；
- 弹窗必然遮住一部分目标窗口内容，而用户真正要看的恰恰是「落到目标目录里会是什么样」；
- 用户 2026-10-30 与 2026-11-27 **两次确认不做弹窗** ⇒ 改为把目标目录与树直接画在覆盖层的主区域里，一格都不用跟随鼠标。

**保留有效的部分**（已并入主从式方案）：

- 「拖拽启动前一次性算好、拖拽中只做 GDI 绘制」的时序原则 —— 这条是硬约束（`Services/CustomOleDragDrop.cs:387` 的 OLE `DoDragDrop` 同步阻塞 UI 线程），主从式方案沿用；
- 树结构取自 `DragDropItemExpander.ExpandItems` + `ExtractPathResolver.ResolveAll`，与执行侧同源。

**`DragPreviewBitmapBuilder.cs` 的处理**：它 136 行、**当前 0 调用者**。其中「展开 → `BuildExtractPreview` → 折行」的逻辑可移植到 `DragConflictPreview.BuildRowModel`；移植完成后该文件应删除。

