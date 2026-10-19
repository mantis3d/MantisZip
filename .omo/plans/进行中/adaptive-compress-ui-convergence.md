# 压缩加速三开关 — UI 收敛（Phase 1）+ 魔数检测/自适应规则实现（Phase 2）

> **状态**: 🚧 Phase 1 实施中（Task 1–6 + 常驻提示已落地，Task 8 验证中）| **日期**: 2026-10-16（**2026-10-18 评审修订 + 实测拍板 + 同日双机复测改判**；**2026-10-19 Phase 1 实施：Task 1/2/4/5/6 完成，新增常驻科普提示**）| **优先级**: P1 | **预估工时**: Phase 1 = **4-5h**（组数规则简化为 `clamp(核数,4,16)`，Task 0 双机实跑完成；改动 B 暂缓 → Phase 1 无门控代码）/ Phase 2 = **5-7h**（原 3-4h + Task 12 本机基准测试约 +2-3h）
> **来源**: 用户 2026-10-16 提出「压缩有个选项有两个开关『多线程』和『自适应』，测试发现不启用自适应时开启多线程反而有反作用」→ 实测 + 代码审计后扩展为两阶段计划
> **原型**: [docs/prototypes/compress-format-options-panel.html](../../docs/prototypes/compress-format-options-panel.html)（交互原型，已覆盖两处界面）
> **评审**: 2026-10-18 逐条核对源码，修正 4 处会直接踩坑的事实错误（A1–A4）、1 处与既有 bench 结论冲突（B1）、若干语义/文档遗漏（C/D 项），详见文末「修订记录」
> **实测拍板（2026-10-18）**: Task 0 基线复测已实跑（20 逻辑核机 + 8 逻辑核机双机对照，报告 `.buildout\bench-before-*.txt` / `bench-retest-8lp-*.txt`）→ 引擎默认组数 `clamp(核数,4,16)`、方案 C 保留但标注「未验证」；**改动 B（MT 依赖自适应）暂缓拍板**——20 核机上「仅 MT」有真实收益、8 逻辑核机上「仅 MT」净负收益（用户原始观察在弱 CPU 上复现），任何一刀切静态策略均片面 → Phase 1 维持三开关独立（无门控代码），最终策略待 Phase 2「本机基准测试」按机器定夺；详见文末「拍板记录」

## 背景

压缩加速面板有三个开关（`自适应压缩` / `魔数检测` / `多线程压缩`），压缩对话框 ZIP 面板另有一个 `并行压缩组数`。2026-10-16 实测 + 全仓代码审计后发现五个问题（2026-10-18 已逐条复核；其中 #2 经同日双机复测后判定为硬件相关、暂缓拍板，其余证据成立）：

| # | 问题 | 证据 |
|---|------|------|
| 1 | **「并行压缩组数」是性能陷阱** | 16 核机器把组数设为 2，压缩只用 **1.95 个核**（进程 CPU/墙钟实测），耗时 **5.47s vs 设 16 组的 1.64s（3.3x）**。根因：N 组路径每组强制 `mt=off`（`Core/Engines/ZipEngine.cs:4103`），**并行度 = 组数**（实测 `avgCores ≈ degree`）。且该旋钮对速度**无正收益**（deg=1/2/4/8/16 互有胜负，平台区 2–8）⚠ 与 2026-10-05 bench 结论方向相反，见 Task 0 |
| 2 | ~~**「多线程压缩」在自适应关闭时是负收益**~~ → **双机数据矛盾，暂缓拍板** | 原证据：可压缩数据（text 语料）上「仅多线程」比**完全不开多线程**慢 **1.5–1.8x**。2026-10-18 **双机复测**：20 核机上**未复现**（text B 0.88s < D 0.95s，MT 有真实收益）；但 **8 逻辑核机（i7-3770 4C/8T）上复现**（text B 2.66s vs D 1.87s 慢 42%；mixed B 3.90s vs A 2.85s 慢 37%）——**强 CPU 有收益 / 弱 CPU 负收益，一刀切静态策略必然片面** → 改动 B **暂缓**：Phase 1 维持三开关独立（无门控代码），最终策略待 Phase 2 本机基准测试（Task 12）按机器定夺 |
| 3 | **「魔数检测」是空转开关** | `AdaptiveSmartDetect` 全仓**无任何引擎消费者**：只有 UI 绑定 + `AppSettings` 持久化 + `CompressRequest → ArchiveOptions` 透传；`AdaptiveRuleMatcher.ResolveLevel(..., adaptiveSmartDetect:)` 形参**只写日志、不参与分支**（`Core/Utils/AdaptiveRuleMatcher.cs:30`）。而 `Core/Abstractions/ArchiveEngine.cs:183` 注释写的是「自适应关闭时无效」——语义上该联动，UI 也漏了 |
| 4 | **「自适应规则」也是空转** | `AdaptiveRuleMatcher.ResolveLevel`（唯一会应用规则的函数）**无生产调用点**（仅 `AdaptiveCompressionTests` 引用）；`CompressRequest` / `ArchiveOptions` **根本没有规则字段**。即用户配了规则不生效 |
| 5 | 自定义格式的「魔数 hex」也不生效 | `ZipEntryClassifier.GetAdaptiveLevel` 只读 `format.Extensions`（`Core/Utils/ZipEntryClassifier.cs:73-94`），**从不读 `MagicHex`** |

**Phase 1 目标**：先出一版「UI 不说谎」的可用版本 —— 删除/隐藏空转控件，修掉 #1 这个真实性能坑（删「并行压缩组数」旋钮 + 引擎默认组数改 `clamp(核数,4,16)`，见 Task 1/2）。#2 经双机实测**判定为硬件相关、无法一刀切** → 改动 B **暂缓**（Phase 1 维持独立无门控，见 Task 3）。
**Phase 2 目标**：实现 #3 #4（魔数检测 + 自适应规则），把 Phase 1 隐藏的 UI 接回来，并新增**本机基准测试**（Task 12：按机器实测 MT 收益，决定默认勾选 + 常驻收益提示文案）。

## 决策记录（2026-10-16 用户授权「按你觉得合适的来」+ 2026-10-18 评审补充）

| 决策 | 取值 | 理由 |
|------|------|------|
| 改动 A（删「并行压缩组数」）是否进 Phase 1 | **进** | 实测确认「deg=2 陷阱」（比默认慢 3.5–3.7x），且该旋钮对速度无正收益 → 属真实缺陷修复 |
| 改动 B（多线程依赖自适应）是否进 Phase 1 | **暂缓（2026-10-18 双机数据后改判）** | 曾基于 20 核机数据拍板「取消」（仅 MT 有真实收益），但 8 逻辑核机复测**方向反转**（text B 2.66s vs D 1.87s 慢 42%）→ **强 CPU 有收益 / 弱 CPU 负收益，一刀切必然片面**。Phase 1 维持独立无门控，最终策略待 Phase 2 本机基准测试（Task 12） |
| 引擎侧方案 C（`mt = 核数/组数`）是否进 Phase 1 | **进（4 行改动），标注「未验证」** | 2026-10-16 测量：`mt=N` 被 7z 接受（`-mmt=N`）、端到端 avgCores 恒 ≈12。⚠ **2026-10-18 实测补充**：默认路径（本机 16 组）下 C 是 **no-op**（`mt = max(1, 20/16) = 1`），仅在「核数 > 16」或「文件数 < 组数（少量大文件）」时生效，而这两场景的收益**尚未验证**（本机小文件语料证明单组 `mt=on` 只用约 1 个核）→ 保留但不得当性能依据，验证方法见 Task 2「待验证」 |
| 默认组数取多少 | **`clamp(核数,4,16)`（2026-10-18 实测拍板）** | Task 0 实测否决两个候选：`clamp(核数,2,8)` 相对现默认（cap 16）是回退（deg=8 比 deg=16 慢 21–30%）；`deg=1` 更慢（text ~1.8x）。取 `clamp(核数,4,16)`：保留本机 16 组的最优表现，低核下限 4 避开已证实的 deg=2 陷阱 |
| 自定义格式「魔数 hex」（#5）是否一起隐藏 | **一起隐藏** | 与魔数检测是同一件事，留着同样是骗人 UI |
| 「隐藏」的实现方式 | **用显式 `false` 门控 + Phase 2 注释，不删标记** | Phase 2 = 改回来（1 行），避免重建标记；代价是留少量 dead markup（计划内已列「Phase 2 恢复清单」）。⚠ XAML 内占位注释必须写 `<!-- -->`（不是 `//`） |
| 保留不动的部分 | **仅「与魔数/规则相关」的部分**：`AppSettings.AdaptiveSmartDetect` / `AdaptiveOverrides` / `MultiThreadedStoreFormatIds`、`CompressRequest`/`ArchiveOptions` 透传链路、其余三语 key、`EditAdaptiveRuleDialog`、`AddCustomFormatDialog` | Phase 2 只需「UI 接回来 + 引擎实现」，链路不重做。**`AppSettings.ParallelCompressDegree` 与 2 个组数 key 不在保留范围内（Task 1 要删）** —— 2026-10-18 评审修正：原文「保留 AppSettings 字段」与 Task 1 自相矛盾 |
| ~~「MT 依赖自适应」的生效值由谁计算~~ → **不适用（改动 B 暂缓）** | **Phase 1 不做任何夹取** | `MultiThreadedCompression` 与 `AdaptiveCompression` Phase 1 保持**互相独立**（无门控代码，同现状）；`CompressService.BuildOptions` / `CompressFlow.BuildRequest` 均不引入相关夹取（评审 A1 随之关闭）。最终策略（若需要门控/默认值调整）待 Task 12 基准测试结果 |
| 「多线程压缩」的持久化语义 | **维持原语义**：`AppSettings.MultiThreadedCompression` 直存直用，**不引入「意图 / 生效值」二分** | Phase 1 无门控无需区分意图与生效值（评审 C3 随之关闭）；UI 显示的就是引擎收到的值。Task 12 若引入「基准测试建议默认值」，只影响**首次默认播种**与提示文案，不改直存直用语义 |
| ZIP 面板 MT 的联动条件 | **仅剩 1 个：ZIP32 4GB 超限**（现有 `UpdateTotalSourceSize` 逻辑**保留不动**：超限 → 取消勾选 + 置灰 + 灰字提示；退出超限 → 恢复原勾选 `_mtCheckedBeforeLimit`） | 自适应不是 MT 的门控（改动 B 暂缓，Phase 1 无联动）→ 不需要 `RefreshZipMtState()` 状态机（评审 A2 随之关闭）。另：删掉组数面板后 `ZipMultiThreadCheck_IsCheckedChanged` 已无联动对象 → Task 1 连带删除该处理器及其 axaml 事件属性 |

