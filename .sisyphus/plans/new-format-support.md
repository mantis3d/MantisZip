# 新增压缩格式支持计划

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜] (0/7)
> **前置依赖**: 无（SharpCompress 已引入，无需新增主依赖直至 Phase 4）

---

## 动机

### 现状

| 格式 | 解压浏览 | 压缩输出 | 引擎 |
|------|:--------:|:--------:|------|
| ZIP | ✅ | ✅ | ZipEngine (SharpCompress) |
| 7z | ✅ | ✅ | SevenZipEngine (SharpSevenZip) |
| RAR | ✅ | ❌ | SevenZipEngine (只读) |
| ISO | ✅ | ❌ | SevenZipEngine (只读) |
| TAR | ✅ | ❌ | TarGzEngine (通过 GetFormatByExtension 映射为 Tar) |
| GZip (.gz) | ✅ | ❌ | TarGzEngine |
| tar.gz | ✅ | ✅ | TarGzEngine |

**压缩输出仅 3 种格式**，而 SharpCompress 底层支持更多格式，只需包装新 Engine 类即可解锁。本项目已有 `IArchiveEngine` 策略模式，新增格式代价低。

### 目标格式

| 格式 | 解压 | 压缩 | 依赖 | 优先级 |
|------|:----:|:----:|------|:------:|
| **TAR 裸格式** | ✅ 已有 | ➕ 新增 | SharpCompress 已有 | P0 |
| **BZip2 (.bz2)** | ➕ 新增 | ➕ 新增 | SharpCompress 已有 | P1 |
| **Tar.bz2** | ➕ 新增 | ➕ 新增 | SharpCompress 已有 | P1 |
| **XZ (.xz)** | ➕ 新增 | ➕ 新增 | SharpCompress 已有 | P1 |
| **Tar.xz** | ➕ 新增 | ➕ 新增 | SharpCompress 已有 | P1 |
| **CAB (.cab)** | ➕ 新增 | ❌ 只读 | SharpCompress 已有 | P2 |
| **Zstandard (.zst)** | ➕ 新增 | ➕ 新增 | 需引入 `ZstdNet` 或 `K4os.Compression.LZ4` | P3 |

---

## 改动范围总览

涉及 **~18 个文件**（按 Phase 渐进，非一次性）：

### 核心库（Core）

| 文件 | 改动 |
|------|------|
| `Core/Abstractions/ArchiveEngine.cs` | `ArchiveFormat` 枚举新增成员；`ArchiveEngineFactory` 注册新引擎、扩展 `GetFormatByExtension` 和 `GetEngineByExtension` |
| `Core/Engines/BZip2Engine.cs` | **新建** — BZip2 压缩/解压引擎 |
| `Core/Engines/XzEngine.cs` | **新建** — XZ 压缩/解压引擎 |
| `Core/Engines/CabEngine.cs` | **新建** — CAB 只读引擎 |
| `Core/Engines/ZstdEngine.cs` | **新建** — Zstandard 引擎（Phase 4） |
| `Core/Abstractions/ITableDataProvider.cs` | 可能可选扩展（CAB 元数据展示） |

### UI（WPF）

| 文件 | 改动 |
|------|------|
| `UI/Dialogs/CompressSettingsWindow.xaml` | `FormatComboBox` 新增格式项 |
| `UI/Dialogs/CompressSettingsWindow.xaml.cs` | `LoadDefaultsFromSettings` / format 映射更新 |
| `UI/MainWindow/MainWindow.xaml.cs` | `IsArchiveFile` 扩展名列表、`GetCompressedDisplayMode` |
| `UI/AppSettings.cs` | `DefaultFormat` 注释更新、关联设置新增 |
| `UI/App.xaml.cs` | 初始化中的编码注册（BZip2/XZ 无需额外编码注册） |

### Shell 集成

| 文件 | 改动 |
|------|------|
| `UI/Shell/ShellIntegration.cs` | `SupportedExtensions` 列表扩展 |
| `UI/Shell/ShellIntegration.Assoc.cs` | 文件关联安装/卸载逻辑 |

### 本地化

| 文件 | 改动 |
|------|------|
| `UI/Localization/strings.zh.json` | 新格式显示名称 |
| `UI/Localization/strings.en.json` | 同上 |

---

## 架构决策

### Engine 类设计模式

每个新格式一个 Engine 类，遵循现有 `IArchiveEngine` 接口：

