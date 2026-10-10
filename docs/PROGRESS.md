# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (基于 Avalonia)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 10 + Avalonia 12 + SharpCompress + SharpSevenZip

## 版本
- **当前版本**: 0.5.2
- **发布日期**: 2026-07-22

## 变更记录结构

> 本文档仅保留**里程碑级**变更（新功能上线、架构级变更、重大 bug 修复）。逐条详细变更（含小 bugfix / i18n / 样式 / 计划类）归档至独立文档：
>
> - **[progress-avalonia-detail.md](progress-avalonia-detail.md)** — Avalonia 版 + 共享层逐条详情（按日期/版本从新到旧）
> - **[progress-wpf.md](progress-wpf.md)** — WPF 遗留版完整历史（按版本从新到旧）
>
> 新增条目规则（AGENTS.md 规则 3）：里程碑级变更在本文档对应月份/版本下追加一行；其余变更追加到 `progress-avalonia-detail.md`。

---

### MantisZip.UI.Avalonia（主力版）— 里程碑

按月分组，每月按日期从新到旧排列。

#### 2026-10

- **10-10** — **进度窗口通道行左右分组 + 实时压缩率信息列 + 详细常显（channel-info 计划 Task 1–7，Avalonia+Core）**：通道行重构为**左右分组**——左=单文件域（目录中间省略/文件名恒完整/文件底纹/文件大小），右=批次域（批次序号/批次底纹/合并百分比 `45% (12/40)`/字节进度·压缩率/ToolTip）；**批级进度底纹化**（新增 `ThemeProgressBatchBrush` 紫系亮/暗成对，替代独立 ProgressBar）；不加列标题。详细模式**推翻门禁常显** + 非并行合成单条 `IsParallel=false` 通道行（隐藏序号/批明细/压缩说明）；路径两列（`ProgressDisplayCalculator.MiddleEllipsis` 中间省略）；底纹双缺陷修复（几何基准 ContentPresenter→最近 Grid + ClipToBounds；N 组 adapter 漏拷 `FilePercentComplete`）。`ArchiveProgress` +`FileTotalBytes`/`BatchProcessedBytes`/`BatchTotalBytes`/`CompressionRatio`；行模型 +`IsParallel`/`DirectoryText`/`FileNameText`/`FileSizeText`/`PctDetailText`/`InfoText`/`BatchRatio`/`TooltipText`；VM +`HasParallelChannel`/`ShowCompressChannelHint`/`NotifyChannelProperties`；三语 +3 ToolTip key
- **10-10** — **逐文件压缩后大小 + 压缩率（列表列 + 统计卡段 2 整体汇总，compression-ratio 计划 Task 1–5，Avalonia+Core）**：ZIP 压缩**完成后后置回填**——`CompressAsync` 末尾读成品 ZIP 中央目录（`entry.CompressedSize`；加密包中央目录未加密无需密码）逐条目上报 `EntryCompressedBytes` + 整体 `TotalCompressedBytes`（分卷跳过、异常吞掉）。列表模式每行新增列「压缩后大小 (率%)」（`EntryProgressItem.CompressedText`，如 `600 KB (50%)`，空=隐藏）；「完整」档统计卡**段 2** 并行度旁新增整体压缩汇总 `🗜 300 MB (60%)`（格式 A）+ 条件分隔符 `·`（`ShowChannelMetaSeparator`/`ShowChannelMetaRow`）；三语 +`Progress_Stats_Compressed`。仅 ZIP 压缩显示（解压/7z 固实/TAR/GZ/分卷按 Rule 6 隐藏）
- **10-10** — **进度窗口两处缺陷修复（Avalonia+Core）**：① **压缩率列恒空**——压缩侧播种/实时上报 `EntryKey` 用反斜杠（`FileScanner` 的 `Path.Combine`/`GetRelativePath`）而后置回填/解压侧用正斜杠，不匹配致回填新建 `Size=0` 重复行；修复：VM 索引边界统一 `ArchivePath.Normalize`（`SeedEntryItems`+`UpdateEntryStatus`）；② **MT 压缩镜像阶段进度冻结**——MT 两阶段（镜像拷贝→7z）中镜像刻意不推进 `PercentComplete`（单组恒 0）而 7z 阶段 `FilePercentComplete=null`，呈「文件动→批次动」交替；修复：`CompressGroupWithSevenZip` 把该组进度区间前半段给镜像（按已拷贝字节映射）、后半段给 7z，N 组 adapter 改用组 `PercentComplete` 推导（原用字节重算丢弃镜像进度）。回归测试 `CompressAsync_MultiThreaded_MirrorPhase_AdvancesOverallProgress`
- **10-09** — **进度窗口统计卡三行结构实施（方案 A 去分母 + 终态字节累加器 + 并行度后置，Avalonia）**：「完整」档 6 张统计卡重构为 5 张三行卡（行 1 图标+标题横排 / 行 2 文件数量 / 行 3 文件大小）+ 并行度后置单行元素；已处理卡行 2 由 `N/M` 分数改纯计数（方案 A，分母与总大小卡行 2 同源重复、比例由总进度条承担）；VM 新增 5 个 `[ObservableProperty]` 字段（`StatsProcessedSize`/`StatsTotalCount`/`StatsSkippedSize`/`StatsFailedSize`/`StatsOverwrittenSize`，默认 `—` 灰显）+ 3 个终态字节累加器（跳过/出错/已覆盖，`UpdateEntryStatus` 加 `long? fileSize = null` + 去重守卫）；新建 `DashBrushConverter`（`—`→`ThemeTextSecondaryBrush`、真实值→对应主题色，按 `ConverterParameter` 分发）；5 卡加 `MinWidth="80"` 防文字长度变化导致相邻卡跳动；中/精简两档零改动（D5）。测试：`ProgressViewModelTests` +7 新测试 + 1 改名（`ShowsFractionString`→`ShowsPureCount` 断言 `"60"`），Avalonia 170 通过（5 失败为预存 WebView 环境问题）/ Core 615 通过、构建 0/0
- **10-08** — **手工测试三问题修复（Avalonia）**：① 进度条 0% 不可见——双主题 `ThemeProgressBgBrush` 与面板背景 `ThemeSplitterBg` 对比度不足（差 0/4）→ 拉开至单通道差 ≥16（Light `#E0E0E0→#C8C8C8`、Dark `#424242→#555555`），新增 `ProgressTrackContrastTests` 色值守卫；② 进度窗口缺文件总数/总大小——`ProgressViewModel` 新增 `_statsTotalFiles` + `StatsTotalSizeText/Value`（`TotalBytes > 0` 才写、Rule 6 隐藏），已处理卡值改 N/M 分数（"60/100"），「完整」档新增 📏 总大小第 6 卡 + 中等档补总大小行 + 三语 `Progress_Stats_TotalSize`；③ 解压线程数选择无效——三处断链接通（`NumericUpDown` 补 `Mode=TwoWay` / 确认回调回传 `ParallelExtractDegree` / `ExtractFlow.ExtractAsync` 增显式参数，对话框值优先、AppSettings 兜底），进度窗口「并行」卡显示值同步对齐。测试：`ProgressViewModelTests` +6、新增 `ExtractParallelDegreeWiringTests`（5 源码守卫 + 2 行为）与 `ProgressTrackContrastTests`；Avalonia 163 通过（5 失败为预存 WebView 环境问题）/ Core 615 通过、构建 0/0
- **10-06** — **N 组并行压缩（计划 Task 1–8）联调完成 + 三处联调缺陷修复 + 组数 UI 补齐 + 压缩侧列表播种（Avalonia+Core）**：修复① 压缩对话框「多线程压缩/魔数检测/并行压缩组数」三开关**从未传到执行侧**（`ShowCompressSettingsDialog` 拷贝清单漏拷 → 引擎恒走串行、进度窗口「详细」通道永不出现）；② 详细面板引用 `RatioToWidthConverter` 但该资源只在 `MainWindow` 局部定义 → 首次渲染通道行即抛异常 **UI 卡死**（补本窗口作用域资源并统一 `StringNotEmpty` key）；③ 模式单选项未与 `ContentMode` 绑定 → 自动切「详细」时按钮仍停「简约」（改单向绑定）。补齐计划 Task 5 Step 3 缺失的「并行压缩组数」控件（多线程开关旁，MT 关闭时整块隐藏）。体验：窗口打开即「正在准备…」+ 不定进度条；镜像拷贝由 `File.Copy` 改**分块 + 100ms 节流上报**（保留源 mtime），简约条无逐文件百分比时**回退总进度**。**压缩侧「列表」播种**（原经用户决定正式延期项）：新增公开 `SourceEntryEnumerator`（复用 `FileScanner`，Key 与引擎 `EntryKey` 同源）+ `CompressFlow.TrySeedEntryItemsInBackground` 接线主窗口/CLI → 列表显示**全部条目**（Pending）并随终态更新。三语 +`Progress_Preparing`；`.gitignore` 排除 `.buildout/`。**G2 实测：N 组无加速（维持默认 `ProcessorCount`）**。UI 构建 0 警告 0 错误；Core 608 + Avalonia 114 测试全绿
- **10-06** — **修复「测试压缩包」静默失败 + 进度条不动（Avalonia + Core）**：① **加密包测试静默失败**——`TestArchive` 只从会话缓存读密码，无会话密码时 `TestArchiveAsync` 快速失败、状态栏仅「测试失败」无弹窗；现抽取公共方法 `TryEnsureArchivePasswordAsync`（会话缓存 → 密码库自动匹配 → 密码对话框快速验证循环，与 `LoadArchiveAsync` 打开流程同构），`TestArchive` 测试前先确保密码，取消时提示「已取消 - 需要密码」中止、不再假失败；密码已验证正确但测试仍失败 → 弹「文件损坏」窗（损坏与密码问题区分）；② **进度条不动（Core `SevenZipEngine.TestArchiveAsync`）**——`Check()`（=7z.dll TestArchive 语义，整包提取校验）阶段只触发 `FileExtractionFinished`（每条目 1 次，带 byte `PercentDone`）不触发 `Extracting`，旧实现在校验阶段零进度上报且校验后还冗余逐条目 `ExtractFile` 二次解压（固实包 O(n²)）；现订阅 `FileExtractionFinished` 上报 `ArchiveProgress`（CurrentFile/PercentComplete/FilePercentComplete，`Math.Clamp((double)e.PercentDone)`），删除冗余逐条目循环（返回值语义不变）。mztest 实测：130MB 未加密 RAR 1.4s→0.8s、105MB 未加密 7z 776ms 均有逐条进度；加密 RAR 三态正确（无密码 7ms False/正确密码 631ms True 89 进度事件/错误密码 5ms False）。Core 423 通过 / UI 131 通过（5 失败为预存 TestPreview 样本缺失）
- **10-05** — **修复 7z `mt=on` 并行压缩成果被 100% 丢弃（Core，方案 A）**：ZIP 自适应压缩「多线程」模式此前用 `MergeTempZipToWriter` 把 7z 多线程产物**解压回原始字节再用 .NET Deflate 重压**，导致 `mt=on` 成果全部作废、文件被压两遍、最终压缩级别与用户设置脱钩。删除该方法，改为「方案 A」：ZIP 内容以 7z 产物为准——纯压缩组直接 `File.Move`，混合组经 `ZipBinaryRewriter` copy-mode 原样复制并以 `Store` 追加其余条目；`ZipBinaryRewriter.NewEntry` 新增 `Store` 参数（方法 0 直写、字节透传），copy-mode 校验放宽至接受 Deflate64(9)。回归测试 `CompressAsync_MultiThreaded_PreservesSevenZipCompressedBytes` / `AddToArchiveAsync_MultiThreaded_PreservesSevenZipCompressedBytes` 锁定压缩字节不被重压；Core 构建 0 错误 0 警告、全量 577 通过 / 3 跳过（基线 572 + 新增 5），无回归
- **10-04** — 进度窗口原型对齐改造 T1–T11 全部落地（F1/F2/F4 已通过，F3 自动化部分 8✅/4◐/0✗）：**纠偏 v2 计划的原型误读**——删除 `TopDisplayMode`（全路径/仅目录/仅文件名）+ `DensityMode`（紧凑/标准/宽松），换为 v6 原型真实语义的内容模式（简约/详细/列表）+ 信息量分级（少/中/完整）；`ArchiveProgress` 扩展 `EntryKey`/`EntryStatus` 契约（**不改 `IArchiveEngine` 签名**）+ 新增 `EntryProgressItem` 行模型，三引擎 9 处埋点复用 `ConflictStatsCounter` 站点，列表模式逐文件 6 态（✓完成/✗出错/⏭跳过/⏳n%/○等待/已覆盖）；布局 10 行→7 行（批处理列表上移顶部）；5 张图标统计卡（第 5 张为**真实** `ParallelExtractDegree`「并行」，替换假「进程 N 线程」）；详细模式复用真实并行批次；补漏失败行内错误消息 + 复制 toast + 密码徽标入场动画；`ExtractFlow` 条目播种（ZIP/7z 走 `ListEntriesAsync` 含○等待行，TAR/GZ 按 D7 **不播种**走渐进建行，>5000 条转渐进）；新增 `PasswordRetryLoop` 错密码循环重弹兜底。**性能硬约束全部守住**：逐条目上报锁外 `Report`、列表虚拟化，六场景基准中位数回退均 ≤3%（最大 +1.5% 来自 `progress=null` 消费者）；F1 构建 0 错误 0 警告、F2 Core 549 + Avalonia 109 测试全绿、三语各 1257 key（新增 32/净删 0）。**已修复功能缺口（当日晚间补齐）**：CLI 解压（`--extract-here`/`-to-name`/`-smart`）直连 `engine.ExtractAsync` 绕过 `ExtractFlow`，致 ZIP 列表模式为空且「并行」卡不出现（主窗口路径不受影响）。复核发现缺口覆盖**两个叶子**而非一个——恰好 1 个压缩包时命中单文件叶子 `RunCliExtractWithProgressAsync`，多选时命中 `RunCliDirectExtractBatchAsync`，只修后者会出现「改完毫无变化」。已将 `ExtractFlow` 的播种/并行度入口放宽为 `internal` 并在两个叶子接线（多文件叶子另加逐包 `ClearEntries` 与并行度逐包重算）；新增 5 条播种路径回归用例；构建 0 错误 0 警告、Avalonia 114 + Core 549 测试全绿。**运行时复验：「并行」卡已证实**——`--extract-here` 单文件（15,000 条目 ZIP）切「完整」密度后采到 `⚙ / 并行 / 20`，数值与 `ParallelExtractDegree`/`ProcessorCount` 一致，修复前该卡不可能渲染；**播种亦已证实（`SizeText` 判据）**——行第 3 列绑 `EntryProgressItem.SizeText`，而 `SeedEntryItems` 建行设 `Size`、`UpdateEntryStatus` 的 upsert 兜底建行不设 `Size`，故兜底行 `SizeText` 恒为空串；实测单文件 51 条目 ZIP（1×1000MB + 50×1MB）渲染出 `⏳ | big.bin | 1000 MB | 40%` 与 `✅ | small_000.bin | 1 MB | 已完成`，`SizeText` 非空 ⇒ 行必来自播种。两项缺口**全部闭合**；顺带取得条目级 `⏳n%` 取证，并判定 `○等待` 行**结构性不可采**（并行按体积降序启动，列表头部恒为已启动条目，Pending 恒在虚拟化视口外），故不再以「未捕到 `○等待`」记为缺口
- **10-04** — **修复点击压缩包内任意条目即崩溃（WebView2 初始化异常逃逸）**：用户报告「点压缩包内条目后应用无提示直接退出」。**根因**——`PreviewPanel.axaml` 把 `NativeWebView` **常驻在活动视觉树**，而 `NativeWebView` 在 `OnAttached` 时初始化 WebView2，于是**任何**预览（含点目录、点不支持预览的格式）都会触发初始化；该初始化失败的异步异常在 UI 线程 Dispatcher 上抛出时栈上早已跳出预览调用（await 边界之外），应用又无任何未处理异常订阅者 → 未捕获异常直接终止进程。用户实测 `E_ACCESSDENIED (0x80070005)`，headless 环境复现为 `RPC_E_CHANGED_MODE (0x80010106)`，二者同源于 `NativeWebView.OnAttached`。**修复三层**——① `PreviewPanel.axaml` 移除常驻 WebView，改由 `EnsureWebViewForHtml()` **仅在 HTML 预览时惰性创建**到 `WebViewHost`（HTML 双轨仍需 WebView，故惰性而非彻底移除）；② `MainWindowViewModel.ShowPreviewAsync` 对 `entry.IsDirectory` **短路**到 `ShowUnsupported()`，跳过提取；③ `InstallWebViewGuard()` 订阅 `DispatcherUnhandledException`，命中 `LooksLikeWebViewFailure()` 即 `e.Handled=true` 并走 `HandleWebViewUnavailable()` → 自动降级 ReverseMarkdown 渲染。**按用户明确要求走方案 A+B：不强制下载 WebView2、不引入 `Microsoft.Web.WebView2.Core` 直接依赖**，WebView2 缺失一律降级而非崩溃。另修两处衍生缺陷：attach 异常与 `NavigationCompleted` 失败竞态致守卫窗口提前关闭（新增 `_liveWebView` 跟踪、守卫条件改为「创建窗口存在 **或** 有存活 WebView」且仅 `e.IsSuccess` 关窗）；静态 `_webViewGuardInstalled` 只在首个 Dispatcher 订阅、致全量套件跨用例竞态（改为按 Dispatcher 实例幂等安装的 `_guardedDispatcher`）。降级路径由「按压缩包内部路径 `File.ReadAllBytesAsync` 读 HTML」（该路径在归档内不存在，必然失败）改为新增同步 `ShowHtmlFallbackFromSource(string?)` 直接消费内存中的 `HtmlSourceContent`，并删除基于文件路径的死方法。**测试**：新增 `PreviewWebViewLazyInitTests` 6 条替换探索性的 `DirectoryPreviewCrashReproTests`（已删除），关键修正是**每条非 HTML 用例先挂载 `PreviewPanel` 再触发预览**并断言 `WebViewHost.Content == null`（初版两条在挂载前断言，属空转测试）。Avalonia **136 通过 / 0 失败 / 3 跳过**（139）、Core **423 通过 / 0 失败 / 2 跳过**（425）、`dotnet build -c Release` exit 0（2 warning 为既有 `TextEncodingDetector.cs:121` CS8604 与 `PreviewViewModel.cs:1406,1442` CS0618）。**LSP 环境问题（非本次引入）**：本机 Roslyn LSP 报约 900 个 CS0246/CS0103，连 `using Avalonia;`、`InitializeComponent`、`x:Name` 字段与 `[ObservableProperty]` 生成成员都无法解析，经 `git stash` 在 pristine HEAD 上复现**完全相同**的错误（甚至报出 HEAD 才有的 `HtmlPreviewWebView`/`ShowHtmlFallback`），确认为 LSP 未加载项目引用与 source generator 输出（`Active LSP clients: 0`）；同一手法亦确认上述 2 个 build warning 为既有 —— **本项目以编译器与测试为门禁，LSP 不可作门禁**。未验证：headless 仅能模拟 WebView2 **失败**路径，真实成功渲染待实机确认
- **10-01** — **解压选择器新增「保留完整路径」开关 + 左下通用参数区（修复「预览所见 ≠ 实际落盘」）**：解压目标目录选择对话框新增**左下参数区**（`RootGrid` 新增一行，浏览器网格下方、确定/取消上方），「保留完整路径」是该区第一个参数项；勾选变化经既有 `SchedulePreviewRebuild` 300ms 防抖实时重建预览树。**核心修复**——原对话框只返回路径字符串（`Task<string?>`），调用方拿到路径后**回头独立读** `settings.ExtractPreserveFullPath`（`MainWindowViewModel.cs:2375`、`DragDropService.cs:105`），用户在预览阶段无法表达意图，形成「预览所见 ≠ 实际落盘」；新增 `ExtractPickResult(DestPath, PreserveFullPath)` 强类型返回通道贯穿两个有对话框的消费点。**参数区是通用宿主**：`AddOption(key, labelKey, initial, onChanged, isEnabled, disabledHintKey)` 注册表 + `PickerOptionItem` 模型，渲染层只遍历注册表生成「标签 + 控件」、不认识任何具体 key，将来更多参数/别的模式加参数只扩展注册项、无参数则整区 `IsVisible=false` 隐藏（规则 6）。决策 A=仅本次生效不回写设置；决策 a=压缩包根目录（`currentFolder` 为空）禁用该参数 + ToolTip（两模式产出完全相同，由契约测试锁定）。实施经两轮审阅修正 **18 项缺陷**（2 阻塞 + 5 编译阻塞 + 2 高危布局 + 11 中低），含 `ExtractSettingsWindow` 两处遗漏调用点（否则 CS0029/CS0173）、横向 `StackPanel` 致 `WrapPanel` 永不换行溢出窗口、`ToolTip.ShowOnDisabled` 缺失导致禁用提示不显示。Avalonia **130 通过 / 0 失败 / 3 跳过**（基线 105 + 新增 25 条：落盘契约 3 + 架构守卫 4 + 参数区结构 5 + **预览↔落盘逐条对账 7** + 布局与提示配置 6）、Core 416 通过、`dotnet build` exit 0 / 0 error（6 个 warning 均为既有 `TextEncodingDetector.cs:121` CS8604 与 `PreviewViewModel.cs:1406,1442` CS0618，不在本次改动文件内）；其中 2 条关键回归锁做了**负控制验证**（注入缺陷确认测试失败）。参数区的禁用提示已做到**运行时行为级验证**（手工套 `ItemTemplate` 生成真实控件树，断言 `ShowOnDisabled=True`、`Tip` 解析为真实文案、`IsEnabled` 正确传导）。剩余 3 项 GUI 目视验收待实机：悬停时提示框**实际渲染弹出**（headless 已实测 `MouseMove` + `RunJobs()` 后 `ToolTip.GetIsOpen` 仍 false，用例已加显式 Skip 并写明原因）、多参数换行观感、解压设置窗口浏览链路

