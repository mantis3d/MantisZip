# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF → Avalonia 迁移中)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF → Avalonia 迁移中 + SharpCompress + SharpSevenZip

## 版本
- **当前版本**: 0.4.5
- **发布日期**: 2026-07-06

## 规划中
- Avalonia 跨平台移植后续 Phase

### avalonia-port 分支 (WIP)
  - **预览子系统行为一致化** — 修复 GIF 魔数路由（`FileFormat.Gif` → `PreviewType.Gif` 而非 `PreviewType.Image`）；9 个元数据格式（PE/CSV/SQLite/ISO/Torrent/Office/Video/Audio/Font）隐藏空工具栏边框；`ShowUnsupported` 重置 `IsToolbarVisible = false` 避免残留；SVG `MemoryStream` 补 `using`；移除无 XAML 绑定的死代码 `IsTransparencySupported` (2026-07-15)
  - **预览图像 ZoomFit 自适应视口** — 新增 `ViewportWidth`/`ViewportHeight` 属性（`PreviewContentScroller.SizeChanged` 驱动）；`ZoomFit()` 改用实际视口尺寸替代硬编码 600×500；`ShowImage`/`ShowGif` 初始缩放统一调用 `ZoomFit()`；新增 `_isZoomFitActive` 标记：ZoomFit 开启时窗口缩放自动重新适应，手动缩放后不覆盖；图像显示改用 `Width`/`Height` + `Stretch="Uniform"` 替代 `ScaleTransform`（确保 ScrollViewer 滚动区与图像实际缩放后尺寸一致） (2026-07-15)
  - **ICO BMP 帧解码修复** — `DecodeBmpFrame` 中修正 DIB header 的 `biHeight` 为 `pixelHeight`（非原始双倍值），避免 SkiaSharp 解码时多读 `pixelHeight` 行垃圾数据导致小图标上方出现黑色方块；解码 XOR 像素后解析 AND 掩码设置透明像素（1 bit/pixel，bottom-up 存储，需 y-mirror），修复 BMP 帧透明度丢失 (2026-07-15)
  - **P0-3 魔数检测预览格式信息栏** — 魔数检测结果写入预览信息栏 `FormatMetadata`（`Insert(0, "格式")`）；扩展名冲突时显示 `⚠️ {name}（扩展名: .ext）`；匹配 WPF 行为（检测关闭时不显示、魔数失败时扩展名回退）(2026-07-15)
  - **P0-3 魔数检测预览修复** — `App.axaml.cs` 启动时初始化 `PreviewService.EnableFormatDetection`/`PreviewHeadSize`；`ShowPreviewAsync` 第 549 行改为先调用 `ClassifyPreviewByMagicAsync` 魔数检测，失败则回退 `ClassifyPreview(ext)` 扩展名判定；魔数检测结果影响预览类型路由（扩展名不再硬编码）；修复"格式"行累积 bug — 插入前先反向遍历移除旧 `Key == "格式"` 项 (2026-07-15)
  - **P0-2 压缩选项增强** — `AppSettings` 新增 10 个压缩属性（ZipEncoding/SevenZipCompressionMethod/SevenZipSolid/SevenZipSolidBlockSize/SevenZipDictionarySize/SevenZipNumFastBytes/SevenZipMatchFinder/ZipCompressionMethod/ZipEncryptionMethod/SevenZipEncryptHeaders）；`DynamicFormatOptionsPanel.LoadDefaults()` 从硬编码索引改为读取 AppSettings 实际值；`SettingsWindowViewModel` 新增 10 个 `[ObservableProperty]` + 8 个 ComboBox Option 集合；SettingsWindow 压缩 Tab 新增「高级选项」区域 10 个控件 (2026-07-15)
  - **P0-2 压缩选项增强(二)** — SettingsWindow 压缩选项拆为 ZIP/7z 两个独立 Border 分组；DynamicFormatOptionsPanel 移除 `DataContext = self` 修复格式切换不联动；
    DynamicFormatOptionsPanel 补齐 WPF 缺失的控件（ZIP 压缩方法、7z 固实块大小/字典大小/单词大小/匹配器）；文本与 WPF `CompressOpt_*` 完全对齐（en/zh-CN）；
    新建 `Services/CompressionOptionData.cs` 共享数据类，SettingsWindowViewModel / DynamicFormatOptionsPanel / CompressSettingsViewModel 统一从此读取选项列表，消除 SettingsWindow 与压缩面板之间的选项不一致（固实块大小 WPF 的 10 项 vs 旧版 5 项等）；
    CompressSettingsWindow 格式 ComboBox 补充 7z 选项、集成 DynamicFormatOptionsPanel、打开时从 AppSettings 加载默认值 (2026-07-15)
  - **ICO 预览迁移** — 新建 `Services/IcoParser.cs` 解析 ICO 文件提取所有帧（PNG 直接解码，BMP 剥离 AND 掩码后用 SkiaSharp 解码，修复 biHeight 翻倍惯例导致的黑色半截问题）；PreviewViewModel 新增 `ShowIcoGallery()` / `IcoFrames` / `ToggleIcoFlattenAlpha`；PreviewPanel 新增 ItemsControl + WrapPanel 画廊布局，每帧显示图标+尺寸标签，工具栏 🎨 去透明切换 (2026-07-15)
  - **CSV/SQLite 预览 DataGrid 修复** — Avalonia DataGrid `AutoGenerateColumns` 不兼容 `DataView`（已知 Issue #27），改为手动 `SetupDataGridColumns` 创建列绑定 `Row.ItemArray[i]`；修复水平滚动条缺失（移除外层 ScrollViewer）、列标题不刷新（监听 CsvData/SqliteTableData）、添加网格线；BT 种子文件列表改为目录树结构 (`TorrentTreeNode` + `TreeDataTemplate`)；预览标题使用种子/字体内部名称 (2026-07-09)
  - **FontParser 优先显示中文名称** — 字体 name 表解析新增 `lid`（language ID）追踪；`ShouldReplaceNameEntry` 同平台下优先取简体中文（lid=0x0804），中文字体标题栏显示中文名 (2026-07-09)
  - **全局界面字体设置 + 文本预览字体隔离** — 设置窗口外观 Tab 新增"全局界面字体" ComboBox（枚举系统字体）；`AppSettings.AppFontFamily` 持久化；`App.axaml` 添加 Window 级 `FontFamily="{DynamicResource AppGlobalFont}"` 样式；`ApplyAppFontFamily()` 在启动和保存设置后刷新资源 + 迭代已打开窗口应用（特定字体设本地值，默认字体清本地值避免 hover 回退）；文本预览 TextBox 改为绑定 `TextPreviewFontFamily` 而非继承的 `FontFamily`，避免被全局字体覆盖；文本预览字号调节（A+/A−）即时持久化到 `AppSettings.TextPreviewFontSize`；新增中英文键 `Settings_Preview_FontDefault` / `Settings_Appearance_AppFontFamily` (2026-07-06)
  - **WPF 字体预览重构** — 替换 GDI+ 位图渲染为 WPF 原生 GlyphTypeface + DrawGlyphRun → RenderTargetBitmap（DirectWrite 管道）；新增 CFF-OTF 检测跳过 unsafe FontFamily 避免原生崩溃；新增 CJK 字形检测自动过滤不支持的样本文字；Avalonia 端新增 SkiaSharp 字体位图渲染 + CJK 过滤 + 回退 TextBlock (2026-07-04)
  - **Avalonia 字体预览性能优化** — 合并折行和测量为一遍（`List<(string, float)>`，消除重复 `MeasureText`）；缓存字体 bytes + 主题色到内存供 `ReRenderFontPreview` 复用，避免每次重新读文件 + `AppSettings.Load()` 的 JSON I/O；SKBitmap → WriteableBitmap 直接 `Marshal.Copy` 像素内存，跳过 PNG 编解码往返（`SKImage.Encode` → `new Bitmap(stream)`）(2026-07-05)
  - **Avalonia 字体预览自动换行 + 窗口缩放响应** — `FontPreviewWrapWidth` 属性驱动 SkiaSharp 折行宽度；`x:Name="FontPreviewScrollViewer"` 绑定 `ScrollViewer.Bounds.Width`；`SizeChanged` 200ms 防抖 + `ReRenderFontPreview()` 窗口缩放后自动刷新位图 (2026-07-05)
  - **修复 PreviewPanel DataContextChanged 事件订阅泄漏** — 解构匿名 lambda 为命名方法，DataContext 变更时先 `-=` 旧 VM 的 `PropertyChanged` 再 `+=` 新 VM；`SizeChanged` 提取为独立命名方法只订阅一次 (2026-07-05)
  - **Phase 10: WPF 功能补齐（进度条/信息面板/状态栏）** — 状态栏增强（DirStats 目录文件计数/FilterStats 过滤统计/EncodingInfo 编码信息→6 列布局）；预览信息面板（文件元数据侧栏 + 横向/纵向位置切换 AppSettings.InfoPanelOrientation）；文件列表进度条 DataGridTemplateColumn（Size/CompressedSize/Modified 背景 Rectangle 色条 + CompressionRatio 列），RatioToWidthConverter/BrushResourceConverter，8 色主题资源（亮/暗），视图菜单开关（进度条/目录独立基准），i18n 中英文键 (2026-07-01)
  - **进度条 XAML 模板列补齐** — Size/CompressedSize/Modified/CompressionRatio 四列从 DataGridTextColumn 改为 DataGridTemplateColumn（Rectangle 背景色条 + MultiBinding RatioToWidthConverter）；视图菜单添加进度条/目录独立基准/信息面板方向三项开关；ArchiveItemModel 新增 RatioDisplay/RatioSort 属性 (2026-07-05)
  - **信息面板修复** — 默认方向改为 Vertical（下方）；"详细信息"移到上方、"基本信息"移到底部；大小/压缩后/压缩率一行三列；底部加间距避免被状态栏遮挡 (2026-07-02)
  - **P0 元数据字段补齐** — ShowImage 新增 DPI；ShowAudio 新增 BitDepth；ShowOffice 新增 ModifiedDate；ShowTorrent 新增 CreationDate/TrackerCount/IsPrivate/AdditionalInfo (2026-07-02)
  - **ExtractSettingsWindow + CompressSettingsWindow GroupBox 重构** — ExtractSettingsWindow: Separator → 3 Border GroupBox（源文件/目标目录/文件冲突），窗口 530 CanResize=False；CompressSettingsWindow: 3 TabItem 加 compactTab，General tab 顺序 WPF 一致（源文件列表带 AddFile/AddFolder/Remove 按钮 + 压缩选项合并组），Password/Comment tab 同样 Border GroupBox 分组；ViewModel SelectedPaths 改为 ObservableCollection 支持增删；新增 i18n 键 (2026-06-30)
  - **SettingsWindow Tab 紧凑样式 + DonationDialog 修复** — SettingsWindow Tab 标题改用全局 `TabItem.compactTab` class selector（FontSize=18, MinHeight=36），emoji FontSize=16；子 tab 统一应用 compactTab 样式；窗口 720×560；DonationDialog 修复 `avares://DonateQr.png` 找不到崩溃（csproj 加 `<AvaloniaResource>`），两按钮 Width=340 与二维码宽度对齐 (2026-06-30)
  - **Phase 10 计划 + 测试菜单** — 创建 `avalonia-phase10-feature-parity.md` 规划 WPF 功能补齐（文件列表进度条/预览信息面板/状态栏增强）；主菜单新增 🧪 测试菜单，内含 16 个可独立打开的对话框/窗口（含默认测试数据），i18n 中英文键，构建零错误 (2026-06-30)
  - **Phase 9: 文件列表交互补齐** — DataGrid 添加双击目录进入、Enter/Backspace/Delete 键盘导航、列排序（`..` 置顶 + 目录优先 + 箭头标记），与 WPF 文件列表交互行为保持一致 (2026-06-21)
  - **Bugfix: 筛选工具栏尺寸输入框白边 + 空值红框** — 添加 `NullableLongConverter` 处理空字符串→null 绑定；尺寸 TextBox 加 `Padding="2,0"` `BorderThickness="1"` 消除白边遮挡数字 (2026-06-22)
  - **Phase 8: 设置窗口 TabControl 重构 + i18n 补全 + ComboBox 修复** — SettingsWindow 重构为完整 TabControl（压缩/解压/上下文菜单/高级/预览），Preview 分 4 子标签页（文本/字体/表格/布局）；新增 70+ i18n 中英文键；AppSettings 扩展 shadow 配置属性；LocalizationManager `T()` 添加 null-safe fallback；修复 Avalonia 12 不支持 `SelectedValuePath` 导致的 ComboBox 选择不生效，改用 `ItemsSource` + `SelectedItem` + `Option(Display,Value)` 模式 (2026-06-21)
  - **Phase 7: 功能补齐 — CLI/IPC/设置标签页/对话框/i18n** — CLI 9 命令 + IPC 多实例; 设置窗口 Extract/ContextMenu/Advanced 三标签页; 10 个新对话框 (CompressConflictDialog/ConflictDialog/ErrorDialog/PasswordEditDialog/PasswordHelpDialog/LogPrivacyHelpDialog/MatchedPasswordDialog/DonationDialog); CompressSettingsWindow Password 标签增强（库模式/新密码模式/强度指示/自动规则）; i18n 中英文全键 (2026-06-19)
  - **Bugfix: 暗色菜单弹出面板白色背景 + 控制前景色修复** — 添加 `MenuFlyoutPresenterBackground` 修复菜单弹出面板背景；覆盖 `MenuFlyoutItemForeground`/`MenuFlyoutItemForegroundPointerOver`/`MenuFlyoutItemForegroundDisabled`/`ButtonForegroundDisabled`/`TabItemHeaderForegroundUnselected`/`TabItemHeaderForegroundSelected`/`ComboBoxForeground`/`ComboBoxItemForeground`/`ComboBoxItemForegroundPointerOver`/`ComboBoxItemForegroundSelected`/`CheckBoxForegroundUnchecked`/`CheckBoxForegroundChecked` 等 Fluent 资源键及 App.axaml TabItem 样式、SettingsWindow Foreground
  - **Bugfix: SQLite 预览文件锁定** — SqliteConnection 加 `Pooling=False`，防止连接池在 Dispose 后仍持文件句柄导致重新预览失败
  - **Bugfix: 按钮悬停黑白色** — FluentTheme ControlTheme 用黑白资源覆盖按钮 ContentPresenter 的 `:pointerover`/`:pressed` 背景，已添加 14 个 Fluent 资源覆盖至 ThemeLight/ThemeDark
  - **Phase 6: 样式统一与视觉打磨** — 全局控件 CornerRadius 6px + Transitions (0.15s) + TextBox/ComboBox 焦点高亮 + Dialog Padding 统一 16
  - **Phase 5: 工具栏按钮样式重构** — Button/ToggleButton 统一样式类，消除重复属性实例，按钮高度 42→54
  - **Phase 4: App.axaml 统一控件样式** — 移除 WPF 风格样式，适配 Avalonia 原生样式系统 (2026-06-15)

