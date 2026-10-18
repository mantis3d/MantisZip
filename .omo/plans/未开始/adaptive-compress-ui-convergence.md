# 压缩加速三开关 — UI 收敛（Phase 1）+ 魔数检测/自适应规则实现（Phase 2）

> **状态**: 📋 待实施 | **日期**: 2026-10-16（**2026-10-18 评审修订 + 实测拍板**）| **优先级**: P1 | **预估工时**: Phase 1 = **4-5h**（实测拍板后下调：**改动 B 取消** + 组数规则简化为 `clamp(核数,4,16)`，Task 0 已实跑完成）/ Phase 2 = 3-4h
> **来源**: 用户 2026-10-16 提出「压缩有个选项有两个开关『多线程』和『自适应』，测试发现不启用自适应时开启多线程反而有反作用」→ 实测 + 代码审计后扩展为两阶段计划
> **原型**: [docs/prototypes/compress-format-options-panel.html](../../docs/prototypes/compress-format-options-panel.html)（交互原型，已覆盖两处界面）
> **评审**: 2026-10-18 逐条核对源码，修正 4 处会直接踩坑的事实错误（A1–A4）、1 处与既有 bench 结论冲突（B1）、若干语义/文档遗漏（C/D 项），详见文末「修订记录」
> **实测拍板（2026-10-18）**: Task 0 基线复测已实跑（本机 20 逻辑核，报告 `.buildout\bench-before-{text,media,mixed}.txt`）→ **改动 B 取消**（MT 保持独立）、引擎默认组数 `clamp(核数,4,16)`、方案 C 保留但标注「未验证」；详见文末「拍板记录」，评审项 A1/A2/C3/C4 随之关闭

## 背景

压缩加速面板有三个开关（`自适应压缩` / `魔数检测` / `多线程压缩`），压缩对话框 ZIP 面板另有一个 `并行压缩组数`。2026-10-16 实测 + 全仓代码审计后发现四个问题（2026-10-18 已逐条复核，证据全部成立）：

| # | 问题 | 证据 |
|---|------|------|
| 1 | **「并行压缩组数」是性能陷阱** | 16 核机器把组数设为 2，压缩只用 **1.95 个核**（进程 CPU/墙钟实测），耗时 **5.47s vs 设 16 组的 1.64s（3.3x）**。根因：N 组路径每组强制 `mt=off`（`Core/Engines/ZipEngine.cs:4103`），**并行度 = 组数**（实测 `avgCores ≈ degree`）。且该旋钮对速度**无正收益**（deg=1/2/4/8/16 互有胜负，平台区 2–8）⚠ 与 2026-10-05 bench 结论方向相反，见 Task 0 |
| 2 | ~~**「多线程压缩」在自适应关闭时是负收益**~~ → **已证伪，改动取消** | 原证据：可压缩数据（text 语料）上「仅多线程」比**完全不开多线程**慢 **1.5–1.8x**（机制：MT 路径改用 7z 的 Deflate，无自适应剔除时慢压缩器白跑）。**2026-10-18 实测未在默认组数下复现**：text 上「仅多线程」0.88s **快于**全关 0.95s 与仅自适应 0.97s（压缩率还高 15%）、mixed 上 0.98s ≪ 全关 2.13s；「慢 1.5–1.8x」仅在 deg≤4（用户可设的陷阱档）出现 → **用户 2026-10-18 拍板：取消改动 B**（详见 Task 0 实测结果 + Task 3） |
| 3 | **「魔数检测」是空转开关** | `AdaptiveSmartDetect` 全仓**无任何引擎消费者**：只有 UI 绑定 + `AppSettings` 持久化 + `CompressRequest → ArchiveOptions` 透传；`AdaptiveRuleMatcher.ResolveLevel(..., adaptiveSmartDetect:)` 形参**只写日志、不参与分支**（`Core/Utils/AdaptiveRuleMatcher.cs:30`）。而 `Core/Abstractions/ArchiveEngine.cs:183` 注释写的是「自适应关闭时无效」——语义上该联动，UI 也漏了 |
| 4 | **「自适应规则」也是空转** | `AdaptiveRuleMatcher.ResolveLevel`（唯一会应用规则的函数）**无生产调用点**（仅 `AdaptiveCompressionTests` 引用）；`CompressRequest` / `ArchiveOptions` **根本没有规则字段**。即用户配了规则不生效 |
| 5 | 自定义格式的「魔数 hex」也不生效 | `ZipEntryClassifier.GetAdaptiveLevel` 只读 `format.Extensions`（`Core/Utils/ZipEntryClassifier.cs:73-94`），**从不读 `MagicHex`** |

