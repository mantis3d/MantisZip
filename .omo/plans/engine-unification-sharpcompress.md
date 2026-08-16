# 引擎统一计划：SharpZipLib → SharpCompress + 7z.exe → SevenZipSharp

> 替换 SharpZipLib + 外置 7z.exe 进程调用，统一所有格式的压缩/解压实现
> **状态**: ✅ 已完成 | **阶段**: [✅✅✅✅] (4/4)

---

## 动机

| 现状（SharpZipLib） | 目标（SharpCompress） |
|------|------|
| 最后一次发布 2023 年（1.4.2） | 仍在积极维护 |
| ZIP 解析严格，碰到非标准 Zip64 直接抛异常 | 解析更宽容，兼容各种工具生成的 ZIP |
| 每种格式不同 API（ZipFile / TarInputStream / GZipInputStream） | 统一 `IArchive` / `IReader` 接口 |
| `ZipStrings.CodePage` 是进程级全局副作用 | `ReaderOptions.ArchiveEncoding` 按实例设置 |
| 阻塞 I/O | 原生 async/await |
| 不支持原地修改压缩包 | 同样不支持（方案一致） |
| `BeginUpdate/CommitUpdate` 黑盒，旧条目 I/O 阶段无法上报进度 | 手动解压全量 → 合并 → 重压缩，可逐条目上报进度，消除跳变 |

> **同时替换 7z.exe 外置进程调用** — 见 Phase 4，作为可选项

---

## 改动范围

涉及 **8 个文件**：

| 文件 | 依赖度 | 预估工时 |
|------|--------|---------|
| `Core/Abstractions/ArchiveEngine.cs` | 🟡 接口扩展 — 新增 `ExtractEntriesAsync` | 15min |
| `Core/Engines/ZipEngine.cs` | 🔴 最重 — 读写、更新、测试全用 SharpZipLib | 2-3h |
| `Core/Engines/SevenZipEngine.cs` | 🟡 中等 — 7z 压缩（CompressAsync / AddToArchiveAsync） | ~3h |
| `Core/Engines/TarGzEngine.cs` | 🟢 轻 — Tar/GZip 流操作 | 30min |
| `Core/Utils/ArchiveEntryExtractor.cs` | 🟢 轻 — SharpCompress 简化，保留作为过滤组件 | 15min |
| `UI/App.xaml.cs` | 🟢 轻 — QuickVerifyPassword | 10min |
| `UI/MainWindow.DragDrop.cs` | 🟢 轻 — 拖拽时 GZip 提取 | 10min |
| `UI/MainWindow.xaml.cs` | 🟢 轻 — 只有引用/调用 | — |

**依赖变更：**

| 包 | 操作 |
|----|------|
| `SharpZipLib 1.4.2` (Core) | ❌ → ⏸️ 待 Phase 5 移除（仍在 ZipEngine + ArchiveCommentDialog 中使用） |
| `SharpZipLib 1.4.2` (UI) | ⏸️ 待 Phase 5 添加（仅 ArchiveCommentDialog 原地改注释需要） |
| `SharpCompress 0.48.1` | ➕ 已添加（替代 SharpZipLib ZIP/TAR/GZ，但 ZipEngine 仍有 SharpZipLib 残留） |
| `SharpSevenZip 2.0.12` | ➕ 已添加（替代 7z.exe + SevenZipExtractor） |
| `SevenZipExtractor 1.0.19` | ❌ 已移除 |
| `7z.dll` | ➕ 随应用分发（LGPL） |

---

## 分阶段实现

### Phase 1：TarGzEngine + DragDrop GZip ✅

- [x] TarGzEngine 全部方法从 SharpZipLib 迁移到 SharpCompress API
- [x] MainWindow.DragDrop.cs GZip 提取使用新的 SharpCompress API
- [x] 验证：打开 .tar.gz 正常列出/提取/预览；拖拽 tar.gz 内文件正常

**文件：** `TarGzEngine.cs` + `MainWindow.DragDrop.cs`

**SharpZipLib → SharpCompress API 映射：**

| SharpZipLib | SharpCompress |
|------------|---------------|
| `GZipOutputStream` | `GZipStream` (writer mode) |
| `GZipInputStream` | `GZipStream` (reader mode) |
| `TarOutputStream` | `TarWriter` |
| `TarInputStream` | `TarReader` |
| `TarArchive.CreateOutputTarArchive` | `TarWriter.Create` |
| `TarArchive.CreateInputTarArchive` | `TarReader.Open` |