## Phase 1：UI 收敛（4-5h）

### Task 0（新增 · 评审 B1/B2）— 基线复测与默认组数定调

> **为什么必须先做**：本计划「deg=1 因串行镜像慢 5%、平台区 2–8」与 `docs/superpowers/plans/2026-10-05-n-group-parallel-compress.md:20`（`docs/PROGRESS.md` 10-06 同步）的结论「**N 组无加速，degree=1 全面最快或持平，degree=2 最差**」**方向相反**。两次 bench 若都成立，则「默认 8 组」这一决策缺乏依据；若旧结论已过期，必须在计划里写清差异来源（机器/语料/代码版本）。

**Files:** 无代码改动；产出报告留在 `.buildout/`（已 gitignore）

- [x] **Step 1: 复测当前（改动前）基线** ✅ 2026-10-18 已实跑（报告 `.buildout\bench-before-*.txt`）

    ```powershell
    dotnet run scripts\bench-zip-mt.cs -- --profile text  --degrees 0,1,2,4,8,16 --reps 3 --report .buildout\bench-before-text.txt  --json .buildout\bench-before-text.json
    dotnet run scripts\bench-zip-mt.cs -- --profile media --degrees 0,1,2,4,8,16 --reps 3 --report .buildout\bench-before-media.txt --json .buildout\bench-before-media.json
    dotnet run scripts\bench-zip-mt.cs -- --profile mixed --degrees 0,1,2,4,8,16 --reps 3 --report .buildout\bench-before-mixed.txt --json .buildout\bench-before-mixed.json
    ```

    `--degrees` 扫描各组数（`0` = 引擎默认，改动前 = `ProcessorCount`，改动后 = Task 2 的新默认规则 —— 便于 before/after 直接对齐「默认路径」）；`--profile` 取 `text` / `media` / `mixed`；`--report` 出 UTF-8 完整报告（终端可能乱码），`--json` 出结构化结果。

    **执行环境记录（2026-10-18 实跑）**：本机 **20 逻辑核**（与脚本头部记录的 2026-10-04 旧 bench 同环境）、`dotnet 10.0.402`、语料为脚本默认合成集（文本 200×250KB / 媒体 100×500KB）、`--reps 3`。⚠ 计划后文出现的「16 核 / avgCores ≈12」是 2026-10-16 的测量值，本机复现不了；Task 0 只判定**相对快慢**（deg=1 单组 `mt=on` vs 2–8 组），该判定与核数无关。另注：本机 `--degrees 0` 后（Task 2 完成后）为 `clamp(20,2,8)=8` 组 × `mt=20/8=2`。
    Expected: 报告给出各 degree 的墙钟秒数与 CPU 占用；与 2026-10-16 的 5.47s / 1.64s 量级对齐（同机同语料）。

- [x] **Step 2: 判定并定调（2026-10-18 完成）**

    - `deg=1`（既有单组 `mt=on` 路径）**劣于**多组（text 慢 ~1.8x，media/mixed 打平）→ **「默认 = 1」否决**；
    - 但「2–8 组快于 deg=1」也不足以支撑 `clamp(核数,2,8)`：本机 **deg=8 明显慢于 deg=16**（text +21% / mixed +30%）→ 该候选同样否决；
    - **定调：`clamp(核数,4,16)`**（本机 = 16 组）。旧 bench「N 组无加速、degree=1 全面最快」不成立的可能成因：旧扫描**未含 deg=16**，且当时 `MultiThreadedCompression` 默认关闭（`docs/PROGRESS.md` 2026-10-06 条目自身注明）→ 原结论适用范围有限。

- [x] **Step 3: 低核数对照（评审 B2）—— ✅ 部分完成（2026-10-18 8 逻辑核机复测）**

    原计划在 4 核 VM 上跑；实际在 **8 逻辑核机（i7-3770 4C/8T）** 上完成三语料全扫描（`.buildout\bench-retest-8lp-{text,media,mixed}.txt`，数据见下方「8 逻辑核机复测」小节）。纯 2–4 物理核环境仍未测。

    结论：8 逻辑核下 **deg=2 陷阱依旧成立**（text B 6.83s vs deg=0 2.66s，慢 2.6x），其余组数（1/4/8/16）相对 deg=0 落在 1.0–1.7x 区间，**未出现新的超订塌陷** → `clamp(核数,4,16)` 在 8 核级别无额外风险。但更重要的发现是 **MT 路径在本机整体净负收益**（见观测 4′），该问题无法靠调组数解决。

- [x] **Step 4: 把 before 数据与定调结论写进本计划（供 Task 8 做 after 对照）** ✅ 数据与拍板结果均已在下方

#### 📊 实测结果（2026-10-18 实跑 · 本机 20 逻辑核 / dotnet 10.0.13 / level 5 / reps 3）

报告：`.buildout\bench-before-{text,media,mixed}.txt` + `.json`

**最小耗时（秒）** —— B = 仅多线程，C = 自适应+多线程，A = 仅自适应，D = 全关（A/D 与组数无关，用作基准）

| 语料 | 配置 | deg=0 | deg=1 | deg=2 | deg=4 | deg=8 | deg=16 |
|------|------|-------|-------|-------|-------|-------|--------|
| text 54MB / 200 文件 | **B** | 0.88 | 2.60 | **3.07** | 1.63 | 0.96 | **0.79** |
| text | **C** | 2.88 | 2.55 | **3.05** | 1.64 | 0.99 | **0.78** |
| text | A | 0.97 | 1.75 ⚠ | 0.94 | 0.94 | 0.94 | 0.94 |
| text | D | 0.95 | 1.65 ⚠ | 0.91 | 0.91 | 0.91 | 0.91 |
| media 49MB / 100 文件 | **B** | 0.27 | 0.24 | **0.56** | 0.36 | 0.26 | 0.27 |
| media | **C** | 0.23 | 0.23 | 0.22 | 0.22 | 0.22 | 0.24 |
| media | A | 0.22 | 0.22 | 0.21 | 0.22 | 0.22 | 0.22 |
| media | D | 1.22 | 1.22 | 1.22 | 1.22 | 1.22 | 1.22 |
| mixed 103MB / 300 文件 | **B** | 0.98 | **0.81** | **3.59** | 1.98 | 1.26 | 0.97 |
| mixed | **C** | 0.98 | 0.96 | **3.25** | 1.88 | 1.20 | 0.99 |
| mixed | A | 1.18 | 1.18 | 1.18 | 1.18 | 1.18 | 1.19 |
| mixed | D | 2.13 | 2.14 | 2.13 | 2.13 | 2.15 | 2.13 |

- ⚠ text 的 **deg=1 那一批**里 A/D（代码上**与组数无关**）也整体涨到 ~1.7s（+80%）→ 该批疑有环境噪声；`deg=1` 的**绝对值**建议复测（`--degrees 1 --reps 5`），本文的定性排序不受影响。
- 压缩率：B/C 在 text 上 **7.15x**（比 A/D 的 6.21x 高 15%），mixed 1.823x（+2.1%），media 全部 1.000x（全 Store）。
- `deg=0`（引擎自动 = 改动前 `ProcessorCount` → cap 16）与 `deg=16` 基本重合（text 0.88/0.79、mixed 0.98/0.97、media 0.27/0.27）→ 现默认已接近本机最优。

**观测（Task 2 / Task 3 的定调依据）**

