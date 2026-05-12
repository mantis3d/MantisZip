# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF + SharpZipLib + SevenZipExtractor

## 版本
- **当前版本**: 0.1.6
- **发布日期**: 2026-05-12
- **开发中**: 目录预览、预览开关按钮、图片解码降采样优化

## 功能列表

### v0.1.2 (2026-05-08)
1. **文件预览** - 选中文件后预览图片/文本内容
2. **预览信息面板** - 图片预览时右侧显示文件名、大小、压缩率等信息
3. **ArchiveEntryExtractor** - 单文件提取工具类 (Core)
4. **退出清理** - 程序退出时清理预览临时文件
5. **目录树绑定** - IsExpanded/IsSelected 改为 INotifyPropertyChanged 绑定
6. **智能目录树选择** - 双击进入子目录时不重建树，而是查找展开并选中已有节点
7. **多选支持** - 文件列表改为 Extended 选择模式，状态栏显示多选统计
8. **状态栏增强** - 添加目录统计、选中统计、压缩包概览
9. **预览行高 Star 支持** - 预览行高保存 GridLength 类型，支持 Star/Pixel 两种模式
10. **过滤保护** - 添加 _isProgrammaticFilter 防止编程切换目录触发预览

### v0.1.1 (2026-04-24)
1. **7z 压缩** - 基于 7z.exe
2. **压缩进度条** - 每 100ms 时间间隔更新
3. **取消功能** - 压缩/解压过程中可取消
4. **拖拽 Explorer 卡死修复** - 改用 Show() 非阻塞
5. **隐藏设置窗口** - 压缩时隐藏，完成后恢复
6. **关于页面** - 添加 7-Zip LGPL 许可证声明

### v0.1.0 (2026-04-22)
1. **ZIP 解压** - 基于 SharpZipLib，支持 GBK 编码
2. **ZIP 压缩** - 基于 SharpZipLib
3. **7z 解压** - 基于 SevenZipExtractor
4. **RAR 解压（只读）** - 基于 SevenZipExtractor
5. **TAR 压缩/解压** - 基于 SharpZipLib
6. **GZ 压缩/解压** - 基于 SharpZipLib
7. **TAR.GZ (.tgz) 压缩/解压** - 基于 SharpZipLib
8. **目录树导航** - 左侧面板显示压缩包内目录结构
9. **文件列表** - 右侧面板显示当前目录下的直接子项
10. **密码管理** - 支持 glob/regex 模式匹配的密码管理器
11. **密码输入对话框** - 下拉选择已保存的密码
12. **版本号显示** - 右下角状态栏显示
13. **拖拽解压** - 拖拽 ZIP 文件到窗口解压
14. **拖拽压缩** - 拖拽普通文件生成 ZIP

### v0.1.3 (2026-05-09)
1. **修复 `_currentFormat` bug** - 非 ZIP 格式预览改用扩展名映射，不再误判为 SevenZip

### 开发中 (即将发布为 v0.2.0)
1. **AppSettings 设置系统** - 持久化用户偏好 JSON（压缩/解压/菜单/预览/高级），`AppSettings` 单例
2. **SettingsWindow 设置窗口** - 五标签页 UI（压缩、解压、上下文菜单、预览、高级），Shell 状态检测 + 即时应用
3. **ShellIntegration 右键菜单** - HKCU 无管理员注册，层叠子菜单/独立动词双模式，per-verb 开关（压缩/快速压缩/打开/解压），AppliesTo 过滤器，shell32.dll 图标
4. **SystemIconHelper 系统图标** - SHGetFileInfo 获取 16x16 文件类型图标，ConcurrentDictionary 缓存，支持虚拟文件
5. **ProgressWindow 双进度条** - 文件级进度（顶部）+ 总体进度（底部），`SetProgress(ArchiveProgress)` 重载
6. **ArchiveProgress.FilePercentComplete** - Core 层新增 per-file 粒度字段
7. **ZipEngine per-file 进度** - ExtractAsync/CompressAsync 逐文件汇报 0%→100%，100ms 节流
8. **CLI 入口点** - `--compress`（多实例 IPC 合并路径）、`--compress-quick`（默认设置直接压缩）、`--extract`（绕过主窗口直连解压）、`--open`（主窗口浏览）、`--install-shell` / `--uninstall-shell`
9. **MainWindow 增强** - 预览设置感知（EnableImagePreview/EnableTextPreview/MaxTextPreviewBytes），异步图片解码 DecodePixelWidth=1920，提取后打开文件夹
10. **全局初始化 App.InitializeApp()** - 所有 CLI 入口点统一执行 GBK 编码注册
11. **ZipEngine 目录条目修复** - ExtractAsync 创建空目录条目而非跳过（修复点号目录名问题）

