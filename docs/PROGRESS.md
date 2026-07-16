# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF → Avalonia 迁移中)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF → Avalonia 迁移中 + SharpCompress + SharpSevenZip

## 版本
- **当前版本**: 0.4.4
- **发布日期**: 2026-06-29

## 版本历史

版本历史按项目分为三个独立线索：
- **Avalonia 版** — 主力开发，无正式版本号，以日期为标识
- **WPF 版** — 遗留版，有正式发布版本号
- **共享层** — Core 引擎、ShellExt COM 组件、构建/CI/安装器，两项目共用

---

### MantisZip.UI.Avalonia（主力版）

**2026-07-16** — 移除 WebView2 依赖：Markdown/HTML/PDF 预览改用纯 .NET 跨平台实现
  - Markdown: Markdig AST → `MarkdownPreviewBuilder` → Avalonia 原生控件树（替代 Markdig→HTML→WebView2）
  - HTML: `ReverseMarkdown` 转为 Markdown → 复用 MarkdownPreviewBuilder 控件树（替代 WebView2 直接渲染）
  - PDF: `UglyToad.PdfPig` + `SkiaSharp` 逐页位图渲染 + 翻页导航（替代 WebView2 加载 PDF）
  - 移除 `Avalonia.Controls.WebView` 包引用和所有 WebView2 相关代码（csproj/axaml/cs）
  - 新增依赖：ReverseMarkdown 4.7.0、PdfPig 0.1.15、PdfPig.Rendering.Skia 0.1.15.4
  - Build 0 errors 0 warnings

**2026-07-16** — 预览两阶段加载：立即信息栏 + 弹跳点加载页 → 异步内容
  - ShowPreviewAsync 拆分 Phase 1（同步显示加载状态+信息栏）与 Phase 2（异步提取后显示内容）
  - `_previewLoadVersion` 版本号守卫防止异步结果覆盖新选择文件
  - PreviewViewModel 新增 `IsLoadingPreview`/`LoadingFileName` 属性和 `ShowLoading()` 方法
  - `OnPreviewTypeChanged` 自动在内容就绪时关闭加载页
  - PreviewPanel.axaml 新增全页居中弹跳点动画加载页，替代旧的 ProgressBar

**2026-07-16** — 修正 ICO BMP 帧 biHeight 翻倍导致的小图标黑色方块
  - `IcoParser.DecodeBmpFrame` 在写入 BMP 文件前修正 DIB header 的 biHeight 为实际像素高度
  - SkiaSharp 多读 pixelHeight 行垃圾数据导致小图标上方渲染出黑色方块

**2026-07-16** — 修复 ICO BMP 帧透明度丢失
  - ICO BMP 帧透明度来自 AND 掩码（1 bit/pixel，位于 XOR 像素数据之后）
  - 解码 XOR 像素后解析 AND 掩码，对掩码位=1 的像素设置 alpha=0
  - AND 掩码在原始 DIB 中 bottom-up 存储，解码后位图 top-down，需 y-mirror

**2026-07-16** — PDF 预览性能优化：限制渲染分辨率 + 隐藏工具栏
  - `ShowPdfAsync` 中获取 PDF 页面原始尺寸（PdfPig GetPage），计算合适的缩放比例限制渲染最大宽度 1920/高度 1080
  - `LoadPdfPageAsync` 改用动态 `_pdfRenderScale` 替代硬编码 1.0f
  - 避免大页面 PDF（如键盘图海报）渲染出数百 MB 的超大位图
  - PDF 改为元数据格式 `IsToolbarVisible = false`，对齐其他元数据格式

**2026-07-15** — 预览图像行为一致性与 ZoomFit 自适应视口
  - GIF 魔数路由修复：`FileFormat.Gif` → `PreviewType.Gif`（而非错误地归入图片预览）
  - 9 个元数据格式隐藏空工具栏边框（PE/CSV/SQLite/ISO/Torrent/Office/Video/Audio/Font）
  - ZoomFit 改用实际视口尺寸（`PreviewContentScroller.SizeChanged`）替代固定 600×500
  - ShowImage/ShowGif 初始缩放统一调用 ZoomFit()
  - `_isZoomFitActive` 标记：ZoomFit 模式窗口缩放自动重适应，手动缩放后不覆盖
  - 图像改用 `Width/Height + Stretch=Uniform` 替代 ScaleTransform，修复滚动区空白

**2026-07-15** — ICO 多帧画廊预览
  - 新增 `IcoParser`：解析 ICO 目录，提取所有帧（PNG 直接解码，BMP 经 SkiaSharp 带 AND 掩码剥离）
  - PreviewViewModel: ShowIcoGallery、IcoFrames 集合、FlattenAlpha 切换
  - PreviewPanel: ItemsControl + WrapPanel 画廊布局，每帧带尺寸标签
  - MainWindowViewModel: `.ico` 文件路由到 ICO 画廊而非 Image 预览
  - 工具栏新增 FlattenAlpha 切换（白色背景上渲染半透明像素）

**2026-07-15** — P0-2 压缩选项统一 + SettingsWindow 分组 + 格式行修复
  - SettingsWindow 压缩选项拆分为 ZIP/7z 两组，与 WPF 布局对齐
  - DynamicFormatOptionsPanel 修复格式切换；补充 ZIP 压缩方法、7z 固实块大小/字典大小/单词大小/匹配器
  - 新增 `CompressionOptionData` 共享类：所有选项列表统一数据源，消除 ViewModel 间不一致
  - 本地化键全部与 WPF 对齐（`FormatOptions_*` ↔ `CompressOpt_*`）
  - CompressSettingsWindow 新增 7z 格式选项和 DynamicFormatOptionsPanel
  - MainWindowViewModel 修复预览信息面板格式行累加