```csharp
// 模板 — 以 BZip2 为例
public class BZip2Engine : IArchiveEngine
{
    public bool CanHandle(ArchiveFormat format) =>
        format is ArchiveFormat.BZip2;

    // ExtractAsync — SharpCompress.BZip2Archive 或 BZip2Stream 逐文件提取
    // CompressAsync — BZip2Writer / BZip2Stream 压缩
    // ListEntriesAsync — 遍历 BZip2Archive.Entries
    // TestArchiveAsync — 尝试打开 + 遍历全部条目
    // ExtractEntriesAsync — 按 key 过滤提取
    // DeleteEntriesAsync — NotSupported (BZip2 不支持原地删除)
    // AddToArchiveAsync — NotSupported (BZip2 不支持原地添加)
}
```

### BZip2 单文件 vs 归档

- `.bz2` 单文件：`BZip2Stream` 直接包装，解压为单文件，压缩为单文件
- `.tar.bz2` 组合格式：`TarWriter` + `BZip2Stream` 嵌套，类似于当前 TarGzEngine 的 Tar + GZip 组合

### XZ 同样处理

- `.xz` 单文件：`XZStream` 包装
- `.tar.xz`：`TarWriter` + `XZStream` 嵌套

建议用两个 Engine 还是合并？

**决策：用两个 Engine（BZip2Engine / XzEngine）各自处理纯格式和 tar 组合。** 原因：
1. 与 TarGzEngine 一致（GZip 组合内置）
2. IArchiveEngine 粒度是格式而非容器组合
3. GetFormatByExtension 将 `.tar.bz2` → `BZip2`，`.tar.xz` → `XZ`

---

## 分阶段实现

### Phase 0：基础设施（30min）

**目标：** 扩展枚举 + 工厂 + 格式映射，不改引擎逻辑。

文件：
- `Core/Abstractions/ArchiveEngine.cs`

改动：
1. `ArchiveFormat` 枚举新增 `BZip2`, `XZ`, `Cab`, `Zstd`
2. `ArchiveEngineFactory.SupportedExtensions` 追加 `.bz2`, `.tar.bz2`, `.xz`, `.tar.xz`, `.cab`, `.zst`, `.tar.zst`
3. `GetFormatByExtension` 新增 case：
   - `.bz2` → `BZip2`
   - `.tar.bz2` / `.tbz` / `.tbz2` → `BZip2`
   - `.xz` → `XZ`
   - `.tar.xz` → `XZ`
   - `.cab` → `Cab`
   - `.zst` / `.tar.zst` → `Zstd`
4. `GetEngineByExtension` 同样扩展

**交付物：** 枚举完整、工厂可映射到新格式（即使引擎返回 null 也不会抛）。

```
- [ ] ArchiveFormat 枚举扩展
- [ ] SupportedExtensions 扩展
- [ ] GetFormatByExtension / GetEngineByExtension 新 case
```

---

### Phase 1：TAR 裸格式 + GZip 单文件输出（1h）

**目标：** 压缩时可选输出纯 `.tar`（无压缩）和 `.gz`（单文件 gzip）。

文件：
- `UI/Dialogs/CompressSettingsWindow.xaml` — FormatComboBox 新增两项
- `UI/Dialogs/CompressSettingsWindow.xaml.cs` — format → ext 映射
- `UI/AppSettings.cs` — DefaultFormat 注释

改动：
1. XAML 新增 ComboBoxItem：
   ```xml
   <ComboBoxItem Content="TAR (.tar)" Tag="tar"/>
   <ComboBoxItem Content="GZIP (.gz)" Tag="gz"/>
   ```
2. `FormatComboBox_SelectionChanged` 中处理 `tar`/`gz` 的逻辑（其实已有通用逻辑，只需确认 ext 映射）
3. `BrowseOutputButton_Click` 中的 ext 映射已通过 `format == "tar.gz" ? ".tar.gz" : "." + format` 处理，`.tar` 和 `.gz` 自然生效
4. TarGzEngine 当前 `CompressAsync` 已能处理纯 Tar（检查 gzip 层逻辑）
   - 若 `outputPath` 以 `.tar` 结尾且 TarGzEngine.CompressAsync 当前写 gzip，则需加条件跳过 gzip 层

**关键验证：** TarGzEngine.CompressAsync 当前是否硬编码 gzip 层？

> 需要读取 `TarGzEngine.CompressAsync` 确认。如果硬编码 gzip，则需修改为根据扩展名判断是否加 gzip。

```
- [ ] 验证 TarGzEngine.CompressAsync 是否硬编码 gzip
- [ ] 如有必要，修改为条件性 gzip
- [ ] FormatComboBox 新增 TAR / GZIP 项
- [ ] 验证压缩输出正确
```

---

### Phase 2：BZip2 支持（3-4h）

**目标：** 完整 BZip2 读/写，含 `.bz2` 和 `.tar.bz2`。

文件：
- `Core/Engines/BZip2Engine.cs` （新建，~250 行）
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎
- `UI/Dialogs/CompressSettingsWindow.xaml` — 新增 ComboBoxItem

