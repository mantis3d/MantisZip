# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-10-24  
**当前版本**: 0.5.3

---


## 待实现设计方案

以下功能已有独立方案设计文档（`.omo/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P1** | 自动更新检测 | [auto-update.md](.omo/plans/未开始/auto-update.md) | 🟡中 | 4-6h | GitHub Releases API 版本检查、AboutWindow 更新 Tab、UpdateAvailableDialog、设置开关、单元测试 |
| **P1** | 面包屑地址栏（PathBreadcrumb） | [path-breadcrumb.md](.omo/plans/未开始/path-breadcrumb.md) | 🟡中 | 6-8h | 三处地址栏（主窗口/QuickPathPicker/CustomFilePickerDialog）统一改造为资源管理器式面包屑：段点击直达 + Ctrl+L 进编辑态 + 段数阈值折叠 + 虚拟根段；导航事件仍由宿主处理，第一版不做分隔符同级目录下拉（📋 2026-10-21 修订：按代码核对修正行号/行为描述——`TryConfirmFile` 无模式判断须保持现状；`Path` 定为 StyledProperty+OneWay；补磁盘空路径态与 `Breadcrumb_EditPlaceholder` 三语；QuickPathPicker 4 宿主纳入回归） |
| **P1** | Office 图片预览（已完成计划内的遗留子项） | [office-content-preview-avalonia.md](.omo/plans/已完成/office-content-preview-avalonia.md#office-图片预览) | 🟢低-🟡中 | 2-10h | 📋 所属计划 `office-content-preview-avalonia` 已于 v0.5.0 完成（DOCX/XLSX/PPTX 内容预览），本行只跟踪其文末「Office 图片预览」未实现子项，尚未开工（全仓无图片提取代码）。三方案渐进：A=PPTX 图片（~50行，复用 Canvas）、B=+DOCX 行内图片（~150行）、C=浮动定位（不推荐）。待决定实施方案 |
| **P1** | 快速预览与渐进式加载 | [preview-quick-modes.md](.omo/plans/未开始/preview-quick-modes.md) | 🟡中 | ~25h | 三种预览模式（快速/渐进/完整），叠加在已实施的两阶段加载（Phase 2）之上。**Avalonia-only（规则 11）**；已按 2026-10-20 修正版校准（设置项复用现有字段、DBF/字幕等 Unsupported 格式降级前置、GIF→AnimatedImage、ICO→IcoGallery、HTML WebView 双轨已落地、文件列表缩略图已独立成 `file-list-thumbnails.md`） |
| **P1** | 新增压缩格式（BZip2/XZ/Zstd/Brotli + 7z.dll 只读解锁） | [new-format-support.md](.omo/plans/进行中/new-format-support.md) | 🟡中 | 11-16h | 🟡 部分完成（2026-08-20 核实）：Core 侧 TAR 裸格式 / GZip 单文件压缩与文件关联已就绪；UI 压缩格式下拉尚未放开。后续按序增加 BZip2 → XZ → Zstd → Brotli，并解锁 7z.dll 只读格式（详见计划文档） |
| **P1** | 自包含体积优化（Avalonia 迁移后） | [selfcontained-size-optimization.md](.omo/plans/未开始/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 三步渐进：InvariantGlobalization → 保守修剪 → 激进修剪，目标降至 20–25 MB |
| **P1** | ZIP 多线程压缩方案 D（自适应 + 多线程第四个选项） | [parallel-compress-plan-d.md](.omo/plans/未开始/parallel-compress-plan-d.md) | 🟡中 | 4-5h | 自适应压缩新增"自适应 + 多线程"选项：Store 类直接写入 + 需压缩类 SharpSevenZip mt=on；UI 联动（格式目录/用户规则/多线程提示可见性）；实验性标签 + 帮助弹窗 |
| **P1** | 压缩加速三开关 UI 收敛（Phase 1 ✅） / 魔数检测+自适应规则（Phase 2 主体 ✅） | [adaptive-compress-ui-convergence.md](.omo/plans/进行中/adaptive-compress-ui-convergence.md) | 🟢 P1 完成 | P1 4-5h / P2 5-7h | 🟢 2026-10-21 Phase 2 Task 9-11 实施完成：SSOT 统一到 `ZipEntryClassifier`；`AdaptiveRuleMatcher.ResolveLevel` 唯一决策入口（规则 > SSOT 扩展名 > 魔数）；**「仅存储格式」已并入自适应规则**（控件组删除、旧配置迁移为 Store 规则、默认规则集扩到 6 类）；多线程路径提示 + 行内「多线程下不生效」标记。**2026-10-23 复核补漏**：自定义 `MagicHex` 接线、ZIP 面板魔数联动、MT 提示实时刷新、CLI 自适应开关、帮助文案「三个开关」、死字段/单测/过期注释清理。**未做**：Task 12 本机基准测试 UI、Task 13 组数定标 |
| **P1** | 意图-执行接缝收口（消灭「点了没反应」类 bug） | [intent-execution-seam.md](.omo/plans/未开始/intent-execution-seam.md) | 🔴高 | 18-24h | 📋 2026-10-24 立项、已定案待实施：同族事故已 **7 次**（压缩侧逐属性手抄漏 3 次 / 拖入分支无人接续 / CLI 静默 `Shutdown` / 解压 CLI `FilteredEntryKeys` 恒 null / 密码库保存在且仅主窗口入口生效）。方案＝设置窗口**注入唯一执行器**（漏接续变编译错误）+ 确认事务（`Rejected(reason)` 替代静默 return）+ 不可变 DTO（消除属性补读）+ `IProgressPresenter` 收口两套进度实现。按波落地：压缩 4 入口 → 解压 5 入口 → 全仓 68 处 `ShowDialog` 清账；同时新增 AGENTS.md 规则 19 与 `guards` 第 5 条 `dialog-seam` 守卫。**`CopyExecutionSettingsFrom` 及其两份反射守卫测试退休** |
| **P2** | ZIP 多线程压缩方案 A（分组并行 + 多级别） | [parallel-compress-plan-a.md](.omo/plans/未开始/parallel-compress-plan-a.md) | 🟡中 | 6-8h | 按级别分组 → 各组独立 ZipWriter 并行 → 合并 ZIP，完整多级别自适应 + 多线程。方案 D 完成后实施 |
| **P2** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.omo/plans/未开始/compression-estimator.md) | 🟡中 | 14-18h | 🔶 部分实现（比率表 / 格式目录 / 自适应规则模型已完成，估算器本体未开始）。2026-10-21 拆分：本行 = 压缩前预估（快速查表 / 采样标准 / 可选精确 + 设置窗口面板）；ETA 增强见下一行；自适应已归档 |
| **P2** | 压缩中 ETA 增强 | [runtime-eta.md](.omo/plans/未开始/runtime-eta.md) | 🟡中 | 5-7h | 🔶 基础 ETA 已实现（EMA 速度 + 单档案剩余 + 暂停冻结）；本计划补先验速度融合（冷启动即有值）、批处理总剩余、估算中/即将完成文案 |
| **P2** | Winget 发布 | [winget-publishing.md](.omo/plans/未开始/winget-publishing.md) | 🟢低 | 1-2h | 发布到 Windows Package Manager 社区仓库；首次手动提交后 CI 自动化 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.omo/plans/未开始/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup → WiX MSI 迁移 |
| **P2** | 图标主题系统①基础设施（IconView + DrawingImage 迁移） | [icon-theme-infra.md](.omo/plans/未开始/icon-theme-infra.md) | 🟡中 | 22-28h | 📋 2026-10-15 立项；2026-10-19/10-21 两次修订；**2026-10-24 第三次修订**（2026-10-21~23 四次启动优化重构后重普查，**旧数字 215/206/17 文件/116 键全部作废**）：图标契约 `Geometry`→`DrawingImage`，新增 `IconView`（`SourceKey` 订阅刷新 + `MeasureOverride` 尺寸兜底 + 等比缩放；前景色经**外置** `IconViewStyles.axaml` 的 ControlTheme 对齐 Fluent `PathIcon` 默认值）+ 三层字典骨架（`IconThemeAssembler` 纯函数装配 + `ShouldSkip` 双哨兵、按主题 id 缓存 provider）+ `AppSettings.SelectedIconTheme`；消费面 **224 处 / 25 个 axaml** + **8 处 converter** + C# 动态站点（`ArchiveContentView.Events.cs:53/91`，旧误记 `MainWindow.axaml.cs`）迁移，A 套零视觉差异。**两项硬伤已写进计划**：①「首帧哨兵」`_loadedThemeUri` 初值 Light 会让默认「浅色 + A 套」首次 `ApplyTheme` 早退 → 首帧图标全空；②测试宿主是裸 `Application`（无 App.axaml/ControlTheme），故 ControlTheme 外置 + 装配抽纯函数。新增 T0 重普查、T9b `verify-repo.ps1` 第五条图标引用守卫（图标键名拼错**唯一**门禁）、AGENTS 规则 4/8 同步。含启动性能约束（§4.7：启动期解析次数 = 1，A/B 门控 TOTAL ≤20ms）。规格：[2026-10-15-icon-theme-system-design.md](docs/superpowers/specs/2026-10-15-icon-theme-system-design.md) |
| **P2** | 图标主题系统②导入包 + 单图标覆盖 + 设置 UI | [icon-theme-packs.md](.omo/plans/未开始/icon-theme-packs.md) | 🟡中 | 26-34h | 📋 2026-10-15 立项（拆分第 2/3）、2026-10-21 复核、**2026-10-24 第三次修订**：SVG 图标包（文件夹/ZIP）导入 + 覆盖率校验（结构错误原子拒绝、渐变降级跳过 D-C，第②层）；单图标覆盖（第③层）；设置「外观」Tab 图标风格分区 + `IconManagerWindow`。2026-10-24 订正：①键数口径 **138 条 `Add` / 137 带键 / 1 null / 117 去重**（13 组 20 条冗余行）；②**新增偏差 D-E：必须支持 `stroke`→`GeometryDrawing.Pen`**（Lucide/Feather/Tabler 全是 `fill="none"+stroke`，否则导入成功但渲染错乱），连带要求①的 `IconView.Render` 透传 Pen 并处理 `DrawingGroup.Opacity/Transform/ClipGeometry`；③Zip Slip 改为「`SanitizeEntryPath` 净化 + `GetSafePath` 复核，逃逸即整包拒绝」；④**UI 不新增 SharpCompress**（解 ZIP 用 in-box `ZipFile`，`PreviewViewModel.cs:2789` 先例）；⑤`IconCatalog` 提前到 T2 并**同 commit 改 `GUARD_FILES.iconTestVm`**（否则规则 8 守卫 117 条全红）；⑥启动预算实测（`Geometry.Parse`×117 >60ms 则只装当前主题）；⑦内置 id `A`–`G` 保留字拒绝、删当前选中包即时回退 A、`GetIconThemeProvider` 双来源 + provider/`_loadedIconThemeId` 缓存失效、非选中包缩略预览走 `IconThemeBuilder`、落盘形态偏差 D-D。依赖① |
| **P3** | 图标主题系统③C/D/E/F/G 内容生产 + IconGen 多套 | [icon-theme-sets.md](.omo/plans/未开始/icon-theme-sets.md) | 🔴高 | 24-34h | 📋 2026-10-15 立项（拆分第 3/3）、2026-10-21 复核、**2026-10-24 第三次修订**：IconGen 多套产出（离线、sources vendored）；Phosphor Bold/Duotone + E 套彩色源决策门（兜底 C 几何程序化着色）；6 字典 × **117** 键覆盖率契约（`Distinct`）；**G 强调色改运行时烘焙**（`GeometryDrawing` 非 `IResourceHost`，写 `{DynamicResource}` 不解析；生成物写哨兵 + `IconThemeBuilder` 按「图标集 id + 主题变体」烘焙缓存）；D 底块中性半透明灰 `#40808080`（暗色可见）、部件数放宽为 **≥1 个 null 线框**；`mapping.json` 改 `{"icons":{…}}` 包裹（`_comment` 不得被当图标键）；署名三处（AboutWindow「致谢」Tab —— **内容是绑定属性，须同时改 `AboutWindowViewModel` + 三语 key** / THIRD-PARTY-NOTICES / README），Lucide 为 **ISC + Feather MIT** 双声明；归一化下沉 Core `SvgGeometryNormalizer`（T1 验收：同一 `viewBox` 样本输出逐字符相等）。依赖①；T5 时序须排在②的 `IconCatalog` 之后 |
| **P2** | RAR 压缩（外置 rar.exe） | [rar-compression.md](.omo/plans/未开始/rar-compression.md) | 🟡中 | 8-10h | 通过已安装的 WinRAR 实现 RAR 压缩（含 SevenZipEngine 注册冲突处理） |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.omo/plans/未开始/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到… |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.omo/plans/未开始/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| **P2** | 文件过滤与文件列表筛选数据统一 | [unify-file-filter-data.md](.omo/plans/未开始/unify-file-filter-data.md) | 🟡中 | 8-12h | 📋 2026-10-20 核对：计划文档为 WPF 期产物（Task 4/5 直指 WPF `MainWindow.xaml`），实施前须按规则 11 转 Avalonia。目标：`FileFilterCriteria` 扩展 `TextSearch`/`ExcludeText`/`TextMatchMode`，删除 `SearchFilters`+`ArchiveFilter.ApplyFilters()`（现 `MainWindowViewModel.Navigation.cs:309` 仍在用旧模型），筛选栏加预设下拉 / 扩展名 Tag 选择器 /「以当前过滤解压」 |
| **P2** | 插件生态（.mp 市场 + 宿主安装器/运行时） | [plugin-ecosystem.md](.omo/plans/未开始/plugin-ecosystem.md) | 🔴高 | 44-64h | 📋 待拍板（2026-10-22）：Publisher 产包/校验已独立实现（47+8 测试通过），宿主侧安装/加载/市场为 0；待定：`MantisZip.PluginKit` 是否并入主仓、插件跑子进程还是宿主内、市场索引静态 JSON 还是 VPS |
| **P2** | Win11 一级右键菜单 | [win11-first-level-menu.md](.omo/plans/未开始/win11-first-level-menu.md) | 🔴高 | 1-2周 | IExplorerCommand 实现，HKLM 提权注册，双接口共存 |
| **P2** | 进度条分段着色（按压缩包状态） | [progress-bar-segments.md](.omo/plans/未开始/progress-bar-segments.md) | 🟡中 | 3-4h | 自定义分段进度条控件替换普通 `ProgressBar`，按批处理项状态着色（红=失败/绿=成功/青=跳过/蓝=进行中），动态预算缝隙 |
| **P2** | 压缩文件名后缀模板 | [filename-suffix-template.md](.omo/plans/未开始/filename-suffix-template.md) | 🟢低 | 2-3h | `{date}`/`{datetime}`/`{seq}` 占位符替换，防同名覆盖 |
| **P2** | 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.omo/plans/未开始/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API(通用) 两层提取嵌入缩略图；完成后可扩展文件列表缩略图模式 |
| **P2** | 文件列表缩略图查看方式 | [file-list-thumbnails.md](.omo/plans/未开始/file-list-thumbnails.md) | 🟡中 | 4-6h | 三种布局模式循环切换（详情/平铺/内容行），内容卡三档（图像缩略图/文字头部预览/系统图标），布局模式持久化 |
| **P2** | 骨架态实时进度计数器 | [skeleton-progress-counter.md](.omo/plans/未开始/skeleton-progress-counter.md) | 🟡中 | 3-4h | 骨架加载期间每 0.5s 更新目录节点的文件数/大小（如「加载中 1637 个文件，2.45GB」） |
| **P2** | 预览"显示内容"开关 | [preview-show-content-toggle.md](.omo/plans/未开始/preview-show-content-toggle.md) | 🟡中 | 2-3h | 压缩/解压预览树新增「显示内容」开关：关闭后只显示骨架、内容彻底隐藏不可展开，并跳过磁盘递归扫描（大型源目录预览加速）；状态持久化，摘要栏隐藏时显示输出路径 |
| **P2** | 文件选择器去弹窗化（CustomFilePickerControl 内嵌） | [embedded-file-picker.md](.omo/plans/未开始/embedded-file-picker.md) | 🟡中 | 1-2天 | 📋 2026-10-08 立项：把文件/目录选择器内嵌进压缩/解压设置窗（PickItems 简单模式默认、高级开关可选、Tab 重排、解压窗对称、拖拽兜底统一走 ExtractSettingsWindow）。三步走：先抽纯控件再分别接入；不动 CustomFilePickerDialog 静态入口与系统浏览豁免 |
| **P2** | 扩展预览格式支持 | [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) | 🟡中 | ~25h | 🟡 部分完成（3.5/6 阶段）：工具栏+信息面板、快速出货格式（Torrent/PE/PDF/SQLite/ISO 等）、SVG/字体/ICO/Office、MKV/WebM 元数据、部分提取基础设施均已就绪；LNK/DBF/EPUB/字幕/STL 与 Magick.NET/LibVLC 插件化待做（分阶段明细见计划文档） |
| **P2** | 浮动/停靠预览面板 | [floating-preview-panel.md](.omo/plans/进行中/floating-preview-panel.md) | 🟡中 | 8-10h | 🟡 部分完成（2026-10-16 核实）：四位置停靠切换已在 Avalonia 落地（右侧/下方可持久化）；浮动独立窗口（Phase 1）与拖拽分离+边缘吸附（Phase 3）未做 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.omo/plans/未开始/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚 |
| **P2** | Core 层临时目录根可注入（便携模式延伸） | [core-temp-root-injectable.md](.omo/plans/未开始/core-temp-root-injectable.md) | 🟡中 | 2-3h | 📋 2026-08-20 立项：Core 层 4 处 `%TEMP%\MantisZip` 硬编码在便携模式下仍写系统 TEMP；方案为 `TempPaths` 静态类注入自定义根，未注入时路径不变（详见计划文档） |
| **P2** | Avalonia CLI 解压后打开文件夹对齐 WPF | [cli-extract-open-folder.md](.omo/plans/未开始/cli-extract-open-folder.md) | 🟡中 | 2-3h | 📋 2026-08-20 立项：WPF CLI 解压后按 `OpenFolderAfterExtract` 打开目标文件夹，Avalonia CLI 只解压不打开；方案为成功后复用 `SmartOpenPathResolver`，多文件批处理保持不开（实施前需确认 WPF `--extract` 行为） |
| **P2** | 文本预览语法高亮（Avalonia） | [text-preview-syntax-highlighting.md](.omo/plans/未开始/text-preview-syntax-highlighting.md) | 🟡中 | 4-5h | 📋 2026-09-21 计划文档已重写为 Avalonia 版（原 WPF AvalonEdit 方案废弃），**代码尚未实施**（csproj 无 AvaloniaEdit/TextMate 引用）：AvaloniaEdit + TextMate（VS Code 语法全集，免自研语法包）替换现有文本预览容器，亮/暗主题联动 + 扩展名→魔数→结构特征三级语言识别 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.omo/plans/未开始/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 压缩包管理器（历史 / 标签 / 目录树筛选） | [archive-manager.md](.omo/plans/未开始/archive-manager.md) | 🟡中 | 2-3天 | 独立窗口记录所有处理过的压缩包：手动标签、智能分组（最近/常用）、目录树筛选、缩略图预览；SQLite 持久化 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.omo/plans/进行中/preview-modular-providers.md) | 🟡中 | 3-4h | 🟡 部分完成（2026-10-16 核实，Task 1/4）：`ITableDataProvider` 已纳入 Core 抽象层（`SqliteDataReader` 实现，提交 1cef18f），但 UI SQLite 预览仍直连 `Microsoft.Data.Sqlite`、接口零消费者；模块拆分/反射加载/安装包组件未做 |
| **P3** | 文件列表自定义列 | [custom-columns.md](.omo/plans/未开始/custom-columns.md) | 🟡中 | 4-6h | 可自定义显示文件元数据列（文档标题、图片尺寸等） |
| **P3** | 冻结列（水平滚动时列固定） | [frozen-column.md](.omo/plans/未开始/frozen-column.md) | 🟢低 | 1-2h | 右键列标题冻结/取消冻结，分隔线，设置持久化 |
| **P3** | ICO 文件自身图标显示 | [ico-file-icon-extract.md](.omo/plans/未开始/ico-file-icon-extract.md) | 🟢低 | 2-3h | ico 文件列表显示自身嵌入图标 |
| **P3** | 右键菜单目录结构预览 | [context-menu-tree-preview.md](.omo/plans/未开始/context-menu-tree-preview.md) | 🔴高 | 6-8h | COM 菜单中展示压缩包顶层文件树 |
| **P4** | 解压多压缩包按来源目录分组（已完成计划内的遗留子项） | [result-preview-panel.md](.omo/plans/已完成/result-preview-panel.md) | 🟡中 | 2-4h | 所属计划 `result-preview-panel` 已完成（v0.4.5），本行只跟踪其文末「未实现项（后续可做）①」：当前多压缩包条目合并平铺，改为按来源目录分组 + 压缩包壳节点 |
| **P4** | 结果预览截断占位符点击展开（已完成计划内的遗留子项） | [result-preview-panel.md](.omo/plans/已完成/result-preview-panel.md) | 🟢低 | 1-2h | 所属计划 `result-preview-panel` 已完成（v0.4.5），本行只跟踪其文末「未实现项（后续可做）②」：当前截断为静态"…"文本，改为点击就地展开完整子节点 |
| **P4** | 自研 ZIP 引擎（Per-entry 压缩 + 多线程） | [custom-zip-engine.md](.omo/plans/未开始/custom-zip-engine.md) | 🟡中 | 5-6天 | Per-entry 压缩级别/方法控制（产品卖点）+ 多线程并行压缩；纯 .NET 实现（DeflateStream），零外部依赖；支持 AES-256 加密；后续可开源为独立库 ZipPerEntry |
| **P4** | 文本/ZIP 注释回退编码策略（非中文 locale GBK 乱码 + copy-mode 注释损坏） | [text-fallback-encoding-policy.md](.omo/plans/进行中/text-fallback-encoding-policy.md) | 🟡中 | 4-6h | 🟡 **Phase 1（A1+A3）已实施 2026-10-20**，Phase 2（可配置回退代码页）待定。原立项背景：CI（en-US runner）暴露 `ZipCommentHelper` 回退代码页取 `CultureInfo.CurrentCulture.TextInfo.ANSICodePage`（en-US=1252）→ GBK 注释乱码 `»¶Ó­Ê¹ÓÃ±¾Ñ¹Ëõ°ü`；同时发现 `ZipBinaryRewriter.ReadEocd` 用 `Encoding.UTF8.GetString`（**不抛异常**，`catch` 为死代码）+ `Encoding.Default`（.NET Core 恒 UTF-8）→ copy-mode 重写把 GBK 注释写成 U+FFFD，属**数据损坏**。已实施：①注释新增 `DecodeComment`（严格 UTF-8 → **严格 GB18030** → 宿主 ANSI，GB18030 必须用异常回退——其默认解码器把截断序列静默换成 `?` 而非 U+FFFD）；②`ReadEocd`/`WriteEocd` 改**字节透传**；③测试侧 `zh-CN` pin 反转为**故意 en-US**。验证 Core 717/1skip/0fail、Avalonia 306/3skip/0fail。否决全局候选打分（Western 文本 `café` 类字节被 GBK 解成 CJK 且无 U+FFFD，无法可靠区分） |
| **P4** | 外部工具视频元数据 | — | 🟢低 | 2-3h | ffprobe 集成 |
| **P4** | 跨平台移植（macOS / Linux） | [cross-platform-port.md](.omo/plans/未开始/cross-platform-port.md) | 🟡中大 | 6-8周 | ✅ WPF→Avalonia 迁移已完成（Phases 0-10）。剩余 macOS/Linux 移植：Phase 0 CI+路径适配 → Phase 1 引擎适配 → Phase 2 UI 适配 → Phase 3 打磨发布；7z 压缩策略、拖拽降级、右键菜单取舍等决策点见计划文档 |

> **执行顺序**（3 项计划有依赖链）：`nuget-dependency-upgrade`（SharpCompress ≥0.49.0）→ `compression-estimator`（依赖升级后的 per-entry 级别/多实例并行；`compression-performance-optimization` 已于 v0.5.0 完成、`progress-window-enhancement` 已完成）→ `progress-bar-segments`（在进度窗口重构后的布局上替换 SegmentProgressBar）。压缩中 ETA 增强已独立为 `runtime-eta`（先验速度来自 `compression-estimator` 的快速预估，无先验时行为与现状一致，零返工）。额外受影响计划：`cross-platform-port`（依赖 SharpCompress 升级后跨平台验证）、`selfcontained-size-optimization`（TrimmerRootAssembly 需随升级更新）、`context-menu-tree-preview`（SharpCompress API 升级后需验证条目列出兼容性）。`archive-rename-entry`/`rar-compression`/`startup-native-splash` 为软依赖，ProgressWindow 增强会自动改善体验。


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


