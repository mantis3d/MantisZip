# 跨平台移植可行性研究

> 评估 MantisZip 从 Windows-only（WPF + COM ShellExt）迁移到跨平台架构（Linux/macOS/Windows）的可行性、主要障碍和实施路径。
> **前提**：ShellExt（COM 右键菜单）在非 Windows 平台直接砍掉；核心竞争力是「急速预览 + 密码管理」，不是 Explorer 集成。
> **状态**: 🔍 调研 | **任务**: [⬜⬜⬜⬜⬜⬜] (0/6)
> **创建日期**: 2026-06-11 | **更新日期**: 2026-06-11

## TL;DR

> **Quick Summary**: 如果把 ShellExt 砍掉（非 Windows 平台本来就不可能运行 COM 组件），只保留 Core + UI，**移植是可行的**。Core 层的预览解析器全部是纯托管代码，已经跨平台；真正的阻碍集中在：
>
> 1. **WPF → Avalonia**（21 个窗口，机械活儿，2-3 月）
> 2. **WebView2** → Avalonia WebView（API 完全不同，需重写预览宿主层）
> 3. **SharpSevenZip（7z.dll）** → SharpCompress 读 7z + 放弃或换 p7zip CLI 写 7z
> 4. **SystemIconHelper**（SHGetFileInfo）→ MIME 类型图标提供器
> 5. **DPAPI** → AES-GCM + 平台密钥链（2 天替换）
>
> **关键发现：80% 的 Core/ 已经跨平台。** 全部预览解析器（PE、PDF 元数据、SQLite、ISO、Torrent、Office、视频、音频、CSV、文本、字体解析）都是纯 BinaryReader 托管代码，零 Windows API 依赖。
>
> **预估**: 一个熟练的 Avalonia 开发者 2-3 月可完成。
>
> **Deliverables**:
> - 已识别的全部 Windows 依赖清单（按严重程度分级）
> - WPF → Avalonia 迁移策略
> - WebView2 替代方案
> - 7z 压缩在非 Windows 平台的策略
> - 渐进式迁移路线图
>
> **Estimated Effort**: 🟡中大 (2-3 月单人 / 1-2 月团队)

---

## 动机

### 场景

- 用户在 Linux（Ubuntu/Debian/Arch）或 macOS 上需要压缩/解压工具带快速预览和密码管理
- 现有跨平台压缩工具（File Roller、Ark、The Unarchiver）预览能力弱
- 开发者希望在 macOS/Linux 上本地开发和测试（CI 目前只跑 Windows）

### 核心价值定位

```
Windows Explorer 深度集成     ❌ 不是核心竞争力（砍掉 ShellExt 不影响核心功能）
         ↓
  急速预览（20+ 格式即时预览）  ✅ 核心竞争力
  密码管理（智能密码匹配库）    ✅ 核心竞争力
  多引擎支持（ZIP/7z/Tar/RAR） ✅ 基础功能
  批量处理（多压缩包并行）      ✅ 加分项
```

---

## 当前状态统计

```
MantisZip.Core     → net9.0              ✅ 已跨平台（pure managed .NET，除 DPAPI + 7z.dll）
MantisZip.UI       → net9.0-windows      🔴 WPF 绑定
MantisZip.ShellExt → net9.0-windows      🔴 COM 绑定 → 非 Windows 砍掉
MantisZip.Tests    → net9.0-windows      🔴 部分测试依赖注册表
```

---

## 预览系统逐格式分析

这是 MantisZip 最大的核心竞争力。好消息：**绝大多数预览逻辑已经是跨平台的**。

