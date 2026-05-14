# 引擎统一计划：SharpCompress + SevenZipSharp

> 替换 SharpZipLib + SevenZipExtractor + 7z.exe 调用
> 状态: 📋 计划（未开始）

---

## 目标

- 移除 SharpZipLib 依赖
- 移除 SevenZipExtractor 依赖（以及它对原生 7z.dll 的依赖）
- 移除 `SevenZipEngine.CompressAsync` 中 `Process.Start("7z.exe")` 的 Shell 调用
- 统一三个引擎为一个通用实现
- 获得真正的 async I/O

## 方案

```
SharpCompress → 所有格式的读取 + ZIP/TAR/GZ 写入
                   取代：SharpZipLib + SevenZipExtractor

SevenZipSharp → 仅在用户选 7z 格式时用于压缩（包装 7z.dll）
                   取代：Process.Start("7z.exe")
```

依赖变更：

| 当前 | 未来 |
|------|------|
| SharpZipLib 1.4.0 | ❌ 移除 |
| SevenZipExtractor 1.0.19 | ❌ 移除 |
| 7z.exe (外置进程调用) | ❌ 移除 |
| (无) | ➕ SharpCompress 最新版 |
| (无) | ➕ SevenZipSharp (或维护中的 fork) |
| (无) | ➕ 7z.dll (随应用分发, LGPL) |

---

## 改动范围

### 1. 新增文件

| 文件 | 说明 |
|------|------|
| `Core/Engines/SharpCompressEngine.cs` | 新的通用引擎，替代三个引擎 |
| `Core/Utils/SevenZipCompressor.cs` | 包装 SevenZipSharp 做 7z 压缩 |
| `Core/Utils/ArchiveEncodingHelper.cs` | GBK/UTF-8 编码辅助 |

### 2. 修改文件

| 文件 | 改动 |
|------|------|
| `Core/Abstractions/ArchiveEngine.cs` | `ArchiveEngineFactory` 注册新引擎；`ArchiveItem` 可能微调 |
| `Core/Utils/ArchiveEntryExtractor.cs` | 改为基于 SharpCompress 的单条目提取 |
| `.csproj` | 移除 SharpZipLib/SevenZipExtractor，添加 SharpCompress/SevenZipSharp |

### 3. 删除文件

| 文件 | 替代 |
|------|------|
| `Core/Engines/ZipEngine.cs` | SharpCompressEngine |
| `Core/Engines/SevenZipEngine.cs` | SharpCompressEngine + SevenZipCompressor |
| `Core/Engines/TarGzEngine.cs` | SharpCompressEngine |
| `Core/Utils/SplitOutputStream.cs` | SharpCompress 内置分卷支持 |

### 4. UI 层影响

| 位置 | 改动 |
|------|------|
| `MainWindow.xaml.cs` 中 `ExtractTarGzSingleEntry` | 改为 SharpCompress 实现 |
| `MainWindow.xaml.cs` 中拖拽提取路径 | 检查统一 |
| `App.xaml.cs` 中 `QuickVerifyPassword` | 异常类型可能需要调整 |

---

## 实现步骤

### Step 1：添加 SharpCompress + SevenZipSharp 依赖

```xml
<PackageReference Include="SharpCompress" Version="0.38.0" />
<PackageReference Include="SevenZipSharp" Version="1.0.0" />
```

同时将 `7z.dll` 纳入项目资源（`Resources/7z.dll`，生成操作为 `Content` + `Copy to Output`）。

### Step 2：实现 SharpCompressEngine

统一接口，用 `ArchiveFactory.Open` / `WriterFactory.OpenWriter` / `ReaderFactory.OpenReader` 处理所有格式。

核心 API 映射：

| 当前方法 | SharpCompress 实现 |
|---------|-------------------|
| `ListEntriesAsync` | `ArchiveFactory.Open` → `archive.Entries` → 映射为 `ArchiveItem` |
| `ExtractAsync` | `archive.WriteToDirectory` 或逐条目 `entry.WriteTo` |
| `CompressAsync` (zip/tar.gz) | `WriterFactory.OpenWriter` + `writer.WriteAll` / `writer.Write` |
| `TestArchiveAsync` | `ArchiveFactory.Open` + 遍历读取每个条目 |
| `CompressAsync` (7z) | 委托给 `SevenZipCompressor` |

### Step 3：实现 SevenZipCompressor

包装 SevenZipSharp，专门处理 7z 格式压缩：

