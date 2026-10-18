# 项目架构

## 技术决策

| 决策项 | 选择 | 日期 |
|--------|------|------|
| 开发语言 | C# (.NET 10) | 2026-04-23 |
| UI 框架 | Avalonia 12 (net10.0) | 2026-08 (迁移完成) |
| 架构模式 | MVVM (CommunityToolkit.Mvvm + Source Generators) | 2026-08 |
| 目标用户 | 普通用户 | 2026-04-23 |
| 压缩引擎 | SharpCompress (ZIP/TAR/GZ) + SharpSevenZip (7z/RAR) | 2026-04-23 |
| 加密 | AES-256 | 2026-04-23 |
| 界面语言 | 中文（支持英文切换） | 2026-04-23 |
| 发布形式 | 安装包 + 便携版 | 2026-04-23 |
| 最低系统 | Windows 10 (1809+) | .NET 10 支持的最低版本 |
| 界面风格 | 现代风格，亮色/暗色/跟随系统 三态主题 | 2026-08 |
| 默认压缩级 | 5（平衡） | 2026-04-23 |
| 预览系统 | 内容检测（魔数）+ 扩展名回退 | 2026-04-23 |

> **注意**: WPF 版本 (`MantisZip.UI`) 已在迁移完成后删除，仅保留 Avalonia 版本。

## 项目结构

