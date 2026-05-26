# VirtualFileDataObject — 拖拽延迟渲染替换方案

> 用 COM 原生 IDataObject 替代 WPF OLE 桥，解决拖拽导出时 Explorer 崩溃问题
> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜] (0/5)

---

## 动机

### 当前问题：急切提取 (Eager-Extraction)

目前拖拽导出使用 **7-Zip 模式的急切提取**：在 `DoDragDrop` 之前，先将所有文件完整提取到 `%TEMP%\MantisZip\DragDrop\{GUID}\`，然后调用：

```csharp
DragDrop.DoDragDrop(FileListGrid, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
```

**弊端：**

| 问题 | 影响 |
|------|------|
| 大文件延迟 | 用户点住拖拽后要等文件全部提取完才能开始拖 |
| 进度窗口阻塞体验 | 进度窗口在拖拽开始前显示提取进度，用户无法控制何时释放 |
| 磁盘空间占用 | 所有文件解压到 temp，即使 Explorer 只复制其中几个 |
| 不必要的全量提取 | 用户可能只拖到同盘目录（只需要更新元数据），但仍需全部解压 |

### 为什么不用 WPF 自带的 IDataObject 延迟渲染？

**曾经的尝试：** 实现 `System.Windows.IDataObject`，在 `GetData()` 被 Explorer 调用时才提取。

**结果：** Explorer 崩溃。

**根因：** WPF 的 OLE 桥接层 (`IComDataObject`) 在将 `string[]` 转换为 `CF_HDROP` 时有一个内部 Bug。当 `_innerData` 不是 `DataStore` 类型（即自定义实现）时，转换逻辑出错，导致 Explorer 进程 crash。已通过 WPF 源码 (v8.0.1) 确认。**此问题无法从应用侧修复。**

### 解决方案：VirtualFileDataObject

直接实现 `System.Runtime.InteropServices.ComTypes.IDataObject`（COM 原生接口），绕过 WPF 的 OLE 桥，从 COM 层面提供 `CF_HDROP` 格式数据，Explorer 不再崩溃。

---

## 任务清单

- [ ] **1. `VirtualFileDataObject` — COM IDataObject 实现** — 核心组件，实现 `System.Runtime.InteropServices.ComTypes.IDataObject`
- [ ] **2. `COMStreamWrapper` — 流包装器** — 将 .NET Stream 暴露为 COM IStream
- [ ] **3. 拖拽流程改造** — `MainWindow.DragDrop.cs` 集成 VFDO，替换现有急切提取
- [ ] **4. 进度窗口适配** — 延迟渲染模式下进度报告方式调整
- [ ] **5. 回退方案** — VFDO 不可用时回退到当前急切提取

## 架构设计

### 核心思想

```
DragDrop.DoDragDrop
    ↓
传递给 COM 的 IDataObject 是我们自己的实现
    ↓
Explorer 在释放鼠标后调用 GetData(ref FORMATETC, out STGMEDIUM)
    ↓
我们才开始按需提取文件，通过 COMStreamWrapper 流式传递
    ↓
Explorer 边读边写，实现真正的延迟渲染
```

### 交互流程对比

**当前 (急切提取)：**
```
用户按下拖拽 → 创建 TempDir → 逐一提取所有文件 → 显示进度窗口 → DoDragDrop(已就绪路径)
    → Explorer 复制文件 → 清理 TempDir
                  ↑
        用户要等所有文件提取完才能拖
```

**目标 (延迟渲染)：**
```
用户按下拖拽 → DoDragDrop(IDataObject) 立即响应
    → Explorer 接收拖拽 →
        用户放下文件 → Explorer 调用 GetData()
            → 提取单个文件到内存流 → COMStreamWrapper 传递数据
            → 展示进度窗口（此时才真正开始干活）
    → 清理
                  ↑
        拖拽即时响应，提取发生在 drop 之后
```

### COM 数据流详解

```
Explorer Drop 时调用：
  IDataObject.GetData(ref FORMATETC, out STGMEDIUM)
    ↓
  FORMATETC.cfFormat == CF_HDROP
    ↓
  我们构建 DROPFILES 结构：
    DROPFILES.pFiles = offset to file list
    DROPFILES.fWide  = true (UTF-16)
    files列表 = "C:\Temp\MantisZip\DragDrop\...\file1.txt\0file2.txt\0\0"
    ↓
  分配 HGLOBAL (GlobalAlloc) 写入结构
    ↓
  设置 STGMEDIUM.tymed = TYMED_HGLOBAL
          STGMEDIUM.hGlobal = handle
    ↓
  Explorer 读取 HGLOBAL 获得文件路径 → 开始常规文件复制操作
```

关键点：我们不需要流式提取。文件已经提取到 temp 了？不对，延迟渲染的核心是：我们只在 `GetData()` 被调用时才去提取文件。

但实际上对于 `CF_HDROP`，Explorer 期望的是**已经存在磁盘上的文件路径**。HGLOBAL 里放的是路径字符串数组。所以这个方案本质上还是要把文件先提取到 temp——但时机不同：

**急切提取 vs 延迟渲染：**

| 方案 | 提取时机 | 用户等待 | 问题 |
|------|---------|---------|------|
| 急切提取 | DoDragDrop **前** | 必须等全部提取完才能拖动 | 大文件多的场景体验差 |
| 延迟渲染 | GetData() **时**，即 drop 之后 | drop 后开始提取，提取完才开始复制 | 可以即时开始拖，但 drop 后仍有等待 |

延迟渲染的真正优势不是省去提取，而是：
1. **拖拽操作即时响应** — 用户无需等待提取即可开始拖动
2. **提取发生在后台** — drop 后才开始，进度窗口在 drop 后弹出
3. **取消更自然** — 如果用户拖到一半取消（按 ESC 或拖回原窗口），一次提取都不需要做
4. **ProgressWindow 展示时机合理** — 用户在放下的那一刻才看到进度，而不是在拖动前

---

## 实现方案

### 方案 A：纯 P/Invoke 实现 (推荐)

不依赖任何第三方库，直接用 P/Invoke 调用 Windows API。

#### 核心类型

```csharp
// COM IDataObject 接口（在 System.Runtime.InteropServices.ComTypes 中已有定义）
using System.Runtime.InteropServices.ComTypes;

// FORMATETC — 数据格式描述
[StructLayout(LayoutKind.Sequential)]
public struct FORMATETC
{
    public short cfFormat;       // 剪贴板格式
    public IntPtr ptd;           // 目标设备（通常为 null）
    public DVASPECT dwAspect;    // 呈现层面
    public int lindex;           // 索引（-1 表示全部）
    public TYMED tymed;          // 传输介质
}

// STGMEDIUM — 数据传输介质
[StructLayout(LayoutKind.Sequential)]
public struct STGMEDIUM
{
    public TYMED tymed;          // 介质类型
    public IntPtr unionMember;   // 实际数据指针（hGlobal / pUnkForRelease 等）
    public IntPtr pUnkForRelease;// Release 回调
}

// DROPFILES — 文件拖拽结构
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct DROPFILES
{
    public int pFiles;           // 到文件列表的偏移
    public int ptX;              // 拖放位置 X
    public int ptY;              // 拖放位置 Y
    [MarshalAs(UnmanagedType.Bool)]
    public bool fNC;             // 是否非客户区
    [MarshalAs(UnmanagedType.Bool)]
    public bool fWide;           // 是否 UTF-16
}
```

#### VirtualFileDataObject 实现

```csharp
public sealed class VirtualFileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject
{
    private readonly string[] _filePaths;
    // 注意：_filePaths 在构造时是「可能的路径」
    // 实际提取发生在 GetData 被调用时

    public VirtualFileDataObject(string[] filePaths)
    {
        _filePaths = filePaths;
    }

    public int QueryGetData(ref FORMATETC formatetc)
    {
        // 只支持 CF_HDROP + TYMED_HGLOBAL
        if (formatetc.cfFormat == (short)DataFormats.GetDataFormat(DataFormats.FileDrop).Id
            && (formatetc.tymed & TYMED.TYMED_HGLOBAL) != 0)
            return 0; // S_OK
        return unchecked((int)0x80040064); // DV_E_FORMATETC
    }

    public int GetData(ref FORMATETC formatetc, out STGMEDIUM medium)
    {
        medium = default;

        if (formatetc.cfFormat != (short)DataFormats.GetDataFormat(DataFormats.FileDrop).Id
            || (formatetc.tymed & TYMED.TYMED_HGLOBAL) == 0)
            return unchecked((int)0x80040064); // DV_E_FORMATETC

        // 在这里触发实际提取！
        // 提取 _filePaths 中的文件到临时目录
        // 然后构建 DROPFILES 结构
        var actualPaths = EnsureFilesExtracted();

        // 构建 DROPFILES 结构写入 HGLOBAL
        medium.tymed = TYMED.TYMED_HGLOBAL;
        medium.unionMember = BuildDropFiles(actualPaths);
        medium.pUnkForRelease = Marshal.GetIUnknownForObject(this);

        return 0; // S_OK
    }

    // ... 其他 IDataObject 方法返回 DV_E_FORMATETC
}
```

#### 实际提取触发点

```csharp
private string[] EnsureFilesExtracted()
{
    // 如果已经提取过，返回缓存路径
    if (_extractedPaths != null) return _extractedPaths;

    // 创建临时目录
    _dragTempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DragDrop", Guid.NewGuid().ToString());
    Directory.CreateDirectory(_dragTempDir);

    // 执行实际提取（异步转同步，因为 GetData 在 COM 线程）
    var results = new List<string>();
    foreach (var item in _itemsToDrag)
    {
        var outputPath = Path.Combine(_dragTempDir, item.FullPath);
        // 同步提取——需要同步版本的 ExtractEntry
        ExtractEntryForDragSync(item, outputPath);
        results.Add(outputPath);
    }

    _extractedPaths = results.ToArray();
    return _extractedPaths;
}
```

**⚠️ 线程问题：** `GetData()` 由 Explorer 在 OLE 线程上同步调用。不能直接使用 `async` 提取方法。有两种处理方式：

1. **同步提取** — 为需要提取的条目提供同步版本的 ExtractEntry。ArchiveEntryExtractor 底层用的是同步 Stream 操作，可以封装同步版本。
2. **阻塞等待** — `Task.Run(() => ExtractAsync()).GetAwaiter().GetResult()` — 简单但可能死锁，不推荐。

**推荐方案 1**，因为 `ArchiveEntryExtractor` 和 `TarInputStream` 的底层操作本身就是同步的，只有外层的 `ExtractEntryAsync` 是 `Task.CompletedTask` 包装的 async。

### 方案 B：VirtualFileDataObject 社区库

使用 [Microsoft.VisualStudio.OLE.Interop](https://www.nuget.org/packages/Microsoft.VisualStudio.OLE.Interop) 或社区封装（如 [DataObjectEx](https://github.com/jamOxtTea/DataObjectEx)），简化 COM 接口实现。

**优点：** 不需要手写 P/Invoke，直接用封装好的 `VirtualFileDataObject` 类
**缺点：** 引入外部依赖；部分库不再维护

```csharp
// 假设用某个封装库
var dataObject = new VirtualFileDataObject();
dataObject.SetData(new FileDescriptor("file1.txt", fileContentStream));
DragDrop.DoDragDrop(source, dataObject, DragDropEffects.Copy);
```

**推荐方案 A**：不引入外部依赖，完全自己实现，因为涉及的核心 API 不多（`GlobalAlloc`/`GlobalLock`/`GlobalFree` + COM 接口）。

---

## 改动范围

涉及 **5 个文件**：

| 文件 | 改动 | 预估工时 |
|------|------|---------|
| `UI/VirtualFileDataObject.cs` | 🆕 新增 — COM IDataObject 实现，含 DROPFILES 构建、GlobalAlloc 管理 | 2h |
| `UI/MainWindow.DragDrop.cs` | 修改 `FileListGrid_PreviewMouseMove` — 改用 VirtualFileDataObject 替代直接 DataObject；移除急切提取逻辑中的进度窗口；将提取触发移到 GetData 回调 | 1h |
| `UI/ProgressWindow.xaml/.cs` | 不修改或微调 — 延迟渲染模式下，ProgressWindow 在 drop 后、文件复制过程中展示；可能需要改为显示「正在将文件复制到目标…」而非提取进度 | 20min |
| `Core/Utils/ArchiveEntryExtractor.cs` | 可选 — 添加同步版本的 ExtractEntry 方法（`ExtractEntry` 不带 Async 后缀），因为 GetData 不能 async | 30min |
| `UI/App.cs` | 可选 — 添加拖拽提取的独立 temp 清理（当前是 DragDrop 方法结束时清理，延迟渲染模式下清理时机不同） | 15min |

**运行时依赖变更：** 无（只用 `System.Runtime.InteropServices` + `System.Runtime.InteropServices.ComTypes`，均已引用）

---

## 实现细节

### 1. VirtualFileDataObject.cs 完整骨架

```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace MantisZip.UI;

/// <summary>
/// COM 原生 IDataObject 实现，绕过 WPF OLE 桥的 CF_HDROP bug。
/// 延迟渲染：文件在 GetData 时才提取到临时目录。
/// </summary>
public sealed class VirtualFileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject
{
    private static readonly short FileDropFormat =
        (short)DataFormats.GetDataFormat(DataFormats.FileDrop).Id;

    private readonly Func<string[]> _extractor;
    private string[]? _extractedPaths;
    private string? _tempDir;
    private IntPtr _hGlobal;
    private bool _disposed;

    /// <summary>
    /// 创建 VirtualFileDataObject。
    /// </summary>
    /// <param name="extractor">
    /// 提取回调。首次 GetData 时调用，返回实际文件路径数组。
    /// 将在 OLE 线程上同步执行，请确保提取逻辑是同步的。
    /// </param>
    public VirtualFileDataObject(Func<string[]> extractor)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
    }

    ~VirtualFileDataObject() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (_hGlobal != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_hGlobal);
            _hGlobal = IntPtr.Zero;
        }
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* 静默清理失败 */ }
        }
        _disposed = true;
    }

    // ===== IDataObject 实现 =====

    int IDataObject.GetData(ref FORMATETC formatetc, out STGMEDIUM medium)
    {
        medium = default;
        if (formatetc.cfFormat != FileDropFormat
            || (formatetc.tymed & TYMED.TYMED_HGLOBAL) == 0)
            return DV_E_FORMATETC;

        // 只在首次 GetData 时执行提取
        if (_extractedPaths == null)
        {
            _extractedPaths = _extractor();
        }

        // 构建 DROPFILES 结构
        medium.tymed = TYMED.TYMED_HGLOBAL;
        medium.unionMember = BuildDropFiles(_extractedPaths);
        medium.pUnkForRelease = IntPtr.Zero; // 调用者负责释放
        _hGlobal = medium.unionMember;        // 跟踪以便 finalizer 清理
        return S_OK;
    }

    int IDataObject.GetDataHere(ref FORMATETC formatetc, ref STGMEDIUM medium) => DV_E_FORMATETC;

    int IDataObject.QueryGetData(ref FORMATETC formatetc)
    {
        if (formatetc.cfFormat == FileDropFormat
            && (formatetc.tymed & TYMED.TYMED_HGLOBAL) != 0)
            return S_OK;
        return DV_E_FORMATETC;
    }

    int IDataObject.GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return MK_E_SAME;
    }

    int IDataObject.SetData(ref FORMATETC formatetc, ref STGMEDIUM medium, bool release) => S_FALSE;

    int IDataObject.EnumFormatEtc(DATADIR direction, out IEnumFORMATETC? enumFormatetc)
    {
        enumFormatetc = null;
        return E_NOTIMPL;
    }

    int IDataObject.DAdvise(ref FORMATETC formatetc, ADVF advf, IAdviseSink? adviseSink, out int connection)
    {
        connection = 0;
        return OLE_E_ADVISENOTSUPPORTED;
    }

    int IDataObject.DUnadvise(int connection) => OLE_E_ADVISENOTSUPPORTED;

    int IDataObject.EnumDAdvise(out IEnumSTATDATA? enumAdvise)
    {
        enumAdvise = null;
        return OLE_E_ADVISENOTSUPPORTED;
    }

    // ===== 辅助方法 =====

    private static IntPtr BuildDropFiles(string[] filePaths)
    {
        // 计算 DROPFILES 结构 + 文件列表所需总大小
        int structSize = Marshal.SizeOf<DROPFILES>();
        int stringSize = filePaths.Sum(p => (p.Length + 1) * 2); // 每个路径 + null 终止符 * UTF-16
        stringSize += 2; // 末尾双 null 终止

        IntPtr hGlobal = Marshal.AllocHGlobal(structSize + stringSize);

        // 写入 DROPFILES 结构
        var dropFiles = new DROPFILES
        {
            pFiles = structSize,           // 从结构开头偏移到文件列表
            ptX = 0,
            ptY = 0,
            fNC = false,
            fWide = true                   // UTF-16
        };
        Marshal.StructureToPtr(dropFiles, hGlobal, false);

        // 写入文件列表 (UTF-16, null-terminated)
        IntPtr stringPtr = hGlobal + structSize;
        foreach (var path in filePaths)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(path + '\0');
            Marshal.Copy(bytes, 0, stringPtr, bytes.Length);
            stringPtr += bytes.Length;
        }

        // 末尾双 null
        Marshal.WriteInt16(stringPtr, 0);

        return hGlobal;
    }
}