## 版本历史（从新到旧）

### v0.4.5+ (2026-07-13) 可配置双击行为 + 解压后自动删除原压缩包

1. **可配置双击行为** — SettingsWindow 文件关联 Tab 新增「双击压缩包」GroupBox + ComboBox（打开/原地解压/智能原地解压/打开解压窗口）
   - `DoubleClickAction` 设置持久化到 settings.json，默认 `"open"` 保持现有行为
   - `App.xaml.cs` 的 `--open` 分发改为按 `DoubleClickAction` 路由到 `HandleOpen`/`HandleExtractHere`/`HandleExtractSmart`/`HandleExtract`
2. **解压后自动删除原压缩包** — SettingsWindow 解压 Tab 新增「解压完成后将原压缩包移到回收站」CheckBox
   - `DeleteArchiveAfterExtract` 设置持久化到 settings.json，默认关闭
   - 所有解压成功路径（批量/单文件 CLI + MainWindow 界面解压）完成后调用 `TryDeleteArchiveAfterExtract`
   - 使用 `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` 移到回收站
   - 重试 3 次（200ms 间隔）应对 7z.dll 句柄延迟释放
   - 预览/双击打开/拖拽提取不触发删除
3. **修复文件占用导致删除失败** — `HasEncryptedEntries` 和 `QuickVerifyPassword` 的 ZIP 分支改用 `FileShare.Read | FileShare.Delete` 打开文件流，避免 SharpCompress 内部默认 `FileShare.Read` 句柄阻止 Shell 回收站操作
4. **解压后智能打开目标目录** — 全部 4 个 `OpenFolderAfterExtract` 调用点增加公共根目录检测：如果压缩包内所有条目共享同一根目录（如 `my_project/a.txt`、`my_project/b.txt`），打开 `dest/my_project/` 而非 `dest/`，减少一次手动点进目录的操作
   - 新增 `GetCommonRootDirectory` + `ResolveSmartOpenPathAsync` 方法
5. **修改文件**：`AppSettings.cs`、`strings.zh.json`、`strings.en.json`、`SettingsWindow.xaml`、`SettingsWindow.xaml.cs`、`App.xaml.cs`、`App.Extract.cs`、`App.Password.cs`、`MainWindow.xaml.cs`、`MainWindow.Menu.cs`

### v0.4.5++ (2026-07-14) 便携版模式

1. **便携模式** — 完整实现 `portable-mode.md`
   - `AppSettings.cs`：`Portable.txt` 哨兵检测 → `IsPortableMode`，settings 和 passwords 文件重定向到 `exe 目录/Data/`，`Save()` 跳过 `SyncContextMenuToRegistry()`
   - `PasswordManager.cs`：`CustomDataDir` 注入 + `GetPasswordPath()` 动态路径
   - `App.xaml.cs`（OnStartup）：便携模式下跳过 FirstRunShell/FirstRunAssoc 注册；`--install-shell`/`--uninstall-shell`/`--install-assoc`/`--uninstall-assoc` 报错退出
   - `MainWindow.Preview.cs`：`GetTempDir()` 便携版返回 `Data/Temp/`
   - `MainWindow.DragDrop.cs`：拖拽临时目录使用 `GetTempDir() + "DragDrop/"`
   - `SevenZipEngine.cs`：`ResolveDefaultSevenZipDllPath()` 首候选 `{BaseDir}/7z.dll`
   - `App.OnExit`：清理指向 `Data/Temp/` 而非系统 `%TEMP%`
2. **修改文件（6 个）**：`AppSettings.cs`、`PasswordManager.cs`、`App.xaml.cs`、`MainWindow.Preview.cs`、`MainWindow.DragDrop.cs`、`SevenZipEngine.cs`

### v0.4.5++ (2026-07-14) 文件过滤预设显示 + 过滤统计文本始终显示

1. **修复预设 ComboBox 显示类型名** — `FileFilterPreset` 添加 `ToString()` 返回 `Name`，确保预设名称正确显示
2. **过滤统计文本始终显示** — 去掉 `FilterStatsText` 的 `Collapsed`/`Visible` 切换，始终占位避免 UI 跳动
   - `ShowFilterStats` 改为空方法（保持兼容）
   - 未启用过滤时显示空文本
3. **修改文件**：`FileFilterPreset.cs`、`FileFilterEditor.xaml`、`FileFilterEditor.xaml.cs`、`ExtractSettingsWindow.xaml.cs`

### v0.4.5++ (2026-07-14) 文件冲突对话框添加暂停/取消按钮

1. **暂停/取消功能** — CompressConflictDialog 和 ConflictDialog 各添加两个底部按钮
   - **暂停**：收起对话框 → 进度窗口进入暂停状态（后台 `ManualResetEventSlim` 等待）→ 进度窗口继续时重新弹出冲突对话框
   - **取消**：通过 conflictResolver 抛出 `OperationCanceledException` 终止整个压缩/解压操作，等同于进度条上的取消
   - 暂停按钮图标（⏸/✕）仅存在于本地化字符串，XAML 不再硬编码（修复图标重复 bug）
2. **循环重入改造** — 所有调用方 conflictResolver 支持暂停/取消循环：
   - `App.xaml.cs` — `CreateExtractOptions()` 解压循环
   - `App.Compress.cs` — 批量压缩循环
   - `CompressSettingsWindow.xaml.cs` — `RunCompressAsync` / `RunSeparateCompressAsync` / `RunCombinedCompressAsync` 3 处循环
3. **暂停状态控制** — `ProgressWindow.PauseFromConflict()` 打开 `_pauseEvent`（后台 `Wait(ct)`）→ 恢复时关闭 `_pauseEvent` 并重新调用 conflictResolver
4. **修改文件**：`App.xaml.cs`、`App.Compress.cs`、`CompressConflictDialog.xaml`、`CompressConflictDialog.xaml.cs`、`CompressSettingsWindow.xaml.cs`、`ConflictDialog.xaml`、`ConflictDialog.xaml.cs`、`ProgressWindow.xaml.cs`、`strings.zh.json`、`strings.en.json`

### v0.4.4+++ (2026-07-10) 视图菜单添加"隐藏预览信息"开关

1. **预览信息面板独立显隐控制** — 在视图菜单新增 `IsCheckable` 菜单项"隐藏预览信息(_I)"
   - 关闭后：`PreviewInfoPanel`（文件名、大小、压缩比、日期）和 `PreviewExtraInfoPanel`（格式元数据）同时隐藏，预览内容区独占空间
   - 两级控制：`PreviewToggleMenu` 控制预览整体开关，`PreviewInfoToggleMenu` 控制信息面板开关
   - 状态持久化到 `settings.json`（`ShowPreviewInfoPanel`）