**编码量：** 两个文件各约 150 行重写。

**验证：** 打开 `.tar.gz` 正常列出/提取/预览；拖拽 tar.gz 内文件正常。

---

### Phase 2：ArchiveEntryExtractor + QuickVerifyPassword ✅

- [x] ArchiveEntryExtractor 从 SharpZipLib 迁移到 SharpCompress
- [x] App.xaml.cs / MainWindow.xaml.cs 中相关代码迁移
- [x] Zip/Tar/Gz 单项提取、预览、拖拽全部正常工作

**文件：** `ArchiveEntryExtractor.cs` + `App.xaml.cs` + `MainWindow.xaml.cs`

**ArchiveEntryExtractor：**

```csharp
// 当前 (SharpZipLib)
using var zipFile = new ZipFile(archivePath);
var entry = zipFile.GetEntry(entryName);
using var inStream = zipFile.GetInputStream(entry);
inStream.CopyTo(outStream);

// 替换为 (SharpCompress)
using var archive = ArchiveFactory.Open(archivePath);
var entry = archive.Entries.FirstOrDefault(e => e.Key == entryName);
if (entry != null) entry.WriteTo(outputPath);
```

SharpCompress 的 `WriteTo(Stream)` 使单条目提取变得极简。

**QuickVerifyPassword：**

```csharp
// 当前: ZipFile + ZipEntry.IsCrypted / AESKeySize
// 替换: SharpCompress 的 ZipArchive + ZipArchiveEntry
```

`App.xaml.cs` 中 `using ICSharpCode.SharpZipLib.Zip` 和 `MainWindow.xaml.cs` 中的对应引用均删除。

**验证：** 预览 ZIP/7z 内文件正常；密码匹配正常。

> **💡 过滤功能前置：保留 ArchiveEntryExtractor 作为基础组件**
>
> 虽然 SharpCompress 让单条目提取变得极简，但过滤功能的解压路径需要这个能力——用户筛选后只提取部分条目。
> 此文件用 SharpCompress 重写（~30 行）后**保留**，作为过滤功能的基础组件，后续由过滤计划直接调用。
> 名称可简化保持 `ArchiveEntryExtractor`，或后续重构为 `ArchiveEntryExtractor` + `EntryExtractorOptions`。

---

### Phase 3：ZipEngine ✅

**文件：** `ZipEngine.cs`（~350 行完整重写）

- [x] **3.1** `ListEntriesAsync` / `ExtractAsync` / `TestArchiveAsync` 迁移
- [x] **3.2** `CompressAsync` 迁移
- [x] **3.3** `AddToArchiveAsync` / `DeleteEntriesAsync` 迁移
- [x] **3.4** `IArchiveEngine` 新增 `ExtractEntriesAsync` 接口
- [x] **3.5** 编码处理（UTF-8/GBK 回退）正确性验证

#### 3.1 读取（ListEntriesAsync / ExtractAsync / TestArchiveAsync）

| SharpZipLib | SharpCompress |
|------------|---------------|
| `ZipFile(archivePath)` | `ArchiveFactory.Open(path, readerOptions)` |
| `zipFile.Cast<ZipEntry>()` | `archive.Entries` |
| `entry.Name` / `entry.IsDirectory` / `entry.Size` / `entry.CompressedSize` / `entry.DateTime` | `entry.Key` / `entry.IsDirectory` / `entry.Size` / `entry.CachedSize` / `entry.LastModifiedTime` |
| `zipFile.GetInputStream(entry)` | `entry.OpenEntryStream()` |
| `TestZipFile.TestArchive(zipFile)` | 遍历所有条目读取验证 |
| `entry.IsCrypted` / `entry.AESKeySize` | `entry.IsEncrypted` |

**编码处理对比：**

```csharp
// SharpZipLib（全局副作用）
ZipStrings.CodePage = 65001;
var zip = new ZipFile(path);
if (entries.Any(e => !e.IsUnicodeText))
{
    zip.Close();
    ZipStrings.CodePage = 936;
    zip = new ZipFile(path);
}

// SharpCompress（按实例设置，无副作用）
var options = new ReaderOptions
{
    ArchiveEncoding = new ArchiveEncoding
    {
        Default = Encoding.GetEncoding("gbk") // 或 UTF-8
    }
};
using var archive = ArchiveFactory.Open(path, options);
```

