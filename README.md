# MantisZip

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
可以在压缩包内直接预览 图片、文本、HTML、Markdown 格式。

将来会加入更多支持的格式，目前已经开发计划中的有：
- 其它图像格式：tga、hdr、exr 等
- 其它文档格式：pdf、doc 等
- 视频与音频
- 字体文件：ttf 等
- 数据库：SQLite 等
- BT种子文件：torrent

对于不能预览的格式，可以展示一些信息，比如 exe 文件的图标和版本号，fbx 文件的点数和面数等。

以及，以内容而非扩展名判断文件内容进行预览。

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
- 7z **压缩**依赖外部 `7z.exe`，解压不受影响。未来计划用 [SevenZipSharp](https://github.com/sevenzipsharp/SevenZipSharp) 或原生 7z DLL 绑定完全重写压缩部分。
- 预览 Markdown 和 HTML 使用 WPF `WebBrowser` 控件，可能会触发系统试图联网。本软件目前没有需要联网的功能也不会试图联网。
- 加密的 7z/RAR 压缩包**不支持**单项预览提取（`ArchiveEntryExtractor` 会抛出 `NotSupportedException`）。
- RAR 格式不支持压缩（只读解压）。

---

## 📋 开发计划

### 核心引擎 (Core Engine)

| 状态 | 功能 | 详情 |
|:----:|------|------|
| ✅ | ZIP 压缩/解压（AES-256 加密） | v0.1.0 |
| ✅ | 7z 压缩/解压 | v0.1.0 / v0.1.1，压缩依赖 7z.exe |
| ✅ | TAR/GZ (.tar.gz) 压缩/解压 | v0.1.0 |
| ✅ | RAR 解压（只读） | v0.1.0 |
| ✅ | ISO 解压（只读） | v0.2.3 |
| ✅ | 分卷压缩 | v0.1.5 |
| ✅ | 添加到压缩包 | v0.2.9 |
| ✅ | 从压缩包删除 | v0.2.9 |
| ☐ | **引擎统一迁移** → [详细设计](.sisyphus/plans/engine-unification-sharpcompress.md) | 用 SharpCompress 替换 SharpZipLib，SevenZipSharp 替换 7z.exe |



### 预览系统 (Preview System)

| 状态 | 功能 | 详情 |
|:----:|------|------|
| ✅ | 图片预览 — 自适应大图降采样，MaxWidth/Height 约束防止小图拉伸 | v0.1.2 / v0.1.6 |
| ✅ | 文本预览 — UTF-8 / GBK 自动检测，支持可调字号 | v0.1.2 |
| ✅ | Markdown 渲染（Markdig） HTML 渲染（WebBrowser） | v0.1.5 |
| ✅ | 预览信息面板 — 三列布局，原始大小 / 压缩后 / 压缩率 | v0.2.4 |
| ☐ | 文本预览语法高亮（AvalonEdit）→ [详情](docs/PLAN.md#近期p2) | |
| ☐ | **魔数识别 + 元数据展示** → [详细设计](.sisyphus/plans/preview-format-detection.md) | 按真实内容（非扩展名）判断格式，展示差异化信息 |
| ☐ | 预览格式扩展（PDF / SVG / CSV / EXE 元数据 等）→ [详情](docs/PLAN.md#预览格式扩展p2-p3) | 更多格式逐步支持 |

### 密码管理器 (Password Manager)

| 状态 | 功能 | 详情 |
|:----:|------|------|
| ✅ | 多规则密码匹配 — glob / regex 模式，自动遍历所有规则 | v0.1.0 / v0.2.2 |
| ✅ | 快速密码验证 — ZIP PVV 2字节校验，7z 构造 Entries 试探 | v0.2.2 |
| ✅ | 密码保存 — JSON 持久化 + 描述标签 + 匹配规则输入 | v0.1.0 |
| ✅ | 密码帮助窗口 — 匹配规则讲解 + 使用范例 | v0.2.1 |
| ✅ | 密码库导入导出 — 明文 JSON 备份/迁移，上限 1000 条 | v0.2.12 |
| ✅ | 防暴力破解 — 自动尝试仅前 100 条，添加/导入满千拦截 | v0.2.12 |
| ☐ | 加密存储（DPAPI 加密保存密码库） | |

### 用户界面 (UI)

| 状态 | 功能 | 详情 |
|:----:|------|------|
| ✅ | 目录树导航 + 文件列表（Extended 多选） | v0.1.0 |
| ✅ | 压缩配置面板（格式 / 级别 / 加密 / 分卷） | v0.1.2 |
| ✅ | 设置窗口（6 标签页：压缩 / 解压 / 菜单 / 预览 / 高级 / 关于）→ 菜单页按浏览/压缩/解压分组 | v0.1.3 / v0.2.10 |
| ✅ | 进度窗口（双进度条 + 暂停 / 继续 + 密码区） | v0.1.3 |
| ✅ | 文件冲突处理（覆盖 / 自动重命名 / 跳过 / 覆盖旧文件 / 覆盖小文件） | v0.2.4 |
| ✅ | 窗口状态记忆（大小 / 面板位置 / 列宽持久化） | v0.1.3 |
| ✅ | 调试日志开关 | v0.2.4 |
| ✅ | 文件列表右键菜单（解压到… / 复制路径 等） | v0.2.5 |
| ✅ | 暗色 / 亮色主题切换（主题色资源字典 + Appearance 设置页） | v0.2.9 |
| ☐ | **文件大小进度条** → [设计](.sisyphus/plans/file-size-progress-bar.md) | 大小列背景按文件体积比例填充 |
| ✅ | 国际化（中 / 英）→ [详情](docs/PLAN.md#近期p2) | v0.2.7 |

### 系统集成 (System Integration)

| 状态 | 功能 | 详情 |
|:----:|------|------|
| ✅ | Shell 右键菜单 — 动词模式 + 层叠子菜单双模式，per-verb 独立开关（打开/压缩/压缩到独立的/压缩到父目录/解压到此处/智能解压/解压到压缩包名/解压到…），菜单分组分隔线，AppliesTo 过滤器 | v0.1.3 / v0.2.10 |
| ✅ | 文件关联（打开方式） | v0.1.3 |
| ✅ | CLI 入口点（`--compress` / `--compress-separate` / `--compress-combined` / `--extract` / `--extract-here` / `--extract-smart` / `--extract-to-name` / `--open` / `--install-shell` / `--uninstall-shell`） | v0.1.3 / v0.2.10 |
| ✅ | 系统图标（SHGetFileInfo 16×16 文件类型图标，ConcurrentDictionary 缓存） | v0.1.3 |
| ✅ | 智能解压（Smart Extract）— ArchiveStructureAnalyzer 分析压缩包结构，自动决定是否保留顶层文件夹 | v0.2.10 |
| ✅ | 安装包（Inno Setup，自动生成 Setup.exe） | v0.2.0 |
| ☐ | COM 动态右键菜单（动态显示文件名、菜单排序）→ [详情](docs/PLAN.md#远期p3) | |
| ☐ | VirtualFileDataObject（COM 原生拖拽延迟渲染，替代 WPF OLE 桥）→ [详情](docs/PLAN.md#远期p3) | |

### 高级特性 (Advanced)

| 状态 | 功能 | 详情 |
|:----:|------|------|
| ✅ | 拖拽解压（拖入窗口） | v0.1.0 |
| ✅ | 拖拽压缩（拖入文件） | v0.1.0 |
| ✅ | 拖拽导出到 Explorer（急切提取模型，子目录结构保留） | v0.1.4 |
| ✅ | 智能编码检测（UTF-8 / GBK / Shift-JIS 等） | v0.1.3 |
| ✅ | 还原文件修改时间 | v0.2.4 |
| ✅ | 智能解压（Smart Extract）— 自动分析压缩包结构，判断是否保留顶层文件夹 | v0.2.10 |
| ☐ | **提取日志与解压「后悔药」** → [详细设计](.sisyphus/plans/extract-journal-undo.md) | 解压自动记录清单，一键回滚删除释放文件 |
| ☐ | **压缩包对比 (Archive Diff)** → [详细设计](.sisyphus/plans/archive-diff.md) | 选定两个压缩包，文件级差异对比 + 差异提取 |
| ☐ | **压缩预估 (Compression Estimator)** → [详细设计](.sisyphus/plans/compression-estimator.md) | 压缩前估算大小 / 耗时，帮助选择最佳参数 |
| ☐ | 右键菜单目录结构预览 → [详情](docs/PLAN.md#远期p3) | 参考 bandizip |
| ☐ | 外部工具视频元数据（ffprobe 时长 / 分辨率）→ [详情](docs/PLAN.md#远期p3) |不一定加 |

> ✅ 已完成 &nbsp;&nbsp; ☐ 规划中 &nbsp;&nbsp; 有独立设计文档的项目用粗体标出并附链接。详细技术方案见 [docs/PLAN.md](docs/PLAN.md)。

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

# 运行测试
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
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
| `--compress-separate <路径1> <路径2> ...` | 依次将每个选定项压缩到各自所在目录 |
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

## 📦 支持的格式 | Supported Formats

| 格式 | 压缩 | 解压 | 加密 |
|------|:----:|:----:|:----:|
| ZIP | ✅ | ✅ | ✅ AES-256 |
| 7z | ✅ * | ✅ | ✅ |
| TAR | ✅ | ✅ | ❌ |
| GZ / TGZ | ✅ | ✅ | ❌ |
| RAR | ❌ | ✅ | ✅ |
| ISO | ❌ | ✅（只读浏览） | ❌ |

\* 7z 压缩依赖外部 7z.exe（[7-Zip](https://www.7-zip.org/)）

---

## 🏗 项目架构 | Architecture

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
│       ├── App.xaml/.cs             # 应用入口 + CLI 处理 + --compress IPC
│       ├── AppSettings.cs           # 用户设置（JSON 持久化）
│       ├── SettingsWindow.xaml/.cs  # 设置窗口
│       ├── ShellIntegration.cs      # 右键菜单注册（HKCU 无管理员）
│       ├── SystemIconHelper.cs      # SHGetFileInfo 系统图标
│       ├── ProgressWindow.xaml/.cs  # 双进度条窗口
│       ├── CompressSettingsWindow   # 压缩配置对话框
│       ├── PasswordDialog           # 密码输入/管理对话框
│       ├── FileConflictHelper.cs    # 解压冲突处理
│       └── AppMessageBox.xaml/.cs   # 统一消息框
├── tests/
│   └── MantisZip.Tests/            # xUnit 单元测试（40+ 用例）
│       ├── Engines/                 # ZipEngine / SevenZipEngine / TarGzEngine 测试
│       └── Fixtures/                # 测试用压缩包生成
├── docs/
│   └── PLAN.md                     # 开发计划与进度
├── AGENTS.md                       # AI Agent 开发指南
└── MantisZip.sln
```

**设计原则**:
- **Code-behind 模式**：所有逻辑在 `MainWindow.xaml.cs`（及 partial class 文件），不使用 MVVM
- **策略模式**：`IArchiveEngine` 接口 + `ArchiveEngineFactory` 工厂按扩展名分发
- **依赖方向**：`MantisZip.UI` → `MantisZip.Core`，核心层无 UI 依赖
- **单例设置**：`AppSettings` 单例，JSON 持久化到 `%LOCALAPPDATA%\MantisZip`
- **编码处理**：per-instance `StringCodec`，不依赖全局 `ZipStrings.CodePage`，自动适配系统区域

---

## 📦 第三方依赖 | Dependencies

### MantisZip.Core

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | 1.4.2 | ZIP / TAR / GZ 压缩和解压核心引擎 | MIT |
| [SevenZipExtractor](https://github.com/adoconnection/SevenZipExtractor) | 1.0.19 | 7z / RAR / ISO 解压（封装 7z.dll） | LGPL-2.1 |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | 10.0.8 | DPAPI 加密存储密码 | MIT |

### MantisZip.UI

| 包名 | 版本 | 用途 | 许可证 |
|------|------|------|--------|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.3.2 | MVVM 辅助（仅用部分基类） | MIT |
| [Markdig](https://github.com/xoofx/markdig) | 1.1.3 | Markdown → HTML 渲染 | BSD-2-Clause |
| [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) | 5.0.1 | Vista 风格文件夹选择对话框 | BSD-3-Clause |
| [Ude.NetStandard](https://github.com/jehugaleahsa/udetector) | 1.2.0 | Mozilla 字符编码检测（文本预览） | MIT |
| [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) | 2.0.2 | GIF 动画支持 | MIT |

### 外部工具（运行时依赖）

| 工具 | 用途 | 许可证 | 备注 |
|------|------|--------|------|
| [7-Zip](https://www.7-zip.org/) | 7z 格式压缩（调用 7z.exe） | GNU LGPL | 解压不依赖；路径可在设置中修改 |

---

## 🏛 项目技术栈

| 层次 | 技术 |
|------|------|
| 语言 | C# 13 |
| 运行时 | .NET 9 |
| UI 框架 | WPF（Windows only）|
| 测试框架 | xUnit 2.9.2 + Microsoft.NET.Test.Sdk 17.12.0 |
| 构建 | dotnet CLI / Visual Studio 2022+ |
| 操作系统 | Windows 10 (1809+) / Windows 11 |

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

欢迎提交 Issue 和 Pull Request。参见 [AGENTS.md](AGENTS.md) 了解项目约定和注意事项。

---

## 📄 许可证 | License

本项目使用 **MIT 许可证** — 详见 [LICENSE](LICENSE) 文件。  
This project is licensed under the MIT License.

---

## 🙏 致谢 | Acknowledgments

- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) — ZIP/TAR/GZ 引擎（MIT）
- [SevenZipExtractor](https://github.com/adoconnection/SevenZipExtractor) — 7z/RAR 解压（LGPL-2.1）
- [Ude.NetStandard](https://github.com/jehugaleahsa/udetector) — Mozilla 通用字符编码检测（MIT）
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 工具包（MIT）
- [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) — WPF 文件夹选择对话框（BSD-3-Clause）
- [Markdig](https://github.com/xoofx/markdig) — Markdown 渲染（BSD-2-Clause）
- [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) — WPF GIF 动画支持（MIT）
- [7-Zip](https://www.7-zip.org/) — 7z 压缩引擎（GNU LGPL）
