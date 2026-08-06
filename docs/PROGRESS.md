# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF → Avalonia 迁移中)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF → Avalonia 迁移中 + SharpCompress + SharpSevenZip

## 版本
- **当前版本**: 0.4.5
- **发布日期**: 2026-07-22

## 版本历史

版本历史按项目分为三个独立线索：
- **Avalonia 版** — 主力开发，无正式版本号，以日期为标识
- **WPF 版** — 遗留版，有正式发布版本号
- **共享层** — Core 引擎、ShellExt COM 组件、构建/CI/安装器，两项目共用

---

### MantisZip.UI.Avalonia（主力版）

**2026-08-06** — 预览缩放修复：适应高度受 contentTop 横条影响 + SVG 预览接入缩放系统
  - **图像适应高度回归**：contentTop 横条（元数据面板可配置系统引入）位于内容区 ScrollViewer 内部、图像上方，其高度从未从可用视口高度中扣除 → 图像按完整视口缩放导致出现滚动条
  - **修复**：`PreviewPanel.axaml.cs` 抽出 `UpdateViewportSize()`，可用高度 = 外层 `PreviewContentScroller.Bounds.Height` − `ContentTopBorder.Bounds.Height`（`double.IsFinite` + `> 0` 防御未布局时 NaN/0）；横条加 `x:Name="ContentTopBorder"` 并订阅其 `SizeChanged`——横条高度变化（Phase 2 合并 format 行/字段换行）不触发外层 ScrollViewer SizeChanged，必须单独重算；`ZoomIn/ZoomOut` 置 `_isZoomFitActive = false` 后不再强制重算（手动缩放不回归）
  - **SVG 适应高度无效**：`ShowSvg` 从未设置 `ImageWidth/ImageHeight`、未调用 `ZoomFit()`，XAML 的 Image 无 `ScaledWidth/ScaledHeight` 绑定 → 完全未接入缩放系统
  - **修复**：`ShowSvg` 补 `ImageWidth/ImageHeight = 栅格化尺寸` + 末尾 `ZoomFit()`；SVG ScrollViewer 的 Image 加 `Width/Height="{Binding ScaledWidth/ScaledHeight}" Stretch="Uniform"`，ScrollViewer 加 Auto 滚动条（与 Image/GIF 对齐）
  - 影响面：Image/GIF/PDF 共用 `ViewportHeight`，一处修复三方受益（PDF 同类潜在滚动条一并解决）
  - 验证：`dotnet build` 0 errors（29 个既有警告，与本次改动无关）

**2026-08-06** — 修复刷新/重新打开压缩包后文件列表为空（根目录必现）
  - **根因**：`ClearArchiveInternal` 清空压缩包状态时遗漏 `CurrentFolder`（残留上一浏览位置的 `""`）；`LoadArchiveAsync` 重建后依赖 `SelectedFolder = FolderTreeRoot` 触发 `OnSelectedFolderChanged → NavigateToFolder` 填充列表，而 `NavigateToFolder` 对 `CurrentFolder == node.FullPath` 短路返回（L1196 跳过 `PopulateEntries`）。根节点 `FullPath = ""`（`ArchiveTreeBuilder.BuildTree`）→ 根目录点刷新、或上个压缩包停在根目录后打开新包时，残留 `CurrentFolder` 恰好等于根路径 → 短路 → 列表空白，切换目录后 `PopulateEntries` 正常执行才恢复（与用户测试完全吻合）
  - **修复**：`ClearArchiveInternal()` 补 `CurrentFolder = null`，使所有重载路径（刷新/增删文件/打开）统一走正常填充路径
  - 验证：`dotnet build` 0 errors（29 个既有警告，与本次改动无关）

**2026-08-05** — 文件选择器右栏面板宽度可拖拽调整 + 持久化（PickItems / ExtractFolder）
  - **根因**：`PickItemsPanel`/`ExtractFolderPanel` 显式 `Width="260"` + 所在列 `Auto`——显式 Width 覆盖 Stretch，面板不随 `RightSplitter` 拖拽变化（表现为"宽度固定拖不动"）
  - **修复**：面板去掉固定 `Width`，改 `MinWidth="200" MaxWidth="800"`（Stretch 填满列 → 拖拽实时跟随）；Row 1 Grid 命名 `BrowserGrid` 并把 `ColumnDefinitions` 拆为显式元素
  - **拖拽范围约束**：显示面板的模式（PickItems/ExtractFolder）在构造时给 `ColumnDefinition[4]` 设 `MinWidth=200 MaxWidth=800`（GridSplitter 尊重列级约束）；其余模式列保持 `Auto` 且列级 Min=0——避免 Avalonia issue #5323（列级 MinWidth 导致面板隐藏时列不塌缩，OpenFile/SaveFile 布局被挤）
  - **持久化**：`AppSettings` 新增 `PickItemsPanelWidth`/`ExtractFolderPanelWidth`（两模式各自记忆，0=默认 260）；构造恢复（[200,800] 外回退 260）、`OnClosing` 保存列实际像素宽（超界防御跳过）
  - **Avalonia 适配**：`ColumnDefinition` 的 `x:Name` 不生成字段（CS0103），改 `BrowserGrid.ColumnDefinitions[4]` 索引访问（对齐 `PreviewPanel` 先例）；`GridLength.IsPixel` 在 Avalonia 为 `IsAbsolute`
  - 验证：构建 0 errors、Avalonia 测试 43 通过 / 2 跳过（既有 IconProvider）

**2026-08-05** — 进度窗口全面对齐 WPF：批处理列表始终可见 + 暂停真实生效 + 完成态/钉住 + CLI 解压/压缩全程进度窗口 + `--compress` 对话框修复
  - **批处理文件列表始终可见**：`ProgressWindow.axaml` 移除 `IsVisible="False"`（单文件操作也显示列表）；`MainWindowViewModel.RunWithProgress` 签名扩展为 `(title, filePaths, operation)`，11 个调用点传入列表项（解压=压缩包路径、压缩=输出路径、测试=条目名）；新增 `BatchStatusReporter` 回调把引擎 `onItemStatus` 逐项状态接到 `SetCurrentBatchItem`/`UpdateBatchItemStatus`；`InitBatchMode` 不再覆盖 WindowTitle（标题由调用方传入）
  - **暂停真实生效**：`RunWithProgress`/拖拽解压/CLI 解压/压缩的进度经 `CreatePauseAwareProgress` 嵌套包装，暂停事件真实阻断操作（此前多路径直接 `CreateBackgroundProgress` 导致暂停键无效果）
  - **完成态与 📌 钉住**：`RunWithProgress` 成功路径 `SetComplete` + `AutoCloseOrWaitAsync(0, Close)`（尊重 KeepOpenOnComplete，对齐 WPF MainWindow.Menu.cs）；`CompressWithProgress` 重写结尾四分支——成功 `SetComplete` + `AutoCloseOrWaitAsync(2500, Shutdown)`、部分失败 `result.Failed>0` → `SetErrorSummary`+`CompleteWithErrors`+等待手动关闭、异常 → `UpdateBatchItemStatus(Failed)`+错误汇总+等待手动关闭、取消 → 关窗+退出；移除内部 `Task.Delay(1500)`
  - **CLI 解压进度窗口**：新增 `RunCliExtractWithProgressAsync`（对齐 WPF HandleExtractBatchCore）——CLI 解压（`--extract`/`--extract-smart` 等 5 个分发点）全程显示 ProgressWindow（列表+逐项状态+暂停/取消），成功 2.5s 自动关闭、`ExtractResult.HasFailures` 显示错误汇总等待手动关闭、`UnauthorizedAccessException` 关闭窗口后走提权流程；`WaitForWindowCloseAsync` helper 复用
  - **`--compress` 对话框修复（3 层 bug，实测暴露）**：① `ShowCompressDialogAndRun` 非阻塞 `dlg.Show()` + `finally { desktop.Shutdown(); }` 导致对话框出现即被杀死 → 改 `Closed` 事件 + `compressStarted` 标志（对齐 WPF `win.Closed += Shutdown`）；② `CompressSettingsViewModel` 构造函数在挂载 `CollectionChanged` 前添加路径，`TryAutoFillOutputPath()` 永不触发 → 构造函数末尾显式调用（对齐 WPF ShowCompressWindow 自动填充，CLI `--compress` 依赖此逻辑，此前 OutputPath 为空直接报错）；③ `HandleCompress` 设置 `ShutdownMode.OnExplicitShutdown`（对话框关闭触发默认 OnLastWindowClose 会在压缩开始前杀死进程——日志证实 ZipEngine 已进入但无完成记录）
  - **`--compress-separate` IPC 期间进度窗口**：管道收集期立即显示 ProgressWindow（`InitBatchMode` 预填首个实例路径 + `App_CompressCollecting` 收集提示），取消同步终止管道；`CompressWithProgress` 支持复用现有窗口
  - **实测验证**（UI Automation 驱动真实进程）：`--extract`/`--extract-smart`/`--compress-quick`/`--compress-separate`/`--compress-combined` 全部退出码 0、产物正确；失败路径（损坏 zip）不自动退出等待手动关闭；`--compress` 对话框→点击开始压缩→生成正确 zip→自动关闭→退出；取消路径正常退出
  - 本地化：新增 `App_CompressCollecting`（zh/en 成对）；构建 0 errors 0 warnings；Core 253 测试通过；Avalonia 41 通过（2 跳过）