1. ✅ **deg=2 陷阱确认**：三语料 B/C 的 `deg=2` 全部最差（text 3.07/3.05、mixed 3.59/3.25、media 0.56），相对 `deg=0` 慢 **3.5–3.7x** → 删旋钮的正当性成立。
2. ❌ **「平台区 2–8」不成立**：本机是「**越多组越快（至 16）**」。text deg=8 0.96 vs deg=16 0.79（+21%）；mixed 1.26 vs 0.97（+30%）→ **`clamp(核数,2,8)` 相对现默认是性能回退**。
3. ❌ **「deg=1 只慢 5%」不成立**：text deg=1 远慢于 deg=16（单组 `mt=on` 2.60s vs 16 组 0.79s；即便按噪声折算 ~1.44s 仍慢 ~1.8x）→ 「**默认 = 1**」分支**否决**。但 mixed 上 deg=1 反而最快（B 0.81 / C 0.96）—— 该语料一半是 Store，N 组合并有额外 IO 开销。
4. ❌ **改动 B 的前提在 20 核机默认组数下不成立**：text **B 0.88 < D 0.95 < A 0.97**（且 B 压缩率还高 15%）、mixed **B 0.98 ≪ A 1.18 ≪ D 2.13**。「仅多线程更慢 1.5–1.8x」只在 **deg≤4** 出现（vs D：deg=1 1.58x、deg=2 3.4x、deg=4 1.8x）。⚠ **此结论仅对强 CPU 成立——8 逻辑核机方向反转，见下方复测小节与观测 4′**。
5. ⚠ **方案 C（`mt = 核数/组数`）在本机默认路径上是 no-op**（groups=16 → `mt = max(1, 20/16) = 1`）。其价值取决于「7z 的 `mt` 能否在**单文件内**并行」——本语料（200×250KB 小文件）证明单组 `mt=on` 只用约 1 个核（2.60s vs 16 组 0.79s），故需**大文件语料**（`--corpus <含数百 MB 级单文件的目录>`）才能验证；未验证前不应作为 Phase 1 的必要条件。

#### 📊 8 逻辑核机复测（2026-10-18 · i7-3770 4C/8T / dotnet 10.0.11 / level 5 / reps 3）

> **为什么补测**：20 核机的「改动 B 取消」拍板仅基于单机数据；用户报告两台电脑结论不一致 → 在第二台机器（弱 CPU）上按相同参数复跑三语料。报告：`.buildout\bench-retest-8lp-{text,media,mixed}.txt` + `.json`。

**最小耗时（秒）** —— `deg=0` 在本机解析为 `ProcessorCount=8` 组

| 语料 | 配置 | deg=0 | deg=1 | deg=2 | deg=4 | deg=8 | deg=16 |
|------|------|-------|-------|-------|-------|-------|--------|
| text 54MB / 200 文件 | **B** | **2.66** | 2.72 | 6.83 ⚠ | 3.93 | 3.17 | 3.01 |
| text | **C** | **2.61** | 2.79 | 7.65 ⚠ | 4.43 | 2.86 | 3.10 |
| text | A | 2.11 | 2.64 | 2.39 | 2.29 | **1.99** | 2.61 |
| text | D | **1.87** | 2.01 | 1.93 | 2.08 | **1.82** | 2.02 |
| media 49MB / 100 文件 | A | **0.60** | 0.57 | 0.61 | 0.78 | 0.68 | 0.60 |
| media | C | **0.60** | 0.66 | 0.75 | 0.94 | 0.71 | 0.62 |
| media | B | 1.00 | 1.09 | 1.62 | 1.58 | 1.09 | **0.95** |
| media | D | 2.62 | 2.41 | 2.86 | 2.83 | 2.84 | **2.31** |
| mixed 103MB / 300 文件 | A | **2.85** | 3.09 | **2.60** | 2.68 | 2.84 | 3.03 |
| mixed | C | **3.32** | 4.09 | 6.14 ⚠ | 4.04 | 3.59 | 3.84 |
| mixed | B | **3.90** | 4.24 | 7.49 ⚠ | 4.72 | 4.08 | 4.23 |
| mixed | D | **4.16** | 4.18 | 4.37 | 4.46 | 4.93 | 4.59 |

**双机对照（deg=0，即各自引擎默认组数）**

| 语料/对比 | 20 核机（deg=0→16 组） | 8 逻辑核机（deg=0→8 组） | 方向 |
|---|---|---|---|
| text：B（仅MT）vs D（全关） | B 0.88 **<** D 0.95（MT 快 7%） | B 2.66 **>** D 1.87（MT **慢 42%**） | **反转** |
| mixed：B vs A（仅自适应） | B 0.98 **<** A 1.18（MT 快 17%） | B 3.90 **>** A 2.85（MT **慢 37%**） | **反转** |
| media：A vs B vs D | A 0.22 < B 0.27 < D 1.22 | A 0.60 < B 1.00 < D 2.62 | 一致（A 最优） |
| deg=2 陷阱（B 相对 deg=0 慢） | 3.5x | 2.6x | 一致 |
| B/C 压缩率优势 | text +15%、mixed +2.1% | text +15%、mixed +2.1% | **完全一致**（硬件无关） |

**观测 4′（8 逻辑核机，Task 3 / Task 12 的定调依据）**

- ❌ **「改动 B 取消」的前提在弱 CPU 上不成立**：text 上 **B 2.66 > A 2.11 > D 1.87**（B 比 D 慢 42%）、mixed 上 **A 2.85 < C 3.32 < B 3.90 < D 4.16**——**用户 2026-10-16 的原始观察「不启用自适应时开启多线程反而有反作用」在本机成立**。
- 机制解释：i7-3770 仅 **4 物理核**（8 逻辑核是超线程），8/16 组并行是超订；Deflate 为 ALU 密集负载，超线程仅带来约 20–30% 额外吞吐，撑不起 8 路真并行。20 核机 16 组并行把 2.6s 压到 0.79s（并行效率高），本机同机制只能得到 ~2.7s（并行效率坍塌），再叠加 MT 路径固有的临时文件写入 + 合并开销 → 净收益转负。
- 组数调整救不了：本机 B 最优组数是 deg=0/1（~2.7s），仍显著差于串行 D（1.87s）——**问题不在组数，在 MT 路径本身对弱 CPU 不划算**。
- 两机一致的事实：**deg=1（单组 `mt=on`）在小文件语料上都只用约 1 个核**（本机 B deg=1 2.72s vs D 1.87s；20 核机 2.60s vs 0.91s）—— 印证「7z 的 mt 对多小文件无效」，Task 2 方案 C 的「未验证」标注继续成立。

**✅ 拍板结果（2026-10-18 用户决策 · 双机数据后修订）**

- **(a) 改动 B：暂缓** —— 20 核机有收益 / 8 逻辑核机负收益，一刀切静态策略（无论「独立」还是「依赖」）必然片面。Phase 1 **维持三开关独立、无门控代码**（同现状，Task 3 无代码改动）；最终策略待 **Task 12 本机基准测试**按机器定夺（检测到负收益 → 默认不勾选 + 常驻科普提示，用户仍可手动开启）。
- **(b) 默认组数：`clamp(核数,4,16)`** —— 20 核机 = 16 组（实测最优）；8 逻辑核机 = 8 组（虽非本机 B 最优，但与现默认一致，且 deg=2 陷阱已被下限 4 避开）。低核下限 4 在 8 逻辑核级别未见新塌陷（Step 3）。
- **(c) 方案 C：保留**（`mt = max(1, 核数/组数)`），但**标注「未验证」** —— 默认路径下为 no-op，仅在「核数 > 16」或「文件数 < 组数」时生效（见 Task 2）。

**⚠ 仍然悬空的验证项（不阻塞 Phase 1，但须在计划里留痕）**

- **纯 2–4 物理核环境未测**（8 逻辑核 = 4 物理核部分回答了 Step 3；`clamp(核数,4,16)` 的 2 核行为仍未实测）→ 若日后发现回退，退化为 `min(核数,16)` 即可（1 行）。
- **Step 1 的 deg=1 批疑有环境噪声**（20 核机该批里与组数无关的 A/D 也整体 +80%）→ 若要引用 deg=1 的绝对秒数，先 `--degrees 1 --reps 5` 复测。
- **方案 C 的收益未验证**（需 `--corpus <含数百 MB 级单文件的目录>`；两机小文件语料下 7z 的 `mt` 近似 no-op）。
- **8 逻辑核机的 MT 负收益幅度是否代表所有弱 CPU**（本机是 2012 年 Ivy Bridge；较新的 6–8 核 CPU 可能介于两机之间）→ Task 12 基准测试正是为此设计，不做硬件白名单猜测。

---

### Task 1 — 删除「并行压缩组数」控件（UI 全删 + 设置字段）