BZip2Engine 实现要点：

| 方法 | SharpCompress API | 备注 |
|------|-------------------|------|
| `ListEntriesAsync` | `BZip2Archive` via `ArchiveFactory.OpenArchive` | Tar.bz2 自动识别为 Tar 内层 |
| `ExtractAsync` | `BZip2Archive` 遍历 + `entry.WriteToFile` | 同 ZipEngine 模式 |
| `CompressAsync` | `BZip2Writer`（SharpCompress.Writer） | 多文件需先 Tar → BZip2 嵌套 |
| `TestArchiveAsync` | 遍历全部入口 | |
| `CanHandle` | `BZip2` 格式 | |
| `CanAdd / CanDelete` | `false`（不支持原地修改） | |

**BZip2 压缩技术路径：**

```
多文件 → TarBuilder (MemoryStream) → BZip2Stream → FileStream
```

SharpCompress 的 `WriterFactory.Open(stream, ArchiveType.Tar, CompressionType.BZip2)` 可直接写入组合 tar.bz2。

**单文件 .bz2 压缩：**
```
单文件 → BZip2Stream(FileStream, CompressionMode.Compress) → CopyFrom(sourceStream)
```

```
- [ ] 新建 BZip2Engine.cs
- [ ] ListEntriesAsync / ExtractAsync 实现
- [ ] CompressAsync 实现（tar.bz2 组合 + 单文件 bz2）
- [ ] TestArchiveAsync 实现
- [ ] ArchiveEngineFactory 注册 BZip2Engine
- [ ] UI FormatComboBox 新增 BZip2 项
- [ ] 验证：压缩 .bz2 / .tar.bz2 → 解压 → 内容一致
- [ ] 验证：打开现有 .bz2 / .tar.bz2 → 正常浏览/提取
```

---

### Phase 3：XZ 支持（2-3h）

**目标：** 完整 XZ 读/写，含 `.xz` 和 `.tar.xz`。

文件：
- `Core/Engines/XzEngine.cs`（新建，~250 行）
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎
- `UI/Dialogs/CompressSettingsWindow.xaml` — 新增 ComboBoxItem

XZ 实现与 BZip2 几乎完全对称，不同点：

| 方面 | BZip2 | XZ |
|------|-------|----|
| SharpCompress Writer Type | `CompressionType.BZip2` | `CompressionType.XZ` |
| Stream class | `BZip2Stream` | `XZStream` |
| 压缩级别映射 | 1-9 → BZip2 级别 | 1-9 → XZ 级别（preset） |

**注意：** SharpCompress 的 XZ 写支持需要确认版本。如果支持不足，可退而使用 `XZStream` 自行包装。

```
- [ ] 新建 XzEngine.cs
- [ ] ListEntriesAsync / ExtractAsync 实现
- [ ] CompressAsync 实现（tar.xz + 单文件 xz）
- [ ] TestArchiveAsync 实现
- [ ] ArchiveEngineFactory 注册 XzEngine
- [ ] UI FormatComboBox 新增 XZ 项
- [ ] 验证：压缩 .xz / .tar.xz → 解压 → 内容一致
```

---

### Phase 4：CAB 只读支持（1.5h）

**目标：** 支持打开/浏览/提取 Windows CAB 文件（不输出）。

文件：
- `Core/Engines/CabEngine.cs`（新建，~150 行）
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎

CABEngine 实现要点：

| 方法 | 说明 |
|------|------|
| `CanHandle` | `Cab` 格式 |
| `ListEntriesAsync` | `CabArchive` via `ArchiveFactory.OpenArchive` |
| `ExtractAsync` | 遍历 + WriteToFile |
| `TestArchiveAsync` | 遍历全部条目 |
| `CompressAsync` | `NotSupportedException` |
| `CanAdd / CanDelete` | `false` |

**CAB 特殊点：**
- CAB 条目路径可能以反斜杠分隔，需统一为正斜杠（同现有 `fileName.Replace('\\', '/')` 模式）
- 无压缩级别概念

**UI 无需改动**（CAB 只读，不加入 FormatComboBox）。

```
- [ ] 新建 CabEngine.cs
- [ ] ListEntriesAsync / ExtractAsync 实现
- [ ] TestArchiveAsync 实现
- [ ] ArchiveEngineFactory 注册 CabEngine
- [ ] 验证：打开 .cab → 正常浏览/提取
```

---

### Phase 5：UI 统一化（1h）

**目标：** FormatComboBox 的选项列表不再硬编码，改为从引擎注册表动态生成。

文件：
- `Core/Abstractions/ArchiveEngine.cs` — 新增 `GetCompressibleFormats()` 工厂方法
- `UI/Dialogs/CompressSettingsWindow.xaml` — 移除硬编码 ComboBoxItem
- `UI/Dialogs/CompressSettingsWindow.xaml.cs` — 动态生成下拉选项

