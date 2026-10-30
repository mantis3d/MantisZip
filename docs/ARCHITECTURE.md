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
- **传递链**：`DynamicFormatOptionsPanel` → `CompressSettingsWindow.SnapshotFormatOptionsToViewModel`（经 `CompressSettingsViewModel.CaptureFormatOptions` 回调注入）→ `CompressSettingsViewModel.TryConfirmAsync` 产出 `CompressSettings` 载荷 → `CompressSession.BuildRequest`（私有，`CompressSettings` + `AppSettings` 各取各的字段）→ `CompressRequest.SevenZipMultithreaded` → `CompressService.BuildOptions` → `ArchiveOptions.SevenZipMultithreaded`
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

## 实现细节补充（自 AGENTS.md 迁出，2026-10-20）

> AGENTS.md 受工作区指令 64KB 预算限制，以下小节从 AGENTS.md 迁出；AGENTS.md 只保留摘要与硬性规则，细节以本文为准。

### 大文件 partial 布局（2026-10-20）

三个原本 2400–4400 行的类按功能家族拆为 partial（纯移动，零行为变化）；命名沿用 `ShellIntegration.*` 惯例 `<Type>.<功能区>.cs`。

| 类 | 主干 | partial 文件 |
|---|---|---|
| `App` | `App.axaml.cs`（字段/初始化/CLI 分派/DebugLog） | `App.Theming.cs`（主题/字体/紧凑度）、`App.CliExtract.cs`、`App.CliCompress.cs`、`App.CliInfra.cs`（shell 命令/提权/IPC） |
| `ZipEngine` | `ZipEngine.cs`（能力声明/ListEntries/TestArchive） | `ZipEngine.Open.cs`（打开 + 编码探测）、`.Extract.cs`（全量/过滤/并行）、`.Compress.cs`（CompressAsync + 分卷清理）、`.CompressGroup.cs`（增删条目 + 多线程组） |
| `MainWindowViewModel` | `MainWindowViewModel.cs`（字段/属性/本地化/LoadArchive） | `.Password.cs`、`.Preview.cs`、`.Navigation.cs`、`.Extract.cs`、`.Compress.cs` |

**源码守卫测试注意**：`AppShutdownWiringTests.AppRelPaths` 与 `ExtractPreserveFullPathWiringTests` / `ExtractParallelDegreeWiringTests` 的 `MainViewModelRelPaths` 显式枚举全部 partial；**新增 partial 必须同步追加**，否则守卫只扫描一部分文件。

### 主窗口 UI 延迟加载（2026-10-21）

`MainWindow.axaml` 里**启动时不可见**的区块改为按需创建（每块一个 `ContentControl` 宿主 + `EnsureXxx()` 幂等方法）：

| 区块 | 宿主 | 创建时机 | 元素数 |
|---|---|---|---|
| 过滤栏 | `FilterBarHost` | `IsFilterBarVisible` 首次为 true | ~50 |
| 压缩包内容区 | `ArchiveContentHost` | `IsArchiveLoaded` 首次为 true | ~153 |
| 预览面板 | `PreviewPanelHost` | 内容区创建后 / 首帧后 0.5s 预取 / `PreviewPanelNeeded` 兜底 | ~297 |
| 菜单子项 | —（顶级菜单项常驻） | 悬停 `PointerEnteredItem` 预构建 / `SubmenuOpened` 兜底 | ~100 |
| 诊断菜单（TestMenu） | — | 仅 `#if DEBUG` 创建（Release 完全不加载） | ~100 |

**菜单子项延迟的特殊约定**：

- 顶级菜单项留在 `MainWindow.axaml`（用户一眼能看到），其子项抽到 `Controls/Menus/*MenuItems.axaml`（根 `StackPanel` + `x:Class` → `Children` 可枚举，便于搬运）
- `AttachMenuItems()` 把视图 `Children` 搬进 `MenuItem.Items`，**必须显式设 `DataContext`**（控件脱离原父级后继承链断裂，否则绑定全失效）
- XAML 事件处理器（如 `RecentFileMenuItem_Click`）必须**随视图迁入**——XAML 事件在声明它的类中解析
- 顶级菜单项的 `Header` 绑定仍留在 `MainWindow.axaml`（顶级项本身未搬走）

**约定**：

- 抽出为 `UserControl` 后，视图**公开具名控件**（属性名加 `Control` 后缀，**避免与 XAML 编译器为 `x:Name` 生成的字段同名 → CS0102**），`MainWindow` 保留全部业务逻辑并通过这些属性访问
- 视图根 Grid **沿用原容器**（列/行定义与子元素坐标原样保留）→ 布局零变化
- 依赖视图内部几何的事件（列头右键菜单、地址栏回车）随视图迁入；仅依赖 VM 的事件留在 `MainWindow` 并在创建时**手动订阅**（XAML `EventName="..."` 在 `UserControl` 里解析不到外部处理器）
- **namescope 变更**：拆出后 `this.FindControl<T>("X")` 取不到视图内控件 → 改用视图公开属性
- **状态回写保护**：视图未创建时，依赖视图状态的持久化（如 `CaptureColumnStates`）必须**原样回写**上次读到的快照，否则空值会覆盖用户已保存的设置
- **启动期恢复动作延后**：列状态 / 排序箭头 / 布局快照 / 预览位置等「读盘 → 应用」动作，改到视图创建时执行（构造期只读盘、只存快照）
- **⚠️ 局部资源必须随视图搬走**：`{StaticResource X}` 的查找作用域只到**声明它的那个资源字典**。原声明在 `MainWindow.axaml` 的 `<Window.Resources>`（窗口局部资源）里的转换器，随 XAML 拆到 `UserControl` 后**必须一并搬进 `<UserControl.Resources>`**——否则只在**真正渲染出该控件/模板**时抛 `KeyNotFoundException`（典型：DataGrid 列的 `CellTemplate` 只在生成首行时构建，空集合永不构建 → 打开压缩包才崩）
- 切割 `MainWindow.axaml` 时**用 XDocument（`SetLineInfo`）取行号**，勿用肉眼下标（文件每改一次行号即漂移）

