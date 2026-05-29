# ICO 文件列表图标 — 显示文件自身嵌入图标

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜] (0/5)

## TL;DR

> **Quick Summary**: 现在 `.ico` 文件在文件列表中显示为 Windows Shell 关联的通用图标（空白文档带画笔），改为显示该 ICO 文件**自身嵌入的图标图像**——即"这个图标文件长什么样"。
>
> **Deliverables**:
> - `IcoIconHelper` — 从 ICO 字节解析出最合适的帧作为 `ImageSource`
> - 引擎新增 `ReadEntryBytesAsync` — 将压缩包内单个条目读入 `byte[]`
> - 文件列表异步图标加载 — `.ico` 文件先给默认图标，后台逐个提取、解析、替换
>
> **Estimated Effort**: 小（2-3 小时）
> **Parallel Execution**: YES — Task 1 与 Task 2 可并行
> **Critical Path**: Task 1, 2 → Task 3 → Task 4 → Task 5

---

## Context

### Original Request

> 文件列表里扩展名 ico 的文件会显示为一个空白图标，我希望它显示这个 ico 文件本身的图标。

### Current Behavior

`MainWindow.xaml.cs:601-603` 设置图标时，对 `.ico` 文件走统一的 `SystemIconHelper.GetFileIcon(".ico")`，内部用虚拟文件名 `"file.ico"` 调 `SHGetFileInfo`。Windows Shell 返回的是 **.ico 扩展名的关联图标**（空白文档带小画笔），而不是每个 ICO 文件**自身嵌入的图标图像**。

另外 `SystemIconHelper` 按扩展名缓存（key = `".ico"`），所以所有 `.ico` 文件图标完全相同。

### Design Decisions

| 项目 | 决策 |
|------|------|
| 图标选取 | 取最接近 16×16 的帧（列表图标就是 16×16）|
| 帧格式 | 同时支持 ICO 内嵌 BMP 帧和 PNG 帧 |
| 加载时机 | 异步加载：列表先显示占位图标，后台逐文件提取解析 |
| 引擎接口 | 新增 `ReadEntryBytesAsync`，不修改现有 `ListEntriesAsync`/`ExtractEntryAsync` |

### ICO 容器格式简述

```
+0  reserved (2)   = 0
+2  type      (2)   = 1 (icons)
+4  count     (2)
+6  entries[]       = count * 16 bytes (w, h, colors, planes, bpp, size, offset)
    image data      = BMP (with BITMAPINFOHEADER) or PNG (PNG sig)
```

.NET 的 `BitmapDecoder` 原生支持 ICO（每帧对应一个 icon entry），拿到字节后用 `MemoryStream` + `BitmapDecoder.Create` 即可提取各帧。

---

## 任务清单

- [ ] **1. 新增 `IcoIconHelper`** — ICO 字节 → 16×16 `ImageSource`
- [ ] **2. 引擎新增 `ReadEntryBytesAsync`** — 压缩包条目 → `byte[]`
- [ ] **3. 文件列表异步图标加载** — 加载时占位 + 后台解析 `.ico`
- [ ] **4. 本地化字符串** — 如果有需要用户感知的状态信息
- [ ] **5. 单元测试** — `IcoIconHelper` 测试 + 集成验证

---

## 任务详情

### Task 1 — `IcoIconHelper`（新增文件）

**文件**: `src/MantisZip.UI/IcoIconHelper.cs`

**行为**:

```
IcoIconHelper.ExtractIcon(byte[] icoData) → ImageSource?
```

- 从字节流创建 `MemoryStream`
- 用 `BitmapDecoder.Create(stream, ...)` 解码 ICO
- 遍历所有帧，选取最接近 16×16 的帧（按 `abs(w*h - 256)` 排序）
- 选中帧 `Freeze()` 后返回
- 不能解码的（非 ICO 数据、损坏文件等）返回 `null`

**边界情况**:
- 多帧 ICO：选最接近 16×16 的那一帧
- 单帧 ICO：直接返回该帧
- 损坏 / 空数据：返回 null，调用方 fallback 到系统默认图标
- BMP 内嵌帧（含 alpha）与 PNG 内嵌帧均被 `BitmapDecoder` 统一处理，无需区分

**参考**: 预览面板已用同样方式解码 ICO（`MainWindow.Preview.Image.cs:121-136`），可复用思路。

---

### Task 2 — 引擎新增 `ReadEntryBytesAsync`

**涉及文件**:
- `src/MantisZip.Core/Abstractions/ArchiveEngine.cs`
- `src/MantisZip.Core/Engines/ZipEngine.cs`
- `src/MantisZip.Core/Engines/SevenZipEngine.cs`
- `src/MantisZip.Core/Engines/TarGzEngine.cs`

**接口变更**:

```csharp
// IArchiveEngine 新增
public interface IArchiveEngine
{
    // ...现有接口...
    
    /// <summary>
    /// 读取压缩包中指定条目的全部字节（不解压到磁盘）
    /// </summary>
    Task<byte[]?> ReadEntryBytesAsync(string archivePath, string entryName, 
        string? password = null, CancellationToken cancellationToken = default);
}
```

**各引擎实现**:

| 引擎 | 实现方式 |
|------|---------|
| `ZipEngine` | SharpCompress — 打开 `ZipArchive` → 定位 entry → `MemoryStream` 读取 |
| `SevenZipEngine` | SharpSevenZip — 用 `SevenZipExtractor.ExtractEntry` 到 `MemoryStream` |
| `TarGzEngine` | SharpCompress — 类似 Zip 方式处理 |

