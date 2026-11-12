# MantisZip — 开发计划

> 未来待开发功能规划。已实现的功能见 [docs/PROGRESS.md](PROGRESS.md)，技术架构见 [docs/ARCHITECTURE.md](ARCHITECTURE.md)，每项计划的完整设计见 `.omo/plans/`。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-11-12  
**当前版本**: 0.5.7


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

### 阶段 v0.5.5 —— 文件选择与导航（约 8-16h，已完成）

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| ✅ 面包屑地址栏（PathBreadcrumb） | [path-breadcrumb.md](.omo/plans/已完成/path-breadcrumb.md) | 🟡中 | 6-8h | 三处地址栏（主窗口 / QuickPathPicker / CustomFilePickerDialog）改为资源管理器式面包屑：段点击直达 + Ctrl+L 进编辑态 + 段数阈值折叠 + 虚拟根段。2026-10-21 已按代码核准（`TryConfirmFile` 无模式判断须保持现状；`Path` 定为 StyledProperty + OneWay；QuickPathPicker 4 宿主纳入回归）。接缝改造后它的头号风险（逐字符回写 Path）已降级为「按钮可用性 / 实时提示」。**2026-10-29 已完成并归档**（三处接入 + 6 个无头用例；真机冒烟项见计划末「未做」） |
| ✅ 文件选择器去弹窗化（内嵌控件） | [embedded-file-picker.md](.omo/plans/已完成/embedded-file-picker.md) | 🟡中 | 8-16h | 把文件/目录选择器内嵌进压缩/解压设置窗：抽出 `Controls/CustomFilePickerControl` 纯控件，压缩窗 6 页签 / 解压窗 4 页签内嵌（简单/高级模式、已选清单、弹性高度），「解压选中项到 / 拖拽兜底」改用解压设置窗 + 预置选中集；`CustomFilePickerDialog` 静态入口与「系统浏览」豁免不动。**2026-10-30 已完成并归档** |

> **v0.5.5 验收口径**：三处地址栏都能像资源管理器一样点段跳转、Ctrl+L 编辑；压缩/解压设置窗不再弹模态选择器，选完当场回填；窗口缩放与深浅主题下都不丢焦点、不串路径。

### 阶段 v0.5.6 —— 文件列表体验（约 17-25h，✅ 已完成）

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| ✅ 文件列表缩略图查看方式 | [file-list-thumbnails.md](.omo/plans/已完成/file-list-thumbnails.md) | 🟡中 | 4-6h | 三种布局循环切换（详情 / 平铺 / 内容行）+ 内容卡三档 + 持久化。**「现代感」最大来源**，复用 `PreviewService` 的 head 缓存（v0.5.4 已做取数层内存优先） |
| ✅ 骨架态实时进度计数器 | [skeleton-progress-counter.md](.omo/plans/已完成/skeleton-progress-counter.md) | 🟢低 | 3-4h | 骨架期每 0.5s 更新目录节点的文件数/大小（现在是静态「加载中」）。从「小活清仓」提到 A 档——它直接决定打开大包时的第一印象 |
| ✅ ICO 文件自身图标显示 | [ico-file-icon-extract.md](.omo/plans/已完成/ico-file-icon-extract.md) | 🟢低 | 2-3h | 列表里 ico 文件显示自身嵌入图标（现在显示通用图标）。与缩略图同属「列表里看得见」的一批 |
| ✅ 主窗口列表复选框多选 | [file-list-checkbox-selection.md](.omo/plans/已完成/file-list-checkbox-selection.md) | 🟡中 | 8-12h | 列表加复选框列（默认关闭，开关持久化到 `AppSettings.FileListCheckboxMode`）：勾选跨滚动/切目录保持、全选可见/清空、状态栏「已选 N 项」；顺带补上**批量删除**（`DeleteFiles` 现在仍只看 `SelectedEntry` 单条）。补 #37「与主窗口文件列表统一」这半 |
| ✅ 缩略图卡片外观设置 | [thumbnail-card-settings.md](.omo/plans/已完成/thumbnail-card-settings.md) | 🟢低 | 2-3h | 「设置 → 外观 → 缩略图」子页签：卡片外边框开关（默认关）、透明图是否垫纯色底 + 取色器、三档尺寸 96/128/160。全部走资源注入即时生效，缓存按尺寸分开 |
| ✅ 缩略图清晰度修正 | [file-list-thumbnails.md](.omo/plans/已完成/file-list-thumbnails.md) | 🟢低 | 1-2h | 卡片里的 `.ico` 取边长最接近 160 的帧（原按面积差会挑到 16px）、`.svg` 按卡片尺寸重绘（原按 viewBox 24px 渲染再放大）、无缩略图时改用系统大图标 96px |
| ✅ 复选框模式一致性 | [checkbox-mode-consistency.md](.omo/plans/已完成/checkbox-mode-consistency.md) | 🟡中 | 4-6h | 切换复选框模式时做选区 ⇄ 勾选的双向转换（打开=选区并入勾选、关闭=当前目录勾选退回选区）；压缩/解压内嵌选择器改名叫「复选框模式」并统一成文件列表那套手势（点行只高亮、点复选框才勾、Shift/Ctrl 连勾、右键全选/反选/清空） |
| ✅ 卡片视图复选框 + 快捷多选 | [checkbox-multiselect-shortcuts.md](.omo/plans/已完成/checkbox-multiselect-shortcuts.md) | 🟡中 | 4-6h | 平铺 / 内容行视图也显示复选框；**Shift 连选 + Ctrl 增减**（表格视图按复选框列同样生效）；「视图 → 列表布局」子菜单直达详情 / 平铺 / 内容，并加「复选框多选」开关 |

