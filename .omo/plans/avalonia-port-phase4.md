# Avalonia 移植 Phase 4：去残留依赖 + 测试

> **分支**: `avalonia-port`（从 Phase 3 继续）
> **目标**: 清除 Avalonia UI 层残留的 Windows-only 依赖，使项目能在非 Windows 平台编译运行；补充 Avalonia 测试
> **约束**: ⚠️ 所有修改仅限于 `src/MantisZip.UI.Avalonia/` 目录及新增的测试项目。不修改 Core 层、WPF 项目或 CI 配置。
> **设计决策**:
>   - **读/写分离**：7z 读取换 SharpCompress 涉及 Core 层，本阶段不做，延后到 Core 层独立修改
>   - **图标系统**：放弃 Win32 SHGetFileInfo，改用内置 SVG/PNG 通用图标集 + MIME 类型映射（全部在 Avalonia 层完成）
>   - **System.Drawing.Common** 完全移除，GIF 解码换 SkiaSharp
>   - **IconProvider** 是纯 Avalonia 层替换，不影响 Core
>   - 测试项目引用 `MantisZip.UI.Avalonia` + `MantisZip.Core`，放在 `tests/` 下
> **不做的事情**:
>   - 不修改 `MantisZip.Core/` 任何文件（7z 引擎、LogRedactor、ArchiveEntryExtractor 等 Core 层改动延后）
>   - 不修改 CI workflow（`.github/workflows/`）
>   - 不做跨平台安装包
>   - 不修改 WPF 项目
> **创建日期**: 2026-06-16
> **状态**: 📋 计划 | **任务**: [⬜⬜⬜⬜⬜⬜]

---

## Task A1：替换 IconService（Win32 → 内置图标集）

> 核心问题：`Services/IconService.cs` 使用 `SHGetFileInfo`（shell32.dll P/Invoke）和 `System.Drawing.Icon.FromHandle()` 获取文件类型图标，非 Windows 平台崩溃。

**文件变更**（全部在 `src/MantisZip.UI.Avalonia/` 内）:
```
Services/IconService.cs    ← 重写：移除 Win32 P/Invoke
Resources/Icons/           ← NEW: 内置 SVG/PNG 通用图标集（~30 个）
Models/IconProvider.cs     ← NEW: 扩展名 → 图标映射
```

- [ ] **A1.1** 创建 `Resources/Icons/` 目录，内置一套通用文件类型图标（SVG 或嵌入式 PNG）：
  - 通用文件图标、文件夹图标、压缩包图标（zip/tar/7z/rar）
  - 按类别：文本（txt/csv/log）、图像（jpg/png/gif/webp/ico）、音频（mp3/wav/flac）、视频（mp4/mkv/avi）、文档（pdf/docx/xlsx/pptx）、代码（exe/dll/sys）、数据库（sqlite）、光盘镜像（iso）、种子（torrent）
  - 来源：从 Fluent UI System Icons、Material Design Icons 等开源图标集中提取 SVG
- [ ] **A1.2** 创建 `Models/IconProvider.cs`：`GetIcon(string extension) → Avalonia.Media.Imaging.Bitmap?`
  - 优先精确匹配扩展名，回退到类别匹配，最后回退到通用文件图标
  - 缓存已加载图标 `ConcurrentDictionary<string, Bitmap>`
  - 文件夹图标单独处理
- [ ] **A1.3** 重写 `Services/IconService.cs`：
  - 删除全部 `[DllImport]`（`shell32.dll`、`user32.dll`）
  - 删除 `System.Drawing` 引用
  - 移除 `[SupportedOSPlatform("windows")]`
  - 委托给 `IconProvider.GetIcon()`
  - 保持公开 API 不变（`GetFileIcon(string extension) → Bitmap?`）
- [ ] **A1.4** 验证：`dotnet build` 通过；文件列表能显示内置图标

---

## Task A2：移除 System.Drawing.Common

> 核心问题：csproj 引用了 `System.Drawing.Common`（.NET 9 Windows-only），用于 GIF 帧解码。

