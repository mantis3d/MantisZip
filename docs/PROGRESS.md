# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF + SharpZipLib + SevenZipExtractor

## 版本
- **当前版本**: 0.2.3
- **发布日期**: 2026-05-13 (updated)

## 版本历史（按日期排序）

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

### v0.1.1 (2026-04-24)
1. **7z 压缩** - 基于 7z.exe
2. **压缩进度条** - 每 100ms 时间间隔更新
3. **取消功能** - 压缩/解压过程中可取消
4. **拖拽 Explorer 卡死修复** - 改用 Show() 非阻塞
5. **隐藏设置窗口** - 压缩时隐藏，完成后恢复
6. **关于页面** - 添加 7-Zip LGPL 许可证声明

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

### v0.1.3 (2026-05-09)
1. **修复 `_currentFormat` bug** - 非 ZIP 格式预览改用扩展名映射，不再误判为 SevenZip
2. **AppSettings 设置系统** - 用户偏好 JSON 持久化（压缩/解压/菜单/预览/高级），`AppSettings` 单例
3. **SettingsWindow 设置窗口** - 五标签页 UI（压缩/解压/上下文菜单/预览/高级），Shell 状态检测 + 即时应用
4. **ShellIntegration 右键菜单** - HKCU 无管理员注册，层叠子菜单/独立动词双模式，per-verb 开关，AppliesTo 过滤器，shell32.dll 图标
5. **SystemIconHelper 系统图标** - SHGetFileInfo 获取 16x16 文件类型图标，ConcurrentDictionary 缓存，支持虚拟文件
6. **ProgressWindow 双进度条** - 文件级进度（顶部）+ 总体进度（底部），`SetProgress(ArchiveProgress)` 重载
7. **ArchiveProgress.FilePercentComplete** - Core 层新增 per-file 粒度字段
8. **ZipEngine per-file 进度** - ExtractAsync/CompressAsync 逐文件汇报 0%→100%，100ms 节流
9. **CLI 入口点** - `--compress`（多实例 IPC 合并路径）、`--compress-quick`、`--extract`、`--open`、`--install-shell` / `--uninstall-shell`
10. **MainWindow 增强** - 预览设置感知（EnableImagePreview/EnableTextPreview/MaxTextPreviewBytes），异步图片解码 DecodePixelWidth=1920
11. **全局初始化 App.InitializeApp()** - 所有入口统一执行 GBK 编码注册
12. **ZipEngine 目录条目修复** - ExtractAsync 创建空目录条目而非跳过

### v0.1.4 (2026-05-11)
1. **拖出到 Explorer 拖拽提取** - 7-Zip 急切提取模型：提取后拖拽
2. **ProgressWindow 拖拽集成** - 提取时显示进度 → 拖拽时显示"正在拖拽"提示
3. **_isOwnDrag 防自投** - 拖回自己窗口时忽略，不弹添加到压缩包
4. **子目录结构保留** - 使用 FullPath 保留目录层次
5. **自定义 IDataObject 实验（废弃）** - 确认 WPF OLE bridge bug，不可修复
6. **VirtualFileDataObject 列入未来计划** - COM 原生 IDataObject，可实现延迟渲染不崩溃

### v0.1.5 (2026-05-11)
1. **HTML 预览** - WebBrowser 加载 .html/.htm 文件预览
2. **Markdown 预览** - Markdig 渲染 .md/.markdown 为带样式的 HTML
3. **文本预览字号** - AppSettings.TextPreviewFontSize + SettingsWindow 滑块 + 实时预览
4. **Shell 菜单重构** - 新增 --extract-here / --extract-to-name CLI；菜单项重命名排序
5. **Shell 安装移至设置** - 工具菜单移除安装/卸载，改为设置窗口三按钮（安装/卸载/应用）
6. **拖拽提取增强** - ProgressWindow 全程展示 + 子目录结构保留 + _isOwnDrag 防自投
7. **分卷压缩** - CompressSettingsWindow 分卷大小 ComboBox；ZIP 引擎 SplitOutputStream；7z 引擎传 -v{size}b