> **v0.5.6 验收口径（✅ 已达成）**：列表能在三种布局间循环并记住选择；打开 1000+ 条目的包时骨架区能看到数字在涨；ico 文件在列表里显示自己的图标而不是通用图标；复选框模式勾选 3 项后删除只弹一次确认、删完勾选态清空，且默认关闭时行为与今天完全一致；缩略图卡片的外边框 / 垫底色 / 尺寸三项在设置里改完即时生效；卡片视图也能勾选，Shift 连选与「视图 → 列表布局」子菜单可用；切换复选框模式时选区与勾选能互相转换，压缩/解压窗里的复选框选择手势与主列表一致。

### 阶段 v0.5.7 —— 数据安全收尾与压缩文件名模板（约 20-26h，✅ 已完成）

> **2026-11-11 调整**：v0.5.7 里的「Office 预览切片 3」后移，**并进 v0.5.11 新功能与债务**（与卡片元数据、AXAML 编译绑定同属预览/质量提升类，不挤占拖拽收尾的窗口）；空出的位置换成**压缩文件名模板**（8-10h）。理由：文件名模板直接消解「同日期同名的压缩包互相覆盖」这一日常痛点，且风险面小（Core 纯函数 + 命名收敛在 `CompressPathPlanner`，模板留空时行为逐字节不变）。

> **2026-11-04 重排**：原定「Office 预览切片 3 + 卡片元数据 + AXAML 编译绑定」整体后移。依据是判据第 3 条（削减「点了没反应」类失信 bug 优先于增加功能面）：KI-012 是**数据丢失级**——「解压到此处 / 同名目录 / 智能解压」三条路径完全不读冲突设置、恒静默覆盖；它的阻塞条件 `progress-window-conflict-policy` 已于 2026-11-01 定稿、无设计风险，没有理由继续挂在 B 档。卡片元数据（新计划）与编译绑定（只防未来错误）都不该抢在这个窗口前面。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| ✅ 进度窗口「文件冲突自动处理」控件（TeraCopy 式） | [progress-window-conflict-policy.md](.omo/plans/已完成/progress-window-conflict-policy.md) | 🟡中 | 12-16h | 进度窗加「冲突时:」下拉（覆盖 / 跳过 / 重命名 / 询问），运行中切换立即生效；Core 只加一个 `ArchiveOptions.ConflictActionProvider` 动态源；实例级生效、不回写设置，保留 `ConflictDialog` 供「询问」使用；解压与压缩两侧都要接 |
| ✅ KI-012：三条原地解压路径读冲突设置 | 同上（Phase 3 顺带修复，2026-11-04 已关闭） | 🟡中 | 含在上行 | `ViewModels/MainWindowViewModel.Extract.cs` 的「解压到此处 / 同名目录 / 智能解压」三处 `ExtractService.ExtractAsync(...)` 不传 `ArchiveOptions` ⇒ 恒静默覆盖；修完后三条路径与右键「解压选中项到」共用同一个 options 构造 |
| ✅ 压缩文件名模板（三入口默认值 + 命名预设） | [filename-template.md](.omo/plans/已完成/filename-template.md) | 🟡中 | 8-10h | 用占位符模板生成压缩包**主名**（`{name}` `{name:auto}` `{ext}` `{parent}` `{date}` `{datetime}` `{seq}` `{seq:0000}`），格式扩展名由所选格式自动补；模板非空即接管命名（忽略「保留源文件扩展名」，要保留请写 `{ext}`）；命名收敛在 Core 的 `CompressPathPlanner`，预览树 = 实际输出；压缩对话框 / `--compress-quick` / `--compress-separate` 三档默认值 + 可存命名预设；未知占位符 / 非法字符 / 渲染为空 / 多源重名在确认时拦截并给出原因 |

