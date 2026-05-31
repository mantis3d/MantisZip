# 项目架构

## 技术决策

| 决策项 | 选择 | 日期 |
|--------|------|------|
| 开发语言 | C# (.NET 9, Windows) | 2026-04-23 |
| UI 框架 | WPF | 2026-04-23 |
| 架构模式 | Code-behind（非 MVVM） | 2026-04-23 |
| 目标用户 | 普通用户 | 2026-04-23 |
| 压缩引擎 | SharpCompress (ZIP/TAR/GZ) + SharpSevenZip (7z/RAR) | 2026-04-23 |
| 加密 | AES-256 | 2026-04-23 |
| 界面语言 | 中文（支持英文切换） | 2026-04-23 |
| 发布形式 | 安装包 + 便携版 | 2026-04-23 |
| 最低系统 | Windows 10 (1809+) | .NET 9 支持的最低版本 |
| 界面风格 | 现代风格，亮色/暗色主题 | 2026-04-23 |
| 默认压缩级 | 5（平衡） | 2026-04-23 |
| 预览系统 | 内容检测（魔数）+ 扩展名回退 | 2026-04-23 |

## 项目结构

```
MantisZip/
├── src/
│   ├── MantisZip.Core/              # 核心业务逻辑
│   │   ├── Abstractions/            # IArchiveEngine 接口 + 数据模型
│   │   │   ├── ArchiveEngine.cs     # IArchiveEngine + ArchiveProgress + ArchiveItem
│   │   │   └── ITableDataProvider.cs    # 表格数据提供者接口
│   │   ├── Engines/                 # 各格式引擎实现
│   │   │   ├── ZipEngine.cs         # ZIP (SharpCompress)
│   │   │   ├── SevenZipEngine.cs    # 7z/RAR (SharpSevenZip)
│   │   │   └── TarGzEngine.cs       # TAR/GZ (SharpCompress)
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
│   ├── MantisZip.ShellExt/          # COM 组件（.NET 9 comhost，Explorer 右键菜单）
│   │   ├── ContextMenuHandler.cs    # IShellExtInit + IContextMenu 实现
│   │   ├── ShellExtLog.cs           # 日志（OutputDebugString）
│   │   └── NativeMethods.cs         # Win32 P/Invoke
│   └── MantisZip.UI/                # WPF 桌面应用（net9.0-windows）
│       ├── MainWindow.xaml / .cs            # 主窗口（code-behind）
│       ├── MainWindow.DragDrop.cs           # 拖拽导出
│       ├── MainWindow.Menu.cs               # 菜单事件
│       ├── MainWindow.Preview.cs            # 预览入口 + 分发
│       │   ├── Preview.Image.cs             # 图片/GIF
│       │   ├── Preview.Metadata.cs          # PE/PDF/字体/音视频等元数据
│       │   ├── Preview.Text.cs              # 文本/CSV
│       │   └── Preview.Web.cs               # HTML/Markdown/SVG
│       ├── MainWindow.UI.cs                 # UI 辅助方法
│       ├── App.xaml / .cs                   # 应用入口
│       ├── App.Cli.cs / App.PipeServer.cs   # CLI + IPC
│       ├── App.Password.cs / App.Logging.cs # 密码 + 日志
│       ├── AppConstants.cs                  # 版本号常量
│       ├── AppSettings.cs                   # 设置（JSON 持久化）
│       ├── SettingsWindow.xaml / .cs        # 设置窗口
│       ├── ProgressWindow.xaml / .cs        # 双进度条窗口
│       ├── CompressSettingsWindow / ExtractSettingsWindow  # 压缩/解压配置对话框
│       ├── ConflictDialog / CompressConflictDialog / ErrorDialog  # 对话框
│       ├── ArchiveCommentDialog / PasswordDialog / PasswordEditDialog
│       ├── PasswordHelpDialog / PasswordManagerWindow / LogPrivacyHelpDialog
│       ├── ShellIntegration.cs              # 右键菜单注册（HKCU）
│       ├── SystemIconHelper.cs              # SHGetFileInfo 系统图标
│       ├── Localization/                    # 中/英 JSON 翻译资源
│       ├── Themes/                          # 亮色/暗色主题
│       └── Resources/                       # 图标、样式
├── tests/
│   ├── MantisZip.Tests/                    # xUnit（40+ 用例）
│   │   ├── Engines/                        # 引擎测试
│   │   └── Fixtures/                       # 测试用压缩包
│   └── test_encoding/                      # 一次性 ZIP 编码调试工具
├── docs/
│   ├── PLAN.md                    # 未来开发计划
│   ├── ARCHITECTURE.md            # 本文件
│   ├── PROGRESS.md                # 版本历史
│   ├── CLI.md                     # 命令行指南
│   └── manual-test-checklist.md   # 手动测试清单
├── .sisyphus/
│   ├── notepads/                  # 功能学习笔记
│   └── plans/                     # 设计方案
├── AGENTS.md
└── MantisZip.sln
```

