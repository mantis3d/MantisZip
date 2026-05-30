# 项目架构

## 项目结构

```
MantisZip/
├── src/
│   ├── MantisZip.Core/              # 核心业务逻辑
│   │   ├── Abstractions/            # IArchiveEngine 接口 + 数据模型
│   │   ├── Engines/                 # ZipEngine / SevenZipEngine / TarGzEngine
│   │   └── Utils/                   # PasswordManager / ArchiveEntryExtractor / CoreLog
│   └── MantisZip.UI/                # WPF 桌面应用（.net9.0-windows）
│       ├── MainWindow.xaml/.cs      # 主窗口（所有逻辑 code-behind）
│       ├── MainWindow.Preview.cs    # 文件预览子系统
│       ├── MainWindow.DragDrop.cs   # 拖拽导出
│       ├── MainWindow.Menu.cs       # 菜单事件
│       ├── MainWindow.UI.cs         # UI 辅助方法
│       ├── App.xaml/.cs             # 应用入口（核心：OnStartup、主题、选项创建）
│       ├── App.Cli.cs               # CLI 命令处理器（--compress / --extract / --open 等）
│       ├── App.PipeServer.cs        # 命名管道 IPC 多实例通信
│       ├── App.Password.cs          # 密码管理（TryMatchPassword / QuickVerify 等）
│       ├── App.Logging.cs           # 日志子系统（Log / LogDebug / TraceLog）
│       ├── AppSettings.cs           # 用户设置（JSON 持久化）
│       ├── SettingsWindow.xaml/.cs  # 设置窗口
│       ├── ShellIntegration.cs      # 右键菜单注册（HKCU 无管理员）
│       ├── SystemIconHelper.cs      # SHGetFileInfo 系统图标
│       ├── ProgressWindow.xaml/.cs  # 双进度条窗口
│       ├── CompressSettingsWindow   # 压缩配置对话框
│       ├── ExtractSettingsWindow    # 解压设置对话框（TabControl + GroupBox，与 Compress 风格一致）
│       ├── ArchiveCommentDialog     # 压缩包注释编辑对话框
│       ├── PasswordDialog           # 密码输入/管理对话框
│       ├── FileConflictHelper.cs    # 解压冲突处理
│       └── AppMessageBox.xaml/.cs   # 统一消息框
├── tests/
│   └── MantisZip.Tests/            # xUnit 单元测试（40+ 用例）
│       ├── Engines/                 # ZipEngine / SevenZipEngine / TarGzEngine 测试
│       └── Fixtures/                # 测试用压缩包生成
├── docs/
│   ├── PLAN.md                     # 开发计划与进度
│   ├── CLI.md                      # 命令行使用指南
│   └── ARCHITECTURE.md             # 本文件
├── AGENTS.md                       # AI Agent 开发指南
└── MantisZip.sln
```

## 设计原则

| 原则 | 说明 |
|------|------|
| **Code-behind 模式** | 所有逻辑在 `MainWindow.xaml.cs`（及 partial class 文件）和 `App` 系列 partial class，不使用 MVVM |
| **策略模式** | `IArchiveEngine` 接口 + `ArchiveEngineFactory` 工厂按扩展名分发 |
| **依赖方向** | `MantisZip.UI` → `MantisZip.Core`，核心层无 UI 依赖 |
| **单例设置** | `AppSettings` 单例，JSON 持久化到 `%LOCALAPPDATA%\MantisZip` |
| **编码处理** | per-instance `StringCodec`，不依赖全局 `ZipStrings.CodePage`，自动适配系统区域 |

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