> **v0.5.7 验收口径（✅ 已达成）**：解压途中能从进度窗把「冲突时」切到覆盖 / 跳过 / 重命名且立刻生效；「解压到此处 / 同名目录 / 智能解压」遇到同名文件不再静默覆盖（默认仍问一次）；KI-012 关闭；文件名模板留空时命名与今天逐字节一致，填 `{name}_{date}` 后三个源分别产出带日期的主名、预览树与对话框只读框显示的就是最终文件名、未知占位符 `{foo}` 在确认时被拦下并给出原因。

### 阶段 v0.5.8 —— 唯一根目录处理（压缩去目录 / 套根 + 解压剥根，约 10-16h）

> **2026-11-12 新增**：GitHub issue #35 的**线 1**（线 2 的选择器手势吐槽已由 v0.5.6 的 `checkbox-mode-consistency` 解决）。本阶段只做这一项；原 v0.5.8「拖拽收尾与碎片清仓」顺延为 v0.5.9，其后两段依次 +1。三项新设置的**默认值全部是关闭/原样** ⇒ 不选它时结果与今天逐字节一致。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 唯一根目录处理（压缩去目录 / 套根 + 解压剥根） | [compress-extract-root-mode.md](.omo/plans/进行中/compress-extract-root-mode.md) | 🟡中 | 10-16h | 压缩设置窗新增三态「包内根目录」：`原样`（默认）/ `去目录本身`（仅 Sources 恰为一个目录时可用，否则置灰）/ `套唯一根目录`（Separate 模式置灰，根名输入框默认 = 压缩包文件名去扩展名）。解压设置窗新增「去掉压缩包顶层目录」开关，无唯一根时置灰，剥根策略挂在 `ExtractPathResolver` 上（预览与实解共用同一份，叠序为「先剥唯一根、再裁浏览前缀」）。默认值与其它设置一致：窗内载入设置窗的默认值、确认不回写。首版只做 GUI，CLI 参数列入范围外 |

> **v0.5.8 验收口径**：默认三态为「原样」、解压开关关闭时，所有现有流程（含智能解压）结果与改动前逐字节一致；选中 `7zip` 目录压缩后包内没有 `7zip/` 这一层、子项结构与目录内一致，Sources 有多个目录或混合源时该选项置灰；选多项压缩后包内所有项都在 `<根名>/` 下，根名默认等于包文件名且可改，Separate 模式下该态置灰；唯一根包解压时预览树与实解都少一层、散装包该开关置灰；根名改过 / 剥根开关改过的选择不回写设置，重启后加载的是设置窗口里的默认值。

### 阶段 v0.5.9 —— 拖拽收尾与碎片清仓（约 17-24h）

> **2026-11-04 重排**：`drag-drop-direct-extract` 自 2026-10-30 起挂在「7/10」，是唯一带「拖了没反应」风险的半成品（`DragPreviewPopup.cs` 至今不存在）。碎片清仓同时收到这里，避免小活池越攒越久。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 拖拽直接解压 · 收尾（预览弹窗 + 双投放区 + 落点冲突预览） | [drag-drop-direct-extract.md](.omo/plans/进行中/drag-drop-direct-extract.md) | 🔴高 | 10-14h | 文件树预览弹窗（独立 Win32 窗、跟随鼠标、`PointerPressed` 预取式渲染、双阈值降级）+ 双投放区（保留完整路径 / 扁平解压，方向可设）+ 落点冲突预览（覆盖层最多 8 行、目标已存在即标红、每轮 IO ≤40 次 + 慢盘 500ms 降级）+ Task 8 手工验收。**硬约束**：拖拽期间 UI 线程被 OLE `DoDragDrop` 阻塞，渲染必须提前完成 |
| 进度条分段着色 | [progress-bar-segments.md](.omo/plans/未开始/progress-bar-segments.md) | 🟡中 | 3-4h | 按批处理项状态着色（红=失败 / 绿=成功 / 青=跳过 / 蓝=进行中）；原依赖 `parallel-compress-plan-d` 的布局前提，D 仍在 B 档 ⇒ 先做「按当前布局着色」的兼容版，D 落地后补分段 |
| CLI 解压后打开文件夹对齐 | [cli-extract-open-folder.md](.omo/plans/未开始/cli-extract-open-folder.md) | 🟢低 | 2-3h | CLI 解压成功后复用 `SmartOpenPathResolver`；多文件批处理保持不开 |
| Core 临时目录根可注入 | [core-temp-root-injectable.md](.omo/plans/未开始/core-temp-root-injectable.md) | 🟢低 | 2-3h | Core 层 4 处 `%TEMP%\MantisZip` 硬编码在便携模式仍写系统 TEMP；方案为 `TempPaths` 注入，未注入时行为不变 |

