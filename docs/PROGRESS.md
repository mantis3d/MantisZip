# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF + SharpZipLib + SevenZipExtractor

## 版本
- **当前版本**: 0.1.2
- **发布日期**: 2026-05-08

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

### 待实现功能
- 分卷压缩
- 压缩方式选择 (Store/Deflate/BZip2/LZMA) - 需换 SharpCompress 库
- Shell 右键菜单集成
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
│   │   │   ├── SevenZipEngine.cs # 7z/RAR 只读引擎
│   │   │   └── TarGzEngine.cs # TAR/GZ 引擎
│   │   ├── Models/
│   │   └── Utils/
│   │       ├── PasswordManager.cs # 密码管理
│   │       └── ArchiveEntryExtractor.cs # 单文件提取 (预览用)
│   └── MantisZip.UI/
│       ├── MantisZip.UI.csproj
│       ├── MainWindow.xaml        # 主窗口
│       ├── MainWindow.xaml.cs  # 主窗口逻辑 + FolderNode
│       ├── PasswordDialog.xaml  # 密码输入对话框
│       ├── PasswordDialog.xaml.cs
│       ├── PasswordEditDialog.xaml # 密码编辑对话框
│       ├── PasswordEditDialog.xaml.cs
│       └── PasswordManagerWindow.xaml # 密码管理窗口
└── docs/
    ├── PLAN.md                # 开发计划
    └── PROGRESS.md            # 本文档
```

### 核心类
| 类 | 位置 | 说明 |
|-----|------|------|
| IArchiveEngine | Abstractions | 压缩引擎接口 |
| ArchiveItem | Abstractions | 文件项模型 |
| ArchiveOptions | Abstractions | 压缩选项 |
| ArchiveFormat | Abstractions | 压缩格式枚举 |
| ZipEngine | Engines | ZIP 压缩/解压，GBK 编码支持 |
| SevenZipEngine | Engines | 7z/RAR 解压（只读）|
| TarGzEngine | Engines | TAR/GZ 压缩/解压 |
| PasswordManager | Utils | 密码管理工具 |
| ArchiveEntryExtractor | Utils | 单文件提取工具 (预览) |

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