**动机：** 每新增一个格式就要改 XAML，容易遗漏。改为 `ArchiveEngineFactory` 暴露 `GetCompressibleFormats()` 返回 `IArchiveEngine[]`，UI 遍历引擎列表，对 `CanCompress`（或 `CanAdd`）为 true 的引擎生成下拉项。

```csharp
// ArchiveEngineFactory 新增
public static IEnumerable<(ArchiveFormat Format, string DisplayName, string Extension)> GetCompressibleFormats()
{
    yield return (ArchiveFormat.Zip, "ZIP", ".zip");
    yield return (ArchiveFormat.SevenZip, "7z", ".7z");
    yield return (ArchiveFormat.Tar, "TAR", ".tar");
    yield return (ArchiveFormat.GZip, "GZIP", ".gz");
    // 新格式自动加入...
}
```

**UI 绑定：** `FormatComboBox.ItemsSource` 绑定到 `GetCompressibleFormats()`，`DisplayMemberPath = "DisplayName"`，`SelectedValuePath = "Extension"`。

```
- [ ] ArchiveEngineFactory 新增 GetCompressibleFormats()
- [ ] CompressSettingsWindow 动态生成下拉选项
- [ ] 移除 XAML 硬编码 ComboBoxItem
- [ ] 验证所有格式仍可选
```

---

### Phase 6：Zstandard 支持（3-5h）

**目标：** 引入 Zstd 依赖，实现 `.zst` 和 `.tar.zst` 读/写。

文件：
- `Core/Engines/ZstdEngine.cs`（新建，~300 行）
- `Core/MantisZip.Core.csproj` — 新增 NuGet 依赖
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎
- `UI/Dialogs/CompressSettingsWindow.xaml` — 新增 ComboBoxItem

**依赖选择：**

| 包 | 说明 |
|----|------|
| `ZstdNet` | 封装 `libzstd.dll`，需分发 native dll |
| `K4os.Compression.LZ4` | 纯 C#，但只支持 LZ4 而非 Zstd |
| `IronSnappy` | Snappy 而非 Zstd |

Zstandard .NET 生态目前主要有：
- `ZstdNet` — 较成熟，但依赖 native libzstd.dll
- `Standart.Hash.Qualcomm.Zstd` — 纯 C# 实现
- 直接 P/Invoke `libzstd.dll`

**推荐：** 先用 `ZstdSharp`（纯 C# port）或 `ZstdNet`，评估 native 依赖对分发的负担。

**SharpCompress 集成：**
SharpCompress 不支持 Zstd 内置，需自行实现：
- 读取：`ZstdStream` 包装 `FileStream` 解压
- 写入：`ZstdStream` 包装 `FileStream` 压缩
- Tar 组合：`TarWriter` 输出到 `ZstdStream`

```
- [ ] 调研并引入 Zstd .NET 绑定
- [ ] 新建 ZstdEngine.cs
- [ ] 单文件 .zst 读/写实现
- [ ] tar.zst 组合读/写实现
- [ ] ArchiveEngineFactory 注册
- [ ] UI 新增 Zstandard 选项
- [ ] 验证：压缩 .zst / .tar.zst → 解压 → 内容一致
```

---

## 验证清单

### 每个 Phase 通用验证

```
[ ] dotnet build 通过（Core + UI）
[ ] 打开对应格式压缩包 → 正常浏览文件列表
[ ] 提取到本地 → 文件完整、目录结构正确
[ ] 压缩（如支持）→ 生成有效文件
[ ] 7z/WinRAR 验证生成的文件可正常打开
[ ] 进度报告正常（ProgressWindow）
[ ] lsp_diagnostics 无新 warning/error
```

### 各格式特殊验证

| 格式 | 特殊验证项 |
|------|-----------|
| `.tar` | tar tvf 验证无 gzip 层 |
| `.gz` | gzip -d 验证可正常解压 |
| `.bz2` | 多文件 tar.bz2 解压后目录结构 |
| `.xz` | xz -d 验证；高压缩级别性能 |
| `.cab` | Windows expand 命令验证 |
| `.zst` | zstd -d 验证；压缩率对比 |

---

## 未纳入范围

- **ARJ (.arj)** — SharpCompress 不支持，无 .NET 活跃库
- **LZH (.lzh)** — SharpCompress 不支持，用户群极小
- **DMG (.dmg)** — macOS 专有格式，Windows 无意义
- **WIM (.wim)** — Windows 映像格式，与压缩工具定位不符
- **CPIO (.cpio)** — SharpCompress 不支持
- **RAR 压缩** — 已有独立计划 `.sisyphus/plans/rar-compression.md`
