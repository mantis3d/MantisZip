# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-10-20  
**当前版本**: 0.5.3

---


## 待实现设计方案

以下功能已有独立方案设计文档（`.omo/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P1** | Win11 一级右键菜单 | [win11-first-level-menu.md](.omo/plans/未开始/win11-first-level-menu.md) | 🔴高 | 1-2周 | IExplorerCommand 实现，HKLM 提权注册，双接口共存 |
| **P1** | 新增压缩格式（BZip2/XZ/Zstd/Brotli + 7z.dll 只读解锁） | [new-format-support.md](.omo/plans/进行中/new-format-support.md) | 🟡中 | 11-16h | 🟡 部分完成（2026-08-20 核实）：Core 侧 TAR 裸格式 / GZip 单文件压缩与文件关联已就绪；UI 压缩格式下拉尚未放开。后续按序增加 BZip2 → XZ → Zstd → Brotli，并解锁 7z.dll 只读格式（详见计划文档） |
| **P1** | 自包含体积优化（Avalonia 迁移后） | [selfcontained-size-optimization.md](.omo/plans/未开始/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 三步渐进：InvariantGlobalization → 保守修剪 → 激进修剪，目标降至 20–25 MB |
| **P1** | ZIP 损坏检测（CRC 校验）修复 | [zip-crc-validation.md](.omo/plans/进行中/zip-crc-validation.md) | 🟢低 | 3-5h | 🔄 实施中（2026-10-15）：根因=解压走 `OpenEntryStream()` 裸流绕过 CRC 校验；已改走官方校验路径并新增失败原因枚举 + 三语文案 + 回归测试（Core 684 通过），Avalonia 侧待全量重建复验（详见计划文档） |
| **P1** | ZIP 多线程压缩方案 D（自适应 + 多线程第四个选项） | [parallel-compress-plan-d.md](.omo/plans/未开始/parallel-compress-plan-d.md) | 🟡中 | 4-5h | 自适应压缩新增"自适应 + 多线程"选项：Store 类直接写入 + 需压缩类 SharpSevenZip mt=on；UI 联动（格式目录/用户规则/多线程提示可见性）；实验性标签 + 帮助弹窗 |
| **P1** | 压缩加速三开关 UI 收敛（Phase 1）+ 魔数检测/自适应规则实现（Phase 2） | [adaptive-compress-ui-convergence.md](.omo/plans/未开始/adaptive-compress-ui-convergence.md) | 🟡中 | P1 4-5h / P2 5-7h | 2026-10-18 拍板定稿：**Phase 1（4-5h）UI 收敛**——删「并行压缩组数」性能陷阱旋钮（引擎默认组数 `clamp(核数,4,16)`），隐藏魔数检测/自适应规则/魔数 hex 三个空转控件；改动 B（MT 依赖自适应）因双机实测数据矛盾暂缓，三开关保持独立无门控。**Phase 2（5-7h）** 实现魔数检测与自适应规则、接回隐藏 UI，新增 Task 12 本机基准测试（MT 收益按机器实测定夺）；拍板依据详见计划「拍板记录」「修订记录」。 |
| **P2** | ZIP 多线程压缩方案 A（分组并行 + 多级别） | [parallel-compress-plan-a.md](.omo/plans/未开始/parallel-compress-plan-a.md) | 🟡中 | 6-8h | 按级别分组 → 各组独立 ZipWriter 并行 → 合并 ZIP，完整多级别自适应 + 多线程。方案 D 完成后实施 |
| **P2** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.omo/plans/未开始/compression-estimator.md) | 🟡中 | 4-5h | 压缩前估算大小/耗时 |
| **P2** | Winget 发布 | [winget-publishing.md](.omo/plans/未开始/winget-publishing.md) | 🟢低 | 1-2h | 发布到 Windows Package Manager 社区仓库；首次手动提交后 CI 自动化 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.omo/plans/未开始/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup → WiX MSI 迁移 |
| **P2** | RAR 压缩（外置 rar.exe） | [rar-compression.md](.omo/plans/未开始/rar-compression.md) | 🟡中 | 8-10h | 通过已安装的 WinRAR 实现 RAR 压缩（含 SevenZipEngine 注册冲突处理） |
| **P2** | 快速预览与渐进式加载 | [preview-quick-modes.md](.omo/plans/未开始/preview-quick-modes.md) | 🟡中 | ~25h | 三种预览模式（快速/渐进/完整），叠加在已实施的两阶段加载（Phase 2）之上。**Avalonia-only（规则 11）**；已按 2026-10-20 修正版校准（设置项复用现有字段、DBF/字幕等 Unsupported 格式降级前置、GIF→AnimatedImage、ICO→IcoGallery、HTML WebView 双轨已落地、文件列表缩略图已独立成 `file-list-thumbnails.md`） |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.omo/plans/未开始/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到… |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.omo/plans/未开始/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| **P2** | 进度条分段着色（按压缩包状态） | [progress-bar-segments.md](.omo/plans/未开始/progress-bar-segments.md) | 🟡中 | 3-4h | 自定义分段进度条控件替换普通 `ProgressBar`，按批处理项状态着色（红=失败/绿=成功/青=跳过/蓝=进行中），动态预算缝隙 |
| **P2** | 进度窗口状态行 + 失败明细 + 关闭链统一 + 倒计时 + 解压后置开关 | [progress-window-status-line.md](.omo/plans/未开始/progress-window-status-line.md) | 🟢核心完成 | 12-16h | ✅ 核心实施完成（2026-10-13，Task 1–11），GUI 人工验收待做。内容：状态行下移 + `EntryFailureReason` 三语错误摘要（红/黄分层、可复制）+ 关闭链统一（三条件自动按下 📌）+ 自动关闭倒计时 + 解压后「打开目录/移入回收站」开关。设计拍板（D1–D19）与任务分解详情见计划文档及 spec |
| **P2** | 压缩/解压出错信息增强（整包错误本地化+详情透传+原因分布） | [error-info-enhancement.md](.omo/plans/进行中/error-info-enhancement.md) | 🟡中 | 10-13h | 🚧 进行中（progress-window-status-line 续作）：整包错误本地化映射、红/黄摘要补失败原因分布行、三引擎 `EntryErrorDetail` 透传 + Win32 错误提示细分。Task 1–8 与任务 11 已实施提交，任务 9 GUI 人工验收待做；修订决策（D2 透传等）详见计划文档 |
| **P2** | 压缩文件名后缀模板 | [filename-suffix-template.md](.omo/plans/未开始/filename-suffix-template.md) | 🟢低 | 2-3h | `{date}`/`{datetime}`/`{seq}` 占位符替换，防同名覆盖 |
| **P2** | 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.omo/plans/未开始/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API(通用) 两层提取嵌入缩略图；完成后可扩展文件列表缩略图模式 |
| **P2** | 文件列表缩略图查看方式 | [file-list-thumbnails.md](.omo/plans/未开始/file-list-thumbnails.md) | 🟡中 | 4-6h | 三种布局模式循环切换（详情/平铺/内容行），内容卡三档（图像缩略图/文字头部预览/系统图标），布局模式持久化 |
| **P2** | 骨架态实时进度计数器 | [skeleton-progress-counter.md](.omo/plans/未开始/skeleton-progress-counter.md) | 🟡中 | 3-4h | 骨架加载期间每 0.5s 更新目录节点的文件数/大小（如「加载中 1637 个文件，2.45GB」） |
| **P2** | 预览"显示内容"开关 | [preview-show-content-toggle.md](.omo/plans/未开始/preview-show-content-toggle.md) | 🟡中 | 2-3h | 压缩/解压预览树新增「显示内容」开关：关闭后只显示骨架、内容彻底隐藏不可展开，并跳过磁盘递归扫描（大型源目录预览加速）；状态持久化，摘要栏隐藏时显示输出路径 |
| **P2** | 文件选择器去弹窗化（CustomFilePickerControl 内嵌） | [embedded-file-picker.md](.omo/plans/未开始/embedded-file-picker.md) | 🟡中 | 1-2天 | 📋 2026-10-08 立项：把文件/目录选择器内嵌进压缩/解压设置窗（PickItems 简单模式默认、高级开关可选、Tab 重排、解压窗对称、拖拽兜底统一走 ExtractSettingsWindow）。三步走：先抽纯控件再分别接入；不动 CustomFilePickerDialog 静态入口与系统浏览豁免 |
| **P2** | 扩展预览格式支持 | [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) | 🟡中 | ~25h | 🟡 部分完成（3.5/6 阶段）：工具栏+信息面板、快速出货格式（Torrent/PE/PDF/SQLite/ISO 等）、SVG/字体/ICO/Office、MKV/WebM 元数据、部分提取基础设施均已就绪；LNK/DBF/EPUB/字幕/STL 与 Magick.NET/LibVLC 插件化待做（分阶段明细见计划文档） |
| **P2** | 浮动/停靠预览面板 | [floating-preview-panel.md](.omo/plans/进行中/floating-preview-panel.md) | 🟡中 | 8-10h | 🟡 部分完成（2026-10-16 核实）：四位置停靠切换已在 Avalonia 落地（右侧/下方可持久化）；浮动独立窗口（Phase 1）与拖拽分离+边缘吸附（Phase 3）未做 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.omo/plans/未开始/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚 |
| **P2** | 启动优化二期（ApplyTheme 双重加载修复 + trace 定罪） | [startup-theme-dedup-trace.md](.omo/plans/已完成/startup-theme-dedup-trace.md) | 🟢低 | 2.5-3.5h | ✅ 完成（2026-10-19）：ApplyTheme 同主题跳过 + 换肤先 Load 再替换 + 图标字典缓存（`_loadedThemeUri`/`_cachedIconProvider`），skip 路径实测 0.3ms；A/B 实测 XAML 重载真实成本仅 ~13ms，Init.Theme 段余量 76.8ms 定罪为 `AppSettings.Load()` 反复全量读盘+STJ 反序列化（singleton 化为后续立项候选）；trace 三样本共识：JIT 无顶层热点 → **R2R 门控不命中不立项**，`XamlIlPopulate`（预生成装配）为 Win.Xaml 段在途成本，`Avalonia.InitDone` 属框架铁板（splash 覆盖域） |
| **P2** | 启动优化三期：AppSettings 进程级缓存 | [startup-appsettings-cache.md](.omo/plans/已完成/startup-appsettings-cache.md) | 🟢低 | 1.5-2h | ✅ 完成（2026-10-19）：Load/Save 静态缓存 + Save 落盘后同步被保存实例 + `ResetCacheForTests`（commit `a96db21`，新测试 2 条通过、全量无新增失败）。A/B（安静环境）TOTAL −8.8ms/ThemeSpan 0（差异极小）——**启动收益有限、主要价值在运行期**：72 处调用点重开窗口/打开对话框每次 2-16ms→0、未来调用点位零成本；`Init.Theme` 段余属 ApplyTheme 内首调 Load（首调成本缓存不可消除，ThemeSpan<10ms 立项门控自撤回并记为设计错误），彻底消除需 STJ source-gen（另行评估）。**GUI 验收待做**：改设置→保存→重开窗口值同步 + 二期 5 项回归清单 |
| **P2** | 原生 Win32 启动 Splash | [startup-native-splash.md](.omo/plans/已完成/startup-native-splash.md) | 🟡中 | 3-4h | ✅ 完成（2026-10-20，commit `c5fd2df`）：`NativeSplash` Win32 静态类（独立线程+消息泵+GDI 绘制+跨平台护栏+静默降级）覆盖无参冷启动与全部 CLI 慢路径；非 UI 命令不显示；文本按 Language+启动方式选 key（新增 `Status_Starting` 三语）；8 类终端窗口 Opened 挂 Hide。冒烟实测：splash ~196ms 出现、~1.2s 熄灭（主窗 Opened 钩子 + 300ms 防闪烁）、进程稳定存活。**关键反证教训**：string 字段 struct marshal 不确定性（A/W 错配 1407；`GetClassInfoEx` ref-marshaling 释放垃圾指针致 ntdll `0xC0000374` 堆崩溃静默退进程）——**可靠方案 = 类名一律 `StringToHGlobalUni` 指针传入**。**GUI 手动验收待做**：焦点/任务栏/DPI/`--compress`/`--open`/语言切换 |
| **P2** | 面包屑地址栏（PathBreadcrumb） | [path-breadcrumb.md](.omo/plans/未开始/path-breadcrumb.md) | 🟡中 | 6-8h | 三处地址栏（主窗口/QuickPathPicker/CustomFilePickerDialog）统一改造为资源管理器式面包屑：段点击直达 + Ctrl+L 进编辑态 + 段数阈值折叠 + 虚拟根段；导航事件仍由宿主处理，第一版不做分隔符同级目录下拉 |
| **P2** | Core 层临时目录根可注入（便携模式延伸） | [core-temp-root-injectable.md](.omo/plans/未开始/core-temp-root-injectable.md) | 🟡中 | 2-3h | 📋 2026-08-20 立项：Core 层 4 处 `%TEMP%\MantisZip` 硬编码在便携模式下仍写系统 TEMP；方案为 `TempPaths` 静态类注入自定义根，未注入时路径不变（详见计划文档） |
| **P2** | Avalonia CLI 解压后打开文件夹对齐 WPF | [cli-extract-open-folder.md](.omo/plans/未开始/cli-extract-open-folder.md) | 🟡中 | 2-3h | 📋 2026-08-20 立项：WPF CLI 解压后按 `OpenFolderAfterExtract` 打开目标文件夹，Avalonia CLI 只解压不打开；方案为成功后复用 `SmartOpenPathResolver`，多文件批处理保持不开（实施前需确认 WPF `--extract` 行为） |
| **P2** | 文本预览语法高亮（Avalonia） | [text-preview-syntax-highlighting.md](.omo/plans/未开始/text-preview-syntax-highlighting.md) | 🟡中 | 4-5h | ✅ 2026-09-21 重写为 Avalonia 版（原 WPF AvalonEdit 方案废弃）：AvaloniaEdit + TextMate（VS Code 语法全集，免自研语法包）替换现有文本预览容器，亮/暗主题联动 + 扩展名→魔数→结构特征三级语言识别 |
| **P3** | 设置项无消费者修复（密码匹配提示/默认解压位置/级联菜单开关） | [settings-unwired-keys.md](.omo/plans/进行中/settings-unwired-keys.md) | 🟡中 | 3-5h | 📋 2026-09-27 立项：收尾「设置写了没人读」的最后 3 项。①②（密码匹配提示/默认解压位置）已评估、用户决策暂不动（结论见计划文档「评估结论」小节）；③`EnableCascadingMenu` 确认为陈旧描述，仅修正 AGENTS.md、零代码改动并闭环。其余 7 项已于同批修复 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.omo/plans/未开始/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 压缩包管理器（历史 / 标签 / 目录树筛选） | [archive-manager.md](.omo/plans/未开始/archive-manager.md) | 🟡中 | 2-3天 | 独立窗口记录所有处理过的压缩包：手动标签、智能分组（最近/常用）、目录树筛选、缩略图预览；SQLite 持久化 |
| **P3** | 原生图标 DLL | [icon-dll.md](.omo/plans/已归档/icon-dll.md) | 🟡中 | 2-3h | ☑️ 2026-08-20 核实：图标已嵌入 `MantisZip.ShellExt.dll` 托管资源、效果已达成；原「.rc 原生资源 DLL」方案不再需要 |
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
| **P2** | 图标主题系统①基础设施（IconView + DrawingImage 迁移） | [icon-theme-infra.md](.omo/plans/未开始/icon-theme-infra.md) | 🟡中 | 14-18h | 📋 2026-10-15 立项、2026-10-19 修订普查（拆分第 1/3）：图标契约 `Geometry`→`DrawingImage`，新增 `IconView`（`Foreground` 继承 + `SourceKey` 订阅刷新）+ 三层字典骨架 + `AppSettings.SelectedIconTheme`；全量 215 处 `PathIcon` 迁移，A 套零视觉差异。规格：[2026-10-15-icon-theme-system-design.md](docs/superpowers/specs/2026-10-15-icon-theme-system-design.md) |
| **P2** | 图标主题系统②导入包 + 单图标覆盖 + 设置 UI | [icon-theme-packs.md](.omo/plans/未开始/icon-theme-packs.md) | 🟡中 | 16-22h | 📋 2026-10-15 立项（拆分第 2/3）：SVG 图标包（文件夹/ZIP）导入 + 覆盖率校验（结构错误原子拒绝、渐变降级跳过 D-C，第②层）；单图标覆盖（第③层）；设置「外观」Tab 图标风格分区 + `IconManagerWindow`。依赖① |
| **P3** | 图标主题系统③C/D/E/F/G 内容生产 + IconGen 多套 | [icon-theme-sets.md](.omo/plans/未开始/icon-theme-sets.md) | 🔴高 | 20-30h | 📋 2026-10-15 立项（拆分第 3/3）：IconGen 多套产出；Phosphor Bold/Duotone + 彩色/手绘；6 字典 × 116 键覆盖率契约；G 强调色 DynamicResource 决策门；About 页署名。依赖① |
| **P2** | 文本/ZIP 注释回退编码策略（非中文 locale GBK 乱码 + copy-mode 注释损坏） | [text-fallback-encoding-policy.md](.omo/plans/进行中/text-fallback-encoding-policy.md) | 🟡中 | 4-6h | 🟡 **Phase 1（A1+A3）已实施 2026-10-20**，Phase 2（可配置回退代码页）待定。原立项背景：CI（en-US runner）暴露 `ZipCommentHelper` 回退代码页取 `CultureInfo.CurrentCulture.TextInfo.ANSICodePage`（en-US=1252）→ GBK 注释乱码 `»¶Ó­Ê¹ÓÃ±¾Ñ¹Ëõ°ü`；同时发现 `ZipBinaryRewriter.ReadEocd` 用 `Encoding.UTF8.GetString`（**不抛异常**，`catch` 为死代码）+ `Encoding.Default`（.NET Core 恒 UTF-8）→ copy-mode 重写把 GBK 注释写成 U+FFFD，属**数据损坏**。已实施：①注释新增 `DecodeComment`（严格 UTF-8 → **严格 GB18030** → 宿主 ANSI，GB18030 必须用异常回退——其默认解码器把截断序列静默换成 `?` 而非 U+FFFD）；②`ReadEocd`/`WriteEocd` 改**字节透传**；③测试侧 `zh-CN` pin 反转为**故意 en-US**。验证 Core 717/1skip/0fail、Avalonia 306/3skip/0fail。否决全局候选打分（Western 文本 `café` 类字节被 GBK 解成 CJK 且无 U+FFFD，无法可靠区分） |
| **P4** | 外部工具视频元数据 | — | 🟢低 | 2-3h | ffprobe 集成 |
| **P2** | 跨平台移植（macOS / Linux） | [cross-platform-port.md](.omo/plans/未开始/cross-platform-port.md) | 🟡中大 | 6-8周 | ✅ WPF→Avalonia 迁移已完成（Phases 0-10）。剩余 macOS/Linux 移植：Phase 0 CI+路径适配 → Phase 1 引擎适配 → Phase 2 UI 适配 → Phase 3 打磨发布；7z 压缩策略、拖拽降级、右键菜单取舍等决策点见计划文档 |

> **执行顺序**（3 项计划有依赖链）：`nuget-dependency-upgrade`（SharpCompress ≥0.49.0）→ `compression-estimator`（依赖升级后的 per-entry 级别/多实例并行；`compression-performance-optimization` 已于 v0.5.0 完成、`progress-window-enhancement` 已完成）→ `progress-bar-segments`（在进度窗口重构后的布局上替换 SegmentProgressBar）。时间估算已按简单实时速度方案落地（`progress-window-enhancement`，已完成），compression-estimator 完成后再升级为加权融合方案，零返工。额外受影响计划：`cross-platform-port`（依赖 SharpCompress 升级后跨平台验证）、`selfcontained-size-optimization`（TrimmerRootAssembly 需随升级更新）、`context-menu-tree-preview`（SharpCompress API 升级后需验证条目列出兼容性）。`archive-rename-entry`/`rar-compression`/`startup-native-splash` 为软依赖，ProgressWindow 增强会自动改善体验。


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