| 预览格式 | 底层 API | 跨平台？ | 说明 |
|---------|----------|---------|------|
| **图像** (jpg/png/webp/gif/ico) | `System.Windows.Media.Imaging.BitmapImage` | 🔴 WPF-only | 需换 Avalonia `Bitmap` + SkiaSharp 解码 |
| **GIF 动画** | `WpfAnimatedGif` + `BitmapDecoder` | 🔴 WPF-only | 需手动帧解码或 SkiaSharp |
| **文本** (txt/csv/log) | `Ude.NetStandard` + `DataTable` | 🟢 跨平台 | DataGrid 换 Avalonia 版 |
| **HTML/Markdown** | Markdig (🟢) + WebView2 (🔴) | 🟡 Markdig OK，WebView2 阻塞 | 需换 Avalonia WebView（WebKit） |
| **SVG** | WebView2 | 🔴 WebView2 | 同上，或 SkiaSharp SVG |
| **PE** (exe/dll) | BinaryReader 手动解析 | 🟢 **完全跨平台** | 纯托管代码 |
| **PDF 元数据** | BinaryReader + 正则 | 🟢 **完全跨平台** | 内容渲染依赖 WebView2 |
| **字体** (ttf/otf/woff) | BinaryReader 手动解析 (🟢) + WPF `FontFamily` (🔴) | 🟡 解析 OK，渲染需换 | Avalonia `FontManager` 替代 |
| **音频** (wav/flac/mp3) | BinaryReader 手动解析 | 🟢 **完全跨平台** | 封面图显示需换图像控件 |
| **SQLite** | `Microsoft.Data.Sqlite` | 🟢 **完全跨平台** | |
| **ISO** | BinaryReader 手动解析 | 🟢 **完全跨平台** | |
| **Torrent** | Bencode 手动解析 + SHA1 | 🟢 **完全跨平台** | |
| **Office** (docx/xlsx/pptx) | ZIP + XML 手动解析 | 🟢 **完全跨平台** | 没有用 OpenXML SDK |
| **视频** (mp4/mkv/avi) | BinaryReader 手动解析 | 🟢 **完全跨平台** | |

**汇总**：15 种预览格式中，**11 种已经是完全跨平台的纯托管代码**。只有图像/HTML/SVG/字体渲染受 WPF 束缚。

---

## 密码管理系统分析

### DPAPI 依赖深度：🟡 浅

```
PasswordManager.cs
  ├── Load()   → ProtectedData.Unprotect()   ← 仅 1 行
  ├── Save()   → ProtectedData.Protect()      ← 仅 1 行
  ├── CRUD 操作  → 全部在内存 JSON 对象上     ← 🟢 跨平台
  ├── 模式匹配   → 纯字符串/C# 代码            ← 🟢 跨平台
  └── 导入/导出  → 纯 JSON 序列化              ← 🟢 跨平台
```

**替换方案**：AES-256-GCM（.NET `System.Security.Cryptography.AesGcm`，跨平台）+ `IPlatformKeychain` 接口：
- Windows → 保留 DPAPI
- macOS → Keychain
- Linux → libsecret 或文件级加密

预估工作量：**2 天**。

---

## 引擎系统分析

| 引擎 | 当前依赖 | 跨平台？ | 替代方案 |
|------|---------|---------|---------|
| **ZipEngine** | SharpCompress | 🟢 **已跨平台** | 无需改动 |
| **TarGzEngine** | SharpCompress | 🟢 **已跨平台** | 无需改动 |
| **SevenZipEngine**（读/列出） | SharpSevenZip → 7z.dll | 🔴 Windows-only | SharpCompress 可读 7z 🟡 |
| **SevenZipEngine**（压缩/写） | SharpSevenZipCompressor | 🔴 **无托管替代** | Linux 上限用 p7zip CLI 或只支持 ZIP |
| **RarEngine**（读） | SharpSevenZip → 7z.dll | 🔴 Windows-only | SharpCompress 可读 RAR 🟡 |
| **ArchiveEntryExtractor**（单项提取预览） | SharpSevenZip + SharpZipLib | 🔴 7z 部分阻塞 | SharpCompress 替代 🟡 |

### 关键策略：读/写分离

```
非 Windows 平台：
  7z 读取/列出/提取 ✅ SharpCompress.SevenZipArchive（已存在引用）
  7z 压缩/写入      ❌ 没有纯托管 7z 写入器
    可选方案：
      A. 禁用 7z 压缩（只支持 ZIP + tar.gz）
      B. 通过 p7zip CLI 进程调用（需要用户安装 p7zip）
      C. 使用第三方 C# 7z 编码库（生态不成熟）
  RAR 读取          ✅ SharpCompress.RarArchive
  RAR 压缩          ❌ 不支持（Windows 上本来也不支持）
```

**推荐方案**：非 Windows 平台 7z 压缩降级为「仅支持 ZIP + tar.gz」，p7zip 作为可选的增强模式。在 UI 中标注「Linux 上 7z 压缩需要安装 p7zip」。

---

## 真正的阻塞清单（修正版）

### 🔴 关键阻塞（必须解决）