### 依赖流向

```
MantisZip.Core ──────┬── MantisZip.UI.Avalonia ──reference── MantisZip.ShellExt (COM)
(net10.0)            │   (net10.0, 主力开发)                  (Explorer.exe 宿主)
                     │
                ZipEngine    SevenZipEngine    TarGzEngine
               (SharpCompress) (SharpSevenZip) (SharpCompress)
```

### Engine pattern（策略 + 工厂）

所有引擎均在 `MantisZip.Core`，与 UI 框架无关：

- `IArchiveEngine` interface：`ListEntriesAsync`, `ExtractAsync`, `CompressAsync`, `TestArchiveAsync`
- `ArchiveEngineFactory` 静态构造注册引擎，按文件扩展名分发
- `SevenZipEngine.CompressAsync` 使用 `SharpSevenZipCompressor`（7z.dll COM 绑定）
- `ArchiveEntryExtractor`（Core/Utils）处理预览用单项提取；支持 Zip 与 7z

### 进度上报契约

- `ArchiveProgress`（Core/Abstractions/ArchiveEngine.cs）：`PercentComplete`（总体 0–100）、`FilePercentComplete`（可空 double，单文件 0–100）、`FileName`（当前文件）、`Message`
- `ZipEngine` 经缓冲 I/O 拷贝循环逐文件上报（100ms 节流），每个文件起始报 0%、结束报 100%
- `SevenZipEngine.ExtractAsync` / `TarGzEngine.ExtractAsync` 仅在完成时上报 100%
- `SevenZipEngine.CompressAsync` 经 `SharpSevenZipCompressor.Compressing` 事件上报

### ZIP 压缩加速（自适应 / 多线程 / 引擎侧组数）

**三开关互相独立**（`AppSettings.AdaptiveCompression` / `AdaptiveSmartDetect` / `MultiThreadedCompression`）——2026-10-18 双机实测表明**多线程收益与 CPU 相关**：20 核机上 text/mixed 有 10–20% 收益且压缩率更好（text +15%），8 逻辑核机（i7-3770 4C/8T）上净负收益（text 慢 42%、mixed 慢 37%）。任何一刀切静态策略都只对一半硬件正确 → 不做硬门控，UI 用常驻提示说明收益与 CPU 相关。**Phase 2 的 Task 12（本机基准测试）与 Task 13（post-C 组数定标）未做**，最终策略仍待后续；双机数据与评审记录见 `.omo/plans/进行中/adaptive-compress-ui-convergence.md`。

**组数已从 UI 移除**：引擎默认 `clamp(CPU 核数, 4, 16)`（下限 4 避开已证实的 deg=2 陷阱；20 核机实测 16 组最优，deg=8 慢 21–30%）。组内线程数 = 核数/组数（方案 C）**未验证**——默认路径下多为 no-op，仅在「核数 > 16」或「文件数 < 组数」时生效，**不得**当性能依据写进文档/发布说明。

**⚠ ZIP 的 MT 路径与 7z 的 `mt=on` 是两件事**：ZIP MT 按「纯压缩组（7z Deflate）/ 混合组」分派，用 N 个 `mt=off` 的 7z 压缩器组间并行 + copy-mode 合并（不重压缩）；7z 的 `mt=on` 是 7z.dll 原生线程。两者不可混为一谈。

**决策链（Phase 2 / Task 9-11）**