2. **修改文件**：`AppSettings.cs`、`strings.zh.json`、`strings.en.json`、`L.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`MainWindow.Menu.cs`、`MainWindow.Preview.cs`

### v0.4.4+ (2026-07-09) 移除 Applications shell\open\command 防止安装时错误路由

1. **移除 `Applications\MantisZip.UI.exe\shell\open\command` 注册** — 避免新软件安装时 Windows Shell 关联刷新将 exe 打开操作错误地路由到 MantisZip
   - `SupportedTypes` 保留，不影响"打开方式"的展示
   - 右键菜单（COM handler）、双击走 per-format ProgId 均不受影响
2. **修改文件**：`src/MantisZip.UI/Shell/ShellIntegration.Assoc.cs`（删 1 行 + 注释）

### v0.4.4++ (2026-07-07) 修复 COM handler 动词名"open"与系统标准动词冲突

1. **COM handler 动词名冲突修复** — COM 右键菜单的 GCS_VERB 返回动词名与系统标准 "open" 重名，
   导致新软件安装时 Windows Shell 关联刷新可能误将 MantisZip 的"打开"动词当作 exe 的默认打开程序
   - `GetCommandString` GCS_VERB: `"open"` → `"mantiszipopen"`
   - `ResolveCommandId` 字符串映射: `"open" => CmdIdOpen` → `"mantiszipopen" => CmdIdOpen`
   - 正常右键菜单操作不受影响（走整数偏移路径，不经动词名）
   - 不影响 SFX 自解压文件的打开支持
2. **修改文件**：`src/MantisZip.ShellExt/ContextMenuHandler.cs`（改 2 行）

### v0.4.4 (2026-07-07) COM 动态菜单 + pending 状态 + 延迟级联安装

1. **COM 动态菜单组件** — `MantisZip.ShellExt` 实现 `IShellExtInit` + `IContextMenu` 作为 COM 组件
   - 动态菜单文本（根据文件名生成「解压到 {name}」「压缩到 {name}.zip」）
   - 纯 Win32 图标加载（无 `System.Drawing` 依赖）
   - 多选文件数量显示（「打开压缩包 等 {N} 个文件」）
   - 本地化菜单文本（通过注册表 + AppSettings 的 `L.T()`）
   - 子母菜单模式（cascade/verb 两种注册方式）
   - 单个菜单项开关（8 个独立 toggle）
2. **COM + 延迟级联安装流程** — Install 仅注册 COM，级联菜单在检测到 COM 未加载时自动安装
   - COM 注册成功 → 状态 `pending`（仅 COM shellex，无 cascade）
   - COM DLL 不存在 → 状态 `disabled`（安装 cascade 兜底）
   - 启动时 `CheckComStatus()` 扫 Explorer 模块 → 未加载 comhost.dll 则安装 cascade
   - Settings 安装按钮和首次运行流程均立即调用 CheckComStatus，确保菜单立即可用
   - 避免安装时同时注册两个菜单导致右键出现重复菜单
3. **动态菜单状态跟踪** — `DynamicMenuStatus`（Active/Pending/Fallback/Disabled）
   - `CheckComStatus()` 找到 comhost.dll → 升级 `active` + `UninstallStaticMenus()` 清理级联
   - 未找到 → 安装 cascade 兜底，状态保持 `pending`
   - 状态文字：pending→"动态菜单加载失败，暂时回退到静态菜单"、active→"动态菜单已启用"
4. **ShellExt 进程名日志** — `ContextMenuHandler` 构造函数记录宿主进程名（Explorer.exe 或其它）
5. **移除废弃代码**：`TestComInExplorerContext()`、`TestComActivation()`、三级回退分支
6. **修改文件**:
   - `src/MantisZip.ShellExt/ContextMenuHandler.cs` (+8 行进程名日志)
   - `src/MantisZip.UI/Shell/ShellIntegration.Menu.cs` (+/-：Install 延迟级联、CheckComStatus、UninstallStaticMenus、删除 TestComActivation/TestComInExplorerContext)
   - `src/MantisZip.UI/Shell/ShellIntegration.cs` (新增 DynamicMenuStatus_Pending)
7. **pending 态 COM 菜单占位符** — COM handler 检测到 pending 状态时插入灰色禁用分隔符 `"────────"` 而不是隐藏或显示完整菜单
   - 避免初次右键时 COM 菜单 + 级联同时出现的重复问题
   - 不自晋升、不写注册表、不卸级联——完全在 COM handler 内封闭
   - 新增 `GetDynamicMenuStatus()` 读取 `HKCU\Software\MantisZip\ContextMenu\DynamicMenuStatus`
   - `src/MantisZip.ShellExt/ContextMenuHandler.cs`
8. **安装包 .NET 9 检测修复** — `IsDotNet9Installed` 无法检测已安装的 .NET 9 Desktop Runtime
   - 根因：.NET 9 把版本号存为注册表**值名称**（DWORD）而非子键，`RegGetSubkeyNames` 永远找不到
   - 修复：增加文件系统回退检测 `cmd /c dir ...\9.*`
   - 同时也修复了 `IsWebView2Installed` 缺少 HKLM (32-bit 视图) 回退的问题
   - `installer.iss`
   - `src/MantisZip.UI/App.xaml.cs` (启动时调用 CheckComStatus)
   - `src/MantisZip.UI/Dialogs/SettingsWindow.xaml.cs` (UpdateShellStatus pending 分支 + InstallBtn 调用 CheckComStatus)
   - `src/MantisZip.UI/Localization/L.cs` (新增 Settings_Menu_StatusDynamicPending)
   - `src/MantisZip.UI/Resources/strings.zh.json` / `strings.en.json` (pending 状态文本)

### v0.4.4+ (2026-07-08) AddToArchiveAsync 加密条目预检 — CI 环境下预期异常修复

1. **修复 CI 测试失败** `AddToArchiveAsync_CopyMode_ThrowsOnEncryptedSource`：该测试依赖 SharpCompress 在解压加密条目时抛出 `CryptographicException`，但此行为随环境和版本变化，CI 环境下不会抛出。
2. **ZipEngine.AddToArchiveAsync 旧路径**：
   - 传入 `options.Password` 给 `OpenArchiveWithEncodingFallback`，使带密码时能正确解压加密条目。
   - 新增显式预检：遍历 `archive.Entries` 检查是否有非目录加密条目但未提供密码 → 提前抛出 `InvalidOperationException`。
3. **测试更新**：改为预期 `InvalidOperationException`（确定性异常，不依赖 SharpCompress 的环境特定行为）。移除未使用的 `using SharpCompress.Common`。
4. **修改文件**：`src/MantisZip.Core/Engines/ZipEngine.cs`、`tests/MantisZip.Tests/Utils/ZipBinaryRewriterTests.cs`

### v0.4.4+ (2026-07-03) 双击文件默认程序打开 + 上级目录预览刷新修复

1. **新功能：双击文件调用系统默认程序打开** — 在 `FileListGrid_PreviewMouseDoubleClick` 中添加文件双击处理分支：
   - `AppSettings.DoubleClickOpenThreshold` 设置阈值（MB 为单位，默认 10MB，0=禁用），在设置窗口解压缩 Tab 末尾配置
   - 超过阈值时弹出确认对话框："文件超过 X MB，确定要解压并打开吗？"
   - 文件 >= 1MB 时显示 ProgressWindow 显示提取进度，< 1MB 则静默提取
   - 提取到 `%TEMP%\MantisZip\OpenWith\{GUID}\` 后通过 `Process.Start(UseShellExecute=true)` 调用系统默认程序打开
   - Tar/GZip/ISO 不支持单文件提取，弹出"该格式不支持双击打开"
   - 加密未输入密码时提示"请先输入密码"
   - 状态栏更新为"已用默认程序打开 {文件名}"
   - Temp 文件随 App.OnExit 自动清理
2. **修复 Bug：上级目录（..）选中时预览面板不刷新** — 移除 `FileListGrid_SelectionChanged` 中的 `!lastClicked.IsNavigationEntry` 守卫条件
3. **修改文件（7 个）**：`AppSettings.cs`（+2 行）、`SettingsWindow.xaml`（+17 行）、`SettingsWindow.xaml.cs`（+11 行）、`L.cs`（+6 行）、`MainWindow.UI.cs`（+112 行）、`strings.zh.json`（+6 行）、`strings.en.json`（+6 行）

### v0.4.4 (2026-07-03) 密码流程统一 + QuickVerify 7z 扩展

1. **QuickVerifyPassword 扩展支持 7z EncryptHeaders=false** — 新增 `BoundedWriteStream`（写入 ~8KB 后静默丢弃），提取最小加密条目验证密码。所有格式的 QuickVerify 现在都可靠。
2. **删除 `CanTrustQuickVerify`** — QuickVerify 对所有格式可靠，不再需要此区分。
3. **`ResolvePasswordAsync` 统一密码入口** — 新增 `PasswordResult` 类 + 统一方法，涵盖：检查加密 → TryMatchPassword → 对话框循环。所有调用方通过同一入口获取密码。
4. **调用方大幅简化**：
   - `LoadArchiveAsync`：~100 行分支逻辑 → 2 个 ResolvePasswordAsync 调用
   - `ExtractAsync`：~50 行 TryMatchPassword+ExtractWithPasswordAsync → ResolvePasswordAsync
   - `RunExtractStatic`：~60 行 → ResolvePasswordAsync
   - `HandleExtractBatchCore`：~40 行 → ResolvePasswordAsync
5. **删除 `ExtractWithPasswordAsync`** — 不再使用。
6. **修复**：密码输入框"取消"后再次弹出陷入循环（加 `userCancelled` 标志区分取消和密码错误）。
7. **修改文件**: `App.Password.cs`（+294/-162 行）、`MainWindow.xaml.cs`（+111/-285 行）、`App.Extract.cs`（+32/-139 行）
8. **新增文件**: `.sisyphus/plans/password-flow-unification.md`
9. **installer-selfcontained.iss 完全离线安装包** — 移除 WebView2 在线下载逻辑，改为本地捆绑 Evergreen Standalone Installer
   - 新增 `WebView2-LICENSE.txt` 微软再分发许可声明
   - 新增 `installer\download-redist.ps1` 预下载脚本
   - 新增 `installer\redist\MicrosoftEdgeWebView2RuntimeInstallerX64.exe`（从微软官方下载）
   - 删除 `URLDownloadToFile` 函数和 `EvergreenBootstrapperUrl` 常量
10. **installer.iss 下载进度可视化 + WebSetup 修复**：
    - 用 `TDownloadWizardPage` 替代 `URLDownloadToFile` 静默下载，用户可见 .NET/WebView2 下载进度条
    - 修复 `Type Mismatch` 运行时错误：补全 `Show/try-except/finally/Hide` 生命周期和 `nil` 回调参数
    - 修复 32 位安装程序在 64 位系统上检测不到已安装运行时：`HKLM` → `HKLM64`（避免 WOW6432Node 重定向）
    - 修复 .NET 安装后立即启动 MantisZip 触发 Windows 下载提示：安装成功后在 `CurStepChanged` 追加 3 秒 `Sleep` 等待注册完成
11. **installer-selfcontained.iss 同步更新**：移除 `deps.json`/`runtimeconfig.json` 引用（自包含发布不生成）

### v0.4.4+ (2026-07-02) 压缩包路径处理一站式重构——ArchivePath 统一入口

1. **新建 `ArchivePath` 类** — `ArchivePathExtensions.cs` → `ArchivePath`，压缩包路径处理的一站式入口
   - `Normalize()`：`\` → `/` 统一分隔符，null 安全
   - `TrimEndSeparator()`：去除尾部斜杠（保留根路径 `C:\`）
   - `GetFileName()` / `GetDirectoryName()` / `GetFileNameWithoutExtension()`：自动处理尾部斜杠，无需调用方手动 TrimEnd
   - `GetFileNameWithoutExtension()` 特殊处理 `.tar.gz` 双扩展名，与 `ArchiveEngine.GetFormatByExtension` 保持一致
   - `FindEntry()`：按归一化路径在条目集合中查找
2. **消除 4 种遗留路径处理模式**：
   - 去除 29 处内联 `.Replace('\\', '/')` → `ArchivePath.Normalize()`
   - 去除 16 处 `.TrimEnd('\\', '/')` → `ArchivePath.GetFileName`/`GetDirectoryName`/`GetFileNameWithoutExtension`/`TrimEndSeparator`
   - 消除 `NormalizePathSeparators()` 扩展方法
   - `ContextMenuHandler.cs` 保留 2 处（ShellExt 不引用 Core）
3. **修改文件（11 个）**：`ArchivePathExtensions.cs`（新建）、`ZipEngine.cs`、`SevenZipEngine.cs`、`ArchiveEntryExtractor.cs`、`ArchiveStructureAnalyzer.cs`、`FileConflictHelper.cs`、`MainWindow.DragDrop.cs`、`App.Compress.cs`、`App.Open.cs`、`CompressSettingsWindow.xaml.cs`、`CompressSettingsWindow.Password.cs`

### v0.4.4 (2026-07-01) NoDotNet 安装包增强——.NET 9 自动下载
### v0.4.5+ (2026-07-03) 压缩选项增强——7z 字典/块/匹配器 + ZIP 方法 + 加密方式

1. **压缩选项增强计划** — `.sisyphus/plans/compression-options-enhancement.md`
2. **AppSettings 新增 7 个默认属性**：`SevenZipSolidBlockSize`、`SevenZipDictionarySize`、`SevenZipNumFastBytes`、`SevenZipMatchFinder`、`ZipCompressionMethod`、`ZipEncryptionMethod`、`SevenZipEncryptHeaders`
3. **ArchiveOptions 新增对应属性** — `ArchiveEngine.cs` 添加 7 个属性 + 默认值 + XML 文档
4. **CompressRequest + BuildOptions** — `CompressService.cs` 添加 7 个 init 属性 + `BuildOptions` 传递
5. **DynamicFormatOptionsPanel** — 7z 面板新增 4 个 ComboBox（固实块大小、字典大小、Word Size、匹配器）+ 固实联动禁用；ZIP 面板新增"压缩方法"ComboBox（Deflate/Deflate64/BZip2/LZMA/PPMd/Store）
6. **SettingsWindow** — 压缩 Tab 新增 6 个 ComboBox + 1 个 CheckBox 设置默认值；已在 `LoadSettings`/`SaveSettings` 中添加对应逻辑
7. **CompressSettingsWindow** — 加密 Tab 新增"加密方式"GroupBox（ZIP 加密方式 ComboBox + 7z 加密文件头 CheckBox）
8. **SevenZipEngine** — `ConfigureCompressor` 应用新参数（CustomParameters `s`、LzmaDictionarySize、LzmaNumFastBytes、LzmaMatchFinder、EncryptHeaders）
9. **ZipEngine** — 加密路径使用 SharpSevenZip `CompressionMethod` + `ZipEncryptionMethod`；非加密路径使用 SharpCompress `CompressionType`；支持 Deflate64/BZip2/LZMA/PPMd/Store
10. **修改文件（12 个）**：`AppSettings.cs`、`ArchiveEngine.cs`、`CompressService.cs`、`DynamicFormatOptionsPanel.xaml`、`DynamicFormatOptionsPanel.xaml.cs`、`SettingsWindow.xaml`、`SettingsWindow.xaml.cs`、`CompressSettingsWindow.xaml`、`CompressSettingsWindow.xaml.cs`、`SevenZipEngine.cs`、`ZipEngine.cs`、`PROGRESS.md`
11. **后续修复**：Word Size 中文标签、匹配器/字典/固实块"默认"改为带数值显示（默认(273)/默认(BT4)/默认(16MB)/默认(全固实)）、固实块选项扩展到 10 个（16MB~4GB）、`SolidCheck_Changed` null 保护

### v0.4.4+ (2026-07-02) 压缩包路径处理一站式重构——ArchivePath 统一入口

1. **新建 `ArchivePath` 类** — `ArchivePathExtensions.cs` → `ArchivePath`，压缩包路径处理的一站式入口
   - `Normalize()`：`\` → `/` 统一分隔符，null 安全
   - `TrimEndSeparator()`：去除尾部斜杠（保留根路径 `C:\`）
   - `GetFileName()` / `GetDirectoryName()` / `GetFileNameWithoutExtension()`：自动处理尾部斜杠，无需调用方手动 TrimEnd
   - `GetFileNameWithoutExtension()` 特殊处理 `.tar.gz` 双扩展名，与 `ArchiveEngine.GetFormatByExtension` 保持一致
   - `FindEntry()`：按归一化路径在条目集合中查找
2. **消除 4 种遗留路径处理模式**：
   - 去除 29 处内联 `.Replace('\\', '/')` → `ArchivePath.Normalize()`
   - 去除 16 处 `.TrimEnd('\\', '/')` → `ArchivePath.GetFileName`/`GetDirectoryName`/`GetFileNameWithoutExtension`/`TrimEndSeparator`
   - 消除 `NormalizePathSeparators()` 扩展方法
   - `ContextMenuHandler.cs` 保留 2 处（ShellExt 不引用 Core）
3. **修改文件（11 个）**：`ArchivePathExtensions.cs`（新建）、`ZipEngine.cs`、`SevenZipEngine.cs`、`ArchiveEntryExtractor.cs`、`ArchiveStructureAnalyzer.cs`、`FileConflictHelper.cs`、`MainWindow.DragDrop.cs`、`App.Compress.cs`、`App.Open.cs`、`CompressSettingsWindow.xaml.cs`、`CompressSettingsWindow.Password.cs`


### v0.4.3+ (2026-07-01) NoDotNet 安装包增强——.NET 9 自动下载

1. **installer.iss 新增 .NET 9 Desktop Runtime 自动检测 + 下载安装** — 安装时自动检测注册表 `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App`，缺失时从 `aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe` 下载并静默安装 `/quiet /install /norestart`。完全复用已有 WebView2 模式（`URLDownloadToFile` + `Exec`）。失败不阻塞安装（仅记日志）。
2. **安装包文件名重命名** — `installer.iss` 输出：`NoDotNet` → `WebSetup`（因现支持自动下载 .NET）；`installer-selfcontained.iss` 输出：`Setup` → `Offline`（自包含离线包）。感谢用户建议。
3. **贡献者鸣谢页面更新** — `AboutWindow.xaml` 新增财务贡献者显示区。由上一轮计划（contributors-panel）完成。
4. **文本子类型检测已关闭**：`DetectTextSubtype()` 启发式精度不足暂禁用，代码保留，`Detect()` 中改回返回 `FileFormat.Text`

### v0.4.4 (2026-06-30) 魔数检测预览系统 Phase 1+2+3 完成

1. **新功能：魔数检测文件真实格式** — `preview-magic-detection.md`（全部 44 项任务完成）
   - **Phase 1 — Core**：`FileFormatDetector`（35+ 魔数签名 + ZIP 子类型 + PE 双重验证）
   - `LooksLikeText()` 启发式检测：纯文本文件（无魔数签名）的兜底识别，基于 null 字节比例 + 可打印字符 + UTF-8 序列分析
   - `ExtractHeadAsync`/`ExtractHeadTailAsync`：压缩包条目头部/尾部字节提取，支持 ZIP Deflate/Store、7z 固态降级、RAR
   - MP4 moov box 解析：mvhd 时长 + tkhd 分辨率
   - `FileFormatHelper`：90+ 格式中文显示名
   - `PreviewHeadSize` 设置（默认 4096）
   - **Phase 2 — UI（WPF）**：魔数优先路由重构（`TryMagicPreview`），将魔数检测结果写入 `PreviewExtraInfoPanel`（冲突时红色提示），扩展名回退仅作为魔数 Unknown 时的兜底
   - **冲突检测 + 切换按钮**：魔数检测结果与扩展名不一致时，在预览工具栏两组按钮之间插入"按扩展名/按魔数"切换按钮，点击后重新预览
   - `AppSettings.EnableFormatDetection` 开关（默认 true）
   - **Phase 3 — ArchiveEngineFactory 魔数兜底**：`GetEngineByExtension` 在扩展名未匹配时，读取文件头部字节调用 `FileFormatDetector.Detect()` 识别真实档案格式，支持 .epub/.docx/.xlsx/.pptx 等 ZIP 子类型自动路由到 ZipEngine
2. 新建文件：`FileFormatDetector.cs`（650+ 行）、`FileFormatHelper.cs`（95 行）
3. 修改文件：`FileFormatInfo.cs`（追加 11 枚举值）、`ArchiveEntryExtractor.cs`（+224 行）、`AppSettings.cs`、`ArchiveEngine.cs`（+60 行魔数兜底 + 映射）、`MainWindow.Preview.cs`（+180 行魔数路由 + 冲突切换）、`MainWindow.Preview.Text.cs`（文本左对齐修复）

### v0.4.3+ (2026-06-30) 工具栏新增「解压选择文件」按钮

1. **工具栏新增「解压选择文件」按钮** — 位于「解压」与「压缩」之间，行为与右键菜单「解压到…」一致：选中文件后弹窗选择目标目录再解压。图标 📑。按钮在加载压缩包后启用。
2. **右键菜单图标统一** — 「解压到…」图标从 📤 改为 📑，与工具栏按钮保持一致。

### v0.4.3+ (2026-06-30) 默认路径优先级设置

1. **AppSettings** — 新增 `DefaultPathPriority` 属性，支持 4 种策略：场景相关 / 资源管理器 / 最近使用 / 桌面
2. **ResolveDefaultPath() 静态方法** — 按优先级链自动选取最佳默认路径，链中非空即停
3. **设置 UI** — 🧰 高级标签页新增「默认路径优先级」GroupBox，4 个 RadioButton
4. **7 个 QuickPathPreDialog 调用点** — 全部接入 `ResolveDefaultPath`，弹窗不再是空路径开局

### v0.4.3+ (2026-06-29) 预览系统计划更新（Avalonia 方向 + 快速预览模式）

1. **新计划：Avalonia 预览机会分析** — `preview-avalonia-opportunities.md`
   - 分析 WPF→Avalonia 迁移对预览系统各格式的影响
   - 评估 PSD/AI/HDR 等新格式的预览方案
   - 提出 HDR 全景 360° 查看器方案（WebView2 + Three.js / Skia 自渲染两路线）
   - 音视频播放替代方案（LibVLCSharp）
   - **三级依赖隔离体系**：将 Magick.NET/LibVLC 等重大依赖拆分为可选插件，通过 `plugins/` 目录 + `AssemblyLoadContext` 加载，控制安装包体积
2. **新计划：快速预览与渐进式加载** — `preview-quick-modes.md`
   - 三种预览模式：⚡快速 / ▶渐进 / 📄完整
   - 28 个格式逐行分析每种模式下的行为
   - 后台 `ProgressiveLoadManager` 管理渐进加载与取消
   - 后续扩展：文件列表缩略图模式
3. **修正计划**：`preview-extended-formats.md` 更新——Phase 2D（Magick.NET）改为插件化方案，Phase 3.10/3.11（音视频）改为 LibVLC 插件，Phase 4 EXR/TIFF 由 Magick.NET 覆盖移除
4. **PLAN.md**：新增两条调研条目 + 快速预览 P2 条目

### v0.4.3 (2026-06-22) QuickPathControl 统一路径选择 + 资源管理器窗口检测 + 书签管理器 + 权限跳过

1. **QuickPathControl 统一路径选择（压缩/解压窗口）**：
   - CompressSettingsWindow：原有 OutputPathTextBox + BrowseOutputButton → QuickPathControl（文件保存模式），新增独立 FileNameTextBox 输入文件名
   - ExtractSettingsWindow：原有 ManualPathTextBox + BrowseButton → QuickPathControl（文件夹选择模式）
   - QuickPathControl 支持收藏夹 ⭐、历史记录 🕐、资源管理器窗口 🪟、浏览 📁
2. **资源管理器窗口检测修复**：
   - ExplorerWindowTracker 重写：COM IShellWindows（CLSID 直接创建）为主 + Win32 EnumWindows（CabinetWClass）兜底
   - 解决 Shell.Application.Windows() 在某些系统返回空列表的问题
3. **书签管理器菜单**：
   - 主菜单 工具 > 新增「书签管理器(_B)...」打开 FavoriteManagerWindow
   - 新增 Main_Menu_BookmarkManager 本地化键（中/英）
4. **布局修复**：
   - CompressSettingsWindow 内 Grid 从 5 行扩展至 7 行，消除 FormatOptionsPanel 与 VolumeSize 重叠
5. **压缩包内逐条目权限跳过**：
   - 新增 `ExtractResult` 类（`SucceededEntries`/`FailedEntries`）
   - `IArchiveEngine.ExtractAsync` 返回类型从 `Task` 改为 `Task<ExtractResult>`
   - ZipEngine/SevenZipEngine/TarGzEngine 逐条目循环包 `try-catch(UnauthorizedAccessException)`，跳过失败条目继续处理其余
   - GZip 单文件解压同样包 try-catch
   - 调用方（App.Extract.cs）根据 `ExtractResult` 判断全失败/部分成功
2. **UAC 提权弹窗修复**：
   - 删除 `HandleExtractBatchCore` 预检（事前扫描所有目标目录），完全由 catch 响应式拦截
   - 首次权限不足弹窗一次，后续静默跳过（标记 Failed），不退出进程
   - 已提权仍失败不退出（其他目录可能可写）
   - 仅用户点击「以管理员身份运行」时才重启旧进程
3. **设计文档**：`.sisyphus/plans/uac-elevation-permission.md` 更新
4. **进度窗口错误摘要**：ProgressWindow 新增 `ErrorSummaryBox`（可复制 TextBox），解压权限错误时在进度条和按钮之间显示错误摘要文本
5. **计划状态同步**：将 `zip-copy-mode-optimization.md`（v0.4.2）和 `uac-elevation-permission.md`（v0.4.2）从 PLAN.md 待实现移至 PROGRESS.md 历史设计方案索引，同步更新跨平台分析计数
6. **DynamicFormatOptionsPanel 后端接线**：ZIP 编码/7z 压缩方法/7z 固实选项从 UI 接入到压缩引擎：
   - `ArchiveOptions`/`CompressRequest` 新增 `FileNameEncoding`、`SevenZipCompressionMethod`、`SevenZipSolid` 属性
   - `ZipEngine`：根据 `FileNameEncoding` 选择 ZIP 文件名编码（utf-8/gbk/default）
   - `SevenZipEngine.ConfigureCompressor`：根据选项选择压缩方法（LZMA/LZMA2/PPMd/BZip2/Deflate），非固实时设 `CustomParameters["s"]="off"`
   - 修复 `FormatComboBox_SelectionChanged` 未同步 `FormatOptionsPanel.SelectedFormat` 导致面板不随格式切换的 bug
7. **默认格式选项设置**：设置窗口 → 压缩标签页新增「默认格式选项」区域：
   - AppSettings 新增 `ZipEncoding`、`SevenZipCompressionMethod`、`SevenZipSolid` 属性
   - SettingsWindow 读写持久化
   - `DynamicFormatOptionsPanel.LoadDefaults()` 打开压缩窗口时自动加载设置值
    - 快捷压缩路径（`--compress-quick`/`--compress-separate`/`--compress-combined`）读取 AppSettings 默认值
8. **RELEASE_NOTES.md 双语化**：将 v0.4.3 更新内容翻译为中英双语，格式与 v0.4.2/v0.4.1 保持一致

### v0.4.2 (2026-06-20) 安装程序主题/语言选择修复 / ZIP copy-mode 进度与取消

1. **修复安装时主题选择不生效的 Bug**：
   - `installer\prebuilt\settings.json` 缺少 `Theme` 和 `Language` 字段，导致 `CopyFile` 主路径下用户向导选择被丢弃
   - 在该文件中添加 `__LANG__` 和 `__THEME__` 占位符
   - 新增 `PatchSettingsThemeAndLanguage` 过程，`CopyFile` 成功后读取 JSON、替换占位符为用户实际选择的语言和主题
   - `installer.iss` + `installer-selfcontained.iss` 同步修改
2. **ZIP 添加/删除进度与取消优化**：
   - `CompressNewEntry`：从 3 遍（全读→CRC32→Deflate）改为单遍流式（80KB 块流式 CRC32 + Deflate），降低内存占用并支持逐块取消
   - `CopyStreamRangeAsync`：新增每 80KB 块粒度进度报告（FilePercentComplete 0→100 + 整体 PercentComplete 平滑插值），100ms 节流
   - 收尾阶段分步报告：写入中央目录 92% → 写入目录尾 94% → 刷入磁盘 97% → 原子替换 100%
   - Phase 1/2 条目权重从 100 降至 90，保留 10% 给收尾阶段
   - `WriteCentralDirectory` 移除逐条进度（毫秒级操作无意义）
   - `ComputeCrc32` 移除（被增量 CRC32 替代）
   - 所有 211 个测试通过

### v0.4.1 (2026-06-18) 发布流程修复 + 文档双语化

1. **ZIP Copy-Mode 优化：压缩流直拷替代解压-重压缩**：
   - 新增 `ZipBinaryRewriter`（Core/Utils），实现 ZIP 二进制级别的压缩流直拷：EOCD 扫描、CDFH 解析、LFH 读取/重写、中央目录重建
   - `ZipEngine.AddToArchiveAsync` 新增 copy-mode 快路径：对非加密 ZIP 优先尝试二进制直拷，失败自动 fallback 到原解压-重压缩路径
   - `ZipEngine.DeleteEntriesAsync` 新增 copy-mode 快路径 + SharpSevenZip 加密删除路径（此前加密 ZIP 删除功能缺失）
   - 支持 Deflate(8)/Store(0) 条目、Bit-3 Data Descriptor 改写、GBK 文件名 raw-bytes 无损保留
   - 不支持的格式（LZMA/BZip2/ZIP64/加密/SFX）自动 fallback，零入侵既有代码
   - 28 个单元测试覆盖：直拷、过滤、添加新条目、加密回退、Store、注释、取消、中文文件名、空 keepSet
   - 性能：10MB ZIP 加 1KB 文件 < 0.3s，100MB ZIP 删 1KB 文件 < 1.0s（旧方法分别 > 2s 和 > 20s）
   - 设计文档：`.sisyphus/plans/zip-copy-mode-optimization.md`

2. **CI release notes regex 修复**：
   - GitHub workflow 提取 RELEASE_NOTES.md 内容时 regex 缺少 `(?s)` 单行模式标志，导致 `.` 不匹配换行符，捕获组 `(.*?)` 无法跨行截取，回退到读取全文
   - 修复后正确提取首个 `## v` 标题到下一个 `##` 之间的文本

