# 引擎统一计划：SharpZipLib → SharpCompress + 7z.exe → SevenZipSharp

> 替换 SharpZipLib + 外置 7z.exe 进程调用，统一所有格式的压缩/解压实现
> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜] (0/4)

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
| `SharpZipLib 1.4.2` | ❌ 移除 |
| `SharpCompress` | ➕ 新增（替代 SharpZipLib） |
| `SevenZipSharp`（或兼容 fork） | ➕ 新增（替代 7z.exe，Phase 4） |
| `7z.dll` | ➕ 随应用分发（LGPL，Phase 4） |

---

## 分阶段实现

### Phase 1：TarGzEngine + DragDrop GZip [⬜⬜⬜] (0/3)

- [ ] TarGzEngine 全部方法从 SharpZipLib 迁移到 SharpCompress API
- [ ] MainWindow.DragDrop.cs GZip 提取使用新的 SharpCompress API
- [ ] 验证：打开 .tar.gz 正常列出/提取/预览；拖拽 tar.gz 内文件正常

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

### Phase 2：ArchiveEntryExtractor + QuickVerifyPassword [⬜⬜⬜] (0/3)

- [ ] ArchiveEntryExtractor 从 SharpZipLib 迁移到 SharpCompress
- [ ] App.xaml.cs / MainWindow.xaml.cs 中相关代码迁移
- [ ] Zip/Tar/Gz 单项提取、预览、拖拽全部正常工作

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

### Phase 3：ZipEngine [⬜⬜⬜⬜⬜] (0/5)

**文件：** `ZipEngine.cs`（~350 行完整重写）

- [ ] **3.1** `ListEntriesAsync` / `ExtractAsync` / `TestArchiveAsync` 迁移
- [ ] **3.2** `CompressAsync` 迁移
- [ ] **3.3** `AddToArchiveAsync` / `DeleteEntriesAsync` 迁移
- [ ] **3.4** `IArchiveEngine` 新增 `ExtractEntriesAsync` 接口
- [ ] **3.5** 编码处理（UTF-8/GBK 回退）正确性验证

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

#### 3.3 更新（AddFilesToCurrentArchiveAsync）— 最大改动点

**SharpZipLib 做法（伪原地）：**

```csharp
using var zipFile = new ZipFile(archivePath);
zipFile.BeginUpdate();
zipFile.Add(newFile, entryName);
zipFile.CommitUpdate();
```

**SharpCompress 做法：解压全量 → 合并 → 重压缩**

```csharp
// 1. 解压所有现有文件到临时目录
// 2. 复制新文件到临时目录
// 3. 用 ZipWriter 重新压缩临时目录为 outputPath
// 4. 用 outputPath 覆盖原文件
```

流程细节：

```
原始: archive.zip
  ├── a.txt
  └── dir/b.txt

添加: c.txt

Step 1: 解压 archive.zip → %TEMP%\MantisZip\Rebuild\{guid}\
  ├── a.txt
  └── dir/
       └── b.txt

Step 2: 复制 c.txt → 同一临时目录

Step 3: 压缩临时目录 → archive.zip.new

Step 4: archive.zip.new → archive.zip (覆盖原文件)

Step 5: 清理临时目录
```

**注意事项：**
- 加密 + 进度回调在重新压缩时需要重新应用
- 大文件时临时空间翻倍
- 需要确保原子覆盖（写 .new → 覆盖原文件）
- 取消时清理临时目录

**编码估算：** `AddFilesToCurrentArchiveAsync` 当前在 `MainWindow.xaml.cs`，约 100 行。重写后提取到 `ZipEngine` 中作为 `AddEntriesAsync` 方法，UI 层调用简化。

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

### Phase 4：7z.exe → SevenZipSharp [⬜⬜⬜] (0/3)

> **可选阶段** — 如果外置 7z.exe 已经满足需求，可跳过此阶段。

- [ ] **4.1** 前置工作：验证 SevenZipSharp fork 兼容 .NET 9
- [ ] **4.2** `CompressAsync` / `AddToArchiveAsync` 迁移
- [ ] **4.3** 7z.dll 分发与 LGPL 合规处理，SettingsWindow 7z 路径清理

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
| 无需安装 7-Zip | — | — | — | ✅ |

---

## 总工时

| Phase | 内容 | 工时 |
|-------|------|------|
| 1 | TarGzEngine + DragDrop | 30min |
| 2 | ArchiveEntryExtractor + QuickVerifyPassword | 25min |
| 3 | ZipEngine 全量重写 | 2-3h |
| 4 | 7z.exe → SevenZipSharp | ~3h |
| **合计** | | **~6-7h** |

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

### 3. 进度报告粒度

SharpCompress 和 SevenZipSharp 都没有细粒度进度事件（如当前 `ZipEngine` 的 buffer copy 循环 + 100ms 报告）。替换后维持当前的手动报告模式。

### 4. 并行执行

Phase 3 涉及大量文件 I/O（解压全部 → 重压缩），是 CPU + I/O 密集操作。`ZipEngine` 中的 `CompressAsync` 和 `AddFilesAsync` 应使用 `Task.Run` 避免阻塞 UI，与当前模式一致。

### 5. 7z.dll 分发法律问题

- 7-Zip 使用 LGPL 许可证
- 允许与专有软件一起分发，前提是动态链接
- 需要在应用中包含 LGPL 声明
- 不能静态链接或修改 DLL 本身
- 7z.dll 约 1.5MB，可作为 `Content` 资源内嵌到输出目录

### 6. SevenZipSharp 备选方案

如果 SevenZipSharp fork 不可用或有问题，**跳過 Phase 4**，只走 Phase 1-3。影响：用户仍需安装 7-Zip，但 ZIP/TAR/GZ 的体验已统一提升。

---

## Definition of Done

- [ ] Phase 1：TarGzEngine 完全使用 SharpCompress，SharpZipLib Tar/GZip 引用清除
- [ ] Phase 2：ArchiveEntryExtractor + QuickVerifyPassword 迁移完成，预览/密码正常
- [ ] Phase 3：ZipEngine 全量重写通过测试清单（所有测试项）
- [ ] Phase 4（可选）：7z 压缩使用 SevenZipSharp，7z.exe 外部依赖移除
- [ ] `dotnet build` 通过，`dotnet test` 全部通过
- [ ] 所有 `ZipStrings.CodePage` 全局副作用移除

### Final Checklist

- [ ] Tar.gz 打开/提取/压缩/预览正常
- [ ] ZIP 打开/提取/压缩/添加/删除/预览正常（含 GBK/UTF-8/Zip64/加密）
- [ ] 7z/RAR 解压/预览正常
- [ ] 拖拽导出 zip/tar.gz 内文件正常
- [ ] 密码管理（QuickVerifyPassword）正常工作
- [ ] 选择性提取（ExtractEntriesAsync）正常工作
- [ ] 向后兼容：旧压缩包打开无异常
- [ ] SharpZipLib 依赖移除，SharpCompress 依赖已添加
- [ ] 所有测试通过

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
