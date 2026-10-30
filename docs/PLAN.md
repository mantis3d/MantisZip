# MantisZip — 开发计划

> 未来待开发功能规划。已实现的功能见 [docs/PROGRESS.md](PROGRESS.md)，技术架构见 [docs/ARCHITECTURE.md](ARCHITECTURE.md)，每项计划的完整设计见 `.omo/plans/`。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-10-29  
**当前版本**: 0.5.5


---

## 这份文档怎么读

> **2026-10-24 结构重构**：原 P1–P4 优先级分档废弃。原因有两个：① 旧 P1 里混着「已做完主体只剩尾巴」和「该砍」的计划，读起来像全部要做；② 旧 P4 里混进了「已完成计划的遗留子项」，两者性质不同。现在按**「现在做，还是不做」**分档，每档只有一个含义。

| 档位 | 含义 | 承诺 |
|:--:|---|---|
| **S** | 本轮（迭代 1）就做 | 已排期，做完就发版 |
| **A** | 下一批发布（按 `v0.5.x` 阶段交付） | 已排期到具体版本号；每完成一版与用户讨论一次，从 B 档择优补入后续阶段（**滚动管道**，2026-10-29 起） |
| **B** | 有价值，排队 | **不排期**，有空档再取 |
| **进行中** | 主体做过、还差一截 | 需要单独决定「继续做完」还是「冻结」 |
| **已冻结** | 想做，但现在不投入 | 附**解冻条件**，条件不满足不讨论 |
| **候选删除** | 建议从计划库移除 | 等你确认后我才移动计划文件 |

分档判据（按此顺序）：

1. **用户可感知频率** — 每天用的路径 ≫ 一周一次 ≫ 一辈子一次
2. **杠杆** — 做完能让后续 N 个计划变便宜或变安全
3. **风险方向** — 削减「点了没反应」类失信 bug 优先于增加功能面
4. **不可逆成本** — 重构面积 × 回归风险 ÷ 可用时间

补充约定：

- **每行的「设计文档」链接是唯一事实源**，本表的说明只记录「要不要做、做到哪了」，不复制实施细节。
- 当前策略是 **「面向公开用户 + 少而稳」**（2026-10-24 确认）：宁可少做，不做半成品；可感知的「丑/慢/点了没反应」优先于功能数量。
- **已知但这次不修的问题不在这里**：进 [KNOWN_ISSUES.md](KNOWN_ISSUES.md)（规则 21）。本文件只装「要不要做」的决定；说得出方案与工时的才算计划，只是「知道它有问题、现在不动」的进登记簿。

---

## S 档 —— 本轮必做（✅ 已全部完成，2026-10-28）

> **执行顺序（2026-10-24 讨论定，2026-10-26 / 10-27 / 10-28 按完成情况更新）**：~~图标~~（已完成）→ ~~接缝~~（已完成）→ ~~预览~~（已完成）→ ~~自动更新~~（已完成）。先做「一眼能看出变化 + 零逻辑风险」的，再做「掏返工税」的，最后做「地基先就位才划算」的。「压缩包内重命名」原排在最后，2026-10-28 已移回 A 档——它是 A 档「暂存区」的地基，与 A 档相邻做完能省一次返工，不必占本轮 S 档窗口。
>
> 已完成并归档：⚡ 插队项「删除压缩包内目录静默无效」（2026-10-26，随 v0.5.4）、S 档「图标归一化」（2026-10-26，随 v0.5.4）、S 档「意图-执行接缝收口（含波次 3 全仓清账）」（2026-10-26，随 v0.5.4）、S 档「预览取数层内存优先（切片 1+2+4）」（2026-10-28，随 v0.5.4）、S 档「自动更新检测」（2026-10-28，随 v0.5.4）、⚡ 插队项「安装器产品身份统一 + 旧版迁移桥」（2026-10-27，随 v0.5.4，issue #45）、⚡ 插队项「压缩包识别分层（容器 / 子类型）+ 自解压 exe 支持」（2026-10-27，随 v0.5.4，issue #54 / #36）—— 详见 [PROGRESS.md 历史设计方案索引](PROGRESS.md)。

> S 档所有条目均已落地并归档（见上），本节不再保留待办表格。