> **v0.5.9 验收口径**：拖到 Explorer 窗口 / 桌面时能看到文件树预览与同名冲突提示，双投放区可分别落到「保留完整路径 / 扁平」两个位置；进度条按状态着色；CLI 解压后能打开目标文件夹；便携模式的临时文件不再写到系统 TEMP。

### 阶段 v0.5.10 —— 压缩任务队列（P0–P2，约 18-26h，维持原计划）

> TeraCopy 式串行队列：多选/批量压缩不再并发弹多个进度窗，而是一条队列逐个跑；单任务失败不中断整批，失败行显示原因、可单独重试。
> **只做单进程队列**（GUI 内触发）。跨进程并入同一队列（CLI / 资源管理器右键）属 P3，与「引擎移出 UI 线程」同批，留在 B 档。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| P0 串行队列 | [compress-job-queue.md](.omo/plans/未开始/compress-job-queue.md) | 🟡中 | 4-6h | `Models/CompressJob.cs` + `Services/CompressJobQueue.cs`（无 UI 的调度器）+ 入队接缝（`Views/MainWindow.axaml.cs:295` 的 `await dialog.RunConfirmedAsync(...)` 改为入队后立即返回）+ 进度窗标题「第 N/M 个任务」。**入队只许用设置窗返回的 `ConfirmOutcome<CompressSettings>` 载荷**（规则 20，禁止弹窗后回读） |
| P1 队列窗 | 同上 | 🟡中 | 8-12h | 常驻 `Views/JobQueueWindow.axaml` + `ViewModels/JobQueueViewModel.cs`：任务列表（输出名 / 源数量 / 状态 / 进度 / 取消）+ 底部「暂停全部 / 取消全部 / 清除已完成」。**新窗口必须一开始就 `x:CompileBindings="True"` + `x:DataType`**，并纳入 `-Task guards` 第 9 条 `compilebindings` |
| P2 失败隔离与弹窗归属 | 同上 | 🟡中 | 6-8h | 失败行显示原因 +「重试」（复制 job 重新入队）；冲突 / 密码弹窗 owner 从 `ProgressWindow.CurrentVisible` 静态单例改为会话级引用 |

> **v0.5.10 验收口径**：多选 3 个源压缩只出现**一条**队列、进度窗标题显示「第 N/M 个任务」；单任务失败时整批继续，队列窗对应行标红并给出原因、可单独重试；压缩入口点「确定」后立即返回，按钮不再等整轮跑完。进度紧就把 P2 裁成「只显示失败原因、不做重试」（约 4h，总 16-22h）。

### 阶段 v0.5.11 —— 新功能与债务（约 22-30h，滚动，未排具体日期）