**Phase 1 目标**：先出一版「UI 不说谎」的可用版本 —— 删除/隐藏空转控件，修掉 #1 这个真实性能坑（删「并行压缩组数」旋钮 + 引擎默认组数改 `clamp(核数,4,16)`，见 Task 1/2）。#2 经实跑**证伪** → **改动 B 取消**（MT 与自适应保持独立，见 Task 3 记录）。
**Phase 2 目标**：实现 #3 #4（魔数检测 + 自适应规则），并把 Phase 1 隐藏的 UI 接回来。

## 决策记录（2026-10-16 用户授权「按你觉得合适的来」+ 2026-10-18 评审补充）

| 决策 | 取值 | 理由 |
|------|------|------|
| 改动 A（删「并行压缩组数」）是否进 Phase 1 | **进** | 实测确认「deg=2 陷阱」（比默认慢 3.5–3.7x），且该旋钮对速度无正收益 → 属真实缺陷修复 |
| 改动 B（多线程依赖自适应）是否进 Phase 1 | **取消（2026-10-18 实测拍板）** | 前提在默认组数下**证伪**：「仅多线程」并不慢于「全关 / 仅自适应」（text 0.88s vs 0.95/0.97s，且压缩率 +15%；mixed 0.98s vs 2.13/1.18s）。强制依赖会白丢真实收益 |
| 引擎侧方案 C（`mt = 核数/组数`）是否进 Phase 1 | **进（4 行改动），标注「未验证」** | 2026-10-16 测量：`mt=N` 被 7z 接受（`-mmt=N`）、端到端 avgCores 恒 ≈12。⚠ **2026-10-18 实测补充**：默认路径（本机 16 组）下 C 是 **no-op**（`mt = max(1, 20/16) = 1`），仅在「核数 > 16」或「文件数 < 组数（少量大文件）」时生效，而这两场景的收益**尚未验证**（本机小文件语料证明单组 `mt=on` 只用约 1 个核）→ 保留但不得当性能依据，验证方法见 Task 2「待验证」 |
| 默认组数取多少 | **`clamp(核数,4,16)`（2026-10-18 实测拍板）** | Task 0 实测否决两个候选：`clamp(核数,2,8)` 相对现默认（cap 16）是回退（deg=8 比 deg=16 慢 21–30%）；`deg=1` 更慢（text ~1.8x）。取 `clamp(核数,4,16)`：保留本机 16 组的最优表现，低核下限 4 避开已证实的 deg=2 陷阱 |
| 自定义格式「魔数 hex」（#5）是否一起隐藏 | **一起隐藏** | 与魔数检测是同一件事，留着同样是骗人 UI |
| 「隐藏」的实现方式 | **用显式 `false` 门控 + Phase 2 注释，不删标记** | Phase 2 = 改回来（1 行），避免重建标记；代价是留少量 dead markup（计划内已列「Phase 2 恢复清单」）。⚠ XAML 内占位注释必须写 `<!-- -->`（不是 `//`） |
| 保留不动的部分 | **仅「与魔数/规则相关」的部分**：`AppSettings.AdaptiveSmartDetect` / `AdaptiveOverrides` / `MultiThreadedStoreFormatIds`、`CompressRequest`/`ArchiveOptions` 透传链路、其余三语 key、`EditAdaptiveRuleDialog`、`AddCustomFormatDialog` | Phase 2 只需「UI 接回来 + 引擎实现」，链路不重做。**`AppSettings.ParallelCompressDegree` 与 2 个组数 key 不在保留范围内（Task 1 要删）** —— 2026-10-18 评审修正：原文「保留 AppSettings 字段」与 Task 1 自相矛盾 |
| ~~「MT 依赖自适应」的生效值由谁计算~~ → **不适用（改动 B 取消）** | **不做任何夹取** | `MultiThreadedCompression` 与 `AdaptiveCompression` 保持**互相独立**；`CompressService.BuildOptions` / `CompressFlow.BuildRequest` 均不引入相关夹取（评审 A1 随之关闭） |
| 「多线程压缩」的持久化语义 | **维持原语义**：`AppSettings.MultiThreadedCompression` 直存直用，**不引入「意图 / 生效值」二分** | 改动 B 取消后无需区分意图与生效值（评审 C3 随之关闭）；UI 显示的就是引擎收到的值 |
| ZIP 面板 MT 的联动条件 | **仅剩 1 个：ZIP32 4GB 超限**（现有 `UpdateTotalSourceSize` 逻辑**保留不动**：超限 → 取消勾选 + 置灰 + 灰字提示；退出超限 → 恢复原勾选 `_mtCheckedBeforeLimit`） | 自适应不再是 MT 的门控（改动 B 取消）→ 不需要 `RefreshZipMtState()` 状态机（评审 A2 随之关闭）。另：删掉组数面板后 `ZipMultiThreadCheck_IsCheckedChanged` 已无联动对象 → Task 1 连带删除该处理器及其 axaml 事件属性 |

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