- **唯一入口**：`AdaptiveRuleMatcher.ResolveLevel(filePath, globalLevel, adaptiveCompression, adaptiveSmartDetect, rules, customFormats)`。优先级：**用户规则（扩展名）> SSOT 扩展名 > 魔数 > 全局级别**；引擎 4 条路径（MT 分拣 / MT 组内 7z / 串行写入 / `AddToArchiveAsync`）全部走它，禁止另起一套判定（历史事故：分拣说 Store、写入却 Deflate）。
- **SSOT**：`ZipEntryClassifier` 提供 `IsCompressed(ext)` / `IsCompressedFormat(FileFormat)` / `IsCompressedByMagic(path)` / `TryReadHead` / `MatchCustomFormatByMagic`；`CompressedExtensions` 是扩展名侧唯一事实源，`CompressionCoefficients` 只做压缩率预估。
- **魔数检测**：仅文件 ≥ 64KB（`MagicDetectMinBytes`）才读前 4KB；先按自定义格式 `MagicHex` 匹配（命中后查引用该 FormatId 的用户规则），再按 `FileFormatDetector.Detect` 判断是否为内置「已压缩」格式；任何 IO 异常都回落扩展名规则，不中断压缩。
- **容器 / 子类型分层（2026-10-27，issue #54 / #36）**：`FileFormatDetector.DetectContainer` 只回答「是不是压缩包容器」（Zip / 7z / RAR / GZip 魔数），`Detect` 才对 ZIP 细分为 docx / odt / epub…；`DetectFormat` 一次给全（`FormatDetection(Container, Subtype)`）。**选引擎只读容器** —— `ArchiveEngineFactory.MapContainerToArchiveFormat` 是恒等映射（7 → 6），子类型一律不参与；**选预览器 / 判「内容已压缩」才读子类型**。历史事故：第 3 层只认 5 种 zip 子类型 ⇒「识别得越细越打不开」（jar 的首条目 `META-INF/MANIFEST.MF` 被判成 `Odt`，`Odt` 没有引擎 ⇒ 「不支持的文件格式」）。自解压 exe 走另一条路：`SfxProbe.TryDetectEmbeddedContainer` 仅在「扩展名未命中 + 文件头 `MZ`」时读尾部 8KB 窗口找 zip/7z/RAR 签名，命中即路由 `SevenZipEngine`（7z.dll 自己按签名搜索内嵌包；**禁止**路由 `ZipEngine` —— SharpCompress 对 SFX zip 静默返回 0 条）。
- **规则生效范围**：仅 ZIP 路径；ZIP 加密子路径跳过逐条目降级。MT 路径下只有「仅存储」类规则全程生效，「指定压缩级别」类只对串行路径生效（设置页有提示 + 行内标记）。
- **UI 现状**：设置页「自适应规则」「格式目录」「魔数检测」随自适应开关显示；ZIP 面板魔数检测随自适应开关显隐；「并行压缩组数」与「仅存储格式」控件已删除，`MultiThreadedStoreFormatIds` 仅作迁移字段。
- **默认规则集**：6 类（图片/视频/音频/压缩包/字体/文档），迁移入口 `AppSettings.AdaptiveOverridesDefaultsVersion`；旧「仅存储格式」用户额外项迁移成一条 Store 规则。⚠ 图片类仍含 `Bmp`（旧默认行为保留），与 SSOT「BMP 未压缩」判定不一致，属已记录例外。
- **7z 路径准入**：`IsMultiThreadedEligible` 在两种情况下返回 true —— ① 用户开启 `MultiThreadedCompression`；② 压缩方法为 **Deflate64**（SharpCompress `ZipWriter` 无法写 method 9，必须由 7z 产出，此时 `mt=1` 单线程）。加密、分卷、ZIP32 上限仍是硬前置条件；分卷 + Deflate64 尚无 7z 分卷路径（会回落到不支持 method 9 的串行 writer）。
- **文件名编码**：MT / 加密 ZIP 走 7z，统一 `CustomParameters["cu"]="on"` 强制 UTF-8 条目名（bit 11）；用户显式选 GBK 时 MT 回落串行（7z 无法可靠产出 GBK）。否则 7z 会按系统 ANSI 写名，与 Store 条目的 UTF-8 混用。
- **不可读源文件**：MT 镜像/Store 合并不走 `ReadFileWithRetry`，入口先用 `ReadErrorHandler.FilterUnreadableFiles`（`SharedReadStream` + `ErrorResolver`）预检并上报 Skipped；镜像与 Store 合并均用 `SharedReadStream.OpenRead`，避免被编辑器持有的文件让整次 MT 压缩失败。
- **临时空间**：MT 路径在系统临时目录做「源镜像 + 每组 temp zip + 最终包」，峰值约为输入 + 输出（甚至 2× 输入）；`IsMultiThreadedEligible` 会预检临时盘可用空间（<2× 输入回落串行），但**不能指定临时盘**。串行路径直接写输出，无此开销。
- **重复相对路径**：MT 镜像按相对路径落盘，重复会被覆盖；`CompressAsync` 检测到归一化条目名重复时回落串行，保留重复条目。
- **进度**：N 组与单组 MT 的合并阶段都把 `ZipBinaryRewriter` 的固定百分比线性映射到 `[组阶段终点, 100]`，避免跳到 92%；`FileCompressionFinished` 无文件名，条目 `Completed` 状态仍按 FIFO 近似（仅 UI 状态）。
- **UI 提示**：ZIP 面板的 `ZipAvailabilityHint` 会按加密 / 分卷 / 非 Deflate / GBK 显示「自适应/多线程不生效」原因；`CompressSettingsViewModel.StartCompress` 拦截 Deflate64 + 分卷（Core API 直接调用仍无 7z 分卷路径）。

### ArchiveItem duality

- **Core**：`MantisZip.Core.Abstractions.ArchiveItem` — 引擎产出
- **Avalonia UI**：`ArchiveItemModel`（Models/）包装 `ArchiveItem`，附加 `IconSource`、显示属性、`SizeRatio`（数据条宽度）、`SortOrder`

### 术语：数据条（Data Bars）≠ 进度条（Progress Bar）

2026-10-22 起，用户界面把文件列表里那两条「每行文字后表示相对占比的彩色横条」改称
**数据条 / 資料條 / Data Bars**（菜单项：视图 → 数据条）。它表达的是**数据的相对占比**
（大小 / 压缩后大小 / 压缩率 / 修改时间四列，长度 = 该条目在当前列表中的占比），**不是任务进度**。

| 概念 | 界面名称 | 代码里的名字 |
|---|---|---|
| 文件列表占比条 | **数据条 / Data Bars** | `ShowProgressBars` / `ProgressBarEnabled` / `ProgressBar*Brush`（**沿用旧名**） |
| 解压/压缩窗口的百分比进度 | 进度条（不变） | `ProgressWindow` / `ThemeProgressBgBrush` 等 |

**为何代码名不改**：`AppSettings.ShowProgressBars` 是 settings.json 的**持久化键**（改名 → 老用户设置读不到）；
`ProgressBar*` 主题资源键共 13 个 × 2 主题（改名 → 漏改一个即运行时报 `KeyNotFoundException`）。
两者均已在代码注释里注明「= 界面上的数据条」。i18n key 已同步改为 `Menu_DataBars`。

### 目录聚合 — ComputeDirectoryStats

`ArchiveEntryLister.ComputeDirectoryStats`（`Core/Services/ArchiveEntryLister.cs`）是目录聚合的共享契约，返回 `Dictionary<string, DirStats>`，`DirStats(Count, Size, CompressedSize, NewestModified)`：

- **递归子树语义**：每个文件计入其全部祖先目录（`a/b/c.txt` 同时计入 `a` 与 `a/b`），目录大小/压缩后大小 = 子树内所有文件之和
- `Count` **仅统计文件**（跳过目录条目）
- `NewestModified` = 子树内最新文件修改时间，`DateTime.MinValue` 文件不参与
- **消费者**：Avalonia `MainWindowViewModel.PopulateEntries`（基于过滤后的 `filteredSource` 计算，目录行的大小/日期/压缩后大小由聚合填充）
- **注意**：ResultTreeView 的 `CalculateDescendantStats`（`Services/ResultPreviewService.cs`）是独立的树状聚合，`TotalDescendantCount` 只统计文件节点（不含目录条目）、跳过 `IsFilteredOut` 被过滤项、无压缩后大小/日期 —— 与 `DirStats` 语义不同，勿混用

### 解压路径解析 — ExtractPathResolver

`ExtractPathResolver`（`Core/Utils/ExtractPathResolver.cs`）是**预览树与实际解压**输出路径计算的单一事实来源，杜绝两者不一致：