**2026-07-15** — P0-2 压缩选项 + P0-3 魔数检测预览集成
  - AppSettings 新增 10 个压缩属性（格式/压缩级别/编码/方法/固实选项等）
  - DynamicFormatOptionsPanel 从设置读取默认值
  - SettingsWindow 高级选项 UI（压缩选项分组面板）
  - PreviewService.ClassifyPreviewByMagicAsync：魔数优先的格式分类
  - App 启动时初始化魔数检测；ShowPreviewAsync 魔数优先路由 + 扩展名回退
  - FormatMetadata 信息面板显示格式检测结果（冲突时：警告图标 + 扩展名提示）

**2026-07-15** — TabControl UI 细节修复 + 工具栏按钮背景统一
  - TabControl 模板 override 封装 ItemsPresenter 到 Border 设 tab strip 背景 `ThemeHeaderBgBrush` 消除 tab 标题间隙白色断层
  - `TabControl.Padding=0` 消除 ContentPresenter Margin 导致的内容区左右白边
  - 消除 `TabItem:selected` 双下划线；修复 `TabItem:pointerover` hover 色不生效
  - 新增 `ThemeTabHover` 浅蓝色资源
  - ToolbarButton 默认背景改 `ThemeHeaderBgBrush` 与工具栏底色融合

**2026-07-14** — TextBox Fluent 主题样式修复
  - Fluent Light 主题 `TextControlBorderBrush`/`TextControlBackground` 遮蔽 `Application.Resources` 覆盖值
  - 改用 `/template/` 选择器绕开限制，直接设置普通状态背景/边框
  - Light/Dark 新增 17 项 `TextControl*` 资源覆盖

**2026-07-13** — UI 功能补齐（对话框 + 控件 + 转换器）
  - 11 个对话框（Elevation×3/AddFavorite/FavoriteManager/ArchiveComment/AppMessageBox/QuickPath/QuickPathPre/ArchiveSaveAs/UnifiedExtract）完整移植
  - 2 个控件（QuickPathControl/DynamicFormatOptionsPanel）+ 1 个转换器（BatchStatusConverters）
  - MainWindowViewModel 5 个新对话框回调；MainWindow Favorites 子菜单
  - 20+ i18n 中英文键；可构建 0 错误

**2026-07-13** — i18n 缺失 key 补齐
  - 扫描代码中 427 个 `LocalizationManager.T()` 引用，补齐 42 个缺失 key
  - 从 WPF 复制 `languages.json`；创建 `Icons/.gitkeep` 保留空目录；复制 `DonateQr.jpg`

**2026-07-09** — 字体预览中文名称优先 + CSV/SQLite DataGrid 修复
  - FontParser name 表解析新增 `lid`（language ID）追踪；同平台下优先取简体中文（lid=0x0804）
  - Avalonia DataGrid `AutoGenerateColumns` 不兼容 `DataView`，改为手动 `SetupDataGridColumns`
  - 修复水平滚动条缺失、列标题不刷新、添加网格线
  - BT 种子文件列表改为目录树结构 (`TorrentTreeNode` + `TreeDataTemplate`)
  - 预览标题使用种子/字体内部名称

**2026-07-06** — 全局界面字体设置 + 文本预览字体隔离
  - 设置窗口外观 Tab 新增"全局界面字体" ComboBox（枚举系统字体）
  - `AppSettings.AppFontFamily` 持久化；`ApplyAppFontFamily()` 启动和保存设置后刷新
  - 文本预览 TextBox 改为绑定 `TextPreviewFontFamily`，避免被全局字体覆盖
  - 文本预览字号调节（A+/A−）即时持久化到 `AppSettings.TextPreviewFontSize`
  - 新增中英文键 `Settings_Preview_FontDefault` / `Settings_Appearance_AppFontFamily`

**2026-07-06** — 进度条 XAML 模板列补齐
  - Size/CompressedSize/Modified/CompressionRatio 四列从 DataGridTextColumn 改为 DataGridTemplateColumn
  - Rectangle 背景色条 + MultiBinding RatioToWidthConverter
  - 视图菜单添加进度条/目录独立基准/信息面板方向三项开关
  - `ArchiveItemModel` 新增 `RatioDisplay`/`RatioSort` 属性

**2026-07-05** — PreviewPanel DataContextChanged 事件订阅泄漏修复
  - 解构匿名 lambda 为命名方法，DataContext 变更时先 `-=` 旧 VM 再 `+=` 新 VM
  - `SizeChanged` 提取为独立命名方法只订阅一次

**2026-07-05** — 字体预览性能优化
  - 合并折行和测量为一遍（`List<(string, float)>`，消除重复 `MeasureText`）
  - 缓存字体 bytes + 主题色到内存，避免每次重新读文件 + JSON I/O
  - SKBitmap → WriteableBitmap 直接 `Marshal.Copy` 像素内存，跳过 PNG 编解码往返

**2026-07-05** — 字体预览自动换行 + 窗口缩放响应
  - `FontPreviewWrapWidth` 属性驱动 SkiaSharp 折行宽度
  - 绑定 `ScrollViewer.Bounds.Width`；`SizeChanged` 200ms 防抖 + 自动刷新位图