### v0.1.6 (2026-05-12)
1. **目录预览** - 选中文件夹时显示系统文件夹图标 + 目录信息
2. **工具栏预览开关** - ToggleButton 控制预览面板显隐，状态持久化 ShowPreviewPanel
3. **图片解码降采样优化** - 仅对宽度 >1920px 的图片设 DecodePixelWidth=1920，小图保持原生清晰度
4. **MaxWidth/MaxHeight 约束** - 设 PreviewImage MaxWidth/MaxHeight 为实际像素，防止 Stretch="Uniform" 拉伸小图
5. **预览开关收起残留空白修复** - 收起时 HidePreview() 复位 Grid 行/列尺寸
6. **预览开关重显修复** - 打开时 ShowPreviewPanel() 恢复布局 + 重显选中项预览

### v0.2.0 (2026-05-12)
1. **MIT 许可证 + LICENSE 文件** - 项目切换为 MIT 开源
2. **OpenCode 声明 + 捐赠链接** - README 添加 Sisyphus Agent 致谢和捐赠按钮
3. **512x512 应用图标** - App.ico 嵌入 EXE，标题栏 + 任务栏 + 右键菜单全部使用自定义图标
4. **默认布局优化** - 预览面板默认右侧、信息面板默认纵向、窗口 1200×800、目录树默认 396px
5. **滚动条拖拽冲突修复** - FileListGrid 滚动条点击忽略拖拽检测
6. **压缩扫描进度** - ZipEngine/TarGzEngine EnumerateFiles + 100ms 进度报告，不再卡"正在准备..."
7. **Inno Setup 安装包** - 自动生成 MantisZip-0.2.0-Setup.exe 安装程序

### v0.2.1 (2026-05-12)
1. **加密 ZIP 解压密码提示修复** - ZipEngine `ListEntriesAsync` 设置 `IsEncrypted`；`ExtractAsync` 预检 `IsCrypted` 抛出中文异常
2. **密码管理器帮助窗口** - PasswordHelpDialog 讲解匹配规则 + 范例

### v0.2.3 (2026-05-13)
1. **ISO 格式支持** - SevenZipExtractor 底层 7z.dll 原生支持 ISO，加 `ArchiveFormat.Iso` 枚举 + 引擎注册即可，几乎零成本
2. **文件计数显示** - 进度窗口添加"文件 42/100"计数，从 `ArchiveProgress.TotalFiles`/`ProcessedFiles` 读取
3. **文件关联清理** - 移除不支持的 `.bz2`/`.cab` 扩展名注册；`.iso` 因已支持加回
4. **文档排序** - PROGRESS.md 版本历史按日期排序，`future.md` 合并入 PLAN.md 后删除
5. **预览格式扩展规划** - 在 PLAN.md 新增 RTF/CSV/SVG/EXE/PDF/XLSX/TTF/SQLite/嵌套 ZIP 等预览方案的难度评估
6. **文件冲突 Ask 弹窗** - 新增 ConflictDialog 自定义窗口，支持覆盖/重命名/跳过 + "应用到全部"
7. **暂停/继续** - ProgressWindow 添加暂停按钮，ManualResetEventSlim + PauseAwareProgress 包装器
8. **版本升级** - 0.2.3

### v0.2.2 (2026-05-13)
1. **SevenZipEngine 路径可配置** - 从 `private const` 改为 `static` 属性，启动时从 AppSettings 加载
2. **ZIP AES-256 加密检测** - `IsCrypted || AESKeySize > 0` 覆盖传统加密和 AES 加密
3. **7z 加密检测** - `entry.IsEncrypted` 替代硬编码 `false`
4. **QuickVerifyPassword** - 读 1 字节快速验证密码，不等完整解压
5. **密码区集成到进度条窗口** - 显示尝试/匹配状态、显示/隐藏密码、复制按钮，取代旧独立密码对话框
6. **自动匹配密码** - 打开压缩包时自动遍历已保存密码规则，匹配后预览/拖拽直接使用
7. **工具栏密码按钮** - 加密压缩包未匹配密码时可点击输入
8. **状态栏密码指示** - 显示 🔑 已匹配密码 / 🔒 需要密码
9. **密码对话框自动弹出** - 打开加密压缩包且无匹配密码时弹窗输入
10. **预览加载进度** - 预览窗格显示不确定进度条
11. **预览文件大小上限** - 设置中可配置，超过上限不预览
12. **FileConflictAction 实现** - 设置中覆盖/重命名/跳过/询问全部实现，询问支持"应用到全部"
13. **暂停/继续按钮** - 进度条窗口添加暂停功能，`ManualResetEventSlim` + `PauseAwareProgress` 包装器
14. **文件关联** - 注册 `.zip/.7z/.rar` 等格式的 ProgId + OpenWithProgids，设置页管理
15. **测试加密压缩包** - TestArchiveAsync 传递 `_currentPassword`，加密时先弹密码框
16. **隐式目录推导** - BuildFolderTree + FilterFiles 从文件路径推导目录节点，解决无显式目录条目的 ZIP 不显示子目录
17. **预览竞态修复** - 添加 `_previewCts` 取消令牌，切换文件时取消旧预览
18. **代码去重** - FormatSize、ResolveExtractDestination、OpenInExplorer 统一入口
19. **ShutdownMode 修复** - CLI 模式关进度条不自动退出，密码对话框能正常显示
20. **版本升级** - 0.2.2