- **核心方法**：`ResolveRelativePath(entryKey, currentFolder, preserveFullPath)`（裁剪当前浏览层前缀 + `FileConflictHelper.SanitizeEntryPath` 防 Zip Slip），另有 `TrimCurrentFolderPrefix` / `ResolveAll` 便捷封装
- **语义**：`preserveFullPath=false` 且 `currentFolder` 非空时裁剪前缀；前缀不匹配的条目保持原路径；`SanitizeEntryPath` 丢弃 `..`/`.` 段但保留普通目录段（`docs/../../evil.txt` → `docs/evil.txt`；真正的路径逃逸拦截在 `GetSafePath`）
- **消费者**（必须用同一 resolver，勿另写路径逻辑）：
  - `SelectedItemsExtractService.ExtractEntriesAsync`（实际解压，经 `pathOverrides` 喂给引擎）
  - `ResultPreviewService.BuildExtractPreview`（预览树，`preserveFullPath`/`currentFolder` 参数，恶意路径条目逐条跳过以防整树损毁 —— 解压侧保持抛异常整批失败）
- **入口**：`MainWindowViewModel.ExtractSelectedTo` 把 `CurrentFolder` + 设置传给 `CustomFilePickerDialog.ShowExtractFolderAsync`（→ 预览）与实际解压，输入相同故结果必然一致
- **文件过滤**：Avalonia 版 `ExtractSettingsWindow` 过滤后，`MainWindowViewModel.ExtractArchive` 在 `FilteredEntryKeys` 非空时改走 `engine.ExtractEntriesAsync` 只解压匹配项；否则保持 `ExtractService.ExtractAsync` 全量

### 智能打开路径 — SmartOpenPathResolver

`SmartOpenPathResolver`（`MantisZip.UI.Avalonia/Services/SmartOpenPathResolver.cs`，移植自 WPF `App.Extract.cs:662-701`）是**解压完成后打开资源管理器文件夹**的智能路径判断，避免用户多点一次：

- **语义**：`GetCommonRootDirectory` 采用 **100% 严格公共根**——仅当所有**非目录**条目共享同一顶层目录时返回该目录（逐条取 `FullPath` 首个 `/` 前段，任一缺 `/` 或不同即返回 null）；**不**复用 Core 的 `ArchiveStructureAnalyzer.HasSingleRootDirectory`（≥60% 阈值，语义不同，会造成与 WPF 行为不一致）
- **方法**：`ResolveSmartOpenPathAsync(archivePath, dest, password)`（`ListEntriesAsync` → `GetCommonRootDirectory` → `Path.Combine(dest, commonRoot)`，任何异常/空列表兜底返回 dest）
- **消费者**（3 处接线，全部传本次解压实际使用的密码——加密包解压成功后 `ListEntriesAsync` 同密码必然成功）：
  - `MainWindowViewModel.OpenExtractedFolderAsync`（选中条目解压后，`ExtractSelectedEntriesCoreAsync` 调用）
  - `MainWindowViewModel.ExtractArchive`（主对话框全量解压后，按 `OpenFolderAfterExtract` 开关调用——曾因死代码从不打开，已修复）
  - `DragDropService`（拖拽解压后）
- **范围边界**：`ExtractArchiveHere`/`ExtractArchiveToName`（应用内）与 WPF 一致不开文件夹；WPF CLI `--extract-here`/`--extract-to-name` 批处理会打开（`App.Extract.cs:614-619`），Avalonia CLI 是否对齐见 `.omo/plans/未开始/cli-extract-open-folder.md`

### UI 模式：MVVM

使用 `CommunityToolkit.Mvvm` 的 `ObservableObject` + source generators（`[ObservableProperty]`, `[RelayCommand]`）：

- **ViewModels**：`MainWindowViewModel`, `PreviewViewModel`, `ProgressViewModel`, `CompressSettingsViewModel`, `ExtractSettingsViewModel`, `SettingsWindowViewModel`, `IconTestViewModel`（图标测试窗口）、`UiTestViewModel`（UI 控件测试窗口）、`MetadataPanelSettingsViewModel`（元数据面板设置窗口）
- **Services**：`ArchiveService`, `CompressService`, `ExtractService`, `SelectedItemsExtractService`（选择条目解压，消费 `ExtractPathResolver`）、`CompressFlow`（压缩公共流程：冲突弹窗/暂停/历史与密码库后置，主窗口 VM 与 CLI 共用）、`ExtractFlow`（解压公共流程 + 冲突弹窗）、`CompressSession` / `ExtractSession`（唯一执行器：吃 `Settings` 载荷 + `RunContext`，见规则 20）、`MainWindowProgressPresenter` / `CliProgressPresenter`（`IProgressPresenter` 的桌面 / CLI 实现）、`DragDropService`（拖拽解压后置流程）+ `OverlayController`（Win32 覆层）+ `CustomOleDragDrop`（自实现 OLE 拖拽，根治光标）+ `DropTargetDetector`（目标目录检测）+ `DragDropItemExpander`（选中目录展开）+ `DragPreviewBitmapBuilder`（拖拽预览位图）、`PasswordService`（密码验证/自动匹配）、`FileFilterHelper`（文件过滤）、`NativeMethods`（Win32 P/Invoke）、`PreviewService`, `IconService`, `LocalizationManager`, `CompressionOptionData`（选项数据源）、`GifDecoder`（自实现 GIF 解码）、`IcoParser`（ICO 多帧解析）、`MarkdownPreviewBuilder`（Markdig AST→控件树）、`ResultPreviewService`（结果预览树，`BuildExtractPreview` 与解压共用 `ExtractPathResolver` 保证预览=实际）、`MetadataSettingsManager`（元数据面板配置持久化）、`MetadataRenderEngine`（元数据渲染引擎）、`ShellIntegration`（partial 拆分：`ShellIntegration.cs` + `ShellIntegration.Menu.cs` + `ShellIntegration.Assoc.cs`）
- **Views**：`MainWindow.axaml`, `PreviewPanel.axaml` (UserControl), `SettingsWindow.axaml`, `UiTestWindow.axaml`（UI 控件测试，仅 Debug 显示）
- **Controls**：`ResultTreeView`（结果预览可复用控件，Compact/Full 模式、截断/过滤/冲突高亮）、`CustomFilePickerDialog`（**统一文件/目录选择器**，新增选择入口必须用它、禁用原生 StorageProvider，见 AGENTS 规则 16）、`QuickPathPicker`（自包含路径速选控件）、`InfoPanel`（元数据信息面板）、`FileFilterEditor`（文件过滤条件编辑器）、`DynamicFormatOptionsPanel`（压缩格式动态选项，7z 固实块等）、`QuickPathControl`（路径速选控件）
- **Converters**：`BrushResourceConverter`（主题色键→画刷）、`GeometryResourceConverter`（资源键→Geometry）
- **紧凑度模式**：Compact/Normal/Loose 三档，`ApplyCompactness()` 运行时注入 12 个 `DynamicResource`（间距/控件高度/圆角），无需重启
- **上下文工具栏**：目录树工具栏（展开/折叠全部+自动展开开关+过滤器+分隔符切换）+ 文件列表工具栏（选择/反选/展平/排序/地址栏），`PathIcon` 矢量按钮
- 对话框通过 ViewModel 的回调委托（`ShowPasswordDialog`, `ShowExtractSettingsDialog` 等）与 View 解耦