**2026-08-05** — 文件列表双击文件打开：提取临时目录 + 系统默认程序（WPF 功能补齐）
  - **根因**：`FileListGrid_DoubleTapped` 只处理目录导航（`NavigateToFolderPath`），文件双击分支缺失——双击文件"没效果"。功能未从 WPF 移植。
  - **移植**：`MainWindowViewModel` 新增 `OpenEntryWithDefaultAppAsync`，对齐 WPF `DoubleClickOpenFileAsync`：① 阈值 `DoubleClickOpenThreshold`（0=禁用，默认 10MB）；② 格式检查（Tar/GZip/ISO 不支持单项提取 → 提示）；③ 密码检查（`_hasEncryptedArchive` 且无密码 → 提示）；④ 超过阈值弹确认框；⑤ 提取到 `%TEMP%\MantisZip\OpenWith\{GUID}\`（独立于预览临时目录）；⑥ ≥1MB 走 `RunWithProgress` 进度窗口；⑦ `ArchiveEntryExtractor.ExtractEntryAsync` + `Process.Start(UseShellExecute)` 默认程序打开；⑧ 失败/取消清理临时目录
  - **View**：`FileListGrid_DoubleTapped` 改为 async void，文件分支调用 VM 方法
  - **本地化**：新增 `Main_DoubleClickFormatNotSupported`/`Main_DoubleClickOpenConfirm`/`Main_DoubleClickPasswordNeeded`/`Main_Status_DoubleClickOpened`/`Main_Status_ExtractFailed`/`App_ConfirmTitle`（zh/en，文案对齐 WPF）
  - **验证**：构建 0 errors、Avalonia 测试 43 通过 / 2 跳过（既有 IconProvider）

**2026-08-05** — 压缩设置对话框高级选项仅本次压缩生效（不再污染设置窗口默认值）
  - **根因**：`CompressSettingsWindow` 关闭时 `SaveFormatOptionsToSettings()` 将 12 项高级选项写回 `AppSettings` 并 `Save()`；设置窗口读取同一 AppSettings → 对话框修改泄漏为全局默认值（写回是为了让 request 构建点读到值，代价是被持久化）
  - **方案 A**：删除写回方法，改 `SnapshotFormatOptionsToViewModel()` 关闭时快照到对话框自己的 `CompressSettingsViewModel`（8 项面板选项 + 分卷）；`CompressSettingsViewModel` 新增 8 个高级选项属性（FileNameEncoding/ZipCompressionMethod/SevenZipCompressionMethod/SevenZipSolid/SevenZipSolidBlockSize/SevenZipDictionarySize/SevenZipNumFastBytes/SevenZipMatchFinder），构造函数从 AppSettings 读默认值——对话框下次打开仍预填设置窗口默认值
  - **request 构建改从 VM 读取**：`ExecuteCompressFromSettings`（应用内）与 `ShowCompressDialogAndRun`（CLI `--compress`）高级选项不再经 AppSettings 中转；`MainWindow` 对话框回调补复制全部高级选项 + 分卷字段（`SelectedSplitSizeOption`/`CustomSplitSizeText`）
  - **顺带修复**：① CLI `--compress` 入口 request 此前缺全部高级选项与分卷（对话框里改了不生效），现补齐并显式调用快照（该入口覆盖了窗口内部 CloseAction）；② 应用内路径分卷字段从不复制导致 `cvm.SplitSize` 恒为 0（对话框分卷选择丢失）；③ VM 构造器此前未从设置加载 `ZipEncryptionMethod`/`SevenZipEncryptHeaders`（对话框显示与设置窗口不一致）
  - **测试**：`CompressSettingsViewModelTests` 新增构造器镜像 AppSettings + 高级选项可写读回；构建 0 errors、41 通过（2 跳过为既有 IconProvider）

**2026-08-05** — 文件列表列宽可拖拽调整 + 列状态持久化（WPF 功能补齐）
  - **启用拖拽**：`FileListGrid` 加 `CanUserResizeColumns="True"`——Avalonia 12 DataGrid 该属性默认 `false`（源码核实 `Register<DataGrid, bool>` 未传默认值，`DATAGRID_defaultCanUserResizeColumns=true` 为移植遗留的未使用常量），与 WPF 默认 `true` 相反，必须显式开启
  - **名称列像素化**：`Width="*"` → `Width="250" MinWidth="120" MaxWidth="800"`——源码核实 Star sizing 下最后可见列不可拖（`CanResizeColumn` 对 `LastVisibleColumn` 返回 false），像素化后 6 列全部可拖；其余列补 `MinWidth/MaxWidth`（大小 60/400、压缩后 60/400、比率 60/300、日期 80/400），拖拽结果受列级 Min/Max 强制约束，均对齐 WPF
  - **持久化**：`WindowStateManager` 扩展 `ColumnStates`（`ColumnId=SortMemberPath`/`Width`/`Visible`/`DisplayIndex`），与 WPF window.json 的 ColumnStates 结构双向兼容（未知字段忽略、无匹配列跳过）；`MainWindow` 构造时 `ApplyColumnStates` 恢复、`Closing` 时 `CaptureColumnStates` 保存；名称列强制不可隐藏；图标列（无 SortMemberPath）不参与
  - **端到端验证**：写入 WPF 格式 window.json（含 TreeColumnWidth/Crc32/IsEncrypted 等 Avalonia 无字段）→ 启动恢复 5 列宽 → WM_CLOSE 正常关闭回写一致；Crc32/IsEncrypted 正确跳过；构建 0 errors

**2026-08-05** — 解压路径统一为单一事实源（`ExtractPathResolver`）+ ExtractSettings 文件过滤接入实际解压
  - **问题根因**：「解压选择文件到」实际解压按 `ExtractPreserveFullPath` 裁剪路径，但 `ResultPreviewService.BuildExtractPreview` 恒按「保留完整路径」建树 → 预览树与实际落盘不一致
  - **核心改造**：新增 Core `ExtractPathResolver`（`TrimCurrentFolderPrefix`/`ResolveRelativePath`/`ResolveAll`，语义与解压侧历史逻辑逐字一致），预览树与实际解压共用同一路径计算，从结构上杜绝不一致；`BuildExtractPreview` 新增 `preserveFullPath`（默认 `true`）/`currentFolder`（默认 `""`）参数，恶意路径条目逐条 try-catch 跳过（解压侧保持抛异常整批失败）
  - **Select 链路闭环**：`CustomFilePickerDialog.ShowExtractFolderAsync` 贯穿 `currentFolder`+`preserveFullPath`，`MainWindowViewModel.ExtractSelectedTo` 把 `CurrentFolder`+设置同时传给预览与实际解压，输入相同故结果必然一致
  - **文件过滤补全**：Avalonia 版 `ExtractSettingsWindow` 过滤后 `MainWindowViewModel.ExtractArchive` 在 `FilteredEntryKeys` 非空时改走 `engine.ExtractEntriesAsync` 只解压匹配项（此前预览灰显过滤项但实际全量解压——与 WPF 版行为对齐）
  - **测试**：新增 `tests/MantisZip.Tests/ExtractPathResolverTests.cs` 12 项全过；Avalonia 构建 0 errors；Avalonia 测试 41/41 通过（2 跳过）
  - **文档**：AGENTS.md 新增「Extract path resolution」契约小节 + Services 列表补 `SelectedItemsExtractService`；result-preview-panel.md 补「预览=实际一致性」说明

**2026-08-04** — 列选择菜单切换图标与主菜单 ToggleIconBox 样式对齐 + AGENTS.md 全局类样式约定补全
  - **切换图标对齐**：`ColumnHeaderContextMenu_Opening` 的列可见性菜单项图标由 `MenuItem.Icon` 槽位 CheckBox 改为与主菜单切换项同构的 `Border.ToggleIconBox`（继承 App.axaml 全局样式：20×20、圆角 3、边框 1.5、背景过渡动画）+ 12×12 `PathIcon`（几何取自列标题自身，保证与列头图标一致）；`Background` 用 `BoolToToggleBgBrushConverter`（可见 → `ThemeToggleBrush` 强调色底，隐藏 → 透明空心）
  - **放置位置对齐**：切换盒与文字改放 `MenuItem.Header` 的 `StackPanel`（`Spacing` 解析 `SpacingXxs` 紧凑度资源，新增 `GetSpacingXxs` helper），不再放 Icon 槽位——与主菜单 4 处切换项（MainWindow.axaml:217-269）逐项一致
  - **AGENTS.md 规则 4 补全**：新增「Avalonia 全局类样式」小节，文档化 App.axaml 定义的 6 个类（`ToolbarButton`/`ToolbarIcon`/`ToolbarButtonIcon`/`ToolbarButtonLabel`/`compactTab`/`ToggleIconBox`）+ 两条注意（PathIcon 不继承 Foreground、全局 TextBlock 不设 Foreground 以保护 emoji）
  - 构建 0 errors

**2026-08-04** — 文件选择器地址栏新增「添加到收藏」入口
  - **入口**：`CustomFilePickerDialog` 地址栏输入框右侧新增 ⭐ 按钮（复用 `IconStar` 资源，主题样式对齐导航按钮），点击弹 `AddFavoriteDialog`（预填当前目录名+路径）确认后经 `FavoritePathManager.Add` 写入收藏
  - **联动**：收藏后速选面板 `RefreshSources()` 立即刷新并高亮当前目录；`UpdateFavoriteButtonState()` 随导航同步——当前目录已在收藏（含桌面/文档/下载系统路径）时按钮置灰 + ToolTip「已在收藏夹」
  - **本地化**：新增 `Picker_AddFavorite` / `Picker_AlreadyFavorite`（zh/en 双语）；`QuickPathControl` 保持纯速选职责未改动
  - 构建 0 errors

**2026-08-04** — 字体预览空白修复（自定义样本文本含中文标签前缀时整行被 CJK 过滤删除）
  - **根因**：`RenderFontPreview` 对不支持 CJK 的字体按「整行」过滤——行内含任一汉字即整行删除。自定义样本文本每行带中文标签（英文：/数字：/汉字：…）时全部行被删 → 样本文本变空字符串
  - **连字路径异常**：空样本文本拆出的空行经 HarfBuzz shaping 得 `glyphCount=0`，`SKTextBlobBuilder.Build()` 返回 null → `SKCanvas.DrawText(null)` 抛 `ArgumentNullException("text")`（日志 `[FONT] SkiaSharp render failed: Value cannot be null. (Parameter 'text')`）
  - **无连字路径**：空字符串 `DrawText("")` 不抛异常，安静渲染空白位图（BitcountGridDouble 表现）
  - **A 根因修复**：CJK 过滤改按字符过滤（仅剔除 CJK Unified/Symbols/Fullwidth/Radicals，保留英文/数字/符号/emoji 含 surrogate pair）；过滤结果为空时回退默认英文样本文本
  - **B 防御修复**：HarfBuzz 路径 `glyphCount==0` 跳过绘制仅推进行距；`Build()` 返回 null 时同样跳过
  - **C 健壮性**：渲染失败回退的 `TextContent` 改用原始（未过滤）样本文本，不再显示空文本
  - 复现验证：用户真实样本文本 × 3 字体 × 2 路径全部正常渲染；构建 0 errors；核心测试 241/241 通过

**2026-08-04** — 设置窗口「压缩」tab 拆分为「通用 / 格式」两个子 tab
  - **结构重构**：「压缩」tab 由单层 `ScrollViewer` 改为嵌套 `TabControl`（`TabStripPlacement="Top"`，样式对齐「预览」tab 子 tab）；「通用」子 tab 承载 默认格式/压缩级别/选项，「格式」子 tab 承载 ZIP 默认选项 + 7z 默认选项，各自独立滚动
  - **本地化补全**：压缩 tab 内 13 处硬编码中文全部替换为 i18n 绑定；新增 15 个 key（`Settings_Compress_Tab_General`/`Settings_Compress_Tab_Format` + `Settings_Compress_Options` + `Settings_Zip_*` ×4 + `Settings_SevenZip_*` ×8），zh/en 双语；`SettingsWindowViewModel` 新增 15 个本地化属性并注册语言切换通知
  - 构建 0 errors

**2026-08-03** — 拖拽解压与「解压选中项」流程统一（[drag-extract-unify.md](.sisyphus/plans/drag-extract-unify.md)）
  - **统一解压流程**：新建 `SelectedItemsExtractService`（Services/）统一「拿到输出路径后」的解压动作；右键「解压选中项到…/到此处」与拖拽解压差异仅剩获取输出路径的方式（文件选择器 vs 目标检测）；统一引擎批量通道（一次开包）+ `pathOverrides` 路径计算 + 统一 `ArchiveOptions` 冲突处理
  - **拖拽路径语义改与右键一致**：删除 `DragDropItemExpander.GetExtractPath`（选中目录锚点），路径计算统一走 `ExtractPreserveFullPath` + 裁剪当前浏览层（`currentFolder` 由拖拽入口传入）；拖出单文件即为文件本身、不再带路径
  - **TarGzEngine 实现按条目提取**：`ExtractEntriesAsync` 从抛 `NotSupportedException` 改为实现（tar/tar.gz 单次扫描匹配 keySet + 纯 .gz 整流解压），推翻原「降级全量」决策；右键 tar/gz 不再降级全量（选中子目录文件按条目解压，语义修正）
  - **冲突统一走设置 6 策略**：拖拽删除手动 switch / 自建 `ShowConflictDialogAsync` / `_applyAllAction`，统一 `AppSettings.FileConflictAction` + VM `ShowExtractFileConflictDialogAsync`（补 `CancelOperation` → 抛 OCE，保留「取消整个操作」语义）；修 `MapConflictActionString` 连字符映射漏洞（`overwrite-if-older`/`overwrite-if-smaller` 此前被漏匹配落成 Overwrite）
  - **进度窗口统一模态**：拖拽删除自身非模态 `ProgressWindow` + 外层 `Task.Run`，改为模态阻塞（与右键同模式）；「解压选中项到此处」冲突策略由硬编码 `"overwrite"` 改用设置值，打开文件夹行为统一走 `settings.OpenFolderAfterExtract`
  - **保留差异**：拖拽失败仍弹 `AppMessageBox`（无确认环节）；状态消息 key 各自保留（`Status_DragXxx` / `Status_ExtractXxx`）
  - **展开逻辑统一 + 修复目录空解压 bug**：`GetSelectedEntriesForExtract` 数据源从 `CurrentEntries`（当前视图）改为 `GetAllRawItems()`（全量条目），内部复用 `DragDropItemExpander.ExpandItems`（与拖拽同一展开实现）；修复「浏览到某层选中目录右键解压只解压空目录」问题（目录内部文件不在当前视图中导致匹配不到）；返回类型改 `List<ArchiveItem>` 简化调用链（去掉 `ToCoreItem` 中间转换）
  - 构建 0 errors（Core/Avalonia/WPF）/ Avalonia 测试 40 通过（2 skip）

**2026-08-03** — 文件列表右键菜单「解压选中项」整合为「解压选中项到…」直接文件选择器
  - **行为变更**：右键菜单删除「解压选中项」（原弹 `ExtractSettingsWindow`）；「解压选中项到…」改为直接弹 `CustomFilePickerDialog.ShowExtractFolderAsync`（ExtractFolder 模式：选目录 + 底部实时解压冲突预览），初始路径预设为 `压缩包所在目录\压缩包同名文件夹`（同名文件夹不存在时由 `ResolveInitialPath` 自动降级到父目录）
  - **解压默认值**：选完路径直接解压，冲突策略用 `AppSettings.FileConflictAction`、打开文件夹用 `AppSettings.OpenFolderAfterExtract`（与 `--extract` CLI 一致）
  - **工具栏联动**：工具栏导出按钮由 `ExtractSelectedCommand` 改绑 `ExtractSelectedToCommand`（ToolTip 同步）；删除 `ExtractSelected` 命令及 `Ctx_ExtractSelected`/`Tooltip_ExtractSelected` key，新增 `Tooltip_ExtractSelectedTo`（zh/en 双语）
  - **解耦**：`MainWindowViewModel` 新增 `ShowExtractFolderPicker` 回调委托（`Func<IReadOnlyList<ArchiveItem>, string?, Task<string?>>`），由 `MainWindow.axaml.cs` 注入 `CustomFilePickerDialog.ShowExtractFolderAsync`
  - 构建 0 errors

**2026-08-03** — 结果预览面板计划文档与实际实现对齐（`result-preview-panel.md` 重写为完成态）
  - **文档重写**：`.sisyphus/plans/result-preview-panel.md` 由「待实现方案」改为「已完成」记录：实际布局（三列 Grid + 工具栏/摘要栏/树三行）、实际变更范围（复用 CustomFilePickerDialog、DragPreviewBitmapBuilder、PreviewTreeConverters、主题适配）、详细设计（PreviewTreeNode 实际属性、ResultTreeView 7 个 StyledProperty + 显示规则顺序、ResultPreviewService 无概念容器结构、窗口联动方式、3 处复用场景）
  - **PLAN.md 同步**：移除已完成任务行「解压/压缩结果预览面板」（原 6 项待做中 5 项已实现），三个未实现项保留为 P2 待实施行
  - **三个遗留项（后续可做）**：① 解压多压缩包按来源目录分组（当前 `GetAllRawItems()` 合并平铺）；② 截断占位符点击就地展开（当前静态"…"文本）；③ 快速/完整冲突检测双模式（当前固定全量 `checkExists`）
  - **i18n 清理**：`ResultTreeView` 5 处硬编码中文 ToolTip 全部本地化（复用 `Tree_ExpandAll`，新增 `Preview_Result_FileCount/DirInfo/ConflictCount/FileExists`，移除未用 `Preview_Result_Title`）；冲突计数/文件计数/目录统计文本改用 i18n key
  - 构建 0 errors

**2026-08-04** — SVG 预览永久加载修复 + headless 回归测试（测试项目迁移 xunit v3）
  - **Bug 修复**：`PreviewViewModel.ShowSvg` 成功渲染后从未设置 `PreviewType = PreviewType.Svg`，导致加载遮罩永不关闭（`OnPreviewTypeChanged` 仅在 `PreviewType != None` 时清除 `IsLoadingPreview`）——预览 SVG 文件会一直显示加载动画
  - **同类问题审计**：逐一核对 Avalonia `PreviewViewModel` 全部 22 个 `Show*` 方法，唯一缺失 `PreviewType` 赋值的是 `ShowSvg`；分发路径（`MainWindowViewModel.ShowPreviewAsync` switch 全覆盖）与 WPF 遗留版（`finally { HidePreviewLoading(); }` 结构免疫）均无同类问题
  - **回归测试**：新增 `PreviewViewModelTests` + `TestAppBuilder`（headless `AppBuilder` + `[assembly: AvaloniaTestApplication]`），用例验证 `ShowSvg` 后 `PreviewType==Svg`、`!IsLoadingPreview`、`IsSvgVisible`、`PreviewImage!=null`
  - **测试项目迁移**：`Avalonia.Headless.XUnit` 12.0.4 仅支持 xunit v3，与既有 xunit 2.9.2 冲突（CS0433）→ 迁移 `xunit.v3` 3.2.2 + `xunit.runner.visualstudio` 3.1.0；v12 headless 用 `[AvaloniaFact]`（替代 8.x `[AvaloniaTest]`），既有测试代码零改动
  - 验证：Avalonia 构建 0 errors / Avalonia 测试 41 通过（2 skip，均为既有显式 Skip）

**2026-08-03** — 目录行聚合显示（大小=子树和 / 日期=最新文件 / 压缩后大小按格式可用性 / 压缩率方案 A）
  - **目录聚合**：`PopulateEntries` 基于过滤后 `filteredSource` 调用 Core `ComputeDirectoryStats`，目录行应用聚合——大小 = 子树所有文件之和、日期 = 子树最新文件时间、压缩后大小 = 子树和（zip 等可得格式）
  - **派生属性重构**（`ArchiveItemModel`）：`SizeDisplay`/`LastModifiedDisplay`/`CompressedSizeDisplay` 由一次性字符串字段改为派生计算属性 + `[NotifyPropertyChangedFor]` 联动，设置 `Size`/`LastModified` 即自动刷新；`CompressionRatio` 同样改派生属性（目录聚合后自动重算）
  - **压缩后大小可用性**：新增 `CompressedSizeAvailable` 标志（zip/iso/.tar 可用；7z/rar/.tgz/.gz 不可用），不可用时文件与目录的压缩后大小列显示空（对齐 WPF `CompressedDisplayMode.Unavailable`）
  - **压缩率列（方案 A）**：移除 `RatioDisplay` 的 `IsDirectory` 门控，目录显示聚合压缩率；不可用格式下目录/文件压缩率一律空；`RatioSort` 保留目录 → -1 排序
  - 行为变化：7z 等格式文件行的压缩后大小由 `0 B` → 空、压缩率由 `0.0%` → 空；`LastModified=MinValue` 文件日期列由 `0001-01-01` → 空
  - 验证：Avalonia 构建 0 errors / Avalonia 测试 40 通过（2 skip）/ Core 241 通过（含 5 个 `ComputeDirectoryStats` 新用例）

**2026-08-03** — 拖拽解压修复（P1/P2）：.gz/.iso 提取支持 + 状态消息 i18n + 失败弹窗
  - **Core 单条目提取（共享层）**：`ArchiveEntryExtractor` 支持纯 `.gz` 单文件（新增 `ExtractGZipEntry` + `IsPlainGZipFile` 判定，GZipStream 直解，修复拖拽解压 .gz 必然抛 `InvalidFormatException`）；`ArchiveFormat.Iso` 并入 SharpSevenZipExtractor 分支（`ExtractEntryAsync`/`ExtractHeadAsync`/`ExtractTailSync`，修复拖拽/预览 ISO 必然抛 `NotSupportedException`）
  - **i18n**：新增 9 key（`Status_DragHint/DetectingTarget/ExtractingTo/Done/Cancelled/DragCancelled/Failed/PickFolder` + `DragOverlay_OwnWindow`）zh/en 双语；`DragDropService`/`MainWindow`/`OverlayController` 的拖拽状态消息全部改走 `LocalizationManager`（原为硬编码中文）
  - **失败反馈**：`DragDropService` 失败分支追加 `AppMessageBox` 错误弹窗（带 owner 窗口，弹窗自身异常被包裹，不影响 finally 清理）
  - 验证：Avalonia 构建 0 errors / Avalonia 测试 40 通过（2 skip）/ 实测 .gz、.iso、tar.gz 对照组单条目提取全部成功（手工构造最小 ISO9660 镜像经 7z.dll 读取）

**2026-08-03** — 全新 QuickPathPicker 自包含可复用路径速选控件（已完成：控件 + 三宿主全部集成）
  - **QuickPathPicker 控件**（`Controls/QuickPathPicker.axaml`）：URL 路径输入框（AutoCompleteBox，复用 CustomFilePicker 补全逻辑：历史匹配 + 父目录枚举）+ ⭐🕐🪟 三单 Tab 快捷浮层（复用 `QuickPathControl.SingleTab`+`ApplySingleTabMode`，无目录树）+ 📁 浏览按钮；内置 PointerPressed tunnel light-dismiss；控件永远只收目录——浏览/输入选到文件自动收敛为父目录（`CoerceToDirectory` 纯函数，TDD 覆盖），文件名归其它控件职责
  - **公共 API**：`Path` StyledProperty（TwoWay 双向绑定）+ 可注入 `BrowseAction(Func<Window?, string?, Task<string?>>?)`（默认内置纯目录选择 `ShowFolderAsync`），浏览差异经注入委托解决
  - **本地化**：新增 1 key `QuickPath_Browse`（zh/en）；⭐🕐🪟 ToolTip 复用既有 `QuickPath_Tab*`
  - **宿主集成（全部完成）**：
    - `SettingsWindow`：替换手动 TextBox+📁 → `<QuickPathPicker Path="{Binding CustomPath}" />`，默认零配置
    - `CompressSettingsWindow`：替换 Output 区 StackPanel（输出目录 TextBox + 快捷按钮行）+ 3 个 Popup → `<QuickPathPicker Path="{Binding OutputDirectory}" />`；注入 `OutputPathPicker.BrowseAction`（保存对话框 + 格式联动，拆目录/文件名，文件名仍写回独立 `OutputFileName`）；删除全部旧处理器（Quick*_Click / OutputPathControl_PathSelected / CloseOutputPopups / SyncOutputPathControl）
    - `ExtractSettingsWindow`：替换目标路径 TextBox+Browse+快捷按钮行+3 个 Popup → `<QuickPathPicker Path="{Binding DestinationPath}" />`；注入 `DestinationPicker.BrowseAction`（解压模式文件夹对话框 `ShowExtractFolderAsync`）；删除旧处理器（Quick*_Click / DestPathControl_PathSelected / CloseDestPopups / BrowsePath_Click / QuickBrowseButton_Click）
  - **净删除**：Compress/Extract 两窗口 AXAML 大幅精简（各 -130 余行），code-behind 冗余处理器删除
  - 测试：新增 `QuickPathPickerDirectoryNormalizationTests`（5 归一化用例，纯 temp 目录不依赖真实文件）；Avalonia 测试 42 通过（40+2 skip）/ 构建 0 errors / lsp 干净

**2026-08-03** — 设置窗口自定义路径 QuickPath — 「手动路径」输入框旁加快捷路径选择浮层
  - **QuickPath 浮层**：`SettingsWindow`「高级」Tab 默认路径优先级分组内，自定义路径 TextBox 右侧加文件夹图标按钮，点击弹出 `QuickPathControl`（全部来源：收藏/历史/窗口/目录树），选中路径写回 `CustomPath` 并关闭；复用 CompressSettings 的「PointerPressed tunnel 手动 light-dismiss」交互模式；ToolTip 本地化（`QuickPath_Title`）
  - 构建 0 errors / Avalonia 测试 35 通过 / lsp 干净

**2026-08-03** — 默认路径优先级功能 — 文件选择器初始路径可自定义排序来源（context/explorer/recent/custom）+ 手动路径
  - **AppSettings（Task 1）**：新增 `DefaultPathOrder`（`List<string>`，默认 `["context","explorer","recent","custom"]`）+ `CustomDefaultPath`（手动路径值，留空跳过）
  - **CustomFilePickerDialog 解析链（Task 2）**：`ResolveInitialPath` 改从 `DefaultPathOrder` 顺序逐个尝试第一个可用来源（context→场景相关路径、explorer→当前资源管理器窗口、recent→最近使用路径、custom→`CustomDefaultPath`），全部不可用则桌面兜底；新增 `ResolveContextPath`/`ResolveCustomPath` 帮助函数
  - **设置 UI（Task 3–4）**：新增 `PathPriorityItemModel`（Kind/DisplayName/CanMoveUp/CanMoveDown）；`SettingsWindowViewModel` 增 `PathPriorityItems` 集合 + `MovePathUp/Down` RelayCommand + `CustomPath`（加载/保存/语言切换刷新）；设置窗口「高级」Tab 新增「默认路径优先级」分组（↑↓ 排序 ItemsControl + 桌面兜底说明 + 手动路径 TextBox + 提示文案）
  - **本地化（Task 5）**：新增 7 key（`Settings_DefaultPath_GroupHeader/Context/Explorer/Recent/Custom/DesktopRow/Hint`）zh/en 双语
  - 构建 0 errors / Avalonia 测试 35 通过（Core 测试受 Explorer.exe 锁定 ShellExt.dll 的既有环境阻塞，自身编译 0 错误）
  - ⏳ 手动验收待做：设置里 ↑↓ 重排并保存后重新打开校验持久化；语言切换后排序项名称刷新

**2026-08-03** — 默认路径优先级缺陷修复 — 「资源管理器路径」现返回最近打开窗口、「场景路径」现由第一源路径推导目录
  - **explorer 来源失效（Core）**：`ExplorerWindowTracker.GetActiveExplorerPath()` 原要求资源管理器窗口为前台活动窗口（`IsActive`），但 MantisZip 打开 picker 时前台是 MantisZip 自己 → 该来源永远返回 null。改为「优先活动窗口，否则返回枚举到的第一个资源管理器窗口」，使该来源在对话框场景下实际可用（WPF 复用同一函数同步受益）
  - **context 来源失效（Avalonia）**：压缩设置「添加文件/文件夹」原来 `ShowOpenItemsAsync(this)` 不传 `initialPath`，context 场景路径无源而跳过。改为从第一个源路径推导目录（文件→所在目录，目录→本身）作为 `initialPath` 传入
  - 构建 0 errors / Avalonia 测试 35 通过 / lsp 干净

**2026-08-03** — 默认路径优先级 context 补充 — 主窗口「打开/浏览」入口也传入场景路径（当前压缩包所在目录）
  - 修复上一轮遗漏：压缩设置入口已传 context，但主窗口 `OpenFileDialogAsync`（打开压缩包/浏览）仍传 null，导致经主窗口打开选择器时 context 来源恒为空。现改为将当前已打开压缩包的所在目录（`CurrentArchivePath` 的父目录）作为场景路径初值传入，无当前存档时退回优先级链其它来源
  - 构建 0 errors / Avalonia 测试 35 通过 / lsp 干净

**2026-08-01** — 文件选择器多选（PickItems 模式）实施完成 — CompressSettingsWindow 合并「添加文件/文件夹」单按钮
  - **CustomFilePickerDialog PickItems 模式（Task 1–4）**：新增 `PickerMode.PickItems` 与静态入口 `ShowOpenItemsAsync`（返回 `IReadOnlyList<string>?`，取消返回 null）；文件+目录混合勾选累积，跨目录导航保留；`FileBrowserItem` 继承 `ObservableObject`（`IsSelected` TwoWay 绑定勾选框，`CanCheck` 控制勾选框显隐，订阅 PropertyChanged 统一走 `ToggleAccumulated` 单入口）；右侧面板（PickItems 累积列表 / ExtractFolder 原底部 PreviewArea 迁入）按模式切换；批量「＋添加所选 (N)/－移除所选 (M)」计数联动高亮、逐项 × 移除、清空、空态占位；双击/Enter 文件切换勾选不关闭、目录进入导航；系统浏览按钮 PickItems/ExtractFolder 隐藏；布局重构 `220,5,*,5,Auto` 三列 + 右栏、窗口 900×620、FileNameArea/OK 行顺移
  - **CompressSettingsWindow 合并按钮（Task 6）**：「添加文件」「添加文件夹」双按钮 → 单按钮「添加文件/文件夹」（`Compress_AddItems`）；`PickFiles` 回调改调 `ShowOpenItemsAsync`，`AddFiles` 命令体零改动；VM 删除 `PickFolder` 属性 + `AddFolder` 命令 + `Compress_AddFolder` 注册；原生 `StorageProvider` 依赖移除
  - **本地化（Task 5）**：新增 8 key（`Picker_PickItemsTitle`/`Picker_SelectedCount`/`Picker_AddSelected`/`Picker_RemoveSelected`/`Picker_ClearSelection`/`Picker_AccumulatedEmpty`/`Picker_ExtractPreviewTitle`/`Compress_AddItems`）zh/en 双语；修复 3 个死 key（标题 switch 补 PickItems case、清空按钮/空态占位补文案绑定）
  - **评审修复**：spec 评审 4 项（清空按钮无标签、空态占位空白、PickItems 窗口标题误导、死 key）+ 代码质量评审 4 项（非 PickItems 模式右栏 GridSplitter 可见回归、取消契约返回非 null、双路径冗余累积清理、Rule 7 行高/ToolTip）全部修复
  - 构建 0 errors（新增代码 0 warnings）/ Avalonia 测试 35 通过
  - ⏳ 手动验收待做：PickItems 勾选/批量/逐项/清空/跨目录/双击不关闭/OK 返回/取消 null；ExtractFolder 右栏预览回归；OpenFile/PickFolder/SaveFile 布局回归

**2026-08-01** — 列表/树控件整行命中测试修复 + 主题色/图标调整 + 文件选择器多选设计文档
  - **整行可点修复（8 处）**：Avalonia 命中测试规则是 `Background=null` 的控件不参与命中（透明区穿透），导致 ListBox/TreeView 项模板根元素无背景时「必须点击文字才能选中」。为 8 处项模板根元素补 `Background="Transparent"`：CustomFilePickerDialog.FileList、QuickPathControl.PathList/DirTree、ResultTreeView 预览树、CompressSettingsWindow 密码库、ProgressWindow 批量列表、MainWindow 目录树、PreviewPanel Torrent 树；hover/选中高亮同步铺满整行（也为后续 PickItems 勾选框方案铺路）
  - **主题色调整（ThemeLight）**：ThemeAccent `#0078D4→#60B8FC`、ThemeButtonHover `#D0D0D0→#AFC1FF`、ThemeListSelected `#E0F0FF→#81C6FF`、ThemeTabHover `#E4F0FF→#AAC2FF`（蓝色系）
  - **QuickPathControl 图标与格式**：收藏夹 Tab 图标 `IconHome→IconLightning`；多处缩进统一重排
  - **文件选择器多选设计文档**：新增 `.sisyphus/plans/file-picker-multi-select.md`（PickItems 模式：勾选框累积 + 跨目录保留 + 右侧已选项目面板 + 批量添加/移除按钮，Task 1–7 全展开），同步 PLAN.md 新增 P2 条目
  - 构建 0 errors / 0 warnings