## 设计原则

| 原则 | 说明 |
|------|------|
| **Code-behind 模式** | 所有逻辑在 `MainWindow.xaml.cs`（及 partial class 文件），不使用 MVVM |
| **策略模式** | `IArchiveEngine` 接口 + `ArchiveEngineFactory` 工厂按扩展名分发 |
| **依赖方向** | `MantisZip.UI` → `MantisZip.Core`，核心层无 UI 依赖 |
| **单例设置** | `AppSettings` 单例，JSON 持久化到 `%LOCALAPPDATA%\MantisZip` |
| **编码处理** | per-instance `StringCodec`，不依赖全局 `ZipStrings.CodePage` |

## 技术栈

| 层次 | 技术 |
|------|------|
| 语言 | C# 13 |
| 运行时 | .NET 9 |
| UI 框架 | WPF（Windows only） |
| 压缩引擎 | SharpCompress (ZIP/TAR/GZ) + SharpSevenZip (7z/RAR) |
| 测试框架 | xUnit + Microsoft.NET.Test.Sdk |
| 构建 | dotnet CLI / Visual Studio 2022+ |
| 操作系统 | Windows 10 (1809+) / Windows 11 |

## 依赖配置

### MantisZip.Core

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| SharpCompress | 0.48.1 | ZIP/TAR/GZ 压缩解压核心引擎 | MIT |
| SharpSevenZip | 2.0.45 | 7z/RAR/ISO 压缩解压（封装 7z.dll） | LGPL-2.1 |
| SharpZipLib | 1.4.2 | 遗留 — 仅少量兼容代码 | MIT |
| System.Security.Cryptography.ProtectedData | 10.0.8 | DPAPI 加密存储密码 | MIT |

### MantisZip.UI

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 辅助（仅部分基类） | MIT |
| Markdig | 1.2.0 | Markdown → HTML 渲染 | BSD-2-Clause |
| Ookii.Dialogs.Wpf | 5.0.1 | 文件夹选择对话框 | BSD-3-Clause |
| Ude.NetStandard | 1.2.0 | 字符编码检测（文本预览） | MIT |
| WpfAnimatedGif | 2.0.2 | GIF 动画支持 | MIT |
| Microsoft.Web.WebView2 | 1.0.3967.48 | Edge Chromium 内核（PDF/HTML/Markdown/SVG） | BSD-3-Clause |

### 测试

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| xunit | 2.9.2 | 单元测试框架 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.12.0 | 测试运行器 | MIT |

### 外部工具（运行时依赖）

| 工具 | 用途 | 许可证 | 备注 |
|------|------|--------|------|
| [7z.dll](https://www.7-zip.org/) | SharpSevenZip 绑定 | GNU LGPL | 随应用分发 |

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
| 打开/浏览压缩包 | ✅ | 目录树 + 文件列表 |
| 解压（全量/选中/智能） | ✅ | 冲突处理四选项 |
| 压缩（快速/定制/多卷） | ✅ | 格式/级别/密码/预设 |
| 文件内预览 | ✅ | 图片/文本/HTML/元数据等 20+ 格式 |
| 压缩包编辑（添加/删除/重命名） | ✅ | ZIP/7z |
| 拖拽导出到资源管理器 | ✅ | 急切提取模型 |
| 密码管理器 | ✅ | DPAPI 加密，自动尝试 |
| 右键菜单 | ✅ | COM shell extension |
| 文件关联 | ✅ | 设置页面管理 |
| 国际化 | ✅ | 中英双语，JSON 资源 |

## 技术说明

### 中文编码支持
- .NET 9+ 需要显式注册 GBK 编码：`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`
- SharpCompress 通过 ReaderOptions 按实例设置编码，无全局 `ZipStrings.CodePage`

### 快速密码验证
- `QuickVerifyPassword` 读第一个加密条目 1 字节验证密码
- ZIP AES: PVV 2 字节校验
- 7z: 构造 `ArchiveFile(path, password)` + 访问 `Entries` 计数
- 失败自动跳到下一条规则，全部失败再弹密码输入框