#### 派生属性通知：集中通知模式

当多个计算属性依赖同一组源属性时，**禁止**在每个 `[ObservableProperty]` 字段上逐个标注 `[NotifyPropertyChangedFor]`（容易漏、难维护）。改用 `partial void OnXxxChanged` + 集中通知方法：

```csharp
// ✅ 正确：集中通知
[ObservableProperty]
private bool _optionA;

[ObservableProperty]
private bool _optionB;

partial void OnOptionAChanged(bool value) => NotifyXxxProperties();
partial void OnOptionBChanged(bool value) => NotifyXxxProperties();

private void NotifyXxxProperties()
{
    OnPropertyChanged(nameof(IsVisibleA));
    OnPropertyChanged(nameof(IsVisibleB));
    OnPropertyChanged(nameof(SummaryText));
}

// ❌ 错误：逐个标注，容易漏
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsVisibleA))]
[NotifyPropertyChangedFor(nameof(IsVisibleB))]
[NotifyPropertyChangedFor(nameof(SummaryText))]
private bool _optionA;
```

原则：新增派生属性只需改 `NotifyXxxProperties()` 一处，源字段保持干净。

### 设置系统（AppSettings 分类）

`AppSettings` singleton 存在于 `MantisZip.UI.Avalonia/Models/AppSettings.cs`，序列化到 `%LOCALAPPDATA%\MantisZip\settings.json`（进程级缓存，见 `Load()`/`Save()` 静态缓存）。

设置包含以下分类：
- **压缩**：DefaultFormat (zip/7z/tar.gz), DefaultLevel (1–9), KeepOriginalExtension, ZipEncoding, ZipCompressionMethod, ZipEncryptionMethod, SevenZipCompressionMethod, SevenZipSolid, SevenZipSolidBlockSize, SevenZipDictionarySize, SevenZipNumFastBytes, SevenZipMatchFinder, SevenZipMultithreaded, SevenZipEncryptHeaders
- **分卷**：SplitSizeTag (0=不分卷/1MB/10MB/…), CustomSplitSizeMB
- **解压**：ExtractDestination (ask/same-dir/desktop), FileConflictAction (ask/overwrite/rename/skip), OpenFolderAfterExtract
- **解压扩展**：EnableDragExtract, ExtractPreserveFullPath
- **上下文菜单**：EnableCompressMenu, EnableOpenMenu, ShowMenuIcons, EnableSmartExtractMenu, EnableExtractHereMenu, EnableExtractToNamedMenu, EnableExtractToMenu, EnableCompressSeparate, EnableCompressCombined, EnableDynamicMenu（COM 动态菜单 / 静态级联回退，`EnableCascadingMenu` 已废弃移除）
- **预览**：EnableImagePreview, EnableTextPreview, MaxTextPreviewBytes, ShowPreviewPanel, ShowPreviewInfoPanel, TextPreviewFontSize, TextPreviewFontFamily, TextEncodingPreference, MaxTablePreviewRows, MaxTablePreviewCols, MaxPreviewFileSize, FontPreviewFontSize, FontPreviewSampleText, FontPreviewEnableLigature, PreviewPosition, InfoPanelOrientation, EnableFormatDetection, PreviewHeadSize
- **密码管理**：ShowPasswordMatchNotification, PasswordRevealByDefault
- **外观**：Theme (Light/Dark), MaxRecentFiles, AppFontFamily, CompactnessMode (Compact/Normal/Loose), Language, ShowProgressBars, SeparateDirBaseline, AutoExpandTreeToCurrent（目录树自动展开）, ProgressContentMode / ProgressInfoDensity / ProgressWindowWidth（进度窗口显示偏好：内容模式 / 信息量分级 / 窗口宽度，关闭时写回）, AutoCloseDelaySeconds / ProgressPinByDefault（进度窗口关闭行为：完成后自动关闭延时秒数 / 是否默认钉住 📌）
- **文件关联（Avalonia 新增）**：AssocZip/7z/Rar/Tar/TarGz/Gz/Iso, CustomAssocExtensions
- **收藏夹（Avalonia 新增）**：FavoritePaths (List<string>)
- **调试**：EnableDebugLogging, LogPrivacyMode (off/filename/extension/full)
- **高级**：SevenZipPath, PreserveDirectoryRoot, CleanTempOnStartup

### 预览子系统开发约定

预览在 `PreviewPanel.axaml` + `PreviewViewModel` + `PreviewService`，入口 `ClassifyPreviewByMagicAsync()`（**魔数优先**，扩展名回退）→ `ShowXxx(filePath)`。

新增预览格式必须同步三处：`PreviewType` 枚举、`PreviewCapabilities`（能力查表驱动工具栏按钮，禁止再加 `HasXxxControls` 硬编码）、`MetadataRegistry` 元数据字段。预览路径与实际解压共用 `ExtractPathResolver`（预览=实际）；部分提取走 `ArchiveEntryExtractor.ExtractHeadAsync`/`ExtractHeadTailAsync`（7z 固实自动降级）。