**文件变更**（全部在 `src/MantisZip.UI.Avalonia/` 内）:
```
MantisZip.UI.Avalonia.csproj     ← 移除 System.Drawing.Common 引用
ViewModels/PreviewViewModel.cs   ← 重写 GIF 解码（SkiaSharp 替代）
Services/GifDecoder.cs           ← NEW: SkiaSharp 基的 GIF 帧提取
```

- [ ] **A2.1** 创建 `Services/GifDecoder.cs`：
  - 用 SkiaSharp `SKCodec`（`SKCodec.Create(stream)`）解码 GIF
  - `GetFrameCount()`、`GetFrameTime(int index)`、`GetFrameBitmap(int index)`
  - 返回 `List<(SKBitmap bitmap, int delayMs)>`
  - 如果 SKCodec 不支持动画，回退为单帧静态图像
- [ ] **A2.2** 重写 `PreviewViewModel.cs` 中的 GIF 预览（373–406 行）：
  - 用新 `GifDecoder` 替代 `System.Drawing.Image.FromFile()` / `GetFrameCount()` / `SelectActiveFrame()`
  - 播放逻辑不变（Timer 驱动帧切换，工具栏播放/暂停/帧导航）
- [ ] **A2.3** 从 `MantisZip.UI.Avalonia.csproj` 移除 `<PackageReference Include="System.Drawing.Common" />`
- [ ] **A2.4** 验证：GIF 预览正常播放动画；`dotnet build` 无错误

---

### A1.5 文件列表图标：IconProvider 与 emoji 互补

> A1.2 的 `IconProvider` 处理文件类型图标（DataGrid 每行左侧）。**但这不涉及菜单/工具栏按钮的图标**——那些用 emoji 即可，无需额外图标文件。

- [ ] **A1.5.1** 确认 `IconProvider` 只负责 DataGrid 行级文件类型图标（.zip → 压缩包图标，.txt → 文本图标）
- [ ] **A1.5.2** 工具栏/菜单按钮的图标继续使用 emoji 字符（Avalonia 原生支持，跨平台）
  - 例如：新建 `🆕`、打开 `📂`、解压 `📤`、压缩 `📥`、筛选 `🔍`、预览 `👁`
  - emoji 用 `TextBlock Text="🆕"` 直接写在按钮 Content 或旁边
  - 不新增 NuGet 包（Avalonia SkiaSharp 已内置 emoji 字体）

---

## Task A3：全局样式统一 + emoji 图标补全

> 当前样式现状：只有颜色调色板，全局控件模板缺失，无圆角/阴影/间距规范。每个窗口各自重复声明 `Button:pressed`/`Button:pointerover` 等选择器。工具栏按钮只有文字没有 emoji 图标。

### A3.1 全局控件模板（写入 ThemeLight.axaml / ThemeDark.axaml）

- [ ] **A3.1.1** 提取全局 Button Style：
  - 圆角 4px，内边距 Padding 8,4，最小高度 28
  - 默认背景/悬停/按下颜色绑定 `ThemeButtonBgBrush` 系列
  - 删除各窗口内重复的 `Style Selector="Button:pressed"` 选择器
- [ ] **A3.1.2** 提取全局 TextBox Style：
  - 圆角 4px，内边距 8,4，焦点边框颜色 `ThemeAccentBrush`
  - Placeholder 文字颜色 `ThemeTextSecondaryBrush`
- [ ] **A3.1.3** 提取全局 ComboBox / ComboBoxItem Style
- [ ] **A3.1.4** 提取全局 CheckBox / RadioButton Style
- [ ] **A3.1.5** 提取全局 TabControl / TabItem Style
- [ ] **A3.1.6** 提取全局 DataGrid Style（列头背景、行选中色、交替行色）
- [ ] **A3.1.7** 提取全局 ProgressBar Style（进度条前景色、轨道色）
- [ ] **A3.1.8** 提取全局 ScrollBar Style（滑块圆角、轨道色）
- [ ] **A3.1.9** 提取全局 ToggleButton Style（选中态背景色区分）
- [ ] **A3.1.10** 提取全局 DatePicker / Slider Style