**注意**: 不修改 `ListEntriesAsync` 现有行为，新增方法只在需要读取文件内容时调用。

---

### Task 3 — 文件列表异步图标加载

**涉及文件**: `src/MantisZip.UI/MainWindow.xaml.cs`、`src/MantisZip.UI/MainWindow.UI.cs`

**改动思路**:

1. **`ArchiveItem` 新增属性**（可选）:
   ```csharp
   public bool IsIconLoading { get; set; } // 用于 UI 绑定状态显示
   ```

2. **`LoadArchiveAsync` 加载流程**:
   - `.ico` 文件在 `.Select()` 阶段先给一个**默认占位图标**（`SystemIconHelper.GetFileIcon(".ico")` 返回的通用图标）
   - 保持现有同步路径不变，不阻塞列表显示
   - 列表显示后，后台遍历 `_allItems` 中所有 `.ico` 文件

3. **异步图标替换**:
   ```csharp
   // LoadArchiveAsync 末尾
   _ = Task.Run(async () =>
   {
       var engine = ArchiveEngineFactory.GetEngine(_currentFormat);
       if (engine == null) return;
       
       foreach (var item in _allItems.Where(i => 
           !i.IsDirectory && Path.GetExtension(i.Name).Equals(".ico", StringComparison.OrdinalIgnoreCase)))
       {
           try
           {
               var bytes = await engine.ReadEntryBytesAsync(_currentArchivePath!, item.FullPath, 
                   _currentPassword, _previewCts?.Token ?? CancellationToken.None);
               if (bytes == null) continue;
               
               var icon = IcoIconHelper.ExtractIcon(bytes);
               if (icon == null) continue;
               
               await Dispatcher.InvokeAsync(() => item.IconSource = icon);
           }
           catch { /* 静默 fallback，保留占位图标 */ }
       }
   });
   ```

**延迟考虑**:
- 如果压缩包里有大量 `.ico`（如 100+），后台逐个提取会占用 I/O
- 建议加一个**上限**：超过 N 个 `.ico` 文件时跳过异步加载（保留占位图标），N 暂定 20
- 在状态栏显示 "正在加载 N 个文件图标…" 提供反馈

---

### Task 4 — 本地化字符串

**涉及文件**: `src/MantisZip.UI/Localization/L.cs` + 对应翻译文件

可能需要的字符串：
- `"LoadingIcons"` → "正在加载文件图标…" / "Loading file icons…"

（如果 Task 3 不做状态栏提示则可跳过此任务。）

---

### Task 5 — 单元测试

**文件**: `tests/MantisZip.Tests/IcoIconHelperTests.cs`（新增）

测试场景：

| 场景 | 输入 | 预期 |
|------|------|------|
| 标准 16×16 单帧 ICO | 已知 ICO 字节 | 返回非空 `ImageSource` |
| 多帧 ICO（16+32+48） | 已知多帧 ICO | 返回 16×16 帧 |
| 损坏数据 | `new byte[] { 0, 0, 0, 0 }` | 返回 null |
| 空数据 | `Array.Empty<byte>()` | 返回 null |
| 非 ICO 数据（PNG 伪装） | 一个普通 PNG 文件 | 返回 null / fallback |

---

## 涉及文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `src/MantisZip.UI/IcoIconHelper.cs` | **新增** | ICO 字节 → ImageSource 解析 |
| `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | **修改** | `IArchiveEngine` 新增 `ReadEntryBytesAsync` |
| `src/MantisZip.Core/Engines/ZipEngine.cs` | **修改** | 实现 `ReadEntryBytesAsync` |
| `src/MantisZip.Core/Engines/SevenZipEngine.cs` | **修改** | 实现 `ReadEntryBytesAsync`（或 `NotSupportedException`） |
| `src/MantisZip.Core/Engines/TarGzEngine.cs` | **修改** | 实现 `ReadEntryBytesAsync` |
| `src/MantisZip.UI/MainWindow.xaml.cs` | **修改** | `LoadArchiveAsync` 异步图标替换 |
| `src/MantisZip.UI/MainWindow.UI.cs` | **修改** | `ArchiveItem` 可选新增 `IsIconLoading` |
| `src/MantisZip.UI/Localization/L.cs` | **修改** | 本地化字符串（可选） |
| `tests/MantisZip.Tests/IcoIconHelperTests.cs` | **新增** | 单元测试 |

---

## Commit Strategy

- **1**: `feat(core): add IArchiveEngine.ReadEntryBytesAsync and implementations`
- **2**: `feat(ui): add IcoIconHelper for extracting icon from .ico byte data`
- **3**: `feat(ui): lazy-load actual icons for .ico files in file list`
- **4**: `test(core): add IcoIconHelper unit tests`
- **5**: `chore(i18n): add icon loading status strings`（可选）

---

## Success Criteria

### Verification Commands

```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj       # 0 errors
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj   # all pass
```

### Final Checklist

- [ ] `.ico` 文件在列表中显示自身嵌入图标（而非通用关联图标）
- [ ] 多帧 ICO 正确选取最接近 16×16 的帧
- [ ] 损坏/空 ICO 文件 fallback 到系统默认图标
- [ ] 超过 20 个 `.ico` 文件时跳过异步加载（不卡 UI）
- [ ] 非 `.ico` 的文件图标不受影响
- [ ] 文件夹图标不受影响
- [ ] 预览面板 ICO 预览不受影响
- [ ] 所有引擎的 `ReadEntryBytesAsync` 实现正常（或不支持的引擎抛 `NotSupportedException`）
- [ ] `dotnet build` 通过
- [ ] `dotnet test` 通过