- [ ] **Step 3: 低核数对照（评审 B2）—— ⚠ 未做**（本机 20 核，无低核环境）

    ```powershell
    dotnet run scripts\bench-zip-mt.cs -- --profile mixed --degrees 0,1,2,4 --reps 3 --report .buildout\bench-before-lowcore.txt
    ```

    在 4 核机器/虚拟机上跑（或据此对照低核默认组数表现）。
    Expected: 记录 2/4 核下的默认组数表现。**定调为 `clamp(核数,4,16)` 后：若低核下出现回退（如 2 核时 4 组超订反而变慢），退化为 `min(核数,16)`（1 行）—— 不再是「默认 = 1」分支。**

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
4. ❌ **改动 B 的前提在默认组数下不成立**：text **B 0.88 < D 0.95 < A 0.97**（且 B 压缩率还高 15%）、mixed **B 0.98 ≪ A 1.18 ≪ D 2.13**。「仅多线程更慢 1.5–1.8x」只在 **deg≤4** 出现（vs D：deg=1 1.58x、deg=2 3.4x、deg=4 1.8x）→ 若按计划强制「自适应关 ⇒ MT 关」，用户在 text/mixed 上会**白丢真实收益**（含 +15%/+2% 压缩率）。
5. ⚠ **方案 C（`mt = 核数/组数`）在本机默认路径上是 no-op**（groups=16 → `mt = max(1, 20/16) = 1`）。其价值取决于「7z 的 `mt` 能否在**单文件内**并行」——本语料（200×250KB 小文件）证明单组 `mt=on` 只用约 1 个核（2.60s vs 16 组 0.79s），故需**大文件语料**（`--corpus <含数百 MB 级单文件的目录>`）才能验证；未验证前不应作为 Phase 1 的必要条件。

**✅ 拍板结果（2026-10-18 用户决策）**

- **(a) 改动 B：取消** —— MT 与自适应保持**互相独立**；Task 3 改为「记录该决策 + 不做任何夹取」。
- **(b) 默认组数：`clamp(核数,4,16)`** —— 本机 = 16 组（与现默认一致，保留实测最优）；低核下限 4 避开已证实的 deg=2 陷阱。
- **(c) 方案 C：保留**（`mt = max(1, 核数/组数)`），但**标注「未验证」** —— 默认路径下为 no-op，仅在「核数 > 16」或「文件数 < 组数」时生效（见 Task 2）。

**⚠ 仍然悬空的验证项（不阻塞 Phase 1，但须在计划里留痕）**

- **Step 3 低核对照未做**（本机 20 核，无 4 核环境）→ `clamp(核数,4,16)` 的低核行为（尤其 2 核时 4 组 > 核数的轻度超订）**仍未实测**；若日后在低核机器上发现回退，退化为 `min(核数,16)` 即可（1 行）。
- **Step 1 的 deg=1 批疑有环境噪声**（该批里与组数无关的 A/D 也整体 +80%）→ 若要引用 deg=1 的绝对秒数，先 `--degrees 1 --reps 5` 复测。
- **方案 C 的收益未验证**（需 `--corpus <含数百 MB 级单文件的目录>`；本机小文件语料下 7z 的 `mt` 近似 no-op）。

---

### Task 1 — 删除「并行压缩组数」控件（UI 全删 + 设置字段）