| 文件 | 改动 |
|------|------|
| `Controls/DynamicFormatOptionsPanel.axaml` | 删 `ParallelDegreePanel`（:96-116，标签 + `ParallelDegreeInput` + 灰色提示），**以及 `ZipMultiThreadCheck` 上的 `IsCheckedChanged="ZipMultiThreadCheck_IsCheckedChanged"`（:80）** —— 该处理器的唯一作用是联动组数面板显隐；改动 B 暂缓（Phase 1 无门控）后它没有任何用途（**不是**「留给 Task 3 复用」） |
| `Controls/DynamicFormatOptionsPanel.axaml.cs` | 删 `ParallelDegreeLabel`/`ParallelDegreeHint`（:61-62）、`ParallelCompressDegree` 属性（:247-255）、**`ZipMultiThreadCheck_IsCheckedChanged` 整个处理器**（:312-320，连同 XML 注释）、`UpdateTotalSourceSize` 里的面板显隐 2 行（:384-385）、`LoadDefaults` 里的组数播种与面板显隐（:420-423）、面板相关 XML 注释（:93-95）。**保留** `_mtCheckedBeforeLimit`（:41）与 `UpdateTotalSourceSize` 其余逻辑（ZIP32 4GB 门控仍需要它） |
| `ViewModels/CompressSettingsViewModel.cs` | 删 `_parallelCompressDegree`（:209-211）、钳制 handler（:496-509）、设置默认值播种（:631-632）、`LocalizedStrings` 2 行（:493-494） |
| `Dialogs/CompressSettingsWindow.axaml.cs` | 删 `ParallelCompressDegree` 快照（:276-277，含注释） |
| `Views/MainWindow.axaml.cs` | 删 `cvm.ParallelCompressDegree = dialog.ViewModel.ParallelCompressDegree`（:249） |
| `Services/CompressFlow.cs` | 删 `ParallelCompressDegree = vm.ParallelCompressDegree`（:99） |
| `Models/AppSettings.cs` | 删 `ParallelCompressDegree` 字段与注释（:103-104；旧 `settings.json` 里的值被忽略，`Load()` 未启用 `UnmappedMemberHandling.Disallow`，无害） |
| 三语 `strings.*.json` | 删 `Compress_ParallelDegree` / `Compress_ParallelDegree_Hint`（各文件 :47-48，三语同步，否则 `AllThreeLanguages_HaveSameKeySet` 失败） |

> **保留** `ArchiveOptions.ParallelCompressDegree`（`Core/Abstractions/ArchiveEngine.cs:168`）与 `CompressRequest.ParallelCompressDegree`（`Core/Services/CompressService.cs:103`，默认值改 `0` 见 Task 2）—— 引擎层输入，`tests/MantisZip.Tests/Engines/ParallelCompressTests.cs`（显式 3/1）与 `scripts/bench-zip-mt.cs` 仍在使用，**显式值仍照旧生效**。
>
> **连带（评审 D4）**：Task 2 改了默认规则后，`ArchiveEngine.cs:164-167` 与 `ZipEngine.cs:4342-4345` 的 XML 注释（「0 = 取 ProcessorCount」）必须一并更新，否则又是一处静默漂移点。
>
> **自查**：`rg -n "ParallelDegreeInput|ParallelDegreePanel" src`、`rg -n "ParallelCompressDegree" src/MantisZip.UI.Avalonia`、`rg -n "ZipMultiThreadCheck_IsCheckedChanged" src` 均应为 0 命中（Core 侧仅引擎/请求字段）。

### Task 2 — 引擎侧：默认组数 `clamp(核数,4,16)` + 方案 C（并行度解耦，未验证）

> **定调（2026-10-18 实测拍板）**：默认组数 = **`clamp(核数, 4, 16)`**（本机 = 16，与现默认一致；低核下限 4 避开已证实的 deg=2 陷阱）。方案 C 保留但**必须标注「未验证」** —— 默认路径下是 no-op，仅在「核数 > 16」或「文件数 < 组数」时生效。

| 文件 | 改动 |
|------|------|
| `Core/Engines/ZipEngine.cs:4346` `ResolveParallelCompressDegree` | 默认规则 `<=0 → clamp(ProcessorCount, 4, 16)`（原为 `ProcessorCount` 无上下限）；**显式正数 1..16 仍照旧生效**（保 `degree=1` 回退语义，bench 脚本与测试依赖） |
| `Core/Services/CompressService.cs:103` | `CompressRequest.ParallelCompressDegree` 默认 `ProcessorCount` → `0`（统一走引擎默认规则，避免请求层/引擎层各有一套默认值） |
| `Core/Engines/ZipEngine.cs:4103` ⚠ **未验证** | `compr.CustomParameters["mt"] = batchIndex.HasValue ? Math.Max(1, Environment.ProcessorCount / Math.Max(1, batchCount ?? 1)).ToString() : "on";`（方案 C：组内线程数 = 核数/组数） |
| `ArchiveEngine.cs:164-167` / `ZipEngine.cs:4342-4345` | 同步更新 XML 注释为新语义（「0 = 引擎默认 = `clamp(核数,4,16)`」） |

**依据（Task 0 实测，本机 20 核）**：`deg=16`（16 组 × `mt=off` = 16 线程）为最优（text 0.79s / mixed 0.97s）；`deg=8` 慢 21–30%（text 0.96 / mixed 1.26）；`deg=2` 最差（text 3.07 / mixed 3.59）；`deg=1`（单组 `mt=on`）在 text 上慢 ~1.8x；`deg=0`（现默认 = cap 16）≈ `deg=16` → **定调 `clamp(核数,4,16)` 后本机默认路径不变，删旋钮本身即纯收益**。

**「未验证」的确切含义（务必保留此标注）**：7z 的 `mt` 只在**单个文件内部**并行；同组内多个小文件时仍顺序处理。本机语料（200×250KB）实测单组 `mt=on` 只用约 1 个核（2.60s vs 16 组 0.79s）。故方案 C 的收益**只可能**出现在「核数 > 16」或「文件数 < 组数（少量大文件）」场景，需用大文件语料验证：

```powershell
# 用含数百 MB 级单文件的目录作语料，观察 mt 公式的 avgCores / 耗时差
dotnet run scripts\bench-zip-mt.cs -- --corpus <大文件目录> --degrees 0,1 --reps 3 --report .buildout\bench-bigfile.txt
```

若验证显示无收益，方案 C 可原样保留（无害 no-op）或删除，但**不得**作为性能依据写进文档 / 发布说明。

**公式正确性（评审补充已核对）**：两处调用点传入的 `batchCount` 是**实际组数** `groups.Count`（`ZipEngine.cs:2015` / `:3046`），且为整除（向下取整），故 `组数 × mt` 不会超过核数、不会过度订阅；`Math.Max(1, …)` 兜底「组数 > 核数」的配置（显式 degree、或核数很少）。

**测试**：`ParallelCompressTests` 使用显式 1/3/4 → 不受影响；已核对**无**测试断言 `ResolveParallelCompressDegree(0)` → 补一条 `ResolveParallelCompressDegree_Zero_UsesEngineDefault`，断言 `clamp(ProcessorCount,4,16)`（本机 20 → 16），把默认语义钉住。

### Task 3 — 改动 B 暂缓（2026-10-18 双机数据后改判）：Phase 1 三开关保持独立

> **决策**：**暂缓**「多线程压缩依赖自适应压缩」的最终拍板。`MultiThreadedCompression` 与 `AdaptiveCompression` 在 Phase 1 引擎 / 请求 / UI 三层**保持互相独立（无门控代码，同现状）**；本 Task **无代码改动**，只记录决策 + 留回归护栏。**最终策略待 Task 12 本机基准测试按机器定夺**（检测到 MT 负收益 → 默认不勾选 + 常驻科普提示，不做强制门控）。

**双机证据（Task 0 实测，默认组数下 deg=0）**

| 语料 | 20 核机 | 8 逻辑核机（i7-3770） | 结论 |
|------|---------|---------------------|------|
| text：B（仅MT）vs D（全关） | B **0.88s** < D 0.95s（MT 快 7%） | B **2.66s** > D 1.87s（MT **慢 42%**） | **方向反转** |
| mixed：B vs A（仅自适应） | B **0.98s** < A 1.18s（MT 快 17%） | B **3.90s** > A 2.85s（MT **慢 37%**） | **方向反转** |
| media：A vs B vs D | A 0.22s < B 0.27s < D 1.22s | A 0.60s < B 1.00s < D 2.62s | 一致（A 最优） |
| B/C 压缩率优势 | text +15%、mixed +2.1% | text +15%、mixed +2.1% | 一致（硬件无关） |

**为什么暂缓而不是「取消」或「恢复门控」**

- 原「取消」拍板（曾写为「已证伪」）仅基于 20 核机 → 对弱 CPU 用户片面（其上用户 2026-10-16 原始观察成立）。
- 原改动 B（自适应关 ⇒ MT 强制关）会对强 CPU 用户片面（剥夺 text/mixed 真实收益，含压缩率 +15%）。
- **任何一刀切静态策略都只对一半硬件正确** → Phase 1 不引入门控（零代码改动，风险最低），Phase 2 用基准测试按机器给答案。

**随之关闭的评审项**（关闭理由从「改动 B 取消」改为「Phase 1 无门控代码」，结论不变）

- **A1**（只做 UI 门控会漏遗留配置/CLI）→ 关闭：Phase 1 不做夹取，无遗漏可言。
- **A2**（自适应门控与 ZIP32 超限双重置灰互相覆盖）→ 关闭：MT 只剩 ZIP32 一个门控，现有 `UpdateTotalSourceSize` 逻辑不动。
- **C3**（「意图 vs 生效值」持久化语义）→ 关闭：直接用 `AppSettings.MultiThreadedCompression`。
- **C4**（面板侧联动改动点）→ 关闭：`ZipMultiThreadCheck_IsCheckedChanged` 随组数面板一并删除（Task 1）。
- **A3 的镜像风险**：删除时必须**同时**删 axaml 事件属性与处理器，否则 XAML 编译失败（已写入 Task 1 表格与自查）。