| # | 阻塞项 | 涉及规模 | 预估工作量 | 说明 |
|---|--------|---------|-----------|------|
| 1 | **WPF → Avalonia 迁移** | 21 XAML + 18 Window + 全部代码后置 | 2-3 月 | 最大工作量，但机械性强 |
| 2 | **WebView2 → Avalonia WebView** | `MainWindow.Preview.Web.cs` + 4 格式 | 1-2 周 | API 完全不同，需重写 |
| 3 | **SharpSevenZip（读）→ SharpCompress** | `SevenZipEngine.cs`, `ArchiveEntryExtractor.cs` | 1-2 周 | SharpCompress 已引用，测试验证 |
| 4 | **SharpSevenZip（写）→ 降级/CLI** | `SevenZipEngine.CompressAsync` | 1 周 | 策略决策 + 实现 |
| 5 | **SystemIconHelper → MIME 图标** | `SystemIconHelper.cs` + 多处调用 | 1 周 | 替换为按 MIME 类型返回内置图标 |

### 🟡 中等阻塞（可绕行）

| # | 阻塞项 | 工作量 | 说明 |
|---|--------|-------|------|
| 6 | **DPAPI → AES-GCM** | 2 天 | 深度浅，封装接口即可 |
| 7 | **GIF 动画** | 2-3 天 | 手动帧解码或 SkiaSharp |
| 8 | **Ookii.Dialogs** | 1 天 | Avalonia 原生对话框 |
| 9 | **Emoji 渲染** | 0.5 天 | Avalonia 原生支持 emoji |
| 10 | **图像解码** | 3-5 天 | WPF BitmapImage → Avalonia Bitmap + SkiaSharp |

### 🟢 无需担心

| # | 项 | 说明 |
|---|----|------|
| 11 | **Core 层（除 DPAPI + 7z）** | 全部纯托管，无需改动 |
| 12 | **拖拽支持** | Avalonia 有跨平台 DragDrop API |
| 13 | **命令式 CLI（--compress 等）** | 纯控制台，不依赖 UI |
| 14 | **路径正则（C:\）** | 小改即可 |
| 15 | **主题系统** | Avalonia 也有 ResourceDictionary + 主题切换 |

---

## 框架替代方案

| 框架 | WPF 兼容度 | Linux | macOS | 推荐？ | 理由 |
|------|-----------|-------|-------|--------|------|
| **Avalonia** | ⭐⭐⭐⭐⭐ | ✅ 优秀 | ✅ 优秀 | **✅ 推荐** | 同为 XAML，Window/Dispatcher/DataGrid 概念一致，社区活跃 |
| **MAUI** | ⭐⭐ | ⚠️ 有限 | ✅ | ❌ | 控件集不同，无 DataGrid，Linux 支持差 |
| **Uno Platform** | ⭐⭐ | ⚠️ 实验性 | ✅ | ❌ | WPF 兼容层有限 |

**Avalonia 关键匹配点**：

| WPF 概念 | Avalonia 对应 | 兼容度 |
|---------|--------------|--------|
| `Window` | `Window` | 几乎一致 |
| `UserControl` | `UserControl` | 几乎一致 |
| `Dispatcher` | `Dispatcher` | API 相同 |
| `DataGrid` | `DataGrid` | 几乎一致 |
| `TreeView` | `TreeView` | 类似 |
| `StackPanel`/`Grid`/`WrapPanel` | 同名 | 完全一致 |
| `DynamicResource` | `DynamicResource` | 同 |
| `ICommand` | `ICommand` | 同 |
| `INotifyPropertyChanged` | 同 | 同 |
| `Style` / `ControlTemplate` | 同 | 语法略有差异 |
| `BitmapImage` | `Bitmap` | API 不同，需重写 |
| `WebView2` | `WebView` | API 完全不同 |
| `Ookii.Dialogs` | 内置 `OpenFileDialog` | 不同类名 |
| `DragDrop` | `DragDrop` | API 不同但概念一致 |

---

## 框架升级带来的新预览能力

> 换 Avalonia 不只是一次「跨平台移植」—— SkiaSharp 渲染后端和跨平台 WebView 的能力甚至**超过 WPF**，让一些在 WPF 下「想做但太麻烦」的预览功能变得顺手。

### 🏆 最大提升：告别 WebView2 拐杖

WPF 版用 WebView2 做 SVG、Markdown→HTML、PDF 内容渲染的拐杖，带来 5 秒初始化延迟、100+ MB 内存开销和进程崩溃风险。Avalonia 下：