**2026-07-31** — 预览树异步加载 — 解压/压缩设置窗口大量文件时不再卡 UI
  - **原始树构建异步化**：`BuildExtractPreview`/`BuildCompressPreview` 移入后台线程（同步签名保留，所有调用点零改动），输入参数（SelectedPaths/OutputPath/DestinationPath 等）进后台前快照；`_previewBuildVersion` 版本号守卫丢弃过期异步结果，快速增删源/切换路径/过滤时不出现错乱
  - **进度上报**：`ResultPreviewService.BuildExtractPreview` 新增可选 `IProgress<double>` 参数逐文件上报（1% 节流避免高频投递），`Progress<T>` 捕获 UI 同步上下文自动封送回主线程
  - **加载覆层**：`ResultTreeView` 新增 `IsLoading`/`BuildProgress` StyledProperty，树区域覆盖半透明进度条 + 本地化文案（`Preview_Result_Building`，zh/en）；进度 <0 显示不定进度条（压缩树无法预估条目总数），≥0 显示确定进度（解压树按条目数）
  - **250ms 延迟阈值**：快速构建（<250ms）不显示覆层，避免输入时闪烁；早期返回路径（无文件/路径无效/路径为空）同步清除构建状态，防止上一个慢构建的覆层卡死
  - 构建 0 errors / Avalonia 测试 35 通过

