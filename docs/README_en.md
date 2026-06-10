> 🌐 Language: [中文](../README.md) | [English](README_en.md)


# MantisZip
![Logo](/docs/images/Logo.png)

**Lightweight all-in-one Windows compression/decompression tool**

> Free & Open Source  
> Built on .NET 9 + WPF    
> 🤖 AI-assisted development by [OpenCode](https://opencode.ai) and [OhMyOpenCode](https://ohmyopencode.com)'s Sisyphus Agent

---

<p align="center">
  <b>📂 Open</b> &nbsp;·&nbsp; <b>📤 Extract</b> &nbsp;·&nbsp; <b>📥 Compress</b> &nbsp;·&nbsp; <b>👁 Preview</b> &nbsp;·&nbsp; <b>🔑 Password Manager</b> &nbsp;·&nbsp; <b>📎 Drag & Drop Export</b>
</p>

---

## 📚 Introduction

MantisZip is a free, open-source compression/decompression tool for Windows, featuring **in-archive preview** and **password manager** for enhanced convenience. You can directly view images, text, Markdown, and HTML files inside archives without extracting them first.

## ✨ Features

### In-Archive Preview
Preview **images**, **text**, **HTML/Markdown**, **SVG**, **fonts**, and more directly inside archives.

Some formats support **metadata display** (no full file loading required):

| Preview Type | Information Displayed |
|----------|----------|
| PE executable (exe/dll) | Company, product name, file version, architecture, subsystem, description |
| PDF document | Version, page count, title, author, encryption status |
| Office document (docx/xlsx/pptx) | Title, author, page/slide/worksheet count |
| Audio (WAV / FLAC) | Duration, sample rate, bit depth, channels, bitrate |
| Video (MP4 / MKV / AVI) | Resolution, duration, codec |
| Database (SQLite) | Encoding, page size, table count |
| Disc image (ISO) | Volume label, format, size |
| BitTorrent | InfoHash, file tree, Magnet link, Tracker, creator |

Future enhancement: detect file content by magic bytes rather than extension.

### Password Manager
Save frequently used passwords and auto-match them by rules.

If a correct password is entered for a file, you can choose to save it — no need to re-enter next time you open or extract. Passwords are encrypted with DPAPI.

Supports import/export (plain JSON) for backup and migration. Max 1000 entries; auto-try limited to first 100 entries to prevent brute-force abuse.

### Extraction Conflict Options
Beyond the usual "Overwrite", "Skip", and "Auto-rename", adds "Overwrite older files" and "Overwrite smaller files". "Auto-rename" can also seamlessly switch to manual rename.

### Debug Logging
When enabled, detailed operation logs are written to `debug.log` for troubleshooting.

---

## 🤔 Known Issues
- This software prioritizes features and usability, so performance may lag behind mainstream compression tools. Optimization will come in future releases.
- **Drag-and-drop export** uses 7-Zip's eager-extraction model (extracts all files to temp before initiating drag), causing delays with many large files. This feature is off by default and can be enabled in settings. A WPF OLE bridge bug prevents deferred rendering — may be rewritten with COM `VirtualFileDataObject` in the future.
- Markdown, HTML, SVG, and PDF preview use the WebView2 control, with all external network requests blocked (only `file://` local access allowed). WebView2 initializes on first run (auto-installs if the runtime is missing).
- Encrypted 7z/RAR archives do **not** support single-entry preview extraction (`ArchiveEntryExtractor` throws `NotSupportedException`).
- RAR format does not support compression (read-only extraction).

---

## 🖼️ Screenshots

> Coming soon

### In-Archive Preview

Images

Text

HTML

Markdown

Adjustable preview pane position

### Password Manager
Save frequently used password matching rules.

Auto-attempt password matching during extraction.

Password input

### Extraction Conflict

### Debug Logging

---

## 📦 Supported Formats

| Format | Compress | Extract | Encrypt |
|------|:----:|:----:|:----:|
| ZIP | ✅ | ✅ | ✅ AES-256 |
| 7z | ✅ | ✅ | ✅ |
| TAR | ✅ | ✅ | ❌ |
| GZ / TGZ | ✅ | ✅ | ❌ |
| RAR | ❌ | ✅ | ✅ |
| ISO | ❌ | ✅ (read-only) | ❌ |

---

## 📋 System Requirements

- **OS**: Windows 10 (1809+) / Windows 11
- **Runtime**: [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- **WebView2 Runtime**: HTML/Markdown/SVG/PDF preview requires [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (preinstalled on Win11; auto-installed on Win10 or distributed via Evergreen Bootstrapper)

---

## 🔧 Build

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

**Output**: `src/MantisZip.UI/bin/Debug/net9.0-windows/MantisZip.UI.exe`

---

## ⌨️ CLI

MantisZip supports powerful command-line invocation (e.g., for context menu integration).

```powershell
# 打开压缩包浏览
MantisZip.UI.exe --open "D:\文档.zip"

# 快速压缩（默认设置直接压缩）
MantisZip.UI.exe --compress-quick "D:\照片" -- "D:\备份.zip"
```

See the [CLI Guide](docs/CLI.md) for the full parameter list.

---

## 🏗 Architecture

See the [Architecture Document](docs/ARCHITECTURE.md) for details on module structure and technology stack.

---

## 📋 Roadmap

Currently in rapid iteration (v0.3.4). Supports multi-volume compression, password manager, in-archive preview, Shell context menu, and more.

For detailed near-term feature planning and history, see [Development Plan](docs/PLAN.md).

---

## 📦 Dependencies

### MantisZip.Core

| Package | Version | Purpose | License |
|------|------|------|--------|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 0.48.1 | Core ZIP/TAR/GZ engine (replaces SharpZipLib) | MIT |
| [SharpSevenZip](https://github.com/sevenzipsharp/SevenZipSharp) | 2.0.45 | 7z/RAR/ISO engine (wraps 7z.dll) | LGPL-2.1 |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | 1.4.2 | Legacy — minimal compatibility code | MIT |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | 10.0.8 | DPAPI-encrypted password storage | MIT |

### MantisZip.UI

| Package | Version | Purpose | License |
|------|------|------|--------|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MVVM utilities (partial base classes only) | MIT |
| [Markdig](https://github.com/xoofx/markdig) | 1.2.0 | Markdown → HTML rendering | BSD-2-Clause |
| [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) | 5.0.1 | Vista-style folder picker dialog | BSD-3-Clause |
| [Ude.NetStandard](https://github.com/jehugaleahsa/udetector) | 1.2.0 | Mozilla charset detection (text preview) | MIT |
| [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) | 2.0.2 | GIF animation support | MIT |
| [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) | 1.0.3967.48 | HTML/Markdown/SVG/PDF preview (replaces WPF WebBrowser) | BSD-3-Clause |

### External Tools (Runtime Dependencies)

| Tool | Purpose | License | Notes |
|------|--------|--------|------|
| [7z.dll](https://www.7-zip.org/) | Native 7z/RAR parsing (SharpSevenZip binding) | GNU LGPL | Distributed with app, dynamically linked |

---

## 🤝 Contributing

Issues and Pull Requests are welcome. See [AGENTS.md](AGENTS.md) for project conventions and notes.

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — ZIP/TAR/GZ engine (MIT)
- [SharpSevenZip](https://github.com/sevenzipsharp/SevenZipSharp) — 7z/RAR engine (LGPL-2.1)
- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) — Legacy ZIP/TAR/GZ engine (MIT)
- [Ude.NetStandard](https://github.com/jehugaleahsa/udetector) — Mozilla universal charset detection (MIT)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM toolkit (MIT)
- [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) — WPF folder picker dialog (BSD-3-Clause)
- [Markdig](https://github.com/xoofx/markdig) — Markdown rendering (BSD-2-Clause)
- [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) — WPF GIF animation support (MIT)
- [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) — WebView2 control for HTML/Markdown/SVG/PDF rendering (BSD-3-Clause)
- [7-Zip](https://www.7-zip.org/) — 7z compression engine (GNU LGPL)

---

## 💖 Support

If MantisZip helps you, feel free to buy me a coffee ☕

### Afdian
[Support on Afdian](https://afdian.com/a/MantisZen)
![Afdian QR Code](docs/images/afdian-MantisZen.jpg)(https://afdian.com/a/MantisZen)

### WeChat Donation
![WeChat QR Code](docs/images/afdian-MantisZen.jpg)

---
