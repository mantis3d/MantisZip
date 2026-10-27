# MantisZip — 开发计划

> 未来待开发功能规划。已实现的功能见 [docs/PROGRESS.md](PROGRESS.md)，技术架构见 [docs/ARCHITECTURE.md](ARCHITECTURE.md)，每项计划的完整设计见 `.omo/plans/`。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-10-25  
**当前版本**: 0.5.4

---

## 这份文档怎么读

> **2026-10-24 结构重构**：原 P1–P4 优先级分档废弃。原因有两个：① 旧 P1 里混着「已做完主体只剩尾巴」和「该砍」的计划，读起来像全部要做；② 旧 P4 里混进了「已完成计划的遗留子项」，两者性质不同。现在按**「现在做，还是不做」**分档，每档只有一个含义。

| 档位 | 含义 | 承诺 |
|:--:|---|---|
| **S** | 本轮（迭代 1）就做 | 已排期，做完就发版 |
| **A** | 下一轮（迭代 2–3） | 已排期，但可以滚动调整 |
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

## S 档 —— 本轮必做（约 14-21h，≈ 两天满负荷）

> **执行顺序（2026-10-24 讨论定，2026-10-26 按完成情况更新）**：~~图标~~（已完成）→ ~~接缝~~（已完成）→ 预览 → 自动更新。先做「一眼能看出变化 + 零逻辑风险」的，再做「掏返工税」的，最后做「地基先就位才划算」的。
>
> 已完成并归档：⚡ 插队项「删除压缩包内目录静默无效」（2026-10-26，随 v0.5.4）、S 档「图标归一化」（2026-10-26，随 v0.5.4）、S 档「意图-执行接缝收口（含波次 3 全仓清账）」（2026-10-26，随 v0.5.4）—— 详见 [PROGRESS.md 历史设计方案索引](PROGRESS.md)。

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 预览取数层内存优先（切片 1+2+4） | [preview-memory-first.md](.omo/plans/未开始/preview-memory-first.md) | 🟡中 | 12-17h | 📋 2026-10-24 立项。现状：**每次预览都先把整条目解压到 `%TEMP%\MantisZip\Preview\` 再从磁盘读回**（固定文件名、每次预览前清空整个目录）；文本类写盘只为读 1MB；便携模式往 U 盘写；敏感文件明文落 TEMP。本批＝文本/CSV/Markdown/代码/日志走内存 + 图片 `SKBitmap.Decode(Stream)` + PDF `PdfDocument.Open(Stream)` + head 读取改真流式（Zip 不再整条目解压进内存）+ 临时文件按条目命名 + head LRU + >15MB 的 head-only 部分预览。**明确保留落盘**：HTML→WebView（`NativeWebView` 不支持 `data:` URI）、打开方式（外部程序要路径）、字体（`FontParser` 要文件名）。注：15MB 上限本身可在设置里调大，故切片 4 收窄为**文本/PDF/Office 的部分预览**（图片必须完整字节，无部分可言）。Office（切片 3）见 A 档 |
| 自动更新检测 | [auto-update.md](.omo/plans/未开始/auto-update.md) | 🟡中 | 4-6h | GitHub Releases API 版本检查 + AboutWindow「更新」Tab + `UpdateAvailableDialog` + 设置开关 + 单元测试。**面向公开用户后这从「加分项」变「基础设施」**：没有它，每个修复都到不了用户手上 |

> 本轮验收口径：公众能感知到的两件事同时改善——**预览不再先落盘（且大文件不再一律被拒）**、**安装完会自己知道有新版本**。（图标归一化、意图-执行接缝均已于 2026-10-26 完成，随 v0.5.4 验证）

---

## A 档 —— 下一轮（约 60h，是「接下来两轮」的量，不是一轮）

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| 面包屑地址栏（PathBreadcrumb） | [path-breadcrumb.md](.omo/plans/未开始/path-breadcrumb.md) | 🟡中 | 6-8h | 三处地址栏（主窗口 / QuickPathPicker / CustomFilePickerDialog）改为资源管理器式面包屑：段点击直达 + Ctrl+L 进编辑态 + 段数阈值折叠 + 虚拟根段。📋 2026-10-21 已按代码核准（`TryConfirmFile` 无模式判断须保持现状；`Path` 定为 StyledProperty+OneWay；补磁盘空路径态与 `Breadcrumb_EditPlaceholder` 三语；QuickPathPicker 4 宿主纳入回归） |
| 压缩包内重命名/移动条目 | [archive-rename-entry.md](.omo/plans/未开始/archive-rename-entry.md) | 🟡中 | **6-8h**（原估 3-4h） | 右键重命名(F2)/移动到…。7-Zip / WinRAR 都有，公开用户会问。**工时按 6-8h 重估**：实现是全量重写包，必须先写临时包再原子替换 + 校验，否则一次失败就是毁包 |
| 预览取数层内存优先（切片 3：Office） | [preview-memory-first.md](.omo/plans/未开始/preview-memory-first.md) | 🟡中 | 5-7h | Office 各 `*.Open(Stream)`（`SpreadsheetDocument` / `WordprocessingDocument` / `PresentationDocument`），字体明确豁免。**单独排在 A 档的理由**：三者都是「本来就在解析完整文档」，省下的只是落盘，用户感知最弱，且逐库确认 Stream 支持最费时 |
| 自包含体积优化（第一步） | [selfcontained-size-optimization.md](.omo/plans/未开始/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 📋 **目标已改写**：原「降至 20–25MB」与现有依赖（SkiaSharp / ImageSharp / OpenXml / Sqlite / PdfPig / WebView）矛盾，激进修剪会打穿预览。改为**第一步只做测量基线 + 验证保守修剪可行性**，目标区间 60–90MB。若确认只发 SelfContained 包，这一步的收益优先级上调 |
| Winget 发布 | [winget-publishing.md](.omo/plans/未开始/winget-publishing.md) | 🟢低 | 1-2h | 发布到 Windows Package Manager 社区仓库；首次手动提交后 CI 自动化。公开用户的分发入口，1-2h 的性价比极高 |

### A 档 · 小活清仓（批量 8 项，合计约 25h）

> 都是 2–4h 的「便宜、感知明确」项，攒成一批做完，避免它们长期占着队列。

| 功能 | 设计文档 | 工时 | 说明 |
|---|---|:--:|---|
| 进度条分段着色 | [progress-bar-segments.md](.omo/plans/未开始/progress-bar-segments.md) | 3-4h | 按批处理项状态着色（红=失败/绿=成功/青=跳过/蓝=进行中），动态预算缝隙 |
| 压缩文件名后缀模板 | [filename-suffix-template.md](.omo/plans/未开始/filename-suffix-template.md) | 2-3h | `{date}`/`{datetime}`/`{seq}` 占位符，防同名覆盖 |
| 预览「显示内容」开关 | [preview-show-content-toggle.md](.omo/plans/未开始/preview-show-content-toggle.md) | 2-3h | 关闭后只显示骨架、跳过磁盘递归扫描（大型源目录预览加速） |
| 骨架态实时进度计数器 | [skeleton-progress-counter.md](.omo/plans/未开始/skeleton-progress-counter.md) | 3-4h | 骨架期每 0.5s 更新目录节点文件数/大小 |
| ICO 文件自身图标显示 | [ico-file-icon-extract.md](.omo/plans/未开始/ico-file-icon-extract.md) | 2-3h | ico 文件在列表显示自身嵌入图标 |
| Core 临时目录根可注入 | [core-temp-root-injectable.md](.omo/plans/未开始/core-temp-root-injectable.md) | 2-3h | Core 层 4 处 `%TEMP%\MantisZip` 硬编码在便携模式仍写系统 TEMP；方案为 `TempPaths` 注入，未注入时行为不变 |
| CLI 解压后打开文件夹对齐 | [cli-extract-open-folder.md](.omo/plans/未开始/cli-extract-open-folder.md) | 2-3h | CLI 解压成功后复用 `SmartOpenPathResolver`；多文件批处理保持不开 |
| 提取日志与解压「后悔药」 | [extract-journal-undo.md](.omo/plans/未开始/extract-journal-undo.md) | 3-4h | 解压记录 + 一键回滚 |

---

## B 档 —— 有价值，排队（不排期）

| 功能 | 设计文档 | 难度 | 工时 | 说明 |
|---|---|:--:|:--:|---|
| ZIP 多线程压缩方案 D（自适应 + 多线程） | [parallel-compress-plan-d.md](.omo/plans/未开始/parallel-compress-plan-d.md) | 🟡中 | 4-5h | 收口已做完 80% 的压缩加速叙事。**半成品选项比没有选项更糟**（用户会以为没生效），建议在 S 档之后尽早取 |
| ZIP 多线程压缩方案 A（分组并行 + 多级别） | [parallel-compress-plan-a.md](.omo/plans/未开始/parallel-compress-plan-a.md) | 🟡中 | 6-8h | 按级别分组 → 各组独立 ZipWriter 并行 → 合并。**依赖方案 D 完成** |
| 压缩预估（Compression Estimator） | [compression-estimator.md](.omo/plans/未开始/compression-estimator.md) | 🟡中 | 14-18h | 🔶 比率表/格式目录/自适应规则模型已完成，**估算器本体未开始**（`grep Estimator` 命中 0）。含压缩前查表/采样标准/可选精确预估 |
| 压缩中 ETA 增强 | [runtime-eta.md](.omo/plans/未开始/runtime-eta.md) | 🟡中 | 5-7h | 🔶 基础 ETA 已实现（EMA 速度 + 单档案剩余 + 暂停冻结）；本计划补先验速度融合、批处理总剩余、收尾文案 |
| 文本预览语法高亮 | [text-preview-syntax-highlighting.md](.omo/plans/未开始/text-preview-syntax-highlighting.md) | 🟡中 | 4-5h | 📋 计划已重写为 Avalonia 版（原 AvalonEdit 方案废弃），**代码未实施**（csproj 无 AvaloniaEdit/TextMate）。AvaloniaEdit + TextMate + 三级语言识别 |
| 文件列表缩略图查看方式 | [file-list-thumbnails.md](.omo/plans/未开始/file-list-thumbnails.md) | 🟡中 | 4-6h | 三种布局循环切换（详情/平铺/内容行）+ 内容卡三档 + 持久化。**「现代感」最大来源**，复用现有 PreviewService |
| 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.omo/plans/未开始/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API 两层提取嵌入缩略图；它是上面「文件列表缩略图」的进一步扩展，**建议拆开做** |
| 文件过滤与文件列表筛选数据统一 | [unify-file-filter-data.md](.omo/plans/未开始/unify-file-filter-data.md) | 🟡中 | 8-12h | 📋 计划文档是 WPF 期产物，实施前须按规则 11 转 Avalonia。目标：`FileFilterCriteria` 扩展 `TextSearch`/`ExcludeText`/`TextMatchMode`，删除 `SearchFilters` + `ArchiveFilter.ApplyFilters()`（`MainWindowViewModel.Navigation.cs:309` 仍在用旧模型） |
| RAR 压缩（外置 rar.exe） | [rar-compression.md](.omo/plans/未开始/rar-compression.md) | 🟡中 | 8-10h | 通过已安装的 WinRAR 实现 RAR 压缩（含 SevenZipEngine 注册冲突处理）。公开用户会问，但依赖外部程序 |
| 压缩/解压配置预设 | [compress-preset.md](.omo/plans/未开始/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| 文件选择器去弹窗化（内嵌控件） | [embedded-file-picker.md](.omo/plans/未开始/embedded-file-picker.md) | 🟡中 | 1-2天 | 📋 2026-10-08 立项：把文件/目录选择器内嵌进压缩/解压设置窗。三步走：先抽纯控件再分别接入；不动 `CustomFilePickerDialog` 静态入口与系统浏览豁免。**与接缝计划有交集，排在接缝之后做可省一次返工** |

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
| 快速预览与渐进式加载（三种预览模式） | [preview-quick-modes.md](.omo/plans/未开始/preview-quick-modes.md) | ~25h | 预览体系已经很深，三模式是「复杂大于收益」。其「§3.3 提取策略：内存 vs 硬盘」子项已**单独立项**为 [preview-memory-first.md](.omo/plans/未开始/preview-memory-first.md) 并进 S 档 | 若解冻，**只做「快速模式」最小版**（渐进渲染部分，取数层已由 preview-memory-first 承担） |
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
                                    （embedded-file-picker / archive-rename-entry 等先做它可省返工）

icon-normalization（S，已完成 2026-10-26）──── 前置 ──▶ icon-theme-infra（冻结）──▶ packs ──▶ sets

parallel-compress-plan-d ── 前置 ──▶ parallel-compress-plan-a

compression-estimator ── 可选前置 ──▶ runtime-eta（无先验时行为与现状一致，零返工）

preview-memory-first（S）── 取数层复用 ──▶ file-list-thumbnails（缩略图复用同一 head 缓存）

parallel-compress-plan-d ── 布局前提 ──▶ progress-bar-segments（分段进度条在进度窗口重构后的布局上）

NuGet 升级（SharpCompress ≥0.49.0）── 联动 ──▶ selfcontained-size-optimization（TrimmerRootAssembly 需随升级更新）
```

补充说明：

- `cross-platform-port` 已冻结。**唯一保留的纪律**：新增代码不要加深 Win32 耦合（新增平台相关调用时，优先走已有抽象或加一层接口）。
- `archive-rename-entry` / `rar-compression` 为软依赖，进度窗口增强会自动改善其体验。

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