| 文件 | 改动 |
|------|------|
| `Controls/DynamicFormatOptionsPanel.axaml` | 删 `ParallelDegreePanel`（:96-116，标签 + `ParallelDegreeInput` + 灰色提示），**以及 `ZipMultiThreadCheck` 上的 `IsCheckedChanged="ZipMultiThreadCheck_IsCheckedChanged"`（:80）** —— 该处理器的唯一作用是联动组数面板显隐；改动 B 取消后它没有任何用途（**不是**「留给 Task 3 复用」） |
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

### Task 3 — 改动 B 已取消（2026-10-18 实测拍板）：三开关互相独立

> **决策**：**不做**「多线程压缩依赖自适应压缩」。`MultiThreadedCompression` 与 `AdaptiveCompression` 在引擎 / 请求 / UI 三层**保持互相独立**；本 Task **无代码改动**，只记录决策 + 留回归护栏。

**证据（Task 0 实测，本机 20 核，默认组数下）**

| 语料 | 仅多线程（B） | 仅自适应（A） | 全关（D） | 结论 |
|------|--------------|--------------|-----------|------|
| text 54MB | **0.88s**（7.15x） | 0.97s（6.21x） | 0.95s | MT 更快且压缩率 +15% |
| mixed 103MB | **0.98s**（1.823x） | 1.18s（1.786x） | 2.13s | MT 比 A 快 17%、比 D 快 2.2x |
| media 49MB | 0.27s | **0.22s** | 1.22s | 自适应小胜（已压缩数据 Store 更快，差 0.05s） |

原假设「仅多线程比全关慢 1.5–1.8x」只在 `deg≤4` 复现（deg=1 1.58x / deg=2 3.4x / deg=4 1.8x）—— 那正是「并行压缩组数」旋钮能设出的陷阱档；Task 1 删掉旋钮后该路径不可达 → **强制依赖的代价（丢 text/mixed 的真实收益）大于收益（省 media 上 0.05s）**，故取消。

**随之关闭的评审项**

- **A1**（只做 UI 门控会漏遗留配置/CLI）→ 关闭：不做夹取，无遗漏可言。
- **A2**（自适应门控与 ZIP32 超限双重置灰互相覆盖）→ 关闭：MT 只剩 ZIP32 一个门控，现有 `UpdateTotalSourceSize` 逻辑不动。
- **C3**（「意图 vs 生效值」持久化语义）→ 关闭：直接用 `AppSettings.MultiThreadedCompression`。
- **C4**（面板侧联动改动点）→ 关闭：`ZipMultiThreadCheck_IsCheckedChanged` 随组数面板一并删除（Task 1）。
- **A3 的镜像风险**：删除时必须**同时**删 axaml 事件属性与处理器，否则 XAML 编译失败（已写入 Task 1 表格与自查）。

**回归护栏（并入 Task 8 验收）**

- 关闭「自适应压缩」**不影响** ZIP 面板「多线程压缩」的勾选与生效值（`CompressRequest.MultiThreadedCompression == AppSettings.MultiThreadedCompression`）。
- `rg -n "MultiThreadedCompression\s*&&\s*.*AdaptiveCompression" src` → 0 命中（防未来误加依赖）。
- 遗留 `settings.json`（`MT=true` & `AdaptiveCompression=false`）：压缩请求里 MT 仍为 `true`（实测该组合在 text/mixed 上更快）。
- 若日后仍想省掉 media 上的 0.05s：用 **UI 提示**（如灰字「已压缩数据居多时建议同时开启自适应」）而非强制夹取，**不引入字段依赖**。

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

- **不新增任何三语 key**（改动 B 取消后，「MT 依赖自适应」的说明行不再需要；设置窗口与 ZIP 面板均无需新增文案）。
- 删除三语 key `Compress_ParallelDegree` / `Compress_ParallelDegree_Hint`（Task 1）。
- **`AGENTS.md`（评审 A4，原指令不成立）**：AGENTS.md **没有**「加速」段落，也**从未提及**「并行压缩组数」→ 原指令「改写加速段落 + 删除组数描述（含 `DynamicFormatOptionsPanel` 的 7z 面板说明）」全部作废。实际动作：
  - **不要动** `AGENTS.md:117-124`（「7z 多线程压缩（mt=on）」讲的是 `SevenZipMultithreaded`，与本次删除无关）
  - `AGENTS.md:174` Controls 列表行（`DynamicFormatOptionsPanel`（压缩格式动态选项，7z 固实块等））保持准确，无需改
  - **新增一小节**（建议放在「7z 多线程压缩（mt=on）」之后）：「ZIP 压缩加速（自适应 / 多线程 / 引擎侧组数）」——写明：三开关**互相独立**（MT 不依赖自适应，2026-10-18 实测决策，可附 before 数据）；组数已从 UI 移除（引擎默认 `clamp(核数,4,16)`，「组内线程数 = 核数/组数」未验证）；魔数检测与自适应规则 UI 暂隐藏（Phase 2 实现）。同时点明 **ZIP 的 MT 路径与 7z 的 `mt=on` 是两件事**（前者按「纯压缩组 / 混合组」分派并用 `mt=off` 的 N 组并行，后者是 7z.dll 原生线程），避免读者把本节与上面那节混为一谈