**2026-07-31** — 拖拽链路系统审查修复（8 问题全清：高危 3 + 中危 3 + 低危 2）
  - **高危#1 EnableDragExtract 开关失效**：Avalonia 拖拽块从未检查该设置（WPF 参考 `MainWindow.DragDrop.cs` 有检查），现于 PointerMoved 越过阈值后检查 `AppSettings.Load().EnableDragExtract`，关闭时不启动拖拽
  - **高危#2 多选拖拽拖错行**：PointerPressed 新增 `HitTestPressedRowItem`（`InputHitTest` + visual tree 找 `DataGridRow` 取 `DataContext`），仅当按下行已在当前选区时才保留旧多选区；按下未选中行时只拖新按下的行（镜像 WPF `InputHitTest` 语义）
  - **高危#3 右键取消拖拽仍解压**：`CustomDropSource.QueryContinueDrag` 的 `pressed >= 2`（左键拖拽中按下右键/中键 = 标准 OLE 取消手势）分支现在也触发取消回调；回调命名 `onEscPressed`→`onCancelled`（Esc + 右键取消统一语义）
  - **中危#5 自家窗口误判**：`DragDropService.IsOverOwnWindow` 弃用类名启发式（`Avalonia-` 前缀会误认其他 Avalonia 应用），改用 HWND 比较（`WindowFromPoint` → `GetAncestor(GA_ROOT)` → 与 `_ownerWindow` 句柄比较，同 OverlayController）
  - **中危#6 Sanitize 弱化**：`DragDropItemExpander` 删除本地弱 `SanitizeRelativePath`，复用 Core `FileConflictHelper.SanitizeEntryPath`（剔 `..`/`.`、剔非法字符、空结果抛异常）
  - **中危#7 拖拽阈值**：32px → 4px（镜像 WPF `MinimumHorizontalDragDistance`，消除"黏手"感）
  - **低危#8a GetUniquePath 重复**：删除 DragDropService 本地 99 次尝试版本，复用 Core `PathHelper.GetUniquePath`（支持 `.tar.gz` 双扩展名、1000 次尝试）
  - **低危#8b 盘根标题空**：目标为盘根（如 `C:\`）时 `folderName` 回退用完整路径
  - 构建 0 errors

**2026-07-31** — C 方案第一阶段落地（路线 2：自实现 OLE 拖拽）+ 拖拽光标按状态切换 + 光标文件入库
  - **C 方案第一阶段（路线 2）实施**：`CustomOleDragDrop.cs` 新建，自实现 `IOleDataObject`/`IOleDropSource`/`IOleEnumFormatEtc` + `CustomDataObject`（HGLOBAL UTF-16 延迟渲染）/`CustomDropSource`/`CustomEnumFormatEtc`；`GiveFeedback` 返回 S_OK + 直接 `SetCursor`，根治 Avalonia `OleDragSource` 固定返回 `USEDEFAULTCURSORS` 导致的禁止光标；Esc 检测改用 OLE `fEscapePressed`，`WH_KEYBOARD_LL` 钩子整体退役（用户验证：拖拽光标成功）
  - **拖拽光标按 overlay 状态切换（绿/红/金/灰 四状态）**：`OverlayController` 新增线程安全只读属性 `CurrentStatus` / `IsOverOwnWindow`（`_stateLock` 保护）；`CustomDropSource` 改收 `Func<nint>` 光标提供器，`GiveFeedback` 每次回调按当前状态动态取光标（与覆层颜色同一状态源）
  - **光标文件入库**：`.cur` 从 bin 移入 `Resources\Cursors\`，csproj 加 `<None Include="Resources\Cursors\**">` 复制规则（与 MenuIcons 同模式），运行时按 `Resources\Cursors\` 子目录读取；文件约定 `DragCursor.cur`（金色/默认）、`DragCursorOk.cur`（绿色/可放置）、`DragCursorWarn.cur`（红色/警告）、`DragCursorSelf.cur`（灰色/自家窗口）；缺失回退基础箭头 → 系统标准箭头；仅文件句柄 finally 中 `DestroyIcon`
  - 构建 0 errors

**2026-07-31** — 拖拽解压高危修复 + 拖拽光标方案（A 实施 / C 计划入库）
  - **Esc 取消**：`WH_KEYBOARD_LL` 钩子检测 Esc，拖拽中按 Esc 取消解压并提示（用户验证通过）
  - **ask 冲突**：拖拽解压遇到重名文件弹 `ConflictDialog`，支持"应用到全部"（用户验证通过）
  - **DataTransfer**：改用自定义格式 `MantisZipDragFormat`（Avalonia 12 新 API）；Explorer 不识别，光标仍为禁止
  - **DebugLog**：`EnableDebugLogging` 开关 + `LogRedactor` 脱敏 + 10MB 轮转；移除 overlay 每帧/每秒热路径日志
  - **光标方案 A**：`SetSystemCursor` 替换系统 OCR_NO + `DragCursor.cur` 支持；实测无效（替换后 `LoadCursor(OCR_NO)` 仍返回旧句柄，已用独立进程实证）
  - **光标方案 C**：自实现 OLE 拖拽 + 虚拟文件（根治光标 + 让 Explorer 接受拖放）已写入 `drag-drop-direct-extract.md` 计划文档，docs/PLAN.md 同步
  - 构建 0 errors

**2026-07-31** — 暗色模式预览树文件名黑色修复（NodeForegroundConverter 动态解析主题画刷）
  - **NodeForegroundConverter**：正常项不再返回 `UnsetValue`，改为从 `Application.Current.TryGetResource` 动态解析 `ThemeTextPrimaryBrush`
  - **ResultTreeView**：订阅 `ActualThemeVariantChanged`，主题切换时递归刷新所有节点 ForegroundKey 绑定
  - **PreviewTreeNode**：新增 `RaiseForegroundKeyChanged` / `RaiseForegroundKeyChangedRecursive`
  - 构建 0 errors

**2026-07-31** — QuickPathControl 重构为 Tab 式速选面板 + 新增 CustomFilePickerDialog 统一替换路径选择
  - **QuickPathControl 重构**：剥离地址栏/浏览按钮，改为 ⭐收藏/🕐历史/🪟窗口 三 Tab + 搜索框常驻（输入跨三来源聚合过滤，带来源标签）；新增 `PathSelected` 事件（选中即触发 + `PathHistoryManager.Record`）；`SetCurrentPath` 供宿主高亮；紧凑度感知行高
  - **CustomFilePickerDialog 新增**：自建文件/目录选择器（800×620 解压 / 800×420 其他）。四模式 `PickerMode`（PickFolder/SaveFile/OpenFile/ExtractFolder），静态入口 `ShowFolderAsync` / `ShowSaveFileAsync` / `ShowOpenFileAsync` / `ShowExtractFolderAsync`。布局：顶部地址栏（AutoCompleteBox 文件系统补全 + 历史建议 + ◀▶▲📁 导航）+ 左 QuickPathControl（220px）+ 右文件浏览（系统图标/大小/日期，双击进入，Enter/Backspace/Alt+←→ 键盘导航）+ 底部解压预览区（仅解压模式：GridSplitter + ResultTreeView 内建，路径变化防抖 ~300ms 重建，`checkExists:true` 实时冲突检测，MaxItemsPerDirectory=8/MaxDepth=4）
  - **宿主集成**：CompressSettingsWindow BrowseOutput→`ShowSaveFileAsync`（格式联动：按 DefaultFormat 计算默认扩展名）、PickFolder→`ShowFolderAsync`；ExtractSettingsWindow BrowseFolder→`ShowExtractFolderAsync(_entries)`；AddFavoriteDialog 浏览→`ShowFolderAsync`；MainWindow 打开压缩包→`ShowOpenFileAsync`（带压缩包扩展名筛选器）；Settings 7z.dll 路径→`ShowOpenFileAsync`（*.dll 筛选器，原 placeholder 实现补全）；保留 1 处系统对话框（添加文件到压缩包，多选）
  - **OpenFile 筛选器**：`ShowOpenFileAsync` 新增 `fileExtensions` 参数（支持 `*.zip`/`.zip`/`zip`/`*.*` 格式），OpenFile 模式按扩展名过滤文件列表
  - **体验修复**：① 地址栏新增盘符下拉 ComboBox（`DriveInfo.GetDrives()` 枚举可读盘，选择即跳转根目录，导航时自动同步选中）；② 列表选中高亮修复——QuickPathControl 与文件浏览 ListBox 的 ItemContainerTheme 补 `:selected`/`:pointerover` 伪类样式（`ThemeListSelectedBrush`/`ThemeTabHoverBrush`）；③ 图标真实化——QuickPathControl Tab 按钮 emoji → PathIcon（IconStar/IconHistory/IconHome），列表项 emoji → `IconService.GetFolderIcon()` 真实系统图标（Icon 为空回退 PathIcon + GeometryResourceConverter），📁 系统对话框按钮 → PathIcon IconFolder；④ 地址栏 ◀▶▲ 导航按钮 → PathIcon（IconChevronLeft/IconChevronRight/IconArrowUp）+ ToolTip；⑤ 底部新增「文件名」文本框 +「文件类型」下拉（SaveFile/OpenFile 模式显示）：SaveFile 类型=压缩格式（zip/7z/tar.gz）切换联动更新扩展名、双击/Enter 文件填入文件名框；OpenFile 类型=筛选器（压缩包/所有文件）切换重新过滤列表；⑥ 文件名框联动——选中文件自动同步文件名框（防重入标记避免清选中）；OpenFile 确定支持直接输入完整路径/相对当前目录文件名打开，不依赖列表选中；⑦ 目录树 Tab——QuickPathControl 新增 🌲 目录树（与 ⭐🕐🪟 并列）：平铺所有可读盘符为根（无「此电脑」虚拟根），节点展开惰性异步枚举子目录（`Task.Run` + `IsLoaded` 防重复，无权限目录静默为空），选中节点触发 PathSelected + 记历史，搜索框在树 Tab 下隐藏；CustomFilePickerDialog 地址栏盘符下拉移除（由目录树取代）；⑧ 目录树展开修复——Avalonia TreeView 无子节点不显示展开箭头，改为每个未加载目录预置占位子节点（`IsPlaceholder`）让箭头可见，展开事件时清占位并异步填充真实子目录（子目录同样预置占位）；⑨ 压缩窗口输出路径/文件名分离——Manual 模式下输出路径改为内嵌 QuickPathControl（⭐🕐🪟🌲 速选，PathSelected→OutputDirectory），文件名独立 TextBox（`OutputFileName` 不含扩展名）+ 扩展名标签（`OutputExtensionLabel` 跟随格式）；ViewModel 新增 OutputPath↔(OutputDirectory+OutputFileName) 双向拆分/合成（防重入），Separate/Combined 下 QuickPathControl 隐藏（`IsQuickPathVisible`）；`Compress_OutputFileName` i18n key；⑩ 下拉面板化——内嵌整块面板改回紧凑一行「路径 TextBox + 📂 ToggleButton」，点击弹出 Popup 浮层（320px 宽）内含 QuickPathControl，选中路径后自动收起（Avalonia 12 `IsLightDismissEnabled` 点击外部关闭）；⑪ 快捷按钮行——输出路径下 5 按钮一行：⭐收藏/🕐历史/🪟窗口/🌲目录树 各切换到 QuickPathControl 对应 Tab 并弹出复用面板（`PathTab` 提升为 public 枚举 + `SelectTab()`），📁浏览 打开保存文件对话框（ShowSaveFileAsync 格式联动）返回完整路径拆为目录+文件名；⑫ 解压窗口同步——DestinationPath 区域同样改「路径 TextBox + ⭐🕐🪟🌲 快捷按钮行 + QuickPathControl 复用面板」，📁浏览 保留解压模式对话框（ShowExtractFolderAsync 内建 ResultTreeView 冲突预览）；⑬ 快捷按钮切换修复——Popup `IsLightDismissEnabled` 会把点按钮当"点击外部"先关浮层导致要点两次，改为按钮 Click 后 `Dispatcher.UIThread.Post` 延迟到 light dismiss 完成再打开（压缩/解压窗口），点另一按钮=直接切面板；⑭ 独立浮层方案（弃用延迟重开）——每个快捷按钮对应独立 Popup，内含**单 Tab 模式 QuickPathControl**（新增 `SingleTab` 属性 + `ApplySingleTabMode()`：隐藏 Tab 行+搜索框只显示指定来源），各浮层开关互不干扰彻底规避 light dismiss 竞争；压缩/解压窗口各 4 个独立浮层，选中路径后全部关闭；⑮ 手动 light dismiss——Popup `IsLightDismissEnabled` 遮罩会拦截外部点击（按钮收不到 Click 导致点两次），改 `IsLightDismissEnabled=False` + 主窗口 `PointerPressed`（Tunnel）先关闭全部浮层、按钮 Click 再打开对应浮层，一次点击=关旧+开新；⑯ 目录树点击关闭修复——树节点单击触发 PathSelected 导致浮层立即关闭无法展开浏览，新增 `PathConfirmed` 事件（树节点双击触发）+ 公开 `CurrentTab` 属性：宿主对树 Tab 单击仅导航不关闭、双击才确认关闭；列表 Tab 单击即确认；⑰ 压缩/解压窗口移除 🌲 目录树快捷按钮（QuickPathControl 目录树功能保留，CustomFilePickerDialog 完整面板仍用），两窗口快捷行剩 ⭐🕐🪟 + 📁浏览
  - **清理**：删除 4 个测试菜单对话框（QuickPathDialog/QuickPathPreDialog/ArchiveSaveAsDialog/UnifiedExtractDialog）+ 4 个 VM 僵尸委托 + 测试菜单/switch 分支/测试 key/strings 翻译
  - i18n：新增 `QuickPath_Tab*`/`QuickPath_SearchPlaceholder`/`QuickPath_Empty*`/`Picker_*` 等 key（zh + en）
  - 构建 0 errors；测试 35 passed / 2 skipped

**2026-07-30** — Toggle 图标方框风格（Total Commander 样式）+ ToggleButton checked 反白
  - **菜单 Toggle**：4 个 View 菜单切换项改用 `Border.ToggleIconBox`（20×20，3px 圆角，150ms 过渡动画），ON 态半透明强调色填充，OFF 态空心方框，替代原 CheckBox
  - **新增 ThemeToggleBrush**：Light `#400078D4` / Dark `#4D0078D4` 半透明强调色，专用 toggle 背景，避免直接使用 `ThemeAccentBrush` 导致图标看不清
  - **ToggleButton checked 反白**：全局 `ToggleButton:checked` Foreground 改 White + `ToggleButton:checked PathIcon Foreground="White"` 直接命中 PathIcon（不继承），移除 PathIcon 显式 Foreground，覆盖工具栏/ResultTreeView 共 4 处 ToggleButton
  - **新增 BoolToToggleBgBrushConverter**：`true` → `ThemeToggleBrush`，`false` → `Transparent`
  - 构建 0 errors