#### 2026-09

- **09-30** — 进度窗口增强 T1-T6 数据/模型层（T7-T9 UI 待续）：`ProgressDisplayMode` 枚举（TopDisplayMode 三模式 + DensityMode 三档）+ `ParallelBatchProgressItem` 批次行模型；`ProgressViewModel` 重构——模式/密度属性 + `NotifyDisplayProperties()` 集中通知、冲突统计组（`HasConflictStats`，压缩路径不上报隐藏）、已用/剩余时间 ETA、`UpsertParallelBatch`/`SetCurrentBatchItem` 批次集合（末档案终值兜底）、`CurrentFileLabel`/`FileCountText`/`BatchArchiveIndexText`；亮/暗主题成对新增 `ThemeStatusFailedBrush`/`ThemeStatusSkippedBrush`；三语各 +20 key（统计/时间/模式/密度/批次），三语 key 校验通过；构建 0 错误，Core 544 + Avalonia 96 测试全绿
- **09-30** — **文件选择入口统一到自定义选择器（消除原生对话框）+ 拖拽解压兜底改用带解压预览的对话框**：① `DragDropService` 目标目录检测失败时的兜底由原生 `OpenFolderPickerAsync` 改为 `CustomFilePickerDialog.ShowExtractFolderAsync`（初始路径=压缩包同级同名文件夹，实传 `_currentFolder`/`ExtractPreserveFullPath` 保证预览=实际），条目展开提前到选路径之前，顺带修掉「无可解压内容仍先弹一次框」的旧毛病；② 工具栏「添加文件」由 `OpenFilePickerAsync` 改为 `ShowOpenItemsAsync`（放开文件夹选择，与拖拽添加/`AddFilesToArchiveAsync` 语义对齐）；③ 密码管理器导入/导出迁移到自定义选择器；④ `CustomFilePickerDialog` 新增 `FileTypeOption` + `ShowSaveFileAsync(fileTypes/suggestedFileName)`/`ShowOpenFileAsync(fileTypes)`，`GetSelectedSaveExtension`/`MatchesFileFilter` 改为读选中下拉项的 Patterns（消除硬编码 `index==1`）；⑤ 全仓仅保留选择器内「系统浏览」逃生按钮的原生调用；⑥ 新增 `Picker_FileTypeJson`、清理 2 个无调用方 key；Avalonia 测试 105 通过 0 失败（含三语 key 集同步校验）
- **09-27** — 修复 main→alpha 合并丢失 TGA/AVIF/APNG 预览：`c677a65` 合并时 `PreviewViewModel.cs` 整文件冲突取 ours，丢弃 main 侧 `ShowTgaImage`/`ShowAvifImage`/`ApngDecoder`（+144/-16 行），三方合并恢复 4 个预览分发方法；`strings.zh-TW.json` 补 55 个缺失 key 使三语 key 集一致；`--no-incremental` 构建 0 警告 0 错误，Core 511 + Avalonia 96 测试全绿，TGA 无损/RLE 解码冒烟通过
- **09-27** — **设置项默认值批量修复（7 项「设置写了没人读」）**：① 压缩对话框读取 `DefaultFormat`/`DefaultLevel`（选项域守卫，非法值回退 zip/5，修复「压缩级别恒 5」）；② 解压对话框读取 `OpenFolderAfterExtract`（复选框恒不勾选）；③ `DeleteArchiveAfterExtract` 接入 GUI 全部 **6 处**解压成功路径（此前仅 CLI 生效）；④ `MaxRecentFiles` 生效（`RecentFilesManager.ResolveMaxEntries` 纯函数，替代硬编码 10）；⑤ `CleanTempOnStartup` 启动后台清理落地（`CleanTempOnStartupCore` fire-and-forget，对齐 WPF `App.xaml.cs:141`）；⑥ `AllowElevation` 关闭时直接提示不弹提权确认；测试镜像断言替换硬编码默认值断言；Avalonia 105 + Core 415 通过；剩余 3 项无消费者已立计划 [settings-unwired-keys.md](../.omo/plans/未开始/settings-unwired-keys.md)
- **09-26** — **APNG 动画预览支持**：复用 `PreviewType.AnimatedImage` 管线 + ImageSharp 解码器（`SixLabors.ImageSharp 3.1.5`），修复 `FrameDelay` Rational 类型转换（`uint`→`long`）；SKCodec 原生 `FrameCount=0` 无法识别 APNG 动画，ImageSharp 兜底解码 5 帧 APNG 正常播放；集成 `ShowGif` 统一管线（播放/暂停/逐帧/缩放/透明背景/信息面板帧数），Core/Avalonia 单测 509/509 通过
- **09-24** — 过滤/拖拽/右键解压统一并行化 + 并行冲突弹窗修复：`ExtractEntriesAsync` 改造为 dispatcher（命中文件 ≥2 且并行度 >1 自动走并行，与全量解压共用决策逻辑），原串行逻辑迁入 `ExtractEntriesAsyncSequential` 不变，新增 `ExtractEntriesAsyncParallel`（多实例并行 + 批次复用 archive + outputPathOverrides 路径覆盖 + 目录条目预创建）；修复并行路径 Ask 冲突弹窗静默降级为覆盖的 bug（同步 ResolvePath 只认同步回调 → 快速路径 + 信号量串行化 ResolvePathAsync）；`CreateExtractOptions` 返回类型非 null 消除 NRE 隐患；4 个新回归测试
- **09-23** — 修复 MultiThreaded 压缩模式 UI 冻结：`CompressGroupWithSevenZip` 挂接 `FileCompressionStarted` 进度事件，每 100ms 节流报告当前文件名 + 字节进度；mt=on 多线程事件并发触发，lock 保护计数与节流
- **09-18** — 自适应压缩重构为三开关正交设计：移除 `AdaptiveCompressionMode` 四选一枚举，替换为三个独立开关（自适应压缩 / 魔数检测 / 多线程压缩）；魔数检测仅自适应开启时可见；组合行为：仅自适应=用户规则列表+格式目录、仅多线程=无设置界面、自适应+多线程=仅存储格式选择；向后兼容旧设置文件自动迁移；6 个 i18n key
- **09-18** — 自适应压缩级别新增「多线程」模式（实验性）：SharpSevenZip `mt=on` 多线程压缩需压缩文件，Store 类文件直写 ZipWriter；统一帮助弹窗（HelpDialog 外壳 + AdaptiveHelpContent）；标题栏「实验性」橙色标签 + 帮助按钮；格式目录/用户规则 UI 可见性联动（Disabled 隐藏全部、MultiThreaded 隐藏用户规则）；11 个本地化 key
- **09-17** — ZIP 自适应压缩：按文件扩展名自动判断已压缩文件（60+ 格式：图片/音视频/字体/归档/已压缩文档），已压缩文件 Store（不压缩），其余保持用户选定级别；仅 Deflate/Deflate64 + 非加密 ZIP 有效；压缩对话框 ZIP 面板新增「自适应压缩」复选框；12 文件 +69 行
- **09-17** — 自适应压缩级别升级：三模式系统（Disabled / StoreForCompressed / SmartDetect）+ 格式目录（FormatCatalog 内置 40+ 格式 + 用户自定义）+ 用户规则覆盖（AdaptiveOverrideRule 8 级）+ 经验系数表（CompressionCoefficients 7 分类 × 7 级别 × ZIP/7z 双表）；SettingsWindow 新增自适应压缩模式选择 + 格式目录 + 规则管理面板；22 项单元测试；32 个本地化 key
- **09-16** — 压缩/解压性能优化（ZIP 解压并行 + 7z 多线程压缩）：① **ZIP 并行解压**自研多实例实现（SharpCompress 单实例线程不安全）——Round-Robin 分批 + **每批次复用 1 个 archive 实例**（减少 80-90% OpenArchive 开销）+ 进度报告锁竞争修复（曾因锁内 `progress.Report()` 导致 8 线程争用、100×1MB 解压从 0.3s 劣化到 8.5s 的 25x 回退）；② 缓冲区 256KB→4MB（ZipEngine/TarGzEngine/ZipBinaryRewriter）；③ `ParallelExtractDegree` 设置项（1-16，默认 CPU 核心数，1=串行回退）；④ **7z 多线程压缩 `mt=on` 实测 4.63x**（100×1MB/8核：38.7s→8.4s），压缩对话框 7z 面板 + 设置窗口全局默认值双开关；Core 380 + Avalonia 96 测试全绿
- **09-16** — NuGet 核心依赖全面升级：Markdig 0.40.0→1.3.2、SharpCompress 0.48.1→0.50.4、SkiaSharp 3.119.4→4.152.0、Svg.Skia 2.0.0.5→5.2.1、HarfBuzzSharp 14.2.0→14.2.1.3；96 Avalonia + 373 Core 测试全绿，0 构建错误（24 项 CS0618 SkiaSharp 4.x deprecation warning 为非阻塞技术债）
- **09-16** — Avalonia 12.0.4→12.1.2 全栈升级（Avalonia/Avalonia.Controls.DataGrid/Avalonia.Controls.WebView/Avalonia.Desktop/Avalonia.Themes.Fluent）；96 Avalonia + 373 Core 测试全绿，0 构建错误（新增 2 项 CS0618）
- **09-21** — 修复 HTML 预览安全设置回归三连：CSP 拼接缺少 `style-src` 指令导致内联样式被兜底 `default-src` 拦截（样式/脚本选项最严时预览样式全部丢失）+ CSP meta 注入在 `<!DOCTYPE>` 之前触发浏览器 Quirks Mode（布局行为异常）+ WebView 页面顶部一小条被预览滚动区裁切（WebView 移出 ScrollViewer 与预览滚动区平级）
- **09-21** — 文本预览语法高亮计划重写为 Avalonia 方案：废弃 WPF 版 AvalonEdit+XSHD 方案（AvalonEdit WPF-only），改用 **AvaloniaEdit 12.0.0 + TextMate**（`AvaloniaEdit.TextMate`，VS Code 语法全集覆盖 `TextExtensions` 40+ 扩展名、内置 DarkPlus/LightPlus 主题 `SetTheme` 一键切换）；架构确认 PreviewType（查看器）与 Language（高亮）分离 + 扩展名→魔数→JSON/INI 结构特征三级语言识别优先级链（配合当日落地的文本内容检测）；条目从 PLAN.md 已废弃表移回正式 P2 区
- **09-21** — CSV 预览接入编码选择器 + 修复魔数路径 CSV 被误判为纯文本：CSV 预览与 Text/Markdown/HTML 统一走 `DecodePreviewBytes()` 字节级解码管线（`RebuildCsv` 编码切换即时重建 DataGrid）；`MapFileFormatToPreviewType` 将 `FileFormat.Csv` 从 Text 组独立映射到 `PreviewType.Csv`（此前扩展名兜底已识别 Csv 却在最后映射被压回 Text，CSV 永远显示为纯文本）
- **09-21** — 文本格式内容识别扩展（B 保守版）：`DetectTextSubtype` 新增 JSON/INI 内容启发式——INI 用 `[Section]` 段头 + key=value 结构校验，JSON 用首字符 `{`/`[` + 括号配平（容忍 head 截断）+ `"key":` 引号键/数组元素判定，误报率≈0；为扩展名缺失的格式识别提供兜底信号（为未来语法高亮 Language 识别铺路）
- **09-20** — 文本预览编码选择器：Text/Markdown/HTML 预览统一接入 `File.ReadAllBytes` + `DecodePreviewBytes()` 字节级编码检测管线，支持用户手动切换编码（auto/UTF-8/GBK/GB18030/Shift_JIS 等 9 项），切换即时重渲染；预览工具栏新增编码选择 ComboBox + 本地化（zh/en 成对，4 key），语言切换自动重建下拉 DisplayName
- **09-17** — 修复文本预览 936 编码报错 + GBK 种子中文乱码：Avalonia 启动注册 CodePagesEncodingProvider（此前迁移遗漏导致 `Encoding.GetEncoding(936)` 抛 NotSupportedException，文本预览提示 "coding 936 无法预览"）；TorrentParser 尊重种子 `encoding` 字段（BitComet GBK 种子）+ 优先读取 `name.utf-8`/`path.utf-8`/`comment.utf-8` 后缀字段（BEP 惯例），实测 100DVD.rar 内中文种子 0/9 乱码
- **09-13** — HTML 预览 WebView 双轨升级 + 安全设置：NativeWebView 主体渲染 + ReverseMarkdown 降级路径（WebView 不可用时自动 fallback）；`</>` 源码/渲染切换按钮（HTML & Markdown 共用）；HTML 预览安全设置三开关（允许 JavaScript / 外部资源 / 导航，默认全关）+ CSP meta 注入 + NavigationStarting 拦截；设置窗口预览 tab 新增 HTML 子标签页（IconHtml 图标）
- **09-12** — 报错信息一键复制：AppMessageBox 统一复制按钮（Error/Warning 弹窗显示，复制内容含版本号+时间戳+完整消息，一处改动覆盖全部弹窗）+ 主窗口状态栏错误文本改用只读 TextBox 可选中复制
- **09-12** — 修复损坏压缩包测试静默通过：TestArchive 捕获 TestArchiveAsync bool 结果（RunWithProgress 的 completed 仅表示未取消/未抛异常，损坏判定此前被丢弃）+ PasswordService QuickVerifyPassword 改用严格 ZipArchive.OpenArchive
- **09-10** — 收藏相关 UI 图标统一：新增 FluentUI Bookmark 图标系列（Bookmark / BookmarkAdd / BookmarkMultiple / BookmarkOff）替换原星标，覆盖收藏管理器按钮、工具菜单、路径速选控件与文件选择器；图标测试窗口 PathIcon 资源键改为可复制
- **09-10** — 文件过滤编辑器布局优化：文件大小（最小值/最大值）和日期（起始/截止）从纵向排列改为水平排列，提升空间利用率
- **09-10** — 修复设置窗口语言面板「翻译贡献者{0}」占位符未被替换：`LanguageTranslatorText` 属性未传递翻译者名称参数给 `LocalizationManager.T()`，改为从 `AvailableLanguages` 获取当前语言的 `TranslatorText` 并格式化
- **09-10** — 压缩预览渐进式加载 + 骨架状态图标区分（FluentUI folder_sync）：`BuildSourceSubtree` 两阶段（浅层先行→全量逐源重建）、`AssembleCompressPreview` 装配、`SourceSubtree` 按源缓存 + `FilterSignature` 失效检测；`BuildDirectoryNode` 深度边界重构（`depth≥maxDepth` 子目录挂占位、文件仍枚举）+ `IsEmptyDirectory` 含占位视为非空；`PreviewTreeNode` 占位属性（`IsLoadingPlaceholder`/`DisplayLabel`/`IsTruncatedNode`/`ShallowClone`）；`ResultTreeView` 滚动位置保持；`CompressSettingsViewModel` 异步取消 + 250ms 节流渐进装配；9 个单测全绿（96 通过/0 失败）
- **09-10** — 解压预览冲突检测优化：ApplyConflictMarkers ①②③ 短路（destDir 不存在/过滤项/父目录短路）+ BuildExtractPreview 删除内联冲突 + MarkDirectoryConflicts 废弃；单包两阶段冲突检测（depth 2 快速上屏 → 全量后台补全）+ _conflictCts 取消 + PreviewTreeInvalidated 事件；ExtractSettingsWindow 订阅刷新 + 关闭取消；8 个单测全绿（86 通过/0 失败）
- **09-09** — 代码质量修复：ZipEngine sync-over-async 修正（async lambda 内 `GetAwaiter().GetResult()` → `await`）+ 4 处空 catch 块补充异常日志 + 补充 Core 层单元测试 69 个（FileConflictHelper/PathHelper/ArchivePath/LogRedactor，总计 370 测试全绿）
- **09-09** — 消除全部 39 项预存构建警告（AVLN5001/CS8602/CS8604/CS8620/CS8767/CS8826/CS0649/CS4014），dotnet build 达到 0 warnings 0 errors（301 测试通过）
- **09-05** — 修复「保存到密码库」不生效：PreviewViewModel 预览面板密码输入忽略 SavePermanently 标志 + TrySavePassword 静默吞异常
- **09-04** — 纯图标按钮补齐 ToolTip（31 个按钮全本地化，8 个文件）+ 全局 ToolTip 显示延迟调至 100ms（覆盖默认 400ms）
- **09-04** — 修复安装包缺失 `Resources\Icons` 格式图标：两个 `.iss`（`installer.iss` + `installer-selfcontained.iss`）`[Files]` 段仅打包 MenuIcons/Cursors，漏掉文件关联格式图标目录（zip/7z/rar/tar/tgz/gz/iso），安装后文件关联图标退化为应用通用图标
- **09-02** — 修复 ShellExt 复制目标 RID 路径 bug（阻断发布构建：.NET 10 RID 传播使 ShellExt 输出落入 `win-x64` 子目录，publish 报 MSB3030）