- `RELEASE_NOTES.md:19`（v0.5.2 宣传点含「可设置并行压缩组数」）→ 下一版 notes 必须写明：**移除组数旋钮**（引擎自动 `clamp(核数,4,16)`）；**不要**写「多线程依赖自适应」（该改动已取消）；可补实测结论（ZIP 多线程在文本/mixed 语料有 10–20% 收益且压缩率更好，对已压缩数据无增益）。
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
- [ ] 低核数（≤4 核）默认组数无回退（Task 0 Step 3 **仍待做**；若回退 → 退化为 `min(核数,16)`）
- [ ] **三开关互相独立（改动 B 取消的回归护栏）**：关闭「自适应压缩」后，ZIP 面板「多线程压缩」的勾选状态与生效值**均不变**；请求里 `MultiThreadedCompression == AppSettings.MultiThreadedCompression`
- [ ] **遗留 `settings.json`（`MultiThreadedCompression=true` & `AdaptiveCompression=false`）**：MT 仍勾选且生效，请求里 `MultiThreadedCompression=true`（实测该组合在 text/mixed 上更快）
- [ ] `rg -n "MultiThreadedCompression\s*&&\s*.*AdaptiveCompression" src` → 0 命中（防未来误加依赖）
- [ ] **超 4GB**：MT 勾选框禁用 + 灰字提示显示；退出超限后**恢复原勾选**（`_mtCheckedBeforeLimit` 逻辑），全程无互相覆盖
- [ ] `rg -n "ZipMultiThreadCheck_IsCheckedChanged" src` → 0 命中（axaml 事件属性与处理器**同时**删除，无 XAML 编译残留）
- [ ] 设置窗口「加速」页与压缩对话框 ZIP 面板都不再出现「魔数检测」
- [ ] 设置窗口「加速」页不再出现「自适应规则」卡片
- [ ] 自定义格式对话框不再出现「魔数 hex」输入框；格式目录列表不再显示 `[hex]`
- [ ] `settings.json` 旧字段仍可加载（不抛异常、不丢其它设置）
- [ ] 全量构建 + 测试通过；bench before/after 数据已归档

## Phase 2：实现（3-4h，Phase 1 发布后启动）

### Task 9 — 实现魔数检测

- `Core/Utils/ZipEntryClassifier.cs`：新增 `FileFormat` → 「已压缩」集合映射；`GetAdaptiveLevel` 增 `smartDetect` 形参。⚠ **实际有两个重载**（3 参 :57 / 4 参 :73）→ 统一为 5 参（`filePath, userLevel, adaptive, smartDetect, customStoreFormatIds`），删除或收敛 3 参重载（Core 内 4 个调用点，无测试引用）。
- 头部读取：**先按扩展名判定，仅「可压缩」且文件 ≥ 阈值时才读头**（分拣是**单线程预扫**（`ZipEngine.cs:1899` / `:2930`），万级文件串行多出万次 open/seek 会明显拖慢）；`>= 64B` 即可满足 `FileFormatDetector.Detect` 的最低要求，取 4KB；**任何 IO 异常 → 视为未命中并回落扩展名规则**，不得因源文件被占用而中断压缩。写法参照 `ArchiveEngineFactory.GetEngineByMagic`（`ArchiveEngine.cs:586-606`）。
- 接线 4 个调用点：`ZipEngine.cs` 1899 / 2930 / 3260 / 3795（传 `options.AdaptiveSmartDetect`）。注：3260/3795 的 `adaptive` 传的是字面量 `true`，外层已有 `options.AdaptiveCompression &&` 守卫（**不是 bug**，勿顺手改语义）。
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
| 设置窗口「加速」页 魔数检测 的「自适应关闭时禁用」联动（原「改动 C」） | 重新加回 |