3. **RELEASE_NOTES.md 双语化**：
   - 所有版本条目的中文说明下方增加英文对照翻译
   - 标题统一添加 `/ English` 双语标注
4. 修复动态菜单bug
5. 文件列表增加"返回父目录"项目。
6. **计划文档整理**：
   - `drag-drop-direct-extract.md` 更新：纳入纯 Win32 覆盖层方案、UIA 降级、颜色状态机、呼吸动画等设计细节
   - `parent-directory-entry.md` 补充到 PROGRESS.md 历史设计方案索引
   - `quick-path-control.md` 归档（被 `quickpath-unified.md` 取代）
   - 跨平台影响分析重建：从 43（含已完成/已废弃）精简为 26 个待实现计划
   - PLAN.md 新增 `self-contained-installer.md`（P1）待实现条目
7. **UAC 提权机制 — 双模式权限不足处理**：
   - `AppSettings.AllowElevation` 属性（默认 false），设置在设置 → 高级 → 权限提升
   - 新建 `App.Elevation.cs` 含 6 个辅助方法：`IsDirectoryWritable`, `IsElevated`, `RelaunchAsAdmin`, `ShowElevationInfoDialog`, `ShowElevationDialog`, `ShowElevationFailedDialog`
   - 三个新对话框：`ElevationInfoDialog`（仅提示不可写目录+确定）、`ElevationDialog`（提权/取消）、`ElevationFailedDialog`（提权后仍不可写错误）
   - 注入 3 个 CLI 入口点：`HandleExtractBatchCore`、`RunCompressSeparateBatch`、`HandleCompressQuick`
   - 默认行为仅弹提示（不提权）；仅当 `AllowElevation=true` 才弹出 UAC 提权窗口
   - 设置窗口高级标签新增"权限提升" GroupBox
   - 中/英文各 17 个本地化键
   - 设计文档：`.sisyphus/plans/uac-elevation-permission.md`
    - 解压侧新增 `catch(UnauthorizedAccessException)` 响应式拦截：解压中遇到 Access to the path 时触发提权/提示流程，不做事前预检
    - 修复拦截后无响应问题：`Dispatcher.InvokeAsync` 改为同步 `Dispatcher.Invoke`，避免 catch 块内 async 状态机挂起
    - **提权弹窗行为优化**：首次权限不足弹窗一次，后续静默跳过（标记 Failed），不退出进程。已提权仍失败也继续处理（后续目录可能可写）

### v0.4.0 (2026-06-15) 第一个上线版本
  - 功能基本完成，测试基本完成。第一个上线版本。

15. **设置窗口 TabControl 重构 + 完整标签页体系**：
  - SettingsWindow 由单页改为完整 TabControl：压缩/解压/上下文菜单/高级/预览
  - Preview 标签页内嵌子 TabControl：文本/字体/表格/布局
  - 新增 70+ i18n 中英文键覆盖所有标签页/子标签页的标题和提示
  - AppSettings 扩展 Preview/Orientation/Emoji 等 shadow 配置属性
  - LocalizationManager `T(key)` 添加 null-safe fallback（key 不存在时返回默认值）

17. **Phase 9: 文件列表交互补齐**：
  - DataGrid 添加双击目录进入（导航到目录路径 + 选中目录树节点）
  - 键盘导航：Enter 进入目录、Backspace 返回上级、Delete 删除选中条目
  - 列排序自定义：`..` 导航行始终最前 → 目录优先于文件 → 组内按排序列
  - `▲`/`▼` 箭头标记列头，切换目录不清除排序状态
  - ViewModel 新增 `NavigateToFolderPath(string)` 方法

18. **Bugfix: 筛选工具栏尺寸输入框白边 + 空值红框**：
  - +`NullableLongConverter`：`long?` ↔ `string` 转换，空字符串→null，消除清空时 InvalidCastException 红框
  - 尺寸 TextBox 添加 `Padding="2,0"` `BorderThickness="1"`，消除默认粗边框+内边距导致的数字遮挡
  - 同个 Converter 文件 `Converters/DateTimeConverter.cs` 新增，注册到 Window.Resources

16. **Bugfix: Avalonia 12 ComboBox SelectedValuePath 不支持 + 选择不生效**：
  - 根因：Avalonia 12 删除了 `SelectedValuePath` 依赖属性，移除后 `SelectedValue` 拿 string 与 `ComboBoxItem` 对象引用比较，永远不匹配
  - 移除全部 9 处 `SelectedValuePath="Tag"`（AVLN2000 编译错误）
  - 8 个 ComboBox 由内联 `ComboBoxItem` + `SelectedValue` 改为 `ItemsSource="{Binding XxxOptions}"` + `SelectedItem="{Binding SelectedXxxOption}"` + `ItemTemplate`
  - 新增 `Option(string Display, string Value)` record，`Save()` 从 `SelectedXxxOption?.Value` 读取
  - `OnCultureChanged()` 重建 Options 集合以响应语言切换