> 本轮验收口径（✅ 已达成）：公众能感知到的一件事——**安装完会自己知道有新版本**。（「压缩包内重命名」已于 2026-10-28 移回 A 档，与「暂存区」相邻做；图标归一化、意图-执行接缝于 2026-10-26、预览取数层内存优先于 2026-10-28 完成，随 v0.5.4 验证）

---

## A 档 —— 接下来的发布（按版本交付，滚动调整）

> **2026-10-29 结构变更**：原「阶段 A1 / A2」改为**按版本号交付**——一个阶段 = 一个要发布的小版本（v0.5.5、v0.5.6 …）。**每完成一版就和用户讨论一次**，从 B 档里挑相对优先度高的补进后续阶段；A 档因此是**滚动管道**，不是一次性排死的清单。
>
> **执行约定（2026-10-29 定）**：
> 1. 开工某个阶段前，先核对 `src/MantisZip.UI.Avalonia/AppConstants.cs` 的 `Version` 是否等于该阶段版本号；**不一致就停下来问用户**「是否先升版本」，不擅自改版本号（规则 2）。
> 2. 阶段完成 = 发版（打 tag）→ 计划归档 → 与用户讨论下一阶段从 B 档补什么（规则 1 的计划归档同一动作里做）。
> 3. 阶段工时只做参考；超了不追加，直接砍尾部或把剩余项退回 B 档。
> 4. 实现阶段时**尽量拆成多个子代理并行**（用户 2026-10-28 起的固定要求）：按任务边界分派、每个子代理只碰约定文件、构建与测试由父 agent 统一跑（避免 `obj/bin` 互锁）。
>
> **为什么这样切**：同一批文件只开一次刀。v0.5.5 三项都动 `CustomFilePickerDialog` / `QuickPathPicker` / 三处地址栏，一次跑完省一次全量回归；编辑器（重命名 + 暂存区）已降 B 档，等它被选中时再与 `ZipBinaryRewriter` 同批做。

### 阶段 v0.5.5 —— 文件选择与导航（约 8-16h，面包屑已完成，剩内嵌选择器）

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| ✅ 面包屑地址栏（PathBreadcrumb） | [path-breadcrumb.md](.omo/plans/已完成/path-breadcrumb.md) | 🟡中 | 6-8h | 三处地址栏（主窗口 / QuickPathPicker / CustomFilePickerDialog）改为资源管理器式面包屑：段点击直达 + Ctrl+L 进编辑态 + 段数阈值折叠 + 虚拟根段。2026-10-21 已按代码核准（`TryConfirmFile` 无模式判断须保持现状；`Path` 定为 StyledProperty + OneWay；QuickPathPicker 4 宿主纳入回归）。接缝改造后它的头号风险（逐字符回写 Path）已降级为「按钮可用性 / 实时提示」。**2026-10-29 已完成并归档**（三处接入 + 6 个无头用例；真机冒烟项见计划末「未做」） |
| 文件选择器去弹窗化（内嵌控件） | [embedded-file-picker.md](.omo/plans/进行中/embedded-file-picker.md) | 🟡中 | 8-16h | 把文件/目录选择器内嵌进压缩/解压设置窗：先抽纯控件再分别接入，不动 `CustomFilePickerDialog` 静态入口与「系统浏览」豁免。原注「与接缝计划有交集，排在接缝之后做可省返工」——接缝已于 2026-10-26 完成，前提满足 |

> **v0.5.5 验收口径**：三处地址栏都能像资源管理器一样点段跳转、Ctrl+L 编辑；压缩/解压设置窗不再弹模态选择器，选完当场回填；窗口缩放与深浅主题下都不丢焦点、不串路径。

### 阶段 v0.5.6 —— 文件列表体验（约 9-13h）

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 文件列表缩略图查看方式 | [file-list-thumbnails.md](.omo/plans/未开始/file-list-thumbnails.md) | 🟡中 | 4-6h | 三种布局循环切换（详情 / 平铺 / 内容行）+ 内容卡三档 + 持久化。**「现代感」最大来源**，复用 `PreviewService` 的 head 缓存（v0.5.4 已做取数层内存优先） |
| 骨架态实时进度计数器 | [skeleton-progress-counter.md](.omo/plans/未开始/skeleton-progress-counter.md) | 🟢低 | 3-4h | 骨架期每 0.5s 更新目录节点的文件数/大小（现在是静态「加载中」）。从「小活清仓」提到 A 档——它直接决定打开大包时的第一印象 |
| ICO 文件自身图标显示 | [ico-file-icon-extract.md](.omo/plans/未开始/ico-file-icon-extract.md) | 🟢低 | 2-3h | 列表里 ico 文件显示自身嵌入图标（现在显示通用图标）。与缩略图同属「列表里看得见」的一批 |