#### 2026-08

- **08-24** — WPF 对比审计补齐：7z.dll 运行时缺失引导弹窗（含启动接线修复——设置里的 SevenZipPath 此前从未灌入引擎）+ CLI 补 `--help`/`--test`/未知参数告警
- **08-23** — 加密压缩包交互对齐 WPF：工具栏「密码」按钮三态化（禁用钥匙/红锁待输入/绿开锁已匹配，点击输入或查看当前包密码）+ 可列出加密包取消密码后仍可浏览（未匹配状态，补输密码后解锁）+ 加密条目预览直接提示需要密码
- **08-21** — 文件列表列排序增强：三态循环（升→降→未排序恢复原始顺序）+ 列头箭头状态显示 + 排序状态持久化（window.json 与 WPF 兼容）+ 重填后自动重排 + 压缩率列排序 bug 修复
- **08-20** — WPF 差异补齐 P1 清零：智能打开路径（`SmartOpenPathResolver` + 选中条目/拖拽/主流程三处接线 + `ExtractArchive` 死代码修复）+ 便携模式 Temp 目录重定向（`AppSettings.GetTempDir` + 4 处替换）
- **08-20** — PPTX 预览修复：幻灯片顺序改用 presentation.xml 权威播放顺序（字典序错位修复）+ 占位符无 xfrm 时从 slideLayout 借位几何（标题/章节页不再空白）+ 分组形状坐标递归变换
- **08-20** — XLSX 预览改为纯表格还原：不提取列标题（列名统一 Column1..N），所有行（含合并大标题行）原样展示，保留定位式列数与行列上限
- **08-20** — 拖拽/右键「解压选中项」流程完全统一：`ExtractFlow.RunSelectedItemsExtractionAsync` 共享方法（压缩包一行批处理列表 + 状态驱动 + 失败统一弹窗）
- **08-19** — 拖拽添加到压缩包：WPF `Window_Drop` 三分支完整移植 + 复用解压冲突处理 + `AddFilesToArchiveAsync` 公共方法 + DragAddOverlay 覆层
- **08-18** — 图片预览能力系统：透明/动画能力注册表 + GIF 透明 + Animated WebP 预览
- **08-13** — 预览面板位置/显隐全面修复（四种布局移植 + 三入口统一 + 架构 bug 修复）
- **08-11** — 压缩/解压启动即时反馈（收集弹窗、加载遮罩、设置弹窗秒现）
- **08-09** — 压缩包注释读取展示（ZIP + RAR5，根目录预览）
- **08-08** — 便携模式（Portable.txt → Data/）+ 目录树「自动展开」开关
- **08-07** — 压缩/解压 CLI 流程对齐 + CompressFlow/ExtractFlow 公共流程抽取 + path-manifest A/B 数据集实施
- **08-05** — 解压路径统一（ExtractPathResolver 单一事实源）+ 进度窗口全面对齐 WPF
- **08-04** — 主题三态化（跟随系统/亮/暗）
- **08-03** — QuickPathPicker 自包含路径速选控件 + 默认路径优先级（context/explorer/recent/custom）
- **08-03** — 目录行聚合显示（大小=子树和 / 日期=最新 / 压缩后大小）+ 拖拽/右键解压流程统一
- **08-01** — 文件选择器多选（PickItems 模式：勾选累积 + 跨目录保留）