**2026-07-30** — 解压/压缩设置窗口布局统一 + ResultTreeView 冲突/过滤计数修复 + FilterToggle  TwoWay 修复
  - **解压窗口布局**：`ExtractSettingsWindow` 列定义 `450,Auto,*`，左栏 `MinWidth=400`，GridSplitter 加 `ResizeBehavior`，与压缩窗口一致
  - **压缩窗口布局**：`CompressSettingsWindow` 左栏加 `MinWidth=400` ColumnDefinition
  - **解压冲突检测**：`BuildExtractPreview` 启用 `checkExists`，新增 `MarkDirectoryConflicts` 递归检测目录是否存在
  - **摘要计数修复**：`UpdateSummary` 改用原始树 `_originalRoot` 统计，避免 CompactMode 截断导致计数值偏小；`CountTotalFiles`/`CalculateTotalSize` 跳过 `IsFilteredOut` 节点
  - **冲突计数修复**：`CountConflicts` 跳过 `IsFilteredOut` 节点
  - **FilterToggle 修复**：绑定加 `Mode=TwoWay`；过滤项移除逻辑提到 `CompactMode` 判断之前，Full 模式也能生效
  - 构建 0 errors

**2026-07-29** — 过滤全排除预览树紫色显示 + 压缩时弹提示 + Manual 模式自动填充默认路径
  - **紫色空存档**：`PreviewTreeNode` 新增 `IsArchiveEmpty` + `ForegroundKey`，全子节点被过滤时显示紫色，优先级高于冲突红色
  - **NodeForegroundConverter**：新转换器，根据 ForegroundKey 返回紫色(Purple)/红色(ConflictRed)/默认
  - **空存档检测**：`ResultPreviewService` 在构建完成后递归检查 `NodeHasVisibleContent`，无可见内容时标记
  - **压缩时提示**：`ExecuteCompressFromSettings` 过滤后 `sources.Count == 0` 时弹出 `AppMessageBox` 告知用户
  - **Manual 自动填充路径**：`TryAutoFillOutputPath()` 在添加源文件时自动生成默认输出路径
  - **i18n**：新增 `Compress_FilteredAllSkipped`
  - 构建 0 errors

**2026-07-29** — 输出路径无效检测 + 预览树显示"输出路径无效" + 窗口超出屏幕自动上移
  - **路径有效性检查**：新增 `IsOutputPathValid()` 公共方法（Manual 检查路径+父目录存在性、Combined 检查非空、Separate 始终有效）
  - **CanExecuteStartCompress**：改调用 `IsOutputPathValid()`，路径无效时按钮禁用
  - **预览树路径无效显示**：`BuildCompressPreview` 中路径不通过校验时，直接创建"输出路径无效"单节点，不调 ResultPreviewService
  - **实时更新**：`OnOutputPathChanged` 增加 `BuildCompressPreview()` 调用，打字时预览树即时反映路径有效性
  - **i18n**：新增 `Compress_OutputPathInvalid` → "输出路径无效" / "Output path invalid"
  - **窗口自动上移**：新增 `AdjustWindowPosition()`，在 Loaded / Tab 切换 / 加密开关切换时触发，超出屏幕底部则自动上移到可见位置
  - **修复**：`AppIcons.axaml` 重复 `IconArchive` 导致运行时崩溃
  - 构建 0 errors

**2026-07-28** — 字段布局方向切换 + 两端对齐面板 + 设置内实时预览 + 信息栏顺序调整
  - **FieldOrientation**：新增 `FieldLayoutMode`（vertical/horizontal），infoPanel + contentTop 字段名和值可切换左右并排显示，带冒号分隔
  - **JustifyWrapPanel**：自定义 Panel，同一行内字段均匀分布（两端对齐），替代 WrapPanel
  - **ContentTop 行分组**：contentTop 现在也按 Row 值分行，与 infoPanel 一致
  - **设置实时预览**：元数据面板子标签底部增加预览区，显示 infoPanel + contentTop 的字段排布，每个字段带 ˄/˅ 按钮实时调整 Row
  - **信息栏顺序**：格式信息在上、通用信息在下（对调），移除加载/空提示
  - **字段补全**：Torrent 增加 TorrentFileName/MagnetLink/TrackerUrl/TrackerCount/AdditionalInfo 等 10 个字段；新增 IconCount/Encrypted 键；ISO 补充 TotalSize
  - **显示名去重**：common 的 FileSize→"文件大小"、FileModifiedDate→"文件修改日期"
  - 改动的文件：`MetadataPanelSettings.cs`（+FieldLayoutMode）、`PreviewViewModel.cs`、`PreviewPanel.axaml`、`PreviewPanel.axaml.cs`、`SettingsWindow.axaml`、`SettingsWindow.axaml.cs`、`MetadataPanelSettingsViewModel.cs`、`MetadataRenderEngine.cs`、`MetadataHelper.cs`、`MetadataRegistry.cs`
  - 构建 0 errors

**2026-07-28** — 压缩设置"更新匹配规则"修复：压缩完成后从未保存密码/规则
  - **Root Cause**: WPF 在三个完成路径后均调用 `SavePasswordAfterCompress()`，Avalonia 的 `ExecuteCompressFromSettings` 只设了 `StatusMessage`，完全没有密码持久化逻辑
  - **修复**: 在 `ExecuteCompressFromSettings` 压缩成功后添加密码保存：
    - 库模式：去重补充新规则到 `entry.Patterns` → `UpdatePassword` + `MarkUsed`
    - 新密码模式：`AddPassword(password, desc, rules)`
    - 空规则回退：自动生成 `*{ext}`
  - 构建 0 errors

**2026-07-28** — 压缩按钮 Manual 模式输出路径为空时禁用
  - **Root Cause**: `CanExecuteStartCompress` 只检查 `SelectedPaths.Count > 0`，与 WPF `UpdateCompressButton` 的 Manual 模式检查（hasDir + hasName）不一致
  - **修复**: `CanExecuteStartCompress` 在 Manual 模式下增加 `string.IsNullOrEmpty(OutputPath)` 检查；`OnOutputPathChanged` 增加 `NotifyCanExecuteChanged()` 使用户输入路径时按钮状态即时更新
  - 构建 0 errors

**2026-07-28** — 压缩设置"自动生成规则"修复：OutputPath 变更时未刷新规则
  - **Root Cause**: WPF 有 `FileNameTextBox_TextChanged` 触发 `RefreshAutoRules()`，Avalonia 的 `OutputPath` 是 `[ObservableProperty]` 但缺少 `OnOutputPathChanged` 处理器
  - **修复**: 新增 `partial void OnOutputPathChanged(string? value)` → 用户输入/浏览输出路径时自动刷新密码规则
  - 构建 0 errors