### A3.2 视觉细节统一

- [ ] **A3.2.1** 统一窗口间距：窗口 `Padding="16"`，对话框 `Padding="16"`，GroupBox/区域间距 12
- [ ] **A3.2.2** 统一标题字体：TabItem Header `FontSize="14" SemiBold`，区域标题 `FontSize="14" SemiBold`
- [ ] **A3.2.3** 统一分割线颜色 `ThemeBorderBrush` + 高度 1px
- [ ] **A3.2.4** 添加过渡动画：Button/ToggleButton 悬停 0.2s 过渡（`Transitions`）
- [ ] **A3.2.5** 对话框统一阴影（`BoxShadow`）

### A3.3 Dark 主题同步

- [ ] **A3.3.1** 确保 `ThemeDark.axaml` 包含与 Light 相同的完整控件模板（不仅仅是颜色）
- [ ] **A3.3.2** 调整暗色对比度：按钮背景/悬停/按下差异更明显，列表选中色更柔和

### A3.4 清理各窗口重复样式声明

- [ ] **A3.4.1** 从 `MainWindow.axaml` 删除内联的 Button/ToggleButton/TextBox/ComboBox/CheckBox/DatePicker Style 选择器
- [ ] **A3.4.2** 从 `CompressSettingsWindow.axaml` 删除内联 Style 选择器
- [ ] **A3.4.3** 从 `ExtractSettingsWindow.axaml` 删除内联 Style 选择器
- [ ] **A3.4.4** 从 `PasswordManagerWindow.axaml` 删除内联 Style 选择器（DataGrid 相关保留）
- [ ] **A3.4.5** 从 `ProgressWindow.axaml` 删除内联 Style 选择器
- [ ] **A3.4.6** 从 `AboutWindow.axaml` 删除内联 Style 选择器
- [ ] **A3.4.7** 从 `PasswordDialog.axaml` 删除内联 Style 选择器
- [ ] **A3.4.8** 从 `SettingsWindow.axaml` 删除内联 Style 选择器

### A3.5 工具栏/菜单 emoji 图标补全

- [ ] **A3.5.1** 工具栏按钮添加 emoji 图标（`StackPanel` 水平排列 emoji + 文字或 emoji 单独作为 Content）：
  - 新建 `🆕`、打开 `📂`、解压 `📤`、压缩 `📥`、筛选 `🔍`、预览 `👁`
- [ ] **A3.5.2** 菜单项添加 emoji 图标（`MenuItem.Icon`）：
  - 文件：新建 `🆕`、打开 `📂`、刷新 `🔄`、关闭 `🚪`、设置 `⚙`、退出 `❌`
  - 编辑：解压到… `📤`、解压到此处 `📍`、解压到命名 `📁`
  - 视图：切换主题 `🌓`
  - 帮助：关于 `ℹ`
- [ ] **A3.5.3** 状态栏/进度指示 emoji（可选）：
  - 密码匹配 `🔑`、正在加载 `⏳`、完成 `✅`、错误 `❌`

### A3.6 清理已废弃的内联 Button 样式

> 每个按钮上还挂了内联 `<Button.Styles><Style Selector="Button:pressed">...</Style></Button.Styles>`，全局模板建立后全部删除。

- [ ] **A3.6** 逐一确认每个 `.axaml` 文件中的内联 Button.Styles 被全局模板取代后删除

---

## Task A4：csproj/app.manifest 清理

> 低优先级清理项。

- [ ] **A4.1** `MantisZip.UI.Avalonia.csproj`：`<OutputType>` 保持 `WinExe`（Windows 上避免控制台窗口，跨平台无影响）
- [ ] **A4.2** 确认 `MantisZip.UI.Avalonia.csproj` 的 TFM 为 `net9.0`（非 `net9.0-windows`），已正确设置
- [ ] **A4.3** `app.manifest`：Windows-only 兼容性 GUID（`dpapi`、`supportedOS Id` 等）在非 Windows 平台被忽略，保留不变