**回归护栏（并入 Task 8 验收；Phase 1 期间有效）**

- 关闭「自适应压缩」**不影响** ZIP 面板「多线程压缩」的勾选与生效值（`CompressRequest.MultiThreadedCompression == AppSettings.MultiThreadedCompression`）。
- `rg -n "MultiThreadedCompression\s*&&\s*.*AdaptiveCompression" src` → 0 命中（防 Phase 1 期间误加依赖；Task 12 落地后此护栏按最终策略修订）。
- 遗留 `settings.json`（`MT=true` & `AdaptiveCompression=false`）：压缩请求里 MT 仍为 `true`（20 核机实测该组合更快；8 逻辑核机上用户可自行关闭或跑基准测试）。
- Phase 1 **不引入**任何「MT 依赖自适应」的夹取/门控/默认值播种逻辑。

### Task 4 — 隐藏「魔数检测」（两处）

| 位置 | 改动 |
|------|------|
| `ViewModels/SettingsWindowViewModel.cs:189` | `IsSmartDetectVisible => AdaptiveCompression;` → `=> false;  // Phase 2：实现魔数检测后改回` |
| `Controls/DynamicFormatOptionsPanel.axaml:71` | `SmartDetectCheck` 加 `IsVisible="False"` + `<!-- Phase 2：实现后移除 -->` |
| `Controls/DynamicFormatOptionsPanel.axaml.cs:220` | `AdaptiveSmartDetect` getter 恒返回 `false`（控件虽在但已隐藏，避免值继续透传造成「以为生效」） |

> **保留** `AppSettings.AdaptiveSmartDetect`、`CompressRequest`/`ArchiveOptions.AdaptiveSmartDetect`、三语 `Settings_AdaptiveSmartDetect*` / `FormatOptions_ZIP_SmartDetect`。
>
> **评审 C2（隐藏不对称，已确认可接受）**：设置窗口仍会把 `AdaptiveSmartDetect` 写回（`SettingsWindowViewModel.cs:1644`），但控件已隐藏 → 用户无法改动 → 等价 no-op，且**保留用户原值**正好供 Phase 2 恢复。故**不**把设置窗口侧也恒 false（那会丢原值）。压缩对话框侧恒 false 的理由不同：那里的值会进请求、Phase 2 生效，必须保证「隐藏期间不生效」。

### Task 5 — 隐藏「自适应规则」（设置窗口卡片④）

| 文件 | 改动 |
|------|------|
| `ViewModels/SettingsWindowViewModel.cs:198` | `IsUserRulesSectionVisible => AdaptiveCompression && !MultiThreadedCompression;` → `=> false;  // Phase 2` |

> 保留 `AppSettings.AdaptiveOverrides`、`SettingsWindowViewModel.AdaptiveOverrides` 集合与 `AddRule`/`EditRule`/`DeleteRule` 命令、`Dialogs/EditAdaptiveRuleDialog.*`（Phase 2 复用）。卡片隐藏后这些成员暂不可达，属预期。
>
> 注意：同一表达式还用于「格式目录」卡片（`IsFormatCatalogSectionVisible`，:201 → `Views/SettingsWindow.axaml:176`），**本次不动它**（Task 6 只改其中的 `[hex]` 显示）。

### Task 6 — 隐藏自定义格式「魔数 hex」

| 文件 | 改动 |
|------|------|
| `Dialogs/AddCustomFormatDialog.axaml:25` | `MagicHexTextBox` 加 `IsVisible="False"` + `<!-- Phase 2：实现后移除 -->`（注：该 TextBox 是 `StackPanel` 的直接子元素，不是 Grid「行」，直接隐藏即整块消失） |
| `ViewModels/SettingsWindowViewModel.cs:2214` | `MagicHexDisplay` 恒返回 `""`（格式目录列表里不再显示 `[FFD8FFE0]`）——⚠ 原计划写的 :2187 已漂移 |

> 保留 `FormatDefinition.MagicHex` 字段与 `AddCustomFormatDialog.MagicHex` 属性（Phase 2 复用）。编辑既有格式时 `SetExistingValues`（:2134）仍会回填隐藏框并原样存回，无害。

### Task 7 — 文案与文档同步

- **Phase 1 新增 1 个三语 key**：`Compress_MT_BenefitHint`（ZIP 面板「多线程压缩」下方的**常驻科普提示**：「收益与 CPU 配置相关：多核高配置提升明显；核心数较少时收益有限，甚至可能变慢。」）。理由：提示本身可独立成立（不依赖基准测试），且弱 CPU 用户在 **Phase 1** 就会遇到「MT 默认开着但可能更慢」的风险窗口，提前保护优于等 Phase 2 与基准测试捆绑上线（2026-10-19 用户决策提前）。改动 B 暂缓后，「MT 依赖自适应」的说明行仍不需要。Task 12 的常驻提示与此**为同一行**（Phase 2 不再新增）。
- 删除三语 key `Compress_ParallelDegree` / `Compress_ParallelDegree_Hint`（Task 1）。
- **`AGENTS.md`（评审 A4，原指令不成立）**：AGENTS.md **没有**「加速」段落，也**从未提及**「并行压缩组数」→ 原指令「改写加速段落 + 删除组数描述（含 `DynamicFormatOptionsPanel` 的 7z 面板说明）」全部作废。实际动作：
  - **不要动** `AGENTS.md:117-124`（「7z 多线程压缩（mt=on）」讲的是 `SevenZipMultithreaded`，与本次删除无关）
  - `AGENTS.md:174` Controls 列表行（`DynamicFormatOptionsPanel`（压缩格式动态选项，7z 固实块等））保持准确，无需改
  - **新增一小节**（建议放在「7z 多线程压缩（mt=on）」之后）：「ZIP 压缩加速（自适应 / 多线程 / 引擎侧组数）」——写明：三开关 Phase 1 **互相独立**（MT 与自适应无门控依赖；2026-10-18 双机实测表明 MT 收益与 CPU 相关——强 CPU 有收益、弱 CPU 可能负收益，最终策略待 Phase 2 本机基准测试，可附双机数据）；组数已从 UI 移除（引擎默认 `clamp(核数,4,16)`，「组内线程数 = 核数/组数」未验证）；魔数检测与自适应规则 UI 暂隐藏（Phase 2 实现）。同时点明 **ZIP 的 MT 路径与 7z 的 `mt=on` 是两件事**（前者按「纯压缩组 / 混合组」分派并用 `mt=off` 的 N 组并行，后者是 7z.dll 原生线程），避免读者把本节与上面那节混为一谈
- `RELEASE_NOTES.md:19`（v0.5.2 宣传点含「可设置并行压缩组数」）→ 下一版 notes 必须写明：**移除组数旋钮**（引擎自动 `clamp(核数,4,16)`）；**不要**写「多线程依赖自适应」（该改动已暂缓，Phase 1 无门控）；可补实测结论（ZIP 多线程收益与 CPU 相关：20 核机 text/mixed 有 10–20% 收益且压缩率更好，8 逻辑核机净负收益，Phase 2 将以本机基准测试适配；对已压缩数据无增益）。
- `docs/prototypes/compress-format-options-panel.html` → 页首加 Phase 标注（Phase 1 隐藏项 / Phase 2 恢复项），避免原型与实际不符。
- `docs/PLAN.md` 本计划行补「2026-10-18 评审修订」要点。
- `docs/progress-avalonia-detail.md` 记录本次变更（规则 3）。

### Task 8 — 验证

- `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`（规则 12）
- `dotnet build src\MantisZip.Core\MantisZip.Core.csproj`
- `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`（Task 2 的引擎改动 + 新增默认值单测必须全绿）
- `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`（**注意规则 17**：XAML 改动需 `--no-incremental` 全量重建，增量构建会掩盖 `AVLN3000`）
- **after 对照 bench（评审 B1）**：代码改动后按 Task 0 完全相同参数复跑（`--profile text/media/mixed --degrees 0,1,2,4,8,16 --reps 3 --report .buildout\bench-after-*.txt --json .buildout\bench-after-*.json`）。**预期**：本机 `--degrees 0`（新默认 = `clamp(20,4,16)` = 16 组）应与 before 的 `deg=16` 列重合（text B ≈0.79s / mixed ≈0.97s / media ≈0.27s）；偏差 >10% 视为噪声或实现有误，需复跑确认。
- ~~同步更新引用了被删成员的测试（`CompressSettingsViewModelTests` 等）~~ → **已核对：无任何测试引用** `ParallelCompressDegree` / `Compress_ParallelDegree` / `SmartDetectCheck`，也无断言 `ResolveParallelCompressDegree(0)`；改为补 Task 2 列出的新单测（Task 3 无代码改动 → 无新单测）。

**Phase 1 验收清单**