> **不适用**：「MT 依赖自适应压缩」（原 Task 3）已于 2026-10-18 **取消**，Phase 2 也不引入 —— 三开关保持互相独立。设置窗口 `SaveChanges` 的 `AdaptiveSmartDetect` 写回（:1644）在 Phase 1 未改动 → 无需恢复动作。

## 风险

| 风险 | 级别 | 缓解 |
|------|------|------|
| 方案 C（`mt = 核数/组数`）收益**未验证** → 若被当作依据写进文档/发布说明会留下错误结论 | 🟡 中 | Task 2 保留「未验证」标注 + 给出大文件语料验证命令；默认路径下明确为 no-op；文档只写「组数已自动」不写提速 |
| 删 MT 处理器时遗漏 axaml 事件属性（或反过来）→ XAML 编译失败（评审 A3 的镜像） | 🟡 中 | Task 1 要求两者**同时**删除 + `rg -n "ZipMultiThreadCheck_IsCheckedChanged" src` 0 命中 + Task 8 用 `--no-incremental` 全量构建 |
| 本计划性能结论与 2026-10-05 bench 相反 → 默认组数无依据 | 🟢 低（**已闭环**） | Task 0 **已实跑**并定调 `clamp(核数,4,16)`；旧 bench 差异成因（未扫 deg=16、当时 MT 默认关）已写入 Task 0 Step 2 |
| 低核数（2–4 核）默认组数未实测 | 🟡 中（评审 B2，**仍未闭环**） | Task 0 Step 3 待做；不达标则退化为 `min(核数,16)`（1 行） |
| 用户 2026-10-16 的「MT 无自适应 = 反作用」观察被本次实测推翻 → 真实语料下可能仍存在 | 🟡 中 | 改动 B 取消以本机数据为准；若日后在真实语料复现，先 `--corpus <真实目录>` 补测再议，不反向重开改动 B |
| Phase 2 激活默认规则 = 用户可见输出变化 | 🟡 中（评审 C5） | 释放说明 + 三语说明 + 保留 `ResetDefaults` |
| 删除 `AppSettings.ParallelCompressDegree` 后旧 `settings.json` 反序列化异常 | 🟢 低 | `Load()` 未启用 `UnmappedMemberHandling.Disallow`，未知字段被忽略；验收清单已含「旧 settings.json 可加载」 |
| XAML 隐藏门控（`IsVisible="False"`）被后续开发当成「无用代码」清掉 | 🟡 中 | 每处加 `<!-- Phase 2：实现后移除 -->`（**XAML 必须用 XML 注释，不是 `//`**）+ 本计划「恢复清单」 |
| 规则 17 陷阱：增量构建掩盖 `AVLN3000` | 🟡 中 | Task 8 强制 `--no-incremental` |
| Phase 2 拖太久，隐藏的 UI 长期缺席 | 🟡 中 | Phase 2 仅 3-4h 且基础设施（`FileFormatDetector` / `AdaptiveRuleMatcher`）已存在 |
| 删除三语 key 造成三语 key 集不同步 | 🟢 低 | 三语同步删除；`AllThreeLanguages_HaveSameKeySet` 会兜住 |
| 文档漂移（AGENTS.md 无该段落 / 行号漂移） | 🟢 低 | Task 7 按实测改写；引用行号已逐条核对 |

## 修订记录（2026-10-18 评审）

