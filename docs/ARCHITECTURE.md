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
│       ├── Localization/            # 中/英 JSON 翻译资源
│       │   ├── strings.zh-CN.json
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