**2026-07-04** — 字体预览重构（Avalonia 端）
  - SkiaSharp 字体位图渲染 + CJK 检测自动过滤不支持的样本文字
  - 回退 TextBlock 兜底显示

**2026-07-04** — P0 元数据字段补齐
  - ShowImage 新增 DPI；ShowAudio 新增 BitDepth；ShowOffice 新增 ModifiedDate
  - ShowTorrent 新增 CreationDate/TrackerCount/IsPrivate/AdditionalInfo

**2026-07-02** — 信息面板修复
  - 默认方向改为 Vertical（下方）；"详细信息"移到上方、"基本信息"移到底部
  - 大小/压缩后/压缩率一行三列；底部加间距避免被状态栏遮挡

**2026-07-02** — Phase 10: WPF 功能补齐（进度条/信息面板/状态栏）
  - 状态栏增强：DirStats 目录文件计数 / FilterStats 过滤统计 / EncodingInfo 编码信息 → 6 列布局
  - 预览信息面板：文件元数据侧栏 + 横向/纵向位置切换 `AppSettings.InfoPanelOrientation`
  - 文件列表进度条 DataGridTemplateColumn：Size/CompressedSize/Modified 背景 Rectangle 色条
  - RatioToWidthConverter / BrushResourceConverter，8 色主题资源（亮/暗）
  - 视图菜单开关（进度条/目录独立基准），i18n 中英文键

**2026-07-01** — ExtractSettingsWindow + CompressSettingsWindow GroupBox 重构
  - ExtractSettingsWindow: Separator → 3 Border GroupBox（源文件/目标目录/文件冲突），窗口 530
  - CompressSettingsWindow: 3 TabItem 加 compactTab，General tab 顺序与 WPF 一致
  - 源文件列表带 AddFile/AddFolder/Remove 按钮
  - ViewModel SelectedPaths 改为 ObservableCollection 支持增删；新增 i18n 键

**2026-06-30** — SettingsWindow compactTab 样式 + DonationDialog 修复
  - Tab 标题改用全局 `TabItem.compactTab` class selector（FontSize=18, MinHeight=36）
  - 窗口 720×560；DonationDialog 修复 `avares://DonateQr.png` 崩溃

**2026-06-30** — 测试菜单（16 个窗口可独立打开）
  - 主菜单新增 🧪 测试菜单，内含 16 个可独立打开的对话框/窗口（含默认测试数据）
  - i18n 中英文键，构建零错误

**2026-06-22** — Bugfix: 筛选工具栏尺寸输入框白边 + 空值红框
  - 添加 `NullableLongConverter` 处理空字符串→null 绑定
  - 尺寸 TextBox 加 `Padding="2,0"` `BorderThickness="1"` 消除白边遮挡数字

**2026-06-21** — Phase 9: 文件列表交互补齐
  - DataGrid 添加双击目录进入、Enter/Backspace/Delete 键盘导航
  - 列排序（`..` 置顶 + 目录优先 + 箭头标记），与 WPF 行为保持一致

**2026-06-21** — Phase 8: 设置窗口 TabControl 重构 + i18n 补全 + ComboBox 修复
  - SettingsWindow 重构为完整 TabControl（压缩/解压/上下文菜单/高级/预览）
  - Preview 分 4 子标签页（文本/字体/表格/布局）；新增 70+ i18n 中英文键
  - 修复 Avalonia 12 不支持 `SelectedValuePath`，改用 `ItemsSource` + `SelectedItem` + `Option` 模式

**2026-06-19** — Phase 7: CLI 命令补齐 + IPC 多实例 + 10 个新对话框 + i18n
  - 9 个 CLI 命令 + IPC 多实例（compress/compress-separate/compress-combined）
  - 设置窗口 Extract/ContextMenu/Advanced 三标签页
  - 10 个新对话框：CompressConflictDialog/ConflictDialog/ErrorDialog/PasswordEditDialog/PasswordHelpDialog/LogPrivacyHelpDialog/MatchedPasswordDialog/DonationDialog
  - CompressSettingsWindow Password 标签增强（库模式/新密码模式/强度指示/自动规则）
  - i18n 中英文全键

**2026-06-18** — 暗色菜单弹出面板白色背景 + 前景色修复
  - 添加 `MenuFlyoutPresenterBackground` 修复菜单弹出面板背景
  - 覆盖 14+ Fluent 资源键：MenuFlyoutItem/Button/TabItem/ComboBox/CheckBox 前景色

**2026-06-18** — Bugfix: SQLite 预览文件锁定
  - SqliteConnection 加 `Pooling=False`，防止连接池在 Dispose 后仍持文件句柄

**2026-06-18** — Bugfix: 按钮悬停黑白色
  - FluentTheme 用黑白资源覆盖按钮 ContentPresenter 的 `:pointerover`/`:pressed` 背景
  - 添加 14 个 Fluent 资源覆盖至 ThemeLight/ThemeDark

**2026-06-17** — Phase 6: 样式统一与视觉打磨
  - 全局控件 CornerRadius 6px + Transitions (0.15s)
  - TextBox/ComboBox 焦点高亮 + Dialog Padding 统一 16

**2026-06-17** — Phase 5: 工具栏按钮样式重构
  - Button/ToggleButton 统一样式类，消除重复属性实例，按钮高度 42→54