- [ ] 压缩对话框 ZIP 面板不再出现「并行压缩组数」；勾选多线程时也不再出现
- [ ] 默认路径速度不低于改前（Task 0/8 before/after：本机 `deg=0` 应与 before 的 `deg=16` 重合，text ≈0.79s / mixed ≈0.97s）
- [ ] 低核数默认组数无回退（Task 0 Step 3 **8 逻辑核机已测：deg=2 陷阱依旧、无新塌陷**；纯 2–4 物理核仍待做；若回退 → 退化为 `min(核数,16)`）
- [ ] **三开关互相独立（改动 B 暂缓的 Phase 1 回归护栏）**：关闭「自适应压缩」后，ZIP 面板「多线程压缩」的勾选状态与生效值**均不变**；请求里 `MultiThreadedCompression == AppSettings.MultiThreadedCompression`
- [ ] **遗留 `settings.json`（`MultiThreadedCompression=true` & `AdaptiveCompression=false`）**：MT 仍勾选且生效，请求里 `MultiThreadedCompression=true`（20 核机实测该组合 text/mixed 更快；弱 CPU 上可能负收益——Phase 1 不干预，Task 12 处理）
- [ ] `rg -n "MultiThreadedCompression\s*&&\s*.*AdaptiveCompression" src` → 0 命中（防未来误加依赖）
- [ ] **超 4GB**：MT 勾选框禁用 + 灰字提示显示；退出超限后**恢复原勾选**（`_mtCheckedBeforeLimit` 逻辑），全程无互相覆盖
- [ ] `rg -n "ZipMultiThreadCheck_IsCheckedChanged" src` → 0 命中（axaml 事件属性与处理器**同时**删除，无 XAML 编译残留）
- [ ] 设置窗口「加速」页与压缩对话框 ZIP 面板都不再出现「魔数检测」
- [ ] 设置窗口「加速」页不再出现「自适应规则」卡片
- [ ] 自定义格式对话框不再出现「魔数 hex」输入框；格式目录列表不再显示 `[hex]`
- [ ] `settings.json` 旧字段仍可加载（不抛异常、不丢其它设置）
- [ ] 全量构建 + 测试通过；bench before/after 数据已归档

## Phase 2：实现（5-7h，Phase 1 发布后启动；原 3-4h + Task 12 基准测试约 +2-3h）

### Task 9 — 实现魔数检测

- `Core/Utils/ZipEntryClassifier.cs`：新增 `FileFormat` → 「已压缩」集合映射；`GetAdaptiveLevel` 增 `smartDetect` 形参。⚠ **实际有两个重载**（3 参 :57 / 4 参 :73）→ 统一为 5 参（`filePath, userLevel, adaptive, smartDetect, customStoreFormatIds`），删除或收敛 3 参重载（Core 内 4 个调用点，无测试引用）。
- 头部读取：**先按扩展名判定，仅「可压缩」且文件 ≥ 阈值时才读头**（分拣是**单线程预扫**（`ZipEngine.cs:1899` / `:2930`），万级文件串行多出万次 open/seek 会明显拖慢）；`>= 64B` 即可满足 `FileFormatDetector.Detect` 的最低要求，取 4KB；**任何 IO 异常 → 视为未命中并回落扩展名规则**，不得因源文件被占用而中断压缩。写法参照 `ArchiveEngineFactory.GetEngineByMagic`（`ArchiveEngine.cs:586-606`）。
- 接线 4 个调用点：`ZipEngine.cs` 1899 / 2930 / 3262 / 3795（传 `options.AdaptiveSmartDetect`）。注：3262/3795 的 `adaptive` 传的是字面量 `true`，外层已有 `options.AdaptiveCompression &&` 守卫（**不是 bug**，勿顺手改语义）。**2026-10-19 统一**：3262（串行写入）已由 3 参改 4 参（传 `MultiThreadedStoreFormatIds`），与 MT 分拣及 `AddToArchiveAsync`（3795）同口径 —— 单独启用自适应时自定义仅存储格式同样生效，不再「时灵时不灵」。
- **一致性（评审）**：MT 分拣（1899/2930，4 参）+ 写入（3260/3795）+ 单组串行路径必须同口径 —— `ZipEngine.cs:3782-3788` 的注释记录过「分拣说 Store、写入却 Deflate」的事故。
- **待定项**（启动 Phase 2 时拍板）：
  1. 「已压缩」集合边界 —— `Pe`（exe/dll 其实可压）、`Sqlite`、`Svg`、`Tar`（未压缩归档）是否计入？
  2. 「大文件」阈值：常量（如 64KB）还是新设置项？
  3. 冲突方向：扩展名说可压 + 魔数说已压 → Store（无争议）；反过来（`.jpg` 实为文本）是否改回压缩？
- 收益提示：只对**无扩展名 / 扩展名骗人 / 改名残留**文件有增益；正常命名的照片视频已被扩展名规则 Store，不受影响。
- 测试：`.dat` 实为 JPEG → Store；阈值边界；扩展名/魔数冲突；读头失败回落。

### Task 10 — 实现自适应规则

- 规则需要穿过链路才能生效：`CompressRequest` / `ArchiveOptions` 新增规则字段（`List<AdaptiveOverrideRule>`）→ `ZipEngine` 分拣时调用 `AdaptiveRuleMatcher.ResolveLevel`（该函数已实现且**优先级语义已写好**：用户规则 > 内置分类）。
- **SSOT 必须指定（评审 C6）**：`AdaptiveRuleMatcher.ResolveLevel` 用 `CompressionCoefficients.ClassifyByExtension`，与 `ZipEntryClassifier.CompressedExtensions` 是**两套并行的「已压缩」判定**。二选一写死：① `CompressedExtensions` 改为由 `CompressionCoefficients` 驱动；② 明确两套并存并加同步测试。否则串行路径与 MT 路径会给出不同结果。
- 需设计：规则的 `FormatIds` → 扩展名/魔数匹配（`FormatIds` 经 `FormatCatalog.GetById` 查 `Extensions`，**扩展名带点** `.jpg`，与 `Path.GetExtension` 结果口径一致，已核对）；与 `ZipEntryClassifier` 扩展名分类的先后关系；与多线程 Store 分组的交互。
- **⚠ 行为变更（评审 C5，用户可见）**：`AppSettings.GetDefaultAdaptiveOverrides()`（:333-338）的 4 条默认规则（图片/视频/音频/压缩包 → Store，全部 `Enabled=true`）**已对所有老用户持久化** → 规则一旦生效，压缩输出会变（`CompressedExtensions` 未含 `wav`，且含 `svg`/`tar` 等差异，需逐条比对）。处置：释放说明 + 三语说明「默认规则开始生效」；`ResetDefaultsCommand` 保留为清理路径；必要时把「默认规则集」版本化（迁移时按需重写）。
- 测试：规则命中/未命中/禁用规则/多条规则优先级/默认规则集快照。

### Task 11 — 恢复 Phase 1 隐藏的 UI

「Phase 2 恢复清单」（Task 4/5/6 的 `false` 门控逐条改回，并接上真实实现）：

| 位置 | 恢复动作 |
|------|----------|
| `SettingsWindowViewModel.IsSmartDetectVisible`（:189） | `false` → `AdaptiveCompression` |
| `DynamicFormatOptionsPanel.axaml` `SmartDetectCheck`（:71） | 移除 `IsVisible="False"` 与占位注释 |
| `DynamicFormatOptionsPanel.AdaptiveSmartDetect` getter（:220） | 恒 `false` → 读控件 |
| `SettingsWindowViewModel.IsUserRulesSectionVisible`（:198） | `false` → `AdaptiveCompression && !MultiThreadedCompression` |
| `AddCustomFormatDialog.axaml` `MagicHexTextBox`（:25） | 移除 `IsVisible="False"` |
| `SettingsWindowViewModel.MagicHexDisplay`（:2214） | `""` → `[hex]` 显示 |
| 自适应帮助文本 `AdaptiveHelp_Markdown`（三语） | **重新加回「## 魔数检测」段落**（2026-10-19 实施时先行删除，等 Task 9 功能落地后恢复原文） |
| 设置窗口「加速」页 魔数检测 的「自适应关闭时禁用」联动（原「改动 C」） | 重新加回 |

> **不适用**：「MT 依赖自适应压缩」（原 Task 3）已于 2026-10-18 **暂缓**（双机数据矛盾），Phase 1/2 均不引入硬门控 —— 三开关保持互相独立；MT 收益适配改由 **Task 12 基准测试**（软提示 + 默认值建议）承担。设置窗口 `SaveChanges` 的 `AdaptiveSmartDetect` 写回（:1644）在 Phase 1 未改动 → 无需恢复动作。

### Task 12（新增 · 2026-10-18 双机复测后追加）— 本机基准测试 + MT 收益适配

> **动机**：Task 0 双机实测证明 MT 收益与硬件强相关（20 核机有收益 / 8 逻辑核机负收益），任何一刀切静态策略都片面 → 用「每台机器自己测一次」给出数据驱动答案，替代已暂缓的改动 B 硬门控。

**功能设计**

