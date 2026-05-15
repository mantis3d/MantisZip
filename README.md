# MantisZip

**轻量级全功能 Windows 压缩/解压软件** 

> 免费开源  
> 基于 .NET 9 + WPF    
> 🤖 由[OpenCode](https://opencode.ai) 及 [OhMyOpenCode](https://ohmyopencode.com) 的 Sisyphus Agent 辅助开发


---

<p align="center">
  <b>📂 打开</b> &nbsp;·&nbsp; <b>📤 解压</b> &nbsp;·&nbsp; <b>📥 压缩</b> &nbsp;·&nbsp; <b>👁 预览</b> &nbsp;·&nbsp; <b>🔑 密码</b> 
</p>

---
## 📚 简介


## ✨ 功能亮点

### 文件内预览
可以在压缩包内直接预览 图片、文本、HTML、Markdown 格式。

将来会加入更多支持的格式，目前已经开发计划中的有：
- 其它图像格式：tga、hdr、exr 等
- 其它文档格式：pdf、doc 等
- 视频与音频
- 字体文件：ttf 等
- 数据库：SQLite 等
- BT种子文件：torrent
对于不能预览的格式，可以展示一些信息，比如exe文件的图标和版本号，fbx文件的点数和面数等。


### 密码管理器
保存常用密码，可以根据规则自动尝试匹配密码。

如果一个文件输入过正确密码，可以选择保存记录，下次打开与解压则无需再次输入密码。

### 解压文件冲突选项
除了其他软件的“覆盖”“跳过”和“自动重命名”之外，还增加了“覆盖旧文件”和“覆盖小文件”。

### 调试日志


---

## 🤔 已知问题
- 本软件亮点是功能和易用性，所以性能上稍逊于主流压缩软件。将来会逐渐优化。
- 从压缩包内往外拖拽文件有时会有问题，谨慎使用。这个功能默认是关闭的，可以在设置里打开。将来这部分会完全重写。
- 7z压缩需要安装7-zip，解压不受影响，将来可能会把这部分完全重写。
- 预览 MarkDown 和 HTML 使用 WPF WebBrowser 控件，可能会触发系统试图联网。本软件目前没有需要联网的功能也不会试图联网。

---

## 🖼️ 功能截图

### 文件内预览

图片

文本

HTML

Markdown

可以更改预览窗格位置


### 密码管理器
保存常用密码匹配规则。

解压时自动尝试匹配密码。

密码输入

### 解压文件冲突


### 调试日志

---

## 📋 系统要求

- **操作系统**: Windows 10 (1809+) / Windows 11
- **运行时**: [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- **7z 压缩**: 需安装 [7-Zip](https://www.7-zip.org/)（默认路径 `C:\Program Files\7-Zip\7z.exe`，可在设置中修改）

---

## 🔧 构建方法

```powershell
# 克隆仓库
git clone https://github.com/yourusername/MantisZip.git
cd MantisZip

# 构建
dotnet build src\MantisZip.UI\MantisZip.UI.csproj

# 运行
dotnet run --project src\MantisZip.UI\MantisZip.UI.csproj
```

**输出路径**: `src/MantisZip.UI/bin/Debug/net9.0-windows/MantisZip.UI.exe`

---

## ⌨️ 命令行

| 参数 | 说明 |
|------|------|
| *(无参数)* | 正常启动主窗口 |
| `--open <路径>` | 启动主窗口并加载压缩包 |
| `--compress <路径1> <路径2> ...` | 显示压缩对话框（支持多实例 IPC 合并路径） |
| `--compress-quick <路径1> ...` | 使用默认设置直接压缩，显示进度窗口 |
| `--extract <路径>` | 直接解压到目标目录，显示进度窗口 |
| `--install-shell` | 安装右键菜单 |
| `--uninstall-shell` | 卸载右键菜单 |

**示例**:
```powershell
MantisZip.UI.exe --open "D:\文档.zip"
MantisZip.UI.exe --compress-quick "D:\照片" -- "D:\备份.zip"
MantisZip.UI.exe --extract "D:\软件包.7z"
```

---

## 📦 支持的格式 | Supported Formats

| 格式 | 压缩 | 解压 | 加密 |
|------|:----:|:----:|:----:|
| ZIP | ✅ | ✅ | ✅ AES-256 |
| 7z | ✅ * | ✅ | ✅ |
| TAR | ✅ | ✅ | ❌ |
| GZ / TGZ | ✅ | ✅ | ❌ |
| RAR | ❌ | ✅ | ✅ |

\* 7z 压缩依赖外部 `7z.exe`

---

## 🏗 项目架构 | Architecture

```
MantisZip/
├── src/
│   ├── MantisZip.Core/          # 核心业务逻辑
│   │   ├── Abstractions/        # IArchiveEngine 接口 + 数据模型
│   │   ├── Engines/             # ZipEngine / SevenZipEngine / TarGzEngine
│   │   └── Utils/               # PasswordManager / ArchiveEntryExtractor
│   └── MantisZip.UI/            # WPF 桌面应用
│       ├── MainWindow.xaml/.cs  # 主窗口（所有逻辑代码-behind）
│       ├── App.xaml/.cs         # 应用入口 + CLI 处理
│       ├── AppSettings.cs       # 用户设置（JSON 持久化）
│       ├── SettingsWindow       # 设置窗口
│       ├── ShellIntegration     # 右键菜单注册
│       ├── SystemIconHelper     # SHGetFileInfo 系统图标
│       ├── ProgressWindow       # 双进度条
│       └── CompressSettingsWindow # 压缩配置
├── docs/                        # 开发文档
├── AGENTS.md                    # AI Agent 开发指南
└── MantisZip.sln
```

**设计原则**:
- **Code-behind 模式**：所有逻辑在 MainWindow.xaml.cs，不使用 MVVM
- **策略模式**：`IArchiveEngine` 接口 + `ArchiveEngineFactory` 工厂
- **单例设置**：`AppSettings` 单例，JSON 持久化到 `%LOCALAPPDATA%\MantisZip`

---

## 💖 支持项目 | Support

如果 MantisZip 对你有帮助，欢迎请我喝杯咖啡 ☕  


<p align="center">
  <a href="https://afdian.com"><img src="https://img.shields.io/badge/爱发电-支持我-blue?style=for-the-badge" alt="爱发电"></a>
  <a href="https://ko-fi.com"><img src="https://img.shields.io/badge/Ko--fi-Buy%20me%20a%20coffee-orange?style=for-the-badge" alt="Ko-fi"></a>
  <a href="https://www.paypal.com"><img src="https://img.shields.io/badge/PayPal-Donate-blue?style=for-the-badge" alt="PayPal"></a>
</p>


---

## 🤝 贡献 | Contributing

欢迎提交 Issue 和 Pull Request。参见 `AGENTS.md` 了解项目约定和注意事项。

---

## 📄 许可证 | License

本项目使用 **MIT 许可证** — 详见 [LICENSE](LICENSE) 文件。  
This project is licensed under the 

**第三方组件 | Third-party components**:

- 7-Zip (GNU LGPL) — https://www.7-zip.org  
- 其他依赖见 [docs/PLAN.md](docs/PLAN.md)

---

## 🙏 致谢 | Acknowledgments

- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) — ZIP/TAR/GZ 引擎
- [SevenZipExtractor](https://github.com/adoconnection/SevenZipExtractor) — 7z/RAR 解压
- [Ude.NetStandard](https://github.com/jehugaleahsa/udetector) — Mozilla 字符编码检测移植
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 工具包
- [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) — 文件夹选择对话框
- [Markdig](https://github.com/xoofx/markdig) — Markdown 渲染
