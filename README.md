> 🌐 Language: [中文](README.md) | [English](/docs/README_en.md)

# MantisZip
![Logo](docs/images/Logo.png)


**轻量级全功能 Windows 压缩/解压软件** 

> 免费开源  
> 基于 .NET 9 + WPF    
> 🤖 由 [OpenCode](https://opencode.ai) 及 [OhMyOpenCode](https://ohmyopencode.com) 的 Sisyphus Agent 辅助开发

---

<p align="center">
  <b>📂 打开</b> &nbsp;·&nbsp; <b>📤 解压</b> &nbsp;·&nbsp; <b>📥 压缩</b> &nbsp;·&nbsp; <b>👁 预览</b> &nbsp;·&nbsp; <b>🔑 密码管理器</b> &nbsp;·&nbsp; <b>📎 拖拽导出</b>
</p>

---

## 📚 简介

MantisZip 是一款面向 Windows 的免费开源压缩/解压工具，主打**文件内预览**和**密码管理器**等便捷功能。无需解压即可直接查看压缩包内的图片、文本、Markdown、HTML 文件内容。



## ✨ 功能亮点

### 文件内预览
可以在压缩包内直接预览 **图片**、**文本**、**HTML/Markdown**、**SVG**、**字体** 等内容。

部分格式支持**元数据展示**（无需加载完整文件）：

| 预览类型 | 展示信息 |
|----------|----------|
| PE 可执行文件（exe/dll） | 公司、产品名、文件版本、架构、子系统、描述 |
| PDF 文档 | 版本、页数、标题、作者、加密状态 |
| Office 文档（docx/xlsx/pptx） | 标题、作者、页数/幻灯片数/工作表数 |
| 音频（WAV / FLAC） | 时长、采样率、位深、声道、码率 |
| 视频（MP4 / MKV / AVI） | 分辨率、时长、编码 |
| 数据库（SQLite） | 编码、页面大小、表数量 |
| 光盘映像（ISO） | 卷标、格式、大小 |
| BT 种子 | InfoHash、文件树、Magnet 链接、Tracker、创建者 |

将来会增加，以内容而非扩展名判断文件内容进行预览。

### 密码管理器
保存常用密码，可以根据规则自动尝试匹配密码。

如果一个文件输入过正确密码，可以选择保存记录，下次打开与解压则无需再次输入密码。密码以 DPAPI 加密存储。

支持导入导出（明文 JSON），方便备份和迁移。密码库上限 1000 条，自动尝试密码仅前 100 条，防止暴力破解滥用。


### 解压文件冲突选项
除了其他软件的「覆盖」「跳过」和「自动重命名」之外，还增加了「覆盖旧文件」和「覆盖小文件」，「自动重命名」也可无缝切换至手动重命名。


### 调试日志
开启后记录详细操作日志到 `debug.log`，帮助排查问题。


---

## 🤔 已知问题
- 本软件亮点是功能和易用性，所以性能上稍逊于主流压缩软件。将来会逐渐优化。
- **拖拽导出**功能使用 7‑Zip 的 eager-extraction 模式（先全部解压到临时目录再发起拖拽），大文件较多时会有延迟。该功能默认关闭，可在设置里打开。WPF OLE 桥的 bug 导致延迟渲染方案不可行，未来考虑用 COM `VirtualFileDataObject` 重写。
- 预览 Markdown、HTML、SVG、PDF 使用 WebView2 控件，已拦截所有外部网络请求（仅允许 `file://` 本地访问）。初次运行时 WebView2 需初始化（若系统无 Runtime 则会自动引导安装）。
- 加密的 7z/RAR 压缩包**不支持**单项预览提取（`ArchiveEntryExtractor` 会抛出 `NotSupportedException`）。
- RAR 格式不支持压缩（只读解压）。

---

## 🖼️ 功能截图

> 待补充

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

## 📦 支持的格式 | Supported Formats

| 格式 | 压缩 | 解压 | 加密 |
|------|:----:|:----:|:----:|
| ZIP | ✅ | ✅ | ✅ AES-256 |
| 7z | ✅ | ✅ | ✅ |
| TAR | ✅ | ✅ | ❌ |
| GZ / TGZ | ✅ | ✅ | ❌ |
| RAR | ❌ | ✅ | ✅ |
| ISO | ❌ | ✅（只读浏览） | ❌ |

---

## 📋 系统要求

- **操作系统**: Windows 10 (1809+) / Windows 11
- **运行时**: [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- **WebView2 Runtime**: HTML/Markdown/SVG/PDF 预览依赖 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Win11 预装，Win10 自动安装或通过 Evergreen Bootstrapper 分发）

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

# 运行测试
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

**输出路径**: `src/MantisZip.UI/bin/Debug/net9.0-windows/MantisZip.UI.exe`

---

## ⌨️ 命令行

MantisZip 支持强大的命令行调用（例如右键菜单集成）。

```powershell
# 打开压缩包浏览
MantisZip.UI.exe --open "D:\文档.zip"

# 快速压缩（默认设置直接压缩）
MantisZip.UI.exe --compress-quick "D:\照片" -- "D:\备份.zip"
```

完整参数列表见 [命令行使用指南](docs/CLI.md)。

---

## 🏗 项目架构

关于项目的底层模块划分与技术栈架构设计，请参见 [项目架构文档](docs/ARCHITECTURE.md)。

---

## 📋 开发计划

当前项目处于快速迭代阶段（v0.3.4），已支持分卷压缩、密码管理器、文件内预览、Shell 右键菜单等功能。

详细的近期功能排期与历史进度请关注 [开发计划 (PLAN.md)](docs/PLAN.md)。

---

## 📦 第三方依赖 | Dependencies

### MantisZip.Core

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 0.48.1 | ZIP/TAR/GZ 压缩和解压核心引擎（替代 SharpZipLib）| MIT |
| [SharpSevenZip](https://github.com/sevenzipsharp/SevenZipSharp) | 2.0.45 | 7z/RAR/ISO 压缩和解压（封装 7z.dll）| LGPL-2.1 |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | 1.4.2 | 遗留 — 仅少量兼容代码 | MIT |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | 10.0.8 | DPAPI 加密存储密码 | MIT |

### MantisZip.UI

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MVVM 辅助（仅用部分基类） | MIT |
| [Markdig](https://github.com/xoofx/markdig) | 1.2.0 | Markdown → HTML 渲染 | BSD-2-Clause |
| [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) | 5.0.1 | Vista 风格文件夹选择对话框 | BSD-3-Clause |
| [Ude.NetStandard](https://github.com/jehugaleahsa/udetector) | 1.2.0 | Mozilla 字符编码检测（文本预览） | MIT |
| [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) | 2.0.2 | GIF 动画支持 | MIT |
| [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) | 1.0.3967.48 | HTML/Markdown/SVG/PDF 预览（替代 WPF WebBrowser）| BSD-3-Clause |

### 外部工具（运行时依赖）

| 工具 | 用途 | 许可证 | 备注 |
|------|------|--------|------|
| [7z.dll](https://www.7-zip.org/) | 7z/RAR 原生解析（SharpSevenZip 绑定） | GNU LGPL | 随应用分发，动态链接 |

---

## 🤝 贡献 | Contributing

欢迎提交 Issue 和 Pull Request。参见 [AGENTS.md](AGENTS.md) 了解项目约定和注意事项。

---

## 📄 许可证 | License

本项目使用 **MIT 许可证** — 详见 [LICENSE](LICENSE) 文件。  
This project is licensed under the MIT License.

---

## 🙏 致谢 | Acknowledgments

- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — ZIP/TAR/GZ 引擎（MIT）
- [SharpSevenZip](https://github.com/sevenzipsharp/SevenZipSharp) — 7z/RAR 引擎（LGPL-2.1）
- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) — 遗留 ZIP/TAR/GZ 引擎（MIT）
- [Ude.NetStandard](https://github.com/jehugaleahsa/udetector) — Mozilla 通用字符编码检测（MIT）
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 工具包（MIT）
- [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) — WPF 文件夹选择对话框（BSD-3-Clause）
- [Markdig](https://github.com/xoofx/markdig) — Markdown 渲染（BSD-2-Clause）
- [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) — WPF GIF 动画支持（MIT）
- [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) — WebView2 控件，用于 HTML/Markdown/SVG/PDF 内容渲染（BSD-3-Clause）
- [7-Zip](https://www.7-zip.org/) — 7z 压缩引擎（GNU LGPL）

---

## 💖 支持项目 | Support

如果 MantisZip 对你有帮助，欢迎请我喝杯咖啡 ☕  

### Polar
[在 Polar 上支持](https://buy.polar.sh/polar_cl_VaCaW2l2nWkob5CyHe4dOlhL6HrQDK4ueMA9n1JyhNc)

### 爱发电
[在爱发电上支持](https://afdian.com/a/MantisZen)
![爱发电](/docs/images/afdian-MantisZen.jpg)(https://afdian.com/a/MantisZen)

### 微信打赏
![微信打赏](/docs/images/afdian-MantisZen.jpg)


---