两阶段加载用 `_previewLoadVersion` 防竞态；CSV/SQLite 的 `DataGrid` 必须手动建列（`AutoGenerateColumns` 对 `DataView` 有 bug）。

完整格式清单、能力注册表、信息面板配置系统、PDF/字体/ICO/GIF 实现见上文「预览子系统」。

### CLI entry points

| Argument | Behavior |
|---|---|
| `--install-shell` | Install context menu, then exit |
| `--uninstall-shell` | Uninstall context menu, then exit |
| `--compress <paths...>` | Show compress dialog; multi-instance IPC merges paths from multiple Windows shell invocations |
| `--compress-quick <paths...>` | Direct compress with AppSettings defaults + ProgressWindow, then exit |
| `--compress-separate <paths...>` | Sequential per-item compress to each item's parent directory + ProgressWindow + IPC merge |
| `--compress-combined <paths...>` | Combined single archive from all items with common parent name + IPC merge; prompts if cross-drive |
| `--extract-here <path>` | Direct extract to source directory with AppSettings defaults + ProgressWindow, then exit |
| `--extract-smart <path>` | Smart extract (auto-detect top-level folder) + ProgressWindow, then exit |
| `--extract-to-name <path>` | Extract to named folder (archive name without extension) + ProgressWindow, then exit |
| `--extract <path>` | Direct extract with AppSettings defaults + ProgressWindow, then exit |
| `--open <path>` | Launch MainWindow and load archive for browsing |
| `--open-dispatch <path>` | Launch MainWindow and load archive, or dispatch to extract action per `DoubleClickAction` setting (used by file association / shell verb) |
| `--help`, `-h` | 显示 CLI 帮助（有父控制台时输出到控制台，否则弹窗显示），然后退出 |
| `--test` | 启动自检：显示启动测试成功弹窗（含版本与安装目录），然后退出 |
| _(no args)_ | Normal MainWindow launch |

- `App.axaml.cs` `OnFrameworkInitializationCompleted` 中处理所有 CLI 路由

### System icon helper

`IconService` + `IconProvider` 使用 `SHGetFileInfo`（Windows Shell API）按扩展名取 16×16 文件类型图标；`SHGFI_USEFILEATTRIBUTES` 支持虚拟/不存在的文件；结果缓存于 `ConcurrentDictionary`；含文件夹图标。用于文件列表显示原生 Windows 图标。

### 开发约束与历史踩坑

#### 中文 filename encoding (ZIP)

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```

在 `App.OnFrameworkInitializationCompleted`（Avalonia）设置一次。ZIP 编码按实例经 `StringCodec` 处理（无全局 `ZipStrings.CodePage`）；SharpCompress 的 `ZipArchive` 用 `ReaderOptions.ArchiveEncoding` 按实例配置，尊重系统区域。

#### 7z compression uses SharpSevenZip (7z.dll) — no external 7z.exe required

#### 7z encrypted preview SUPPORTED

**实际验证通过**：SharpSevenZip 的 `ExtractFile(index, stream)` 在传入正确密码后，对所有 7z 配置均支持单项提取预览：

- 所有压缩方法（LZMA2 / LZMA / PPMd / BZip2 / Deflate）
- 固实压缩（Solid）与非固实
- AES-256 加密
- 加密文件名（需先传入密码才能列出条目）

`ArchiveEntryExtractor.ExtractSevenZipEntry` 接受 `password` 参数并传递给 `SharpSevenZipExtractor`，与 ZIP 处理方式一致。代码中不存在 `NotSupportedException`。

⚠ **注意**："加密文件名"（`EncryptHeaders = true`）的 7z 压缩包，在输入密码前无法读取文件列表（`ArchiveFileData` 会抛 `SharpSevenZipArchiveException`），UI 需先弹密码框再尝试列条目。

#### `_currentFormat` classification（曾损坏，已修复）

`_currentFormat` 现由 `LoadArchiveAsync` 中的 `GetFormatByExtension()` 按扩展名推导，替代旧的 `engine.CanHandle()` 判断（会把所有非 ZIP 格式误判为 `SevenZip`）。

#### TarGzEngine metadata（曾损坏，已修复）

- ~~`ListEntriesAsync` 把 `LastModified` 设为 `DateTime.Now`（丢失真实时间）~~ → 现用 `entry.ModTime`
- ~~`CompressAsync` 忽略 `ArchiveOptions.CompressionLevel`（固定 gzip level 5）~~ → 现用 `options.CompressionLevel`

#### Password manager — MaxEntries (1000) and auto-try limit (100)

`PasswordManager` 内置 `MaxEntries = 1000` 上限防暴力破解；`EntryCount` 反映当前数量；满时 `AddPassword` 抛 `InvalidOperationException`；`FindMatchingPasswords(maxResults)` 支持可选上限；`TryMatchPassword` 使用 `maxResults: 100` 并经 `out bool limitReached` 暴露命中上限，调用方据此弹窗。

#### Silent catch blocks — all converted to TraceLog

历史上的 `catch { }`（日志、explorer 启动、设置、TarGzEngine）已全部转为 `App.TraceLog()` 或 `CoreLog.Trace()`，错误路径不丢失。仅保留防御性空 catch（注册表清理、进度窗口清理、日志 flush 尽力而为）。

#### `OpenZipFile` — exception-safe disposal

`ZipEngine.OpenZipFile` 把编码探测枚举（`ZipEntry.Any(...)`）包在 try-catch 中：首个 `ZipFile` 枚举抛异常（损坏包）时先释放文件句柄再重抛。

#### `--compress` / `--compress-separate` / `--compress-combined` IPC multi-instance

三种 `--compress-*` 均用 `Mutex` + `NamedPipeServerStream`：Windows 为每个选中文件启动一个进程，首进程作为收集者，其余经命名管道发送路径后退出；收集窗口 800ms；仅首实例显示压缩对话框或 ProgressWindow。

- `--compress`：Mutex `MantisZipCompressMutex`，pipe `MantisZipCompressPipe`
- `--compress-separate`：Mutex `MantisZipCompressSeparateMutex`，pipe `MantisZipCompressSeparatePipe`
- `--compress-combined`：Mutex `MantisZipCompressCombinedMutex`，pipe `MantisZipCompressCombinedPipe`

#### "Subfolder display" toggle semantics

`ShowSubFoldersCheck` 控制 `FilterFiles` 是否包含嵌套项：**勾选**时子目录内容也平铺显示在当前视图。实现上不重建列表，而是以同一逻辑重新过滤 `_allItems`。

#### Filter / selection guard

`_isProgrammaticFilter` 阻止程序化 `FilterFiles` 触发 `SelectionChanged` 预览。`SelectionChanged` 从 `e.AddedItems`/`e.RemovedItems` 推断「最后点击项」而非 `SelectedItem`，以支持多选（Extended 模式）。

#### No CI, no full test suite, no linters

无 CI workflow、无 pre-commit hook、无 linter/analyzer 配置。`test_encoding/` 是一次性 ZIP 编码调试脚本。`tests/` 有 Core 与无头 Avalonia 两个测试项目，非全量覆盖。

#### Smart Extract

`ArchiveStructureAnalyzer`（Core/Utils）分析压缩包结构，决定智能解压直接解到当前目录还是子目录：

- 扫描顶层条目：若 ≥60% 共享同一根目录 → 带子目录解压；否则直接解压
- 由 `ArchiveEngineFactory.SmartExtractEntriesAsync` 调用分析器后带计算出的目标路径委托 `ExtractAsync`

触发入口：`--extract-smart` CLI 或右键「智能解压」。

### UI 样式与注释约定（细节）

#### ToggleIconBox 切换图标

开关/切换类菜单项的指示图标：

- `Background` 绑定 `{Binding <bool>, Converter={StaticResource BoolToToggleBgBrushConverter}}`（true → `ThemeToggleBrush` 强调色底，false → `Transparent` 空心）
- 内部放 10×10 `PathIcon`，`Foreground` 用 `ThemeTextPrimaryBrush`
- 菜单项中放在 `MenuItem.Icon` 槽位（Fluent 主题 `SharedSizeGroup="MenuItemIcon"` 自动共享列宽对齐，`FluentMenuItemIconTheme` 将 Icon 槽位内容限制在 16×16，故 ToggleIconBox 必须为 16×16），**Header 直接用文本绑定**
- code-behind 动态构建时 `Classes = { "ToggleIconBox" }` 同样生效（继承全局样式）

#### 列表控件行高示例

```xml
<!-- DataGrid -->
<DataGrid ItemsSource="{Binding ...}" RowHeight="{DynamicResource ControlHeightMd}" ... />