```
MantisZip/
├── src/
│   ├── MantisZip.Core/              # 核心业务逻辑 (net10.0, 与 UI 框架无关)
│   │   ├── Abstractions/            # IArchiveEngine 接口 + 数据模型
│   │   │   ├── ArchiveEngine.cs     # IArchiveEngine + ArchiveProgress + ArchiveItem
│   │   │   └── ITableDataProvider.cs    # 表格数据提供者接口
│   │   ├── Engines/                 # 各格式引擎实现
│   │   │   ├── ZipEngine.cs         # ZIP (SharpCompress)
│   │   │   ├── SevenZipEngine.cs    # 7z/RAR (SharpSevenZip)
│   │   │   └── TarGzEngine.cs       # TAR/GZ (SharpCompress)
│   │   ├── Services/                # 核心服务
│   │   │   ├── ArchiveTreeBuilder.cs     # 归档树构建
│   │   │   ├── ArchiveEntryLister.cs     # 条目列表与目录聚合
│   │   │   └── CompressService.cs        # 压缩服务
│   │   └── Utils/                   # 工具类
│   │       ├── PasswordManager.cs   # 密码管理器（DPAPI 加密）
│   │       ├── CoreLog.cs           # 调试日志
│   │       ├── LogRedactor.cs       # 日志路径脱敏
│   │       ├── ArchiveEntryExtractor.cs  # 单项预览提取
│   │       ├── ArchiveStructureAnalyzer.cs  # 智能解压根部分析
│   │       ├── FileConflictHelper.cs     # 解压冲突处理
│   │       ├── FileScanner.cs           # 文件遍历扫描
│   │       ├── FileFormatInfo.cs        # 预览元数据模型
│   │       ├── SplitOutputStream.cs     # 分卷压缩输出流
│   │       ├── PeParser.cs / PdfParser.cs / FontParser.cs  # 预览格式解析器
│   │       ├── FlacParser.cs / Id3v2Parser.cs / RiffParser.cs  # 音频解析
│   │       ├── VideoParser.cs           # 视频元数据解析
│   │       ├── IsoParser.cs / OfficeParser.cs / TorrentParser.cs  # 文档/映像解析
│   │       ├── SQLiteParser.cs / SqliteDataReader.cs  # SQLite 解析
│   │       └── ...（其他格式解析器）
│   ├── MantisZip.ShellExt/          # COM 组件（.NET 10 comhost，Explorer 右键菜单）
│   │   ├── ContextMenuHandler.cs    # IShellExtInit + IContextMenu 实现
│   │   ├── ShellExtLog.cs           # 日志（OutputDebugString）
│   │   └── NativeMethods.cs         # Win32 P/Invoke
│   └── MantisZip.UI.Avalonia/       # Avalonia 桌面应用 (net10.0, 主力开发)
│       ├── App.axaml / .cs          # 应用入口
│       ├── AppConstants.cs          # 版本号常量
│       ├── AppSettings.cs           # 设置（JSON 持久化）
│       ├── AppMessageBox.axaml / .cs    # 消息框
│       ├── Converters/              # 值转换器
│       │   ├── BrushResourceConverter.cs
│       │   └── GeometryResourceConverter.cs
│       ├── Controls/                # 可复用控件
│       │   ├── ResultTreeView.axaml / .cs
│       │   ├── QuickPathPicker.axaml / .cs
│       │   ├── InfoPanel.axaml / .cs
│       │   ├── FileFilterEditor.axaml / .cs
│       │   ├── DynamicFormatOptionsPanel.axaml / .cs
│       │   └── QuickPathControl.axaml / .cs
│       ├── Dialogs/                 # 对话框窗口
│       │   ├── AboutWindow.axaml / .cs
│       │   ├── ArchiveCommentDialog.axaml / .cs
│       │   ├── CompressConflictDialog.axaml / .cs
│       │   ├── CompressSettingsWindow.axaml / .cs
│       │   ├── ConflictDialog.axaml / .cs
│       │   ├── DonationDialog.axaml / .cs
│       │   ├── ElevationDialog.axaml / .cs
│       │   ├── ErrorDialog.axaml / .cs
│       │   ├── ExtractSettingsWindow.axaml / .cs
│       │   ├── LogPrivacyHelpDialog.axaml / .cs
│       │   ├── MatchedPasswordDialog.axaml / .cs
│       │   ├── PasswordDialog.axaml / .cs
│       │   ├── PasswordEditDialog.axaml / .cs
│       │   ├── PasswordManagerWindow.axaml / .cs
│       │   ├── ProgressWindow.axaml / .cs
│       │   ├── SettingsWindow.axaml / .cs
│       │   └── UiTestWindow.axaml / .cs
│       ├── Models/                  # 模型
│       │   ├── AppSettings.cs
│       │   ├── ArchiveItemModel.cs
│       │   ├── PreviewTreeNode.cs
│       │   ├── PathPriorityItemModel.cs
│       │   ├── FormatAssocItemModel.cs
│       │   └── SourceArchiveItem.cs
│       ├── Services/                # UI 服务
│       │   ├── ArchiveService.cs
│       │   ├── CompressService.cs
│       │   ├── ExtractService.cs
│       │   ├── SelectedItemsExtractService.cs
│       │   ├── CompressFlow.cs
│       │   ├── ExtractFlow.cs
│       │   ├── DragDropService.cs
│       │   ├── OverlayController.cs
│       │   ├── CustomOleDragDrop.cs
│       │   ├── DropTargetDetector.cs
│       │   ├── DragDropItemExpander.cs
│       │   ├── DragPreviewBitmapBuilder.cs
│       │   ├── PasswordService.cs
│       │   ├── FileFilterHelper.cs
│       │   ├── NativeMethods.cs
│       │   ├── PreviewService.cs
│       │   ├── PreviewCapabilities.cs
│       │   ├── IconService.cs
│       │   ├── IcoParser.cs
│       │   ├── GifDecoder.cs
│       │   ├── MarkdownPreviewBuilder.cs
│       │   ├── ResultPreviewService.cs
│       │   ├── MetadataSettingsManager.cs
│       │   ├── MetadataRenderEngine.cs
│       │   ├── LocalizationManager.cs
│       │   ├── LifetimeDiagnostics.cs
│       │   ├── CompressionOptionData.cs
│       │   ├── SmartOpenPathResolver.cs
│       │   ├── ShellIntegration.cs
│       │   ├── ShellIntegration.Menu.cs
│       │   └── ShellIntegration.Assoc.cs
│       ├── ViewModels/              # 视图模型
│       │   ├── MainWindowViewModel.cs
│       │   ├── PreviewViewModel.cs
│       │   ├── ProgressViewModel.cs
│       │   ├── CompressSettingsViewModel.cs
│       │   ├── ExtractSettingsViewModel.cs
│       │   ├── SettingsWindowViewModel.cs
│       │   ├── IconTestViewModel.cs
│       │   ├── UiTestViewModel.cs
│       │   ├── MetadataPanelSettingsViewModel.cs
│       │   ├── MetadataHelper.cs
│       │   └── SourceArchiveItem.cs
│       ├── Views/                   # 视图
│       │   ├── MainWindow.axaml / .cs
│       │   ├── PreviewPanel.axaml / .cs
│       │   └── SettingsWindow.axaml / .cs
│       ├── Themes/                  # 主题资源
│       │   ├── ThemeLight.axaml
│       │   └── ThemeDark.axaml
│       ├── Localization/            # 中/英/繁中 JSON 翻译资源
│       │   ├── strings.zh-CN.json
│       │   ├── strings.zh-TW.json
│       │   └── strings.en.json
│       └── Resources/               # 图标、样式、菜单图标
├── tests/
│   ├── MantisZip.Tests/            # xUnit (Core 层测试，与 UI 框架无关)
│   │   ├── Engines/                # 引擎测试
│   │   └── Fixtures/               # 测试用压缩包
│   ├── MantisZip.UI.Avalonia.Tests/ # xUnit (Avalonia UI 测试)
│   └── test_encoding/              # 一次性 ZIP 编码调试工具
├── docs/
│   ├── PLAN.md                     # 未来开发计划
│   ├── ARCHITECTURE.md             # 本文件
│   ├── PROGRESS.md                 # 进度里程碑总览（双轨：详情见 progress-*）
│   ├── progress-avalonia-detail.md # Avalonia + 共享层详细变更记录
│   ├── progress-wpf.md             # WPF 遗留版完整历史（仅作参考）
│   ├── CLI.md                      # 命令行指南
│   └── manual-test-checklist.md    # 手动测试清单
├── .omo/
│   ├── notepads/                   # 功能学习笔记
│   └── plans/                      # 设计方案
├── AGENTS.md
└── MantisZip.sln
```