**2026-06-15** — Phase 4: App.axaml 统一控件样式
  - 移除 WPF 风格样式，适配 Avalonia 原生样式系统

**2026-06-11** — Phase 0: 项目骨架（首次提交）
  - 新建 `src/MantisZip.UI.Avalonia/`（net9.0 + MVVM + Skia），目标跨平台
  - 文件浏览（ListBox + 列头）+ 文本预览（编码检测）+ CSV 预览（DataView）+ PE 元数据预览
  - DataGrid v12.0.0 主题资源为空，改 ListBox + ItemsControl 替代

---

### MantisZip.UI（WPF 遗留版）

#### v0.4.5 (2026-07-14)
  - **冲突对话框暂停/取消功能** — CompressConflictDialog/ConflictDialog 新增暂停/取消按钮；CompressSettingsWindow 和 App 层新增 PauseFromConflict 重入路径
  - **预设显示 + 筛选统计文字常显** — 修复预设配置和过滤统计文字始终可见
  - **修复 Win11 日文版 ZIP 假阳性密码检测** — 防范 SharpCompress 误弹密码框
  - **预览信息面板切换** — View 菜单新增切换开关，AppSettings.ShowPreviewInfoPanel 持久化
  - **COM handler 动词重命名** — `open` → `mantiszipopen` 防止 Shell 动词冲突
  - **安装下载依赖增加提示** — 安装时下载 .NET/WebView2 增加用户提示

#### v0.4.4+ (2026-07-09) 移除 Applications shell\open\command
  - 移除 `Applications\MantisZip.UI.exe\shell\open\command` 注册，防止新安装时 Shell 关联刷新错误路由
  - `SupportedTypes` 保留，双击走 per-format ProgId 不受影响

#### v0.4.4 (2026-07-07) COM 动态菜单 + pending 状态 + 延迟级联安装
  - **COM 动态菜单组件** — `MantisZip.ShellExt` 实现 `IShellExtInit` + `IContextMenu`
    - 动态菜单文本（「解压到 {name}」「压缩到 {name}.zip」）
    - 纯 Win32 图标加载（无 `System.Drawing` 依赖）
    - 多选文件数量显示（「打开压缩包 等 N 个文件」）
    - 8 个独立菜单项开关（cascade/verb 两种注册方式）
  - **COM + 延迟级联安装流程** — Install 仅注册 COM，级联菜单在检测到 COM 未加载时自动安装
  - **动态菜单状态跟踪** — `DynamicMenuStatus`（Active/Pending/Fallback/Disabled）
  - **pending 态 COM 菜单占位符** — COM handler 检测到 pending 状态时插入灰色禁用分隔符
  - **安装包 .NET 9 检测修复** — 增加文件系统回退检测 `cmd /c dir ...\9.*`

#### v0.4.4+ (2026-07-03) 双击文件默认程序打开
  - 双击文件调用系统默认程序打开，`DoubleClickOpenThreshold` 设置阈值（默认 10MB）
  - 超过阈值时弹出确认对话框；文件 >= 1MB 显示 ProgressWindow
  - Tar/GZip/ISO 不支持单文件提取，给出提示
  - 上级目录（..）选中时预览面板不刷新修复

#### v0.4.4 (2026-07-03) 密码流程统一
  - `ResolvePasswordAsync` 统一密码入口：检查加密 → TryMatchPassword → 对话框循环
  - LoadArchiveAsync / ExtractAsync / RunExtractStatic / HandleExtractBatchCore 全部简化
  - 删除 `ExtractWithPasswordAsync`；修复密码框取消后陷入循环

#### v0.4.4 (2026-06-30) 魔数检测预览系统 Phase 2 — UI 集成
  - 魔数优先路由重构（`TryMagicPreview`），写入 `PreviewExtraInfoPanel`
  - 冲突检测 + 切换按钮：魔数结果与扩展名不一致时插入"按扩展名/按魔数"切换按钮
  - `AppSettings.EnableFormatDetection` 开关（默认 true）

#### v0.4.3+ (2026-06-30) 工具栏新增「解压选择文件」按钮
  - 位于「解压」与「压缩」之间，行为与右键菜单「解压到…」一致
  - 右键菜单图标统一（📤 → 📑）

#### v0.4.3+ (2026-06-30) 默认路径优先级设置
  - `AppSettings.DefaultPathPriority` 支持 4 种策略：场景相关 / 资源管理器 / 最近使用 / 桌面
  - `ResolveDefaultPath()` 按优先级链自动选取最佳默认路径
  - 设置 UI 高级标签页新增「默认路径优先级」GroupBox

#### v0.4.3 (2026-06-22) QuickPathControl 统一路径选择 + 书签管理器 + 权限跳过
  - QuickPathControl 统一压缩/解压窗口的路径选择（支持收藏夹 / 历史记录 / 资源管理器窗口 / 浏览）
  - 资源管理器窗口检测重写：COM IShellWindows 为主 + Win32 EnumWindows 兜底
  - 书签管理器菜单（工具 > 书签管理器）
  - 压缩包内逐条目权限跳过：`ExtractResult` 类 + try-catch 跳过失败条目继续处理
  - UAC 提权弹窗修复：由事前预检改为响应式拦截，首次弹窗后静默跳过
  - ProgressWindow 错误摘要（可复制 TextBox）
  - DynamicFormatOptionsPanel 后端接线：ZIP 编码/7z 压缩方法/7z 固实选项
  - 默认格式选项设置：`ZipEncoding`、`SevenZipCompressionMethod`、`SevenZipSolid`
  - RELEASE_NOTES.md 双语化