<!-- TreeView -->
<TreeView ItemsSource="{Binding ...}">
  <TreeView.Styles>
    <Style Selector="TreeViewItem">
      <Setter Property="MinHeight" Value="{DynamicResource ControlHeightSm}" />
    </Style>
  </TreeView.Styles>
</TreeView>
```

#### AXAML 中文注释示例

```xml
<!-- 文件名标签：显示当前需要输入密码的压缩包名称 -->
<TextBlock x:Name="FileNameText" FontWeight="Bold" FontSize="14" />

<!-- 密码输入框 + 显示/隐藏切换按钮 -->
<Grid ColumnDefinitions="*,Auto">
  <TextBox x:Name="PasswordBox" PasswordChar="●" />
  <Button Grid.Column="1" x:Name="PwdRevealBtn" Click="OnRevealToggle" />
</Grid>

<!-- 扩展保存选项区域：永久保存、自动生成匹配规则、描述、匹配规则 -->
<StackPanel x:Name="SaveOptionsPanel" Spacing="{DynamicResource SpacingSm}">
  <!-- 永久保存到密码库复选框 -->
  <CheckBox x:Name="SavePermanentlyCheck" />
  <!-- 自动生成匹配规则复选框：根据文件名自动生成 glob/regex 规则 -->
  <CheckBox x:Name="AutoGenerateRulesCheck" />
</StackPanel>
```

#### Shell 新增菜单项同步清单

新增菜单项要同时改三处：`ShellIntegration.Menu.cs`（注册）、`strings.*.json`（`ShellExt_*` 三语）、`WriteMenuTextToRegistry()`（写注册表）。**不要给级联动词设 `CommandFlags=8`**（部分 Windows 会隐藏该项，见「已修复问题」）。

### 拖拽（drag-out 解压 / drag-in 添加）

#### drag-out（拖到资源管理器）— 7-Zip eager-extraction 模型

拖出前先解压到临时目录再 `DoDragDrop`，解压 + 拖拽期间显示 `ProgressWindow`。

- **自家窗口防误判**：`_isOwnDrag` 标记阻止 `Window_Drop` 响应「拖出后又拖回」的临时路径（对 add-to-archive 无意义）
- **子目录保留**：输出临时路径使用 `ArchiveItem.FullPath`，保留相对结构；`ExtractEntryForDragAsync` 按需建中间目录
- **取消**：`ProgressWindow` 经 `CancellationToken` 取消；解压未完成则整体跳过 `DoDragDrop`

**Avalonia 实现**（方案见 [drag-drop-direct-extract.md](.omo/plans/未开始/drag-drop-direct-extract.md)），「拖拽即解压到目标目录」实时模式：

1. `MainWindow.axaml.cs` 文件列表 `PointerPressed` 检测拖拽起点（列标题/空白按下不触发），选中项经 `DragDropItemExpander.ExpandItems` 展开为条目集
2. `OverlayController`（纯 Win32 独立覆层，`UpdateLayeredWindow` 后台线程渲染）显示三色状态机（检测中/可释放/不可释放）+ 呼吸动画（拖拽预览弹窗 DragPreviewPopup 待实施：`DragPreviewBitmapBuilder` 位图构建与 `OverlayController.SetPreview` 槽位已就绪但无调用者）
3. `DropTargetDetector` 用 `WindowFromPoint` + ShellWindows 枚举实时检测目标目录（`#32770` 用 `EnumChildWindows`），`IsOverOwnWindow` 用 HWND 比较防止自家窗口误判
4. 松手后 `DragDropService` 执行后置解压流程（模态进度窗口 + 冲突处理 + 本地化状态消息），完成后清理
5. **光标方案 C**：`CustomOleDragDrop` 自实现 OLE `IDataObject`/`IDropSource`/`IEnumFORMATETC`（`CustomDataObject`/`CustomDropSource`/`CustomEnumFormatEtc`）替代 Avalonia `DragDrop.DoDragDropAsync`（其 `OleDragSource` 固定返回 `USEDEFAULTCURSORS` 导致禁止光标无法替换）；`GiveFeedback` 返回 S_OK + 直接 `SetCursor`，光标按 overlay 状态（绿/红/金/灰）动态切换，根治光标问题；Esc/多键取消经 OLE `fEscapePressed` 检测