#### 2026-07

- **07-31** — QuickPathControl Tab 式速选面板重构 + CustomFilePickerDialog 统一路径选择 + 拖拽光标方案 C（自实现 OLE 拖拽）
- **07-27** — 元数据面板可配置系统（MetadataRegistry + 渲染引擎 + 设置 UI）+ 密码子系统全面补齐
- **07-23** — 拖拽系统重构（Avalonia DragDrop + DragDropService 后置解压 + 覆盖层）
- **07-22** — P1-3 FileFilterEditor 移植（三维过滤 + 预设管理 + 临时预设）
- **07-21** — Office 文档内容预览（DOCX/XLSX/PPTX 纯文本 + 表格）
- **07-20** — Shell/COM 集成移植（ShellIntegration + 文件关联 + 右键菜单 + COM host）+ 双击行为 CLI 分发
- **07-18** — emoji→PathIcon 替换（Phase 2）+ 文件列表行图标改用系统原生
- **07-17** — 紧凑度模式 + 上下文工具栏 + 结果预览面板
- **07-16** — 移除 WebView2 依赖（Markdown/HTML/PDF 纯 .NET 跨平台预览）+ 预览两阶段加载
- **07-15** — ICO 多帧画廊预览 + P0-2 压缩选项 + P0-3 魔数检测预览集成
- **07-13** — UI 功能补齐（对话框 + 控件 + 转换器）
- **07-02** — Phase 10: WPF 功能补齐（进度条/信息面板/状态栏）