| 格式 | WPF 现状 | Avalonia 新方案 | 改善 |
|------|---------|----------------|------|
| **SVG** | WebView2 启动渲染 | Skia 原生渲染（`SkiaSharp.Extended.Svg`） | 瞬间显示，零 WebView2 开销 |
| **PDF 内容** | 元数据 + 小文件 WebView2 导航 | **PdfPig + Skia** 逐页光栅化 | 可翻页/缩放/生成缩略图 |
| **Markdown** | Markdig → HTML → WebView2 | 同链路但 WebKit 原生 WebView，启动更快 | 启动速度提升 |

### 🆕 完全新增的预览能力

| 能力 | 说明 | 技术基础 |
|------|------|---------|
| **Office 缩略图** | 以前只有标题/作者/页数文字，现在可画出 PPTX 第一页、XLSX 前几行表格、DOCX 格式化段落 | `DocumentFormat.OpenXml` + Skia `DrawText`/`DrawRectangle` |
| **音频波形图** | 以前只有采样率/位深等文字，现在可画出 PCM 波形 + FFT 频谱热力图 | WAV/FLAC PCM → Skia `DrawPoints` 填充分段折线 |
| **Hex Dump** | 以前无二进制预览，现可做一个控件：偏移列 + 十六进制 + ASCII 侧栏，颜色标记 | Skia `DrawingContext.DrawText`，GPU 加速不卡顿 |
| **实时图像滤镜** | 以前调亮度/对比度要写像素 Shader 或推像素缓冲区 | Skia 内置 `SKColorFilter.CreateColorMatrix()`，实时滑动滑块 |
| **动图格式扩展** | 以前只支持 GIF（第三方库），现在 WebP 动画/APNG/AVIF 动画原生支持 | `SKCodec` 统一解码，零额外依赖 |
| **文件列表缩略图列** | 以前不敢做（WPF 每张缩略图要完整解码） | Skia GPU 批量生成 PDF 首页/SVG/PPTX 封面缩略图 |

### 💡 平台原生预览集成

| 平台 | 新能力 |
|------|--------|
| **macOS** | 调用 `qlmanage -p` 或 QuickLook 框架 — Apple 原生预览 PDF/Office/图片/视频，零代码覆盖几十种格式 |
| **Linux** | 调用 GNOME Sushi（空格键预览），继承 GNOME 生态全部预览引擎 |
| **Windows** | 复用系统 Preview Handler 与 WPF 版一致 |

### 对比一览

```
         WPF版                             Avalonia版
  ┌────────────────────┐         ┌────────────────────────────┐
  │  ✅ 文本预览        │         │  ✅ 文本预览                │
  │  ✅ 图像预览        │         │  ✅ 图像预览 + 实时滤镜      │
  │  ⚠️ GIF 动画       │         │  ✅ GIF + WebP + APNG 动画  │
  │  ⚠️ WebView2 Svg   │         │  ✅ Skia Svg（瞬间渲染）    │
  │  ⚠️ WebView2 Pdf   │         │  ✅ PdfPig 逐页渲染（可翻页）│
  │  ⚠️ WebView2 Html  │         │  ✅ WebKit 原生 WebView     │
  │  ✅ 元数据预览(11种)│         │  ✅ 同上 + 新增：           │
  │  ❌ 波形图          │         │     🆕 Office 缩略图       │
  │  ❌ Hex Dump       │         │     🆕 音频波形图          │
  │  ❌ 文件列表缩略图  │         │     🆕 Hex Dump           │
  │                    │         │     🆕 macOS QuickLook    │
  └────────────────────┘         │     🆕 GNOME Sushi       │
                                 └────────────────────────────┘
```

**结论**：换 Avalonia 不只是「为了跨平台忍受妥协」，在预览能力上反而是**增强**的。Skia 的 GPU 渲染能力让音频波形、Office 缩略图、Hex Dump 这些 WPF 下『想做但太麻烦』的功能变得轻松可实现。

---

## 项目结构决策

### 对比：三种组织方式

| 维度 | ✅ 两个独立项目 | ❌ 条件编译（#if） | 🟡 多目标 TFM |
|------|---------------|-----------------|--------------|
| **项目布局** | `MantisZip.UI` + `MantisZip.UI.Avalonia` | 同一个项目塞两套 | 同一个项目两个 TFM |
| **XAML 隔离** | ✅ WPF 的 `.xaml` 和 Avalonia 的 `.axaml` 各放各的 | ❌ XAML 不能条件编译，冲突 | ❌ 同样不能同时放两套 XAML |
| **NuGet 包差异** | ✅ 各自 csproj 独立引用 | 🟡 条件 `<PackageReference />` | 🟡 按 TFM 条件引用 |
| **WPF 版不受影响** | ✅ 彻底不动，用户无感 | ❌ 改一行 csproj 可能炸掉 WPF 构建 | ❌ 同理 |
| **逐步交付** | ✅ 每完成一个窗口 Avalonia 版就多一个功能 | ❌ 必须一次性全迁完才能编译 | ❌ 同理 |
| **最终退役** | ✅ 删掉文件夹即可 | ❌ 需清理全部 WPF 残留 | ❌ 同理 |
| **新人友好度** | ✅ 结构清晰 | ❌ `#if` 满天飞 | 🟡 可接受 |