> **2026-11-04 移入**：卡片元数据与 AXAML 编译绑定原先排在 v0.5.7，随「风险优先」重排整体后移。v0.5.10 的队列窗会强制开编译绑定，存量迁移可以慢慢来，不阻塞发布。**2026-11-11 追加**：Office 预览切片 3 也从 v0.5.7 后移进本阶段。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 预览取数层内存优先（切片 3：Office） | [preview-memory-first.md](.omo/plans/已完成/preview-memory-first.md) | 🟡中 | 5-7h | Office 各 `*.Open(Stream)`（`SpreadsheetDocument` / `WordprocessingDocument` / `PresentationDocument`），字体明确豁免。三者本来就在解析完整文档，省下的只是落盘。2026-11-11 从 v0.5.7 后移至此 |
| 卡片元数据（全卡头部窗口 + 小文件整条兜底） | [card-metadata-head-window.md](.omo/plans/未开始/card-metadata-head-window.md) | 🟡中 | 12-16h | 平铺 / 内容布局里**所有**候选卡显示 2–3 行格式元数据（音频 / 视频 / 字体 / 可执行 / PDF / Office / BT 种子 / SQLite / ISO）；取数改成「读条目头部 256 KB → 解析」，小文件（≤2 MB）整条兜底、视频尾段（≤10 MB）补 moov；6 组开关在「设置 → 外观 → 缩略图」第 4 张卡片 |
| AXAML 编译期绑定（渐进式开启 `x:CompileBindings`） | [axaml-compiled-bindings.md](.omo/plans/未开始/axaml-compiled-bindings.md) | 🟡中 | 10-14h | 50 个 `.axaml` 只有 1 个开编译绑定、13 个有 `x:DataType`；渐进三批推进 + `-Task guards` 第 9 条 `compilebindings`（只约束新增与已迁移文件） |
| 缩略图/图标缓存与列表惰性分批（v0.5.6 缩略图功能性能债） | [thumbnail-list-cache-virtualization.md](.omo/plans/未开始/thumbnail-list-cache-virtualization.md) | 🟢低 | Phase 1 **2-3天** | 2026-11-06 立项（`file-list-thumbnails` 实施记录里三条已知改进口子）：①缩略图缓存跨包/跨目录共享（构造参数已具备，切包丢缓存）②ICO 列表图标 LRU 缓存（筛选每敲一字重读最多 20 个 ico）③平铺/内容布局分批惰性铺条（每批 200、滚动到底追加），作为无虚拟化的缓解；Phase 2 真虚拟化面板（自写 VirtualizingPanel）留档，待 Phase 1 QA 后由用户单独决策，未明确决策不得开工。**暂缓**：被下条 UX 修复先做 |

> **v0.5.11 尾部缓冲（进度紧就退回 B 档）**：剩余 2 项——预览「显示内容」开关（2-3h，`preview-show-content-toggle.md`）、提取日志与解压「后悔药」（3-4h，`extract-journal-undo.md`）。原在此列的「压缩文件名模板」已于 2026-11-11 升入 v0.5.7。

---

## B 档 —— 有价值，排队（不排期）

> **2026-11-04 调整**：③冲突策略（含 KI-012 修复）因是数据丢失级失信 bug，升入 **v0.5.7**；④卡片元数据与 AXAML 编译绑定从 v0.5.7 后移到 **v0.5.11**。
> 
> **2026-10-29 用户调研后调整**：①**降级进本档**：压缩包内重命名、编辑操作序列（暂存区）、自包含体积优化——前两项是引擎级改造（每天用得少、但复写整包的回归风险大），暂不占发布窗口；体积优化与现有依赖（SkiaSharp / ImageSharp / OpenXml / Sqlite / PdfPig / WebView）矛盾，收益被高估。②**升入 A 档**：文件列表缩略图、文件选择器内嵌、骨架态进度计数器（见上方各阶段表）。③接缝（规则 20）已完成，内嵌选择器不再需要等它。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 压缩任务队列 · P3 跨进程 + P4 持久化 | [compress-job-queue.md](.omo/plans/未开始/compress-job-queue.md) | 🔴高 | 28-40h | 2026-10-30 拆分：P0–P2（单进程串行队列 + 队列窗 + 失败隔离）已排入 v0.5.10；本行只剩 P3（GUI 单实例互斥 + 队列管道协议 + 引擎移出 UI 线程，与「真暂停」同批）与 P4（队列持久化 / 拖拽重排 / 压缩后校验） |
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
| 元数据显示位置可选（侧边面板 / 正文区） | [metadata-display-location.md](.omo/plans/未开始/metadata-display-location.md) | 🟢低 | 6-8h | 📋 2026-10-31 用户讨论后定案、待启动。exe 预览正文区只有条目名标题、大片空白，信息全在右侧面板；按类型加「显示位置」，选正文区时「程序信息」搬到正文区竖排渲染、面板只留「文件信息」。**只对没有其它正文内容的格式开放**（当前仅 `pe`），默认侧边面板、老配置不动 |
| 解压时同时解开内层压缩包（递归嵌套解压） | [nested-archive-extract.md](.omo/plans/未开始/nested-archive-extract.md) | 🟡中 | 16-22h | 📋 2026-11-05 用户逐项定稿、待排期。内层包解到同名子目录（原包保留）、递归可配深度默认 3（1-10）、只认 `.zip/.7z/.rar/.tar/.gz/.tgz/.tar.gz` 白名单（不做魔数，避开 Office/EPUB）、密码走外层→密码库→弹一次窗、冲突沿用本次策略（`ask` 也弹）、失败跳过并汇总、**5 处入口全覆盖**（对话框 / 三条原地解压 / 拖拽 / CLI）、深度 + 500 包 / 100GB 双硬上限。新增独立 `NestedExtractRunner` + Core 策略/扫描器，不改引擎契约
| 唯一根目录处理（压缩去目录 / 套根 + 解压剥根） | [compress-extract-root-mode.md](.omo/plans/未开始/compress-extract-root-mode.md) | 🟡中 | 10-16h | 📋 2026-11-10 用户逐项定稿、待排期（GitHub issue #35）。压缩设置窗三态选项（原样 / 去目录本身[仅单目录源，否则置灰] / 套唯一根目录[Separate 模式置灰]，根名输入框默认包文件名）+ 解压设置窗「去掉压缩包顶层目录」开关（无唯一根置灰，判定复用智能解压，策略挂 `ExtractPathResolver` 保证预览与实解一致）。默认值与其它压缩/解压设置一样：窗内载入默认不回写、设置窗口改默认；首版只做 GUI（CLI 后补，见计划「范围外」）