#### 2026-06

- **06-21** — Phase 9: 文件列表交互补齐
- **06-21** — Phase 8: 设置窗口 TabControl 重构 + i18n 补全 + ComboBox 修复
- **06-19** — Phase 7: CLI 命令补齐 + IPC 多实例 + 10 个新对话框
- **06-17** — Phase 6: 样式统一与视觉打磨
- **06-17** — Phase 5: 工具栏按钮样式重构
- **06-15** — Phase 4: App.axaml 统一控件样式
- **06-11** — Phase 0: 项目骨架（首次提交）

### MantisZip.UI（WPF 遗留版）

> WPF 版（`MantisZip.UI`）已在迁移完成后**完全删除**，不再维护；本节仅保留迁移前的历史条目。完整历史见 [progress-wpf.md](progress-wpf.md)，仅作参考，不再追加新条目。

### 共享层（Core / ShellExt / 构建）— 里程碑

按版本分组，每组按日期从新到旧排列。

#### v0.5.2

- **10-10** — 进度上报能力增强（支撑 Avalonia 进度窗口两份计划）：`ArchiveProgress` 新增 `FileTotalBytes`/`BatchProcessedBytes`/`BatchTotalBytes`/`CompressionRatio`/`EntryCompressedBytes`/`TotalCompressedBytes`；`ZipEngine` 全报告站点填充 + `CompressAsync` 末尾成品 ZIP 中央目录**后置回填**逐条目压缩字节（分卷跳过、异常吞掉）+ MT 镜像阶段两阶段进度区间（镜像→前半段/7z→后半段，消除冻结）+ N 组 adapter 漏拷 `FilePercentComplete` 修复及改用组 `PercentComplete` 推导进度；`EntryProgressItem` 新增 `CompressedSize`/`CompressedText`；`ProgressDisplayCalculator` 新增 `MiddleEllipsis`。Core 619 通过 / 0 失败（+3 MiddleEllipsis +1 MT 镜像回归）
- **10-06** — 修复 7z/RAR「测试压缩包」进度条不动（Core `SevenZipEngine.TestArchiveAsync`）：`Check()` 阶段（=7z.dll TestArchive 语义，整包提取校验 CRC）只触发 `FileExtractionFinished`（每条目 1 次，`e.PercentDone` 为 byte）不触发 `Extracting` —— 旧实现校验阶段零进度上报，且校验后还冗余逐条目 `ExtractFile` 二次解压（约 2 倍工作量，固实包 O(n²)）；现订阅 `FileExtractionFinished` 上报 `ArchiveProgress{CurrentFile, PercentComplete, FilePercentComplete}`，并删除冗余逐条目循环（返回值仍由 `Check()` 的 `valid` 决定，语义不变）。mztest 实测 130MB 未加密 RAR 1.4s→0.8s、105MB 未加密 7z 776ms，均逐条实时进度