#### v0.4.2 (2026-06-20) 安装程序主题/语言选择修复 + ZIP copy-mode 进度与取消
  - 安装时主题选择不生效修复：`settings.json` 添加占位符 + `PatchSettingsThemeAndLanguage`
  - ZIP 添加/删除进度与取消优化：单遍流式（80KB 块 CRC32 + Deflate），每块粒度进度报告
  - 收尾阶段分步报告：中央目录 92% → 目录尾 94% → 刷盘 97% → 原子替换 100%

#### v0.4.1 (2026-06-18) 发布流程修复 + 文档双语化
  - ZIP Copy-Mode 优化：`ZipBinaryRewriter` 实现二进制级压缩流直拷
  - CI release notes regex 修复
  - RELEASE_NOTES.md 双语化
  - 文件列表增加"返回父目录"项目
  - UAC 提权双模式：`AllowElevation` 设置 + `App.Elevation.cs` + 3 个对话框
  - 解除权限不足响应式拦截 + 提权弹窗行为优化

#### v0.4.0 (2026-06-15) 第一个上线版本
  - 功能基本完成，测试基本完成
  - CLI 参数归一化（`install-assoc` → `--install-assoc`）
  - 右键菜单改为全平台统一静态级联方案（`InstallCascade`），COM 默认不安装
  - 设置窗口新增"动态菜单"选项
  - 临时文件管理 GroupBox + 启动时自动清理
  - Win11 右键菜单不显示修复（HKCU COM 注册被忽略，走静态级联）
  - RELEASE_NOTES.md 移至根目录
  - CI 修复：TarGzEngine 测试 / ISCC ChineseSimplified.isl / ShellExt runtimeconfig.json / en.json 键缺失 / 路径引号截断 / MyAppVersion 传递
  - 全局调试日志增强（`CoreLog.DiagnosticsEnabled` + 43 个 catch 块注入）
  - LogRedactor 隐私脱敏修复（相对路径 regex 分支）
  - README.md 路径修复（反斜杠 → 正斜杠）

#### v0.3.13 (2026-06-14) 修复问题
  - ToggleSepDirBaseline / ToggleProgressBars 根目录状态重置修复
  - CompressConflictDialog 重命名按钮图标丢失修复

#### v0.3.13 (2026-06-13) 对话框 Owner 修正 + 安装脚本 + 字体预览（WPF 端）
  - 对话框 Owner 修正（6 个文件），弹窗不再被主窗口挡住
  - installer.iss 通配符化 + 缺失 DLL 补全
  - 预置用户设置机制（`installer\prebuilt\settings.json`）
  - 字体预览修复（CJK 名优先 + CFF-OTF 回退 + 清理重置）

#### v0.3.13 (2026-06-12) 压缩批处理修复 + 进程残留修复
  - 压缩批处理文件进度条锯齿修复
  - 压缩完成后 exe 进程残留修复（两处 bug）

#### v0.3.13 (2026-06-11) 提取文件列表展示和目录树构建逻辑到 Core
  - `ArchiveTreeBuilder` + `ArchiveEntryLister`（Core/Services）
  - WPF 重构：`BuildFolderTree()` 和 `FilterFiles()` 改为调用 Core 服务

#### v0.3.12 (2026-06-10) 文件列表筛选增强 / 解压路径裁剪
  - 排除文本框 + 子串/通配符两种匹配模式
  - 筛选匹配显示名而非 FullPath（解决根目录名误匹配 bug）
  - 解压路径裁剪设置（保留完整路径 / 相对当前目录）

#### v0.3.11 (2026-06-08) 文件列表拖拽提取修复
  - 异步重入竞态修复（`_isDragExtracting` 标志）
  - ZIP 编码兼容性修复（CP437/GBK 自动探测）
  - Tar/GZip 提取统一委托给 `ArchiveEntryExtractor`
  - 多选/目录拖拽支持、自身拖拽光标修复

#### v0.3.10 (2026-06-06→06-07) 测试按钮完整性检查 + ProgressWindow 集成
  - 引擎测试完整性提升：ZipEngine/TarGzEngine/SevenZipEngine 逐项完整解压验证
  - 测试进度 UI 改为 ProgressWindow，支持取消操作
  - Dispatcher 优先级竞态修复
  - UI 主题一致性修复（跨 7 个 XAML 文件）

#### v0.3.9 (2026-06-06→06-07) 文件关联 + 独立 ProgId + 设置窗口 UI 统一
  - 文件关联 Bug 修复（.tar.gz 跳过 / 自定义扩展名清理 / 图标清理）
  - Per-extension 独立 ProgId（MantisZip.Zip / MantisZip.7z / …），各自显示格式图标
  - 设置窗口 ComboBox 外观统一
  - 压缩密码"不匹配"误报修复
  - 压缩右键菜单 IPC 期间提前显示 UI
  - 批处理模式下取消按钮真正终止压缩
  - 移除 SharpZipLib 注释编辑耦合（ZipCommentHelper）
  - 代码拆分：App.Cli.cs / CompressSettingsWindow / SettingsWindow / ShellIntegration / MainWindow 按职责拆分