14. **Bugfix: 最近文件菜单项灰色不可点击 (Avalonia)**：
    - 根因：在 DataTemplate 中使用 `$parent[Menu]` 或 `$parent[Window]` 绑定 `OpenRecentFileCommand`，但 Avalonia 弹出菜单的视觉树独立，无法通过 `$parent` 找到祖先
    - 改用 `MenuItem.Click` 事件 + 代码后置直接调用 ViewModel 的 `OpenRecentFileCommand` 绕过绑定限制

1. **CI 修复 — TarGzEngineTests.TestArchiveAsync_InvalidArchive_ReturnsFalse DirectoryNotFoundException**：
   - 测试在写入 corrupt .tar.gz 前未创建 `MantisZipTest\` 目录，CI 裸机上目录不存在导致 `DirectoryNotFoundException`
   - 添加 `Directory.CreateDirectory` 确保目录存在，与 ArchiveFixtures 中所有 fixture 方法的做法一致

2. **CI 修复 — ISCC 编译找不到 ChineseSimplified.isl**：
   - Inno Setup 6.7.1 Chocolatey 包未包含 `ChineseSimplified.isl`，`compiler:Languages\` 路径查找失败
   - 从 [kira-96/Inno-Setup-Chinese-Simplified-Translation](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation) 获取语言文件，存入 `setup\Languages\ChineseSimplified.isl`
   - `installer.iss` 改为引用本地相对路径（与翻译项目 CI 方案推荐一致）

3. **CI 修复 — ISCC 编译找不到 MantisZip.ShellExt.runtimeconfig.json**：
   - `MantisZip.ShellExt` 是 COM 类库（无 `<OutputType>`，默认为 `Library`），类库不生成 `runtimeconfig.json`
   - `installer.iss` 第 75 行移除 `MantisZip.ShellExt.runtimeconfig.json` 引用

4. **CI 修复 — en.json 缺少 About_Author_Bilibili 键**：
   - `strings.en.json` 缺少 `About_Author_Bilibili` 键导致 `BothLanguages_HaveSameAboutKeySet` 测试失败
   - 添加英文翻译值 `"Bilibili: space.bilibili.com/44202554"`

5. **CI 修复 — copy-7z-dll.ps1 路径引号截断**：
   - MSBuild `$(PublishDir)` 结尾反斜杠与 `&quot;` 包装组合导致 Windows 命令行解析将 `\"` 当作转义引号，`$PublishDir` 末尾混入多余 `"` 字符
   - `scripts/copy-7z-dll.ps1`：新增 `$PublishDir.TrimEnd('"', '\')` 防御性清理
   - `MantisZip.UI.csproj`：使用 MSBuild 属性函数 `$(PublishDir.TrimEnd('\\'))` 从源头消除结尾反斜杠

6. **Release workflow 修复 — ISCC 找不到 MyAppVersion**：
   - `installer.iss`：`#define MyAppVersion` 改为 `#ifndef` 条件定义，支持 ISCC `/d` 命令行参数覆盖
   - `.github/workflows/release.yml`：移除脆弱的正则替换版本号步骤，改为 `& $iscc "/dMyAppVersion=$env:VERSION" installer.iss` 直接传参，添加 `$LASTEXITCODE` 检查
   - `AGENTS.md`：Version bump checklist 移除 `installer.iss`（不再需要手动同步版本号）
   - 版本号统一同步到 **0.4.0**（`AppConstants.cs`、`MantisZip.UI.csproj`、`docs/PLAN.md`）

7. **RELEASE_NOTES.md 移至根目录**：
   - `docs/RELEASE_NOTES.md` → `RELEASE_NOTES.md`，方便根目录直接访问
   - 更新 CI release workflow 中的读取路径
   - 图片相对路径同步修正为 `docs/images/...`

8. **修复 Win11 右键菜单不显示**：
   - 根因：Windows 11 忽略 HKCU 下的 COM Shell Extension 注册（`shellex\ContextMenuHandlers`），即使注册成功 Explorer 也不会加载 COM 组件
   - `ShellIntegration.Install()` 检测到 Win11（build ≥ 22000）时跳过 COM 注册，直接使用静态级联方案（`InstallCascade`）
   - Win10 行为不变（先试 COM，失败则回退静态）
   - 参考：[Microsoft Q&A: Context menu shell extensions on Win11](https://learn.microsoft.com/en-us/answers/questions/1685103)

9. **全局调试日志增强 — CoreLog.Trace 注入 + DiagnosticsEnabled 开关**：
   - `CoreLog.cs`：`ShouldWriteOverride` 委托 → `DiagnosticsEnabled` 静态 bool，所有 Info/Error/Entry/Exit/Trace/Write 方法统一受控；`[Conditional("DEBUG")]` 的 Info/Error 仅在 DEBUG 编译，Trace 全编译
   - **43 个 catch 块**注入 `CoreLog.Trace` 以捕获静默异常路径：ZipEngine（AddToArchiveAsync、DeleteEntriesAsync、OpenZipFile）、TarGzEngine（ListEntriesAsync、CompressAsync、ExtractAsync）、PasswordManager（AddPassword、DeletePassword、FindMatchingPasswords、DeleteRule）、App.Password（TryMatchPassword）、PeParser/PdfParser/SQLiteParser（Close）、MainWindow.*（ShellExecPreview、SetFormatSpecificInfo、ShowVideoPreview、ExtractWithProgressAsync、DragDrop）、App.Open/Extract（PipeServer/冲突处理/批处理完成）、CompressSettingsWindow/ExtractSettingsWindow（压缩/解压过程中各阶段）、ProgressWindow（批处理初始化）、ShellIntegration.Assoc（SetupAssoc/Install）
   - `App.OnStartup`：设置 `CoreLog.DiagnosticsEnabled = AppSettings.Instance.EnableDebugLogging`
   - `SettingsWindow`：调试开关变更时弹出 `AppMessageBox` 提示重启生效，新增 `Settings_Debug_Restart` 中英文本地化字符串

10. **LogRedactor 隐私脱敏修复 — 相对路径 regex 分支**：
    - `LogRedactor.cs`：_pathRegex 新增第三条分支 `[^\\""<>|:]+(?:\\[^\\""<>|]+)+\\?` 匹配相对路径（如压缩包内条目路径 `字体\FiraCode-Medium.ttf`），不再依赖盘符前缀
    - `AGENTS.md`：修正 LogPrivacyMode 默认值文档从 `"full"` → `"extension"`，补充 `extension` 模式描述，更新 regex 分支计数

11. **README.md 路径修复 — 反斜杠 → 正斜杠**：
    - 将 4 处 `docs\images\` 反斜杠路径替换为 `docs/images/` 正斜杠（GitHub 要求 URL 路径使用正斜杠）
    - 修正 `SettingDebug.png` 的 alt 文本从「压缩文件冲突」改为「调试日志设置」

12. **设置窗口新增「临时文件管理」GroupBox + 启动时自动清理**：
    - 高级 Tab 中原有的「清理预览临时文件」按钮与新增的「清理所有临时文件」按钮归入 GroupBox「临时文件管理」
    - 新增 `AppSettings.CleanTempOnStartup` 设置（默认启用），启动时自动清理 `%TEMP%\MantisZip\` 中的孤儿临时文件（死机/崩溃后的残留）
    - 两个按钮共用 `CleanMantisZipTempFiles()` 方法，删除 `%TEMP%\MantisZip\` 下的所有文件（预览、拖拽导出、引擎重建/删除、字体解析等全部临时文件）

13. **修复 CLI 参数识别 + 右键菜单 Win10 不显示**：
    - CLI 参数归一化：`install-assoc`、`install-shell` 等不带 `--` 前缀的命令现在也能正确识别（自动添加 `--`）；无法识别的参数记录日志警告
    - 右键菜单安装改为全平台统一的静态级联方案（`InstallCascade`），移除 COM 默认安装路径，避免部分 Win10 设备上 `MantisZip.ShellExt.comhost.dll` 加载失败导致菜单不显示的问题
    - `InstallCom()` 代码保留但不再默认调用；`ShellIntegration.Install()` 统一走级联注册

14. **设置窗口新增"动态菜单"选项**：
    - 上下文菜单 Tab 中新增"动态菜单"复选框（`EnableDynamicMenu`，默认开启）
    - 开启时安装 COM 组件（`InstallCom`），关闭时安装静态级联菜单（`InstallCascade`）
    - 移除已死代码的"层叠上下文菜单"复选框（`EnableCascadingMenu`，早就是 cascade-only）
    - 切换选项时弹出提示，告知需重新安装才能生效
    - `com-context-menu.md` 计划补充 Explorer DLL 锁定问题

15. **文件列表新增「返回上级目录」导航行 (`..`)**：
    - 子目录顶部固定显示 `..` 行，点击/回车进入上级目录
    - 排序机制从 `SortDescriptions` 迁移到 `CustomSort`，`..` 永远在最顶
    - 过滤后 `..` 不受文字/日期/大小过滤条件影响，始终显示
    - 键盘快捷键：Enter 进入目录（文件无反应）、Backspace 返回上级目录
    - 右键菜单/拖拽/删除/选中统计均排除 `..` 行
    - 设计文档：`.sisyphus/plans/parent-directory-entry.md`

10. **Avalonia 对话框本地化 + 工具栏修复**：
    - 压缩/解压/进度/关于对话框所有硬编码英文 → `LocalizedStrings` 字典绑定，支持中/英语言切换
    - 所有对话框 ViewModel 新增 `LocalizedStrings` 属性，在构造时通过 `LocalizationManager.T()` 填充
    - 补充 strings.en.json / strings.zh-CN.json 缺少的 11 个翻译键
    - 工具栏 Preview 按钮补全 `TogglePreviewCommand` 和 `IsChecked` 绑定（之前是死按钮）
    - 筛选栏 Filter_* 键加载到 `UpdateLocalizedStrings()` 中，筛选标签不再显示空文本
    - 修复 XAML 筛选栏 `Filter_SearchLabel` → `Filter_Search` 键名不匹配

11. **Avalonia Phase 7: CLI 命令补齐 + IPC 多实例**：
    - `App.axaml.cs` 新增 9 个 CLI 命令：`--compress`、`--compress-quick`、`--compress-separate`、`--compress-combined`、`--extract-smart`、`--install-shell`、`--uninstall-shell`、`--install-assoc`、`--uninstall-assoc`
    - IPC 多实例：compress/compress-separate/compress-combined 使用 Mutex + NamedPipeServerStream 模式（与 WPF 一致）
    - Shell 集成命令委托给 WPF exe 执行

12. **Avalonia Phase 7: 设置窗口 Extract/ContextMenu/Advanced 三标签页**：
    - Extract 标签：解压目标目录、文件冲突策略（Ask/Overwrite/OverwriteOlder/OverwriteSmaller/Rename/Skip）、打开文件夹、拖拽解压、保留完整路径
    - ContextMenu 标签：8 个菜单项独立开关 + 显示图标/动态菜单 + 安装/卸载按钮（非 Windows 提示不可用）
    - Advanced 标签：7z.dll 路径选择、保留目录根、临时文件管理（清理预览/清理全部/启动时自动清理）

13. **Avalonia Phase 7: 10 个新对话框**：
    - CompressConflictDialog：压缩文件冲突（覆盖/重命名/跳过/取消 + 应用到全部）
    - ConflictDialog：解压冲突（磁盘 vs 压缩包文件对比，修改时间/大小）
    - ErrorDialog：通用错误（重试/跳过/中止 + 应用到全部）
    - PasswordEditDialog：密码编辑（密码/描述/匹配规则，支持 Glob/Regex 模式）
    - PasswordHelpDialog：密码管理器帮助（Glob 通配符/正则表达式/自动规则说明）
    - LogPrivacyHelpDialog：日志隐私模式帮助（off/filename/extension/full 四种模式对比）
    - MatchedPasswordDialog：密码自动匹配结果（显示/复制/确认使用）
    - DonationDialog：捐赠页面
    - 全部对话框绑定主题色（`ThemeWindowBgBrush`/`ThemeTextPrimaryBrush`/`ThemeBorderBrush` 等）

14. **Avalonia Phase 7: CompressSettingsWindow Password 标签增强**：
    - 标签使用 TabControl 双模式：库模式（搜索/选择已存密码）和新建模式（密码强度指示/确认匹配）
    - 库模式：搜索过滤、选择状态显示、匹配规则预览
    - 新建模式：PasswordBox/TextBox 切换、强度指示彩色圆圈、确认密码匹配
    - 共享区域：保存到库复选框、描述字段、自动规则开关及编辑
    - 使用 MVVM 方式（WPF 对应部分是代码后置 partial class）

15. **Avalonia Phase 7: i18n 补齐**：
    - strings.zh-CN.json + strings.en.json 同步添加全部新对话框/设置标签页的翻译键
    - 覆盖：Extract 标签（10+ 键）、ContextMenu 标签（8+ 键）、Advanced 标签（5+ 键）、全部对话框标题/按钮/标签
    - 构建验证：`dotnet build src\MantisZip.UI.Avalonia\` — 0 errors, 0 warnings
    - 测试：35 passed, 2 skipped, 0 failed

### v0.3.13 (2026-06-15) 完全移除 SharpZipLib 生产代码依赖

0. **SharpZipLib 加密路径 → SharpSevenZip 替换**（参见 [迁移计划](.sisyphus/plans/zipengine-sharpcompress-migration.md)）：
   - `ZipEngine.CompressAsync` 加密分支：`ZipOutputStream` → `SharpSevenZipCompressor` + `OutArchiveFormat.Zip` + `ZipEncryptionMethod.Aes256`
   - `ZipEngine.AddToArchiveAsync` 加密分支：同上，支持 `commonRootLength` 参数以保持目录结构
   - 删除 `ReadFileWithRetryZipOutputStream` 方法（~90 行加密临时文件写入代码）
   - 新增 `MapCompressionLevelToS7Z` 辅助方法
   - `MantisZip.Core.csproj`：移除 `SharpZipLib v1.4.2` 包引用
   - Killed 2 explorer.exe 进程释放 ShellExt.dll 锁，183 测试全部通过
   - SharpZipLib 保留为 test-only 依赖（用于测试 fixture 创建，不影响生产代码）

1. **Release 自动化**（参见 [计划](.sisyphus/plans/release-automation.md)）：
   - 新建 `.github/workflows/release.yml`：打 `v*` tag 时自动 `dotnet publish` → ISCC 编译安装包 → `gh release create` 发布
   - 版本号从 git tag 派生，CI 自动写入代码文件，消除三处手动同步
   - Release notes 由 `docs/RELEASE_NOTES.md` 提供，发布前编辑该文件顶部最新版本说明即可
   - CI 流程保持不变

### v0.3.13 (2026-06-14) 修复问题
1. **ToggleSepDirBaseline / ToggleProgressBars 根目录状态重置修复**：
   - `ToggleSepDirBaseline_Click` 和 `ToggleProgressBars_Click` 在根目录时不再调用 `LoadArchiveAsync`（会重置展平/筛选状态），改为统一走 `FilterFiles(_currentFolder)`
   - 影响：主菜单"目录独立基准"、进度条显隐切换不再丢失"展平目录"和"筛选"状态

2. **CompressConflictDialog 重命名按钮图标丢失修复**：
   - 勾选"对后续文件使用相同操作"时，`RenameBtn.Content` 被替换为纯字符串（丢掉 ✏️ emoji）
   - 修复：XAML 中给按钮内 TextBlock 命名 `RenameBtnLabel`，代码改设 `.Text` 而非 `.Content`

### v0.3.13 (2026-06-13) DPAPI → AES-GCM 替换 + 安装脚本修正 + 对话框 Owner 修复 + Emoji.Wpf 依赖缺失修复 + ZIP 中文编码假阳性修复

0. **ZIP 中文文件名乱码修复**（写端 + 读端双向）：
   - **写端**：三个 `ZipWriterOptions` 构造位置全部添加 `ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 }`，确保写入时使用 UTF-8 编码并设置 bit 11
   - **读端：bit 11 检测**：新增 `ZipHasUtf8Flag()` 按 ZIP 规范读取中央目录原始位标志，bit 11 已设置时跳过 GBK 回退
   - **读端：CJK 启发式检测**：新增 `LooksLikeValidCjk()`，无 bit 11 但解码结果在 CJK 范围内的也保留 UTF-8，防止第三方工具写的 UTF-8 ZIP 被误判为 GBK
   - 影响：所有 ZIP 读写路径（CompressAsync / AddToArchiveAsync / DeleteEntriesAsync / ListEntriesAsync / ExtractAsync）

1. **installer.iss 通配符化 + Emoji.Wpf 依赖修复**：
   - **[Files]** 改为 `*.dll` 通配符，取代逐一手写 DLL 清单，杜绝未来遗漏
   - 新增打包：`Typography.GlyphLayout.dll`、`Typography.OpenFont.dll`、`Stfu.dll`（Emoji.Wpf 依赖，缺失导致启动闪退 `TypeInitializationException`）
   - 新增打包：`SQLitePCLRaw.*.dll`（3 个）、`WinRT.Runtime.dll`、`Microsoft.Windows.SDK.NET.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`System.Security.Cryptography.ProtectedData.dll`
   - 添加 `ShellExt.runtimeconfig.json`

2. **对话框 Owner 修正**（6 个文件）：修复弹窗被主窗口挡住的问题
   - `AppMessageBox.xaml.cs`：`Show()`/`ShowWithAction()` 添加 `GetActiveWindow()` 自动检测 Owner，修复 85+ 个调用点
   - `MainWindow.Menu.cs`：`SettingsWindow` 设 Owner
   - `App.Compress.cs` / `App.Extract.cs` / `App.Open.cs`：CLI 模式下的冲突/命名对话框设 Owner
   - `CompressSettingsWindow.xaml.cs`：3 个冲突对话框设 Owner

3. **App.xaml.cs**：`new MainWindow()` 包裹进 try-catch，防止 XAML 初始化闪退，改为显示错误对话框

4. **installer.iss 修复**：
   - 添加 `SetupIconFile`，安装包使用 `App.ico` 图标
   - `IsWebView2Installed` 改用 `RegQueryStringValue` 检查 `pv` 版本值，并补充 `HKLM32`（WOW6432Node）检测，防止每次重复下载 WebView2

5. **预置用户设置机制**：
   - 创建 `installer\prebuilt\settings.json` 和 `window.json`，安装器在首次安装时复制到 `%LOCALAPPDATA%\MantisZip\`
   - 用户替换这两个文件即可在安装后自动加载自己的配置

6. **字体预览修复**：
   - `FontParser.ParseSfnt` 修复：name table 解析优先 pid=3（Windows Unicode）而非 pid=1（Mac），解决 CJK 字体名错误问题
   - 新增 CFF-OTF 字体回退机制：三层策略（`#` 语法 → 目录扫描 DirectWrite → GDI+ PrivateFontCollection），绕过 WPF 对 CFF 轮廓字体的加载限制
   - 失败时信息面板显示橙色警告及原因（CFF 轮廓 / Web 字体 / WPF 限制）
   - `ClearPreviewContent` 重置 `Image.Stretch`，避免字体渲染干扰图片预览

7. **DPAPI → AES-GCM 替换**（跨平台移植 Phase 4 子任务）：
   - 新建 `IDataProtector` 接口（`Core/Abstractions/`），抽象数据保护操作
   - 新建 `AesGcmDataProtector`（`Core/Utils/`），基于 .NET `AesGcm`（AES-256-GCM）实现跨平台加密，密钥以文件形式存储于 `%APPDATA%/MantisZip/.masterkey`
   - `PasswordManager` 移除 `[SupportedOSPlatform("windows")]` 特性，改为通过 `IDataProtector` 接口调用加密，默认使用 `AesGcmDataProtector`
   - `Load()` 支持三种格式自动检测：明文 JSON → AES-GCM `MZPAES|` 格式 → 旧 DPAPI 格式（自动迁移）
   - 旧 DPAPI 文件首次加载时自动解密并重写为 AES-GCM 格式，原文件备份为 `passwords.json.dpapi-backup`
   - 所有 7 个 UI 消费端无需修改（`PasswordManager.Instance.*` API 签名不变）
   - 参见 [跨平台移植计划](.sisyphus/plans/cross-platform-port.md)

### v0.3.13 (2026-06-12) ZipEngine SharpZipLib → SharpCompress 迁移 + 压缩批处理文件进度条修复 + 压缩完成后进程残留修复

0. **关联计划同步 + .NET 11 追踪**：
   - `engine-unification-sharpcompress.md`：Phase 5 状态更新为"部分完成"，新增 .NET 11 `System.IO.Compression.ZipArchive` AES-256 支持作为 SharpZipLib 移除的最佳候选项
   - `remove-sharpziplib.md`：Scope Correction 重写，精确标注残留范围
   - `zipengine-sharpcompress-migration.md`：Post-Migration Analysis 新增 .NET 11 追踪节
   - .NET 11 目标 2026 年 11 月发布，届时可彻底移除 SharpZipLib

0. **ZipEngine SharpZipLib → SharpCompress 迁移**（参见 [迁移计划](.sisyphus/plans/zipengine-sharpcompress-migration.md)）：
   - `CompressAsync`：`ZipOutputStream` → `ZipWriter`（未加密路径）；加密路径保留 SharpZipLib 回退（因 SharpCompress ZipWriter 不支持加密）
   - `AddToArchiveAsync`：全部 SharpZipLib API → SharpCompress `IArchive` + `ZipWriter`
   - `DeleteEntriesAsync`：同上，2-pass 结构全部迁移
   - 删除 `OpenZipFile` 静态方法（dead code，68 行）
   - `OpenArchiveWithEncodingFallback`：改为使用 `FileStream(FileShare.Delete)` 解决原子替换时文件锁问题
   - 类注释更新为"基于 SharpCompress，加密回退使用 SharpZipLib"
   - 183 测试全部通过

1. **压缩批处理文件进度条锯齿修复**：`ProgressWindow.SetProgress` 中总进度条改为使用公式 `已完成包数/batch总数 × 100 + 当前包进度/batch总数`，消除批处理模式下每个包 0→100% 导致的锯齿（总进度 0→100→33→0→100→66→0→100）
2. **压缩文件列表状态乱序修复**：移除所有压缩方法中进度包装器内使用 `p.ProcessedFiles`（包内文件数）作为 batch 索引的 `SetCurrentBatchItem` 调用。改为在 `CompressSeparateAsync` 中通过 `onItemStatus?.Invoke(i, BatchItemStatus.InProgress)` 在正确的迭代边界通知 UI，在 `onItemStatus` 回调和单包模式中直接使用 `SetCurrentBatchItem(0)` 指定批处理项索引
3. **QuickView 路径保存按钮注销后残留修复**：CefSharp 退出时崩溃
4. **冲突对话框按钮图标尺寸统一**：CompressConflictDialog 和 ConflictDialog 的按钮图标从 FontSize 16 统一为 24
5. **压缩完成后 exe 进程残留修复**：两处 bug 修复
   - **Bug A**：`CompressSettingsWindow` 三个 CloseAction 中 `progressWindow.Close()` 在用户提前点击"关闭"按钮后抛出 `InvalidOperationException`，导致 `this.Close()` 被跳过、`Current.Shutdown()` 永不执行。修复：加 `if (progressWindow.IsVisible) try { progressWindow.Close(); } catch { }` 保护
   - **Bug B**：`RunCompressSeparateBatch` 的 `catch (OperationCanceledException)` 为空块，取消后进程不退出。修复：添加 `await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown())`

### v0.3.13 (2026-06-11) RAR 提取进度条修复 + 取消后进程残留修复

1. **RAR 大文件提取进度条不更新修复**：`SevenZipEngine.ExtractAsync` / `ExtractEntriesAsync` 中 `SharpSevenZipExtractor.ExtractFile` 是同步阻塞调用，之前只在文件完全提取后报一次进度（0%→100%）。新增 `WriteProgressStream`（`Core/Utils`）包装 `FileStream`，在每次 `Write` 时通过回调按已知 `entry.Size` 计算百分比，100ms 节流上报，与 ZipEngine 的每文件进度模式一致。
2. **取消提取后进程残留修复**：`HandleExtractBatchCore` 中 `failed > 0` 分支在 `progressWindow.Close()` 已触发过 `Closed` 事件后才订阅 `Closed += handler`，导致 `closed.Wait()` 永远阻塞、`app.Shutdown()` 永不执行。修复：通过 `Dispatcher.InvokeAsync(() => IsVisible)` 检查窗口是否已关闭，若已关闭则跳过等待直接 shutdown。
3. **`WaitForManualCloseAsync` 同步修复**：`AutoCloseOrWaitAsync` + `KeepOpenOnComplete` 路径存在相同 bug——窗口已关闭时 `Closed` 事件早已触发，等待将永远阻塞。添加相同的 `IsVisible` 守卫检查。

### v0.3.13 (2026-06-11) 提取文件列表展示和目录树构建逻辑到 Core
1. **ArchiveTreeBuilder**（Core/Services）：`BuildTree()` 从 `ArchiveItem` 列表构建文件夹树，`FolderNode` 类从 WPF 移到 Core
2. **ArchiveEntryLister**（Core/Services）：`GetEntriesInFolder()` 按文件夹路径筛选条目（支持扁平/默认两种浏览模式），`ComputeDirectoryStats()` 预计算各目录的统计信息
3. **WPF 重构**：`BuildFolderTree()` 和 `FilterFiles()` 改为调用 Core 服务，`FolderNode` 类从 `MainWindow.Types.cs` 移除
4. **测试验证**：183 个测试全部通过

### v0.3.14-dev (2026-06-11) Avalonia 移植 —— Phase 0 完成
1. **项目骨架**：新建 `src/MantisZip.UI.Avalonia/`（net9.0 + MVVM + Skia），目标跨平台
2. **分支策略**：`avalonia-port` 分支独立开发，与 master 双向同步
3. **Phase 0 功能**：项目骨架 + 文件浏览（ListBox + 列头）+ 文本预览（编码检测）+ CSV 预览（DataView）+ PE 元数据预览 — 全部验证通过
4. **DataGrid 回退**：`Avalonia.Controls.DataGrid` v12.0.0 主题资源为空（329 字节无样式），改用 ListBox + ItemsControl 替代
5. **NameDisplay fallback**：`ArchiveItemModel.NameDisplay` 优先使用 DisplayName，回退到 Name（ZIP 引擎未设置 DisplayName）

### v0.3.12 (2026-06-10) 文件列表筛选增强（排除框/通配符/显示名匹配前修复）
1. **排除文本框 + 匹配模式选择器**：新增排除过滤（`ExcludeText`），支持子串/通配符两种匹配模式（`FilterMatchMode`），包含和排除独立生效后取交集
2. **排除框对齐修复**：区域 1 从 `StackPanel` 改为 `Grid`（3列×2行），搜索框与排除框同列等宽，文本框统一 `Height=22`+`FontSize=11`+主题绑定
3. **筛选匹配显示名而非 FullPath**：`MatchItem` 改用 `DisplayName`（回退到 `Name`），去掉 `FullPath` 匹配，解决根目录名为 "ma" 时 a/m 全文件匹配的 bug。核心改动：新增 `ArchiveItem.DisplayName`（Core），移除 UI 子类遮蔽，更新测试数据

### v0.3.12 (2026-06-10) 解压路径裁剪设置（相对当前浏览目录 / 保留完整压缩包路径）

1. **新增设置项**：在设置 → 解压 标签页添加"解压时保留完整路径"开关，默认关闭
2. **解压路径裁剪**：`ExtractSelectedAsync` 在构建输出路径时，根据设置和当前浏览目录 (`_currentFolder`) 裁剪条目的 `FullPath` 前缀
   - 关闭时（默认）：`folderA/sub/file.txt` → 当前在 `folderA/` 中解压到 `dest/sub/file.txt`
   - 开启时：`folderA/sub/file.txt` → 解压到 `dest/folderA/sub/file.txt`（保留完整路径）
3. 不影响 `ArchiveEntryExtractor.ExtractEntryAsync` 的条目查找（始终用原始 `FullPath`）

### v0.3.13 (2026-06-11) RAR 提取进度条修复 + 取消后进程残留修复

1. **RAR 大文件提取进度条不更新修复**：`SevenZipEngine.ExtractAsync` / `ExtractEntriesAsync` 中 `SharpSevenZipExtractor.ExtractFile` 是同步阻塞调用，之前只在文件完全提取后报一次进度（0%→100%）。新增 `WriteProgressStream`（`Core/Utils`）包装 `FileStream`，在每次 `Write` 时通过回调按已知 `entry.Size` 计算百分比，100ms 节流上报，与 ZipEngine 的每文件进度模式一致。
2. **取消提取后进程残留修复**：`HandleExtractBatchCore` 中 `failed > 0` 分支在 `progressWindow.Close()` 已触发过 `Closed` 事件后才订阅 `Closed += handler`，导致 `closed.Wait()` 永远阻塞、`app.Shutdown()` 永不执行。修复：通过 `Dispatcher.InvokeAsync(() => IsVisible)` 检查窗口是否已关闭，若已关闭则跳过等待直接 shutdown。
3. **`WaitForManualCloseAsync` 同步修复**：`AutoCloseOrWaitAsync` + `KeepOpenOnComplete` 路径存在相同 bug——窗口已关闭时 `Closed` 事件早已触发，等待将永远阻塞。添加相同的 `IsVisible` 守卫检查。

### v0.3.11 (2026-06-08) 文件列表拖拽提取修复（多选/目录/编码/重入）

1. **异步重入竞态修复**：`PreviewMouseMove` 添加 `_isDragExtracting` 标志，防止异步提取期间并发重入导致多个 ProgressWindow
2. **ZIP 编码兼容性修复**：`ArchiveEntryExtractor.ExtractZipEntry` 改用 `ZipEngine.OpenArchiveWithEncodingFallback`（CP437/GBK 自动探测），解决中文名 ZIP 文件在拖拽中条目查找失败的问题
3. **Tar/GZip 提取统一**：新增 `ArchiveEntryExtractor.ExtractTarGzEntry`，`MainWindow.DragDrop` 不再单独用 `TarInputStream` 提取，所有格式统一委托给 `ArchiveEntryExtractor`
4. **多选拖拽修复**：`PreviewMouseLeftButtonDown` 保存当前 `SelectedItems` 到 `_dragPreservedSelection`，`PreviewMouseMove` 使用保存的选择而非 DataGrid 处理后的 `SelectedItems`
5. **目录拖拽支持**：新增 `ExpandDragItems` 方法，将选中的目录条目展开为其子文件列表；`GetDragExtractPath` 正确剥离父目录前缀保留子目录结构；`DoDragDrop` 传入 `Directory.EnumerateFileSystemEntries`（目录句柄列表而非扁平文件路径），Explorer 递归复制保留完整目录结构
6. **自身拖拽光标修复**：`Window_DragOver` 顶部提前检查 `_isOwnDrag`，自身拖拽时显示 `None` 效果而非残留的 `Copy`

### v0.3.10 (2026-06-06→06-07) 测试按钮完整性检查 + ProgressWindow 集成

1. **引擎测试完整性提升**：三个引擎的 `TestArchiveAsync` 从快速检查改为完整完整性验证
   - ZipEngine: `stream.ReadByte()`（每个条目只读 1 字节）→ `stream.CopyTo(Stream.Null)` 完整解压流
   - TarGzEngine: `ListEntriesAsync().Count > 0`（只计数不验证）→ TarReader 逐项 `CopyTo(Stream.Null)` + `.gz` 时 GZipStream 套接
   - SevenZipEngine: 空循环（只报告进度不测试）→ `extractor.ExtractFile(index, Stream.Null)` 逐项解压 + 保留 `extractor.Check()` 结构校验
2. **测试进度 UI 改进**：内联进度条改为 `ProgressWindow`，支持取消操作，取消不弹确认框（直接 `ct.IsCancellationRequested` 检测）
3. **Dispatcher 优先级竞态修复**：`await` 续体（Normal 优先级）先于 Background 进度更新执行，导致进度 50% 就弹出结果对话框。通过 `Dispatcher.Invoke(() => { }, DispatcherPriority.Background)` 刷新解决
4. **UI 主题一致性修复**（跨 7 个 XAML 文件）：
   - `ProgressWindow` 补齐 Window 背景（`Theme_WindowBg`）和总进度条前景色（`Theme_ProgressFill`）
   - `MainWindow` 状态栏背景从误用的 `Theme_ProgressBg` 改为 `Theme_HeaderBg`
   - `App.xaml` 新增隐式 `TabItem` 样式，提取 CompressSettings/ExtractSettings 两窗口的重复样式，`AboutWindow` 自动获得主题化 Tab 头部
   - 5 个对话框主按钮统一使用 `Theme_Accent` + `Theme_TextOnAccent` 强调色（CompressSettings、ExtractSettings、ArchiveComment、About、AppMessageBox）
       - `SettingsWindow` 语言/Label/LogPrivacyMode 两个 ComboBox 补齐缺失的 `Background="{DynamicResource Theme_WindowBg}"`
5. **AGENTS.md 规则补充**：新增"每次 session 自动执行规则"第 3 条（新 UI 控件必须应用主题样式），并补充缺失主题资源时的处理方式；`Light.xaml` 进度条列颜色加深修复大小列对比度
6. **QuickPathControl 设计完成**: 统一路径快捷选择组件系统（QuickPathControl UserControl + QuickPathDialog + FavoriteManagerWindow），覆盖压缩/解压/提取所有路径选择场景；旧 `explorer-path-switcher.md` 归档

### v0.3.9 (2026-06-06→06-07) 文件关联 Bug 修复 + 独立 ProgId + 设置窗口 UI 统一

1. **文件关联 Bug 修复**：
   - `.tar.gz` 不再被跳过——设置勾选后真正写入注册表 `OpenWithProgids` + `DefaultIcon`
   - `GetInstalledExtensionCount()` 排除 `.tgz`，UI 状态 "N/7" 计数准确
   - `UninstallAssociations()` 现在也清理自定义扩展名的注册表条目
   - `UninstallAssociationForExtension()` 图标清理现在正确处理 `"{exePath}",0` 格式，自定义扩展名不再残留图标
2. **Per-extension 独立 ProgId**（类似 Bandizip）：
   - 每个格式使用独立 ProgId：`MantisZip.Zip`、`MantisZip.7z`、`MantisZip.Rar`、`MantisZip.Tar`、`MantisZip.TarGz`、`MantisZip.Gz`、`MantisZip.Iso`
   - 每个格式在资源管理器中显示自己的格式图标（`.zip` → `zip.ico`、`.rar` → `rar.ico` 等）
   - 旧版 `MantisZip.Archive` 在安装/升级时自动清理迁移
   - 自定义扩展名使用 `MantisZip.Custom`
3. **设置窗口 ComboBox 外观统一**：
   - 5 个缺失 `Background="{DynamicResource Theme_WindowBg}"` 的下拉框补全：冲突动作、主题、预览位置、信息面板方向、字体列表
   - `ConflictCombo` 还补齐了 `Width="300"` 和 `HorizontalAlignment="Left"`
4. **压缩密码「不匹配」误报修复**：
   - 用户在明文模式（👁 显示密码）下输入密码后直接点击压缩按钮时，`PasswordBox.Password` 可能仍为旧值，导致与 `ConfirmPasswordBox.Password` 对比时报"两次输入的密码不一致"
   - 修复：在验证前先同步 TextBox 内容到 PasswordBox，与 `GetPassword()` 已有逻辑一致
5. **压缩右键菜单 IPC 期间提前显示 UI**：
   - 三个压缩右键菜单（用 MantisZip 压缩 / 压缩到独立的 / 压缩到父目录名）在 IPC 路径收集期最长 ~3.8s 内无任何 UI 反馈
   - `--compress-separate` / `--compress-combined`：IPC 前创建 ProgressWindow 显示"正在收集文件..."，取消按钮 IPC 期间可用，收集完成后复用同一窗口进入压缩
   - `--compress`：IPC 前显示轻量无边框加载窗"正在收集文件..."，收集完成后自动关闭并弹出 CompressSettingsWindow
   - 新增本地化键 `App_CompressCollecting`
6. **批处理模式下取消按钮真正终止压缩**：
   - `ProgressWindow.CancelButton_Click` 批处理分支此前只 `Close()` 窗口，`_cts.Cancel()` 未调用，压缩在后台继续跑完生成完整文件
   - 修复：批处理分支也调用 `_cts?.Cancel()`，与非批处理模式一致
7. **移除 SharpZipLib 注释编辑耦合**：
   - 新建 `ZipCommentHelper`（Core/Utils）直接操作 ZIP EOCD 字节读写注释，不依赖 SharpZipLib
   - `ArchiveCommentDialog` 保存注释时显示"正在保存注释..."文字提示（本地化键 `Main_ArchiveComment_Saving`）
   - 清理 3 处无用 SharpZipLib import（App.xaml.cs / App.Cli.cs / MainWindow.xaml.cs）
   - 修正 App.Password.cs 注释（SharpZipLib → SharpCompress）
8. **版本号同步**：AppConstants.cs、.csproj、installer.iss 统一到 v0.3.9
9. **修复 .GetAwaiter().GetResult() 同步-异步反模式**：`ResolveSmartDest` 改为 async，用 `await` 替代阻塞
10. **App.Cli.cs 拆分**：按职责拆为 App.Compress.cs（压缩命令）、App.Extract.cs（解压命令）、App.Open.cs（打开/快速压缩），原文件保留为空白 partial 壳
11. **CompressSettingsWindow 拆分**：密码标签页逻辑独立为 CompressSettingsWindow.Password.cs partial 文件，主文件减少 450 行
12. **SettingsWindow.xaml.cs 拆分**：文件关联面板逻辑独立为 SettingsWindow.Assoc.cs partial 文件，主文件从 1051 行降至 602 行
13. **ShellIntegration.cs 拆分**：拆为 ShellIntegration.Menu.cs（右键菜单注册）+ ShellIntegration.Assoc.cs（文件关联注册），原文件保留共享声明（99 行）
14. **MainWindow.UI.cs 类型抽取**：FolderNode、ArchiveItem 子类、CompressedDisplayMode 枚举移到 MainWindow.Types.cs（139 行）

### v0.3.8 (2026-06-06) 右键菜单增强 + 文件关联面板重构 + 文件列表筛选/搜索

1. **右键菜单修复（批次污染 + 闪烁 + 图标）**：
   - 修复 ShellExt `_fullFileList` 跨右键调用批次污染 — 添加 2 秒时间窗口检测，选少文件不再错误使用上一批的旧大文件列表
   - 修复右键菜单闪烁/空白 — 永久缓存图标 HBITMAP，移除 `CleanupIconCache()` 热路径调用，消除每次右键 40-120ms 图标重载延迟
   - "MantisZip" 子菜单头加图标 — 用 App.ico + `InsertMenuItem` + `MIIM_BITMAP` 替代旧 `InsertMenu` API，根菜单显示软件图标
   - **压缩包计数始终显示** — `FileCountText` 不再隐藏，批处理模式显示 `压缩包 X/Y`（原仅压缩时显示，解压时隐藏）
   - **本地化语义修正** — `Progress_FileCount` 从「文件 X/Y」改为「压缩包 X/Y」/「Archive X/Y」
   - **📌 保持打开切换按钮** — 进度窗口左下角新增图钉 ToggleButton，勾选后进度走完不自动关闭窗口，用户可手动关闭
   - **倒计时期间可切换** — `AutoCloseOrWaitAsync` 每 100ms 轮询 `KeepOpenOnComplete`，倒计时中途勾选/取消勾选即时生效
2. **文件关联面板重构（per-extension 复选框 + 系统图标 + 三态状态）**：
   - 从统一开关改为按扩展名独立复选框列表，支持自定义扩展名添加/删除，行点击切换，全选/取消全选
   - 当前关联程序显示 — 每行显示当前关联的应用名，移除 "Archive"/"Compressed" 等后缀干扰词
   - 系统图标 — 使用 `SystemIconHelper.GetFileIcon` 显示系统真实文件类型图标
   - 打开默认应用按钮 — 修复 `ms-settings:defaultapps` URI 打开失败（添加 `UseShellExecute = true`）
   - 安装 Bug 修复 — 安装按钮现在只关联勾选的格式
   - 关联状态持久化 — 修复每次打开窗口强制全选问题；安装/卸载操作同时保存勾选状态
   - 三态关联状态视觉区分 — 无关联（无色）、已关联未默认（橙色 `#1AFF9800`）、已关联且默认（绿色 `#1A4CAF50`）
   - 默认程序提示 — 安装成功后弹窗增加"请在系统设置中设为默认程序"提示
   - `AppMessageBox.ShowWithAction` — 扩展消息框支持可选操作按钮
   - 删除按钮加宽 — 自定义扩展名 ✕ 按钮从 20×20 扩至 36×24
   - Status 颜色触发修复 — 将 `x:Static` 枚举 DataTrigger 改为 bool 属性绑定
   - `GetExePath` 修复 — 改用 `Assembly.Location` 替代 `Environment.ProcessPath`，兼容 `dotnet run` 场景
   - 自定义扩展名回退到 exe 图标
3. **文件列表筛选/搜索**：
   - 主工具栏「全部子目录」ToggleButton（🌲 图标）展开递归扁平视图
   - 筛选工具栏（文字搜索 + 日期范围 DatePicker × 2 + 大小范围数字输入+单位 ComboBox）
   - 通用过滤引擎 `ArchiveFilter.cs`（Core/Utils）支持组合 AND 过滤，15 个单元测试
   - 多维过滤引擎 — `SearchFilters` record + `ArchiveFilter.ApplyFilters` 支持文字/日期/大小 AND 组合
   - 空结果提示 — 无匹配文件时居中显示"无匹配的文件"，状态栏同步更新 "显示 N/M 个文件"
   - 筛选工具栏显隐 — ToggleButton 控制

### v0.3.7-refined-5 (2026-06-04) 引擎统一

- ✅ **引擎统一已完成** — SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip（v0.3.4）
- ✅ **批量进度文件列表已完成** — `--compress-separate` / `--extract-*` 批量操作进度窗口 + IPC 合并（v0.3.5）
- ✅ **ExtractSettingsWindow 已完成** — 创建 + 重设计，与 CompressSettingsWindow 视觉一致（v0.3.4 创建 / v0.3.6 重设计）
- ✅ **COM 右键菜单已完成** — .NET 9 comhost，Explorer 原生 COM 组件替代静态注册（v0.3.7）

### v0.3.7-refined-4 (2026-06-03) 关于窗口重设计

1. **AboutWindow 新建** — 替代旧 `AppMessageBox.Show()` 为 4 标签页 WPF 对话框（关于/作者/依赖库/致谢），`ResizeMode="CanResizeWithGrip"`，`MinWidth="400"` `MinHeight="350"`，App.ico 窗口图标
2. **关于 Tab 重设计** — 2 列 Grid（标签+内容）展示软件信息，包括：介绍（"轻量级全功能 Windows 压缩/解压软件"）、技术描述、支持格式、许可证、GitHub 仓库链接、Gitee 仓库链接；可扩展行结构
3. **作者 Tab 重设计** — 2 列 Grid 展示 MantisZen 联系方式：邮箱（mailto 超链接）、GitHub 个人页、Gitee 个人页
4. **依赖库 Tab** — 10 项依赖的 4 列表格（库名/版本/许可证/用途），硬编码数据，与 README 一致
5. **致谢 Tab** — 三段式感谢文本（所有开源项目、7-Zip、OpenCode + Sisyphus Agent）
6. **超链接统一处理** — `RequestNavigate` 事件 → `Process.Start(UseShellExecute=true)`，含异常日志
7. **21 个 About_* 本地化键** — 中英文双语，`L.cs` 常量，`l:L` XAML 绑定
8. **13 个冒烟测试** — `AboutWindowTests.cs` 验证 JSON 键存在性/非空/双语一致性/向后兼容（`Main_About_Text` 保留）
9. **死键审计** — `Main_About_Text`/`Main_About_Title` 确认代码无引用（仅 L.cs + JSON 保留）

### v0.3.7-refined-3 (2026-06-03) 密码工具栏 + 关闭压缩包 + 捐赠 + 空状态重设计 + 压缩冲突增强

1. **密码按钮三态重设计** — 工具栏密码按钮改为三种视觉状态：🔑 无加密、🔒 有加密未匹配、🔓 已匹配密码；点击 🔒/🔓 分别弹出密码输入/已匹配密码查看对话框
2. **MatchedPasswordDialog 新建** — 查看已匹配密码的对话框，支持眼睛切换明文/密文 + 一键复制
3. **Theme_StatusSuccessBg 主题色** — 亮色/暗色主题新增绿色成功背景色
4. **PasswordDialog/PasswordManagerWindow RevealByDefault 修复** — 两处对话框现在正确读取 `PasswordRevealByDefault` 设置
5. **密码管理器图标统一** — 工具栏、菜单、设置页面全部改用 🔐 图标
6. **密码输入对话框修复** — 原「显示密码」CheckBox 无事件处理，替换为可用的 👁 Button
7. **关闭压缩包菜单** — 文件菜单新增 ❌ 关闭压缩包 (Ctrl+W)，重置主界面到空状态
8. **文件菜单重排序** — 前三项调整为：🆕 新建 → 📂 打开 → 🕐 最近文件 → ❌ 关闭
9. **捐赠对话框** — 帮助菜单新增 ❤️ 捐赠，弹出 DonationDialog
10. **空状态重设计** — 替换旧 DropHint 为居中提示 + 两张操作卡片（📂 打开压缩包、🔐 密码管理器）
11. **CompressConflictDialog 新增"应用到全部"** — 勾选后对后续所有冲突文件自动应用相同操作
12. **GUI 独立压缩路径适配** — `RunSeparateCompressAsync` 新增 applyToAll 记忆逻辑
13. **CLI 独立压缩路径适配** — `RunCompressSeparateBatch` 同步添加 applyToAll 记忆逻辑
14. **压缩流程统一计划文档** — 创建 `.sisyphus/plans/compress-service-unify.md`
15. **CompressConflictDialog 新增目标文件信息面板** — 展示目标文件信息（大小、修改时间、完整路径）
16. **批量进度文件列表新增「已跳过」状态** — `BatchItemStatus` 枚举新增 `Skipped`

### v0.3.7-refined-2 (2026-06-02) 压缩窗口密码 Tab 重设计 + 调试日志增强

1. **密码选项卡布局重设计** — 对照 `docs/design-compress-password-tab.md` 修复全部差异：
   - 密码库条目改为两行显示（描述 + 规则）
   - 👁 按钮实现真正的 PasswordBox/TextBox 切换（主密码 + 确认密码同时切换）
   - 两个 RadioButton 始终可用，仅内容面板切换禁用/透明度
   - `PwdSelectedStatus` 始终显示，未选择时显示默认提示文字
   - 密码强度改用 `●` + 颜色（红/橙/绿）替代 emoji
   - "自动规则"移到规则文本框左侧，与"规则"标签上下排列
   - "仅 zip 和 7z 支持加密"提示移到 EncryptCheckBox 右侧
   - 两个 GroupBox 与共享区之间增加分隔线
   - "保存到密码库"默认勾选
   - 选中密码条目不再覆盖规则框内容
   - 源文件增减触发自动规则重新计算
   - 搜索框占位文字不再误过滤密码列表
   - 共享区描述/规则框用 `IsEnabled` 替代 `IsReadOnly`，显示标准禁用外观
   - 切到加密 tab 时统一调用 `RefreshPasswordTabUI()` 刷新所有 UI 状态
2. **QuickVerifyPassword 调试日志** — catch 块新增 `TraceLog` 记录异常类型和消息
3. **`PasswordEntry.PatternsDisplay` 属性** — 新增 `[JsonIgnore]` 计算属性，供 XAML 两行列表绑定

### v0.3.7-refined (2026-06-01) COM 右键菜单完善（图标 + 文本 + 本地化）

1. **图标系统重写** — `CreateCompatibleBitmap` → `CreateDIBSection` 32-bit DIB，修复 `MIIM_BITMAP` 透明背景变纯色问题
2. **主菜单标题图标** — "打开/解压" 和 "压缩" 弹出菜单从 `InsertMenu` + `MF_POPUP` 改为 `InsertMenuItem` + `MIIM_SUBMENU` + `MIIM_BITMAP`
3. **CleanupIconCache 时序修复** — 从 `QueryContextMenu` 末尾移到开头
4. **菜单文本精简** — 去掉所有 "用 MantisZip" 前缀
5. **多选文件动态文本** — 选择多个文件时："打开压缩包 等 N 个文件"、"原地解压N个压缩包" 等
6. **菜单文本本地化** — 新增 8 个 `ShellExt_*` key 到 `L.cs` + `strings.zh.json` + `strings.en.json`

### v0.3.7 (2026-05-31) COM 右键菜单 + 注册表设置同步

1. **新建 MantisZip.ShellExt 项目** — .NET 9 类库，`<EnableComHosting>true</EnableComHosting>`，comhost 模式
2. **ContextMenuHandler.cs** — `IShellExtInit` + `IContextMenu` 完整实现，8 个菜单项
3. **NativeMethods.cs** — Win32 互操作：`CF_HDROP` 提取、`InsertMenu`/`MenuItemInfo`、GDI `DrawIconEx` 图标转换、PIDL 路径解析
4. **COM 注册** — `ShellIntegration.InstallCom()`/`UninstallCom()` 在 `HKCU\Software\Classes` 写入 CLSID + shellex
5. **设置同步** — `AppSettings.Save()` → `SyncContextMenuToRegistry()` 写 10 个 DWORD 到 `HKCU\Software\MantisZip\ContextMenu`
6. **构建集成** — ShellExt 项目添加进 `.sln`，UI 项目引用，post-build 自动复制 comhost.dll
7. **版本升级** — 0.3.7

### v0.3.6 (2026-05-30) ExtractSettingsWindow UI 重构

1. **ExtractSettingsWindow 布局重写** — 从简易 Auto 堆叠改造为与 CompressSettingsWindow 一致的 **TabControl + GroupBox + 2-column Grid** 架构
2. **配色对齐** — 移除所有显式 Foreground/Background/BorderBrush，靠主题继承
3. **输出路径布局稳定** — 不再 Visibility 隐藏/显示输出路径行（消除跳动）
4. **新增本地化键** — 8 个新键 + 中英文翻译
5. **版本升级** — 0.3.6

### v0.3.5 (2026-05-30) 批处理进度文件列表 + IPC 合并

1. **ProgressWindow 批处理文件列表** — `BatchItemStatus` 枚举 + `BatchProgressItem` 模型；GridView 三列；每项独立状态指示
2. **`--compress-separate` IPC 合并** — Mutex `MantisZipCompressSeparateMutex` + Pipe；800ms 收集窗口
3. **`--compress-combined` IPC 合并** — Mutex + Pipe；跨驱动器时提示用户输入归档名称
4. **ExtractSettingsWindow 集成** — `HandleExtractBatch` / `HandleExtractBatchCore` 统一入口
5. **Unicode 编码问题修复** — `Process.Start` 调用改为 `UseShellExecute = true`
6. **版本升级** — 0.3.5

### v0.3.4 (2026-05-28~29) 引擎统一 + ExtractSettingsWindow + 调试日志

1. **引擎统一** — SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip 2.0.45
   - SharpSevenZipExtractor 替代 SevenZipExtractor 的 ArchiveFile
   - SharpSevenZipCompressor 替代 7z.exe Process 调用
   - ExtractEntriesAsync 实现（原 NotSupportedException）
   - SevenZipExtractor NuGet 包已移除
2. **SharpSevenZip 升级** — 2.0.12 → 2.0.45
3. **ZIP 添加/删除进度修复** — 移除 SharpZipLib BeginUpdate/CommitUpdate，改用提取→重压缩方案
4. **ExtractSettingsWindow 创建** — 初始版本（XAML + code-behind + ExtractOutputMode 枚举）
5. **7z.dll 状态检测与管理** — `SevenZipEngine` 新增 `CheckDllStatus()` / `ResetDllPath()` API
6. **PreserveDirectoryRoot 设置** — Core 层 `ArchiveOptions.PreserveDirectoryRoot` 属性
7. **async void 修复** — 工具栏 Click 事件处理器从 `async void` 改为 `async Task`
8. **调试日志系统增强** — 引擎分发、文件扫描、智能解压分析、文件冲突解决、冲突弹窗用户操作、分卷输出、密码自动尝试 7 类日志

### v0.3.3 (2026-05-27) 安装器多语言与预览设置增强

1. **数据表格行/列限制可配置** — 设置 → 预览 → 数据表格子标签页
2. **字体预览字号可配置** — 设置 → 预览 → 字体滑块（8–36）
3. **WebView2 启动时预初始化** — `MainWindow.Loaded` 中提前调用 `EnsureWebView2InitializedAsync()`
4. **Inno Setup 安装包多语言支持** — 简体中文安装界面
5. **安装时配置向导页** — 主题/右键菜单/文件关联
6. **安装设置持久化** — 安装器写入 `settings.json`，首次启动自动生效
7. **版本升级** — 0.3.3

### v0.3.2 (2026-05-27) 代码拆分与文档交叉更新

1. **App.xaml.cs 文件拆分** — 1977 行拆为 5 个 partial class 文件
2. **版本号更新** — 0.3.1 → 0.3.2
3. **文档交叉更新** — PLAN.md / PROGRESS.md / AGENTS.md / README.md

### v0.3.1 (2026-05-26) 预览修复与注释

1. **WebView2 PDF 内容渲染** — 替换 WebBrowser 为 WebView2
2. **PDF 页数统计修复** — 修复线性化 PDF 三种场景页数显示 `--`
3. **图片缩放修复** — 默认 FitWindow 避免小图拉伸
4. **GIF 帧导航增强** — 仿 YouTube 式时间戳 + 帧位置选择器
5. **字体预览渲染优化** — 字体预览字号独立于文本预览字号
6. **PE/PDF 预览缓存** — `ConcurrentDictionary` 缓存元数据 5s
7. **README.md 功能一览表** — 分类分级的完整功能列表
8. **代码注释规范化** — 400+ 处方法头注释、内部类注释、参数注释
9. **文件头注释** — 170+ 源文件补齐文件级别注释（用途/版权/作者）
10. **计划文档** — `.sisyphus/plans/` 新增 17 份计划文档
11. **关于页面** — 添加 7-Zip LGPL 许可证声明

### v0.1.0 (2026-04-24) 初始版本

1. **ZIP 解压** — 基于 SharpZipLib，支持 GBK 编码
2. **ZIP 压缩** — 基于 SharpZipLib
3. **7z 解压** — 基于 SevenZipExtractor
4. **RAR 解压（只读）** — 基于 SevenZipExtractor
5. **TAR 压缩/解压** — 基于 SharpZipLib
6. **GZ 压缩/解压** — 基于 SharpZipLib
7. **TAR.GZ (.tgz) 压缩/解压** — 基于 SharpZipLib
8. **目录树导航** — 左侧面板显示压缩包内目录结构
9. **文件列表** — 右侧面板显示当前目录下的直接子项
10. **密码管理** — 支持 glob/regex 模式匹配的密码管理器
11. **密码输入对话框** — 下拉选择已保存的密码
12. **版本号显示** — 右下角状态栏显示
13. **拖拽解压** — 拖拽 ZIP 文件到窗口解压
14. **拖拽压缩** — 拖拽普通文件生成 ZIP

---

## 历史设计方案索引

以下设计方案对应功能已在过往版本中完成，对应设计文档存于 `.sisyphus/plans/` 供回溯参考：

| 功能 | 设计文档 | 实现版本 |
|------|----------|:--------:|
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
| 压缩选项增强（7z/ZIP 参数扩展） | [compression-options-enhancement.md](.sisyphus/plans/compression-options-enhancement.md) | v0.4.5 |
| 密码流程统一 (ResolvePasswordAsync) | [password-flow-unification.md](.sisyphus/plans/password-flow-unification.md) | v0.4.5+ |
| 安装包 .NET 9 自动下载 | [installer-dotnet-autodownload.md](.sisyphus/plans/installer-dotnet-autodownload.md) | v0.4.3+ |
| 贡献者鸣谢面板 | [contributors-panel.md](.sisyphus/plans/contributors-panel.md) | v0.4.3+ |
| 便携版模式 | [portable-mode.md](.sisyphus/plans/portable-mode.md) | v0.4.5++ |