#### v0.5.1

- **10-01** — 修复 ZIP 中文文件名编码三处缺陷（Core，用户报告「拖拽添加中文文件后乱码」）：① `ZipBinaryRewriter.CompressNewEntry` 写 UTF-8 文件名时未置 bit 11（APPNOTE 6.4.4 要求非 ASCII 文件名必须置位），致 7-Zip/WinRAR/资源管理器/unzip 按 CP437 解码乱码——因 `OpenArchiveWithEncodingFallback` 的 `LooksLikeValidCjk` 启发式兜底，应用内不可见；② `CdEntry` 新增 `RawFileNameBytes`，既有条目原样写回，消除 `ReadCentralDirectory` 固定 UTF-8 解码 + `WriteCentralDirectory` 换编码写回的「解码→重编码」往返损坏（删除一个文件会连带毁掉无关条目）；③ 新增 `ResolveFileNameEncoding`，Add/Delete 路径显式 `options.FileNameEncoding` 优先于 `ZipHasUtf8Flag` 启发式，`IArchiveEngine.DeleteEntriesAsync` 加 `ArchiveOptions?` 参数、UI 透传 `AppSettings.ZipEncoding`。新增 7 条测试（含逐字节比对删除前后存活条目、负控制验证旧行为必然 FAIL）
- **10-01** — 版本号升至 v0.5.1（6 处同步）：`AppConstants.Version` / `csproj <Version>` / `installer.iss` 与 `installer-selfcontained.iss` 的 `#define MyAppVersion` 兜底值（原均停在 `0.4.4`）/ `docs/PLAN.md` 与 `docs/PROGRESS.md` 当前版本；新增 v0.5.1 发布说明（APNG·TGA 预览、ZIP 并行解压、7z 多线程压缩、保留完整路径开关、zh-TW 语言、7 项设置读取失效修复、Avalonia 12.1.2 升级）
- **09-27** — 批量修复 7 项设置读取失效：`AppSettings` 压缩默认值 / 解压后删包 / 启动清理临时目录 / 提权开关等设置项改动后不生效（读取路径与 setter 未对齐）
- **09-26** — 新增 APNG 动画预览（Core 魔数检测 `acTL` 块 + `PreviewType.AnimatedImage` 映射 `.apng`；解码器 Rational 类型转换修复）
- **09-26** — 新增 TGA 预览支持（Core TGA 魔数检测 + Avalonia ImageSharp 解码 + 红蓝通道交换修复）

#### v0.5.0

- **09-30** — 进度统计/批次数据通道下沉 Core（进度窗口增强 T1-T4）：`ArchiveProgress` 新增 8 个 nullable 字段（冲突统计 `SkippedFiles`/`FailedFiles`/`OverwrittenFiles` + ZIP 并行批次 `BatchIndex`/`BatchCount`/`BatchPercentComplete`/`BatchProcessedFiles`/`BatchTotalFiles`）+ `ExtractResult.SkippedEntries`/`OverwrittenEntries`；新增 `ConflictStatsCounter`（Interlocked 线程安全），三引擎 10 处 `ResolvePathAsync` 调用点 `File.Exists` 预检埋点（skip/overwritten/failed，完成时回填 ExtractResult 终值）；ZIP 并行批次上报索引/批级进度；清除 ZipEngine 9 处硬编码「正在压缩: 」前缀（`CurrentFile` 存 raw 路径）；新增 `ProgressDisplayCalculator`（前缀剥离/路径拆分/总进度/时长格式化）+ `ProgressSpeedTracker`（EMA 速度 + ETA，档案切换/字节回退重置）+ 15 个单测、`ProgressBatchItem` 统计/密码态/`StatusBrushName` 扩展
- **09-24** — 修复 MT 自定义 Store 格式分拣/写入不一致（Core）：`ZipEngine.ReadFileWithRetry` 写入阶段改用 4 参 `GetAdaptiveLevel`（传 `MultiThreadedStoreFormatIds`），与分拣阶段一致——此前分拣用 4 参判 Store、写入用 3 参重算忽略自定义列表，`.wav` 等自定义格式被分入 StoreGroup 却退回 Deflate（自打脸 bug）；`ZipEntryClassifier.IsCompressed` 签名放宽可空扩展名；测试矩阵落地（A 组 13 个分类器用例 + B/C/D 组 5 个 MT 端到端含加密路径、自定义 Store 格式、进度回归）
- **09-16** — 并行解压 + 7z 多线程压缩基础设施（Core）：`IArchiveEngine.SupportsParallelExtract` 属性 + `ArchiveOptions.ParallelExtractDegree`；`ZipEngine.ExtractAsyncParallel` 多实例并行（Round-Robin 分批、每批次复用 1 个 archive 实例、进度报告锁外上报）；`CopyBufferSize` 256KB→4MB（ZipEngine/TarGzEngine/ZipBinaryRewriter）；`ArchiveOptions.SevenZipMultithreaded`（默认 true）+ `SevenZipEngine.ConfigureCompressor` 写入 `CustomParameters["mt"]` + `CompressRequest`/`CompressService.BuildOptions` 映射；新增 `ParallelExtractTests`（5 用例）+ `SevenZipEngineTests` mt=on 验证（2 用例）
- **09-22** — 修复 GitHub Release 发版失败（构建）：release.yml 存在重复的 Portable-Web 打包步骤——`Compress-Archive` 步骤产出 `MantisZip-*-Portable-Web.zip`，而 `New-PortableZip` 同时段也产出同名文件（8-07 改名后撞名），Compress-Archive 遇已存在文件报 already exists 导致发版中断；删除旧 Compress-Archive 步骤，Web 便携包统一由 New-PortableZip（7z 打包 + 排除 PDB + 预置默认设置）产出
- **09-21** — 文本格式内容识别扩展（Core）：`DetectTextSubtype` 新增 JSON/INI 内容启发式 —— INI 用 `[Section]` 段头 + key=value 结构校验，JSON 用首字符 `{`/`[` + 括号配平（容忍 head 截断）+ 引号键/数组元素判定，误报率≈0；为扩展名缺失格式提供内容兜底信号（为未来语法高亮 Language 识别铺路）
- **09-12** — 修复损坏压缩包打开静默无报错（Core）：ZipEngine 打开改用严格 ZipArchive.OpenArchive（全零/垃圾 .zip 此前被 ArchiveFactory 魔数嗅探误判为 Tar、0 条目静默打开，现抛 ArchiveException）+ TarGzEngine.ListEntriesAsync 移除静默 catch（损坏 .tar 抛错不再静默空列表）+ 新增 3 个回归测试
- **09-04** — 压缩/解压 文件读写错误处理补齐：压缩侧 7z/加密 ZIP 新增 `ReadErrorHandler.FilterUnreadableFiles` 预检（错误弹窗 / 跳过 / 中止，对齐 ErrorResolver）；解压侧三引擎 `ExtractAsync`+`ExtractEntriesAsync` 补 `IOException` 捕获与 per-entry 兜底（被占用条目跳过继续，不再让单个文件中止整个解压）
- **08-31** — .NET 9 → .NET 10 升级（LTS，支持至 2028-11）：全部 7 个项目 TargetFramework 更新 + 移除废弃 `Avalonia.Diagnostics` 包 + `System.Drawing.Common` 升级至 10.0.8
- **08-31** — 卸载/更新文件占用修复：`CloseApplications=yes`（Restart Manager 检测用户关闭占用进程）+ `ShellIntegration.Uninstall` 重启 Explorer 释放 comhost.dll 句柄
- **08-24** — 发布脚本 copy-7z-dll 按 PE 头校验架构：x86 目录不再误拷 64 位 7z.dll（历届安装包均受影响），缺失架构警告跳过 + installer x86 行 skipifsourcedoesntexist
- **08-19** — 添加到压缩包重名条目冲突处理（AddConflictHelper 条目名级解析）+ 保留浏览目录前缀（entryBasePath）
- **08-09** — Core 新增 ArchiveCommentReader（ZIP + RAR5 注释统一读取）+ ZIP 注释编码兼容（GBK 回退）
- **08-07** — 发布管线切 Avalonia + 便携版双变体 + WebSetup + 移除调试符号

