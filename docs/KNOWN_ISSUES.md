# MantisZip — 已知未修问题登记簿

> **这份文档装什么**：已经发现、但**现在不修**的问题 —— 等上游修复、等某个计划完成、缺一个决策、成本超阈值、或本机无法复现。
>
> **这份文档不装什么**：
> - **不是计划**。说得出「做什么、多大、值不值得」的，进 [PLAN.md](PLAN.md) 或 `.omo/plans/`。
> - **不是流水账**。修完的细节进 [progress-avalonia-detail.md](progress-avalonia-detail.md) / [PROGRESS.md](PROGRESS.md)，这里只留状态与 commit。
> - **不是用户手册**。[README.md](../README.md) 的「已知问题」是面向用户的出口，发版时从本文件挑**用户能感知**的条目翻译过去。
> - **不是维护者留白清单**。[MAINTENANCE.md §9](MAINTENANCE.md) 记的是「有意留白」，当某条留白开始咬人（用户可感知、或有人承诺要修）时，到这里立一条并链回去。

## 三条硬纪律

1. **发现「这次不修」的问题 → 当天登一条**，编号 `KI-nnn` 递增、**不复用**。只在对话里说一句等于没记。
2. **修掉 → 回填状态与 commit，并移到文末「档案区」**。不要直接删行：下次撞到同一个栈，要能查到「上次怎么判的、为什么决定不动」。
3. **计划完成时扫一遍**：凡是阻塞类型为「等计划 X」的条目，X 完成当天一并复评（挂到 AGENTS.md 规则 1 的流程尾）。

**每条必须写清三样**，缺一条就别登记：

- **现象 + 证据**（CI run 号 / 异常栈 / `文件:行`）—— 没证据的条目三个月后没人敢信
- **为什么现在不修**（阻塞类型五选一：等上游 / 等计划 / 缺决策 / 成本超阈值 / 无法复现）
- **复评条件**（必须**可判定**：「等 Avalonia 发布含 X 修复的版本」，不是「以后再优化」）

## 索引

| 编号 | 标题 | 类型 | 阻塞类型 | 复评条件（摘要） | 状态 |
|---|---|---|---|---|---|
| **KI-001** | Avalonia 无头测试跨线程竞态 | 测试基础设施 | 等上游 | Avalonia 发布含 `#22021` 修复的版本；或频率升到每 3 次 run 至少 1 次 | 阻塞中 |
| **KI-002** | Core 测试本地跑不了 + `-Task test -Target tests` 假绿 | 测试基础设施 | 缺决策 + 成本 | ① 下次需要在本地验 Core 测试时；② 或加固 `verify-repo.ps1` 时 | 阻塞中 |
| **KI-003** | 24 项 SkiaSharp 4.x CS0618 deprecation | 技术债 | 成本超阈值 | 下次动 `IconProvider` / `IcoParser` / `PreviewViewModel` 时顺带清 | 阻塞中 |
| **KI-004** | UI 测试用固定临时目录（非 GUID） | 测试卫生 | 成本超阈值 | 下次改这两个用例时顺带改；或出现「残留文件导致红」的实例 | 阻塞中 |
| **KI-005** | 29 条 7z 用例被 CI 过滤、永不运行 | 覆盖缺口 | 等计划（CI 环境） | CI 装上 7-Zip 并去掉 `SevenZipEngine` 过滤 | 阻塞中 |
| **KI-006** | `.jar/.war/.ear/.apk` 能打开但**不参与文件关联**（也不进右键菜单/设置页关联列表） | 功能留白 | 缺决策 + 成本 | 用户反馈「想在资源管理器里双击/右键打开 jar」时；或做「关联格式可勾选」设置项时 | 阻塞中 |
| **KI-007** | 大 PDF 预览必须整条目落盘（无法部分预览，也不受 15MB 上限约束） | 性能 / 磁盘 | 成本超阈值 + 上游限制 | PdfPig 支持从截断数据/随机访问流解析时；或用户反馈 >100MB PDF 预览卡顿、临时目录涨满 | 阻塞中 |
| **KI-008** | 断网时「关于 → 更新」页签把检查失败显示成「已是最新版本」 | 交互 / 文案 | 缺决策 | 决定是否让 `UpdateService` 区分「无更新 / 请求失败」两种结局时 | 阻塞中 |

---

## KI-001 Avalonia 无头测试跨线程竞态（上游缺陷）