SharpCompress 需要一个 UTF-8 → GBK 回退策略。目前方案：

```csharp
// 1. 先以 UTF-8 尝试打开
// 2. 如果条目名有乱码特征（高位 ASCII），回退到 GBK
// 3. 回退需要重新打开 archive（ArchiveFactory.Open 是只读的）
```

也可以参考 SharpCompress 的 `ZipArchive` 直接调用 `ZipArchive.DetectEncoding()` 辅助方法。

#### 3.2 压缩（CompressAsync）

| SharpZipLib | SharpCompress |
|------------|---------------|
| `ZipOutputStream` + `ZipEntry` | `ZipWriter` + `writer.Write(entryName, stream)` |
| `ZipEntry.DateTime = ...` | `writer.Write(entryName, stream, modifiedTime)` |
| `SetLevel(compressionLevel)` | `ZipWriterOptions.CompressionLevel` / `DeflateCompressionLevel` |

```csharp
// SharpCompress 压缩示例
using var zipWriter = ZipWriter.Open(outputStream, new ZipWriterOptions
{
    CompressionType = CompressionType.Deflate,
    DeflateCompressionLevel = options.CompressionLevel switch
    {
        <= 3 => DeflateCompressionLevel.Level1,
        <= 6 => DeflateCompressionLevel.Level3,
        _ => DeflateCompressionLevel.Level9,
    }
});
await foreach (var file in sourceFiles)
    zipWriter.Write(file.RelativePath, File.OpenRead(file.FullPath));
```

进度提醒替换：
- 当前：手动 100ms throttle + 循环报告
- SharpCompress 无内置进度事件，仍需要手动报告

#### 3.3 更新（AddFilesToCurrentArchiveAsync / DeleteEntriesAsync）— 最大改动点

**进度修复（核心改进）：**

SharpZipLib 的 `CommitUpdate()` 是黑盒操作，调用后所有旧条目的 I/O 复制期间无法上报进度。用户感知：进度条走到 ~50% 后卡住，然后直接跳到 100%。

SharpCompress 的"解压全量 → 合并 → 重压缩"方案解决了此问题，以下是详细进度报告方案：

```
旧方式（SharpZipLib）：【BUG — 进度条跳跃】
  1. zipFile.Add(新文件) x N       →  快速走到 ~50%（只注册元数据，无 I/O）
  2. CommitUpdate()                 →  进度卡住（真正的 I/O 在这里发生！）
  3. 上报 100%                      →  直接从卡住位置跳到 100%

新方式（SharpCompress）：【修复 — 逐阶段真实进度】
  Step 1: 解压旧条目 → tempDir
    - 遍历 archive.Entries，调用 entry.WriteToFile()
    - 每提取一个条目上报一次进度
    - PercentComplete = 已提取字节数 / (旧总字节数 + 新总字节数) * 100
    - FilePercentComplete = 当前文件的提取进度

  Step 2: 复制新文件到 tempDir（通常极快）
    - 只报告 ProcessedFiles 刷新
    - 此阶段 PercentComplete 值不变（沿用 Step 1 的累计值）

  Step 3: 重新压缩 tempDir → .new
    - 使用 ZipWriter.Write 逐文件写入
    - 每写入一个文件上报一次进度
    - PercentComplete = (旧总字节数 + 已压缩新字节数) / (旧总字节数 + 新总字节数) * 100

  Step 4: 原子覆盖 + 清理
    - 上报 100%

整个过程无跳跃，进度真实反映实际 I/O 量，且按字节加权（大文件占更多百分比）。
```

注意事项：本进度方案同时适用于 **DeleteEntriesAsync**（同样有此问题）。
删除操作改为：解压保留条目 → 重压缩，进度流与上述一致。

**编码估算：** `AddFilesToCurrentArchiveAsync` 当前在 `MainWindow.xaml.cs`，约 100 行。重写后提取到 `ZipEngine` 中作为 `AddEntriesAsync` 方法，UI 层调用简化。`DeleteEntriesAsync` 同样改为解压保留条目→重压缩，进度流一致。

高层次的流程（所有文件 I/O 阶段均可上报进度）：

```
原始: archive.zip
  ├── a.txt
  └── dir/b.txt

添加: c.txt

Step 1: 解压 archive.zip → %TEMP%\MantisZip\Rebuild\{guid}\  [✅ 逐条目进度，字节加权]
  ├── a.txt
  └── dir/
       └── b.txt

Step 2: 复制 c.txt → 同一临时目录                         [轻度进度刷新]

Step 3: 压缩临时目录 → archive.zip.new                        [✅ 逐文件进度，字节加权]

Step 4: archive.zip.new → archive.zip (覆盖原文件)           [上报 100%]

Step 5: 清理临时目录
```