#### v0.4.5

- **08-07** — ShellExt COM 右键菜单扩展 + 路径清单统一 Core 侧（A/B 数据集）
- **08-03** — ArchiveEntryExtractor 支持纯 GZip/ISO 单条目提取 + 目录聚合统计 DirStats 增加 NewestModified
- **07-27** — 异步冲突解析 API（CompressConflictResolver async 化 + 引擎异步冲突解析）
- **07-20** — Avalonia Shell/COM 集成

#### v0.4.4

- **07-02** — 压缩包路径处理一站式重构 + 安装包增强（.NET 9 自动下载）
- **06-30** — 魔数检测预览系统 Phase 1（Core 侧）

#### v0.4.x 早期

- **06-22** — DynamicFormatOptions 压缩格式动态选项（v0.4.3）
- **06-20** — ZIP copy-mode 优化（ZipBinaryRewriter，v0.4.2）
- **06-18** — 自包含安装包（v0.4.1）
- **06-15** — 发布基础设施（v0.4.0）

#### v0.3.x

- **06-15** — 完全移除 SharpZipLib 生产依赖 + DPAPI → AES-GCM 跨平台加密（v0.3.13）
- **06-11** — RAR 提取进度条（v0.3.13）
- **06-08** — ZIP 编码兼容性（v0.3.11）
- **05-31** — ShellExt COM 组件创建（v0.3.7）
- **05-28** — 引擎统一（SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip，v0.3.4）

---

## 历史设计方案索引

以下设计方案对应功能已在过往版本中完成，对应设计文档存于 `.omo/plans/` 供回溯参考：