### 推荐：两个独立项目

```
src/
├── MantisZip.Core (net9.0)              ← 共享，不动它
├── MantisZip.UI (WPF, net9.0-windows)   ← 保持现状，继续修 bug 加功能
└── MantisZip.UI.Avalonia (net9.0)       ← 新建，逐步覆盖
```

**理由就一句话**：让 WPF 版安静地继续工作，新世界从零开始，互不干扰。

### 代码复用率

| 层 | 复用率 | 说明 |
|----|--------|------|
| **Core/**（引擎、解析器、模型） | 100% | 两个项目共享同一个类库，无需改动 |
| **UI C# 逻辑**（预览调用链、对话框数据流） | ~50% | 解析器在 Core 已共享，但图像显示、控件绑定需重写 |
| **XAML / 样式 / 主题** | 0% | 语法完全不同，必须重写 |

---

## Git 分支策略

### 分支结构

```
master (WPF 版主开发)
  ├── 修 Core 层 bug
  ├── 加新预览格式
  ├── 加新功能
  └── WPF UI 改动

avalonia-port (从 master 分出)
  ├── 继承 master 全部历史
  ├── 新目录：src/MantisZip.UI.Avalonia/
  ├── 偶尔修改：src/MantisZip.Core/（bug 修复，cherry-pick 回 master）
  └── 其余项目几乎不动
```

### 双向同步工作流

```
master 修了 SevenZipEngine 的一个 bug ──→ 定期 merge master ──→ avalonia-port
                                              ↓
                                     avalonia-port 拿到修复，Avalonia 版错误已解决

avalonia-port 在 Core 加了 HexDumpParser ──→ cherry-pick ──→ master
                                                              ↓
                                                     WPF 版也能用了
```

| 方向 | 方法 | 频率 | 冲突风险 |
|------|------|------|---------|
| `master → avalonia-port` | `git merge master`（常规合并） | **每周一次** | 🟢 低 — 只在 Core/ 文件有交集 |
| `avalonia-port → master` | `git cherry-pick` 逐个提交 | 需要时 | 🟢 低 — 通常只涉及 Core/ 新增文件 |

### 为什么冲突很少

- Avalonia 分支 99% 时间只动 `src/MantisZip.UI.Avalonia/` —— 这是一个全新目录，与 master 没有任何交集
- Core 层的改动方向一致（修 bug / 加新解析器），两边目标相同，不是相反方向
- 如果真的冲突，是同一个 bug 被两边同时修了，手动保留正确的一方即可

### 最佳实践

| ✅ 要做 | ❌ 不要做 |
|--------|---------|
| 每周 merge 一次 master → avalonia-port | 几个月不合并，积累大量冲突 |
| Core 层 bug 优先在 master 上修，再 merge 到 avalonia | 在 avalonia 分支上修 master 也有的 Core bug |
| Avalonia 独有改动用小提交，方便 cherry-pick | 把 WPF 的 UI 文件改来改去 |

### 最终回合

当 Avalonia 版功能完备后：

```
git checkout master
git merge avalonia-port

此时 master 拥有：
  ├── src/MantisZip.UI/           ← 旧的 WPF 版（可保留可删除）
  └── src/MantisZip.UI.Avalonia/  ← 新的跨平台版（已就位）
```

一次性 merge 的冲突量会比较大（积累久了），但都是机械性冲突 —— 两边改了同一行但目的相同的修复，选任一即可。不存在「Avalonia 改了行为而 WPF 没改」的逻辑冲突。

---

## 渐进式迁移路线图

### Phase 0: Windows 上先跑通 Avalonia 版（2 周）

**目标**：在 Windows 上 WPF 和 Avalonia 两个 UI 共存，验证 Avalonia 版能正常工作。

```
src/
├── MantisZip.UI (WPF)        ← 保持不动，主力 UI
└── MantisZip.UI.Avalonia     ← 新建项目，逐步迁移
```

- 新建 `MantisZip.UI.Avalonia` 项目（`net9.0`，不加 `-windows` TFM）
- 创建 MainWindow 基本骨架（文件列表 + 基本预览 + 解压按钮）
- 在 Windows 上并排运行，对照验证
- Phase 0 结束时：能浏览 ZIP 文件内容 + 预览文本/图像

### Phase 1: 预览系统移植（3-4 周）

**目标**：Avalonia 版支持全部 15 种预览格式。

- **Wave 1**（1 周）：文本/图像/CSV/PE 预览（最简单）
- **Wave 2**（1 周）：音频/SQLite/ISO/Torrent/Office/视频预览（纯数据解析，换 UI 即可）
- **Wave 3**（1 周）：HTML/Markdown/SVG/PDF 预览（重写 WebView 宿主层）
- **Wave 4**（1 周）：字体预览（换 Avalonia FontManager）+ GIF 动画

### Phase 2: 核心窗口移植（4-6 周）

**目标**：全部 21 个窗口在 Avalonia 上可用。

| 窗口 | 复杂度 | 优先级 |
|------|--------|--------|
| MainWindow（文件列表 + 预览 + 工具栏） | 🔴 最高 | P0 |
| CompressSettingsWindow | 🟡 中 | P1 |
| ExtractSettingsWindow | 🟡 中 | P1 |
| ProgressWindow | 🟢 低 | P1 |
| PasswordManagerWindow + 4 个密码对话框 | 🟡 中 | P2 |
| SettingsWindow | 🟡 中 | P2 |
| AboutWindow / DonationDialog / ErrorDialog 等 | 🟢 低 | P3 |

### Phase 3: 引擎适配（2 周）

**目标**：所有压缩/解压操作在非 Windows 平台可用。

- `SevenZipEngine` 读路径用 SharpCompress 实现（SharpCompress 已引用）
- `SevenZipEngine` 写路径：检测平台 → Windows 用 SharpSevenZip，Linux 降级提示或 p7zip
- `ArchiveEntryExtractor` 加 SharpCompress 7z/RAR 分支
- 添加条件编译 `#if WINDOWS` 隔离 native 代码

### Phase 4: 去残留依赖（1 周）

- DPAPI → AES-GCM 密钥封装（`IDataProtector` 接口）
- `SystemIconHelper` → MIME 图标提供器（内置一组 SVG/PNG 通用图标）
- `LogRedactor` 路径正则兼容 `/`
- 硬编码 `C:\Program Files\7-Zip\7z.dll` 改为可配置

### Phase 5: CI + 测试 + 发布（1-2 周）

- GitHub Actions 加 `ubuntu-latest` + `macos-latest` 矩阵
- 注册表测试加 `#if WINDOWS` 条件编译
- 跨平台安装包：Linux AppImage/Flatpak，macOS .dmg
- 文档更新

---

## 风险与限制

| 风险 | 程度 | 缓解措施 |
|------|------|---------|
| **Avalonia 控件行为差异** | 🟡 | Phase 0 先在 Windows 上并排验证 |
| **WebView2 → WebKit 渲染差异** | 🟡 | HTML/SVG 预览可能降级为纯文本 |
| **7z 压缩在 Linux 不可用** | 🟡 | 显示提示信息 + 推荐 p7zip |
| **密码库加密格式变更** | 🟡 | 迁移工具 + 首次运行自动转换 |
| **Avalonia 社区包质量** | 🟢 | 核心控件稳定，第三方包谨慎选择 |
| **性能差异** | 🟢 | Avalonia 使用 Skia 渲染，性能接近 WPF |
| **中文输入/显示** | 🟢 | Avalonia 对 CJK 支持良好 |

---

## 不做的事情

- **❌ 保留 ShellExt（COM 右键菜单）** — 仅在 Windows 上使用 WPF 版时保留
- **❌ 移动端支持** — Android/iOS 不在范围内
- **❌ WebAssembly** — 计算密集型 IO 任务不适合
- **❌ 容器/服务器场景** — GUI 没有意义
- **❌ 一次性全量迁移** — 渐进式，Phase 0→5 逐步交付

---

## Related

- [便携版模式](portable-mode.md) — 跨平台后路径重定向逻辑类似
- [预览魔数检测](preview-magic-detection.md) — 跨平台后扩展名不可靠，魔数检测更关键
- [引擎统一 (SharpCompress)](engine-unification-sharpcompress.md) — SharpCompress 已就位，7z 读取能力已具备