**2026-07-28** — 加密 ZIP 密码 bug 修复：会话缓存错误密码验证 + 预览提取不传密码
  - **Bug 2 修复：缓存错误密码仍显示"密码已匹配"**：`LoadArchiveAsync` 新增 QuickVerify 关卡，从会话缓存取得密码后，先调用 `QuickVerifyPassword` 验证是否正确才信任。验证失败则清缓存走密码解析流程。
  - **预览提取不传密码修复**：`PreviewService.ExtractToTempAsync` 签名增加 `password` 参数，`MainWindowViewModel.ShowPreviewAsync` 调用时传 `_currentPassword`。之前写死 `password: null`，导致加密 ZIP 预览提取永远用空密码解密，SHA 校验失败报 `InvalidFormatException`
  - **压缩设置密码库模式失灵修复**：`App.axaml.cs` 压缩流程和 `MainWindow.axaml.cs` 回调中改用 `GetActivePassword()` 获取密码（而非直接读 `Password` 属性），支持密码库模式下正确获得选中条目密码
  - 改动的文件：`MainWindowViewModel.cs`、`PreviewService.cs`、`CompressSettingsViewModel.cs`（新增 `GetActivePassword()`）、`App.axaml.cs`、`MainWindow.axaml.cs`
  - 构建 0 errors

**2026-07-28** — 压缩设置加密面板行为对齐 WPF + ResultTreeView 宽度可调
  - **密码面板行为对齐 WPF（7 项对齐）**：
    1. 保存复选框标签按模式切换："更新匹配规则"（库模式）/"保存到密码库"（新密码模式）
    2. 用户输入密码时清除密码库选中并自动切换到新密码模式
    3. 描述文本框在库模式下禁止焦点（`IsEnabled=false` + `IsReadOnly=true`）
    4. 匹配规则文本框在自动生成规则时只读（`IsReadOnly=true`）
    5. 自动生成规则切换时自动刷新规则
    6. `RefreshAutoRules` 改为基于输出模式生成规则（Manual→输出文件名，Separate→每文件一行，Combined→公共父目录名），而非基于源文件扩展名
    7. 选中库条目不再写入 `Password` 属性（避免触发自动切模式），压缩时库模式下取 `SelectedPasswordEntry.Password`
  - **ResultTreeView 宽度可调**：Grid `ColumnDefinitions` 改为三列显式布局（`*,Auto,280`），GridSplitter 添加 `ResizeBehavior="PreviousAndNext"`，拖动分割线可调整预览面板宽度
  - **设置窗口可调**：`SettingsWindow` 改为 `CanResize=True`，增大默认尺寸（820×640），添加 `MinWidth/MinHeight` 约束
  - 改动的文件：`CompressSettingsViewModel.cs`、`MainWindowViewModel.cs`、`CompressSettingsWindow.axaml`、`SettingsWindow.axaml`
  - 构建 0 errors
  - **自身窗口判定 Bug 修复**：之前用 `className.StartsWith("Avalonia-")` 检测自身窗口，但所有 Avalonia 应用共享 `Avalonia-` 前缀，导致其他 Avalonia 软件也被误识别为 MantisZip。改用 HWND 句柄比较（`target == _mainHwnd`）
  - **多行文本支持**：`GdiDrawText` 格式标志从 `DT_SINGLELINE | DT_VCENTER`（`0x0125`）改为 `DT_WORDBREAK`（`0x0111`）+ `DT_CALCRECT` 手动垂直居中，支持 `\n` 换行
  - **虚拟文件夹判定**：修正 `ClassifyWindow` 中丢弃 `TryGetExplorerPathFromShell` 返回状态的问题，"我的电脑"/"快速访问"等无合法路径的文件夹不再显示绿色（`Success`），改为 `Warning` 状态显示红色
  - 改动的文件：`OverlayController.cs`（HWND 判定 + 多行文本 + 虚拟文件夹）、`MainWindow.axaml.cs`（传递主窗口 HWND）、`strings.zh-CN.json`（多行文案）
  - 构建 0 errors

**2026-07-25** — 预览树工具栏扩展：过滤显示切换 + 定位选中 + 过滤连接解压预览
  - **ShowFilteredGhosts 切换按钮**：ResultTreeView 工具栏新增 ToggleButton，绑定 ShowFilteredGhosts，切换"全部显示（标灰）"/"仅显示匹配"
  - **定位到选中按钮**：工具栏新增 LocateButton，多选支持，点击后折叠全部并展开所有选中项路径
  - **TreeView 多选**：启用 `SelectionMode="Multiple"` + 选中状态同步控制按钮启用/禁用
  - **ExtractSettings 过滤→预览**：连接 `FileFilterControl.FilterChanged` → 重建预览树 + 更新过滤统计
  - **PreviewTreeNode 目录信息**：新增 `TotalDescendantSize`、`DirectoryInfoText`，目录节点显示"3 项 · 1.2 MB"
  - **ResultPreviewService**：提取 `BuildExtractPreview` 增加 `FileFilterCriteria? filter` 参数，过滤后标记 `IsFilteredOut`
  - **ExtractSettingsViewModel**：增加 `ShowFilteredGhosts`/`PreviewCompactMode` 属性
  - **定位图标**：新增 `IconLocate`（瞄准）和 `IconFilter`（漏斗）PathIcon Geometry
  - **本地化**：新增 `Preview_Result_Locate`、`Preview_Result_HideFiltered` keys
  - 改动的文件：`ResultTreeView.axaml/.cs`、`PreviewTreeNode.cs`、`AppIcons.axaml`、`ResultPreviewService.cs`、`ExtractSettingsWindow.axaml.cs`、`ExtractSettingsViewModel.cs`、`MainWindow.axaml.cs`、`strings.*.json`
  - 构建 0 errors, 12 pre-existing warnings

**2026-07-27** — 元数据面板可配置系统：数据模型 + 渲染引擎 + 设置 UI 集成 + 内联迁移
  - **FieldConfig.Row**：还原行控制字段，同 Row 字段并排显示，不同 Row 换行
  - **MetadataRegistry**：新增 ico/pdf/xlsx/pptx 类型注册 + IconCount/Encrypted 键
  - **MetadataRenderEngine**：完全重写为 `RenderCommon`/`RenderFormat` 双轨，支持 `Row` 分组
  - **MetadataSettingsManager**：配置持久化到 `metadata-panel.json`，自动初始化默认字段
  - **PreviewPanel 布局**：信息栏分为 CommonSections + FormatSections 两区；移除所有内联 `FormatMetadata` 显示；新增 ContentTop 内容区顶部横条（始终可见）；工具栏始终可见
  - **设置 UI**：在预览标签页下新增「元数据面板」子标签，支持选择类型 + 编辑字段的 Row/Order/Position
  - **入口**：预览工具栏齿轮按钮改为打开设置窗口
  - **本地化**：所有字段键通过 `GetFieldDisplayName()` 三阶回退（i18n → registry DisplayName → raw key）；新增 Metadata_Type_ico/pdf 键
  - **向后兼容**：`FormatMetadata` 合并 infoPanel + contentTop 字段，内联预览不受影响
  - 改动的文件（新建）：`MetadataPanelSettings.cs`, `MetadataRegistry.cs`, `MetadataRenderEngine.cs`, `MetadataSettingsManager.cs`, `MetadataHelper.cs`, `MetadataPanelSettingsViewModel.cs`
  - 改动的文件（修改）：`FieldConfig.cs`（+Row）、`PreviewViewModel.cs`、`PreviewPanel.axaml`、`SettingsWindow.axaml`、`SettingsWindowViewModel.cs`、`MetadataRenderEngine.cs`、`strings.zh-CN.json`、`strings.en.json`、`MainWindow.axaml.cs`
  - 废弃/删除：`MetadataPanelSettingsDialog.axaml/.cs`
  - 构建 0 errors
**2026-07-27** — 密码子系统全面补齐：ZIP 加密检测/PasswordManager 集成/密码对话框升级
  - 根因：`ArchiveService.LoadArchiveAsync` 在 `ListEntriesAsync` 成功后从不检查 `items.Any(i => i.IsEncrypted)`，加密 ZIP 包直接返回 Success，不弹出密码窗
  - 修复：
    - `ArchiveService.LoadArchiveAsync` 添加 `IsEncrypted` 检测，无密码时返回 `PasswordRequired`
    - 创建 `PasswordService`（QuickVerifyPassword/TryMatchPassword/TrySavePassword/BoundedWriteStream）
    - `MainWindowViewModel` 集成完整密码流：PasswordManager 自动匹配 → 对话框循环 → QuickVerify 验证 → 永久保存
    - `PasswordDialog` 升级：已保存密码下拉列表、描述/匹配规则编辑、永久保存选项
    - CLI 解压（`TryExtractArchiveAsync`/`TryExtractSmartAsync`）自动读取已保存密码
    - 状态栏增加密码状态图标（🔒/🔓）+ 文字
    - 补充 8 条本地化字符串（中/英）
  - 涉及 11 个文件，Avalonia 构建 0 errors，Core 236/236 测试通过

**2026-07-27** — 压缩文件冲突对话框死锁修复：CompressConflictResolver async 化
  - 根因：Core 的 `ResolveConflict()` 在任何 `await` 之前同步执行 → 若从 UI 线程调用则死锁
  - 修复：`CompressConflictResolver` 从同步委托改为异步（返回 `Task<CompressConflictResolution>`）
  - `Core.CompressService.ResolveConflict` → `ResolveConflictAsync`，`await` 回调
  - Avalonia View：移除 `Dispatcher.UIThread.Post` + `TaskCompletionSource` + `.GetAwaiter().GetResult()` 死锁桥接，改用 `await Dispatcher.UIThread.InvokeAsync`
  - WPF 版同步 `Dispatcher.Invoke` 不变（已在 `Task.Run` 后台线程中运行）
  - 涉及 8 个文件，构建 0 errors，Core 236/236 测试通过

**2026-07-27** — 解压文件冲突对话框死锁修复：async 端到端管线
  - 根因：同步 `Post + GetAwaiter().GetResult()` 桥接在 Avalonia 异步 `ShowDialog` 下死锁（Avalonia 无 WPF 的嵌套消息泵）
  - 修复：`ArchiveOptions.ConflictResolverAsync` 异步回调 + `FileConflictHelper.ResolvePathAsync` + 引擎 `Task.Run(async () => ... await ResolvePathAsync(...)`
  - 所有三层引擎（Zip/SevenZip/TarGz）`ExtractAsync` 改为 `Task.Run(async () =>`，`ResolvePath` → `await ResolvePathAsync`
  - `MainWindowViewModel.ShowExtractFileConflictDialogAsync` 使用 `await Dispatcher.UIThread.InvokeAsync` 显示对话框
  - 构建 0 errors，Core 236/236 + Avalonia 35/37 测试通过

**2026-07-24** — 拖拽覆盖层视觉优化：颜色/呼吸/文案 + 完整路径显示 + 本地化
  - **颜色**：成功状态绿色调亮（RGB 76,175,80 → 107,212,107），无目标灰色改为暖金（RGB 255,215,0）
  - **呼吸速度**：周期 4s → 2s（`Math.PI/20` → `Math.PI/10`）
  - **Explorer 路径**：`ClassifyWindow` 改用 ShellWindows COM 获取完整路径（取代窗口标题短名称）
  - **对话框路径**：`TryGetDialogPath` 从子控件枚举到路径时返回 `Success`（绿）而非 `Warning`（红）
  - **文案新增**：对话框未知路径显示 `"识别不到此窗口路径\n{标题}"`，无目标显示 `"拖拽到文件夹以释放文件"`
  - **本地化**：新增 `DragOverlay_*` × 5 keys（zh-CN + en），覆盖层所有文案通过 `LocalizationManager.T()`
  - 改动的文件：`OverlayController.cs`、`DropTargetDetector.cs`、`strings.zh-CN.json`、`strings.en.json`
  - 构建 0 errors

**2026-07-23** — 覆盖层 Bug 修复：OLE 初始化恢复 + GDI P/Invoke 入口名修正 + UpdateLayeredWindow 位置参数修复 + 呼吸动画
  - **OleInitialize**：恢复 `NativeMethods.OleInitialize` 调用（Avalonia 内部不处理 OLE 初始化，移除后 `DoDragDropAsync` 失败）
  - **GDI P/Invoke 入口名**：`GdiCreateCompatibleDC` → `CreateCompatibleDC` 等（C# 方法有 Gdi 前缀但 Win32 DLL 导出名无前缀，导致 `EntryPointNotFoundException` → 后台线程未捕获 → 进程终止）
  - **UpdateLayeredWindow 位置**：`pptDst` 参数从 `{0,0}` 改为实际窗口坐标（`UpdateLayeredWindow` 同时设置位置和内容，`{0,0}` 将覆层重置到左上角，与 `SetWindowPos` 冲突导致位置跳动）
  - **覆盖层保护**：后台线程增加全域 catch-all，异常捕获并记录后继续运行而非崩溃
  - **Avalonia 窗口过滤**：`ClassifyWindow` 检测到自己的窗口时跳过渲染，避免覆层在 MantisZip 界面上闪烁
  - **窗口位置稳定**：移除 `_lastTargetHwnd` 后备机制，`WindowFromPoint` 返回覆层/空时直接跳过本帧
  - **呼吸动画**：`SourceConstantAlpha` 改为正弦波（40~120，周期 4s），覆层透明度缓慢脉动
  - 构建 0 errors