| 编号 | 评审发现 | 处置 |
|------|----------|------|
| A1 | 改动 B 只做 UI 门控，引擎只认 `options.MultiThreadedCompression`（`ZipEngine.cs:3960`）→ 遗留 `settings.json` / CLI 仍走慢路径 | 新增决策行 + Task 3.1 引擎边界夹取；`_mtUserIntent` 承载意图 → **已关闭**（2026-10-18 改动 B 取消，不做夹取） |
| A2 | `UpdateTotalSourceSize`（`:379`/`:377`）无条件写 `IsEnabled`/恢复勾选，与自适应门控互相覆盖 | Task 3.2 统一 `RefreshZipMtState()`（3 输入 / 4 调用点），删除 `_mtCheckedBeforeLimit` → **已关闭**（MT 只剩 ZIP32 一个门控） |
| A3 | Task 1 只删 `ZipMultiThreadCheck_IsCheckedChanged` 却保留 axaml 事件属性（`:80`）→ XAML 编译失败 | Task 1 改为**保留处理器骨架**（删面板显隐 2 行），Task 3.2 填充意图记录 → **再修订**：改动 B 取消后处理器彻底无用 → 改为「axaml 事件属性与处理器**同时**删除」（Task 1 + 自查 + 风险表） |
| A4 | Task 7 的 AGENTS.md 指令不成立：无「加速」段落、全文无「并行压缩组数」；`:122-123` 讲的是 7z `mt=on` | Task 7 改写为「新增 ZIP 压缩加速小节 + 不动 7z 段落」 |
| B1 | 与 `2026-10-05-n-group-parallel-compress.md:20`（N 组无加速、degree=1 最快）方向相反 | 新增 **Task 0**：同机同语料 before/after 复测，二选一定调默认组数 |
| B2 | 低核数（2–4 核）默认组数未验证 | Task 0 Step 3 + 风险表 |
| C1 | 决策记录「保留 AppSettings 字段」与 Task 1 删字段/删 key 矛盾 | 决策记录行改为「仅保留与魔数/规则相关的部分」，并显式标注删除项 |
| C2 | Task 4 隐藏不对称（设置窗口仍写回 `AdaptiveSmartDetect`） | Task 4 补充说明：保留写回原值（等价 no-op，供 Phase 2 恢复） |
| C3 | 「记住原状态」的持久化语义未定义（跨会话会丢） | Task 3.3：持久化**意图**，生效值 = 意图 && 自适应 → **已关闭**（改动 B 取消，无需意图/生效值二分） |
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
| D8 | 死属性 `IsAdaptiveContentVisible`；规则 6 的置灰例外未说明 | Task 3.3 顺带删除 + 写明理由 → **随改动 B 取消而挂起**：改为「可选清理」（`SettingsWindowViewModel.cs:192` 确认无 XAML 绑定，删不删都不影响行为） |

## 拍板记录（2026-10-18 实测后）

Task 0 基线复测（本机 20 逻辑核，报告 `.buildout\bench-before-{text,media,mixed}.txt`）跑完后，用户于同日拍板三项，计划据此定稿：

| # | 议题 | 拍板结果 | 依据 / 影响 |
|---|------|----------|-------------|
| 1 | 改动 B（MT 依赖自适应）存废 | **取消** | 默认组数下「仅多线程」不慢于「全关 / 仅自适应」（text 0.88s vs 0.95/0.97s 且压缩率 +15%；mixed 0.98s vs 2.13/1.18s）；原「慢 1.5–1.8x」只在 deg≤4（旋钮陷阱档）复现。**Phase 1 因此减少约 2-3h**，评审项 A1/A2/C3/C4 关闭 |
| 2 | 引擎默认组数 | **`clamp(核数,4,16)`** | 否决 `clamp(核数,2,8)`（deg=8 比 deg=16 慢 21–30%）与「默认 = 1」（text 慢 ~1.8x）；本机 = 16 组，与现默认一致 → 删旋钮即纯收益 |
| 3 | 方案 C（`mt = 核数/组数`） | **保留，但标注「未验证」** | 默认路径下为 no-op（本机 16 组 → `mt=max(1,20/16)=1`）；仅在「核数 > 16」或「文件数 < 组数」时生效，收益需大文件语料验证（Task 2 给出命令）；**不得**写入文档当性能依据 |

**连带修订（本轮）**：Task 1（MT 处理器 + axaml 事件属性同时删除）、Task 2（默认规则 `clamp(4,16)` + 方案 C 标注未验证）、Task 3（改为「已取消 + 回归护栏」）、Task 7（不新增三语 key；AGENTS.md 小节改为「三开关互相独立」）、Task 8 验收（before/after 预期、三开关独立护栏、`rg` 自查）、风险表（方案 C 未验证 / 删处理器风险 / 低核未闭环 / 用户原始观察被推翻）、头部工时 6-8h → **4-5h**。

**仍未闭环的验证项**（详见 Task 0 末）：① 低核（≤4 核）默认组数（Step 3 未做，无低核环境）；② deg=1 批次的环境噪声（如需引用绝对秒数则 `--degrees 1 --reps 5` 复测）；③ 方案 C 的大文件语料收益。