### v0.1.6 (2026-05-12)
1. **目录预览** - 选中文件夹时显示系统文件夹图标 + 目录信息
2. **工具栏预览开关** - ToggleButton 控制预览面板显隐，状态持久化 ShowPreviewPanel
3. **图片解码降采样优化** - 仅对宽度 >1920px 的图片设 DecodePixelWidth=1920，小图保持原生清晰度
4. **MaxWidth/MaxHeight 约束** - 设 PreviewImage.MaxWidth/MaxHeight 为实际像素尺寸，防止 Stretch="Uniform" 拉伸小图
5. **预览开关收起残留空白修复** - 收起时调用 HidePreview() 复位 Grid 行/列尺寸
6. **预览开关重显修复** - 打开时调用 ShowPreviewPanel() 恢复布局 + 重显选中项预览

### v0.1.5 (2026-05-11)
1. **HTML 预览** - WebBrowser 加载 .html/.htm 文件预览
2. **Markdown 预览** - Markdig 渲染 .md/.markdown 为带样式的 HTML
3. **文本预览字号** - AppSettings.TextPreviewFontSize + SettingsWindow 滑块 + 实时预览
4. **Shell 菜单重构** - 新增 --extract-here / --extract-to-name CLI；菜单项重命名排序：打开压缩包 / 解压到此处 / 解压到压缩包名 / 解压到…… / 压缩为（文件名）.zip / 压缩
5. **Shell 安装移至设置** - 工具菜单移除安装/卸载，改为设置窗口三按钮（安装/卸载/应用）
6. **拖拽提取增强** - ProgressWindow 全程展示 + 子目录结构保留 + _isOwnDrag 防自投
7. **分卷压缩** - CompressSettingsWindow 分卷大小 ComboBox（1MB~4GB+自定义）；7z 引擎传 -v{size}b；ZIP 引擎 SplitOutputStream 生成 .zip.001/.002/...

### 待实现功能
- 压缩方式选择 (Store/Deflate/BZip2/LZMA) - 需换 SharpCompress 库
- TarGzEngine 保留原始时间戳
- COM 右键菜单（动态文件名、目录结构预览、自定义排序）+ VirtualFileDataObject 延迟渲染（打包为一个 COM 辅助库）
- 右键菜单目录结构预览（Bandizip 风格，靠后）
- 安装包与发布

## 技术架构

### 项目结构
```
MantisZip/
├── MantisZip.sln
├── src/
│   ├── MantisZip.Core/
│   │   ├── MantisZip.Core.csproj
│   │   ├── Abstractions/
│   │   │   └── ArchiveEngine.cs     # IArchiveEngine 接口 + Models
│   │   ├── Engines/
│   │   │   ├── ZipEngine.cs      # ZIP 引擎
│   │   │   ├── SevenZipEngine.cs # 7z/RAR 解压 + 7z 压缩
│   │   │   └── TarGzEngine.cs # TAR/GZ 引擎
│   │   ├── Models/
│   │   └── Utils/
│   │       ├── PasswordManager.cs # 密码管理
│   │       └── ArchiveEntryExtractor.cs # 单文件提取 (预览用)
│   └── MantisZip.UI/
│       ├── MantisZip.UI.csproj
│       ├── App.xaml / App.xaml.cs   # 应用入口 + CLI 处理 + 全局初始化
│       ├── AppConstants.cs         # 版本号常量
│       ├── AppSettings.cs          # 用户设置（JSON 持久化）
│       ├── MainWindow.xaml / .cs   # 主窗口 + FolderNode
│       ├── SettingsWindow.xaml / .cs   # 设置窗口（五标签页）
│       ├── ShellIntegration.cs     # 右键菜单（HKCU 无管理员）
│       ├── SystemIconHelper.cs     # SHGetFileInfo 系统图标
│       ├── ProgressWindow.xaml / .cs   # 双进度条进度窗口
│       ├── CompressSettingsWindow.xaml / .cs # 压缩配置面板
│       ├── PasswordDialog.xaml / .cs
│       ├── PasswordEditDialog.xaml / .cs
│       └── PasswordManagerWindow.xaml / .cs
├── AGENTS.md                  # AI 代理开发指南
└── docs/
    ├── PLAN.md                # 开发计划
    └── PROGRESS.md            # 本文档
```

### 核心类
| 类 | 位置 | 说明 |
|-----|------|------|
| IArchiveEngine | Core/Abstractions | 压缩引擎接口 |
| ArchiveItem | Core/Abstractions | 文件项模型 |
| ArchiveOptions | Core/Abstractions | 压缩选项 |
| ArchiveProgress | Core/Abstractions | 进度报告（含 FilePercentComplete）|
| ArchiveFormat | Core/Abstractions | 压缩格式枚举 |
| ZipEngine | Core/Engines | ZIP 压缩/解压，GBK 编码，per-file 进度 |
| SevenZipEngine | Core/Engines | 7z/RAR 解压（SevenZipExtractor）；7z 压缩（7z.exe） |
| TarGzEngine | Core/Engines | TAR/GZ 压缩/解压 |
| PasswordManager | Core/Utils | 密码管理工具 |
| ArchiveEntryExtractor | Core/Utils | 单文件提取工具 (预览) |
| AppSettings | UI | 用户设置 JSON 持久化单例 |
| ShellIntegration | UI | 右键菜单注册/卸载 |
| SystemIconHelper | UI | SHGetFileInfo 系统图标缓存 |
| ProgressWindow | UI | 双进度条进度窗口 |

