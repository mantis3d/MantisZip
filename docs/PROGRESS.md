# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF + SharpZipLib + 7-Zip

## 版本
- **大版本**: 0.1
- **小版本**: 0.1.0
- **发布日期**: 2026-04-24

## 功能列表

### 已完成功能
1. **ZIP 压缩/解压** - 基于 SharpZipLib，支持 AES-256 加密
2. **目录树导航** - 左侧面板显示压缩包内目录结构
3. **文件列表** - 右侧面板显示当前目录下的直接子项
4. **密码管理** - 支持 glob/regex 模式匹配的密码管理器
5. **密码输入对话框** - 下拉选择已保存的密码
6. **版本号显示** - 右下角状态栏显示 v0.1.0

### 进行中功能
- 子目录过滤逻辑调试

### 待实现功能
- 7z 格式支持 (已完成引擎，未集成 UI)
- TAR, GZ, RAR 格式支持
- 解压功能
- 压缩功能

## 技术架构

### 项目结构
```
MantisZip/
├── MantisZip.sln
├── src/
│   ├── MantisZip.Core/
│   │   ├── MantisZip.Core.csproj
│   │   ├── Engines/
│   │   │   ├── ZipEngine.cs       # ZIP 引擎
│   │   │   └── SevenZipEngine.cs  # 7z 引擎
│   │   ├── Models/
│   │   │   ├── ArchiveItem.cs     # 文件项模型
│   │   │   └── FolderNode.cs      # 目录树节点
│   │   └── Utils/
│   │       └── PasswordManager.cs # 密码管理
│   └── MantisZip.UI/
│       ├── MantisZip.UI.csproj
│       ├── MainWindow.xaml        # 主窗口
│       ├── MainWindow.xaml.cs     # 主窗口逻辑
│       ├── PasswordDialog.xaml    # 密码输入对话框
│       ├── PasswordDialog.xaml.cs
│       └── PasswordManagerWindow.xaml # 密码管理窗口
```

### 核心类
- `ZipEngine` - ZIP 格式处理
- `SevenZipEngine` - 7z 格式处理
- `ArchiveItem` - 文件/目录项模型
- `FolderNode` - 目录树节点模型
- `PasswordManager` - 密码管理器

## 开发日志

### 2026-04-24
- 添加版本号显示 v0.1.0
- 修复目录树重复图标问题 (XAML 移除重复 📁)
- 修复子目录自引用问题 (FullPath 去除末尾 /)
- 修复根目录过滤问题 (仅显示直接子项)

### 2026-04-23
- 实现目录树 + 文件列表布局
- 实现密码管理器 glob/regex 匹配
- 实现密码输入对话框

### 2026-04-22
- 创建解决方案 MantisZip.sln
- 实现 ZIP 压缩/解压引擎
- 基础 UI 框架