> **v0.5.6 验收口径**：列表能在三种布局间循环并记住选择；打开 1000+ 条目的包时骨架区能看到数字在涨；ico 文件在列表里显示自己的图标而不是通用图标。

### 阶段 v0.5.7 —— 预览与接线质量（约 15-21h）

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 预览取数层内存优先（切片 3：Office） | [preview-memory-first.md](.omo/plans/已完成/preview-memory-first.md) | 🟡中 | 5-7h | Office 各 `*.Open(Stream)`（`SpreadsheetDocument` / `WordprocessingDocument` / `PresentationDocument`），字体明确豁免。三者本来就在解析完整文档，省下的只是落盘，所以单独排在 A 档而不占 S 档 |
| AXAML 编译期绑定（渐进式开启 `x:CompileBindings`） | [axaml-compiled-bindings.md](.omo/plans/未开始/axaml-compiled-bindings.md) | 🟡中 | 10-14h | 现状 50 个 `.axaml` 只有 1 个开编译绑定、13 个有 `x:DataType` ⇒ 属性名写错只在运行期表现成「没值 / 红字校验文本 / 永远默认值」。渐进三批推进 + `-Task guards` 第 9 条 `compilebindings`（只约束新增与已迁移文件，存量不判红）。它是规则 23「接线错了就失败得响」的兜底 |

> **v0.5.7 验收口径**：Office 三族预览不再先落盘；迁移过的 axaml 里属性名写错会**构建失败**而不是静默空白。

### 阶段 v0.5.8 —— 压缩任务队列（P0–P2，约 18-26h）

> TeraCopy 式串行队列：多选/批量压缩不再并发弹多个进度窗，而是一条队列逐个跑；单任务失败不中断整批，失败行显示原因、可单独重试。
> **只做单进程队列**（GUI 内触发）。跨进程并入同一队列（CLI / 资源管理器右键）属 P3，与「引擎移出 UI 线程」同批，留在 B 档。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| P0 串行队列 | [compress-job-queue.md](.omo/plans/未开始/compress-job-queue.md) | 🟡中 | 4-6h | `Models/CompressJob.cs` + `Services/CompressJobQueue.cs`（无 UI 的调度器）+ 入队接缝（`Views/MainWindow.axaml.cs:295` 的 `await dialog.RunConfirmedAsync(...)` 改为入队后立即返回）+ 进度窗标题「第 N/M 个任务」。**入队只许用设置窗返回的 `ConfirmOutcome<CompressSettings>` 载荷**（规则 20，禁止弹窗后回读） |
| P1 队列窗 | 同上 | 🟡中 | 8-12h | 常驻 `Views/JobQueueWindow.axaml` + `ViewModels/JobQueueViewModel.cs`：任务列表（输出名 / 源数量 / 状态 / 进度 / 取消）+ 底部「暂停全部 / 取消全部 / 清除已完成」。**新窗口必须一开始就 `x:CompileBindings="True"` + `x:DataType`**（v0.5.7 刚迁完编译绑定，不许添新债），并纳入 `-Task guards` 第 9 条 `compilebindings` |
| P2 失败隔离与弹窗归属 | 同上 | 🟡中 | 6-8h | 失败行显示原因 +「重试」（复制 job 重新入队）；冲突 / 密码弹窗 owner 从 `ProgressWindow.CurrentVisible` 静态单例改为会话级引用 |

> **v0.5.8 验收口径**：多选 3 个源压缩只出现**一条**队列、进度窗标题显示「第 N/M 个任务」；单任务失败时整批继续，队列窗对应行标红并给出原因、可单独重试；压缩入口点「确定」后立即返回，按钮不再等整轮跑完。进度紧就把 P2 裁成「只显示失败原因、不做重试」（约 4h，总 16-22h）。

### 阶段 v0.5.9 —— 小活清仓（约 14-20h，缓冲池，可裁剪）