## 待实现功能
- 压缩方式选择 (Store/Deflate/BZip2/LZMA) - 需换 SharpCompress 库
- COM 右键菜单（动态文件名、目录结构预览、自定义排序）+ VirtualFileDataObject 延迟渲染
- 右键菜单目录结构预览（Bandizip 风格）
- 中/英文界面切换
- 亮/暗主题切换

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
│   │   │   ├── ZipEngine.cs         # ZIP 引擎
│   │   │   ├── SevenZipEngine.cs    # 7z/RAR 解压 + 7z 压缩
│   │   │   └── TarGzEngine.cs       # TAR/GZ 引擎
│   │   ├── Models/
│   │   └── Utils/
│   │       ├── PasswordManager.cs   # 密码管理
│   │       ├── ArchiveEntryExtractor.cs  # 单文件提取 (预览用)
│   │       └── FileConflictHelper.cs     # 解压冲突处理
│   └── MantisZip.UI/
│       ├── MantisZip.UI.csproj
│       ├── App.xaml / App.xaml.cs   # 应用入口 + CLI 处理
│       ├── AppConstants.cs          # 版本号常量
│       ├── AppSettings.cs           # 用户设置（JSON 持久化）
│       ├── MainWindow.xaml / .cs    # 主窗口 + FolderNode
│       ├── SettingsWindow.xaml / .cs    # 设置窗口（六标签页）
│       ├── ShellIntegration.cs      # 右键菜单 + 文件关联
│       ├── SystemIconHelper.cs      # SHGetFileInfo 系统图标
│       ├── ProgressWindow.xaml / .cs    # 双进度条 + 密码区 + 暂停
│       ├── ConflictDialog.xaml / .cs    # 文件冲突对话框
│       ├── CompressSettingsWindow.xaml / .cs
│       ├── PasswordDialog.xaml / .cs
│       ├── PasswordEditDialog.xaml / .cs
│       └── PasswordManagerWindow.xaml / .cs
├── AGENTS.md
└── docs/
    ├── PLAN.md
    └── PROGRESS.md