#### v0.3.8 (2026-06-06) 右键菜单增强 + 文件关联面板重构 + 文件列表筛选/搜索
  - 右键菜单修复（批次污染 / 闪烁 / 图标缓存 / 子菜单图标）
  - 进度窗口增强（保持打开切换按钮 / 倒计时即时生效 / 压缩包计数始终显示）
  - 文件关联面板重构：per-extension 复选框 + 系统图标 + 三态状态视觉区分
  - 文件列表筛选/搜索：全部子目录展开 / 文字+日期+大小 AND 过滤引擎 / 空结果提示

#### v0.3.7-refined-5 (2026-06-04) 引擎统一完成
  - SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip 全部完成
  - 批量进度文件列表 / ExtractSettingsWindow / COM 右键菜单全部完成

#### v0.3.7-refined-4 (2026-06-03) 关于窗口重设计
  - AboutWindow 4 标签页（关于/作者/依赖库/致谢）
  - 21 个 About_* 本地化键 + 13 个冒烟测试

#### v0.3.7-refined-3 (2026-06-03) 密码工具栏 + 关闭压缩包 + 捐赠 + 空状态重设计 + 压缩冲突增强
  - 密码按钮三态重设计（无加密/有加密未匹配/已匹配）
  - MatchedPasswordDialog / Theme_StatusSuccessBg 主题色
  - 关闭压缩包菜单（Ctrl+W）/ 文件菜单重排序
  - 捐赠对话框 / 空状态重设计 / CompressConflictDialog"应用到全部"

#### v0.3.7-refined-2 (2026-06-02) 压缩窗口密码 Tab 重设计 + 调试日志增强
  - 对照 `docs/design-compress-password-tab.md` 修复全部差异
  - PasswordBox/TextBox 切换 / 密码强度 `●` 颜色 / 自动规则调整

#### v0.3.7-refined (2026-06-01) COM 右键菜单完善（图标 + 文本 + 本地化）
  - `CreateDIBSection` 32-bit DIB 修复透明背景变纯色
  - 菜单文本精简 + 多选动态文本 + 8 个 ShellExt_* 本地化键

#### v0.3.7 (2026-05-31) COM 右键菜单
  - 新建 MantisZip.ShellExt 项目（.NET 9 comhost）
  - ContextMenuHandler.cs 完整实现 IShellExtInit + IContextMenu，8 个菜单项
  - NativeMethods.cs Win32 互操作 + COM 注册

#### v0.3.6 (2026-05-30) ExtractSettingsWindow UI 重构
  - TabControl + GroupBox + 2-column Grid 架构
  - 配色对齐（移除显式颜色，靠主题继承）

#### v0.3.5 (2026-05-30) 批处理进度文件列表 + IPC 合并
  - ProgressWindow 批处理文件列表（BatchItemStatus + GridView）
  - `--compress-separate` / `--compress-combined` IPC 合并（800ms 收集窗口）

#### v0.3.4 (2026-05-28~29) 引擎统一 + ExtractSettingsWindow + 调试日志
  - SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip 2.0.45
  - ExtractSettingsWindow 创建 + PreserveDirectoryRoot 设置
  - 调试日志系统增强（7 类日志）

#### v0.3.3 (2026-05-27) 安装器多语言与预览设置增强
  - 数据表格行/列限制可配置 + 字体预览字号可配置
  - WebView2 启动时预初始化 + Inno Setup 多语言支持

#### v0.3.2 (2026-05-27) 代码拆分
  - App.xaml.cs 1977 行拆为 5 个 partial class 文件

#### v0.3.1 (2026-05-26) 预览修复与注释
  - WebView2 PDF 内容渲染 / PDF 页数统计修复 / 图片缩放修复
  - GIF 帧导航增强 / 字体预览渲染优化 / PE/PDF 预览缓存
  - 400+ 方法头注释 + 170+ 文件头注释 + 17 份计划文档

#### v0.1.0 (2026-04-24) 初始版本
  - ZIP/7z/RAR/TAR/GZ/TGZ 压缩解压
  - 目录树导航 + 文件列表 + 密码管理器
  - 拖拽解压/压缩

---

### 共享层（Core / ShellExt / 构建）

这些变更影响两项目共用代码，按时间从新到旧排列。

#### v0.4.4 (2026-07-13) ZipEngine SharpCompress 迁移 Plan B 确认完成
  - SharpSevenZip `OutArchiveFormat.Zip`+`Aes256` 替代 SharpZipLib 加密回退
  - `MantisZip.Core.csproj` 已无 SharpZipLib 引用；Core 构建 0 错误 0 警告；236/236 测试通过

#### v0.4.4+ (2026-07-08) AddToArchiveAsync 加密条目预检
  - 新增显式预检：遍历加密条目但未提供密码 → 提前抛出 `InvalidOperationException`
  - 修复 CI 环境 `CryptographicException` 测试失败（改为确定性异常）

#### v0.4.4+ (2026-07-02) 压缩包路径处理一站式重构 — ArchivePath 统一入口
  - 新建 `ArchivePath` 类：`Normalize()` / `GetFileName()` / `GetDirectoryName()` / `GetFileNameWithoutExtension()` / `FindEntry()`
  - 消除 4 种遗留路径处理模式（29 处 `Replace` + 16 处 `TrimEnd`）
  - 11 个文件修改

#### v0.4.4 (2026-07-01) 安装包增强 — .NET 9 自动下载 + 离线包
  - installer.iss 新增 .NET 9 Desktop Runtime 自动检测 + 下载安装
  - 安装包文件名标准化：`NoDotNet` → `WebSetup`，`Setup` → `Offline`
  - 离线安装包捆绑 WebView2 Standalone Installer（`installer-selfcontained.iss`）

