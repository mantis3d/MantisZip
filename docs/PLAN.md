# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-08-03  
**当前版本**: 0.4.5

---


## 待实现设计方案

以下功能已有独立方案设计文档（`.sisyphus/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P0** | Avalonia: WPF 差异补齐总表 | [avalonia-wpf-diff-plan.md](.sisyphus/plans/avalonia-wpf-diff-plan.md) | 🟡中 | 1-2天 | Shell/COM 集成等各项已基本补齐，剩余少数差异项待确认 |
| **P1** | 统一路径快捷选择 (QuickPathControl → Avalonia) | [quickpath-unified.md](.sisyphus/plans/quickpath-unified.md) | 🟡中 | 2.5-3.5天 | WPF 已完成数据层 + QuickPathControl 组件；Avalonia 阶段重构为 Tab 式速选面板 + CustomFilePickerDialog（左 QuickPath + 右文件浏览）统一替换 5 处路径选择场景，宿主全弹窗调用（无内嵌），废弃 QuickPathPreDialog 过渡方案；2026-07-31 审查修正：4 个测试菜单对话框（QuickPathDialog/QuickPathPreDialog/ArchiveSaveAsDialog/UnifiedExtractDialog）全删 + 4 个僵尸委托清理；布局决策：解压模式内建 ResultTreeView 底部横铺（方案 1，实时冲突检测），替代 ExtraContent 注入（更新设计：[quickpath-control-redesign.md](.sisyphus/plans/quickpath-control-redesign.md)）|
| **P1** | Win11 一级右键菜单 | [win11-first-level-menu.md](.sisyphus/plans/win11-first-level-menu.md) | 🔴高 | 1-2周 | IExplorerCommand 实现，HKLM 提权注册，双接口共存 |
| **P1** | 拖拽/右键解压流程统一 | [drag-extract-unify.md](.sisyphus/plans/drag-extract-unify.md) | 🟡中 | 2-3h | 新建 `SelectedItemsExtractService` 统一两条解压流程（差异仅剩获取输出路径）；拖拽路径语义改与右键一致（`ExtractPreserveFullPath`+裁剪当前浏览层）；`TarGzEngine` 实现按条目提取（推翻原「降级全量」决策）；冲突统一走设置 6 策略 + 统一 Ask 弹窗；拖拽进度统一模态；修 `MapConflictActionString` 连字符映射漏洞 |
| **P1** | 新增压缩格式（BZip2/XZ/CAB 等） | [new-format-support.md](.sisyphus/plans/new-format-support.md) | 🟡中 | 12-20h | 6 阶段渐进：TAR 裸格式/GZip 单文件 → BZip2 → XZ → CAB 只读 → UI 统一化 → Zstandard（需依赖） |
| **P1** | 自包含体积优化（Avalonia 迁移后） | [selfcontained-size-optimization.md](.sisyphus/plans/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 三步渐进：InvariantGlobalization → 保守修剪 → 激进修剪，目标降至 20–25 MB |
| **P1** | Avalonia 拖拽直接解压 | [drag-drop-direct-extract.md](.sisyphus/plans/drag-drop-direct-extract.md) | 🟡中 | 5-7h | 纯 Win32 独立线程覆盖层（三色状态机 + 呼吸动画）+ WindowFromPoint+ShellWindows 检测目标路径；#32770 用 Win32 EnumChildWindows（方案 A，方案 B UIA 为未来可选项）；☑️ 2026-07-23 计划审查完成，Avalonia 分支 API 已确认；☑️ 2026-07-31 高危修复（Esc 取消/ask 冲突/DebugLog）+ 光标临时方案 A（SetSystemCursor）；方案 C（OLE 虚拟文件拖拽，根治光标）已列入未来可选项；☑️ 2026-08-06 预览弹窗实施补充（独立 Win32 弹窗跟随鼠标 460×680 + PointerPressed 预取式渲染 + 双阈值降级，待实施，详见计划文末章节） |
| **P1** | HTML 预览升级：跨平台 WebView + 降级 | [html-preview-webview-fallback.md](.sisyphus/plans/html-preview-webview-fallback.md) | 🟡中 | 4-6h | 用 `Avalonia.Controls.WebView`（各平台原生引擎）替代当前 ReverseMarkdown 有损管线；WebView 不可用时自动降级到 ReverseMarkdown + 修 MarkdownPreviewBuilder table 支持；加工具栏和源码切换 |
| **P1** | QuickPathPicker 自包含路径速选控件 | [2026-08-03-quickpath-picker-design.md](docs/superpowers/specs/2026-08-03-quickpath-picker-design.md) | 🟢低 | 2-4h | 把 CompressSettings/ExtractSettings/Settings 三处重复的「路径输入框 + ⭐🕐🪟📁 + 三个单 Tab 浮层 + 手写 light-dismiss」抽成自包含可复用控件；输入框用 AutoCompleteBox（复用 CustomFilePicker 补全逻辑），浏览差异经注入委托（SaveFile/ExtractFolder/纯目录）解决，文件路径自动归一化为父目录；后续再有路径速选场景一行集成 |
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
| **P2** | 文件选择器多选（文件+目录） | [file-picker-multi-select.md](.sisyphus/plans/file-picker-multi-select.md) | 🟡中 | 4-6h | CustomFilePickerDialog 新增 PickItems 模式：勾选框累积 + 跨目录保留 + 已选项目区；CompressSettingsWindow 合并为「添加文件/文件夹」单按钮（2026-07-31 决策：勾选框方案，根除单击累积导致双击目录误入列表的冲突） |
| **P2** | 目录行聚合显示（大小=子树和 / 日期=最新文件 / 压缩后大小按格式可用性） | [directory-size-date-aggregate.md](.sisyphus/plans/directory-size-date-aggregate.md) | 🟢低 | 3-5h | Core `DirStats`+`ComputeDirectoryStats` 增加 `NewestModified`（共享契约，WPF 维护模式不动）；Avalonia `ArchiveItemModel` 显示属性改派生计算属性 + 新增 `CompressedSizeAvailable`（7z/RAR/.tgz/.gz 压缩后大小列显示空，文件/目录一致，对齐 WPF `CompressedDisplayMode.Unavailable`）；`PopulateEntries` 基于过滤后 `filteredSource` 应用聚合 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.sisyphus/plans/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 原生图标 DLL | [icon-dll.md](.sisyphus/plans/icon-dll.md) | 🟡中 | 2-3h | 将 7 个 .ico 编译进原生资源 DLL，消除路径依赖 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) | 🟡中 | 3-4h | 格式类库独立分发 |
| **P3** | 文件列表自定义列 | [custom-columns.md](.sisyphus/plans/custom-columns.md) | 🟡中 | 4-6h | 可自定义显示文件元数据列（文档标题、图片尺寸等） |
| **P3** | 冻结列（水平滚动时列固定） | [frozen-column.md](.sisyphus/plans/frozen-column.md) | 🟢低 | 1-2h | 右键列标题冻结/取消冻结，分隔线，设置持久化 |
| **P3** | Office 文档内容预览增强（Avalonia） | [office-content-preview-avalonia.md](.sisyphus/plans/office-content-preview-avalonia.md) | 🟡中 | 6-8h | ✅ 已完成一期（DOCX 纯文本大纲+全文、XLSX 表格、PPTX 文本）。后续：WebView 优先统一渲染管线（DOCX→Mammoth→HTML、Markdown→HTML）+ 纯文本降级 📋 + PPTX Canvas 定位预览 📋 |
| **P3** | ICO 文件自身图标显示 | [ico-file-icon-extract.md](.sisyphus/plans/ico-file-icon-extract.md) | 🟢低 | 2-3h | ico 文件列表显示自身嵌入图标 |
| **P3** | 右键菜单目录结构预览 | [context-menu-tree-preview.md](.sisyphus/plans/context-menu-tree-preview.md) | 🔴高 | 6-8h | COM 菜单中展示压缩包顶层文件树 |
| **P1** | Avalonia: UI 功能补齐 | [avalonia-ui-feature-parity.md](.sisyphus/plans/avalonia-ui-feature-parity.md) | 🟡中 | 27/29 完成，2 项待 GUI 验证 | Elevation×3、Favorites×2、QuickPath×2 等 11 个对话框、2 个控件、1 个转换器（2 项阻塞于 GUI 测试） |
| **P1** | 自动更新检测 | [auto-update.md](.sisyphus/plans/auto-update.md) | 🟡中 | 4-6h | GitHub Releases API 版本检查、AboutWindow 更新 Tab、UpdateAvailableDialog、设置开关、单元测试 |
| **P2** | 解压多压缩包按来源目录分组 | [result-preview-panel.md](.sisyphus/plans/result-preview-panel.md) | 🟡中 | 2-4h | 结果预览面板遗留①：当前多压缩包条目合并平铺，改为按来源目录分组 + 压缩包壳节点（详见文档「未实现项（后续可做）」） |
| **P2** | 结果预览截断占位符点击展开 | [result-preview-panel.md](.sisyphus/plans/result-preview-panel.md) | 🟢低 | 1-2h | 结果预览面板遗留②：当前截断为静态"…"文本，改为点击就地展开完整子节点 |
| **P2** | 结果预览冲突检测双模式 | [result-preview-panel.md](.sisyphus/plans/result-preview-panel.md) | 🟡中 | 2-3h | 结果预览面板遗留③：当前固定全量 File.Exists 检测，改为快速（目录级）/完整（逐文件）可切换 |
| **P2** | 可排序的默认路径优先级（文件选择器初始路径） | [path-priority-sortable.md](.sisyphus/plans/path-priority-sortable.md) | 🟢低 | 3-4h | CustomFilePickerDialog 初始路径改为用户可排序的优先级链（场景/资源管理器/最近访问/手动路径，桌面兜底），↑↓ 按钮调整顺序 + 手动路径 TextBox；替代 WPF 四档预设 |
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

### 🟡 需调整 — Core 可复用，UI 需移植（19 个）

`archive-diff.md`、`archive-rename-entry.md`、`auto-update.md`、`compress-preset.md`、`compression-estimator.md`、`custom-columns.md`、`extract-journal-undo.md`、`filename-suffix-template.md`、`font-preview-ligature.md`、`html-preview-webview-fallback.md`、`ico-file-icon-extract.md`、`metadata-panel-configurable.md`、`new-format-support.md`、`office-content-preview-avalonia.md`、`preview-quick-modes.md`、`progress-window-enhancement.md`、`avalonia-ui-feature-parity.md`、`avalonia-wpf-diff-plan.md`、`外部工具视频元数据（无计划文件）`

### 🔴 冲突 — 需完全重写或废弃（9 个）

`drag-drop-direct-extract.md`（Win32 Shell API）、`embedded-thumbnail-preview.md`（Shell 缩略图 API）、`frozen-column.md`（DataGrid 冻结列）、`icon-dll.md`（原生资源 DLL）、`msi-packaging-wix.md`（Windows Installer）、`quickpath-unified.md`（WPF UserControl 体系）、`rar-compression.md`（Windows 外置 rar.exe）、`win11-first-level-menu.md`（COM IExplorerCommand）、`context-menu-tree-preview.md`（COM HMENU）

### 关键发现

1. **🔴 的共性**：全部依赖 Windows Shell API（COM/注册表/P/Invoke/Shell32）或 WPF 独占控件/DataGrid 特定行为。跨平台后这些功能要么砍掉（COM 右键菜单、UAC 提权），要么需要 OS 级不同实现（Linux `.desktop` actions / macOS `NSExtension`）。
2. **🟡 的规律一致**：Core 层的解析器/算法/模型全是纯 C# 可复用，只有 UI 渲染层（WPF XAML + 控件）需要移植到 Avalonia 等价物。
3. **🟢 的 3 个计划**：基本是纯工具代码（正则、IO、字节操作）或发布配置，开箱即跨平台。
4. **最值得跨平台前实现的计划**：优先完成 🟢/🟡 计划（已有 `file-filter-feature.md` 等完成先例），积累跨平台经验后再攻坚 🔴 计划。
5. **Core 层遗留问题**：`PasswordManager`（DPAPI → 已实现 `AesGcmDataProtector` 抽象）和 `SevenZipEngine`（7z.dll Windows-only）已在跨平台准备中。

*此文档将随开发进度持续更新*