- **发现**：2026-10-26，CI run [#231](https://github.com/mantis3d/MantisZip/actions/runs/40306714791)（alpha `173084c3`）
- **现象**：偶发 `[Test Case Cleanup Failure]`（不是断言失败），异常是 `System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it.`
- **证据**：栈为 `HeadlessUnitTestSession.EnsureIsolatedApplication → AppBuilder.SetupUnsafe → AvaloniaHeadlessPlatform.Initialize → Compositor..ctor → ServerCompositor..ctor → DefaultRenderLoop.Add → Dispatcher.VerifyAccess`。2026-10-27 复现于 run `40440152537`（alpha `46b883a0`，**纯文档提交**），受害用例为 `ResultTreeViewFilterTests.TogglingCompactMode_KeepsFilterApplied`；同一代码前后两笔（`4f497e14` / `2e7d4d96`）均 success。受害用例随机，别顺着名字改被测代码。
- **为什么现在不修**：**等上游**。`AvaloniaUI/Avalonia#22021`（session 重建期 `s_uiThread` 认领竞态）与 `#21467`（testhost 死锁）均仍 open，本仓库已做该做的（`tests/MantisZip.UI.Avalonia.Tests/TestParallelization.cs` 有 `DisableTestParallelization = true`，测试内无自有后台线程）。
- **复评条件**：① Avalonia 发布含 `#22021` 修复的版本（注意 `Avalonia.Headless.XUnit` 须与 UI 项目主版本一致）→ 升级后观察 10 次 run；② 频率从当前基线（约 5 次 run 1 次）升到每 3 次至少 1 次 → 评估 `AvaloniaTestIsolationLevel.PerAssembly`（有假绿风险，需单独评审）。
- **临时处置**：`gh run rerun <run-id> --failed`。判据与完整流程见 [MAINTENANCE.md §5.1](MAINTENANCE.md) B 类。
- **状态**：阻塞中

## KI-002 Core 测试本地跑不了，且 `-Task test -Target tests` 假绿

- **发现**：2026-10-27（改 `ParallelExtractTests` 取消用例时）
- **现象**：① `dotnet test tests\MantisZip.Tests` 本机起不来 testhost；② 维护脚本的兜底会对**根本没跑任何用例**的 v2 程序集报「通过」。
- **证据**：
  - `System.ComponentModel.Win32Exception (5): 拒绝访问` at `Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.ProcessHelper.SetExitCallback` → `DefaultEngineInvoker.SetParentProcessExitCallback`（`danger-full-access` 下同样复现，非沙箱专属；见 `MAINTENANCE.md §8`）。
  - `pwsh -File scripts\verify-repo.ps1 -Task test -Target tests` → 输出 `ℹ️ VSTest testhost 不可用，已回退 dotnet exec 直跑测试程序集（xunit MTP）。` + `✅ 测试通过：tests（0.4s，0 警告）`、**exit 0**，但 Core 程序集是 **xUnit v2**、没有 MTP 入口，实际执行 **0 条用例**（当时 800 条，现 801 条，一条没跑）。UI 测试项目是 xUnit v3，兜底路径对它有效。
- **为什么现在不修**：**缺决策 + 成本**。加固脚本需要判断「兜底跑出 0 条结果 = 失败」，改 Core 测试基础设施（迁 xUnit v3）则牵扯 800 条用例与宿主依赖，不属于顺手活。
- **复评条件**：① 下次需要本地验证 Core 测试改动时；② 或任何一次加固 `scripts/verify-repo.ps1` 的时候 —— **最小改法**：MTP 兜底解析不到任何用例结果时判 `ok=false`，别让「0 条用例」显示成绿色。
- **影响面**：Core 测试改动只能本地编译验证、正确性靠 CI 实测（2026-10-27 改取消用例即如此）。
- **状态**：阻塞中

## KI-003 24 项 SkiaSharp 4.x CS0618 deprecation

- **发现**：2026-09-16（v0.5.0 NuGet 依赖升级，SkiaSharp 3.119.4 → 4.152.0）
- **现象**：24 项 CS0618（已标记废弃但还能用），分布在 `IconProvider.cs`（18 项 `SKPath`）、`IcoParser.cs`（1 项 `DrawBitmap`）、`PreviewViewModel.cs`（1 项 `DrawBitmap`）。
- **证据**：[progress-avalonia-detail.md:1547](progress-avalonia-detail.md)（升级条目与 24 项统计）、[:1553](progress-avalonia-detail.md)（技术债清单）。
- **为什么现在不修**：**成本超阈值（收益低）**。改法已明确 —— `SKPath.MoveTo/LineTo/Close` → `SKPathBuilder`、`SKCanvas.DrawBitmap` → 新 `SKSamplingOptions` 重载 —— 但要遍历图标加载与预览绘制路径，属于「顺手做」而不是「专门做」。
- **复评条件**：① 下次改 `IconProvider` / `IcoParser` / `PreviewViewModel` 时顺带清理；② SkiaSharp 宣布移除旧 API（届时会变成编译错误，自动强制处理）。
- **状态**：阻塞中

## KI-004 UI 测试用固定临时目录（非 GUID）

- **发现**：2026-10-26（排查 CI 偶发失败时审计测试卫生）
- **现象**：`tests\MantisZip.UI.Avalonia.Tests\PreviewWebViewLazyInitTests.cs:57` 与 `:287` 用固定目录 `Path.GetTempPath()\MantisZip\Tests`，同项目其余测试均用 `Guid.NewGuid()` 命名（如 `PasswordServiceEncryptionTests`、`MergeWriteRowIntegrationTests`）。
- **证据**：`var dir = Path.Combine(Path.GetTempPath(), "MantisZip", "Tests");`（:57）；`tmpZip` 同目录（:287）。
- **为什么现在不修**：**成本超阈值**。当前未观察到它导致失败（用例串行、内容各自覆盖写），改动要连带处理清理逻辑；而「固定目录」的隐患是残留文件互相污染与并发跑测试时的互踩。
- **复评条件**：① 下次改这两个用例时顺带改成 GUID 子目录；② **或**出现「上一次运行残留导致本次红」的实例（届时立刻升为优先修）。
- **状态**：阻塞中

## KI-005 29 条 7z 用例被 CI 过滤、永不运行

- **发现**：2026-10 期间（CI 门禁定型时即存在）
- **现象**：CI 的测试步骤用 `--filter "FullyQualifiedName!~SevenZipEngine"`，`tests\MantisZip.Tests\Engines\SevenZipEngineTests.cs` 里 **29 个测试方法**（其中含 2 个 `[Theory]`，实际用例数 ≥ 29）在 CI 上永不执行；本地无 native `7z.dll` 时同样跳过。
- **证据**：CI 测试步骤命令（[.github/workflows/ci.yml](../.github/workflows/ci.yml)）；用例计数 `rg -c '^\s*\[(Fact|Theory)' tests\MantisZip.Tests\Engines\SevenZipEngineTests.cs` → 29。
- **为什么现在不修**：**等计划（CI 环境）**。需要 runner 装 7-Zip 或随仓库带 native 二进制，属 CI 环境改动 + 需要评估二进制分发，不是代码问题。
- **复评条件**：CI 装上 7-Zip（或改走自带 native 依赖）后去掉过滤，并把本条移到档案区。
- **关联**：[MAINTENANCE.md §9](MAINTENANCE.md) 的「7-Zip 用例」留白项。
- **状态**：阻塞中

---

## KI-006 `.jar/.war/.ear/.apk` 能打开但不参与文件关联

- **发现**：2026-10-27（实现 issue #54 时决定的范围收窄）
- **现象**：这 4 个扩展名已进 `ArchiveEngineFactory.SupportedExtensions`（拖入、打开对话框、命令行都能打开），但**没有**注册文件关联，也不进右键菜单与设置页的关联列表；资源管理器里双击 `.jar` 仍交给用户原有的程序（通常是 JDK / IDE）。
- **证据**：`src\MantisZip.UI.Avalonia\Services\ShellIntegration.cs` 的 `ArchiveExtensions` 是**显式 8 项常量**（`.zip/.7z/.rar/.tar/.tgz/.tar.gz/.gz/.iso`），不再等于 `SupportedExtensions`；守卫测试 `tests\MantisZip.Tests\ShellIntegrationAssocTests.cs` 的 `IsAssocEnabledInSettings_CoversAssociatedExtensions` 反向断言这 4 个不在关联名单里。
- **为什么现在不修**：**缺决策 + 成本**。纳入关联 = 新增 4 个 `AppSettings` 开关（`AssocJar` 等）+ `IsAssocEnabledInSettings`/`GetProgId`/设置页 `AssocItems` 三处 switch + 三语文案，还要回答「该不该抢用户已有的 `.jar` 默认程序」这个产品问题（`.jar` 与 `.zip` 不同：它常常已经被 JDK 绑走，误抢会打断 `java -jar` 的使用习惯）。
- **复评条件**：用户反馈「希望双击/右键用 MantisZip 打开 jar」时；或做「关联格式可勾选」这类通用设置项时一并纳入。
- **关联**：`.omo/plans/已完成/format-detection-container-sfx.md` 的「与计划的偏差 2」。
- **状态**：阻塞中

## KI-007 大 PDF 预览必须整条目落盘（无法部分预览，也不受 15MB 上限约束）

- **发现**：2026-10-28（`preview-memory-first` 切片 4 判定时）
- **现象**：预览压缩包里的 PDF 时，无论多大都必须先把**整条目**解压到 `%TEMP%\MantisZip\Preview\preview_*.pdf` 才能渲染首页；一个 500MB 的 PDF 会先占 500MB 临时磁盘，再解析出第 1 页。且 PDF 在 `PreviewService.MetadataOnlyExtensions` 里，**不受 `MaxPreviewFileSize`（默认 15MB）拦截**，所以它是唯一「没有大小闸门、又必须整条落盘」的预览类型。
- **证据**：`src\MantisZip.Core\Utils\PdfParser.cs:64` 的 `ParseFromSeekable` 是**头尾双向扫描**（与 `PreviewViewModel.ShowPdfAsync` 一样依赖 xref/trailer 在文件**末尾**）⇒ 只给前 N 字节的截断数据解析不出任何页；`PreviewViewModel` 的 PDF 分支本来就只 `LoadPdfPageAsync(1)`（只渲染首页）。计划里曾设想「PDF 也走 head-only 部分预览」，实测后判定**不可行且是回归**（本来就不受 15MB 限制，加拒绝等于把能看的 PDF 变成不能看）。
- **为什么现在不修**：**成本超阈值 + 上游限制**。要真做到部分预览，得让 PdfPig 支持「从截断流式数据增量解析」或提供随机访问的异步加载器；当前的绕法是内存映射（`MemoryMappedFile`）+ PdfPig 随机访问流，改动面横跨 Core 解析层与三处调用点，而收益只对超大 PDF 成立。
- **复评条件**：① PdfPig 发布支持截断/随机访问流解析的版本（升级时复评）；② 或用户反馈「预览 >100MB 的 PDF 卡顿 / `%TEMP%` 被涨满」——届时优先做内存映射而不是 head-only。
- **关联**：`.omo/plans/已完成/preview-memory-first.md` 的 T10（PDF 一项已判定「一行不改」）。
- **状态**：阻塞中

## KI-008 断网时「关于 → 更新」页签把检查失败显示成「已是最新版本」

- **发现**：2026-10-28（实现 `auto-update` 计划 T5 时）
- **现象**：「关于 → 更新」页签点「检查更新」，在**完全断网 / GitHub 被墙 / DNS 失败 / 超时**时显示「已是最新版本」。用户会误以为确实是新版，而实际上是没连上。启动时的后台检查不受影响（它本来就静默）。
- **证据**：`src/MantisZip.Core/Services/UpdateService.cs:55` 的 `CheckForUpdateAsync` 契约是 `Task<UpdateInfo?>`——「已是最新」「HTTP 非 2xx」「网络异常」「5s 超时」四种情况**全部返回 null**（见 `:103` 起的异常分支与 `GitHubApiBase` 请求），调用方无从区分。`src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml.cs` 的 `CheckForUpdate` 因此把 null 一律映射成 `Update_UpToDate`，代码注释里已写明这是已知妥协。
- **为什么现在不修**：**缺决策**。修法有三条路：①把契约改成 `UpdateCheckResult(UpdateInfo? Update, bool Failed, string? Reason)`（改 Core 公共 API + 8 个单测 + 两个调用点，最干净）；②`CheckForUpdateAsync` 失败时抛异常、由调用方 catch（与现有「异常一律返 null」的设计相反）；③UI 侧再加一次「轻量连通性探测」（多打一次网络，语义还是靠猜）。选哪条取决于「检查更新失败要不要给用户明确提示」这个交互决定，属计划外的新决策。
- **复评条件**：① 决定「检查更新失败」是否要单独提示文案时；② 或用户反馈「明明有新版却提示已是最新」时（此时优先走方案①）。
- **关联**：`.omo/plans/已完成/auto-update.md` T5 的已知妥协说明。
- **状态**：阻塞中

---

## 档案区（已修 / 不会修）

> 修掉或决定放弃的条目移到这里，**保留 commit 或理由**。不要删行 —— 下次撞到同一个栈，靠这里判断「上次怎么处理的」。

| 编号 | 标题 | 结局 | commit / 理由 | 关闭日期 |
|---|---|---|---|---|
| — | — | — | — | — |