```csharp
public static class SevenZipCompressor
{
    public static async Task CompressAsync(
        string[] sourcePaths,
        string outputPath,
        ArchiveOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken ct = default)
    {
        // 1. 设置 7z.dll 路径（从嵌入资源释放）
        SevenZipBase.SetLibraryPath(EmbeddedDllPath);
        
        // 2. 配置压缩器
        var compressor = new SevenZipCompressor
        {
            CompressionLevel = options.CompressionLevel,
            CompressionMethod = CompressionMethod.Lzma2,
        };
        
        // 3. 挂载进度事件
        compressor.Compressing += (_, e) => { /* 映射到 ArchiveProgress */ };
        
        // 4. 压缩
        await Task.Run(() => compressor.CompressFiles(outputPath, sourcePaths), ct);
    }
}
```

### Step 4：更新 ArchiveEngineFactory

```csharp
static ArchiveEngineFactory()
{
    _engines.Add(new SharpCompressEngine()); // 处理 zip/7z/rar/tar/gz
}
```

删除 ZipEngine、SevenZipEngine、TarGzEngine 的注册。

### Step 5：更新 ArchiveEntryExtractor

将 `ExtractZipEntry` / `ExtractSevenZipEntry` 合并为一个基于 SharpCompress 的方法：

```csharp
public static async Task ExtractEntryAsync(/* ... */)
{
    using var archive = ArchiveFactory.Open(archivePath, options);
    var entry = archive.Entries.FirstOrDefault(e => e.Key == entryName);
    entry?.WriteToFile(outputPath);
}
```

### Step 6：删除旧文件 + 清理

- 删除 `ZipEngine.cs`、`SevenZipEngine.cs`、`TarGzEngine.cs`
- 删除 `SplitOutputStream.cs`（SharpCompress 自带分卷支持）
- 删除 `System.Linq`（如果原代码因 `entries.Any(...)` 引入已不再需要）

### Step 7：验证

| 检查项 | 方法 |
|--------|------|
| 所有格式读取 | 打开测试 zip/7z/rar/tar/tar.gz |
| 中文编码 | 测试 GBK 编码的 ZIP 文件 |
| 压缩 | 创建 zip/tar.gz/7z |
| 加密 | 创建带密码的 zip，验证打开需要密码 |
| 预览 | 预览 zip/7z 内文件 |
| 拖拽 | 拖出文件到资源管理器 |
| 进度 | 验证两个进度条正常显示 |
| 编码注册 | 不再需要 `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` 和 `ZipStrings.CodePage = 936` |

---

## 注意事项

### 中文编码

SharpCompress 每个 `ReaderOptions` 可以独立设置编码：

```csharp
new ReaderOptions
{
    ArchiveEncoding = new ArchiveEncoding
    {
        Default = Encoding.GetEncoding("gbk")
    }
}
```

这比当前 `ZipStrings.CodePage = 936`（进程级全局副作用）更干净。

但需要注意：
- 并非所有 ZIP 文件都是 GBK 编码——现代工具创建的是 UTF-8
- 需要尝试 UTF-8 → GBK 回退策略
- 7z 文件使用 Unicode，不需要编码处理

### 7z.dll 分发的法律问题

- 7-Zip 使用 LGPL 许可证
- 允许与专有软件一起分发，前提是动态链接
- 需要在应用中包含 LGPL 声明
- 不能静态链接或修改 DLL 本身

### SevenZipSharp 兼容性

- 原版 SevenZipSharp 最后一次更新是 ~2016 年
- 社区有 `.NET Core` / `.NET 5+` 的 fork
- 如果 fork 也不维护，替代方案：用 `Process.Start` 调用自带的 `7z.exe`（方案 B），但那是备选

> **备选**：如果 SevenZipSharp 不可用，7z 压缩回退到自带 7z.exe 方案。其他格式走 SharpCompress。

---

## 相关文件

- [AGENTS.md](../../AGENTS.md) — 当前引擎模式说明
- [ArchiveEngine.cs](../../src/MantisZip.Core/Abstractions/ArchiveEngine.cs) — 接口定义 + 工厂
- [ZipEngine.cs](../../src/MantisZip.Core/Engines/ZipEngine.cs) — 待删除
- [SevenZipEngine.cs](../../src/MantisZip.Core/Engines/SevenZipEngine.cs) — 待删除
- [TarGzEngine.cs](../../src/MantisZip.Core/Engines/TarGzEngine.cs) — 待删除