// ===== Win32 常量 =====
internal static class Win32HResult
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int DV_E_FORMATETC = unchecked((int)0x80040064);
    public const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
    public const int MK_E_SAME = unchecked((int)0x800401E3);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct DROPFILES
{
    public int pFiles;
    public int ptX;
    public int ptY;
    [MarshalAs(UnmanagedType.Bool)]
    public bool fNC;
    [MarshalAs(UnmanagedType.Bool)]
    public bool fWide;
}
```

### 2. MainWindow.DragDrop.cs 修改

```csharp
// 在 FileListGrid_PreviewMouseMove 中，替换：

// 旧代码 — 急切提取 + DoDragDrop
// pw.SetProgress(100, "⏳ 正在拖拽 — 放到目标位置以复制文件");
// _isOwnDrag = true;
// DragDrop.DoDragDrop(FileListGrid, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
// _isOwnDrag = false;

// 新代码 — 延迟渲染
_isOwnDrag = true;

// 构造 VirtualFileDataObject，传入提取回调
// 提取只在 GetData()（即用户放下文件时）执行
var vfdo = new VirtualFileDataObject(() =>
{
    // 同步提取所有文件
    var syncExtractor = new DragDropExtractor(_currentArchivePath!, _currentFormat, _currentPassword);
    var paths = syncExtractor.ExtractAll(filesToDrag, _dragTempDir);
    return paths;
});