| 组件 | 内容 |
|------|------|
| 测试逻辑 | 设置 → 加速页 → 「运行本机基准测试」按钮（**手动触发，不自动跑**）；合成小语料（复用 `scripts/bench-zip-mt.cs` 的 `Corpus.GenerateText` 思路精简，目标 20×250KB ≈ 5MB，写入 `%TEMP%` 临时目录，测完删除）；用 `ZipEngine` 对比「串行（`MultiThreadedCompression=false`）vs MT（`true` + 引擎默认组数）」各 1 次（先预热不计时），目标 **< 20 秒**完成 |
| 结果判定 | `ratio = 串行耗时 / MT耗时`；`ratio > 1.05` → 「本机 MT 有收益」；`ratio < 0.95` → 「本机 MT 负收益」；其间 → 「收益不明显」 |
| 持久化 | `AppSettings` 新增 `MultiThreadedBenchRatio`（double?，null = 未测）+ `MultiThreadedBenchTimestamp`（DateTime?）；**不引入意图/生效值二分**，只存测量结果 |
| 默认值播种 | 首次打开压缩对话框时：若 `MultiThreadedBenchRatio` 非 null 且 < 0.95 → ZIP 面板 MT **默认不勾选**（无论 `AppSettings.MultiThreadedCompression` 旧值；用户改动后仍直存直用）；≥ 0.95 或未测 → 维持 `AppSettings` 现值 |
| 常驻提示（三语 key） | MT 复选框下方灰字常驻提示（仿 `ZipMtOverLimitHintText` 的样式，`:76-81` 旁新增 `TextBlock`）：**「收益与 CPU 配置相关：多核高配置提升明显；核心数较少时收益有限，甚至可能变慢。」** —— **Phase 1 已加**（key `Compress_MT_BenefitHint`，见 Task 7；2026-10-19 用户决策提前至 Phase 1，与基准测试解耦），本 Task 保留不重复加 |
| 动态结果提示 | 设置页基准测试结果区显示本机实测结论（如「本机实测：开启多线程约慢 1.4x，已建议默认关闭」）+ 「重新测试」按钮；**不在压缩对话框叠加第二条提示**（避免文案堆叠，常驻科普 + 设置页数据已足够） |
| 进度反馈 | 测试期间设置页按钮旁显示「测试中…（约 15 秒）」+ 禁用重复点击；异常（磁盘满等）→ 弹错并清理临时文件，不写入 ratio |

**三语 key（规则 13，新增）**

- `Compress_MT_BenefitHint`（常驻科普提示，上表文案）
- `Settings_RunBench` / `Settings_BenchRunning` / `Settings_BenchResult_Fast` / `Settings_BenchResult_Slow` / `Settings_BenchResult_Neutral` / `Settings_BenchRetry`（设置页按钮与结果）

**边界与风险缓解**

- **插电/电池、后台负载**差异会导致单次测量波动 → 结果带时间戳，UI 提示「结果基于测试时的系统状态」；提供重新测试。
- **不做硬件白名单**（不按 `ProcessorCount` 猜测）——Task 0 已证明核数不是唯一变量（同为少核，较新 CPU 可能介于两机之间），以实测为准。
- 测试语料**不含媒体**（media 语料 A/D 差异由自适应 Store 解释，与 MT 无关；text 语料已足够区分 MT 正/负收益，两机数据均如此）。

**测试**：基准服务单测（ratio 判定三分支）；默认值播种逻辑单测（ratio < 0.95 → 不勾选）；三语 key 同步校验。

**验收**：8 逻辑核机上跑基准 → 得到「负收益」结论 → 重新打开压缩对话框 MT 默认不勾选 + 常驻提示可见；20 核机（或模拟 ratio > 1.05）→ 默认勾选行为不受影响。

## 风险

| 风险 | 级别 | 缓解 |
|------|------|------|
| 方案 C（`mt = 核数/组数`）收益**未验证** → 若被当作依据写进文档/发布说明会留下错误结论 | 🟡 中 | Task 2 保留「未验证」标注 + 给出大文件语料验证命令；默认路径下明确为 no-op；文档只写「组数已自动」不写提速 |
| 删 MT 处理器时遗漏 axaml 事件属性（或反过来）→ XAML 编译失败（评审 A3 的镜像） | 🟡 中 | Task 1 要求两者**同时**删除 + `rg -n "ZipMultiThreadCheck_IsCheckedChanged" src` 0 命中 + Task 8 用 `--no-incremental` 全量构建 |
| 本计划性能结论与 2026-10-05 bench 相反 → 默认组数无依据 | 🟢 低（**已闭环**） | Task 0 **已实跑**并定调 `clamp(核数,4,16)`；旧 bench 差异成因（未扫 deg=16、当时 MT 默认关）已写入 Task 0 Step 2 |
| 低核数（2–4 核）默认组数未实测 | 🟢 低（评审 B2，**部分闭环**） | 8 逻辑核机（4C/8T）三语料复测已做（Task 0 复测小节），deg=2 陷阱依旧、未见新塌陷；纯 2–4 物理核仍未测 → 若日后发现回退，退化为 `min(核数,16)`（1 行） |
| 「MT 收益因 CPU 而异」——两台电脑数据相反，任何一刀切策略都片面 | 🟡 中（**2026-10-18 新增**） | 改动 B 暂缓（Phase 1 无门控）；Phase 2 **Task 12 本机基准测试**按机器给默认值 + 常驻科普提示，不做硬件白名单猜测 |
| Phase 2 激活默认规则 = 用户可见输出变化 | 🟡 中（评审 C5） | 释放说明 + 三语说明 + 保留 `ResetDefaults` |
| 删除 `AppSettings.ParallelCompressDegree` 后旧 `settings.json` 反序列化异常 | 🟢 低 | `Load()` 未启用 `UnmappedMemberHandling.Disallow`，未知字段被忽略；验收清单已含「旧 settings.json 可加载」 |
| XAML 隐藏门控（`IsVisible="False"`）被后续开发当成「无用代码」清掉 | 🟡 中 | 每处加 `<!-- Phase 2：实现后移除 -->`（**XAML 必须用 XML 注释，不是 `//`**）+ 本计划「恢复清单」 |
| 规则 17 陷阱：增量构建掩盖 `AVLN3000` | 🟡 中 | Task 8 强制 `--no-incremental` |
| Phase 2 拖太久，隐藏的 UI 长期缺席 | 🟡 中 | Phase 2 共 5-7h（Task 9-11 仍为 3-4h，Task 12 约 +2-3h）且基础设施（`FileFormatDetector` / `AdaptiveRuleMatcher`）已存在；可拆两次发布推进 |
| 删除三语 key 造成三语 key 集不同步 | 🟢 低 | 三语同步删除；`AllThreeLanguages_HaveSameKeySet` 会兜住 |
| 文档漂移（AGENTS.md 无该段落 / 行号漂移） | 🟢 低 | Task 7 按实测改写；引用行号已逐条核对 |

## 修订记录（2026-10-18 评审 + 同日双机复测修订）