## 设计原则

| 原则 | 说明 |
|------|------|
| **MVVM 模式** | 使用 CommunityToolkit.Mvvm (`ObservableObject` + `[ObservableProperty]`/`[RelayCommand]` source generators) |
| **策略模式** | `IArchiveEngine` 接口 + `ArchiveEngineFactory` 工厂按扩展名分发 |
| **依赖方向** | `MantisZip.UI.Avalonia` → `MantisZip.Core`，核心层无 UI 依赖 |
| **单例设置** | `AppSettings` 单例，JSON 持久化到 `%LOCALAPPDATA%\MantisZip` |
| **编码处理** | per-instance `StringCodec`，不依赖全局 `ZipStrings.CodePage` |
| **路径解析单一事实源** | `ExtractPathResolver` 统一预览树与实际解压的输出路径计算 |

## 技术栈

| 层次 | 技术 |
|------|------|
| 语言 | C# 13 |
| 运行时 | .NET 10 |
| UI 框架 | Avalonia 12 (net10.0, 跨平台就绪) |
| MVVM 框架 | CommunityToolkit.Mvvm 8.4.2 (source generators) |
| 压缩引擎 | SharpCompress 0.48.1 (ZIP/TAR/GZ) + SharpSevenZip 2.0.45 (7z/RAR/ISO) |
| Markdown/HTML | Markdig 0.40.0 + ReverseMarkdown 4.7.0 |
| PDF | PdfPig 0.1.15 + PdfPig.Rendering.Skia 0.1.15.4 |
| SVG | Svg.Skia 2.0.0.5 |
| 字体/图形 | SkiaSharp 3.119.4 + HarfBuzzSharp 14.2.0 |
| Office 文档 | ClosedXML 0.105.0 + DocumentFormat.OpenXml 3.5.1 |
| 编码检测 | Ude.NetStandard 1.2.0 |
| 测试框架 | xUnit v3 + Microsoft.NET.Test.Sdk 17.12.0 |
| 构建 | dotnet CLI |
| 操作系统 | Windows 10 (1809+) / Windows 11 (跨平台支持计划中) |

## 依赖配置

### MantisZip.Core (net10.0)

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| SharpCompress | 0.48.1 | ZIP/TAR/GZ 压缩解压核心引擎 | MIT |
| SharpSevenZip | 2.0.45 | 7z/RAR/ISO 压缩解压（封装 7z.dll） | LGPL-2.1 |
| System.Security.Cryptography.ProtectedData | 10.0.8 | DPAPI 加密存储密码 | MIT |

