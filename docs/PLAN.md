# MantisZip - 全功能解压缩软件

> 详细开发计划及进度跟踪文档

**项目状态**: 🟢 开发中 (Phase 6 — 代码重构与文档更新)  
**创建日期**: 2026-04-23  
**最后更新**: 2026-05-29  
**当前版本**: 0.3.4

---

## 技术决策记录

| 决策项 | 选择 | 日期 |
|--------|------|------|
| 开发语言 | C# (.NET 9, Windows) | 2026-04-23 |
| UI 框架 | WPF | 2026-04-23 |
| 架构模式 | Code-behind（非 MVVM） | 2026-04-23 |
| 目标用户 | 普通用户 | 2026-04-23 |
| 压缩格式 | 全部（ZIP + 7z + TAR/GZ + RAR只读） | 2026-04-23 |
| 加密 | AES-256 | 2026-04-23 |
| 界面语言 | 中文 | 2026-04-23 |
| 发布形式 | 安装包 + 便携版 | 2026-04-23 |
| 最低系统 | Windows 10 (1809+) | .NET 9 支持的最低版本 |
| 界面风格 | 现代风格 | 2026-04-23 |
| 主题切换 | 需要（亮色/暗色） | 2026-04-23 |
| 默认压缩级 | 5（平衡） | 2026-04-23 |
| 图片预览 | 需要 | 2026-04-23 |
| 文件关联 | 需要 | 2026-04-23 |

---

## 零、依赖配置

### MantisZip.Core

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| SharpCompress | 0.48.1 | ZIP/TAR/GZ 压缩解压核心引擎（替代 SharpZipLib） | MIT |
| SharpSevenZip | 2.0.45 | 7z/RAR/ISO 压缩解压（封装 7z.dll） | LGPL-2.1 |
| SharpZipLib | 1.4.2 | 遗留 — 仅少量兼容代码 | MIT |
| System.Security.Cryptography.ProtectedData | 10.0.8 | DPAPI 加密存储密码 | MIT |

### MantisZip.UI

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 辅助（仅用部分基类） | MIT |
| Markdig | 1.2.0 | Markdown → HTML 渲染 | BSD-2-Clause |
| Ookii.Dialogs.Wpf | 5.0.1 | 文件夹选择对话框 | BSD-3-Clause |
| Ude.NetStandard | 1.2.0 | 字符编码检测（文本预览） | MIT |
| WpfAnimatedGif | 2.0.2 | GIF 动画支持 | MIT |
| Microsoft.Web.WebView2 | 1.0.3967.48 | Edge Chromium 内核，替代 WebBrowser（IE），支持 PDF 原生渲染、现代 CSS | BSD-3-Clause |

### 测试

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| xunit | 2.9.2 | 单元测试框架 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.12.0 | 测试运行器 | MIT |

### 外部工具（运行时依赖）