---

## 进行中 —— 6 条计划（2 条已排入 v0.5.x，4 条待排 / 已在做）

> 2026-11-04 清账：7z solid 批量解压、选中条目批量解压、文件名代码页三条的代码与实测均已通过，已归档进「已完成」（HDD 真机复测仍列为 v0.5.6 发布前手工项）；预览扩展剩余部分、浮动面板、注释编码 Phase 2 三条转入「已冻结」；可插拔预览模块转「已归档」。

| 功能 | 设计文档 | 已做 | 剩余 | 我的建议 |
|---|---|---|---|---|
| 唯一根目录处理（压缩去目录 / 套根 + 解压剥根） | [compress-extract-root-mode.md](.omo/plans/进行中/compress-extract-root-mode.md) | — | 未开始（T1–T7） | **已排入 v0.5.8**（10-16h）：issue #35 线 1，压缩三态 + 解压剥根，默认全关、行为逐字节不变 |
| 拖拽直接解压 + 文件树预览弹窗 | [drag-drop-direct-extract.md](.omo/plans/进行中/drag-drop-direct-extract.md) | 主体 Task 1–7 ✅（自实现 OLE 拖拽 `CustomOleDragDrop` + `OverlayController` 三色覆盖层 + 落点检测 + 拖拽后统一走 `ExtractFlow`） | **文件树预览弹窗整体未实施**（`DragPreviewPopup.cs` 不存在）+ 双投放区 + 落点冲突预览 + Task 8 手工验收 | **已排入 v0.5.9**（10-14h）：唯一带「拖了没反应」风险的半成品，优先收尾 |
| 压缩加速三开关 UI 收敛 + 魔数检测/自适应规则 | [adaptive-compress-ui-convergence.md](.omo/plans/进行中/adaptive-compress-ui-convergence.md) | Phase 1 ✅ / Phase 2 主体 ✅（Task 9-11；SSOT 统一到 `ZipEntryClassifier`、`AdaptiveRuleMatcher.ResolveLevel` 唯一决策入口、仅存储格式并入规则、MT 提示、CLI 开关） | Task 12 本机基准测试 UI、Task 13 组数定标 | **收尾**（1-2h）：不补就是文档里长期留个「P1 完成但没做完」，可搭 v0.5.9 碎片清仓 |
| 新增压缩格式（BZip2/XZ/Zstd/Brotli + 7z.dll 只读） | [new-format-support.md](.omo/plans/进行中/new-format-support.md) | Core 侧 TAR 裸格式 / GZip 单文件压缩 + 文件关联已就绪 | UI 压缩格式下拉未放开；BZip2→XZ→Zstd→Brotli；7z.dll 只读格式 | **只做前半**：UI 下拉放开 + 7z.dll 只读解锁（读取做稳）；BZip2/XZ/Zstd **创建**无限期延后。待排（估 6-8h），不急 |
| 文件选择器「新建文件夹」（所有模式） | [picker-new-folder.md](.omo/plans/进行中/picker-new-folder.md) | — | 已完成（T1–T6） | **待发版**（2026-11-04，`beta`）：地址行按钮 + `Ctrl+Shift+N` + 校验留窗的 `InputDialog`；PickFolder/ExtractFolder/SaveFile 建完进新目录，PickItems/OpenFile 只刷新。`ui-tests` 6 例全绿；归档待 `RELEASE_NOTES` 范围确认 |
| 文件选择器地址区拆两行 | [picker-address-two-rows.md](.omo/plans/进行中/picker-address-two-rows.md) | — | 已完成（T1–T4） | **待发版**（2026-11-05，`beta`）：按钮行（左导航 / 右新建+收藏）+ 面包屑整宽行；面包屑 392 → ≈610px，垂直多占 36–46px。两个 headless 用例钉住布局关系；归档待 `RELEASE_NOTES` 范围确认 |