### MantisZip.UI.Avalonia (net10.0)

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| Avalonia | 12.0.4 | 跨平台 UI 框架 | MIT |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 辅助 (ObservableObject + source generators) | MIT |
| Markdig | 0.40.0 | Markdown 解析 (AST → 原生控件树) | BSD-2-Clause |
| ReverseMarkdown | 4.7.0 | HTML → Markdown 转换 (预览 HTML 降级路径) | MIT |
| PdfPig | 0.1.15 | PDF 解析与逐页渲染 | Apache-2.0 |
| Svg.Skia | 2.0.0.5 | SVG 栅格化 (无需 WebView2) | MIT |
| SkiaSharp | 3.119.4 | 2D 图形渲染 (PDF/SVG/字体位图) | MIT |
| HarfBuzzSharp | 14.2.0 | 字体预览字形布局与连字检测 | MIT |
| ClosedXML | 0.105.0 | XLSX 表格预览 | MIT |
| DocumentFormat.OpenXml | 3.5.1 | DOCX/PPTX 文档解析 | MIT |
| Ude.NetStandard | 1.2.0 | Mozilla 字符编码检测 (文本预览) | MIT |

### MantisZip.ShellExt (net10.0-windows)

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| Microsoft.Data.Sqlite | 10.0.8 | SQLite 预览读取 | MIT |

### 外部工具（运行时依赖）