**2026-07-23** — 拖拽系统重构：放弃 Win32 OLE/native CCW，改用 Avalonia DragDrop API + DragDropService 后置解压 + Avalonia Window 覆盖层
  - **架构变更**：彻底放弃手写 COM `IDataObject`/`IDropSource`，改为 Avalonia 内置 `DragDrop.DoDragDropAsync`
  - **移除**：`DragDataObject.cs`、`DropSource.cs`、`DragOverlayWindow.cs`、`DragPreviewPopup.cs`
  - **新增**：`OverlayController.cs` — Avalonia Window + `UpdateLayeredWindow` 实现后台线程覆盖层
  - **DropTargetDetector**：新增 `DirectUIHWND` → `CabinetWClass` 父链上溯 + `GetAncestor` 覆盖整个窗口
  - **DragDropService**：新增 `IsOverOwnWindow()`，在自己窗口上松手时静默取消
  - **MainWindow**：
    - `PointerPressed` 改用隧道策略解决 DataGrid 事件消费问题
    - 新增 `PointerReleased` 隧道监听清除拖拽状态
    - 拖拽阈值 10px → 32px
  - **NativeMethods**：新增 `GetParent`、`GetAncestor`、`UpdateLayeredWindow`、`SIZE`、`BLENDFUNCTION`
  - **OLE 初始化**：移除 `NativeMethods.OleInitialize`（Avalonia 内部处理）
  - DragDropService + DropTargetDetector + OverlayController 加 DebugLog
  - 构建 0 errors

**2026-07-22** — P0 列表控件紧凑度联动 + AGENTS.md 规则 7
  - ExtractSettingsWindow FileListBox：`ItemContainerTheme` + `ControlHeightMd`
  - MainWindow FileListGrid：`RowHeight="{DynamicResource ControlHeightMd}"`
  - MainWindow Folder TreeView：`TreeViewItem` Style 加 `MinHeight="{DynamicResource ControlHeightSm}"`
  - PreviewPanel CsvDataGrid / SqliteDataGrid：`RowHeight="{DynamicResource ControlHeightMd}"`
  - PasswordManagerWindow PasswordGrid：`RowHeight="{DynamicResource ControlHeightMd}"`
  - FavoriteManagerWindow FavoritesGrid：`RowHeight="{DynamicResource ControlHeightMd}"`
  - AGENTS.md 新增 规则 7：列表/树形/表格控件必须使用紧凑度感知的行高（含对照表 + 三种控件示例）
  - Build 0 errors

**2026-07-22** — 文件列表多选 + 预设重命名 + CanExecute 守卫 + 紧凑度联动 + 开关行为统一
  - CompressSettingsWindow：源文件列表改为多选（`SelectionMode="Multiple"`），移除按钮改为 Click code-behind 批量删除
  - Preset rename：新增 Rename 按钮 + `RenamePresetRequested` 事件 + InputDialog 预填当前名 + 重名检查（Compress/Extract 两窗口）
  - CanExecuteStartCompress：`SelectedPaths.Count > 0` 守卫，`CollectionChanged` 时自动更新
  - FileFilterEditor 切换开关统一为 方案 A（隐藏内容而非禁用），`SyncControlStates()` 简化为 `FilterContentPanel.IsVisible`
  - AGENTS.md 新增 规则 6：开关控制面板区域时统一隐藏（方案 A）
  - SourceFilesList 支持紧凑度：`ItemContainerTheme` 用 `ControlHeightMd`（28/32/38 三档）
  - 本地化：新增 3 键（zh-CN + en）
  - Build 0 errors

**2026-07-22** — P1-3: FileFilterEditor 移植（文件过滤控件：三维过滤 + 预设管理 + 临时预设）
  - 新建 Controls/FileFilterEditor.axaml/.cs：完全移植 WPF 版文件过滤控件（扩展名/文件名/大小/日期四维过滤 + 预设管理 + ComboBox 临时预设机制）
  - 新建 Services/FileFilterHelper.cs：ApplyFilter + 递归目录枚举
  - CompressSettingsWindow/ExtractSettingsWindow 各加一个过滤 Tab，接入 GetFilter/GetFilteredEntryKeys
  - AppSettings.cs：FilterPresets 属性 + AddPreset 方法
  - 本地化：新增 28 条键（FileFilter_* + Common_OK/Cancel + Compress/Extract_TabFilter）
  - 主题绑定 + 紧凑度模式全量支持
  - Build 0 errors, Tests 35 passed

**2026-07-20** — P1-1：双击行为 CLI 分发 + 解压后删除原包后端逻辑
  - App.axaml.cs：新增 `--open-dispatch` CLI handler，根据 DoubleClickAction 设置分发到 extract-here/smart-extract/extract-dialog/open
  - App.axaml.cs：新增 TryDeleteArchiveAfterExtract 方法（retry 3x+200ms 间隔，FileSystem.DeleteFile 移入回收站）
  - TryExtractArchiveAsync 和 TryExtractSmartAsync 解压成功后调用 TryDeleteArchiveAfterExtract
  - Build 0 errors

**2026-07-20** — WPF 差异补齐：SettingsWindow 缺失控件移植（DeleteArchiveAfterExtract + DoubleClickAction + AllowElevation + EnableExtractMenu/EnableQuickCompress）
  - AppSettings.cs：新增 6 属性（DeleteArchiveAfterExtract/DoubleClickAction/DoubleClickOpenThreshold/AllowElevation/EnableExtractMenu/EnableQuickCompress）
  - SettingsWindowViewModel.cs：6 个 ObservableProperty + DoubleClickActionOptions Combo + DoubleClickOpenThresholdMB 属性 + Load Save
  - SettingsWindow.axaml 解压 Tab：DeleteArchiveAfterExtract 复选框 + 双击行为区域（ComboBox + MB TextBox）
  - SettingsWindow.axaml 高级 Tab：AllowElevation 复选框
  - 本地化：新增 9 键（zh-CN + en）
  - Build 0 errors

**2026-07-20** — 窗口位置持久化（P2-1）：WindowStateManager + MainWindow 集成
  - 新建 Models/WindowStateManager.cs：将窗口 Width/Height/Position(PixelPoint)/WindowState 持久化到 %LOCALAPPDATA%\MantisZip\window.json
  - MainWindow.axaml.cs 构造函数调用 WindowStateManager.Load(this) 恢复上次位置
  - MainWindow.axaml.cs Closing 事件调用 WindowStateManager.Save(this) 关闭时保存
  - 最小化时不保存（避免 stale 位置），恢复时跳过 FullScreen
  - Build 0 errors

**2026-07-21** — 修复 DOCX 预览中文标题检测 + 计划补充表格渲染方案
  - 修复 `ShowDocx()` 标题检测仅匹配 `"Heading"`（大小写敏感）的问题：
    - StyleId 改为 `OrdinalIgnoreCase` 匹配（`"heading1"`/`"HEADING1"`）
    - 新增 StyleName 显示名称检测（`"标题 1"`、`"heading 1"`）
    - 新增 `OutlineLevel` 段落属性检测（跨语言区最可靠的方式）
    - 级别提取改为通用数字提取（`"Heading1"`→1、`"标题 1"`→1）
  - `.sisyphus/plans/office-content-preview-avalonia.md` 补充两条路线：
    - DOCX 表格内容提取（纯文本分隔符 A / Mammoth→HtmlRenderer B）
    - Markdown 表格渲染补齐（原生 Grid B 路线 A / HtmlRenderer B）
  - 交叉引用 `html-preview-webview-fallback.md` Task 5（已有 BuildTable 方案）
  - Build 0 errors, 0 warnings

**2026-07-21** — Office 文档内容预览（DOCX/XLSX/PPTX 纯文本+表格+文本）
  - NuGet: 添加 DocumentFormat.OpenXml 3.5.1 + ClosedXML 0.105.0
  - DOCX: 左右分栏（GridSplitter + 大纲缩进 + 全文 TextBlock），点击大纲条目跳转到对应位置；大文件保护（>50MB）；无标题回退提示
  - XLSX: ClosedXML → DataTable → DataGrid（首工作表，100 行 × 100 列，首行为列名）；空/密码保护 gracefully handle
  - PPTX: 手动 ZipFile → XDocument → a:t 元素提取 → 幻灯片文本列表；纯图片幻灯片回退显示"（此幻灯片无文字）"
  - PreviewType 枚举拆分：Office → Docx/Xlsx/Pptx，扩展名→PreviewType 映射、魔数映射、ShowPreviewAsync 分发三路同步更新
  - 本地化: 添加 13 条 Preview_Docx/Preview_Xlsx/Preview_Pptx 键（zh-CN + en）
  - Build 0 errors, 0 warnings；Commit 4f02074

**2026-07-21** — 修复文件列表目录图标显示为未知文件图标
  - ArchiveService.cs: IsDirectory 分流调用 GetFolderIcon()（Entries 集合）
  - MainWindowViewModel.cs: PopulateEntries() 中 IsDirectory 分流调用 GetFolderIcon()（CurrentEntries 集合，原代码对所有条目无差别调用 GetFileIcon）
  - Build 0 errors

**2026-07-20** — Shell/COM 集成补齐：安装逻辑对齐 e41c45b + Settings 状态显示 + 首次运行注册
  - SettingsWindow 上下文菜单 tab 增加状态面板（ShellStatusText）和 Apply 按钮；ViewModel 新增 InstallShell/UninstallShell/ApplyShellCommands 真实实现（替换占位）
  - ShellIntegration.Install() 改为 COM-exclusive（仅在 COM 安装失败时安装级联菜单），对齐 WPF e41c45b 最终状态
  - CheckComStatus() 在 COM 未被 Explorer 加载时安装级联菜单作为兜底（而非仅 pending）
  - App.axaml.cs 新增首次运行处理（读取安装程序写入的 FirstRunShell/FirstRunAssoc 注册表标记，延迟到用户进程执行 Shell 注册）
  - 本地化：新增 12 条 Settings_ContextMenu_* 键（zh-CN + en）
  - Build 0 errors；Avalonia 测试 35 passed / 2 skipped

**2026-07-20** — Shell/COM 集成移植（ShellIntegration + 文件关联 + 右键菜单 + COM host）
  - 新建 3 个 Services 文件：ShellIntegration.cs（基类）、ShellIntegration.Assoc.cs（文件关联 per-extension）、ShellIntegration.Menu.cs（右键菜单 COM+cascade 注册）
  - CLI 全部原生化：--install-shell/--uninstall-shell/--install-assoc/--uninstall-assoc 直接调用 ShellIntegration，移除 WPF exe fallback
  - 文件关联 per-extension: 单个扩展名独立开关、MantisZip.{ext} 独立 ProgId、格式图标
  - COM host 部署：csproj 添加 CopyShellExtComhost MSBuild 目标，构建后自动复制 comhost.dll+ShellExt.dll+runtimeconfig.json
  - MenuIcons 10 个 .ico 从 WPF 复制到 Avalonia Resources/MenuIcons/
  - 本地化：Shell_* + ShellExt_* 19 条 key 添加（zh-CN + en）
  - 🐛 修复：HandleShellCommand 改用 Environment.Exit(0) 替代 desktop.Shutdown()，避免 Dispatcher 在 CLI-only 命令后 crash（InvalidOperationException）
  - 🐛 修复：MenuIcons .ico 文件未复制到输出目录（csproj 添加 None Include + CopyToOutputDirectory）
  - CLI 全部 4 个命令验证通过：注册表写入/清理完整（CLSID、shellex handlers、OpenWithProgids、ProgId、ContextMenu text）
  - Build 0 errors；Avalonia 测试 35 passed / 2 skipped；Core 测试 236 passed

**2026-07-19** — PasswordManagerWindow 图标补全 + 搜索栏图标 + 布局调整
  - AppIcons.axaml 新增 IconImport（arrowImport 24 filled）几何图标
  - IconEye/IconEyeOff 从 Regular 路径替换为 Filled 路径，16px 下清晰可见
  - 密码管理器工具栏全部按钮（Add/Edit/Delete/Export/Import/TogglePwd/Help）添加 PathIcon
  - 显示/隐藏密码按钮支持图标切换（IconEye ↔ IconEyeOff）
  - 密码管理器搜索栏添加搜索图标（IconSearch 覆盖在输入框左侧）
  - 搜索栏与工具栏位置交换（工具栏在上，搜索栏在下）

**2026-07-19** — 冲突对话框 UI 补全 + 计划审计
  - ConflictDialog：补全 Topmost、"覆盖较旧"/"覆盖较小"条件覆盖按钮、暂停/取消操作按钮、分隔线；删除多余 Cancel 按钮
  - CompressConflictDialog：补全 Topmost、"添加到压缩包"按钮（CompressConflictAction.Add）、暂停/取消操作按钮、分隔线；删除多余 Cancel 按钮
  - 动作行底部样式统一：PauseBtn/CancelAllBtn 从 Horizontal 改为 Vertical 布局，图标 22×22、FontSize 11
  - AppIcons.axaml 新增 IconPause 几何图标
  - 本地化 keys 补充（9 条 EN/ZH），`dotnet build` 通过，0 错误
  - 审计 `.sisyphus/plans/avalonia-wpf-diff-plan.md`：发现 P0-2（压缩选项 9 属性）、P0-3（魔数检测）已实现但未更新；更新汇总统计和已完成章节