#### v0.4.4 (2026-06-30) 魔数检测预览系统 Phase 1 — Core 引擎
  - `FileFormatDetector`（35+ 魔数签名 + ZIP 子类型 + PE 双重验证）
  - `LooksLikeText()` 启发式检测纯文本文件
  - `ExtractHeadAsync`/`ExtractHeadTailAsync` 压缩包条目标头提取
  - `FileFormatHelper` 90+ 格式中文显示名
  - `ArchiveEngineFactory` 魔数兜底：扩展名未匹配时读取头部字节识别真实格式

#### v0.4.3+ (2026-06-29) 预览系统计划更新（Avalonia 方向 + 快速预览模式）
  - 分析 WPF→Avalonia 迁移对预览系统各格式的影响
  - 三级依赖隔离体系：Magick.NET/LibVLC 等拆分为可选插件

#### v0.4.3 (2026-06-22) DynamicFormatOptionsPanel 后端接线
  - `ArchiveOptions`/`CompressRequest` 新增 `FileNameEncoding`、`SevenZipCompressionMethod`、`SevenZipSolid`
  - ZipEngine 根据 `FileNameEncoding` 选择 ZIP 文件名编码
  - SevenZipEngine 根据选项选择压缩方法/固实模式

#### v0.4.2 (2026-06-20) ZIP copy-mode 优化 + UAC 提权
  - `ZipBinaryRewriter`：二进制级压缩流直拷（EOCD 扫描 + CDFH 解析 + LFH 读写 + 中央目录重建）
  - `AppSettings.AllowElevation` + `App.Elevation.cs` + 3 个提权对话框
  - 设计文档：`zip-copy-mode-optimization.md` / `uac-elevation-permission.md`

#### v0.4.1 (2026-06-18) 自包含安装包
  - `installer-selfcontained.iss` 完全离线安装包
  - 依赖下载脚本 `download-redist.ps1`

#### v0.4.0 (2026-06-15) 发布基础设施
  - `.github/workflows/release.yml` 自动化发布
  - 版本号从 git tag 派生，CI 自动写入
  - installer.iss：`#ifndef MyAppVersion` 支持 `/d` 命令行参数覆盖

#### v0.3.14-dev (2026-06-11) Avalonia Phase 0 — 共享层适配
  - `ArchiveTreeBuilder` + `ArchiveEntryLister` 从 WPF 提取到 Core/Services
  - `FolderNode` 类从 WPF 移到 Core

#### v0.3.13 (2026-06-15) 完全移除 SharpZipLib 生产代码依赖
  - `MantisZip.Core.csproj` 移除 SharpZipLib 包引用（保留 test-only）
  - ZipEngine 加密路径 SharpZipLib → SharpSevenZip

#### v0.3.13 (2026-06-13) DPAPI → AES-GCM 跨平台加密
  - `IDataProtector` 接口 + `AesGcmDataProtector`（AES-256-GCM）
  - `PasswordManager` 移除 `[SupportedOSPlatform("windows")]`
  - 旧 DPAPI 格式自动迁移

#### v0.3.13 (2026-06-12) ZipEngine SharpZipLib → SharpCompress 迁移
  - `CompressAsync` / `AddToArchiveAsync` / `DeleteEntriesAsync` 全部迁移
  - 加密路径保留 SharpZipLib 回退（后由 v0.4.4 SharpSevenZip 替代）

#### v0.3.13 (2026-06-11) RAR 提取进度条
  - `WriteProgressStream`（Core/Utils）支持 SharpSevenZip 同步 ExtractFile 进度回调
  - 100ms 节流，与 ZipEngine 每文件进度模式一致

#### v0.3.11 (2026-06-08) ZIP 编码兼容性
  - `ArchiveEntryExtractor` 统一提取，CP437/GBK 自动探测

#### v0.3.9 (2026-06-06→07) ShellIntegration 拆分 + ZipCommentHelper
  - ShellIntegration.cs 拆为 ShellIntegration.Menu.cs + ShellIntegration.Assoc.cs
  - `ZipCommentHelper` 直接操作 ZIP EOCD 字节，不依赖 SharpZipLib

#### v0.3.8 (2026-06-06) ShellExt COM 持续改进
  - 修复 ShellExt `_fullFileList` 跨右键调用批次污染（2 秒时间窗口检测）
  - 永久缓存图标 HBITMAP，消除每次右键 40-120ms 图标重载延迟

#### v0.3.7 (2026-05-31) ShellExt COM 组件创建
  - 新建 `MantisZip.ShellExt` 项目（.NET 9 类库，`<EnableComHosting>true</EnableComHosting>`）
  - `ContextMenuHandler.cs` + `NativeMethods.cs` Win32 互操作
  - COM 注册 + AppSettings 同步到注册表

#### v0.3.4 (2026-05-28~29) 引擎统一
  - SharpZipLib→SharpCompress（ZipEngine/TarGzEngine）
  - 7z.exe/SevenZipExtractor→SharpSevenZip 2.0.45
  - SevenZipExtractor NuGet 包移除

---

## 历史设计方案索引

以下设计方案对应功能已在过往版本中完成，对应设计文档存于 `.sisyphus/plans/` 供回溯参考：