| 功能 | 设计文档 | 实现版本 |
|------|----------|:--------:|
| 进度窗通道行左右分组 + 信息列 + 详细常显 + 路径两列 + 底纹修复（`ArchiveProgress` 4 新字段 + 行模型 8 字段 + 左右分组通道行 + 批级底纹 + ToolTip + 详细门禁移除/非并行单行合成 + `MiddleEllipsis` + N 组 adapter 漏拷修复） | [progress-window-channel-info.md](.omo/plans/已完成/progress-window-channel-info.md) | 未发布 |
| 逐文件压缩后大小 + 压缩率 + 整体压缩汇总（`CompressAsync` 后置回填成品 ZIP 中央目录 + 列表「压缩后大小 (率%)」列 + 统计卡段 2 整体汇总 + `EntryProgressItem.CompressedText`） | [progress-file-compression-ratio.md](.omo/plans/已完成/progress-file-compression-ratio.md) | 未发布 |
| Avalonia: WPF 差异补齐总表（P0–P2 全部清零：双击行为/删除原包、便携模式（含 Temp 重定向）、文件过滤控件、默认路径优先级、信息面板持久化、智能打开路径（含 `ExtractArchive` 死代码修复）、冲突对话框暂停/取消、密码导入导出、收藏夹、Enable 设置、AllowElevation 等） | [avalonia-wpf-diff-plan.md](.omo/plans/已完成/avalonia-wpf-diff-plan.md) | v0.5.0 |
| 添加到压缩包重名条目冲突处理（`AddConflictHelper` 条目名级解析、语义方向与解压相反：新数据更新/更大→覆盖；ZIP copy-mode `keepEntryNames` 排除被覆盖条目 + legacy Phase 2 应用解析结果；7z 覆盖经 `ModifyArchive`(index→null) 删除 + `CompressFileDictionary` Append 重加；Avalonia Ask 弹窗复用 ConflictDialog，新标题 key `AddConflict_Title`） | [add-archive-conflict-handling.md](.omo/plans/已完成/add-archive-conflict-handling.md) | v0.5.0 |
| Avalonia 拖拽添加（`MainWindowViewModel.AddFilesToArchiveAsync` 抽取 + WPF `Window_Drop` 三分支移植：已打开+压缩包→切换打开 / 已打开+文件→确认框→添加到 `CurrentFolder` / 未打开→打开或 `CompressSettingsWindow` 预填 + `DragAddOverlay` 窗口内两色覆层（绿=可添加/红=格式不支持，呼吸动画对齐拖拽解压）+ 文件夹拖入支持） | [drag-add-overlay.md](.omo/plans/已完成/drag-add-overlay.md) | v0.5.0 |
| Avalonia 拖拽直接解压（纯 Win32 独立线程覆盖层三色状态机 + 呼吸动画 + WindowFromPoint/ShellWindows 目标检测 + #32770 EnumChildWindows + 自实现 OLE 拖拽光标方案 C + DragPreviewBitmapBuilder 预渲染位图） | [drag-drop-direct-extract.md](.omo/plans/已完成/drag-drop-direct-extract.md) | v0.5.0 |
| 解压预览冲突检测优化（①目标根不存在短路 ②过滤项跳过 ③父目录短路 + `ApplyConflictMarkers` 共享服务 + ④单包两阶段：depth 2 快速上屏 → 全量后台补全 + `PreviewTreeInvalidated` 事件刷新） | [extract-preview-conflict-detection-optimization.md](.omo/plans/已完成/extract-preview-conflict-detection-optimization.md) | v0.5.0 |
| 密码错误 vs 文件损坏精准分类（`PasswordVerificationResult` 四态 + `PasswordVerifyInfo` + `TryMatchPasswordEx` 按 HRESULT/异常类型分类，损坏文件不再误报"密码错误"，密码库匹配遇损坏立即停止） | [password-error-classification.md](.omo/plans/已完成/password-error-classification.md) | v0.5.0 |
| 压缩预览渐进式加载（浅层先行→全量逐源重建装配 + `SourceSubtree` 按源缓存 + `FilterSignature` 失效 + `ResultTreeView` 滚动位置保持 + 占位节点属性 + `BuildDirectoryNode` 深度边界重构 + 9 单测） | [compress-preview-progressive-loading.md](.omo/plans/已完成/compress-preview-progressive-loading.md) | v0.5.0 |
| 图片预览能力系统（`PreviewCapabilities` 能力注册表 [Flags]：Zoom/Transparency/FlattenAlpha/AnimationControls 取代 `HasXxxControls` 硬编码 + `PreviewType.Gif`→`AnimatedImage`（GIF/WebP 动画共用）+ GIF 透明棋盘格 + Animated WebP 分流（SKCodec `FrameCount>1`）） | [image-preview-capabilities.md](.omo/plans/已完成/image-preview-capabilities.md) | v0.5.0 |
| Office 文档内容预览增强（DOCX 大纲+全文+表格+Markdown 表格、XLSX DataGrid、PPTX Canvas 定位+分页；WebView 双轨基建于 2026-09-13 完成） | [office-content-preview-avalonia.md](.omo/plans/已完成/office-content-preview-avalonia.md) | v0.5.0 |
| 文本预览编码选择器（Text/Markdown/HTML 预览统一接入字节级编码检测管线，用户手动切换编码即时重渲染；预览工具栏新增编码选择 ComboBox + 自动检测编码显示 + 本地化；激活 `TextEncodingPreference` 持久化；Core `TextEncodingDetector` 新增 `DetectEncoding`/`DetectAndDecodeText`/`DecodeText(byte[], string?)` 字节级 API） | [text-preview-encoding-selector.md](.omo/plans/已完成/text-preview-encoding-selector.md) | v0.5.0 |
| 拖拽/右键解压流程统一（`SelectedItemsExtractService` 统一解压动作、`TarGzEngine` 按条目提取、冲突统一走设置 6 策略 + 统一 Ask 弹窗、拖拽路径语义与右键一致、`MapConflictActionString` 连字符映射漏洞修复） | [drag-extract-unify.md](.omo/plans/已完成/drag-extract-unify.md) | v0.4.5 |
| 目录行聚合显示（`DirStats`+`ComputeDirectoryStats` 增加 `NewestModified`；Avalonia `ArchiveItemModel` 显示属性改派生计算属性 + `CompressedSizeAvailable`；`PopulateEntries` 基于过滤后 `filteredSource` 应用聚合） | [directory-size-date-aggregate.md](.omo/plans/已完成/directory-size-date-aggregate.md) | v0.4.5 |
| 路径清单统一（A/B 数据集：预览=实际绝对一致，CompressPlan 唯一事实来源 + 压缩/解压过滤白名单 + IsBuildPending 按钮门禁） | [path-manifest-unification.md](.omo/plans/已归档/path-manifest-unification.md) | v0.4.5（⏳ 交互清单待用户 GUI 验证） |
| 统一路径快捷选择（WPF QuickPathControl + 数据层；Avalonia 演进为 Tab 式速选面板 + CustomFilePickerDialog，QuickPathBuddy 概念并入 Tab+搜索一体化，QuickPathPreDialog 过渡方案废弃） | [quickpath-unified.md](.omo/plans/已归档/quickpath-unified.md)（已归档，Avalonia 部分被 [quickpath-control-redesign.md](.omo/plans/已归档/quickpath-control-redesign.md) 取代） | v0.4.3+（Avalonia 演进 v0.4.5） |
| QuickPathPicker 自包含路径速选控件（Compress/Extract/Settings 三宿主，AutoCompleteBox 补全 + ⭐🕐🪟 浮层 + 目录归一化，浏览器差异经注入委托） | [2026-08-03-quickpath-picker.md](docs/superpowers/plans/2026-08-03-quickpath-picker.md) + [设计](docs/superpowers/specs/2026-08-03-quickpath-picker-design.md) | v0.4.5（⏳ 待用户 GUI 验证） |
| 文件选择器多选（PickItems 模式：勾选累积 + 跨目录保留 + 右栏已选面板；CompressSettingsWindow 合并「添加文件/文件夹」单按钮） | [file-picker-multi-select.md](.omo/plans/已完成/file-picker-multi-select.md) | v0.4.5 |
| 可排序的默认路径优先级（文件选择器初始路径 context/explorer/recent/custom） | [path-priority-sortable.md](.omo/plans/已完成/path-priority-sortable.md) | v0.4.5 |
| 解压路径统一（`ExtractEntriesAsync` + `pathOverrides`，单一事实源 `ExtractPathResolver`） | [extract-path-unification.md](.omo/plans/已完成/extract-path-unification.md) | v0.4.5 |
| 移除 WebView2 依赖（Markdown/HTML/PDF 跨平台预览） | [remove-webview2-preview.md](.omo/plans/已完成/remove-webview2-preview.md) | v0.4.5 |
| 便携版模式 | [portable-mode.md](.omo/plans/已完成/portable-mode.md) | v0.4.5 |
| 文件冲突对话框暂停/取消 | [conflict-dialog-pause-cancel.md](.omo/plans/已完成/conflict-dialog-pause-cancel.md) | v0.4.5 |
| 压缩选项增强（7z/ZIP 格式参数扩展） | [compression-options-enhancement.md](.omo/plans/已完成/compression-options-enhancement.md) | v0.4.5 |
| 上下文工具栏重构（目录树+文件列表） | [context-toolbars.md](.omo/plans/已完成/context-toolbars.md) | v0.4.5 |
| 解压/压缩结果预览面板 | [result-preview-panel.md](.omo/plans/已完成/result-preview-panel.md) | v0.4.5 |
| 元数据信息面板可配置 | [metadata-panel-configurable.md](.omo/plans/已完成/metadata-panel-configurable.md) | v0.4.5 |
| 紧凑度模式（Compactness Mode） | [compactness-mode.md](.omo/plans/已完成/compactness-mode.md) | v0.4.5 |
| 预览两阶段加载（信息栏+内容分离） | [preview-two-phase-loading.md](.omo/plans/已完成/preview-two-phase-loading.md) | v0.4.5 |
| Avalonia: Shell/COM 集成移植 | [avalonia-shell-com-integration.md](.omo/plans/已完成/avalonia-shell-com-integration.md) | v0.4.5 |
| Avalonia Phase 10: WPF 功能补齐 | [avalonia-phase10-feature-parity.md](.omo/plans/已完成/avalonia-phase10-feature-parity.md) | v0.4.5 |
| Avalonia: i18n 补齐 + 杂物清理 | [avalonia-i18n-and-cleanup.md](.omo/plans/已完成/avalonia-i18n-and-cleanup.md) | v0.4.5 |
| 压缩解压文件筛选 | [file-filter-feature.md](.omo/plans/已完成/file-filter-feature.md) | v0.4.5 |
| emoji 替换为 Fluent UI PathIcon + 文件列表行图标改用系统原生 | [emoji-to-pathicon.md](.omo/plans/已完成/emoji-to-pathicon.md) | v0.4.5 |
| 双击行为 + 解压后删原包 | [doubleclick-extract-settings.md](.omo/plans/已完成/doubleclick-extract-settings.md) | v0.4.4+ |
| 魔数检测文件真实格式 | [preview-magic-detection.md](.omo/plans/已完成/preview-magic-detection.md) | v0.4.4 |
| 密码流程统一 | [password-flow-unification.md](.omo/plans/已完成/password-flow-unification.md) | v0.4.4 |
| 字体预览连字效果开关（HarfBuzzSharp shaping + `CheckFontSupportsLigature` 连字检测 + `IsLigatureEnabled`/`ToggleLigature` 命令 + `CanLigatureToggle` 灰禁用 + 工具栏按钮 + `FontPreviewEnableLigature` 持久化） | [font-preview-ligature.md](.omo/plans/已完成/font-preview-ligature.md) | v0.4.4 |
| 致谢贡献者名单 | [contributors-panel.md](.omo/plans/已完成/contributors-panel.md) | v0.4.3+ |
| 安装程序 .NET 9 自动下载 | [installer-dotnet-autodownload.md](.omo/plans/已完成/installer-dotnet-autodownload.md) | v0.4.3+ |
| 快速压缩拆分为独立/合并两项 | [split-compress.md](.omo/plans/已完成/split-compress.md) | v0.2.10 |
| 加载大文件 overlay | [archive-loading-progress.md](.omo/plans/已完成/archive-loading-progress.md) | v0.3.1 |
| 添加到/从压缩包删除 | [archive-add-delete.md](.omo/plans/已完成/archive-add-delete.md) | v0.2.9 |
| 暗色/亮色主题 | [dark-theme.md](.omo/plans/已完成/dark-theme.md) | v0.2.9 |
| 日志隐私脱敏 | [log-privacy-redaction.md](.omo/plans/已完成/log-privacy-redaction.md) | v0.2.8 |
| 国际化 (i18n) | [i18n-localization.md](.omo/plans/已完成/i18n-localization.md) | v0.2.8 |
| 智能解压 (Smart Extract) | [smart-extract.md](.omo/plans/已完成/smart-extract.md) | v0.2.10 |
| 文件列表筛选/搜索 | [file-list-filter-search.md](.omo/plans/已完成/file-list-filter-search.md) | v0.3.8 |
| 引擎统一 (SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip) | [engine-unification-sharpcompress.md](.omo/plans/已完成/engine-unification-sharpcompress.md) | v0.3.4 |
| 文件大小进度条 | [file-size-progress-bar.md](.omo/plans/已完成/file-size-progress-bar.md) | v0.3.4 |
| PNG 透明通道控制 | [png-transparency-3way.md](.omo/plans/已完成/png-transparency-3way.md) | v0.3.4+ |
| 批量进度文件列表 | [batch-progress-list.md](.omo/plans/已完成/batch-progress-list.md) | v0.3.5 |
| 解压配置面板 (ExtractSettingsWindow) | [extract-settings-window.md](.omo/plans/已完成/extract-settings-window.md) | v0.3.6 |
| COM 右键菜单 | [com-context-menu.md](.omo/plans/已完成/com-context-menu.md) | v0.3.7 |
| COM 迁移映射表 | [com-migration-mapping.md](.omo/plans/已完成/com-migration-mapping.md) | v0.3.7（辅助文档） |
| 压缩窗口密码 Tab 重设计 | [design-compress-password-tab.md](.omo/plans/已完成/design-compress-password-tab.md) | v0.3.7-refined-2 |
| 关于窗口重设计 | [about-window-redesign.md](.omo/plans/已完成/about-window-redesign.md) | v0.3.7-refined-4 |
| 文件关联 per-extension ProgId | [file-assoc-per-extension.md](.omo/plans/已完成/file-assoc-per-extension.md) | v0.3.9 |
| 移除 SharpZipLib 注释编辑耦合 | [remove-sharpziplib.md](.omo/plans/已完成/remove-sharpziplib.md) | v0.3.9 |
| ZipEngine SharpZipLib 完全迁移 (加密路径→SharpSevenZip) | [zipengine-sharpcompress-migration.md](.omo/plans/已完成/zipengine-sharpcompress-migration.md) | v0.3.13 |
| 压缩流程统一化 (CompressService) | [compress-service-unify.md](.omo/plans/已完成/compress-service-unify.md) | v0.4.0 |
| 发布 Release | [release-automation.md](.omo/plans/已完成/release-automation.md) | v0.4.0 |
| 返回上级目录 (.. 导航行) | [parent-directory-entry.md](.omo/plans/已完成/parent-directory-entry.md) | v0.4.0 |
| ZIP 压缩流直拷优化 (ZipBinaryRewriter) | [zip-copy-mode-optimization.md](.omo/plans/已完成/zip-copy-mode-optimization.md) | v0.4.2 |
| UAC 提权 + 权限不足处理 | [uac-elevation-permission.md](.omo/plans/已完成/uac-elevation-permission.md) | v0.4.2 |
| 自包含安装包发布 | [self-contained-installer.md](.omo/plans/已完成/self-contained-installer.md) | v0.4.2 |