**2026-07-18** — Phase 2 emoji→PathIcon 替换：预览按钮 + 对话框 + 文件树 + 过滤栏
  - 新增 11 个 Fluent UI 图标 Geometry：IconSubtract/Play/Pause/Previous/Next/ArrowRight/ArrowFitIn/EyeOff/Document/FontDecrease/FontIncrease
  - PreviewPanel.axaml：8 个预览工具栏按钮 emoji（− + ⟲ A− A+ ⏮ ⏯ ⏭）→ PathIcon
  - ConflictDialog + CompressConflictDialog：✏️ → IconEdit，⏭️ → IconArrowRight
  - PasswordDialog：👁 → IconEye；MatchedPasswordDialog：👁🔑🙈→IconEye/EyeOff/Key，✅→IconCheckmark
  - PreviewTreeNode.cs：📁→IconFolder，📄→IconDocument，📄⚠️→IconWarning（冲突节点改用标准警告图标）
  - ResultPreviewService.cs：📂 根节点名 → "解压预览"
  - ResultTreeView.axaml：📂→IconFolder，⚠️→IconWarning；改用 GeometryResourceConverter 动态绑定 PathIcon
  - MainWindow.axaml：⭐收藏夹标题去除 emoji，🔍搜索→IconSearch，⊘排除→IconProhibited
  - 至此 emoji→PathIcon 替换进度：50/75（已替换 66%）

**2026-07-18** — 图标测试窗口 + GIF 预览宽度 Bug 修复
  - 🐛 修复：GIF 预览 `ImageWidth` 缺失赋值（ShowGif 中只设了 ImageHeight 漏了 ImageWidth），导致显示为 `0 × height` 且 ZoomFit 异常
  - 新增 IconTestWindow（图标测试窗口）：列出程序所有图标的位置、名称、类型（emoji/PathIcon/PathIcon-待替换）、来源文件路径和状态
  - 新增 IconTestItem 模型、IconTestViewModel（含 70+ 条图标数据）、GeometryResourceConverter / StringNotEmptyConverter
  - 通过测试菜单可打开图标测试窗口，支持按名称筛选和分类统计
  - 为 .sisyphus/plans/emoji-to-pathicon.md 计划提供实时验证和数据补充

**2026-07-18** — emoji 图标替换为 PathIcon + 文件列表行图标改用系统原生
  - Phase 1：32 个 Fluent UI System Icons 矢量路径批量提取，创建 AppIcons.axaml 资源字典
  - 菜单、工具栏、状态栏、列头、设置分类、对话框等 ~50 处 emoji TextBlock → PathIcon
  - 保留 ~15 个无直接 Fluent UI 对应的 emoji（🚪 ⚙ 🤖 💬 🕐 等）暂不替换
  - Phase 2：新建 Win32IconProvider.cs，通过 SHGetFileInfo+System.Drawing 获取 Windows 系统原生图标
  - IconService 改为 Win32 优先策略：系统图标 > SkiaSharp 自绘图标（非 Windows 回退）
  - 新增 System.Drawing.Common 依赖项（仅 Windows 生效，非 Windows 自动走回退路径）
  - 🐛 修复：ApplySystemTheme 的 MergedDictionaries.Clear() 会清掉 AppIcons.axaml，Clear() 后重载

**2026-07-18** — 分卷大小 + 密码模式 RadioButton 横向排列
  - Advanced Tab 新增 "分卷大小" 区域：ComboBox（不分卷/1MB/10MB/50MB/100MB/650MB/4GB/自定义）+ 自定义大小 TextBox（仅自定义时显示）
  - 分卷选择持久化到 AppSettings（SplitSizeTag + CustomSplitSizeMB），下次打开自动恢复
  - 密码模式 RadioButton（密码库/新密码）从上下排列改为左右排列
  - 修复 `TextBox.Watermark` 废弃警告 → 改为 `PlaceholderText`

**2026-07-18** — 压缩设置窗口增强：加密 Tab 整理 + 压缩级别 ComboBox + 加密方法
  - 修复输出模式 RadioButton 偶发不同步：`RefreshOutputPathState()` 进入 Manual 模式时始终还原缓存路径
  - 压缩级别从 Slider 改为 ComboBox，共享数据源 `CompressionOptionData.LevelOptions`
  - 新增 Advanced Tab（Tab 2）存放动态格式选项面板，原 Password/Comment Tab 后移
  - 新增 ZIP 加密方法 ComboBox（AES-256/192/128/ZipCrypto）和 7z 加密文件头 Checkbox
  - "加密压缩包" checkbox 移至 Tab 顶部，取消勾选时隐藏全部密码内容（方案A）
  - 加密方式区域根据格式切换：ZIP→显示 AES 选择，7z→显示加密文件头，tar.gz→禁用 Encrypt 并自动清理
  - 新增空键保护（所有加密相关 key 已注册到 ViewModel LocalizedStrings，消除空白 checkbox）
  - 消耗的加密 key：Compress_EncryptionMethod / Compress_ZipEncryption / Compress_EncryptHeaders

**2026-07-17** — 紧凑度模式 + 上下文工具栏 + 结果预览面板
  - 紧凑度模式：Compact/Normal/Loose 三档，资源框架 + ApplyCompactness 运行时切换
  - 间距资源双键约定：SpacingXxx（double 用于 Spacing）、SpacingXxxThk（Thickness 用于 Margin/Padding）
  - 32 个 .axaml 文件全部替换为 DynamicResource 引用，告别硬编码间距
  - 目录树工具栏：展开/折叠全部、过滤器、显示/隐藏分隔符
  - 文件列表工具栏：选择/反选/展平目录/列排序/地址栏导航
  - 3 列布局重构（树工具栏 ↔ 文件列表工具栏 ↔ 预览面板）
  - ResultTreeView 可复用控件 + 解压/压缩结果预览面板（冲突高亮/过滤器灰显）
  - 提取/压缩设置窗口改为 2 列布局带实时文件树预览
  - UI 调整（2026-07-17）：将目录树和文件列表工具栏的按钮设为紧凑正方形（ToolbarIcon），并把目录树工具栏的三颗按钮从 emoji 文本替换为纯 PathIcon（IconArrowUp / IconChevronLeft / IconPin），保留 ToolTip 以维持可访问性。

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

**2026-07-16** — 图片/ICO 预览透明棋盘格背景切换
  - ViewModel 新增 `IsTransparencyBgShown`/`ToggleTransparencyBgCommand`/`HasTransparencyControls`
  - PreviewPanel.axaml 新增 `DrawingBrush` 棋盘格画刷（8×8 平铺）
  - 工具栏新增 🏁 按钮（绑定 `ToggleTransparencyBgCommand`，图片和 ICO 画廊可见）
  - Image/GIF 预览 Image 控件用 Grid 叠加棋盘格 Rectangle，`IsVisible` 绑定 `IsTransparencyBgShown`

**2026-07-16** — PDF 预览性能优化：限制渲染分辨率 + 隐藏工具栏
  - `ShowPdfAsync` 中获取 PDF 页面原始尺寸（PdfPig GetPage），计算合适的缩放比例限制渲染最大宽度 1920/高度 1080
  - `LoadPdfPageAsync` 改用动态 `_pdfRenderScale` 替代硬编码 1.0f
  - 避免大页面 PDF（如键盘图海报）渲染出数百 MB 的超大位图
  - PDF 改为元数据格式 `IsToolbarVisible = false`，对齐其他元数据格式

**2026-07-16** — PDF 预览渲染完成前保留加载状态
  - 将 PreviewType/FormatMetadata/PreviewHeaderText 等 UI 设置移到渲染完成后设置
  - 避免 `OnPreviewTypeChanged` 提前关闭 `IsLoadingPreview` 导致渲染期间显示空白

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

#### v0.4.5 (2026-08-03) AGENTS.md 新增 ComputeDirectoryStats 目录聚合契约说明
  - 新增「Directory aggregation — ComputeDirectoryStats」小节：`DirStats` 字段语义（递归子树和 / Count 仅文件 / NewestModified 忽略 MinValue）、消费者（Avalonia `PopulateEntries`）、与 ResultTreeView `CalculateDescendantStats` 的差异（`TotalDescendantCount` 含目录、无压缩后大小/日期）
  - 涉及文件：`AGENTS.md`（纯文档，无代码变更）

#### v0.4.5 (2026-08-03) 目录聚合统计 DirStats 增加 NewestModified
  - `DirStats` 记录新增 `DateTime NewestModified` 字段（Count/Size/CompressedSize/NewestModified）
  - `ComputeDirectoryStats` 同一趟遍历聚合子树最新文件修改时间（`Max`，`DateTime.MinValue` 文件不参与），递归累加语义不变
  - 涉及文件：`ArchiveEntryLister.cs`；新增 `FileListFilterTests.cs` 5 个用例（子树和 / 压缩和 / 最新日期忽略 MinValue / 文件计数 / 根文件与空目录无条目）
  - Core 241/241 测试通过，构建 0 errors

#### v0.4.5 (2026-08-03) ArchiveEntryExtractor 支持纯 GZip 单文件与 ISO 单条目提取
  - `ExtractEntryAsync`：新增 `.gz` 单文件分支（`ExtractGZipEntry`，GZipStream 直接解压；`IsPlainGZipFile` 判定 `.gz` 且非 `.tar.gz`），修复拖拽解压 .gz 抛 `InvalidFormatException`（TarReader 无法解析纯 gzip 流）
  - `ExtractEntryAsync`/`ExtractHeadAsync`/`ExtractTailSync`：`ArchiveFormat.Iso` 并入 SharpSevenZipExtractor 分支，修复 ISO 单条目提取/头部提取抛 `NotSupportedException`（7z.dll 原生支持 ISO9660，与 `SevenZipEngine.CanHandle(Iso)` 一致）
  - 涉及文件：`ArchiveEntryExtractor.cs`
  - 构建 0 errors；实测 .gz/.iso/tar.gz 提取成功（WPF 拖拽解压同源受益）

#### v0.4.5 (2026-07-31) FolderNode 新增 OnPropertyChanged 受保护方法
  - `FolderNode` 新增 `protected void OnPropertyChanged(string)`，供 PreviewTreeNode 继承使用
  - 涉及文件：`ArchiveTreeBuilder.cs`
  - 构建 0 errors

#### v0.4.5 (2026-07-27) CompressConflictResolver async 化 — ResolveConflictAsync
  - `CompressConflictResolver` 从同步委托改为异步（`Task<CompressConflictResolution>`）
  - `CompressService.ResolveConflict` → `ResolveConflictAsync`，所有调用处 `await`
  - 涉及文件：`CompressConflict.cs`、`CompressService.cs`
  - 构建 0 errors，Core 236/236 测试通过

#### v0.4.5 (2026-07-27) 引擎异步冲突解析 — Task.Run(async) + await ResolvePathAsync
  - ZipEngine/SevenZipEngine/TarGzEngine 的 ExtractAsync/ExtractEntriesAsync Task.Run 改为 async() => await ResolvePathAsync
  - 涉及文件：`ZipEngine.cs`、`SevenZipEngine.cs`、`TarGzEngine.cs`
  - 构建 0 errors，Core 236/236 测试通过

#### v0.4.5 (2026-07-27) 异步冲突解析 API — ConflictResolverAsync + ResolvePathAsync
  - `ArchiveOptions` 新增 `ConflictResolverAsync`（`Func<FileConflictInfo, Task<FileConflictAction>>?`）
  - `FileConflictHelper` 新增 `ResolvePathAsync`，优先使用异步回调，退回到同步 ResolvePath
  - 涉及文件：`ArchiveEngine.cs`、`FileConflictHelper.cs`
  - 构建 0 errors，Core 236/236 测试通过

#### v0.4.5 (2026-07-20) Avalonia Shell/COM 集成—CopyShellExtComhost MSBuild 目标
  - MantisZip.UI.Avalonia.csproj 添加 CopyShellExtComhost 构建目标（AfterTargets Build/Publish）
  - 使用硬编码路径引用 ShellExt 输出（避免跨 TFM ProjectReference 冲突：Avalonia net9.0 × ShellExt net9.0-windows10.0.17763.0）
  - 构建后自动复制 comhost.dll + MantisZip.ShellExt.dll + runtimeconfig.json 到 Avalonia 输出目录

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
| 上下文工具栏重构（目录树+文件列表） | [context-toolbars.md](.sisyphus/plans/context-toolbars.md) | v0.4.5 |
| 解压/压缩结果预览面板 | [result-preview-panel.md](.sisyphus/plans/result-preview-panel.md) | v0.4.5 |
| 紧凑度模式（Compactness Mode） | [compactness-mode.md](.sisyphus/plans/compactness-mode.md) | v0.4.5 |
| 预览两阶段加载（信息栏+内容分离） | [preview-two-phase-loading.md](.sisyphus/plans/preview-two-phase-loading.md) | v0.4.5 |
| Avalonia: Shell/COM 集成移植 | [avalonia-shell-com-integration.md](.sisyphus/plans/avalonia-shell-com-integration.md) | v0.4.5 |
| Avalonia Phase 10: WPF 功能补齐 | [avalonia-phase10-feature-parity.md](.sisyphus/plans/avalonia-phase10-feature-parity.md) | v0.4.5 |
| Avalonia: i18n 补齐 + 杂物清理 | [avalonia-i18n-and-cleanup.md](.sisyphus/plans/avalonia-i18n-and-cleanup.md) | v0.4.5 |
| 压缩解压文件筛选 | [file-filter-feature.md](.sisyphus/plans/file-filter-feature.md) | v0.4.5 |
| emoji 替换为 Fluent UI PathIcon + 文件列表行图标改用系统原生 | [emoji-to-pathicon.md](.sisyphus/plans/emoji-to-pathicon.md) | v0.4.5 |
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