**注意事项：**
- 加密 + 进度回调在重新压缩时需要重新应用
- 大文件时临时空间翻倍
- 需要确保原子覆盖（写 .new → 覆盖原文件）
- 取消时清理临时目录
- **进度事件线程安全**：`IProgress<ArchiveProgress>.Report` 在 `Task.Run` 内的提取和重压缩阶段均从后台线程调用，需确保 `ProgressWindow` 的 `SetProgress` 正确 dispatch 到 UI 线程（已有 `BackgroundDispatcherProgress` 负责此逻辑）

#### 3.4 `IArchiveEngine` 接口扩展 — 新增 `ExtractEntriesAsync`

过滤功能需要按条目选择性解压。在 Phase 3 重写 `ZipEngine` 时同步扩展接口，所有引擎统一实现：

**`IArchiveEngine` 新增方法：**

```csharp
/// <summary>
/// 从压缩包中提取指定条目到目标目录。
/// 用于过滤解压场景（仅解压匹配条件的条目）。
/// </summary>
Task ExtractEntriesAsync(
    string archivePath,
    IReadOnlyList<string> entryKeys,
    string destinationPath,
    string? password = null,
    IProgress<ArchiveProgress>? progress = null,
    CancellationToken cancellationToken = default,
    ArchiveOptions? options = null);
```

**各引擎实现策略：**

| 引擎 | 实现 |
|------|------|
| `ZipEngine` | `archive.Entries.Where(e => entryKeys.Contains(e.Key)).Each(e => e.WriteToFile(...))` |
| `SevenZipEngine` | 遍历 `archive.Entries` 匹配后调用 `entry.WriteToFile`（SharpCompress 统一 API） |
| `TarGzEngine` | `TarReader.MoveToNextEntry()` 循环筛选后 `WriteEntryToDirectory` |

**注意：** `entryKeys` 匹配使用 `ArchiveItem.FullPath`（压缩包内完整路径），不是文件名，避免重名歧义。

现在加接口可以确保：
- Phase 3 所有引擎一次性实现，不需回头补
- 过滤计划直接调用，不绕开 `IArchiveEngine` 抽象
- 保持一致性（进度、密码、取消均通过统一参数传递）

#### 3.5 移除进程级编码设置

当前全局初始化：
```csharp
// App.InitializeApp()
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```
和每个 ZipEngine 方法中：
```csharp
ZipStrings.CodePage = 936; // 安全冗余
```

替换后：
- 保留 `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`（可能被其他组件使用）
- 删除所有 `ZipStrings.CodePage = ...` 调用
- SharpCompress 的编码通过 `ReaderOptions` 按实例设置

---

### Phase 4：7z.exe → SharpSevenZip ✅