## 开发日志

### 2026-04-22
- 创建解决方案 MantisZip.sln
- 实现 ZIP 压缩/解压引擎
- 基础 UI 框架

### 2026-04-23
- 实现目录树 + 文件列表布局
- 实现密码管理器 glob/regex 匹配
- 实现密码输入对话框
- 实现拖拽解压

### 2026-04-24
- 添加版本号显示 v0.1.0
- 修复目录树重复图标问题 (XAML 移除重复 📁)
- 修复子目录自引用问题 (FullPath 去除末尾 /)
- 修复根目录过滤问题 (仅显示直接子项)
- 实现 TAR/GZ 格式支持
- 实现拖拽压缩功能
- **修复 ZIP 中文文件名乱码问题** - 注册 GBK 编码 + 设置 CodePage=936
- 整理 PLAN.md & PROGRESS.md

### 2026-04-24 (继续)
- **实现 7z 压缩** - 使用 7z.exe
- **修复压缩进度条不更新** - 改为每 100ms 异步报告
- **修复取消功能** - 添加 cancellationToken 支持
- **修复拖拽时 Explorer 卡死** - 改用 Show() 非阻塞
- **修复拖拽时卡死** - 隐藏设置窗口，完成后恢复
- **添加 7-Zip LGPL 许可证声明** 到关于页面
- 更新版本到 0.1.1

### 2026-05-08
- **实现文件预览** - 选中文件后预览图片/文本
- **ArchiveEntryExtractor** - 单文件提取工具类
- **退出清理** - 程序退出时清理预览临时文件
- 更新版本到 0.1.2

### 2026-05-09
- **预览信息面板** - 图片预览右侧显示文件名、大小、压缩率、修改日期
- **图片/文本预览分离** - 仅图片显示信息面板，文本/不支持类型自动隐藏
- **目录树重构** - FolderNode 实现 INotifyPropertyChanged，IsExpanded/IsSelected 绑定 TreeView
- **智能目录树选择** - 双击进入子目录时查找并展开已有节点，不再重建树
- **多选扩展** - 文件列表改为 Extended 模式，支持 Crtl/Shift 多选
- **状态栏增强** - 添加目录统计、选中统计（含文件数/目录数/总大小）、压缩包概览
- **预览行高持久化** - 支持 Pixel 和 Star 两种 GridLength 类型的保存/恢复
- **过滤保护** - 添加 _isProgrammaticFilter 开关，防止 FilterFiles 误触 SelectionChanged 预览

---

### v0.1.4 (2026-05-11)
1. **拖出到 Explorer 拖拽提取** - 7-Zip 急切提取模型：提取后拖拽
2. **ProgressWindow 拖拽集成** - 提取时显示进度 → 拖拽时显示"正在拖拽"提示
3. **_isOwnDrag 防自投** - 拖回自己窗口时忽略，不弹添加到压缩包
4. **子目录结构保留** - 使用 FullPath 保留目录层次
5. **自定义 IDataObject 实验（废弃）** - 确认 WPF OLE bridge bug，不可修复
6. **VirtualFileDataObject 列入未来计划** - COM 原生 IDataObject，可实现延迟渲染不崩溃
7. **AGENTS.md 拖拽交互详述** - 架构、关键决策、已知限制

### 2026-05-09 (开发中)
- **AppSettings 设置系统** - JSON 持久化单例，支持压缩/解压/菜单/预览/高级五组配置
- **SettingsWindow 设置窗口** - 五标签页 UI，Shell 状态检测 + 即时应用按钮
- **ShellIntegration 右键菜单** - 层叠/独立双模式，per-verb 开关，AppliesTo 过滤器，shell32.dll 图标
- **SystemIconHelper 系统图标** - SHGetFileInfo + ConcurrentDictionary 缓存
- **ProgressWindow 双进度条** - 文件级进度 + 总体进度，ArchiveProgress 重载
- **ArchiveProgress.FilePercentComplete** - Core 层新增 per-file 进度粒度
- **ZipEngine per-file 进度** - ExtractAsync/CompressAsync 逐文件 0%→100%，100ms 节流
- **CLI 入口点** - --compress（多实例 IPC）、--compress-quick、--extract（直连）、--open、--install/uninstall-shell
- **MainWindow 增强** - 预览设置感知、异步图片解码 DecodePixelWidth=1920、提取后打开文件夹
- **全局初始化 App.InitializeApp()** - 所有入口统一 GBK 编码注册
- **ZipEngine 目录条目** - ExtractAsync 创建空目录而非跳过
- **移除 bin/obj git 跟踪** - 清理已跟踪的构建产物

## 已知问题

| 问题 | 状态 |
|------|------|
| 7z 压缩已完成 | ✅ 已修复 |
| 压缩进度条不更新 | ✅ 已修复 |
| 拖拽 Explorer 卡死 | ✅ 已修复 |
| 编码乱码问题 | ✅ 已修复 |
| 拖拽时卡死 | ✅ 已修复 |
| 取消后报错 | ✅ 已修复 |
| 压缩方法选择 | 待 Phase 3 |