DragDrop.DoDragDrop(FileListGrid, vfdo, DragDropEffects.Copy);
// DoDragDrop 是阻塞的，执行到这里时已经完成 drop
// VirtualFileDataObject 的析构函数会清理临时文件
_isOwnDrag = false;
```

**注意：**
- `VirtualFileDataObject` 的清理（删除临时文件）必须在 Explorer 完成文件复制之后。当前 `IDataObject` 的实现无法感知 Explorer 何时完成复制。
- **解决方案：** 不在 `Dispose` 中清理，而是通过 `DoDragDrop` 返回后**延迟一段时间再清理**，或者使用 `Shell32.SHFileOperation` 检测文件操作完成。
- 更稳妥的做法：记录临时目录路径，在 `DoDragDrop` 返回后等待几秒再清理（通过 `Task.Delay` + 重试机制）。

### 3. 同步提取封装

```csharp
/// <summary>
/// 拖拽提取辅助类，提供同步提取（用于 GetData 回调）。
/// </summary>
internal class DragDropExtractor
{
    private readonly string _archivePath;
    private readonly ArchiveFormat _format;
    private readonly string? _password;

    public DragDropExtractor(string archivePath, ArchiveFormat format, string? password)
    {
        _archivePath = archivePath;
        _format = format;
        _password = password;
    }