---

## 已冻结 —— 暂不投入（附解冻条件）

| 功能 | 设计文档 | 原估工时 | 冻结原因 | 解冻条件 |
|---|---|:--:|---|---|
| 图标主题系统①基础设施（IconView + DrawingImage 迁移） | [icon-theme-infra.md](.omo/plans/未开始/icon-theme-infra.md) | 22-28h | **A 套零视觉差异**——纯契约迁移（`Geometry`→`DrawingImage`），用户看不到任何变化 | 出现「必须支持外部图标包」的真实需求；且先完成 `icon-normalization` |
| 图标主题系统②导入包 + 单图标覆盖 + 设置 UI | [icon-theme-packs.md](.omo/plans/未开始/icon-theme-packs.md) | 26-34h | 依赖①；且「导入第三方图标包」不是当前目标用户群的需求 | ① 完成且有用户提交图标包 |
| 图标主题系统③C/D/E/F/G 内容生产 + IconGen 多套 | [icon-theme-sets.md](.omo/plans/未开始/icon-theme-sets.md) | 24-34h | 要产出 6 套图标资产；单人工具做「图标市场」是审美项目不是产品项目 | ② 完成且确认要做多主题 |
| 快速预览与渐进式加载（三种预览模式） | [preview-quick-modes.md](.omo/plans/未开始/preview-quick-modes.md) | ~25h | 预览体系已经很深，三模式是「复杂大于收益」。其「§3.3 提取策略：内存 vs 硬盘」子项已**单独立项**为 [preview-memory-first.md](.omo/plans/已完成/preview-memory-first.md) 并进 S 档 | 若解冻，**只做「快速模式」最小版**（渐进渲染部分，取数层已由 preview-memory-first 承担） |
| 自研 ZIP 引擎（Per-entry + AES-256） | [custom-zip-engine.md](.omo/plans/未开始/custom-zip-engine.md) | 5-6天 | 零依赖自研 ZIP+AES 是高风险区（项目刚吃过 copy-mode 注释损坏的亏）；per-entry 能力应优先用现有依赖实现 | 现有依赖确实做不到 per-entry 级别控制时 |
| 插件生态（.mp 市场 + 宿主安装器/运行时） | [plugin-ecosystem.md](.omo/plans/未开始/plugin-ecosystem.md) | 44-64h | Publisher 侧已独立实现（47+8 测试通过），**宿主侧为 0**；无用户基数支撑市场 | 有第三方明确要做插件 |
| 跨平台移植（macOS / Linux） | [cross-platform-port.md](.omo/plans/未开始/cross-platform-port.md) | 6-8周 | 📋 **2026-10-24 确认：暂不投入（「想想」）**。这是战略选择不是功能。WPF→Avalonia 迁移已完成，剩余为 macOS/Linux 适配 | 决策上要做跨平台时，**先花 1 天做可移植性体检**（Win32 依赖隔离成接口 + linux CI 只跑 Core 测试），再决定是否投 8 周 |
| Win11 一级右键菜单 | [win11-first-level-menu.md](.omo/plans/未开始/win11-first-level-menu.md) | 1-2周 | 需 MSIX sparse package + 签名，**成本被低估**；现有 Inno 安装的右键菜单已可用 | 打包链路支持 MSIX 签名后 |
| 压缩包管理器（历史/标签/目录树） | [archive-manager.md](.omo/plans/未开始/archive-manager.md) | 2-3天 | SQLite 持久化 + 独立窗口，收益可疑（解压软件不需要「资料库」） | 有用户明确要求历史管理 |

| 扩展预览格式支持（剩余：LNK/DBF/EPUB/字幕/STL + 插件化） | [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) | 3.5/6 阶段已落地 | 已有预览深度已超出用户预期；Magick.NET / LibVLC 插件化是成本黑洞 | 用户点名要 LNK/EPUB 等具体格式时，只做该格式的最小支持 |
| 浮动/停靠预览面板（浮动独立窗 + 边缘吸附） | [floating-preview-panel.md](.omo/plans/进行中/floating-preview-panel.md) | 四位置停靠已落地 | 停靠已覆盖 90% 场景，浮动窗是「复杂大于收益」 | 有多屏用户明确要「预览浮到主窗之外」时 |
| 文本/ZIP 注释回退编码策略 Phase 2（可配置回退代码页） | [text-fallback-encoding-policy.md](.omo/plans/进行中/text-fallback-encoding-policy.md) | Phase 1 已完成 | 现实现覆盖主场景（严格 UTF-8 → GB18030 → 宿主 ANSI） | 有真实用户报告注释乱码时再定 |


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