> 使用 [SharpSevenZip](https://github.com/sevenzipsharp/SevenZipSharp) 替代 7z.exe 进程调用 + SevenZipExtractor。
> 实际选用 SharpSevenZip（兼容 .NET 9 的 SevenZipSharp fork），同时替换了写路径（CompressAsync/AddToArchiveAsync）和读路径（ListEntriesAsync/ExtractAsync/TestArchiveAsync/ExtractEntriesAsync）。

- [x] **4.1** 前置工作：验证 SharpSevenZip 兼容 .NET 9
- [x] **4.2** `CompressAsync` / `AddToArchiveAsync` 迁移（Phase 4a）
- [x] **4.3** 读路径迁移：`ListEntriesAsync` / `ExtractAsync` / `TestArchiveAsync` / `ExtractEntriesAsync`（Phase 4b）
- [x] **4.4** 清理 SevenZipExtractor 残留引用：`ArchiveEntryExtractor.cs` + `App.Password.cs`（Phase 4.5）
- [x] `SevenZipExtractor` NuGet 包已移除

**文件：** `Core/Engines/SevenZipEngine.cs`

**预估工时：** ~3h（含 fork 调研验证）

**覆盖方法：** `CompressAsync` + `AddToArchiveAsync`

#### 前置工作：验证 SevenZipSharp fork

需要选择一个兼容 .NET 9 的 fork，并验证：
- 7z.dll 加载正常（随应用分发 vs 系统已安装）
- 压缩 API 可用
- 进度事件正常触发
- 取消支持

候选：
- `Squid-Box.SevenZipSharp` — 社区 fork，较新
- 其他 GitHub fork

#### 替换内容

当前 7z.exe 两个方法结构相同：
1. 检查 `7z.exe` 路径
2. 拼装 `ProcessStartInfo` 参数
3. `Process.Start` 启动进程
4. 异步读 stderr
5. 循环轮询 `cancellationToken`
6. 检查退出码

替换后的通用模式：

```csharp
await Task.Run(() =>
{
    SevenZipBase.SetLibraryPath(sevenZipDllPath);
    var compressor = new SevenZipCompressor
    {
        CompressionLevel = options.CompressionLevel,
        CompressionMethod = CompressionMethod.Lzma2,
        ArchiveFormat = OutArchiveFormat.SevenZip,
    };

    // 进度事件
    compressor.Compressing += (_, e) =>
    {
        progress?.Report(new ArchiveProgress
        {
            PercentComplete = e.PercentDone,
            Message = $"压缩中: {e.FileName ?? "..."}"
        });
    };

    compressor.FilesFound += (_, e) =>
    {
        progress?.Report(new ArchiveProgress
        {
            Message = $"找到 {e.Value} 个文件"
        });
    };

    // 加密
    if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
    {
        compressor.EncryptHeaders = true;
        compressor.Password = options.Password;
    }

    // 压缩
    compressor.CompressFiles(outputPath, sourcePaths);
}, cancellationToken);
```

#### 设置项清理

删除 `SettingsWindow` 中的 `7z.exe` 路径设置，替换为 `7z.dll` 路径（或自动检测）。

#### 备选方案

如果 SevenZipSharp fork 不可用，**保持 7z.exe 不变**，只走 Phase 1-3。

---

## 测试清单

| 测试项 | Phase 1 | Phase 2 | Phase 3 | Phase 4 |
|--------|---------|---------|---------|---------|
| 打开 tar.gz 文件 | ✅ | — | — | — |
| 打开 zip 文件（UTF-8） | — | — | ✅ | — |
| 打开 zip 文件（GBK 中文名） | — | — | ✅ | — |
| 打开 zip 文件（Zip64 大文件） | — | — | ✅ | — |
| 打开 7z/RAR 文件 | — | ✅ | — | — |
| `ExtractEntriesAsync` 选择性提取 | — | — | ✅ | ✅ |
| 各引擎 `ArchiveItem.Name` 编码一致（同一文件） | — | — | ✅ | — |
| 打开加密 zip | — | ✅ | ✅ | — |
| 压缩为 zip（各级压缩比） | — | — | ✅ | — |
| 压缩为 tar.gz | ✅ | — | — | — |
| 压缩为 7z（加密/分卷） | — | — | — | ✅ |
| 添加文件到已有 zip | — | — | ✅ | — |
| 添加文件到已有 7z | — | — | — | ✅ |
| 预览 zip 内文件 | — | ✅ | ✅ | — |
| 预览 tar.gz 内文件 | ✅ | — | — | — |
| 预览 7z 内文件 | — | ✅ | — | — |
| 拖拽 zip 内文件 | — | — | ✅ | — |
| 拖拽 tar.gz 内文件 | ✅ | — | — | — |
| 密码匹配（QuickVerify） | — | ✅ | ✅ | — |
| 测试压缩包完整性 | — | — | ✅ | — |
| 添加文件进度平滑（无跳跃/卡死） | — | — | ✅ | — |
| 删除条目进度平滑（无跳跃/卡死） | — | — | ✅ | — |
| 大文件占进度比例 > 小文件（字节加权生效） | — | — | ✅ | — |
| 无需安装 7-Zip | — | — | — | ✅ |

---

## 总工时

| Phase | 内容 | 工时 | 状态 |
|-------|------|------|:----:|
| 1 | TarGzEngine + DragDrop | 30min | ✅ |
| 2 | ArchiveEntryExtractor + QuickVerifyPassword | 25min | ✅ |
| 3 | ZipEngine 全量重写（含进度修复） | 3-4h | ✅ |
| 4 | 7z.exe → SharpSevenZip（读+写+清理） | ~4h | ✅ |
| 5 | SharpZipLib 清理 | ~3-4h | ⚠️ **部分完成** (见下方更新) |
| **合计** | | **~11-13h** | **Phase 1-4 完成** |

---

## 风险与注意事项

### 1. 编码回退策略

SharpCompress 不支持像 SharpZipLib 那样用 `IsUnicodeText` 标记判断编码。建议策略：先以 UTF-8 打开并遍历条目，检查是否有编码异常，有则回退 GBK 重新打开。这与当前 `ZipEngine` 的检测模式一致。

**过滤功能依赖：** 文件名匹配（过滤条件如 `文件名包含"报告"`）依赖 `ArchiveItem.Name` 在所有引擎中输出一致的编码。
- 各引擎在 `ListEntriesAsync` 中填充 `ArchiveItem.Name` 时需用统一规则
- `ZipEngine` 使用 `entry.Key`（SharpCompress 直接提供文件名）
- `SevenZipEngine` / `TarGzEngine` 同理
- 建议在 Phase 3 末尾统一验证：同一 ZIP 文件用新旧引擎读出的 `Name` 一致

### 2. 大文件临时空间

Phase 3 的"添加文件"重构需要 ±100% 的临时空间。对于超大压缩包（10GB+），需要考虑流式写入而非常规临时目录。初始实现不做优化，后续可按需处理。

### 3. 进度报告粒度 — 修复 `CommitUpdate` 黑盒问题

**关键修复**：`SharpZipLib` 的 `CommitUpdate()` 是 I/O 黑盒，添加/删除文件时旧条目的复制阶段无法上报进度，导致进度条跳跃。改用 SharpCompress 的"解压全量 → 合并 → 重压缩"后，每个阶段都可上报真实进度。

SharpCompress 和 SevenZipSharp 都没有内置细粒度进度事件，所有进度仍需手动在 I/O 循环中报告。本计划采用 **字节加权逐文件报告**方案：
- 提取旧条目时：按已提取字节数 / 总字节数计算 `PercentComplete`
- 重压缩时：按已写入字节数 / 总字节数计算 `PercentComplete`
- `FilePercentComplete` 在单个大文件处理时持续更新

### 4. 进度事件线程安全

`IProgress<ArchiveProgress>.Report` 在 `Task.Run` 内的提取和重压缩阶段均从后台线程调用。`ProgressWindow` 已有 `BackgroundDispatcherProgress` 负责 dispatch 到 UI 线程，但需注意：
- 不要在一个 `Report` 调用内持有长时间锁
- `ProgressWindow.SetProgress` 不应抛出异常
- 取消时确保不再调用 `Report`

### 5. 并行执行

Phase 3 涉及大量文件 I/O（解压全部 → 重压缩），是 CPU + I/O 密集操作。`ZipEngine` 中的 `CompressAsync` 和 `AddFilesAsync` 应使用 `Task.Run` 避免阻塞 UI，与当前模式一致。

### 6. 7z.dll 分发法律问题

- 7-Zip 使用 LGPL 许可证
- 允许与专有软件一起分发，前提是动态链接
- 需要在应用中包含 LGPL 声明
- 不能静态链接或修改 DLL 本身
- 7z.dll 约 1.5MB，可作为 `Content` 资源内嵌到输出目录

### 7. SevenZipSharp 备选方案

如果 SevenZipSharp fork 不可用或有问题，**跳過 Phase 4**，只走 Phase 1-3。影响：用户仍需安装 7-Zip，但 ZIP/TAR/GZ 的体验已统一提升。

---

### Phase 5：SharpZipLib 清理 ⚠️ 部分完成（经由 `zipengine-sharpcompress-migration` 计划）

> Phase 1-4 已将 SharpZipLib 从 TarGzEngine / ArchiveEntryExtractor 中移除。
> **后续进展**：`zipengine-sharpcompress-migration.md` 计划（v0.3.4）完成了 ZipEngine 三核心方法的 SharpCompress 迁移：
> - `CompressAsync` 未加密分支 → `ZipWriter` ✅；加密分支保留 `ZipOutputStream` ❌
> - `AddToArchiveAsync` 未加密分支 → `IArchive`+`ZipWriter` ✅；加密分支保留 `ZipOutputStream` ❌
> - `DeleteEntriesAsync` → 完全 `IArchive`+`ZipWriter` ✅（该方法无加密）
> - `OpenZipFile` 静态方法已删除 ✅
> - 测试通过 183/183 ✅
>
> **根本原因**：SharpCompress ZipWriter (v0.48.1) 没有任何加密 API（`ZipWriterOptions`/`ZipWriterEntryOptions` 无 password/encryption 属性），详见 `zipengine-sharpcompress-migration.md` post-migration analysis。

#### 5.1 残留现状（已更新）

| 位置 | 文件 | 残留内容 | 能否移除 |
|------|------|----------|---------|
| Core | `ZipEngine.cs` | `ZipOutputStream` 加密回退（CompressAsync/AddToArchiveAsync 加密分支）+ `ReadFileWithRetryZipOutputStream` | ❌ **阻塞** — SharpCompress ZipWriter 无加密 API |
| Core | `MantisZip.Core.csproj` | `PackageReference SharpZipLib 1.4.2` | ❌ 同上 |
| UI | `MainWindow.xaml.cs` | `ReadArchiveComment()` | ✅ 已改用 `ZipCommentHelper` |
| UI | `ArchiveCommentDialog.xaml.cs` | 原地改注释（`ZipCommentHelper` 直接操作 EOCD 字节） | ✅ 已改用 `ZipCommentHelper` |
| UI | `App.xaml.cs` | 无效 `using` | 🗑️ 已删除 |
| UI | `App.Cli.cs` | — | 🗑️ 文件已不存在（合并到 AppPartials/） |
| test | `tests/` 4 个文件 | `ZipOutputStream` 创建加密测试 ZIP 固件 | ❌ 保留（测试需要） |
| test | `test_encoding/test.csproj` | 未使用引用 | 🗑️ 可删 |

#### 5.2 已完成迁移（经由 `zipengine-sharpcompress-migration`）

**ZipEngine 三核心方法 — 自 `zipengine-sharpcompress-migration.md` 计划起已完成：**

| 方法 | SharpZipLib (旧) | SharpCompress (新) | 加密回退 |
|------|------------------|-------------------|---------|
| `CompressAsync` | `ZipOutputStream`+`ZipEntry`+`PutNextEntry` | `ZipWriter`+`writer.Write()` | ✅ 加密用 `ZipOutputStream` |
| `AddToArchiveAsync` | `ZipFile`(读)+`ZipOutputStream`(写) | `IArchive`(读)+`ZipWriter`(写) | ✅ 加密用 `ZipOutputStream` |
| `DeleteEntriesAsync` | `ZipFile`(读)+`ZipOutputStream`(写) | `IArchive`(读)+`ZipWriter`(写) | 该方法无加密 |

**其他已完成清理：**
- `MainWindow.xaml.cs` `ReadArchiveComment()` → 已改用 `ZipCommentHelper.ReadComment`
- `ArchiveCommentDialog.xaml.cs` → 已改用 `ZipCommentHelper.WriteCommentAsync`
- `App.xaml.cs` / `App.Cli.cs` → 无效 import 已删除

#### 5.3 必须保留 SharpZipLib 的原因（已更新）

**主要阻塞：SharpCompress ZipWriter 无加密 API**

SharpCompress v0.48.1 的 `ZipWriter` 在写出路径上完全不支持加密：
- `ZipWriterOptions` — 只有 `DeflateCompressionLevel`、`ArchiveComment`，**没有** password 或 encryption 属性
- `ZipWriterEntryOptions` — 只有时间戳等元数据，**没有** password 或 encryption 属性
- 对比 SharpZipLib `ZipOutputStream.Password` + `AESKeySize` 可直接设置 AES-256 加密

这意味着所有需要**写入加密 ZIP** 的代码路径（CompressAsync 加密分支、AddToArchiveAsync 加密分支）必须保留 SharpZipLib `ZipOutputStream`。

约 **20 行生产代码** + **`ReadFileWithRetryZipOutputStream` helper（~90 行）** 依赖 SharpZipLib。

**次要：测试固件需要创建加密 ZIP 文件**
- `ArchiveFixtures.CreateEncryptedZipArchive()` 使用 `ZipOutputStream`
- 若将来 SharpCompress 支持加密，可以改为 `SharpCompress.Writers.Zip.ZipWriter` + 密码

#### 5.4 依赖变更（已更新）

| 包 | 操作 |
|----|------|
| `SharpZipLib 1.4.2` (Core) | ⏸️ 保留（加密回退依赖） |
| `SharpZipLib 1.4.2` (UI) | ❌ 无需添加（已用 `ZipCommentHelper` 替换） |
| `test_encoding SharpZipLib` | ❌ 可移除（代码未使用） |

#### 5.5 待上游解决的依赖移除条件

SharpZipLib 可移除的前置条件（任一满足即可）：
1. SharpCompress ZipWriter 增加加密 API（password + AES-256 支持）
2. **✅ .NET 11 `System.IO.Compression.ZipArchive` 原生 AES-256 支持** — **最佳候选项**
3. 自建 AES-256 ZIP 写入器（不推荐，高维护成本）

##### 重点跟踪：.NET 11 ZipArchive 密码支持

| 项目 | 内容 |
|------|------|
| **PR** | [dotnet/runtime#122093](https://github.com/dotnet/runtime/pull/122093) |
| **状态** | **活跃** — API 已批准，42 文件，94 commits，安全审查中 |
| **目标版本** | **.NET 11.0.0**（2026 年 11 月） |
| **支持范围** | ZipCrypto + WinZip AES-128/192/256 — 读写 + update mode |
| **安全审查** | bartonjs（.NET 加密团队）已审查 EtM 模式，正在修复 |

.NET 11 发布后，SharpZipLib 加密回退分支（~20 行生产代码）可直接替换为 `ZipArchive` 原生 API，SharpCompress `ZipWriter` 也可评估是否替换。届时 `SharpZipLib 1.4.2` 可彻底从 `.csproj` 移除。

**建议**: 等 .NET 11 RC（2026.08-09）开始集成测试，正式版发布后立即迁移。

---

## Definition of Done ✅

- [x] Phase 1：TarGzEngine 完全使用 SharpCompress，SharpZipLib Tar/GZip 引用清除
- [x] Phase 2：ArchiveEntryExtractor + QuickVerifyPassword 迁移完成，预览/密码正常
- [x] Phase 3：ZipEngine 全量重写通过测试清单（所有测试项）
- [x] Phase 4：7z 压缩使用 SharpSevenZip，7z.exe 外部依赖移除
- [x] `dotnet build` 通过，`dotnet test` 全部通过
- [x] 所有 `ZipStrings.CodePage` 全局副作用移除

### Final Checklist

- [x] Tar.gz 打开/提取/压缩/预览正常
- [x] ZIP 打开/提取/压缩/添加/删除/预览正常（含 GBK/UTF-8/Zip64/加密）
- [x] 7z/RAR 解压/预览正常
- [x] 拖拽导出 zip/tar.gz 内文件正常
- [x] 密码管理（QuickVerifyPassword）正常工作
- [x] 选择性提取（ExtractEntriesAsync）正常工作
- [x] 向后兼容：旧压缩包打开无异常
- [x] 添加文件到已有压缩包：进度 0%→100% 平滑、无跳跃
- [x] 删除文件到已有压缩包：进度 0%→100% 平滑、无跳跃
- [x] 大文件在进度中占比 > 小文件（字节加权确认）
- [x] Phase 1-4 已完成（TarGzEngine / ArchiveEntryExtractor / 7z.exe 迁移）
- [x] SharpZipLib 核心残留已通过 `zipengine-sharpcompress-migration` 计划大幅减少（Compress/Add/Delete 三方法已迁移到 SharpCompress）
- [ ] SharpZipLib 加密回退保留（~20 行生产代码 + ReadFileWithRetryZipOutputStream helper） — 见 Phase 5 ⚠️
- ✅ 所有测试通过（183/183，包括 ZipEngine 迁移后的新测试用例）

---

## 相关文件

- [AGENTS.md](../../AGENTS.md) — 当前引擎架构说明
- [ArchiveEngine.cs](../../src/MantisZip.Core/Abstractions/ArchiveEngine.cs) — 接口定义 + 工厂（Phase 3 新增 `ExtractEntriesAsync`）
- [ZipEngine.cs](../../src/MantisZip.Core/Engines/ZipEngine.cs) — Phase 3 替换
- [TarGzEngine.cs](../../src/MantisZip.Core/Engines/TarGzEngine.cs) — Phase 1 替换
- [SevenZipEngine.cs](../../src/MantisZip.Core/Engines/SevenZipEngine.cs) — Phase 4 替换
- [ArchiveEntryExtractor.cs](../../src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs) — Phase 2 替换
- [App.xaml.cs](../../src/MantisZip.UI/App.xaml.cs) — Phase 2 QuickVerifyPassword 改造 + Phase 3 清理
- [MainWindow.DragDrop.cs](../../src/MantisZip.UI/MainWindow.DragDrop.cs) — Phase 1 GZip 替换