| 编号 | 评审发现 | 处置 |
|------|----------|------|
| A1 | 改动 B 只做 UI 门控，引擎只认 `options.MultiThreadedCompression`（`ZipEngine.cs:3960`）→ 遗留 `settings.json` / CLI 仍走慢路径 | 新增决策行 + Task 3.1 引擎边界夹取；`_mtUserIntent` 承载意图 → **已关闭**（2026-10-18 改动 B 暂缓，Phase 1 不做夹取） |
| A2 | `UpdateTotalSourceSize`（`:379`/`:377`）无条件写 `IsEnabled`/恢复勾选，与自适应门控互相覆盖 | Task 3.2 统一 `RefreshZipMtState()`（3 输入 / 4 调用点），删除 `_mtCheckedBeforeLimit` → **已关闭**（MT 只剩 ZIP32 一个门控） |
| A3 | Task 1 只删 `ZipMultiThreadCheck_IsCheckedChanged` 却保留 axaml 事件属性（`:80`）→ XAML 编译失败 | Task 1 改为**保留处理器骨架**（删面板显隐 2 行），Task 3.2 填充意图记录 → **再修订**：改动 B 暂缓（Phase 1 无门控）后处理器彻底无用 → 改为「axaml 事件属性与处理器**同时**删除」（Task 1 + 自查 + 风险表） |
| A4 | Task 7 的 AGENTS.md 指令不成立：无「加速」段落、全文无「并行压缩组数」；`:122-123` 讲的是 7z `mt=on` | Task 7 改写为「新增 ZIP 压缩加速小节 + 不动 7z 段落」 |
| B1 | 与 `2026-10-05-n-group-parallel-compress.md:20`（N 组无加速、degree=1 最快）方向相反 | 新增 **Task 0**：同机同语料 before/after 复测，二选一定调默认组数 |
| B2 | 低核数（2–4 核）默认组数未验证 | Task 0 Step 3 + 风险表 |
| C1 | 决策记录「保留 AppSettings 字段」与 Task 1 删字段/删 key 矛盾 | 决策记录行改为「仅保留与魔数/规则相关的部分」，并显式标注删除项 |
| C2 | Task 4 隐藏不对称（设置窗口仍写回 `AdaptiveSmartDetect`） | Task 4 补充说明：保留写回原值（等价 no-op，供 Phase 2 恢复） |
| C3 | 「记住原状态」的持久化语义未定义（跨会话会丢） | Task 3.3：持久化**意图**，生效值 = 意图 && 自适应 → **已关闭**（改动 B 暂缓，Phase 1 无需意图/生效值二分） |
| C4 | 面板侧联动改动点未指定 | Task 3.2 明确 `DynamicFormatOptionsPanel` code-behind（含 axaml 事件属性补齐）→ **已关闭**（该处理器随组数面板一并删除） |
| C5 | Task 10 会激活已持久化的 4 条默认规则 → 用户可见输出变化 | Task 10 增加行为变更处置 |
| C6 | 两套「已压缩」判定并存（`CompressedExtensions` vs `CompressionCoefficients`） | Task 10 要求先指定 SSOT |
| D1 | `MagicHexDisplay` 实际在 `:2214`（原写 `:2187`） | Task 6 更正 |
| D2 | `GetAdaptiveLevel` 有两个重载；`:3260`/`:3795` 的 `adaptive` 是字面量 `true`（有守卫，非 bug） | Task 9 补充 |
| D3 | 读头发生在单线程分拣预扫，万级文件串行 IO 开销 | Task 9 补充「先扩展名后读头 + 失败回落」 |
| D4 | 新默认规则下 `ArchiveEngine.cs:164-167` / `ZipEngine.cs:4342-4345` 注释过时 | Task 1/2 增加注释同步 |
| D5 | 风险表建议的 `//` 注释在 `.axaml` 中非法；Task 6 的「所在行」措辞 | 风险表 + Task 6 更正 |
| D6 | Task 8「同步更新引用了被删成员的测试」实际不存在这种引用 | Task 8 改为补新单测 + after 对照 bench |
| D7 | 漏掉的文档同步（RELEASE_NOTES、原型 HTML）；AGENTS.md 设置分类无需改 | Task 7 补充 |
| D8 | 死属性 `IsAdaptiveContentVisible`；规则 6 的置灰例外未说明 | Task 3.3 顺带删除 + 写明理由 → **随改动 B 暂缓而挂起**：改为「可选清理」（`SettingsWindowViewModel.cs:192` 确认无 XAML 绑定，删不删都不影响行为） |
| E1 | 用户报告两台电脑 Task 0 结论不一致（同日 20 核机拍板后） | 8 逻辑核机（i7-3770 4C/8T）复跑三语料：deg=2 陷阱复现、压缩率优势复现，但 **MT 收益方向反转**（text B 慢 42%、mixed B 慢 37%）→ 数据入 Task 0 新增「8 逻辑核机复测」小节，Step 3 改为部分完成 |
| E2 | E1 导致议题 1「改动 B 取消」不再成立（单机结论片面） | 改判为**暂缓**：头部拍板行、问题表 #2、Phase 1 目标、决策记录 3 行、Task 3、Task 7/8 措辞、风险表、拍板记录同步修订；评审项 A1/A2/C3/C4 关闭理由改为「Phase 1 无门控代码」（结论不变） |
| E3 | 一刀切静态策略（独立或依赖）必然对一半硬件片面 | 新增 **Task 12 本机基准测试 + MT 收益适配**（手动触发、小语料、串行 vs MT 对比、默认值播种、常驻科普提示三语 key）；Phase 2 工时 3-4h → 5-7h；风险表新增「MT 收益因 CPU 而异」 |
| E4 | Task 12「默认值播种」会覆盖用户手动选择：`ratio<0.95 → MT 默认不勾选（无论 AppSettings 旧值）` 与「用户改动后仍直存直用」并存 → 用户手动勾上 MT 并保存后，**下次打开对话框又被改回未勾选**（循环），且无提示（计划明确不在对话框叠加提示） | **Phase 2 开工前须定**（不阻塞 Phase 1）：建议 (a) 播种仅在「用户从未手动改过」时生效（需「用户已表态」标记 —— 即被拒绝的意图/生效值二分，或新增 `MultiThreadedManualSet` 字段）；或 (b) 播种覆盖时在对话框显示灰字说明原因。当前按原设计保留，留待 Task 12 细化 |
| E5 | 常驻科普提示（Task 12 的 `Compress_MT_BenefitHint`）与基准测试捆绑 → 弱 CPU 用户在 Phase 1 期间无保护 | **2026-10-19 用户决策提前到 Phase 1**：提示独立成立、成本极低（1 个三语 key + 1 行 TextBlock），ZIP 面板「多线程压缩」下方常驻灰字。Task 12 保留该提示（Phase 1 已加，不重复），仅新增基准测试入口 |
| E6 | 压缩（`CompressAsync`）串行写入用 3 参 `GetAdaptiveLevel`、添加（`AddToArchiveAsync`）用 4 参 → 同一「自适应压缩」开关在两个入口行为不一致（自定义仅存储格式时灵时不灵） | **2026-10-19 用户决策统一**：3262 改 4 参（传 `MultiThreadedStoreFormatIds`），四个调用点同口径；`IsStoreFormatSectionVisible` 由 `自适应 && 多线程` 改为 `自适应`（与引擎行为对齐，消除「卡片隐藏但值生效」）。对默认配置（无自定义列表）零影响，bench before/after 数据仍有效 |
| E7 | 常驻科普提示只在压缩对话框 ZIP 面板有，「设置 → 加速」页（全局默认 MT 开关）没有 → 弱 CPU 用户在设置页看不到风险提示 | **2026-10-19 用户决策**：设置页同步加同一行常驻灰字，复用 `Compress_MT_BenefitHint`（单一事实来源）。~~连带发现的 desc 措辞冲突待定~~ → **已处理**：`Settings_MultiThreadedCompression_Desc` 与 `AdaptiveHelp_Markdown` 多线程段的「显著提升」全部软化为「以提升」（三语）；`AdaptiveHelp_Markdown` 开头「三个开关」同步改为「两个开关」（魔数段已删） |

## 拍板记录（2026-10-18 实测后 · 同日双机复测修订）

Task 0 基线复测先在 20 逻辑核机跑完（报告 `.buildout\bench-before-{text,media,mixed}.txt`），用户拍板三项；随后用户报告**另一台电脑结论不一致**，同日在 8 逻辑核机（i7-3770 4C/8T）复跑三语料（`.buildout\bench-retest-8lp-{text,media,mixed}.txt`），议题 1 据此改判。计划最终按此定稿：

| # | 议题 | 拍板结果 | 依据 / 影响 |
|---|------|----------|-------------|
| 1 | 改动 B（MT 依赖自适应）存废 | **暂缓（同日由「取消」改判）** | 20 核机：仅 MT 有真实收益（text 0.88s < D 0.95s、mixed 0.98s ≪ D 2.13s，压缩率 +15%）→ 曾拍板取消；**8 逻辑核机方向反转**（text B 2.66s vs D 1.87s 慢 42%、mixed B 3.90s vs A 2.85s 慢 37%，用户 2026-10-16 原始观察在弱 CPU 上复现）→ 一刀切静态策略片面。**Phase 1 维持三开关独立、无门控代码**（零代码改动，A1/A2/C3/C4 关闭结论不变），最终策略待 **Task 12 本机基准测试**按机器定夺（默认值播种 + 常驻科普提示，不做强制门控） |
| 2 | 引擎默认组数 | **`clamp(核数,4,16)`** | 否决 `clamp(核数,2,8)`（deg=8 比 deg=16 慢 21–30%）与「默认 = 1」（text 慢 ~1.8x）；20 核机 = 16 组，8 逻辑核机 = 8 组且 deg=2 陷阱依旧（慢 2.6x）、未见新塌陷（Task 0 Step 3 补测）→ 下限 4 避开陷阱，删旋钮即纯收益 |
| 3 | 方案 C（`mt = 核数/组数`） | **保留，但标注「未验证」** | 默认路径下为 no-op（20 核机 16 组 → `mt=max(1,20/16)=1`）；两机小文件语料均证明单组 `mt=on` 只用约 1 个核，收益需大文件语料验证（Task 2 给出命令）；**不得**写入文档当性能依据 |

**连带修订（本轮 + 双机复测轮）**：Task 1（MT 处理器 + axaml 事件属性同时删除）、Task 2（默认规则 `clamp(4,16)` + 方案 C 标注未验证）、Task 3（改为「暂缓 + Phase 1 无门控 + 回归护栏 + 双机证据表」）、Task 7（Phase 1 不新增三语 key，Task 12 负责新增；AGENTS.md 小节改为「三开关独立 + MT 收益与 CPU 相关」）、Task 8 验收（before/after 预期、三开关独立护栏、`rg` 自查）、**Task 12（新增：本机基准测试 + MT 常驻提示）**、风险表（方案 C 未验证 / 删处理器风险 / 低核部分闭环 / **MT 收益因 CPU 而异**）、头部工时 Phase 1 6-8h → **4-5h**、Phase 2 3-4h → **5-7h**。

**2026-10-19 实施补充**：Task 1/2/4/5/6 已落地（`rg` 自查全 0 命中）；Task 7 修正为**新增 1 个三语 key** `Compress_MT_BenefitHint`（常驻科普提示提前到 Phase 1，与 Task 12 基准测试解耦 —— 理由：提示独立成立、弱 CPU 用户在 Phase 1 即有风险窗口）；Task 12 的「常驻提示」一行标注为 Phase 1 已加、不重复；Task 11 恢复清单**不含**该 key（Phase 1 新加，无需恢复）。

**仍未闭环的验证项**（详见 Task 0 末）：① 纯 2–4 物理核默认组数（8 逻辑核 = 4 物理核已部分回答，2 核未测）；② 20 核机 deg=1 批次的环境噪声（如需引用绝对秒数则 `--degrees 1 --reps 5` 复测）；③ 方案 C 的大文件语料收益；④ 各类弱 CPU 的 MT 负收益幅度是否一致（不猜硬件白名单，Task 12 基准测试按机器实测）。