| 功能 | 设计文档 | 实现版本 |
|------|----------|:--------:|
| 移除 WebView2 依赖（Markdown/HTML/PDF 跨平台预览） | [remove-webview2-preview.md](.sisyphus/plans/remove-webview2-preview.md) | v0.4.5 |
| 便携版模式 | [portable-mode.md](.sisyphus/plans/portable-mode.md) | v0.4.5 |
| 文件冲突对话框暂停/取消 | [conflict-dialog-pause-cancel.md](.sisyphus/plans/conflict-dialog-pause-cancel.md) | v0.4.5 |
| 压缩选项增强（7z/ZIP 格式参数扩展） | [compression-options-enhancement.md](.sisyphus/plans/compression-options-enhancement.md) | v0.4.5 |
| 双击行为 + 解压后删原包 | [doubleclick-extract-settings.md](.sisyphus/plans/doubleclick-extract-settings.md) | v0.4.4+ |
| 魔数检测文件真实格式 | [preview-magic-detection.md](.sisyphus/plans/preview-magic-detection.md) | v0.4.4 |
| 密码流程统一 | [password-flow-unification.md](.sisyphus/plans/password-flow-unification.md) | v0.4.4 |
| 致谢贡献者名单 | [contributors-panel.md](.sisyphus/plans/contributors-panel.md) | v0.4.3+ |
| 安装程序 .NET 9 自动下载 | [installer-dotnet-autodownload.md](.sisyphus/plans/installer-dotnet-autodownload.md) | v0.4.3+ |
| 预览格式扩展（12 种元数据格式） | [preview-extended-formats.md](.sisyphus/plans/preview-extended-formats.md) | v0.3.0 |
| 快速压缩拆分为独立/合并两项 | [split-compress.md](.sisyphus/plans/split-compress.md) | v0.2.10 |
| 加载大文件 overlay | [archive-loading-progress.md](.sisyphus/plans/archive-loading-progress.md) | v0.3.1 |
| 添加到/从压缩包删除 | [archive-add-delete.md](.sisyphus/plans/archive-add-delete.md) | v0.2.9 |
| 暗色/亮色主题 | [dark-theme.md](.sisyphus/plans/dark-theme.md) | v0.2.9 |
| 日志隐私脱敏 | [log-privacy-redaction.md](.sisyphus/plans/log-privacy-redaction.md) | v0.2.8 |
| 国际化 (i18n) | [i18n-localization.md](.sisyphus/plans/i18n-localization.md) | v0.2.8 |
| 智能解压 (Smart Extract) | [smart-extract.md](.sisyphus/plans/smart-extract.md) | v0.2.10 |
| 文件列表筛选/搜索 | [file-list-filter-search.md](.sisyphus/plans/file-list-filter-search.md) | v0.3.8 |
| 引擎统一 (SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip) | [engine-unification-sharpcompress.md](.sisyphus/plans/engine-unification-sharpcompress.md) | v0.3.4 |
| 文件大小进度条 | [file-size-progress-bar.md](.sisyphus/plans/file-size-progress-bar.md) | v0.3.4 |
| PNG 透明通道控制 | [png-transparency-3way.md](.sisyphus/plans/png-transparency-3way.md) | v0.3.4+ |
| 批量进度文件列表 | [batch-progress-list.md](.sisyphus/plans/batch-progress-list.md) | v0.3.5 |
| 解压配置面板 (ExtractSettingsWindow) | [extract-settings-window.md](.sisyphus/plans/extract-settings-window.md) | v0.3.6 |
| COM 右键菜单 | [com-context-menu.md](.sisyphus/plans/com-context-menu.md) | v0.3.7 |
| COM 迁移映射表 | [com-migration-mapping.md](.sisyphus/plans/com-migration-mapping.md) | v0.3.7（辅助文档） |
| 压缩窗口密码 Tab 重设计 | [design-compress-password-tab.md](.sisyphus/plans/design-compress-password-tab.md) | v0.3.7-refined-2 |
| 关于窗口重设计 | [about-window-redesign.md](.sisyphus/plans/about-window-redesign.md) | v0.3.7-refined-4 |
| 文件关联 per-extension ProgId | [file-assoc-per-extension.md](.sisyphus/plans/file-assoc-per-extension.md) | v0.3.9 |
| 移除 SharpZipLib 注释编辑耦合 | [remove-sharpziplib.md](.sisyphus/plans/remove-sharpziplib.md) | v0.3.9 |
| ZipEngine SharpZipLib 完全迁移 (加密路径→SharpSevenZip) | [zipengine-sharpcompress-migration.md](.sisyphus/plans/zipengine-sharpcompress-migration.md) | v0.3.13 |
| 压缩流程统一化 (CompressService) | [compress-service-unify.md](.sisyphus/plans/compress-service-unify.md) | v0.4.0 |
| 发布 Release | [release-automation.md](.sisyphus/plans/release-automation.md) | v0.4.0 |
| 返回上级目录 (.. 导航行) | [parent-directory-entry.md](.sisyphus/plans/parent-directory-entry.md) | v0.4.0 |
| ZIP 压缩流直拷优化 (ZipBinaryRewriter) | [zip-copy-mode-optimization.md](.sisyphus/plans/zip-copy-mode-optimization.md) | v0.4.2 |
| UAC 提权 + 权限不足处理 | [uac-elevation-permission.md](.sisyphus/plans/uac-elevation-permission.md) | v0.4.2 |
| 自包含安装包发布 | [self-contained-installer.md](.sisyphus/plans/self-contained-installer.md) | v0.4.2 |