> 都是 2–4h 的「便宜、感知明确」项，攒成一批做完，避免长期占着队列。骨架计数与 ICO 图标已提到 v0.5.6，本批剩 6 项。
> 2026-10-30 调整：原定 v0.5.8，因压缩任务队列占用 v0.5.8 而后移一版。**在这版之前不要再往池子里加新的 2–4h 碎片**，否则永远清不完。

| 功能 | 设计文档 | 工时 | 说明 |
|---|---|:--:|---|
| 进度条分段着色 | [progress-bar-segments.md](.omo/plans/未开始/progress-bar-segments.md) | 3-4h | 按批处理项状态着色（红=失败 / 绿=成功 / 青=跳过 / 蓝=进行中），动态预算缝隙 |
| 压缩文件名后缀模板 | [filename-suffix-template.md](.omo/plans/未开始/filename-suffix-template.md) | 2-3h | `{date}` / `{datetime}` / `{seq}` 占位符，防同名覆盖 |
| 预览「显示内容」开关 | [preview-show-content-toggle.md](.omo/plans/未开始/preview-show-content-toggle.md) | 2-3h | 关闭后只显示骨架、跳过磁盘递归扫描（大型源目录预览加速） |
| Core 临时目录根可注入 | [core-temp-root-injectable.md](.omo/plans/未开始/core-temp-root-injectable.md) | 2-3h | Core 层 4 处 `%TEMP%\MantisZip` 硬编码在便携模式仍写系统 TEMP；方案为 `TempPaths` 注入，未注入时行为不变 |
| CLI 解压后打开文件夹对齐 | [cli-extract-open-folder.md](.omo/plans/未开始/cli-extract-open-folder.md) | 2-3h | CLI 解压成功后复用 `SmartOpenPathResolver`；多文件批处理保持不开 |
| 提取日志与解压「后悔药」 | [extract-journal-undo.md](.omo/plans/未开始/extract-journal-undo.md) | 3-4h | 解压记录 + 一键回滚 |

> **v0.5.9 验收口径**：清完这 6 项，B 档里不再有「2–4h 就能做掉」的碎片。进度紧就先发 v0.5.8，把这批推给下一轮。
---

## B 档 —— 有价值，排队（不排期）