#### drag-in（拖入窗口添加到压缩包）

已实施（方案见 [drag-add-overlay.md](.omo/plans/已完成/drag-add-overlay.md)）。三分支：

1. **已打开压缩包 + 拖入单个压缩包** → 切换打开
2. **已打开压缩包 + 拖入文件/文件夹** → `Main_DragAddConfirm` 确认框（复用 `CompressConflict_Add` 标题）→ `MainWindowViewModel.AddFilesToArchiveAsync`（从 `AddFiles()` 抽取的公共方法：引擎获取、密码、`CreateExtractOptions` 冲突处理、`entryBasePath`、进度、刷新）
3. **未打开压缩包**：拖入压缩包 → 打开；拖入非压缩包 → 打开 `CompressSettingsWindow` 预填源文件

窗口内覆层 `DragAddOverlay`（方案 A：固定显示 `CurrentFolder`，跟随目录行悬停的方案 B 留作后续）：`DragOver`/`DragLeave` 事件驱动，Avalonia 原生 Border 覆层（不用 Win32 `OverlayController`，与拖拽解压的目标在外部 Explorer 窗口不同）；绿色 `#6BD46B` = 可添加「添加到 {CurrentFolder}」，红色 `#F43643` = 格式不支持（`DragEffects.None`）；呼吸动画与拖拽解压完全同参（`DispatcherTimer` 100ms + 正弦 `80+40·sin(tick·π/10)`，alpha 40-120 约 2s 周期，仅背景层 Opacity 呼吸、边框/文字不透明）+ 8px 同色边框 + 白色粗体文字 + ✓/⚠ 状态图标

### 日志隐私脱敏

`LogRedactor`（Core/Utils）为全部日志输出提供集中式路径脱敏。`RegexOptions.Compiled` 正则三分支（盘符 `C:\...`、UNC `\\server\share\...`、相对 `folder\sub\file.ext`），允许路径含空格（早期草稿排除 `\s` 是错的）。

四种模式由 `AppSettings.LogPrivacyMode` 控制（默认 `"extension"`）：

- **off**：不脱敏
- **filename**：`D:\Photos\private\wedding.jpg` → `wedding.jpg`
- **extension**：`D:\Photos\private\wedding.jpg` → `[PATH_1]\[FILE_1].jpg`（仅保留扩展名）
- **full**：同上 → `[PATH_1]`（顺序 ID，同路径同 ID，上限 10000 条）

**注入**：

- `CoreLog.Initialize(diagnosticsEnabled, privacyMode)` — UI 层在 `OnFrameworkInitializationCompleted` 和 `RefreshDebugLogSettings` 调用，统一注入诊断开关与脱敏委托。**禁止直接设置 `CoreLog.DiagnosticsEnabled` 或 `CoreLog.RedactOverride`**
- `App.Log()` / `App.LogDebug()` / `LogStartup()` 在 UI 项目内直接调用 `LogRedactor.RedactPaths()`

**帮助弹窗**：日志隐私帮助为 Markdown 驱动（`LogPrivacyHelp_Markdown` → `MarkdownHelpContent` → `HelpDialog`），位于设置 → 调试页 `[?]` 按钮。

**关键文件**：`Core/Utils/LogRedactor.cs`（框架无关）、`UI.Avalonia/Dialogs/MarkdownHelpContent.axaml(.cs)`、`UI.Avalonia/Dialogs/HelpDialog.axaml(.cs)`、三语 `LogPrivacyHelp_Markdown`。

### 历史归档

#### 迁移阶段状态（截至 2026-10，历史）

| Phase | 内容 | 状态 |
|-------|------|------|
| 0–9 | 项目骨架·浏览·预览·设置·压缩解压·编辑·样式·交互 | ✅ 已完成 |
| 10 | WPF 功能补齐（进度条·信息面板·状态栏） | ✅ 已完成 |
| UI 功能补齐 | 对话框·控件·转换器补齐 | 27/29 ✅（2 项待 GUI 验证） |
| Shell/COM 集成 | ShellIntegration·ShellExt·文件关联·CLI | ✅ 已完成 |
| i18n + 清理 | 缺失 key 补齐·空目录清理·版本对齐 | ✅ 已完成 |
| HTML 预览升级 | 跨平台 WebView + ReverseMarkdown 降级 | 📋 待实施 |

#### 已修复问题（历史记录）

- **级联菜单 `CommandFlags=8` 隐藏项**：在 `ExtendedSubCommandsKey` 级联子菜单的动词上直接设 `ECF_SEPARATORBEFORE`（`CommandFlags=8`）会让这些项在部分 Windows 版本不显示；改用显式分隔符动词解决
- **IPC 管道只接受一个连接**：`StartPipeServer` 原先只创建一个 `NamedPipeServerStream` 并 `WaitForConnectionAsync` 一次，选中 3+ 文件时第 3+ 个进程 `Connect()` 超时；改为 `while (!ct.IsCancellationRequested)` 循环、每客户端新建管道
- **自动重命名时 `CompressConflictDialog` 弹两次**：点击「自动重命名」时 `Rename` 分支重建了 `CompressConflictDialog`；改为捕获首个对话框的 `CustomName` 直接使用，跳过第二次弹窗