---

## Task B1：Avalonia 测试项目

> 新建测试项目，测 ViewModel 逻辑 + Service 层，不依赖 UI 框架渲染。

- [ ] **B1.1** 新建测试项目 `tests/MantisZip.UI.Avalonia.Tests/`（`net9.0`，xUnit）
  - 引用 `MantisZip.UI.Avalonia` 和 `MantisZip.Core`
- [ ] **B1.2** ViewModel 层测试：
  - `CompressSettingsViewModel`：属性设置/命令/密码验证/注释分布
  - `ExtractSettingsViewModel`：路径/冲突策略/命令
  - `ProgressViewModel`：进度更新/取消/重置
  - `MainWindowViewModel`：筛选逻辑（`GetFilteredSource()`）、状态属性（`SelectionStats` / `ArchiveStats`）
  - `PreviewViewModel`：预览类型分类逻辑
- [ ] **B1.3** Service 层测试：
  - `IconProvider`：扩展名 → 图标映射键（不测实际 Bitmap 渲染，只测映射逻辑）
  - `GifDecoder`：帧计数和时间
- [ ] **B1.4** 运行 `dotnet test tests/MantisZip.UI.Avalonia.Tests/` 通过

---

## 验证清单

- [ ] `dotnet build src/MantisZip.UI.Avalonia` 0 errors（Windows）
- [ ] 文件列表显示内置图标，不崩溃
- [ ] GIF 预览正常播放
- [ ] `System.Drawing.Common` 不再被引用
- [ ] 全局控件模板在 Theme 文件中统一，各窗口无重复 `Style Selector`
- [ ] 工具栏/菜单按钮显示 emoji 图标
- [ ] 亮/暗主题切换后按钮/文本框等控件颜色正确
- [ ] 所有 A 类 Task 不修改 `MantisZip.Core/`、`MantisZip.UI/`、`.github/`
- [ ] 测试项目编译通过
- [ ] ViewModel 层测试全部通过

---

## 边界情况与注意事项

1. **7z 和 LogRedactor 延后处理**：本阶段不做。SharpSevenZip/7z.dll 在 Windows 上仍然能用（Avalonia 开发阶段主力在 Windows），7z 读取换 SharpCompress 和 LogRedactor POSIX 路径兼容需要修改 Core 层，留到后续独立分支进行。在此期间，Avalonia 版在 Linux/macOS 上仍然不能读取 7z 文件，也不支持 7z 压缩。

2. **内置图标集大小**：不要追求覆盖全部扩展名——核心覆盖 ~30 类即可（见 A1.1），未覆盖的用通用文件图标回退。

3. **GIF 帧动画性能**：SkiaSharp `SKCodec` 解码 GIF 帧比 System.Drawing 更快（GPU 加速），但大的 GIF（200+ 帧）可能内存占用大。保持现有 100ms 节流和帧缓存机制。

4. **`OutputType=WinExe`**：保持 `WinExe` 而不是改为 `Exe`。`WinExe` 在 .NET 中只是防止 Windows 上弹出控制台窗口的惯用法；在 Linux/macOS 上该设置被忽略，行为与 `Exe` 相同。

5. **`app.manifest`**：保留不变。Windows 上提供 DPI 感知支持；非 Windows 平台忽略该文件。

6. **`ArchiveService.cs` 的 `[SupportedOSPlatform("windows")]`**：原因是它引用了 `IconService.GetFileIcon()`。A1 修复 IconService 后，`ArchiveService.cs` 的 `[SupportedOSPlatform]` 属性可以移除——但这个文件在 `src/MantisZip.UI.Avalonia/Services/` 内，所以可以改。

7. **`ArchiveService.cs` 也在 Avalonia 目录内**：确认它在 `src/MantisZip.UI.Avalonia/Services/ArchiveService.cs`，所以 A1 完成后，可以顺手移除它的 `[SupportedOSPlatform("windows")]`。

8. **测试项目的 `net9.0` TFM**：不要使用 `net9.0-windows`，保持跨平台可运行。