    public string[] ExtractAll(List<ArchiveItem> items, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        var results = new List<string>();

        foreach (var item in items)
        {
            var outputPath = Path.Combine(tempDir, item.FullPath);
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            ExtractSingle(item, outputPath);
            results.Add(outputPath);
        }

        return results.ToArray();
    }

    private void ExtractSingle(ArchiveItem item, string outputPath)
    {
        // 同步版本 — ArchiveEntryExtractor 底层就是同步的，去掉 async 包装
        switch (_format)
        {
            case ArchiveFormat.Zip:
            case ArchiveFormat.SevenZip:
                ArchiveEntryExtractor.ExtractEntry(
                    _archivePath, item.FullPath, outputPath, _format, _password);
                break;
            case ArchiveFormat.Tar:
            case ArchiveFormat.GZip:
                ExtractTarGzSingleEntry(_archivePath, item.FullPath, outputPath);
                break;
            default:
                throw new NotSupportedException($"格式 {_format} 不支持拖拽提取");
        }
    }

    private static void ExtractTarGzSingleEntry(string archivePath, string entryName, string outputPath)
    {
        // 从 MainWindow.DragDrop.cs 搬过来
        // 略（与现有 ExtractTarGzSingleEntry 逻辑一致）
    }
}
```

### 4. ArchiveEntryExtractor 添加同步版本

在 `[ArchiveEntryExtractor.cs](file:///E:/GitHub/MantisZip/src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs)` 中添加：

```csharp
/// <summary>
/// 同步提取条目到指定路径（用于 COM 回调等不能使用 async 的场景）。
/// </summary>
public static void ExtractEntry(
    string archivePath, string entryName, string outputPath,
    ArchiveFormat format, string? password)
{
    // 内容与 ExtractEntryAsync 相同，去掉 async/await 包装
    // 底层 Stream 操作本身就是同步的
    ExtractEntryImpl(archivePath, entryName, outputPath, format, password);
}

private static void ExtractEntryImpl(
    string archivePath, string entryName, string outputPath,
    ArchiveFormat format, string? password)
{
    // 从 ExtractEntryAsync 中提取出的同步逻辑
    // 包括 ZIP 处理、7z 处理
}
```

这样做的好处：
- `ExtractEntryAsync` 内部直接调用 `ExtractEntryImpl`
- 新 `ExtractEntry` 也调用 `ExtractEntryImpl`
- 无代码重复，提供同步、异步双入口

### 5. 临时文件生命周期管理

延迟渲染最大的挑战是：**何时清理临时文件？**

| 时机 | 问题 |
|------|------|
| `DoDragDrop` 返回后立即清理 | Explorer 可能还没读完文件 → 复制失败 |
| `IDataObject` 析构时清理 | 析构时机不确定，可能过早或过晚 |
| 通过 `pUnkForRelease` 回调 | `STGMEDIUM.pUnkForRelease` 在 Explorer 释放数据时调用，但不同 Explorer 版本行为不一致 |

**推荐方案：分层策略**

```csharp
// 第一层：DoDragDrop 返回后，延迟 5 秒尝试清理
// 第二层：如果文件被占用（Explorer 仍在使用），跳过本次，App 退出时统一清理

// App.xaml.cs 中维护一个全局待清理列表
internal static readonly List<string> PendingTempDirs = new();

// VirtualFileDataObject 不自己清理，而是将路径加入待清理列表
// App.OnExit 时批量清理
```

### 6. DragDropExtractor 进度回调

延迟渲染模式下，提取发生在 `GetData` 时，此时 UI 线程可能在 `DoDragDrop` 的消息循环中。进度展示有两种方案：

| 方案 | 方式 | 复杂度 |
|------|------|--------|
| A：无进度 | 提取期间不显示进度（提取通常很快） | 🟢 低 |
| B：Dispatcher 推送 | 在提取回调中通过 `Dispatcher.BeginInvoke` 更新 ProgressWindow | 🟡 中 |

**推荐 Phase 1 用方案 A**，因为延迟渲染本身就是为了消除「拖拽前的进度等待」。提取发生在拖拽放下后，如果是同盘复制，文件数据从 temp 复制到目标目录的时间占主导，提取本身只占小部分。

如果用户反馈需要进度提示，再实现方案 B。

---

## 与现有系统的集成

### 拖拽冲突保护

当前的 `_isOwnDrag` 标志仍然有效：

```csharp
if (_isOwnDrag) return; // 在 Window_Drop 中防止自我循环
```

### 设置项

`AppSettings.EnableDragExtract` 开关继续有效。开启时使用 VirtualFileDataObject 延迟渲染，关闭时拖拽功能完全禁用（当前逻辑已有）。

### 取消

延迟渲染模式下，取消机制需要调整：

- 当前：提取发生在 `DoDragDrop` 前，通过 ProgressWindow 的 CancellationToken 取消
- 延迟：提取发生在 `GetData()` 时，此时已经在 `DoDragDrop` 内部。用户可以通过按 `Esc` 取消 OLE 拖拽操作，此时 `GetData` 不会被调用，提取不会发生

---

## 迁移计划

### Phase 1：基础实现

1. 实现 `VirtualFileDataObject`（COM IDataObject）
2. 添加 `ArchiveEntryExtractor.ExtractEntry` 同步方法
3. 修改 `MainWindow.DragDrop.cs` 使用新对象
4. 验证：拖拽单文件、多文件、子目录文件到不同位置（桌面、文件夹、同盘、异盘）

### Phase 2：稳定性

1. 临时文件生命周期管理（延迟清理 + App 退出清理）
2. 大文件拖拽测试（1GB+，观察 GetData 调用时机）
3. 取消拖拽测试（按 Esc、拖回原窗口）

### Phase 3：体验优化

1. 如果需要，添加进度回调（提取进度显示在 ProgressWindow）
2. 支持拖拽只读/加密文件

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| Explorer 调用 GetData 的时机不确定 | 🟡 | 延迟渲染的特性——按需提取，反正用户已经在拖拽了，提取一下无所谓 |
| 临时文件清理过早导致复制失败 | 🟡 | 分层策略：延迟 5 秒 + App 退出时兜底清理 |
| 同步提取阻塞 OLE 线程 | 🟡 | 小文件提取很快（ms 级）；大文件提取时 OLE 消息泵仍在运行，UI 不会完全冻结 |
| 不同 Windows 版本 Explorer OLE 行为差异 | 🟡 | 在 Win10 1809+ 和 Win11 上分别测试 |
| 加密文件提取需密码输入 | 🟡 | GetData 在 OLE 线程上不能弹 UI 对话框；需在构造时传入密码或预验证 |
| 取消拖拽（按 Esc）后仍提取 | 🟢 | 检查 `QueryGetData` 是否被调用；如果取消时 OLE 不调用 GetData 则不会提取 |
| DROPFILES.fWide 与旧版 Explorer 兼容 | 🟢 | 自 Vista 起 Explorer 完全支持 UTF-16 |

### 加密文件特殊处理

加密文件需要密码才能提取。在延迟渲染模式下，GetData() 回调无法弹出密码输入框（不在 UI 线程上）。

**解决方案：**
1. 预验证：`VirtualFileDataObject` 构造时尝试 `QuickVerifyPassword`，如果当前 `_currentPassword` 无效，在构造 `VirtualFileDataObject` **之前**（还在 UI 线程中）弹出密码对话框。
2. 如果用户取消密码输入，则不启动拖拽。

```csharp
var itemsToDrag = selectedItems.Where(i => !i.IsDirectory).ToList();

// 预检查加密文件
var hasEncrypted = itemsToDrag.Any(i => i.IsEncrypted);
if (hasEncrypted && !await QuickVerifyPasswordAsync())
{
    // 弹出密码输入（复用现有逻辑）
    if (!await PromptPasswordAsync()) return;
}

// 构造 VirtualFileDataObject
var vfdo = new VirtualFileDataObject(() =>
{
    var extractor = new DragDropExtractor(_currentArchivePath!, _currentFormat, _currentPassword);
    return extractor.ExtractAll(itemsToDrag, _dragTempDir);
});
```

---

## 测试要点

| 测试场景 | 预期 |
|---------|------|
| 拖拽 1 个文本文件到桌面 | 文件正常复制，无崩溃 |
| 拖拽 10 个文件（含中文名）到文件夹 | 全部复制成功，文件名正确 |
| 拖拽子目录中的文件 | 保持目录结构 |
| 拖拽加密压缩包中的文件 | 提取成功（需已输入密码） |
| 拖拽到一半按 Esc 取消 | 无 temp 残留 |
| 拖拽后立即关闭应用 | App.OnExit 清理残留 temp |
| 大文件 (500MB+) 拖拽 | ProgressWindow 显示提取进度（Phase 2+）|
| Win10 1809 / Win11 分别测试 | 兼容性验证 |

---

## 后续扩展

- **ProgressWindow 集成**：在同步提取循环中各文件完成后通过 `Dispatcher.BeginInvoke` 更新 UI
- **流式提取**：对超大文件，`IDataObject` 支持 `TYMED_ISTREAM` 或 `TYMED_ISTORAGE`，直接流式传递而无需写入磁盘 temp（但 CF_HDROP 不支持流式，需要其他剪贴板格式）
- **VirtualFileDataObject + ContextMenuHandler 共用**：两者都涉及 COM 接口实现，可以提取为共享辅助库
- **进度估算**：提前计算总大小，在提取回调中反馈给 UI

---

## 参考

- [WPF OLE 桥源码 (v8.0.1)](https://github.com/dotnet/wpf/tree/v8.0.1/src/Microsoft.DotNet.Wpf/src/Shared/MS/Win32) — `IComDataObject` 实现，确认 `CF_HDROP` 转换 bug
- [DROPFILES 结构文档](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-dropfiles) — MSDN
- [IDataObject 接口](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-idataobject) — COM 原生接口规范

---

## Definition of Done

- [ ] `VirtualFileDataObject` 实现 COM `IDataObject` 接口，支持 `CF_HDROP`
- [ ] `COMStreamWrapper` 将 .NET Stream 暴露为 COM IStream
- [ ] 拖拽流程从急切提取改为延迟渲染
- [ ] Explorer 不崩溃（验证 Win10/Win11）
- [ ] 拖拽文件含中文名正常
- [ ] 子目录结构保持
- [ ] 加密压缩包文件提取正常
- [ ] 取消拖拽时无 temp 残留
- [ ] VFDO 不可用时有回退方案
- [ ] `dotnet build` 通过

### Final Checklist

- [ ] `DoDragDrop` 时无急切提取，立即响应
- [ ] 释放鼠标后开始提取文件
- [ ] Explorer 不崩溃（解决 WPF OLE 桥 bug）
- [ ] 拖拽 1 个 / 多个文件均正常
- [ ] 中文文件名正常
- [ ] 子目录结构保持
- [ ] 加密压缩包密码已输入时正常
- [ ] 拖拽取消无残留
- [ ] Win10 1809+ / Win11 兼容
- [GlobalAlloc / GlobalLock](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-globalalloc) — HGLOBAL 内存管理