| 工具 | 用途 | 许可证 | 备注 |
|------|------|--------|------|
| [7z.dll](https://www.7-zip.org/) | SharpSevenZip 绑定，7z/RAR 原生解析 | GNU LGPL | 随应用分发，动态链接 |

## 一、技术选型

### 1.1 核心技术栈

| 类别 | 选择 | 说明 |
|------|------|------|
| 开发语言 | C# (.NET 9, Windows) | 现代 .NET，CLI 直接支持 |
| UI 框架 | WPF | Windows 原生体验，高性能 |
| 架构模式 | Code-behind | 所有逻辑在 MainWindow.xaml.cs（及 partial class 文件）和 App 系列 partial class |
| 压缩库 | SharpCompress (ZIP/TAR/GZ) + SharpSevenZip (7z/RAR) | 引擎统一已于 v0.3.4 完成 |

### 1.2 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10 (1809+) / Windows 11 |
| 运行时 | [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) |
| 7z 压缩 | 使用 SharpSevenZip（7z.dll 原生绑定），无需外部 7z.exe |
| 开发工具 | Visual Studio 2022+ / dotnet CLI |

### 1.3 项目结构

```
MantisZip/
├── src/
│   ├── MantisZip.Core/              # 核心业务逻辑
│   │   ├── Abstractions/            # IArchiveEngine + ArchiveProgress 等模型
│   │   │   ├── ArchiveEngine.cs     # 接口 + 数据模型
│   │   │   └── ITableDataProvider.cs    # 表格数据提供者接口（SQLite/Office）
│   │   ├── Engines/                 # 各格式引擎实现
│   │   │   ├── ZipEngine.cs         # ZIP (SharpCompress)
│   │   │   ├── SevenZipEngine.cs    # 7z/RAR (SharpSevenZip)
│   │   │   └── TarGzEngine.cs       # TAR/GZ (SharpCompress)
│   │   └── Utils/                   # 工具类
│   │       ├── PasswordManager.cs   # 密码管理器（DPAPI 加密）
│   │       ├── CoreLog.cs           # 调试日志（支持隐私脱敏）
│   │       ├── LogRedactor.cs       # 日志路径脱敏
│   │       ├── ArchiveEntryExtractor.cs  # 单项预览提取（ZIP/7z）
│   │       ├── ArchiveStructureAnalyzer.cs  # 智能解压根部分析
│   │       ├── FileConflictHelper.cs     # 解压冲突处理
│   │       ├── FileScanner.cs           # 文件遍历扫描（压缩用）
│   │       ├── FileFormatInfo.cs        # 预览元数据模型
│   │       ├── SplitOutputStream.cs     # 分卷压缩输出流
│   │       ├── PeParser.cs              # PE 可执行文件解析
│   │       ├── PdfParser.cs             # PDF 元数据解析
│   │       ├── FontParser.cs            # 字体文件解析（TTF/OTF/WOFF）
│   │       ├── FlacParser.cs            # FLAC 音频元数据
│   │       ├── Id3v2Parser.cs           # ID3v2 标签解析（MP3 等）
│   │       ├── RiffParser.cs            # RIFF 格式解析（WAV）
│   │       ├── VideoParser.cs           # 视频元数据解析（MP4/MKV/AVI）
│   │       ├── IsoParser.cs             # ISO 映像元数据
│   │       ├── OfficeParser.cs          # Office 文档解析（docx/xlsx/pptx）
│   │       ├── TorrentParser.cs         # BT 种子解析
│   │       ├── SQLiteParser.cs          # SQLite 数据库头解析
│   │       └── SqliteDataReader.cs      # SQLite 表数据读取
│   └── MantisZip.UI/                # WPF 桌面应用（net9.0-windows）
│       ├── MainWindow.xaml / .cs    # 主窗口（所有逻辑 code-behind）
│       ├── MainWindow.DragDrop.cs   # 拖拽导出
│       ├── MainWindow.Menu.cs       # 菜单事件
│       ├── MainWindow.Preview.cs    # 文件预览入口 + 分发
│       │   ├── Preview.Image.cs     # 图片/GIF 预览
│       │   ├── Preview.Metadata.cs  # 元数据预览（PE/PDF/字体/音视频等）
│       │   ├── Preview.Text.cs      # 文本/CSV 预览
│       │   └── Preview.Web.cs       # HTML/Markdown/SVG 预览
│       ├── MainWindow.UI.cs         # UI 辅助方法
│       ├── App.xaml / .cs           # 应用入口（核心：OnStartup、主题、选项创建）
│       ├── App.Cli.cs               # CLI 命令处理器（--compress/--extract/--open 等）
│       ├── App.PipeServer.cs        # 命名管道 IPC 多实例通信
│       ├── App.Password.cs          # 密码管理（TryMatchPassword / QuickVerify 等）
│       ├── App.Logging.cs           # 日志子系统
│       ├── AppConstants.cs          # 版本号常量
│       ├── AppSettings.cs           # 用户设置（JSON 持久化）
│       ├── SettingsWindow.xaml / .cs    # 设置窗口（六标签页）
│       ├── ProgressWindow.xaml / .cs    # 双进度条 + 密码区 + 暂停
│       ├── CompressSettingsWindow.xaml / .cs  # 压缩配置对话框
│       ├── ConflictDialog.xaml / .cs       # 解压冲突弹窗
│       ├── CompressConflictDialog.xaml / .cs  # 压缩冲突弹窗
│       ├── ErrorDialog.xaml / .cs         # 错误提示弹窗
│       ├── AppMessageBox.xaml / .cs       # 统一消息框
│       ├── ArchiveCommentDialog.xaml / .cs    # 压缩包注释编辑
│       ├── PasswordDialog.xaml / .cs      # 密码输入框
│       ├── PasswordEditDialog.xaml / .cs  # 密码编辑框
│       ├── PasswordHelpDialog.xaml / .cs  # 密码帮助窗口
│       ├── PasswordManagerWindow.xaml / .cs  # 密码管理器窗口
│       ├── LogPrivacyHelpDialog.xaml / .cs   # 日志隐私脱敏帮助窗口
│       ├── RatioToWidthConverter.cs       # 文件大小进度条绑定转换器
│       ├── ShellIntegration.cs        # 右键菜单注册（HKCU 无管理员）
│       ├── SystemIconHelper.cs        # SHGetFileInfo 系统图标
│       ├── Localization/              # 本地化资源（中/英 JSON）
│       ├── Themes/                    # 亮色/暗色主题资源字典
│       └── Resources/                 # 图标、样式等资源
├── tests/
│   ├── MantisZip.Tests/              # xUnit 单元测试（40+ 用例）
│   │   ├── Engines/                   # ZipEngine / SevenZipEngine / TarGzEngine 测试
│   │   └── Fixtures/                  # 测试用压缩包生成
│   └── test_encoding/                # 一次性 ZIP 编码调试工具
├── docs/
│   ├── PLAN.md                       # 开发计划与进度（本文件）
│   ├── ARCHITECTURE.md               # 项目架构与技术栈
│   ├── CLI.md                        # 命令行使用指南
│   ├── PROGRESS.md                   # 版本历史变更日志
│   └── manual-test-checklist.md      # 手动测试清单
├── .sisyphus/
│   ├── notepads/                                # 功能学习笔记
│   └── plans/                                   # 详细设计方案文档
├── AGENTS.md
└── MantisZip.sln
```

---

### 设计原则

| 原则 | 说明 |
|------|------|
| **Code-behind 模式** | 所有逻辑在 `MainWindow.xaml.cs`（及 partial class 文件），不使用 MVVM |
| **策略模式** | `IArchiveEngine` 接口 + `ArchiveEngineFactory` 工厂按扩展名分发 |
| **依赖方向** | `MantisZip.UI` → `MantisZip.Core`，核心层无 UI 依赖 |
| **单例设置** | `AppSettings` 单例，JSON 持久化到 `%LOCALAPPDATA%\MantisZip` |
| **编码处理** | per-instance `StringCodec`，不依赖全局 `ZipStrings.CodePage`，自动适配系统区域 |

---

## 二、功能规格

### 2.1 支持的压缩格式

| 格式 | 压缩 | 解压 | 加密 | 备注 |
|------|:----:|:----:|:----:|------|
| ZIP | ✅ | ✅ | ✅ | AES-256 |
| 7z | ✅ | ✅ | ✅ | 使用 SharpSevenZip（7z.dll） |
| TAR | ✅ | ✅ | ❌ | |
| GZ (tar.gz) | ✅ | ✅ | ❌ | |
| RAR | ❌ | ✅ | ✅ | 只读 |
| ISO | ❌ | ✅（只读浏览） | ❌ | 基于 SharpSevenZip |

### 2.2 核心功能

| 优先级 | 功能 | 状态 |
|--------|------|------|
| P0 | ZIP 解压 | ✅ 完成 |
| P0 | ZIP 压缩 | ✅ 完成 |
| P0 | 7z 解压 | ✅ 完成 |
| P0 | RAR 解压（只读） | ✅ 完成 |
| P0 | 密码管理器 | ✅ 完成 |
| P0 | 目录树导航 | ✅ 完成 |
| P0 | 文件列表（仅直接子项） | ✅ 完成 |
| P1 | 7z 压缩 | ✅ 完成 |
| P1 | TAR/GZ 格式 | ✅ 完成 |
| P1 | AES-256 加密 | ✅ 完成 |
| P1 | 分卷压缩 | ✅ 完成 |
| P1 | 压缩级别设置 | ✅ 完成 |
| P2 | 文件/图片预览 | ✅ 完成 |
| P1 | 设置系统 | ✅ 完成 |
| P1 | Shell 右键菜单 | ✅ 完成 |
| P1 | CLI 入口点 | ✅ 完成 |
| P2 | 系统图标 | ✅ 完成 |
| P2 | 逐文件进度 | ✅ 完成 |
| P2 | 文件冲突处理 | ✅ 完成 |
| P2 | 文件关联 | ✅ 完成 |
| P2 | 暂停/继续 | ✅ 完成 |
| P2 | 快速密码验证 | ✅ 完成 |
| P1 | 压缩包注释（编辑已有 + 压缩时指定） | ✅ 完成 |
| P1 | 注释分配策略（AllSame/FirstOnly/PerLine） | ✅ 完成 |
| P2 | 7z 压缩保留目录结构（PreserveDirectoryRoot） | ✅ 完成 |

### 2.3 用户交互

| 优先级 | 功能 | 状态 |
|--------|------|------|
| P0 | 拖拽解压（拖入窗口） | ✅ 完成 |
| P0 | 拖拽压缩（拖入文件） | ✅ 完成 |
| P0 | 拖拽解压（拖出到 Explorer） | ✅ 完成 |
| P0 | 进度条 | ✅ 完成 |
| P0 | 可取消 | ✅ 完成 |
| P0 | 版本号显示 | ✅ 完成 |
| P1 | 压缩配置面板 | ✅ 完成 |
| P1 | 设置窗口 | ✅ 完成 |
| P1 | 右键菜单集成 | ✅ 完成 |
| P1 | CLI 快速压缩/解压/浏览 | ✅ 完成 |
| P2 | 暂停/继续 | ✅ 完成 |
| P2 | 密码匹配进度提示 | ✅ 完成 |
| P2 | 暗色主题 | ✅ 完成 |
| P2 | 国际化（中/英） | ✅ 完成 |

### 2.4 系统集成

| 优先级 | 功能 | 状态 |
|--------|------|------|
| P1 | Shell 右键菜单 — 动词模式/层叠双模式，per-verb 独立开关（打开/压缩/压缩到独立的/压缩到父目录/解压到此处/智能解压/解压到压缩包名/解压到…），菜单分组分隔线 | ✅ 完成 |
| P1 | 文件关联（打开方式） | ✅ 完成 |
| P2 | CLI 快速压缩/解压 — `--compress-separate`/`--compress-combined`/`--extract-smart`/`--extract-here`/`--extract-to-name` | ✅ 完成 |
| P2 | 智能解压（Smart Extract）— 自动分析压缩包结构决定是否保留顶层文件夹 | ✅ 完成 |
| P3 | 桌面剪贴板监控 | ⬜ 待开发 |
| P2 | 引擎统一（SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip） | ✅ v0.3.4 已完成 — [查看详细计划](../.sisyphus/plans/engine-unification-sharpcompress.md) |
| P2 | 文件过滤（按类型/文件名/大小/日期过滤压缩和解压） | 📋 计划 — [查看详细计划](../.sisyphus/plans/file-filter-feature.md)，依赖 SharpCompress 迁移 |

### 2.5 CLI 参考

| 参数 | 说明 |
|------|------|
| *(无参数)* | 正常启动主窗口 |
| `--open <路径>` | 启动主窗口并加载压缩包 |
| `--compress <路径1> <路径2> ...` | 显示压缩对话框（支持多实例 IPC 合并路径） |
| `--compress-quick <路径1> ...` | 使用默认设置直接压缩，显示进度窗口 |
| `--compress-separate <路径1> <路径2> ...` | 依次将每个选定项压缩到各自所在目录（IPC 合并） |
| `--compress-combined <路径1> <路径2> ...` | 将所有选定项合并压缩到公共父目录（跨盘时弹窗输入名称） |
| `--extract <路径>` | 显示解压到…选择目录对话框 |
| `--extract-here <路径>` | 解压到当前目录 |
| `--extract-smart <路径>` | 智能解压（自动检测是否保留顶层文件夹） |
| `--extract-to-name <路径>` | 解压到以压缩包名命名的子目录 |
| `--install-shell` | 安装 Shell 右键菜单 |
| `--uninstall-shell` | 卸载 Shell 右键菜单 |
| `--install-assoc` | 安装文件关联（.zip/.7z/.rar 等默认用 MantisZip 打开） |
| `--uninstall-assoc` | 卸载文件关联 |
| `--test` | 启动测试模式（检查应用配置是否正确） |

**示例**:
```powershell
MantisZip.UI.exe --open "D:\文档.zip"
MantisZip.UI.exe --compress-quick "D:\照片" -- "D:\备份.zip"
MantisZip.UI.exe --compress-separate "D:\照片" "D:\文档"
MantisZip.UI.exe --compress-combined "D:\照片" "D:\文档"
MantisZip.UI.exe --extract "D:\软件包.7z"
MantisZip.UI.exe --extract-smart "D:\软件包.7z"
```

---

## 三、开发计划

### Phase 1: 项目初始化与基础架构
**目标**: 建立项目结构，实现基础 ZIP 压缩/解压 — **100%**

| 序号 | 任务 | 状态 |
|------|------|------|
| 1.1 | 创建解决方案与项目结构 | ✅ 完成 |
| 1.2 | 配置 NuGet 依赖 | ✅ 完成 |
| 1.3 | 实现 Core 层架构 | ✅ 完成 |
| 1.4 | 实现 ZIP 解压 | ✅ 完成 |
| 1.5 | 实现 ZIP 压缩 | ✅ 完成 |
| 1.6 | 实现基本 UI 框架 | ✅ 完成 |
| 1.7 | 目录树导航 | ✅ 完成 |
| 1.8 | 文件列表 | ✅ 完成 |
| 1.9 | 密码管理器 | ✅ 完成 |
| 1.10 | 版本号显示 | ✅ 完成 |

### Phase 2: 扩展格式支持与 UI 完善
**目标**: 支持更多压缩格式 — **100%**

| 序号 | 任务 | 状态 |
|------|------|------|
| 2.1 | 7z 压缩 | ✅ 完成 |
| 2.2 | TAR 格式 | ✅ 完成 |
| 2.3 | GZ 格式 | ✅ 完成 |
| 2.4 | 拖拽压缩 | ✅ 完成 |
| 2.5 | 进度对话框 | ✅ 完成 |
| 2.6 | 压缩配置面板 | ✅ 完成 |

### Phase 3: 高级功能
**目标**: 加密、分卷、注释等高级功能 — **100%**

| 序号 | 任务 | 状态 |
|------|------|------|
| 3.1 | AES-256 加密压缩 | ✅ 完成 |
| 3.2 | 密码解密解压 | ✅ 完成 |
| 3.3 | 分卷压缩 | ✅ 完成 |
| 3.4 | 文件预览 | ✅ 完成 |
| 3.5 | 压缩包注释（编辑 + 压缩注释） | ✅ 完成 |
| 3.6 | 引擎统一（SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip） | ✅ v0.3.4 完成 |

### Phase 4: 系统集成与发布
**目标**: Shell 集成、发布与打包 — **95%**

| 序号 | 任务 | 状态 |
|------|------|------|
| 4.1 | Shell 右键菜单（per-verb 独立开关，分组分隔线） | ✅ 完成 |
| 4.2 | 文件关联 | ✅ 完成 |
| 4.3 | CLI 快速压缩/智能解压 | ✅ 完成 |
| 4.4 | 安装包制作 | ✅ 完成 |
| 4.5 | 发布 Release | ⬜ 待开发 |

---

## 四、进度概览

```
Phase 1: ████████████████████ 100%
Phase 2: ████████████████████ 100%
Phase 3: ████████████████████ 100%
Phase 4: ████████████████████ 100%

总体进度: ████████████████████ 100%
```

---

## 五、已知问题与决策记录

| 日期 | 问题 | 决策 | 状态 |
|------|------|------|------|
| 2026-04-24 | 目录树显示重复图标 | 移除 XAML 中的重复 📁 | ✅ 已修复 |
| 2026-04-24 | 子目录显示自身 | FullPath 去除末尾 / 再比较 | ✅ 已修复 |
| 2026-04-24 | 根目录显示所有嵌套层级 | FilterFiles Name.Split.Length == 1 | ✅ 已修复 |
| 2026-04-24 | 压缩时进度条始终为 0% | processedBytes 在报告前进度更新 | ✅ 已修复 |
| 2026-04-24 | ZIP 中文文件名乱码 | GBK 编码 + ZipStrings.CodePage=936 | ✅ 已修复 |
| 2026-05-11 | 拖出到 Explorer 延迟渲染 | 改用 7-Zip 急切提取模型 | ✅ 已解决 |
| 2026-05-13 | 子目录不显示（无显式条目 ZIP） | BuildFolderTree + FilterFiles 推导隐式目录 | ✅ v0.2.2 已修复 |
| 2026-05-13 | CLI 密码对话框不显示 | ShutdownMode OnExplicitShutdown | ✅ v0.2.2 已修复 |
| 2026-05-13 | 多条密码规则只试第一条 | 改为遍历所有 | ✅ v0.2.2 已修复 |
| 2026-05-18 | 设置里「彩色 Emoji」开关无效 | WPF 原生渲染不传 `D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT` 给 DirectWrite。需引入第三方库 Emoji.Wpf | ✅ 已修复 |
| 2026-05-19 | SharpZipLib CommitUpdate 黑盒，添加/删除时旧条目 I/O 阶段无法上报进度 | 改用提取→重压缩方案，逐文件字节加权平滑进度 | ✅ v0.3.4 已修复 |
| 2026-05-21 | 多处空 `catch { }` 吞异常（日志/explorer/设置等） | 统一改为 `App.TraceLog`/`CoreLog.Trace` 记录，异常路径不再丢失信息 | ✅ v0.2.12 已修复 |
| 2026-05-21 | `OpenZipFile` 枚举异常时 ZipFile 文件句柄泄漏 | 用 try-catch 包裹枚举逻辑，异常时释放已打开的 ZipFile | ✅ v0.2.12 已修复 |

---

## 六、变更日志

逐版本详细变更记录见 [docs/PROGRESS.md](docs/PROGRESS.md)「版本历史」。

---

## 七、下一步工作

### 预览格式扩展（P1）— ✅ v0.3.0 已完成

| 格式 | 状态 |
|------|:----:|
| PE 可执行文件（exe/dll） | ✅ 完成 |
| PDF 元数据 + 内容渲染 | ✅ 完成 |
| 字体预览（TTF/OTF/WOFF） | ✅ 完成 |
| 音频元数据（WAV/FLAC） | ✅ 完成 |
| SQLite 数据库预览 | ✅ 完成 |
| ISO 映像元数据 | ✅ 完成 |
| BT 种子（Torrent） | ✅ 完成 |
| Office 文档（docx/xlsx/pptx） | ✅ 完成 |
| SVG 矢量图渲染 | ✅ 完成 |
| 视频元数据（MP4/MKV/AVI） | ✅ 完成 |
| GIF 播放控制 + 帧导航 | ✅ 完成 |
| 工具栏重构（公共控件左/格式控件右） | ✅ 完成 |
| CSV | ✅ v0.3.1 已完成 — 用 `ShowTablePreview` DataGrid 展示（100 行 × 100 列限制） |
| RTF | 📋 待开发 — WPF RichTextBox |
| LNK | 📋 待开发 — IShellLink |
| ZIP 嵌套 | 📋 待开发 — extract→re-LoadArchiveAsync |

### 近期（P2）

| 任务 | 说明 |
|------|------|
| 文本预览语法高亮 | 用 AvalonEdit 替换当前 TextBox，支持 20+ 语言语法高亮（C#/Python/XML/HTML/SQL/JS 等）。加一个 NuGet 包 + 改控件名 + 两行配置即可 |
| 压缩包内重命名/移动 | 右键「重命名」/「移动到…」、F2 快捷键、extract→delete→add 流程；支持 ZIP/7z |
| RAR 压缩（外置 rar.exe/WinRAR.exe） | 通过已安装的 WinRAR 实现 RAR 格式压缩；支持固实/恢复记录/加密/分卷。依赖：rar.exe 或 WinRAR.exe |

### 远期（P3）

| 任务 | 说明 | 工作量 |
|------|------|--------|
| COM 右键菜单 | 动态菜单名（显示文件名）、菜单排序、自定义图标。注册 `*\shellex\ContextMenuHandlers\{GUID}` → [详细设计](.sisyphus/plans/com-context-menu.md) | 中 |
| **VirtualFileDataObject** | COM 原生 IDataObject 替代 WPF 包装，拖拽延迟渲染不崩溃。需 P/Invoke：COMStreamWrapper、FORMATETC、STGMEDIUM → [详细设计](.sisyphus/plans/virtual-file-data-object.md) | 中 |
| 右键菜单目录结构预览 | 在 COM 菜单中读取压缩包 entry 列表，展示文件树（Bandizip 风格） | 高 |
| 外部工具视频元数据 | ffprobe 提取时长/分辨率/编码，显示在信息面板。需用户安装 FFmpeg | 低 |
| 发布 Release | GitHub Releases + 自动构建 | 低 |

---


### 详细设计方案

以下功能已有独立方案设计文档，见 `.sisyphus/plans/`。按优先级排序，已实现的设计方案移入末尾的「已实现设计方案」小节。

#### 待实现设计方案

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P2** | 文本预览语法高亮 (AvalonEdit) | — | 🟢低 | 1-2h | 替换当前 TextBox，支持 20+ 语言语法高亮 |
| **P2** | 便携版模式 | [portable-mode.md](.sisyphus/plans/portable-mode.md) | 🟢低 | 1-2h | 哨兵文件触发，路径重定向到 exe 目录，免注册表 |
| **P2** | 魔数识别（内容检测替代扩展名检测） | [preview-magic-detection.md](.sisyphus/plans/preview-magic-detection.md) | 🔴高 | 6-8h | 剩余工作：按真实内容（非扩展名）判断格式 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.sisyphus/plans/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚；差异化功能亮点 |
| **P2** | 文件列表筛选/搜索 | [file-filter-feature.md](.sisyphus/plans/file-filter-feature.md) | 🟢低 | 1-2h | 搜索框实时过滤 + 子目录显示切换增强 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.sisyphus/plans/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup EXE → WiX MSI 迁移；企业分发、静默安装 |
| **P2** | RAR 压缩（外置 rar.exe/WinRAR.exe） | [rar-compression.md](.sisyphus/plans/rar-compression.md) | 🟡中 | 6-8h | 通过已安装的 WinRAR 实现 RAR 格式压缩；支持固实/恢复记录/加密/分卷 |
| **P3** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.sisyphus/plans/compression-estimator.md) | 🟡中 | 4-5h | 压缩前估算大小/耗时；三级精度策略 |
| **P3** | VirtualFileDataObject | [virtual-file-data-object.md](.sisyphus/plans/virtual-file-data-object.md) | 🔴高 | 6-8h | COM 原生 IDataObject 替代 WPF OLE 桥，拖拽延迟渲染不崩溃 |
| **P3** | COM 右键菜单 | [com-context-menu.md](.sisyphus/plans/com-context-menu.md) | 🔴高 | 4-6h | 动态菜单名、菜单排序、自定义图标；与压缩预设互不阻塞 |
| **P3** | 右键菜单目录结构预览 | — | 🔴高 | 6-8h | COM 菜单中展示压缩包文件树（Bandizip 风格） |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.sisyphus/plans/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到…；extract→delete→add 流程；支持 ZIP/7z |
| **P2** | 批量进度文件列表 | [batch-progress-list.md](.sisyphus/plans/batch-progress-list.md) | 🟡中 | 3-4h | 批量操作进度窗口增加文件列表，每项显示名称+状态；--extract-batch CLI |
| **P2** | 资源管理器路径快速选择 | [explorer-path-switcher.md](.sisyphus/plans/explorer-path-switcher.md) | 🟢低 | 1-2h | Ctrl+G 唤出已打开的资源管理器窗口路径列表，双击填入目标位置 |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.sisyphus/plans/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置，支持加载/覆盖/右键菜单一键使用；Phase 1 无依赖可独立实施 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.sisyphus/plans/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比；独特功能但非核心 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) | 🟡中 | 3-4h | SQLite/Office 等格式抽取为独立类库，按需分发，缩小安装包体积 |
| **P3** | 发布 Release | — | 🟢低 | 1-2h | GitHub Releases + CI 自动构建 |

