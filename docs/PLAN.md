# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-10-16  
**当前版本**: 0.5.2

---


## 待实现设计方案

以下功能已有独立方案设计文档（`.omo/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P1** | Win11 一级右键菜单 | [win11-first-level-menu.md](.omo/plans/未开始/win11-first-level-menu.md) | 🔴高 | 1-2周 | IExplorerCommand 实现，HKLM 提权注册，双接口共存 |
| **P1** | 新增压缩格式（BZip2/XZ/Zstd/Brotli + 7z.dll 只读解锁） | [new-format-support.md](.omo/plans/进行中/new-format-support.md) | 🟡中 | 11-16h | 🟡 部分完成（2026-08-20 核实）：Core 侧 TAR 裸格式/GZip 单文件压缩已就绪（`TarGzEngine.CompressAsync` 按扩展名分流 `.tar`→无压缩层、`.gz`→单文件 GZipWriter）+ 文件关联 AssocTar/AssocGz 默认 true 已放开；**UI 压缩格式下拉未放开**（`ArchiveFormatValues = ["zip","7z","tar.gz"]`，TAR 裸格式/GZ 单文件待 UI 放开关）。后续：BZip2 → XZ → Zstd（SharpCompress 0.48.1 内置）→ Brotli（.NET 内置 BrotliStream）→ 7z.dll 只读格式解锁（11 种） |
| **P1** | 自包含体积优化（Avalonia 迁移后） | [selfcontained-size-optimization.md](.omo/plans/未开始/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 三步渐进：InvariantGlobalization → 保守修剪 → 激进修剪，目标降至 20–25 MB |
| **P1** | 文件关联失效修复（悬空 UserChoice + 测试污染真实注册表） | [file-assoc-persistence-fix.md](.omo/plans/未开始/file-assoc-persistence-fix.md) | 🟡中 | 3.5-5h | 📋 2026-10-15 立项（现场注册表取证）：`.zip` 关联"随机失效"根因——卸载/`dotnet test` 删掉 per-format ProgId（`MantisZip.Zip`）却从不清理指向它的 `UserChoice` → 悬空默认（双击回退内置、图标失效、"打开方式"无 MantisZip）；本机由 `ShellIntegrationAssocTests` 直接读写真实 HKCU 触发。方案：A 注册表访问收口 `RegistryHost` + 测试沙箱隔离 / B 卸载清悬空 UserChoice / C 修 DefaultIcon 清理读错键 / D 清遗留 `OpenWithProgids\MantisZip.Archive` 值 / E 启动自愈重建缺失 ProgId / F 安装包 `RunOnceId` 硬化 |
| **P1** | ZIP 损坏检测（CRC 校验）修复 | [zip-crc-validation.md](.omo/plans/进行中/zip-crc-validation.md) | 🟢低 | 3-5h | 🔄 实施中（2026-10-15）：根因=引擎用 `OpenEntryStream()` 裸流不校验 CRC（SharpCompress 校验只挂在官方 `WriteTo/WriteToAsync` + `ExtractionOptions.CheckCrc` 路径；0.48.1 尚无该 API，故非版本问题、换版本无解）。已改 `ZipEngine` 测试 + 四处解压走官方校验路径、新增 `EntryFailureReason.CrcMismatch` + 三语文案 + 4 条回归测试（Core 全量 684 通过 / 0 失败）；Avalonia 全量测试 30 失败为增量构建毒化（规则 17），待全量重建复验 |
| **P1** | ZIP 多线程压缩方案 D（自适应 + 多线程第四个选项） | [parallel-compress-plan-d.md](.omo/plans/未开始/parallel-compress-plan-d.md) | 🟡中 | 4-5h | 自适应压缩新增"自适应 + 多线程"选项：Store 类直接写入 + 需压缩类 SharpSevenZip mt=on；UI 联动（格式目录/用户规则/多线程提示可见性）；实验性标签 + 帮助弹窗 |
| **P2** | ZIP 多线程压缩方案 A（分组并行 + 多级别） | [parallel-compress-plan-a.md](.omo/plans/未开始/parallel-compress-plan-a.md) | 🟡中 | 6-8h | 按级别分组 → 各组独立 ZipWriter 并行 → 合并 ZIP，完整多级别自适应 + 多线程。方案 D 完成后实施 |
| **P2** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.omo/plans/未开始/compression-estimator.md) | 🟡中 | 4-5h | 压缩前估算大小/耗时 |
| **P2** | Winget 发布 | [winget-publishing.md](.omo/plans/未开始/winget-publishing.md) | 🟢低 | 1-2h | 发布到 Windows Package Manager 社区仓库；首次手动提交后 CI 自动化 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.omo/plans/未开始/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup → WiX MSI 迁移 |
| **P2** | RAR 压缩（外置 rar.exe） | [rar-compression.md](.omo/plans/未开始/rar-compression.md) | 🟡中 | 8-10h | 通过已安装的 WinRAR 实现 RAR 压缩（含 SevenZipEngine 注册冲突处理） |
| **P2** | 快速预览与渐进式加载 | [preview-quick-modes.md](.omo/plans/未开始/preview-quick-modes.md) | 🟡中 | ~25h | 三种模式（快速/渐进/完整），叠加在已实施的两阶段加载（Phase 2）之上。**Avalonia-first（规则 11，WPF 不做 UI 适配）**；☑️ 2026-08-06 计划修正：WPF 先行→Avalonia-only、HTML 现状（ReverseMarkdown→Markdig 已实现）校准、设置项与现有 `MaxTextPreviewBytes`/`MaxTablePreviewRows` 整合（不新增重复字段）、CTS+`_previewLoadVersion` 双取消机制、DBF/LNK/STL/GZ 等 Unsupported 格式降级为独立前置、Markdown 渐进降为 ~2h（控件树渲染已实现）、总工时 27h→25h（不含 🔴 格式基础预览前置） |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.omo/plans/未开始/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到… |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.omo/plans/未开始/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| **P2** | 进度条分段着色（按压缩包状态） | [progress-bar-segments.md](.omo/plans/未开始/progress-bar-segments.md) | 🟡中 | 3-4h | 自定义 `SegmentProgressBar` 控件替换普通 `ProgressBar`，按批处理项状态分段着色（红=失败/绿=成功/青=跳过/蓝=进行中），动态预算制缝隙；`BatchStatusConverters.GetColor()` 抽取公共颜色映射 |
| **P2** | 压缩/解压出错信息增强（整包错误本地化+详情透传+原因分布） | [error-info-enhancement.md](.omo/plans/未开始/error-info-enhancement.md) | 🟡中 | 10-13h | 📋 2026-10-15 立项（`progress-window-status-line.md` 的续作，修订其 D2「不透传 ex.Message」决策——仅加 `EntryErrorDetail` 字段进 tooltip，摘要正文格式不变）。P1 修 bug（App.axaml.cs 两处硬编码中文违反规则 13 + TarGz/7z Failed 事件缺 FileTotalBytes）；P2 UI 增强（整包错误 ex.Message 直传改 `WholeArchiveErrorLocalizer` 本地化映射、红/黄摘要段头失败原因分布行）；P3 Core 契约扩展（`ArchiveProgress.EntryErrorDetail` 三引擎 catch 填充 + `IoErrorHintResolver` 按 Win32 码细分磁盘满/占用/路径过长提示，不扩 `EntryFailureReason` 枚举）。10 任务 4 波次（P1 双任务可并行），TDD 覆盖 localizer/VM 分布行/引擎契约/hint resolver；P4 高成本项（详情面板/重试/导出）另立 Follow-ups。**任务 11（✅ 已实施 2026-10-15）**：打开阶段损坏/加密分类——损坏 7z/RAR 被误判「需密码」弹框，SharpSevenZip 对加密头与损坏抛同异常（同 HRESULT 0x80131500 同消息）无法区分，方案 B 改用字节级结构探测（`Core/Utils/ArchiveHeaderProbe`：7z StartHeader CRC/越界 + RAR 签名 + 分卷求和），`IsPasswordRelatedError` 增 path 参兜底；损坏语料入库 `tests/corpus/corrupt-archives/`（53 样本）+ harness `tools/CorruptProbe/` |
| **P2** | 压缩文件名后缀模板 | [filename-suffix-template.md](.omo/plans/未开始/filename-suffix-template.md) | 🟢低 | 2-3h | `{date}`/`{datetime}`/`{seq}` 占位符替换，防同名覆盖 |
| **P2** | 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.omo/plans/未开始/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API(通用) 两层提取嵌入缩略图；完成后可扩展文件列表缩略图模式 |
| **P2** | 文件列表缩略图查看方式 | [file-list-thumbnails.md](.omo/plans/未开始/file-list-thumbnails.md) | 🟡中 | 4-6h | 三种布局模式循环切换（详情 DataGrid / 平铺 UniformGridLayout / 内容自定义行），内容卡三档（图像类真实缩略图 128px / 文字类头部预览 / 其余系统图标），持久化 `AppSettings.FileListLayoutMode` |
| **P2** | 骨架态实时进度计数器 | [skeleton-progress-counter.md](.omo/plans/未开始/skeleton-progress-counter.md) | 🟡中 | 3-4h | 骨架加载期间每 0.5s 更新目录节点的文件数/大小（"加载中 1637 个文件，2.45GB"）；`IProgress<SkeletonProgress>` 回调 + `DispatcherTimer` 轮询 |
| **P2** | 预览"显示内容"开关 | [preview-show-content-toggle.md](.omo/plans/未开始/preview-show-content-toggle.md) | 🟡中 | 2-3h | 压缩/解压预览树新增"显示内容"开关（ResultTreeView 工具栏）：关闭后只显示压缩包/目标路径骨架，内容彻底隐藏不可展开，同时跳过 `BuildDirectoryNode` 磁盘递归扫描（大型源目录预览加速）；持久化 `AppSettings.PreviewShowContent`；摘要栏隐藏时显示输出路径 |
| **P2** | 文件选择器去弹窗化（CustomFilePickerControl 内嵌） | [embedded-file-picker.md](.omo/plans/未开始/embedded-file-picker.md) | 🟡中 | 1-2天 | 📋 2026-10-08 立项：选择器内嵌进压缩/解压设置窗；PickItems 默认简单模式（无复选框、单击即选、Shift 范围选、覆盖语义、双击目录进入并清空），高级开关=复选框+跨目录记忆；去右侧累积面板+折叠式已选栏，已选源文件列表置 picker 下方与选择双向同步；内嵌 picker 左栏 QuickPathControl 左右布局；压缩窗 Tab 重排（输出设置最前/添加文件/压缩设置=原高级吸收原常规/文件过滤）；解压窗对称（+添加压缩包页、输出设置页内嵌）；拖拽兜底与「解压到」统一走 ExtractSettingsWindow（第二个 Tab 按场景形态替换：「添加压缩包」或「选择要解压的文件」，保留指定条目语义）；三步走：①抽 CustomFilePickerControl 纯重构零改动 ②压缩窗接入 ③解压窗接入；⚠️ 不做新窗口，不动 CustomFilePickerDialog 静态入口与系统浏览豁免 |
| **P2** | 扩展预览格式支持 | [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) | 🟡中 | ~25h | 🟡 部分完成（3.5/6 阶段）：Phase 0-1 工具栏+信息面板 ✅；Phase 2 快速出货格式（Torrent/PE/PDF/WAV/FLAC/SQLite/ISO/MP3）✅；Phase 3 中等价值格式部分完成（SVG/字体/ICO/Office ✅，LNK/DBF/EPUB/字幕/STL ⬜）；Phase 4 MKV/WebM 元数据 ✅，其余 ⬜；Phase 5 部分完成（ExtractHeadAsync/IsSevenZipSolid ✅）；Magick.NET/LibVLC 插件化待实施 |
| **P2** | 浮动/停靠预览面板 | [floating-preview-panel.md](.omo/plans/进行中/floating-preview-panel.md) | 🟡中 | 8-10h | 🟡 部分完成（2026-10-16 核实）：Phase 2 四位置停靠切换已在 Avalonia 实现（`ApplyPreviewPosition`、`AppSettings.PreviewPosition`、设置窗口 ComboBox，2026-08-13 落地）；Phase 1 Button 触发浮动独立窗口+位置记忆、Phase 3 拖拽分离+边缘吸附停靠（DockingManager + DockingOverlay）未实现 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.omo/plans/未开始/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚 |
| **P2** | 原生 Win32 启动 Splash | [startup-native-splash.md](.omo/plans/未开始/startup-native-splash.md) | 🟡中 | 3-4h | **Windows-only**（跨平台移植时归入「砍掉」类，`Show()` 首行 `OperatingSystem.IsWindows()` 护栏使挂接点无需改动）；覆盖进程冷启动（Avalonia 初始化前 ~1-2s）的无反馈期；已实施一期 A+B+C：`--compress` IPC 收集期间立即显示纯文字弹窗 `CollectingWindow`「正在收集文件…」（无按钮，避免 ProgressWindow 让用户误以为压缩已开始）+ `MainWindow` 增加 `IsLoading` 加载遮罩「正在打开压缩包…」+ `--extract`（解压到…）立即弹窗、条目列表后台加载（消除弹窗前最长 3s 空白期），本计划用纯 Win32 splash（P/Invoke，独立消息泵，**`IsBackground=true`** 防止进程挂起）覆盖冷启动时段；**覆盖所有启动路径**（无参普通启动实测 TOTAL≈2160ms + 全部 CLI 慢路径，仅 `--install-shell`/`--help` 等非 UI 命令排除），无参启动用**新增 key** `Status_Starting`（「正在启动…」，三语成对），CLI 按命令选现有 key（`--compress` 系用 `App_CompressCollecting`）；关闭时机为各场景**终端窗口**（无参为 `MainWindow`，`--compress` 为 `CompressSettingsWindow` 而非首个 `CollectingWindow`，见计划内终端窗口枚举表），`CollectingWindow` 保留作 splash 初始化失败的降级兜底；✅ 前置已就绪：`startup-preview-defer` 已完成（2026-10-10），`StartupTimer` 可直接复用，splash 显示阈值以其冷启动数据（新基线 TOTAL≈2160ms，见计划文件「阶段二验证结果」）为准 |
| **P2** | 面包屑地址栏（PathBreadcrumb） | [path-breadcrumb.md](.omo/plans/未开始/path-breadcrumb.md) | 🟡中 | 6-8h | 三处地址栏（主窗口虚拟路径 / QuickPathPicker / CustomFilePickerDialog）统一改造为资源管理器式面包屑：段点击直达 + 点末尾空白/Ctrl+L 进编辑态（保留 AutoCompleteBox 补全）+ 段数阈值折叠 + 虚拟根段 📦；通用 `PathBreadcrumb` 控件，`NavigateRequested` 事件保持宿主导航单一事实来源；第一版不做分隔符同级目录下拉（预留 `EnumerationRequested`） |
| **P2** | Core 层临时目录根可注入（便携模式延伸） | [core-temp-root-injectable.md](.omo/plans/未开始/core-temp-root-injectable.md) | 🟡中 | 2-3h | 📋 2026-08-20 立项：Core 层 **4 处** `%TEMP%\MantisZip` 硬编码（ZipEngine×2、SevenZipEngine×2）便携模式下仍写系统 TEMP；方案 A：`TempPaths` 静态类 + `TempRootOverride` 注入（对齐 `CoreLog.RedactOverride` 模式），UI 启动时注入 `AppSettings.GetTempDir()`，未注入时路径逐字节不变 |
| **P2** | Avalonia CLI 解压后打开文件夹对齐 WPF | [cli-extract-open-folder.md](.omo/plans/未开始/cli-extract-open-folder.md) | 🟡中 | 2-3h | 📋 2026-08-20 立项：WPF CLI `--extract-here`/`--extract-to-name` 单文件模式 `OpenFolderAfterExtract` 时经 `ResolveSmartOpenPathAsync` 打开文件夹（`App.Extract.cs:614-619`），Avalonia CLI 只解压不打开；方案：`RunExtractCliAsync` 解压成功后复用 `SmartOpenPathResolver` 打开；多文件批处理保持不开；实施前需确认 WPF `--extract`/`--extract-smart` 是否同样打开 |
| **P2** | 文本预览语法高亮（Avalonia） | [text-preview-syntax-highlighting.md](.omo/plans/未开始/text-preview-syntax-highlighting.md) | 🟡中 | 4-5h | ✅ 2026-09-21 重写为 Avalonia 版（原 WPF AvalonEdit 方案废弃）：AvaloniaEdit 12.0.0 + TextMate（`AvaloniaEdit.TextMate`，VS Code 语法全集覆盖 `TextExtensions` 40+ 扩展名，无需自研 XSHD），TextEditor 替换 ScrollViewer+TextBox，亮/暗主题 `SetTheme` 联动，扩展名→魔数→JSON/INI 结构特征三级语言识别（配合 2026-09-21 已落地的文本内容检测）；Phase 1 功能补齐：TextMate 语法分发 |
| **P3** | 设置项无消费者修复（密码匹配提示/默认解压位置/级联菜单开关） | [settings-unwired-keys.md](.omo/plans/进行中/settings-unwired-keys.md) | 🟡中 | 3-5h | 📋 2026-09-27 立项：全量排查「设置写了没人读」10 项中剩余 3 项——①`ShowPasswordMatchNotification` 无消费者（⏸️ 2026-09-27 评估后用户决策暂不动：根因是匹配时机已前移至打开时/解压对话框，进度窗口消费链断；GUI 侧已冗余、CLI 侧 `ResolveCliPassword` 静默是真实缺口；裁剪版移植约 1-2h、废弃需删 5+ 处成本不低于接线，结论详见计划文件内「评估结论」小节，启动时直接按裁剪版执行）；②`ExtractDestination` 无消费者（⏸️ 2026-09-27 评估后用户决策暂不动：核实 WPF 终版 same-dir/desktop 本就跳过对话框、主路径已富对话框化；推荐 b——三个入口预填目的地（`ExtractSettingsViewModel` ctor / `ExtractTo` / `ExtractSelectedTo` defaultDest），CLI `--extract` 自动生效，含镜像测试约 1-2h，c 跳过对话框与已有「解压到此处」命令重复且丢失过滤/单次冲突覆盖，结论详见计划文件内「评估结论」小节，启动时直接按 b 执行）；③`EnableCascadingMenu`（✅ 2026-09-27 评估+修复：该字段在 Avalonia/WPF 后期均不存在——WPF 已于 `5b431fb` 替换为已接线的 `EnableDynamicMenu`，COM 版仅支持级联形态、verb 模式已废弃，计划选项 a/b/c 基于错误前提全部作废；实际缺陷仅为 AGENTS.md 两处陈旧描述，已修正设置清单与「Two modes」段落，零代码改动，本项闭环）。其余 7 项（DefaultLevel/DefaultFormat/OpenFolderAfterExtract/DeleteArchiveAfterExtract/MaxRecentFiles/CleanTempOnStartup/AllowElevation）已于 2026-09-27 同批修复 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.omo/plans/未开始/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 压缩包管理器（历史 / 标签 / 目录树筛选） | [archive-manager.md](.omo/plans/未开始/archive-manager.md) | 🟡中 | 2-3天 | 独立窗口记录所有处理过的压缩包：手动标签、智能分组（最近/常用）、目录树筛选、缩略图预览；SQLite 持久化 |
| **P3** | 原生图标 DLL | [icon-dll.md](.omo/plans/已归档/icon-dll.md) | 🟡中 | 2-3h | ☑️ 2026-08-20 核实：部分实现（机制不同）——图标已嵌入 `MantisZip.ShellExt.dll` 托管资源（11 个 .ico EmbeddedResource，`GetIconForCommand` 运行时读取，无路径依赖）；计划的「原生 .rc 资源 DLL（MantisZip.Icons.vcxproj）」方案未实施，效果已达成 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.omo/plans/进行中/preview-modular-providers.md) | 🟡中 | 3-4h | 🟡 部分完成（2026-10-16 核实，Task 1/4）：`ITableDataProvider` 已纳入 Core 抽象层（`SqliteDataReader` 实现，提交 1cef18f），但 UI SQLite 预览仍直连 `Microsoft.Data.Sqlite`、接口零消费者；模块拆分/反射加载/安装包组件未做 |
| **P3** | 文件列表自定义列 | [custom-columns.md](.omo/plans/未开始/custom-columns.md) | 🟡中 | 4-6h | 可自定义显示文件元数据列（文档标题、图片尺寸等） |
| **P3** | 冻结列（水平滚动时列固定） | [frozen-column.md](.omo/plans/未开始/frozen-column.md) | 🟢低 | 1-2h | 右键列标题冻结/取消冻结，分隔线，设置持久化 |
| **P2** | Office 图片预览 | [office-content-preview-avalonia.md](.omo/plans/已完成/office-content-preview-avalonia.md#office-图片预览) | 🟢低-🟡中 | 2-10h | 三方案渐进：A=PPTX 图片（~50行，复用 Canvas）、B=+DOCX 行内图片（~150行）、C=浮动定位（不推荐）。待决定实施方案 |
| **P3** | ICO 文件自身图标显示 | [ico-file-icon-extract.md](.omo/plans/未开始/ico-file-icon-extract.md) | 🟢低 | 2-3h | ico 文件列表显示自身嵌入图标 |
| **P3** | 右键菜单目录结构预览 | [context-menu-tree-preview.md](.omo/plans/未开始/context-menu-tree-preview.md) | 🔴高 | 6-8h | COM 菜单中展示压缩包顶层文件树 |
| **P1** | Avalonia: UI 功能补齐 | [avalonia-ui-feature-parity.md](.omo/plans/已完成/avalonia-ui-feature-parity.md) | 🟡中 | 27/29 完成，2 项待 GUI 验证 | Elevation×3、Favorites×2、QuickPath×2 等 11 个对话框、2 个控件、1 个转换器（2 项阻塞于 GUI 测试） |
| **P1** | 自动更新检测 | [auto-update.md](.omo/plans/未开始/auto-update.md) | 🟡中 | 4-6h | GitHub Releases API 版本检查、AboutWindow 更新 Tab、UpdateAvailableDialog、设置开关、单元测试 |
| **P2** | 解压多压缩包按来源目录分组 | [result-preview-panel.md](.omo/plans/已完成/result-preview-panel.md) | 🟡中 | 2-4h | 结果预览面板遗留①：当前多压缩包条目合并平铺，改为按来源目录分组 + 压缩包壳节点（详见文档「未实现项（后续可做）」） |
| **P2** | 结果预览截断占位符点击展开 | [result-preview-panel.md](.omo/plans/已完成/result-preview-panel.md) | 🟢低 | 1-2h | 结果预览面板遗留②：当前截断为静态"…"文本，改为点击就地展开完整子节点 |
| **P2** | 自研 ZIP 引擎（Per-entry 压缩 + 多线程） | [custom-zip-engine.md](.omo/plans/未开始/custom-zip-engine.md) | 🟡中 | 5-6天 | Per-entry 压缩级别/方法控制（产品卖点）+ 多线程并行压缩；纯 .NET 实现（DeflateStream），零外部依赖；支持 AES-256 加密；后续可开源为独立库 ZipPerEntry |
| **P2** | 图标主题系统①基础设施（IconView + DrawingImage 迁移） | [icon-theme-infra.md](.omo/plans/未开始/icon-theme-infra.md) | 🟡中 | 12-16h | 📋 2026-10-15 立项（拆分第 1/3）：图标契约 `Geometry`→`DrawingImage`；新增 `IconView`（复用 `TextElement.Foreground` 继承 + `SourceKey` 资源订阅刷新）；三层字典骨架 + `AppSettings.SelectedIconTheme`；182 处 `PathIcon`→`IconView` 迁移（交付标准：A 套零视觉差异）。规格：[2026-10-15-icon-theme-system-design.md](docs/superpowers/specs/2026-10-15-icon-theme-system-design.md) |
| **P2** | 图标主题系统②导入包 + 单图标覆盖 + 设置 UI | [icon-theme-packs.md](.omo/plans/未开始/icon-theme-packs.md) | 🟡中 | 16-22h | 📋 2026-10-15 立项（拆分第 2/3）：SVG 图标包（文件夹/ZIP）导入 + 覆盖率校验（结构错误原子拒绝、渐变降级跳过 D-C，第②层）；单图标覆盖（第③层）；设置「外观」Tab 图标风格分区 + `IconManagerWindow`。依赖① |
| **P3** | 图标主题系统③C/D/E/F/G 内容生产 + IconGen 多套 | [icon-theme-sets.md](.omo/plans/未开始/icon-theme-sets.md) | 🔴高 | 20-30h | 📋 2026-10-15 立项（拆分第 3/3）：IconGen 多套产出；Phosphor Bold/Duotone + 彩色/手绘；6 字典 × 116 键覆盖率契约；G 强调色 DynamicResource 决策门；About 页署名。依赖① |
| **P4** | 外部工具视频元数据 | — | 🟢低 | 2-3h | ffprobe 集成 |
| **P2** | 跨平台移植（macOS / Linux） | [cross-platform-port.md](.omo/plans/未开始/cross-platform-port.md) | 🟡中大 | 6-8周 | ✅ WPF→Avalonia 迁移已完成（Phases 0-10）；实施计划含 Phase 0 基础设施（CI+路径适配）→ Phase 1 引擎适配（7z/RAR 跨平台）→ Phase 2 UI 适配（macOS/Linux 专项）→ Phase 3 打磨发布；需要决策：7z 压缩策略（禁用/p7zip CLI）、拖拽解压降级、右键菜单取舍 |

> **执行顺序**（5 项计划有依赖链）：`nuget-dependency-upgrade`（✅ 已于 v0.5.0 完成）→ `compression-estimator`（依赖升级后的 per-entry 级别/多实例并行；`compression-performance-optimization` 已于 v0.5.0 完成）→ `progress-window-enhancement`（ProgressWindow 布局重构）→ `progress-bar-segments`（在重构后的 Row 4 上替换 SegmentProgressBar）。时间估算可在 progress-window-enhancement 中先用简单实时速度方案，compression-estimator 完成后再升级为加权融合方案，零返工。额外受影响计划：`cross-platform-port`（依赖 SharpCompress 升级后跨平台验证）、`selfcontained-size-optimization`（TrimmerRootAssembly 需随升级更新）、`context-menu-tree-preview`（SharpCompress API 升级后需验证条目列出兼容性）。`archive-rename-entry`/`rar-compression`/`startup-native-splash` 为软依赖，ProgressWindow 增强会自动改善体验。


---


## 已废弃方案

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |废弃原因 |
|--------|------|----------|:----:|:--------:|------|------|
| **P3** | VirtualFileDataObject | [virtual-file-data-object.md](.omo/plans/已归档/virtual-file-data-object.md) | 🔴高 | 6-8h | COM 原生 IDataObject 替代 WPF OLE 桥 | 跨平台移植（Avalonia）后不再依赖 WPF OLE 桥，无 OLE CF_HDROP bug，VFDO 无存在必要 |
| **P4** | 拖拽提取目标检测 | [drag-drop-marker-target.md](.omo/plans/已归档/drag-drop-marker-target.md) | 🟡中 | 1-3h | Marker 文件探测拖放目标目录 | 被 [drag-drop-direct-extract.md](.omo/plans/已完成/drag-drop-direct-extract.md) 取代——WindowFromPoint+ShellWindows 更直接可靠 |
| **P3** | 预览格式识别（旧方案） | [preview-format-detection.old.md](.omo/plans/已归档/preview-format-detection.old.md) | 🟡中 | — | 预览格式识别旧设计 | 已拆分为 [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) + [preview-magic-detection.md](.omo/plans/已完成/preview-magic-detection.md) |
| **P3** | Office 内容预览（WPF 版） | [office-content-preview.md](.omo/plans/已归档/office-content-preview.md) | 🟡中 | — | WPF 版 Mammoth→HTML→WebView2 方案 | 被 [office-content-preview-avalonia.md](.omo/plans/已完成/office-content-preview-avalonia.md)（Avalonia 版）取代 |
| **P3** | 资源管理器式路径切换弹窗 | [explorer-path-switcher.md](.omo/plans/已归档/explorer-path-switcher.md) | 🟡中 | — | WPF 时代 Ctrl+G 路径速选弹窗 | 被 QuickPathControl 系列演进取代（[quickpath-unified.md](.omo/plans/已归档/quickpath-unified.md) → [quickpath-control-redesign.md](.omo/plans/已归档/quickpath-control-redesign.md) → CustomFilePickerDialog） |
| **P3** | QuickPathControl 原始组件计划（WPF 版） | [quick-path-control.md](.omo/plans/已归档/quick-path-control.md) | 🟡中 | — | WPF 版路径速选控件原始设计 | 被 [quickpath-unified.md](.omo/plans/已归档/quickpath-unified.md) → [quickpath-control-redesign.md](.omo/plans/已归档/quickpath-control-redesign.md) → CustomFilePickerDialog 演进取代 |

---

## 跨平台移植影响分析（已完成，仅供参考）

> ⚠️ WPF→Avalonia 迁移已于 2026-08 完成（Phases 0-10），以下为迁移前的兼容性评估（2026-07-22），保留供参考。
>
> 跨平台（macOS / Linux）实施计划见 [.omo/plans/未开始/cross-platform-port.md](../.omo/plans/未开始/cross-platform-port.md)。

跨平台移植（WPF→Avalonia + 砍 ShellExt）对现有计划的影响分三类：

| 影响等级 | 数量 | 含义 |
|---------|:----:|------|
| 🟢 无影响 | 3 | Core 层纯 C# 逻辑，开箱即用 |
| 🟡 需调整 | 20 | Core 逻辑可复用，UI 层（XAML/控件）需移植到 Avalonia |
| 🔴 冲突 | 9 | 依赖 COM/注册表/Shell API/WPF 独占控件，需完全重写或平台替代方案 |

### 🟢 无影响（3 个）

`preview-modular-providers.md`、`selfcontained-size-optimization.md`、`winget-publishing.md`

### 🟡 需调整 — Core 可复用，UI 需移植（18 个）

`archive-diff.md`、`archive-rename-entry.md`、`auto-update.md`、`compress-preset.md`、`compression-estimator.md`、`custom-columns.md`、`extract-journal-undo.md`、`filename-suffix-template.md`、`font-preview-ligature.md`、`ico-file-icon-extract.md`、`metadata-panel-configurable.md`、`new-format-support.md`、`office-content-preview-avalonia.md`、`preview-quick-modes.md`、`progress-window-enhancement.md`、`avalonia-ui-feature-parity.md`、`avalonia-wpf-diff-plan.md`、`外部工具视频元数据（无计划文件）`

### 🔴 冲突 — 需完全重写或废弃（9 个）

`drag-drop-direct-extract.md`（Win32 Shell API，**已完成**）、`embedded-thumbnail-preview.md`（Shell 缩略图 API）、`frozen-column.md`（DataGrid 冻结列）、`icon-dll.md`（原生资源 DLL）、`msi-packaging-wix.md`（Windows Installer）、`rar-compression.md`（Windows 外置 rar.exe）、`win11-first-level-menu.md`（COM IExplorerCommand）、`context-menu-tree-preview.md`（COM HMENU）

### 关键发现

1. **🔴 的共性**：全部依赖 Windows Shell API（COM/注册表/P/Invoke/Shell32）或 WPF 独占控件/DataGrid 特定行为。跨平台后这些功能要么砍掉（COM 右键菜单、UAC 提权），要么需要 OS 级不同实现（Linux `.desktop` actions / macOS `NSExtension`）。
2. **🟡 的规律一致**：Core 层的解析器/算法/模型全是纯 C# 可复用，只有 UI 渲染层（WPF XAML + 控件）需要移植到 Avalonia 等价物。
3. **🟢 的 3 个计划**：基本是纯工具代码（正则、IO、字节操作）或发布配置，开箱即跨平台。
4. **最值得跨平台前实现的计划**：优先完成 🟢/🟡 计划（已有 `file-filter-feature.md` 等完成先例），积累跨平台经验后再攻坚 🔴 计划。
5. **Core 层遗留问题**：`PasswordManager`（DPAPI → 已实现 `AesGcmDataProtector` 抽象）和 `SevenZipEngine`（7z.dll Windows-only）已在跨平台准备中。

*此文档将随开发进度持续更新*