> **2026-10-29 用户调研后调整**：①**降级进本档**：压缩包内重命名、编辑操作序列（暂存区）、自包含体积优化——前两项是引擎级改造（每天用得少、但复写整包的回归风险大），暂不占发布窗口；体积优化与现有依赖（SkiaSharp / ImageSharp / OpenXml / Sqlite / PdfPig / WebView）矛盾，收益被高估。②**升入 A 档**：文件列表缩略图、文件选择器内嵌、骨架态进度计数器（见上方各阶段表）。③接缝（规则 20）已完成，内嵌选择器不再需要等它。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 压缩任务队列 · P3 跨进程 + P4 持久化 | [compress-job-queue.md](.omo/plans/未开始/compress-job-queue.md) | 🔴高 | 28-40h | 2026-10-30 拆分：P0–P2（单进程串行队列 + 队列窗 + 失败隔离）已进 v0.5.8；本行只剩 P3（GUI 单实例互斥 + 队列管道协议 + 引擎移出 UI 线程，与「真暂停」同批）与 P4（队列持久化 / 拖拽重排 / 压缩后校验） |
| 压缩包内重命名条目（引擎级，零重压缩） | [archive-rename-entry.md](.omo/plans/未开始/archive-rename-entry.md) | 🟡中 | **8-10h** | 2026-10-28 从 S 档移回 A 档，2026-10-29 降 B 档。ZIP 走 copy-mode **只改名字字节、压缩数据原样搬运**，7z 走 `ModifyArchive` 就地删 + 单条追加，tar.gz 明确不支持。它是「暂存区」的地基，将来与暂存区**同批做** |
| 压缩包编辑操作序列（暂存区 + 批量提交，含 P2/P3 增强） | [archive-edit-operation-sequence.md](.omo/plans/未开始/archive-edit-operation-sequence.md) | 🟡中 | P1 **12-16h**；P2/P3 **8-12h** | 删除/重命名先入内存队列，列表立刻反映（虚拟视图），点「应用」或关窗时**一次性**提交：ZIP 一次 `RewriteAsync`、7z 一次 `ModifyArchive` + 追加（现状每编辑一次就整包重写）。P2/P3 = 拖拽移动/移动到…、队列级 undo/redo、提交目标改为「另存为新包」。**硬依赖上面的重命名提供 rewriter rename 通道** |
| 自包含体积优化（第一步） | [selfcontained-size-optimization.md](.omo/plans/未开始/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 目标已改写：原「降至 20–25MB」与现有依赖矛盾，激进修剪会打穿预览。第一步只做测量基线 + 验证保守修剪可行性，目标区间 60–90MB。若确认只发 SelfContained 包，优先级上调 |
| ZIP 多线程压缩方案 D（自适应 + 多线程） | [parallel-compress-plan-d.md](.omo/plans/未开始/parallel-compress-plan-d.md) | 🟡中 | 4-5h | 收口已做完 80% 的压缩加速叙事。**半成品选项比没有选项更糟**（用户会以为没生效），建议有空档就尽早取 |
| ZIP 多线程压缩方案 A（分组并行 + 多级别） | [parallel-compress-plan-a.md](.omo/plans/未开始/parallel-compress-plan-a.md) | 🟡中 | 6-8h | 按级别分组 → 各组独立 ZipWriter 并行 → 合并。**依赖方案 D 完成** |
| 压缩预估（Compression Estimator） | [compression-estimator.md](.omo/plans/未开始/compression-estimator.md) | 🟡中 | 14-18h | 🔶 比率表/格式目录/自适应规则模型已完成，**估算器本体未开始**（`grep Estimator` 命中 0）。含压缩前查表/采样标准/可选精确预估 |
| 压缩中 ETA 增强 | [runtime-eta.md](.omo/plans/未开始/runtime-eta.md) | 🟡中 | 5-7h | 🔶 基础 ETA 已实现（EMA 速度 + 单档案剩余 + 暂停冻结）；本计划补先验速度融合、批处理总剩余、收尾文案 |
| 文本预览语法高亮 | [text-preview-syntax-highlighting.md](.omo/plans/未开始/text-preview-syntax-highlighting.md) | 🟡中 | 4-5h | 📋 计划已重写为 Avalonia 版（原 AvalonEdit 方案废弃），**代码未实施**（csproj 无 AvaloniaEdit/TextMate）。AvaloniaEdit + TextMate + 三级语言识别 |
| 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.omo/plans/未开始/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API 两层提取嵌入缩略图；是「文件列表缩略图」的进一步扩展（列表缩略图已升入 v0.5.6），建议拆开做 |
| 文件过滤与文件列表筛选数据统一 | [unify-file-filter-data.md](.omo/plans/未开始/unify-file-filter-data.md) | 🟡中 | 8-12h | 📋 计划文档是 WPF 期产物，实施前须按规则 11 转 Avalonia。目标：`FileFilterCriteria` 扩展 `TextSearch`/`ExcludeText`/`TextMatchMode`，删除 `SearchFilters` + `ArchiveFilter.ApplyFilters()`（`MainWindowViewModel.Navigation.cs:309` 仍在用旧模型） |
| RAR 压缩（外置 rar.exe） | [rar-compression.md](.omo/plans/未开始/rar-compression.md) | 🟡中 | 8-10h | 通过已安装的 WinRAR 实现 RAR 压缩（含 SevenZipEngine 注册冲突处理）。公开用户会问，但依赖外部程序 |
| 压缩/解压配置预设 | [compress-preset.md](.omo/plans/未开始/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |


---

## 进行中 —— 6 条半途计划（需决定「继续做完」还是「冻结」）

> 这 6 项主体已落地，剩下的尾巴性质不同，**不适合当新计划排队**。建议逐条拍板，别让它们长期挂在中间状态。

| 功能 | 设计文档 | 已做 | 剩余 | 我的建议 |
|---|---|---|:--:|---|
| 压缩加速三开关 UI 收敛 + 魔数检测/自适应规则 | [adaptive-compress-ui-convergence.md](.omo/plans/进行中/adaptive-compress-ui-convergence.md) | Phase 1 ✅ / Phase 2 主体 ✅（Task 9-11；SSOT 统一到 `ZipEntryClassifier`、`AdaptiveRuleMatcher.ResolveLevel` 唯一决策入口、仅存储格式并入规则、MT 提示、CLI 开关） | Task 12 本机基准测试 UI、Task 13 组数定标 | **收尾**（1-2h）：不补就是文档里长期留个「P1 完成但没做完」 |
| 新增压缩格式（BZip2/XZ/Zstd/Brotli + 7z.dll 只读） | [new-format-support.md](.omo/plans/进行中/new-format-support.md) | Core 侧 TAR 裸格式 / GZip 单文件压缩 + 文件关联已就绪 | UI 压缩格式下拉未放开；BZip2→XZ→Zstd→Brotli；7z.dll 只读格式 | **只做前半**：UI 下拉放开 + 7z.dll 只读解锁（读取做稳）；BZip2/XZ/Zstd **创建**无限期延后 |
| 扩展预览格式支持 | [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) | 3.5/6 阶段（工具栏+信息面板、Torrent/PE/PDF/SQLite/ISO、SVG/字体/ICO/Office、MKV/WebM 元数据） | LNK/DBF/EPUB/字幕/STL；Magick.NET/LibVLC 插件化 | **冻结剩余部分**：已有预览深度已超出用户预期，插件化是成本黑洞 |
| 浮动/停靠预览面板 | [floating-preview-panel.md](.omo/plans/进行中/floating-preview-panel.md) | 四位置停靠切换已落地（右侧/下方可持久化） | 浮动独立窗口（Phase 1）、拖拽分离+边缘吸附（Phase 3） | **冻结**：停靠已覆盖 90% 场景 |
| 可插拔预览模块体系 | [preview-modular-providers.md](.omo/plans/进行中/preview-modular-providers.md) | Task 1/4：`ITableDataProvider` 已入 Core 抽象层（`SqliteDataReader` 实现，提交 `1cef18f`） | 接口**零消费者**，UI SQLite 预览仍直连 `Microsoft.Data.Sqlite` | **并入或删除**：见「候选删除」 |
| 文本/ZIP 注释回退编码策略 | [text-fallback-encoding-policy.md](.omo/plans/进行中/text-fallback-encoding-policy.md) | Phase 1（A1+A3）✅ 2026-10-20：`DecodeComment` 严格 UTF-8 → 严格 GB18030 → 宿主 ANSI；`ReadEocd`/`WriteEocd` 改字节透传；CI 侧 `zh-CN` pin 转为故意 en-US | Phase 2（可配置回退代码页）待定 | **冻结 Phase 2**：等真实用户报告再定，当前实现已覆盖主场景 |

---

## 已冻结 —— 暂不投入（附解冻条件）

| 功能 | 设计文档 | 原估工时 | 冻结原因 | 解冻条件 |
|---|---|:--:|---|---|
| 图标主题系统①基础设施（IconView + DrawingImage 迁移） | [icon-theme-infra.md](.omo/plans/未开始/icon-theme-infra.md) | 22-28h | **A 套零视觉差异**——纯契约迁移（`Geometry`→`DrawingImage`），用户看不到任何变化 | 出现「必须支持外部图标包」的真实需求；且先完成 `icon-normalization` |
| 图标主题系统②导入包 + 单图标覆盖 + 设置 UI | [icon-theme-packs.md](.omo/plans/未开始/icon-theme-packs.md) | 26-34h | 依赖①；且「导入第三方图标包」不是当前目标用户群的需求 | ① 完成且有用户提交图标包 |
| 图标主题系统③C/D/E/F/G 内容生产 + IconGen 多套 | [icon-theme-sets.md](.omo/plans/未开始/icon-theme-sets.md) | 24-34h | 要产出 6 套图标资产；单人工具做「图标市场」是审美项目不是产品项目 | ② 完成且确认要做多主题 |
| 快速预览与渐进式加载（三种预览模式） | [preview-quick-modes.md](.omo/plans/未开始/preview-quick-modes.md) | ~25h | 预览体系已经很深，三模式是「复杂大于收益」。其「§3.3 提取策略：内存 vs 硬盘」子项已**单独立项**为 [preview-memory-first.md](.omo/plans/进行中/preview-memory-first.md) 并进 S 档 | 若解冻，**只做「快速模式」最小版**（渐进渲染部分，取数层已由 preview-memory-first 承担） |
| 自研 ZIP 引擎（Per-entry + AES-256） | [custom-zip-engine.md](.omo/plans/未开始/custom-zip-engine.md) | 5-6天 | 零依赖自研 ZIP+AES 是高风险区（项目刚吃过 copy-mode 注释损坏的亏）；per-entry 能力应优先用现有依赖实现 | 现有依赖确实做不到 per-entry 级别控制时 |
| 插件生态（.mp 市场 + 宿主安装器/运行时） | [plugin-ecosystem.md](.omo/plans/未开始/plugin-ecosystem.md) | 44-64h | Publisher 侧已独立实现（47+8 测试通过），**宿主侧为 0**；无用户基数支撑市场 | 有第三方明确要做插件 |
| 跨平台移植（macOS / Linux） | [cross-platform-port.md](.omo/plans/未开始/cross-platform-port.md) | 6-8周 | 📋 **2026-10-24 确认：暂不投入（「想想」）**。这是战略选择不是功能。WPF→Avalonia 迁移已完成，剩余为 macOS/Linux 适配 | 决策上要做跨平台时，**先花 1 天做可移植性体检**（Win32 依赖隔离成接口 + linux CI 只跑 Core 测试），再决定是否投 8 周 |
| Win11 一级右键菜单 | [win11-first-level-menu.md](.omo/plans/未开始/win11-first-level-menu.md) | 1-2周 | 需 MSIX sparse package + 签名，**成本被低估**；现有 Inno 安装的右键菜单已可用 | 打包链路支持 MSIX 签名后 |
| 压缩包管理器（历史/标签/目录树） | [archive-manager.md](.omo/plans/未开始/archive-manager.md) | 2-3天 | SQLite 持久化 + 独立窗口，收益可疑（解压软件不需要「资料库」） | 有用户明确要求历史管理 |

---

## 候选删除 —— 建议从计划库移除（待确认）

> 我建议把这 6 项从计划库移除（5 项来自「未开始」，1 项来自「进行中」）。**确认后我再执行**：把计划文件移到 `.omo/plans/已归档/`，并迁入本文档末尾的「已归档方案」表。在此之前链接保持不变。

| 功能 | 设计文档 | 工时 | 删除理由 |
|---|---|:--:|---|
| MSI 安装包（WiX） | [msi-packaging-wix.md](.omo/plans/未开始/msi-packaging-wix.md) | 2-3h | Inno 安装包已满足需求；MSI 只对域环境批量部署有意义 |
| 压缩包对比（Archive Diff） | [archive-diff.md](.omo/plans/未开始/archive-diff.md) | 3-4h | 无用户需求证据；同类工具也没这个功能 |
| 文件列表自定义列 | [custom-columns.md](.omo/plans/未开始/custom-columns.md) | 4-6h | 元数据列（文档标题/图片尺寸）价值低，且与缩略图方案重叠 |
| 冻结列 | [frozen-column.md](.omo/plans/未开始/frozen-column.md) | 1-2h | DataGrid 冻结列在窄窗口才有意义，投入产出比过低 |
| 可插拔预览模块体系 | [preview-modular-providers.md](.omo/plans/进行中/preview-modular-providers.md) | 3-4h | 接口零消费者（`ITableDataProvider` 只有 `SqliteDataReader` 一个实现且 UI 不调）。**建议并入 `preview-extended-formats` 或直接删** |
| 右键菜单目录结构预览 | [context-menu-tree-preview.md](.omo/plans/未开始/context-menu-tree-preview.md) | 6-8h | COM 菜单里画文件树，🔴高难度 / 低收益；且与跨平台方向冲突 |

---

## 已完成计划内的遗留子项

> 这些不是新计划，是已完成计划文末的「未实现项」。单独列出来，避免它们跟着旧 P4 一起被误当成待开发功能。

| 功能 | 来源计划 | 工时 | 说明 |
|---|---|:--:|---|
| Office 图片预览 | [office-content-preview-avalonia.md](.omo/plans/已完成/office-content-preview-avalonia.md) | 2-10h | 所属计划已于 v0.5.0 完成（DOCX/XLSX/PPTX 内容预览），仅其文末图片子项未做（全仓无图片提取代码）。三方案渐进：**A = PPTX 图片（~50 行，复用 Canvas，建议只做这个）**；B = +DOCX 行内图片（~150 行）；C = 浮动定位（不推荐） |
| 解压多压缩包按来源目录分组 | [result-preview-panel.md](.omo/plans/已完成/result-preview-panel.md) | 2-4h | 所属计划已完成（v0.4.5）。当前多压缩包条目合并平铺 → 改为按来源目录分组 + 压缩包壳节点 |
| 结果预览截断占位符点击展开 | [result-preview-panel.md](.omo/plans/已完成/result-preview-panel.md) | 1-2h | 所属计划已完成（v0.4.5）。当前截断为静态「…」文本 → 点击就地展开完整子节点 |

---

## 依赖链与联动

```
intent-execution-seam（S，已完成 2026-10-26）── 软前置 ──▶ 所有「新增 UI 入口」类计划
                                    （embedded-file-picker 已于 v0.5.5 排期，前提已满足）

archive-rename-entry（B）── renameMap / RenameEntriesAsync ──▶ archive-edit-operation-sequence（B，暂存区）
                                    （两者同批做：先 rewriter rename 通道，再暂存区复用）

icon-normalization（S，已完成 2026-10-26）──── 前置 ──▶ icon-theme-infra（冻结）──▶ packs ──▶ sets

parallel-compress-plan-d ── 前置 ──▶ parallel-compress-plan-a

compression-estimator ── 可选前置 ──▶ runtime-eta（无先验时行为与现状一致，零返工）

preview-memory-first（S，已完成 2026-10-28）── 取数层复用 ──▶ file-list-thumbnails（v0.5.6，缩略图复用同一 head 缓存）

parallel-compress-plan-d ── 布局前提 ──▶ progress-bar-segments（分段进度条在进度窗口重构后的布局上）

NuGet 升级（SharpCompress ≥0.49.0）── 联动 ──▶ selfcontained-size-optimization（B 档，TrimmerRootAssembly 需随升级更新）
```

补充说明：

- `cross-platform-port` 已冻结。**唯一保留的纪律**：新增代码不要加深 Win32 耦合（新增平台相关调用时，优先走已有抽象或加一层接口）。
- `rar-compression` 为软依赖，进度窗口增强会自动改善其体验。`archive-rename-entry` 已不再依赖进度窗口增强（2026-10-27 重写后只需一次 `RunWithProgress`）。
- `archive-rename-entry` 与 `archive-edit-operation-sequence` 现同在 B 档：谁先被选中，就顺带把另一方一起排（同批引擎改造，分两次做等于返工两遍）。

---

## 已归档方案

| 功能 | 设计文档 | 难度 | 工时 | 说明 | 废弃原因 |
|---|---|:--:|:--:|---|---|
| VirtualFileDataObject | [virtual-file-data-object.md](.omo/plans/已归档/virtual-file-data-object.md) | 🔴高 | 6-8h | COM 原生 IDataObject 替代 WPF OLE 桥 | 迁移 Avalonia 后不再依赖 WPF OLE 桥，VFDO 无存在必要 |
| 拖拽提取目标检测 | [drag-drop-marker-target.md](.omo/plans/已归档/drag-drop-marker-target.md) | 🟡中 | 1-3h | Marker 文件探测拖放目标目录 | 被 [drag-drop-direct-extract.md](.omo/plans/已完成/drag-drop-direct-extract.md) 取代 |
| 预览格式识别（旧方案） | [preview-format-detection.old.md](.omo/plans/已归档/preview-format-detection.old.md) | 🟡中 | — | 预览格式识别旧设计 | 已拆分为 [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) + [preview-magic-detection.md](.omo/plans/已完成/preview-magic-detection.md) |
| Office 内容预览（WPF 版） | [office-content-preview.md](.omo/plans/已归档/office-content-preview.md) | 🟡中 | — | WPF 版 Mammoth→HTML→WebView2 方案 | 被 [office-content-preview-avalonia.md](.omo/plans/已完成/office-content-preview-avalonia.md) 取代 |
| 资源管理器式路径切换弹窗 | [explorer-path-switcher.md](.omo/plans/已归档/explorer-path-switcher.md) | 🟡中 | — | WPF 时代 Ctrl+G 路径速选弹窗 | 被 QuickPathControl 系列演进取代（CustomFilePickerDialog） |
| QuickPathControl 原始组件计划（WPF 版） | [quick-path-control.md](.omo/plans/已归档/quick-path-control.md) | 🟡中 | — | WPF 版路径速选控件原始设计 | 同上，被后续演进取代 |

---

*此文档将随开发进度持续更新。*
