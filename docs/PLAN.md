# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-07-22  
**当前版本**: 0.4.5

---


## 待实现设计方案

以下功能已有独立方案设计文档（`.sisyphus/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P0** | Avalonia: WPF 差异补齐总表 | [avalonia-wpf-diff-plan.md](.sisyphus/plans/avalonia-wpf-diff-plan.md) | 🟡中 | 1-2天 | Shell/COM 集成等各项已基本补齐，剩余少数差异项待确认 |
| **P1** | 统一路径快捷选择 (QuickPathControl → Avalonia) | [quickpath-unified.md](.sisyphus/plans/quickpath-unified.md) | 🟡中 | 4-6h | WPF 已完成数据层 + QuickPathControl 组件 + CompressSettingsWindow 嵌入；WPF 新增 QuickPathPreDialog 前置窗；Avalonia 阶段以 CustomFilePickerDialog + QuickPathBuddy 统一替换（更新设计：[quickpath-control-redesign.md](.sisyphus/plans/quickpath-control-redesign.md)）|
| **P1** | Win11 一级右键菜单 | [win11-first-level-menu.md](.sisyphus/plans/win11-first-level-menu.md) | 🔴高 | 1-2周 | IExplorerCommand 实现，HKLM 提权注册，双接口共存 |
| **P1** | 解压路径统一（前置：文件筛选） | [extract-path-unification.md](.sisyphus/plans/extract-path-unification.md) | 🟢低 | 1.5h | 三路解压合并为 `ExtractEntriesAsync` + `pathOverrides`，消除重复循环 |
| **P1** | 新增压缩格式（BZip2/XZ/CAB 等） | [new-format-support.md](.sisyphus/plans/new-format-support.md) | 🟡中 | 12-20h | 6 阶段渐进：TAR 裸格式/GZip 单文件 → BZip2 → XZ → CAB 只读 → UI 统一化 → Zstandard（需依赖） |
| **P1** | 自包含体积优化（Avalonia 迁移后） | [selfcontained-size-optimization.md](.sisyphus/plans/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 三步渐进：InvariantGlobalization → 保守修剪 → 激进修剪，目标降至 20–25 MB |
| **P1** | Avalonia 拖拽直接解压 | [drag-drop-direct-extract.md](.sisyphus/plans/drag-drop-direct-extract.md) | 🟡中 | 5-7h | 纯 Win32 独立线程覆盖层（三色状态机 + 呼吸动画）+ WindowFromPoint+ShellWindows 检测目标路径；#32770 用 Win32 EnumChildWindows（方案 A，方案 B UIA 为未来可选项）；☑️ 2026-07-23 计划审查完成，Avalonia 分支 API 已确认 |
| **P1** | HTML 预览升级：跨平台 WebView + 降级 | [html-preview-webview-fallback.md](.sisyphus/plans/html-preview-webview-fallback.md) | 🟡中 | 4-6h | 用 `Avalonia.Controls.WebView`（各平台原生引擎）替代当前 ReverseMarkdown 有损管线；WebView 不可用时自动降级到 ReverseMarkdown + 修 MarkdownPreviewBuilder table 支持；加工具栏和源码切换 |
| **P2** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.sisyphus/plans/compression-estimator.md) | 🟡中 | 4-5h | 压缩前估算大小/耗时 |
| **P2** | Winget 发布 | [winget-publishing.md](.sisyphus/plans/winget-publishing.md) | 🟢低 | 1-2h | 发布到 Windows Package Manager 社区仓库；首次手动提交后 CI 自动化 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.sisyphus/plans/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup → WiX MSI 迁移 |
| **P2** | RAR 压缩（外置 rar.exe） | [rar-compression.md](.sisyphus/plans/rar-compression.md) | 🟡中 | 8-10h | 通过已安装的 WinRAR 实现 RAR 压缩（含 SevenZipEngine 注册冲突处理） |
| **P2** | 快速预览与渐进式加载 | [preview-quick-modes.md](.sisyphus/plans/preview-quick-modes.md) | 🟡中 | ~27h | 三种模式（快速/渐进/完整），所有格式分段消费。WPF 先行，Avalonia 迁移时只改 UI 层 |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.sisyphus/plans/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到… |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.sisyphus/plans/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| **P2** | 进度窗口增强改造 | [progress-window-enhancement.md](.sisyphus/plans/progress-window-enhancement.md) | 🟡中 | 3-4h | 路径/文件名分离三行显示、文件级计数、实时统计栏、批处理每包摘要；计算逻辑抽到 Core 层 |
| **P2** | 压缩文件名后缀模板 | [filename-suffix-template.md](.sisyphus/plans/filename-suffix-template.md) | 🟢低 | 2-3h | `{date}`/`{datetime}`/`{seq}` 占位符替换，防同名覆盖 |
| **P2** | 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.sisyphus/plans/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API(通用) 两层提取嵌入缩略图；完成后可扩展文件列表缩略图模式 |
| **P2** | 字体预览连字效果开关 | [font-preview-ligature.md](.sisyphus/plans/font-preview-ligature.md) | 🟡中 | 3-4h | HarfBuzzSharp shaping + `liga` feature toggle，工具栏按钮 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.sisyphus/plans/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.sisyphus/plans/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 原生图标 DLL | [icon-dll.md](.sisyphus/plans/icon-dll.md) | 🟡中 | 2-3h | 将 7 个 .ico 编译进原生资源 DLL，消除路径依赖 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) | 🟡中 | 3-4h | 格式类库独立分发 |
| **P3** | 文件列表自定义列 | [custom-columns.md](.sisyphus/plans/custom-columns.md) | 🟡中 | 4-6h | 可自定义显示文件元数据列（文档标题、图片尺寸等） |
| **P3** | 冻结列（水平滚动时列固定） | [frozen-column.md](.sisyphus/plans/frozen-column.md) | 🟢低 | 1-2h | 右键列标题冻结/取消冻结，分隔线，设置持久化 |
| **P3** | Office 文档内容预览增强（Avalonia） | [office-content-preview-avalonia.md](.sisyphus/plans/office-content-preview-avalonia.md) | 🟡中 | 6-8h | ✅ 已完成一期（DOCX 纯文本大纲+全文、XLSX 表格、PPTX 文本）。后续：WebView 优先统一渲染管线（DOCX→Mammoth→HTML、Markdown→HTML）+ 纯文本降级 📋 + PPTX Canvas 定位预览 📋 |
| **P3** | 元数据信息面板可配置 | [metadata-panel-configurable.md](.sisyphus/plans/metadata-panel-configurable.md) | 🟡中 | 4-6h | 自定义每个格式在信息栏/内容区顶部的字段显示和同行布局 |
| **P3** | ICO 文件自身图标显示 | [ico-file-icon-extract.md](.sisyphus/plans/ico-file-icon-extract.md) | 🟢低 | 2-3h | ico 文件列表显示自身嵌入图标 |
| **P3** | 右键菜单目录结构预览 | [context-menu-tree-preview.md](.sisyphus/plans/context-menu-tree-preview.md) | 🔴高 | 6-8h | COM 菜单中展示压缩包顶层文件树 |
| **P1** | Avalonia: UI 功能补齐 | [avalonia-ui-feature-parity.md](.sisyphus/plans/avalonia-ui-feature-parity.md) | 🟡中 | 29/29 ✅ | Elevation×3、Favorites×2、QuickPath×2 等 11 个对话框、2 个控件、1 个转换器（2 项阻塞于 GUI 测试） |
| **P1** | 自动更新检测 | [auto-update.md](.sisyphus/plans/auto-update.md) | 🟡中 | 4-6h | GitHub Releases API 版本检查、AboutWindow 更新 Tab、UpdateAvailableDialog、设置开关、单元测试 |
| **P1** | 解压/压缩结果预览面板 | [result-preview-panel.md](.sisyphus/plans/result-preview-panel.md) | 🟡中 | 6-8h | ResultTreeView 控件已完成。待做：① Separate 模式按输出目录分组 + 压缩包壳节点（`IsArchive` + `IconArchive`）；② 解压多压缩包按来源目录分组；③ 压缩端模式/路径/过滤变更时刷新；④ 解压端目标路径/过滤变更时刷新；⑤ 两窗口的 FileFilterControl.FilterChanged → 重建预览树；⑥ i18n key |
| **P4** | 外部工具视频元数据 | — | 🟢低 | 2-3h | ffprobe 集成 |
| **🔍调研** | 跨平台移植可行性 | [cross-platform-port.md](.sisyphus/plans/cross-platform-port.md) | 🟡中大 | 2-3月 | 砍 ShellExt，WPF→Avalonia，WebView2→WebKit，SharpSevenZip→SharpCompress/p7zip，DPAPI→AES-GCM |
| **🔍调研** | Avalonia 预览机会分析 | [preview-avalonia-opportunities.md](.sisyphus/plans/preview-avalonia-opportunities.md) | 🟡中 | — | 分析 Avalonia 迁移对预览系统的影响：SVG/HDR/PSD/AI 新能力、音视频替代方案、HDR 全景 360° 查看器方案 |


---


## 已废弃方案

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |废弃原因 |
|--------|------|----------|:----:|:--------:|------|------|
| **P3** | VirtualFileDataObject | [virtual-file-data-object.md](.sisyphus/plans/virtual-file-data-object.md) | 🔴高 | 6-8h | COM 原生 IDataObject 替代 WPF OLE 桥 | 跨平台移植（Avalonia）后不再依赖 WPF OLE 桥，无 OLE CF_HDROP bug，VFDO 无存在必要 |
| **P2** | 文本预览语法高亮 | [text-preview-syntax-highlighting.md](.sisyphus/plans/text-preview-syntax-highlighting.md) | 🟡中 | 5-7h | AvalonEdit 替换 TextBox，支持 20+ 语言语法高亮 | AvalonEdit 是 WPF-only 控件，跨平台移植（Avalonia）后需使用 AvaloniaEdit 完全重写 |
| **P4** | 拖拽提取目标检测 | [drag-drop-marker-target.md](.sisyphus/plans/drag-drop-marker-target.md) | 🟡中 | 1-3h | Marker 文件探测拖放目标目录 | 被 [drag-drop-direct-extract.md](.sisyphus/plans/drag-drop-direct-extract.md) 取代——WindowFromPoint+ShellWindows 更直接可靠 |

---

## 跨平台移植影响分析

> 对 `docs/PLAN.md` 待实现全部 32 个计划进行的 WPF→Avalonia 兼容性评估。（2026-07-22）
>
> **注意**: 已完成方案（见 `docs/PROGRESS.md` 历史设计方案索引）不再列入本分析。已废弃方案仅作参考。

跨平台移植（WPF→Avalonia + 砍 ShellExt）对现有计划的影响分三类：

| 影响等级 | 数量 | 含义 |
|---------|:----:|------|
| 🟢 无影响 | 3 | Core 层纯 C# 逻辑，开箱即用 |
| 🟡 需调整 | 20 | Core 逻辑可复用，UI 层（XAML/控件）需移植到 Avalonia |
| 🔴 冲突 | 9 | 依赖 COM/注册表/Shell API/WPF 独占控件，需完全重写或平台替代方案 |

### 🟢 无影响（3 个）

`preview-modular-providers.md`、`selfcontained-size-optimization.md`、`winget-publishing.md`

### 🟡 需调整 — Core 可复用，UI 需移植（20 个）

`archive-diff.md`、`archive-rename-entry.md`、`auto-update.md`、`compress-preset.md`、`compression-estimator.md`、`custom-columns.md`、`extract-journal-undo.md`、`extract-path-unification.md`、`filename-suffix-template.md`、`font-preview-ligature.md`、`html-preview-webview-fallback.md`、`ico-file-icon-extract.md`、`metadata-panel-configurable.md`、`new-format-support.md`、`office-content-preview-avalonia.md`、`preview-quick-modes.md`、`progress-window-enhancement.md`、`avalonia-ui-feature-parity.md`、`avalonia-wpf-diff-plan.md`、`外部工具视频元数据（无计划文件）`

### 🔴 冲突 — 需完全重写或废弃（9 个）

`drag-drop-direct-extract.md`（Win32 Shell API）、`embedded-thumbnail-preview.md`（Shell 缩略图 API）、`frozen-column.md`（DataGrid 冻结列）、`icon-dll.md`（原生资源 DLL）、`msi-packaging-wix.md`（Windows Installer）、`quickpath-unified.md`（WPF UserControl 体系）、`rar-compression.md`（Windows 外置 rar.exe）、`win11-first-level-menu.md`（COM IExplorerCommand）、`context-menu-tree-preview.md`（COM HMENU）

### 关键发现

1. **🔴 的共性**：全部依赖 Windows Shell API（COM/注册表/P/Invoke/Shell32）或 WPF 独占控件/DataGrid 特定行为。跨平台后这些功能要么砍掉（COM 右键菜单、UAC 提权），要么需要 OS 级不同实现（Linux `.desktop` actions / macOS `NSExtension`）。
2. **🟡 的规律一致**：Core 层的解析器/算法/模型全是纯 C# 可复用，只有 UI 渲染层（WPF XAML + 控件）需要移植到 Avalonia 等价物。
3. **🟢 的 3 个计划**：基本是纯工具代码（正则、IO、字节操作）或发布配置，开箱即跨平台。
4. **最值得跨平台前实现的计划**：优先完成 🟢/🟡 计划（已有 `file-filter-feature.md` 等完成先例），积累跨平台经验后再攻坚 🔴 计划。
5. **Core 层遗留问题**：`PasswordManager`（DPAPI → 已实现 `AesGcmDataProtector` 抽象）和 `SevenZipEngine`（7z.dll Windows-only）已在跨平台准备中。

*此文档将随开发进度持续更新*