| 工具 | 用途 | 许可证 | 备注 |
|------|------|--------|------|
| [7z.dll](https://www.7-zip.org/) | 7z/RAR 原生解析 (SharpSevenZip 绑定) | GNU LGPL | 随应用分发，动态链接 |

## 功能规格

### 支持的压缩格式

| 格式 | 压缩 | 解压 | 加密 | 备注 |
|------|:----:|:----:|:----:|------|
| ZIP | ✅ | ✅ | ✅ | AES-256 |
| 7z | ✅ | ✅ | ✅ | SharpSevenZip |
| TAR | ✅ | ✅ | ❌ | |
| GZ (tar.gz) | ✅ | ✅ | ❌ | |
| RAR | ❌ | ✅ | ✅ | 只读 |
| ISO | ❌ | ✅（浏览） | ❌ | SharpSevenZip |

### 功能矩阵

| 功能模块 | 状态 | 备注 |
|----------|:----:|------|
| 打开/浏览压缩包 | ✅ | 目录树 + 文件列表，展平/过滤/排序/尺寸比例条 |
| 解压（全量/选中/智能） | ✅ | 冲突处理五选项 (ask/overwrite/rename/skip/overwrite-if-older/overwrite-if-smaller) |
| 压缩（快速/定制/多卷） | ✅ | 格式/级别/密码/预设/固实压缩/加密头 |
| 文件内预览 | ✅ | 图片/动画/GIF/文本/HTML/Markdown/PDF/SVG/字体/ICO/Office/音频/视频/CSV/SQLite/ISO/Torrent 等 20+ 格式 |
| 压缩包编辑（添加/删除/重命名） | ✅ | ZIP/7z |
| 拖拽导出到资源管理器 | ✅ | 急切提取模型 + Win32 覆层三色状态 + 动态光标 |
| 拖拽添加到压缩包 | ✅ | 窗口内绿/红覆层 + 三分支行为 + 复用冲突处理 |
| 密码管理器 | ✅ | DPAPI 加密，自动尝试匹配 (上限 1000 条，自动尝试前 100 条) |
| 右键菜单 | ✅ | COM shell extension (MantisZip.ShellExt) + 静态注册表回退 |
| 文件关联 | ✅ | 设置页面管理 .zip/.7z/.rar/.tar/.tgz/.gz/.iso |
| 国际化 | ✅ | 中英双语，JSON 资源，运行时切换 |
| 紧凑度模式 | ✅ | Compact/Normal/Loose 三档，运行时切换无需重启 |
| 结果预览面板 | ✅ | 压缩/解压设置窗口实时文件树，冲突高亮、过滤灰显、精简模式、异步加载 |

## 技术说明

### 中文编码支持
- .NET 10+ 需要显式注册 GBK 编码：`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`
- SharpCompress 通过 ReaderOptions 按实例设置编码，无全局 `ZipStrings.CodePage`

### 快速密码验证
- `QuickVerifyPassword` 读第一个加密条目 1 字节验证密码
- ZIP AES: PVV 2 字节校验
- 7z: 构造 `ArchiveFile(path, password)` + 访问 `Entries` 计数
- 失败自动跳到下一条规则，全部失败再弹密码输入框

### 7z 加密预览支持
**实际验证通过：** SharpSevenZip 的 `ExtractFile(index, stream)` 在传入正确密码后，对所有 7z 配置均支持单项提取预览：
- 所有压缩方法（LZMA2 / LZMA / PPMd / BZip2 / Deflate）
- 固实压缩（Solid）与非固实
- AES-256 加密
- 加密文件名（需先传入密码才能列出条目）

`ArchiveEntryExtractor.ExtractSevenZipEntry` 接受 `password` 参数并传递给 `SharpSevenZipExtractor`，与 ZIP 的处理方式一致。代码中不存在 `NotSupportedException`。

⚠ **注意：** "加密文件名"（`EncryptHeaders = true`）的 7z 压缩包，在输入密码前无法读取文件列表（`ArchiveFileData` 会抛出 `SharpSevenZipArchiveException`），UI 需要先弹出密码输入框再尝试列出条目。

### 迁移历史
- **Phases 0–10** (2026-04 至 2026-08): 完整 WPF → Avalonia 迁移
- **当前状态**: 迁移完成，WPF 项目已删除，仅 Avalonia 版本维护
- **遗留文档**: `docs/progress-wpf.md` 保留 WPF 完整历史供参考

## 实现细节（自 AGENTS.md 迁出，2026-10-18）

> AGENTS.md 受工作区指令 64KB 预算限制，以下小节从 AGENTS.md 迁出；AGENTS.md 保留摘要与链接，细节以本文为准。

### 并行解压架构

ZIP 解压支持并行模式（`ZipEngine.SupportsParallelExtract = true`），通过**多实例并行 + 批次复用**实现线程安全与低开销：

**核心原理**：SharpCompress 的 `IArchive` 内部维护共享解压状态（字典、滑动窗口），并发访问会导致 `ZlibException`。解决方案是**每个批次独占一个 archive 实例**，无共享状态。

```csharp
// ✅ 安全：每批次独立打开一次 archive，处理整批文件（Round-Robin 分配）
await Parallel.ForEachAsync(batches, new ParallelOptions { MaxDegreeOfParallelism = N }, async (batch, ct) => {
    using var archive = OpenArchiveWithEncodingFallback(archivePath, password);
    foreach (var key in batch) {
        var entry = archive.Entries.First(e => e.Key == key);
        // ... 解压逻辑
    }
});
```

**实现细节** (`Core/Engines/ZipEngine.cs`)：
- `SupportsParallelExtract` 属性返回 `true`（仅 ZipEngine）
- `ExtractAsync` 根据文件数和 `ArchiveOptions.ParallelExtractDegree` 自动选择串行/并行
- 并行实现 (`ExtractAsyncParallel`)：
  1. 单线程获取所有条目键和大小（预读 archive 一次）
  2. 单线程创建所有目标目录（避免竞态）
  3. 按文件大小**降序排列**（大文件优先，避免长尾效应）
  4. **Round-Robin 分批**（`i % N`）：大文件自动分散到不同批次，天然负载均衡
  5. `Parallel.ForEachAsync` 并行处理各批次，**每批次仅打开 1 次 archive**（减少 80-90% OpenArchive 开销）
  6. 冲突处理用 `lock` 同步；进度报告先在锁内拷贝共享变量、**释放锁后**再 `Report`（避免 8 线程锁竞争）
  7. 4MB 缓冲区 (`CopyBufferSize = 4MB`) 配合 `Stream.CopyToAsync`

> ⚠️ 进度报告锁竞争是性能陷阱：曾在锁内直接调用 `progress?.Report()`，8 线程争用导致 100×1MB 解压从 0.3s 劣化到 8.5s（25x 回退）。

**配置** (`AppSettings.ParallelExtractDegree`)：
- 默认 `Environment.ProcessorCount`（可在设置窗口 1-16 调整）
- `1` = 串行回退

**限制**：
- 仅 `ZipEngine` 支持（`SupportsParallelExtract = true`）
- `SevenZipEngine`/`TarGzEngine` 返回 `false`（7z 依赖 SharpSevenZip 原生多线程，TAR 是顺序流）
- `ExtractEntriesAsync`（过滤解压）暂不并行，仅全量 `ExtractAsync` 支持

**性能**：
- 缓冲区 256KB → 4MB：+70% 吞吐
- 多实例并行：理想环境 6x+ 加速（8核 NVMe）
- 组合优化：~10x 理论峰值

### 7z 多线程压缩（mt=on）

`SevenZipEngine` 通过 `CustomParameters["mt"]` 启用 7z.dll 原生多线程压缩：

- **配置**：`ArchiveOptions.SevenZipMultithreaded`（默认 `true`）→ `ConfigureCompressor` 写入 `mt=on/off`
- **UI 入口**：压缩对话框 7z 面板复选框（`DynamicFormatOptionsPanel`）+ 设置窗口全局默认值（`SettingsWindowViewModel`）
- **传递链**：`DynamicFormatOptionsPanel` → `CompressSettingsWindow.SnapshotFormatOptionsToViewModel` → `CompressSettingsViewModel` → `CompressFlow.BuildRequest` → `CompressRequest.SevenZipMultithreaded` → `CompressService.BuildOptions` → `ArchiveOptions.SevenZipMultithreaded`
- **实测性能**（100 × 1MB 随机数据，8 核）：单线程 38.7s → 多线程 8.4s，**4.63x 加速**（压缩率可能略有下降）
- **验证**：`SevenZipEngineTests.CompressAsync_MultiThreaded_CreatesValidArchive`（解压逐字节比对 + `TestArchiveAsync` 完整性校验）
- **仅 7z 有效**：ZIP/TAR/GZ 无此参数；`SharpSevenZipCompressor` 无 `mt` 属性，只能走 `CustomParameters`

### 预览子系统

预览系统在独立的 `PreviewPanel.axaml` (UserControl) + `PreviewViewModel` + `PreviewService`，MVVM 模式：

- **预览类型枚举**: `PreviewType` (Services/PreviewService.cs): `None`, `Text`, `Csv`, `Pe`, `Image`, `AnimatedImage`（GIF / Animated WebP 共用，`Gif` 保留但废弃），`Svg`, `Font`, `Audio`, `Sqlite`, `Iso`, `Torrent`, `Office`, `Docx`, `Xlsx`, `Pptx`, `Video`, `Html`, `Markdown`, `Pdf`, `IcoGallery`, `Unsupported`
- **格式分发**: `PreviewService.ClassifyPreviewByMagicAsync()`（魔数优先）→ `PreviewViewModel.ShowXxx(filePath)` 方法，扩展名回退
- **HTML/Markdown**: 当前 HTML 走 ReverseMarkdown → Markdig → 原生控件树，Markdown 直接 Markdig AST → 控件树。**计划恢复 WebView 双轨方案**：`Avalonia.Controls.WebView`（各平台原生引擎，Win→WebView2, Mac→WKWebView, Linux→WebKit GTK），不可用时降级到 ReverseMarkdown（详见 `.omo/plans/未开始/html-preview-webview-fallback.md`）
- **PDF**: `UglyToad.PdfPig` + `SkiaSharp` 逐页位图渲染 + 翻页导航（PdfPig 0.1.15 + PdfPig.Rendering.Skia 0.1.15.4）
- **ICO 画廊**: 自实现 `IcoParser` 提取全部多帧，`WrapPanel` 画廊布局，FlattenAlpha 切换，透明背景棋盘格
- **SVG**: `Svg.Skia` 直接栅格化 → `WriteableBitmap`（无需 WebView2）
- **字体预览**: `HarfBuzzSharp` shaping + `SkiaSharp` 位图渲染 + 自动折行 + 连字检测（`CheckFontSupportsLigature` 自动检测，`IsLigatureEnabled` 可开关）
- **GIF**: 自实现 `GifDecoder` + `DispatcherTimer` 逐帧动画（无需 `WpfAnimatedGif`）
- **动画图像（AnimatedImage）**: `PreviewType.AnimatedImage` 取代废弃的 `Gif`，GIF 与 Animated WebP 共用（`ShowImage` 内 SKCodec `FrameCount>1` 分流到动画路径，静态 WebP 保持 Image 预览）
- **预览能力注册表**: `PreviewCapabilities`（[Flags] `PreviewCapability`：Zoom/Transparency/FlattenAlpha/AnimationControls，对齐 MetadataRegistry 模式）按 PreviewType 声明能力，`HasZoomControls`/`HasTransparencyControls`/`HasFlattenAlphaControls`/`HasAnimationControls` 全部查表取代 `HasXxxControls` 硬编码；新增格式注册能力即可复用工具栏
- **两阶段加载**: `ShowPreviewAsync` 拆分 Phase 1（同步显示加载状态+弹跳点动画+信息栏）→ Phase 2（异步提取后显示内容），`_previewLoadVersion` 版本号守卫防竞态
- **透明背景切换**: 图片/GIF/ICO 预览的 `DrawingBrush` 棋盘格（8×8），`IsTransparencyBgShown` 绑定 🏁 按钮
- **信息面板（可配置元数据系统）**: 已从硬编码 `FormatMetadata` 重构为可配置系统（方案见 [metadata-panel-configurable.md](.omo/plans/已完成/metadata-panel-configurable.md)）：
  - **存储**: `MetadataSettingsManager` 持久化到独立 `%LOCALAPPDATA%\MantisZip\metadata-panel.json`（与 AppSettings 同目录不同文件），`SettingsChanged` 事件驱动刷新，`InitializeDefaultConfig` 自动补齐注册类型默认配置
  - **注册与渲染**: `MetadataRegistry`（字段键/显示名/分类注册表）→ `MetadataRenderEngine.RenderCommon/RenderFormat` 按 `MetadataPanelSettings`（`TypeMetadataConfig.Enabled` + `FieldConfig.Position`(infoPanel/contentTop/hidden)/`Order`/`Row`）将字段分发到信息栏（`CommonSections`/`FormatSections` 分区渲染，`SectionOrder` 控制上下）与内容区顶部横条（`ContentTopItems`，随内容滚动）；PE 的旧 `PeTitle`/`PeSubtitle` 已被新系统替代
  - **接线**: `MetadataHelper.RenderCommonToViewModel/RenderFormatToViewModel` 供 `PreviewViewModel` 调用，同时同步 `FormatMetadata` 向后兼容；`UpdateCommonMetadata`（Phase 1）→ `ShowXxx`（Phase 2）两阶段填充
  - **全局显隐（持久化）**: 面板显隐 = 内容驱动 `IsInfoPanelVisible` && 用户偏好 `ShowInfoPanel`（`IsInfoPanelEffectiveVisible`）；`ShowInfoPanel` 持久化到 `AppSettings.ShowPreviewInfoPanel`（默认 true，与 WPF 同名），菜单（`ToggleInfoPanelVisibility`）与设置窗口双入口；`ApplyInfoPanelOrientation()` 切换右侧/下方位置（`InfoPanelOrientation` 已持久化）
- **DataGrid**: CSV/SQLite 预览用 `Avalonia.Controls.DataGrid`（手动列创建以绕过 `AutoGenerateColumns` 对 `DataView` 的 bug）
- **结果预览面板**: `ResultPreviewService` 构建文件树 → `ResultTreeView` 控件（Compact/Full 模式、冲突高亮、过滤灰显、深度/文件数截断、摘要栏）。构建期间显示加载覆层：`IsLoading`/`BuildProgress` StyledProperties 驱动（`OnIsLoadingChanged`/`OnBuildProgressChanged`，进度经 `IProgress<double>` 上报，<250ms 快速构建闪覆层、慢构建显示确定/不定进度条）
- **魔数检测**: `PreviewService.ClassifyPreviewByMagicAsync` 通过文件魔数优先判定格式，与扩展名冲突时 FormatMetadata 显示警告提示
- **部分提取（元数据优先）**: `ArchiveEntryExtractor.ExtractHeadAsync`（Core/Utils，`ArchiveEntryExtractor.cs:213`）提取条目前 N 字节到内存，`ExtractHeadTailAsync`（:257）双端提取（head + tail）；ZIP/7z/RAR/Tar/Gz 均支持，**7z 固实自动降级**（`IsSevenZipSolid` 检测 + `ExtractHeadViaFullExtractAsync` 全量提取），Deflate ZIP 尾部提取有 10MB 上限。被魔数检测消费，为元数据优先预览的横切基础设施（对应 preview-extended-formats.md Phase 5）

### Shell integration

`ShellIntegration` (static class) 实现 Windows Explorer 上下文菜单集成。CLI 全部原生化（`--install-shell`/`--uninstall-shell` 等直接调用 ShellIntegration）。

安装 Windows Explorer 上下文菜单条目 via `HKCU\Software\Classes` — no admin required.

Since v0.3.7, `Install()` tries COM component registration first (`MantisZip.ShellExt.comhost.dll` via `InstallCom()`), falling back to static registry verbs if the COM host file is missing. `Uninstall()` clears both COM CLSID + shellex and all static verbs. `IsInstalled` checks the COM CLSID first.

#### COM context menu (ShellExt)

`ContextMenuHandler.cs` implements `IShellExtInit` + `IContextMenu` as a COM component hosted by `Explorer.exe`. Two groups:

- **Extract group** (inside "打开/解压" submenu, archive files only): 打开压缩包, 原地解压包, 智能原地解压, 解压到 {name}, 解压到……
- **Compress group** (inside "压缩" submenu): 压缩到 {name}.zip, 压缩到 {parentDir}.zip, 压缩……

Multi-file selection changes text dynamically: "打开压缩包 等 {N} 个文件", "原地解压{N}个压缩包", "智能原地解压{N}个压缩包".

#### Icon system

11 `.ico` files (`App.ico`, `Open.ico`, `Extract.ico`, `ExtractHere.ico`, `ExtractSmart.ico`, `ExtractToNamed.ico`, `ExtractTo.ico`, `Compress.ico`, `CompressSeparate.ico`, `CompressCombined.ico`, `CompressDialog.ico`) from `src\MantisZip.UI\Resources\MenuIcons\` are embedded as managed resources in `MantisZip.ShellExt.dll` (`MantisZip.ShellExt.csproj` EmbeddedResource, lines 30-41). Loaded at runtime via `GetIconForCommand()`:

1. Read .ico header → find 16×16 entry → extract raw image data
2. `CreateIconFromResourceEx` → get HICON
3. `ConvertIconToBitmap`: `CreateDIBSection` (32-bit DIB, top-down, alpha channel) → `DrawIconEx` → HBITMAP
4. HBITMAPs cached per command type (11 static fields: `_cachedIconMantisZip`, `_cachedIconOpen`, `_cachedIconExtract`, `_cachedIconExtractHere`, `_cachedIconSmartExtract`, `_cachedIconExtractToNamed`, `_cachedIconExtractTo`, `_cachedIconCompress`, `_cachedIconCompressSeparate`, `_cachedIconCompressCombined`, `_cachedIconCompressDialog`)
5. `CleanupIconCache` called at the **start** of each `QueryContextMenu` (not the end, because Explorer draws asynchronously after `QueryContextMenu` returns)

Uses pure Win32 API — no `System.Drawing` dependency (COM host can't use it).

#### Menu text localization

ShellExt reads localized menu text from registry (`HKCU\Software\MantisZip\ContextMenu\Text*`), written by `ShellIntegration.WriteMenuTextToRegistry()` during `InstallCom()`. The UI project's `L.T()` translates 8 `ShellExt_*` keys (zh + en in `strings.*.json`). Fallback to hardcoded Chinese defaults if registry values are absent.

Two modes controlled by `AppSettings.EnableDynamicMenu` (default: on):

- **COM dynamic mode** (default): `InstallCom()` registers the ShellExt COM handler (`MantisZip.ShellExt.comhost.dll`); if COM is unavailable or not yet loaded in Explorer, `CheckComStatus()` installs the static cascade as fallback
- **Static cascade mode** (`EnableDynamicMenu = off`): single "MantisZip" submenu with separators between 浏览/压缩/解压 groups, numbered verbs via `ExtendedSubCommandsKey`

> 注：旧的 `EnableCascadingMenu`（Cascade vs 顶层独立 Verb 两形态开关）已于 WPF 时期移除（commit `5b431fb`，verb 模式作为死代码废弃，静态路径仅存级联一种形态）；Avalonia 全仓无此字段。

Menu items with individual toggles:

| # | Menu Item | Toggle | CLI Trigger |
|---|---|---|---|
| 1 | 打开压缩包 — Open archive | EnableOpenMenu | `--open` |
| 2 | 压缩菜单 — Compress dialog | EnableCompressMenu | `--compress` |
| 3 | 压缩到独立的（文件名）— Per-item archives | EnableCompressSeparate | `--compress-separate` |
| 4 | 压缩到（父目录名）— Combined archive | EnableCompressCombined | `--compress-combined` |
| 5 | 解压到此处 — Extract here | EnableExtractHereMenu | `--extract-here` |
| 6 | 智能解压到此处 — Smart extract | EnableSmartExtractMenu | `--extract-smart` |
| 7 | 解压到（压缩包名）— Extract to named folder | EnableExtractToNamedMenu | `--extract-to-name` |
| 8 | 解压到…… — Extract to… | EnableExtractToMenu | `--extract` |

Open and Extract verbs use `AppliesTo` filter (archive extensions only). Icons via `GetMenuIconPath()` (static cascade mode only; COM mode uses embedded .ico resources) — maps each verb to `Resources\MenuIcons\*.ico` in the output dir (Open/ExtractHere/ExtractSmart/ExtractToNamed/ExtractTo/CompressSeparate/CompressCombined/CompressDialog), returns null if the file is missing.