progress-window-conflict-policy（**v0.5.7**）── Phase 3 接线 ──▶ 修复 KI-012（三条原地解压路径恒静默覆盖）；rar-compression（B 档）的冲突体验同批受益

parallel-compress-plan-d ── 布局前提 ──▶ progress-bar-segments（**已排 v0.5.9**；D 仍在 B 档 ⇒ 先做「按当前布局着色」的兼容版，D 落地后补分段）
filename-template（**v0.5.7 第二项**）── 命名收敛在 CompressPathPlanner ──▶ 预览树 / 三入口 / CLI 共用同一渲染结果（将来 archive-rename-entry 的「另存为」命名可复用）

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
| 拖拽提取目标检测 | [drag-drop-marker-target.md](.omo/plans/已归档/drag-drop-marker-target.md) | 🟡中 | 1-3h | Marker 文件探测拖放目标目录 | 被 [drag-drop-direct-extract.md](.omo/plans/进行中/drag-drop-direct-extract.md) 取代 |
| 预览格式识别（旧方案） | [preview-format-detection.old.md](.omo/plans/已归档/preview-format-detection.old.md) | 🟡中 | — | 预览格式识别旧设计 | 已拆分为 [preview-extended-formats.md](.omo/plans/进行中/preview-extended-formats.md) + [preview-magic-detection.md](.omo/plans/已完成/preview-magic-detection.md) |
| Office 内容预览（WPF 版） | [office-content-preview.md](.omo/plans/已归档/office-content-preview.md) | 🟡中 | — | WPF 版 Mammoth→HTML→WebView2 方案 | 被 [office-content-preview-avalonia.md](.omo/plans/已完成/office-content-preview-avalonia.md) 取代 |
| 资源管理器式路径切换弹窗 | [explorer-path-switcher.md](.omo/plans/已归档/explorer-path-switcher.md) | 🟡中 | — | WPF 时代 Ctrl+G 路径速选弹窗 | 被 QuickPathControl 系列演进取代（CustomFilePickerDialog） |
| QuickPathControl 原始组件计划（WPF 版） | [quick-path-control.md](.omo/plans/已归档/quick-path-control.md) | 🟡中 | — | WPF 版路径速选控件原始设计 | 同上，被后续演进取代 |
| MSI 安装包（WiX） | [msi-packaging-wix.md](.omo/plans/已归档/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno 安装包 + 便携包 + 自包含包 | 2026-11-04 确认删除：Inno 已满足需求，MSI 只对域环境批量部署有意义 |
| 压缩包对比（Archive Diff） | [archive-diff.md](.omo/plans/已归档/archive-diff.md) | 🟡中 | 3-4h | 两个包的内容差异对比 | 2026-11-04 确认删除：无用户需求证据，同类工具也没这个功能 |
| 文件列表自定义列 | [custom-columns.md](.omo/plans/已归档/custom-columns.md) | 🟡中 | 4-6h | 元数据列（文档标题 / 图片尺寸） | 2026-11-04 确认删除：价值低，且与缩略图方案重叠 |
| 冻结列 | [frozen-column.md](.omo/plans/已归档/frozen-column.md) | 🟢低 | 1-2h | DataGrid 左侧列冻结 | 2026-11-04 确认删除：仅窄窗口有意义，投入产出比过低 |
| 可插拔预览模块体系 | [preview-modular-providers.md](.omo/plans/已归档/preview-modular-providers.md) | 🟡中 | 3-4h | ITableDataProvider 抽象 + provider 插件 | 2026-11-04 确认删除：接口零消费者（只有 SqliteDataReader 一个实现且 UI 直连 Microsoft.Data.Sqlite） |
| 右键菜单目录结构预览 | [context-menu-tree-preview.md](.omo/plans/已归档/context-menu-tree-preview.md) | 🔴高 | 6-8h | COM 菜单里画文件树 | 2026-11-04 确认删除：高难度 / 低收益，且与跨平台方向冲突 |


---

*此文档将随开发进度持续更新。*