#### 已实现设计方案

以下设计方案对应的功能已在过往版本中完成，移至此处供回溯参考：

| 功能 | 设计文档 | 实现版本 | 说明 |
|------|----------|:--------:|------|
| 预览格式扩展（12 种元数据格式） | [preview-extended-formats.md](.sisyphus/plans/preview-extended-formats.md) | v0.3.0 | PE/PDF/字体/音频/SQLite/ISO/Torrent/Office/SVG/视频 + 工具栏 + GIF 控制 + 信息面板 |
| 快速压缩拆分为独立/合并两项 | [split-compress.md](.sisyphus/plans/split-compress.md) | v0.2.10 | --compress-separate / --compress-combined，各自独立 IPC 通道 |
| 加载大文件 overlay | [archive-loading-progress.md](.sisyphus/plans/archive-loading-progress.md) | v0.3.1 | 打开大压缩包时居中 overlay 进度条 + 条目计数 |
| 添加到压缩包 / 从压缩包删除 | [archive-add-delete.md](.sisyphus/plans/archive-add-delete.md) | v0.2.9 | 含 IArchiveEngine.DeleteEntriesAsync、UI 入口、确认弹窗 |
| 暗色/亮色主题 | [dark-theme.md](.sisyphus/plans/dark-theme.md) | v0.2.9 | 主题色资源字典 + Appearance 设置页 + 实时切换 |
| 日志隐私脱敏 | [log-privacy-redaction.md](.sisyphus/plans/log-privacy-redaction.md) | v0.2.8 | Core/Utils/LogRedactor + UI 设置面板；三种模式 |
| 国际化 (i18n) | [i18n-localization.md](.sisyphus/plans/i18n-localization.md) | v0.2.8 | JSON 资源文件 + 静态代理类 + MarkupExtension；中英双语 |
| 智能解压 (Smart Extract) | [smart-extract.md](.sisyphus/plans/smart-extract.md) | v0.2.10 | ArchiveStructureAnalyzer 自动分析压缩包结构，决定是否保留顶层文件夹 |
| Quick Compress 拆分为独立/合并两项 | — | v0.2.10 | `--compress-separate` + `--compress-combined`，各自独立 IPC 通道，Shell 菜单两项可分别开关 |
| 设置窗口菜单页分组 | — | v0.2.10 | 浏览/压缩/解压三组 + 分组分隔线，per-item 独立开关，去掉「启用」前缀 |
|WebView2 替换 WebBrowser | — | v0.3.1 |  Edge Chromium 内核渲染 PDF  + HTML/Markdown 现代 CSS 支持；不主动联网 |
| 压缩包注释（编辑已有 + 压缩注释 + 注释分配策略） | — | v0.3.1 | ArchiveCommentDialog + CompressSettingsWindow TabControl Comment tab + CommentDistribution 枚举 |
| Emoji.Wpf 彩色 Emoji 渲染 | — | v0.3.2 | 引入 [Emoji.Wpf](https://github.com/samhocevar/emoji.wpf) NuGet 包，替换 SettingsWindow TabControl 和 MainWindow 工具栏/目录树图标 `<TextBlock>` 为 `<emoji:TextBlock>`，全部启用彩色渲染 |
| 引擎统一 (SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip) | [engine-unification-sharpcompress.md](.sisyphus/plans/engine-unification-sharpcompress.md) | v0.3.4 | 全部 4 阶段 + 清理完成。ZIP 添加/删除进度平滑，无 CommitUpdate 黑盒跳跃 |
| 文件大小进度条 | [file-size-progress-bar.md](.sisyphus/plans/file-size-progress-bar.md) | v0.3.4 | 大小列背景按文件体积比例填充，纯 UI 改动 |
| PNG 透明信息抛弃（Flatten Alpha） | [png-transparency-3way.md](.sisyphus/plans/png-transparency-3way.md) | v0.3.4+ | 工具栏新增 `◼` 按钮切换保留/抛弃 PNG 透明通道，与棋盘格背景按钮独立工作 |



## 八、技术说明

### 中文编码支持
- .NET 9+ 需要显式注册 GBK 编码：`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`
- 引擎统一完成后，不再需要全局设置 ZipStrings.CodePage。SharpCompress 通过 ReaderOptions 按实例设置编码。

### 快速密码验证
- `QuickVerifyPassword` 读第一个加密条目 1 字节验证密码
- ZIP AES: PVV 2 字节校验 → 密码不对立即抛异常
- 7z: 构造 `ArchiveFile(path, password)` + 访问 `Entries` 计数
- 失败自动跳到下一条规则，全部失败再弹密码输入框

---

*此文档将随开发进度持续更新*