```

### 核心类
| 类 | 位置 | 说明 |
|-----|------|------|
| IArchiveEngine | Core/Abstractions | 压缩引擎接口 |
| ArchiveItem | Core/Abstractions | 文件项模型 |
| ArchiveOptions | Core/Abstractions | 压缩选项 + ConflictAction |
| ArchiveProgress | Core/Abstractions | 进度报告 |
| FileConflictAction | Core/Abstractions | 解压冲突策略枚举 |
| ZipEngine | Core/Engines | ZIP 压缩/解压，GBK 编码，per-file 进度 |
| SevenZipEngine | Core/Engines | 7z/RAR 解压（SevenZipExtractor）；7z 压缩（7z.exe） |
| TarGzEngine | Core/Engines | TAR/GZ 压缩/解压 |
| PasswordManager | Core/Utils | 密码管理工具 |
| ArchiveEntryExtractor | Core/Utils | 单文件提取工具 (预览) |
| FileConflictHelper | Core/Utils | 解压冲突处理（Rename/Skip） |
| AppSettings | UI | 用户设置 JSON 持久化单例 |
| ShellIntegration | UI | 右键菜单 + 文件关联注册/卸载 |
| SystemIconHelper | UI | SHGetFileInfo 系统图标缓存 |
| ProgressWindow | UI | 双进度条 + 密码匹配区 + 暂停/继续 |
| ConflictDialog | UI | 文件冲突对话框（覆盖/重命名/跳过/应用到全部）|

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
- 修复目录树重复图标问题
- 修复子目录自引用问题
- 修复根目录过滤问题
- 实现 TAR/GZ 格式支持
- 实现拖拽压缩功能
- **修复 ZIP 中文文件名乱码问题** - GBK 编码 + CodePage=936
- 实现 7z 压缩（7z.exe）
- 修复压缩进度条不更新（100ms 异步报告）
- 修复取消功能
- 修复拖拽 Explorer 卡死
- 添加 7-Zip LGPL 许可证声明
- 更新版本到 0.1.1

### 2026-05-08
- **实现文件预览** - 选中文件后预览图片/文本
- **ArchiveEntryExtractor** - 单文件提取工具类
- **退出清理** - 程序退出时清理预览临时文件
- 更新版本到 0.1.2

### 2026-05-09
- **预览信息面板** - 图片预览右侧显示元数据
- **目录树重构** - FolderNode INotifyPropertyChanged
- **智能目录树选择** - 双击进入子目录查找已有节点
- **多选扩展** - Extended 模式 + 状态栏统计
- **预览行高持久化** - GridLength 类型 + Star 支持
- **_isProgrammaticFilter** - 防止 FilterFiles 误触预览
- AppSettings + SettingsWindow + ShellIntegration + CLI 入口
- SystemIconHelper + ProgressWindow 双进度条
- ZipEngine per-file 进度
- 修复 `_currentFormat` bug

### 2026-05-11
- **拖出到 Explorer 拖拽提取** - 7-Zip 急切提取模型
- ProgressWindow 拖拽集成 + _isOwnDrag
- 自定义 IDataObject 实验（废弃），WPF OLE bridge bug 确认
- HTML/Markdown 预览 + 文本预览字号
- Shell 菜单重构（--extract-here / --extract-to-name）
- 分卷压缩（ZIP SplitOutputStream + 7z -v）
- 更新文档 + VirtualFileDataObject 未来计划

### 2026-05-12
- **目录预览** - ShowDirectoryPreview
- **预览开关** - PreviewToggleBtn
- **图片解码降采样优化** - DecodePixelWidth=1920 条件设置
- **MIT 开源** - LICENSE 文件 + 捐赠链接
- **应用图标** - 512×512 自定义图标
- **默认布局优化** - 预览右侧、信息面板纵向
- **滚动条拖拽冲突修复** - ScrollBar 守卫
- **压缩扫描进度** - EnumerateFiles + 100ms
- **Inno Setup 安装包** - 自动构建 Setup.exe
- **加密 ZIP 密码提示修复** - IsCrypted 预检 + UI 关键词检测
- **密码管理器帮助窗口** - PasswordHelpDialog
- 更新版本到 0.2.0 / 0.2.1

### 2026-05-13（v0.2.2）
- SevenZipEngine 路径可配置（AppSettings 联动）
- ZIP AES 加密检测（IsCrypted + AESKeySize）
- QuickVerifyPassword 快速密码验证
- 密码区集成到 ProgressWindow（替代独立密码对话框）
- 打开时自动匹配密码 + 预览拖拽直接用
- 工具栏密码按钮 + 状态栏密码指示
- 文件冲突处理（Rename/Skip/Ask + 应用到全部）
- 暂停/继续按钮（ManualResetEventSlim）
- 文件关联（ProgId + OpenWithProgids + Applications）
- 隐式目录推导 + 预览竞态修复
- 代码去重 + 各种 bug 修复
- 更新版本到 0.2.2

## 已知问题

| 问题 | 状态 |
|------|------|
| 7z 压缩已完成 | ✅ 已修复 |
| 压缩进度条不更新 | ✅ 已修复 |
| 拖拽 Explorer 卡死 | ✅ 已修复 |
| 编码乱码问题 | ✅ 已修复 |
| 拖拽时卡死 | ✅ 已修复 |
| 取消后报错 | ✅ 已修复 |
| 滚动条拖拽冲突 | ✅ v0.2.0 已修复 |
| 大目录压缩无响应 | ✅ v0.2.0 已修复 |
| 加密 ZIP 解压无密码提示 | ✅ v0.2.1 已修复 |
| 子目录不显示（无显式目录条目的 ZIP） | ✅ v0.2.2 已修复 |
| 预览竞态条件（点 B 显示 A） | ✅ v0.2.2 已修复 |
| CLI 模式密码对话框不弹出 | ✅ v0.2.2 已修复 |
| 冲突处理未实现 | ✅ v0.2.2 已修复 |
| 压缩方法选择 | 待开发 |
| 界面国际化 | 待开发 |
