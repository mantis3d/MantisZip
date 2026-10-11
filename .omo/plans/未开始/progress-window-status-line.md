# 进度窗口状态行 + 失败原因明细 + 关闭链统一 + 倒计时 + 解压后置开关

> **状态**: 📋 设计已确认（2026-10-11 spec 全量拍板，D1–D19），任务分解已生成，待实施
>
> **For agentic workers:** 本文档是设计规格摘要 + 完整任务分解。设计全文见 `docs/superpowers/specs/2026-10-11-progress-window-status-line-design.md`（12 节，D1–D19 决策表——**执行时不得偏离 spec**；本文档只摘执行所需代码与位置）。执行时使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。Steps use checkbox (`- [ ]`) syntax for tracking.

## TL;DR

> **Quick Summary**: 进度窗口五大改造：① 状态行下移到信息区与按钮行之间 + 可复制（14px）；② 错误摘要红/黄分层（红=失败、黄=压缩端跳过）+ 复制按钮，失败原因走 `EntryFailureReason` 枚举映射三语（Core 禁中文文案）；③ 测试压缩包结果写进窗口状态行/错误摘要（移除失败模态框），出错/测试流**自动按下 📌** 实现不自动关闭（单一关闭机制）；④ 自动关闭延时进设置（默认 3s）+ 按钮行左侧倒计时（行内可改，仅本次生效）；⑤ 「解压后打开目录」「解压后移入回收站」两个开关进按钮行（实例级不回写设置，压缩/测试流隐藏）。
>
> **Deliverables**:
> - `Core/Abstractions/ArchiveEngine.cs`：`EntryFailureReason` 枚举 + `ArchiveProgress.EntryFailureReason`
> - `Core/Utils/FailureReasonMapper.cs`（新）+ `Core/Utils/ReadErrorHandler.cs` 预检回调
> - 三引擎 24 处失败埋点带原因 + 压缩端跳过上报带原因
> - `ViewModels/ProgressViewModel.cs`：红/黄段聚合、自动按下 📌、倒计时/后置开关 VM 属性、`LocalizedStrings` 补登记
> - `Dialogs/ProgressWindow.axaml(.cs)`：布局重排、双复制按钮、倒计时控件（编辑态状态机）、📁/🗑 开关、`AutoCloseOrWaitAsync` 单点化
> - `Views/MainWindow.axaml.cs` / `ViewModels/MainWindowViewModel.cs` / `Services/ExtractFlow.cs` / `Services/DragDropService.cs` / `App.axaml.cs`：关闭链收口、TestArchive 接线、后置开关消费
> - `Models/AppSettings.cs` + `Views/SettingsWindow.axaml` + `ViewModels/SettingsWindowViewModel.cs`：`AutoCloseDelaySeconds`
> - 三语 JSON 8 组新 key；`Utils/CountdownParser.cs`（新）
> - 测试：`FailureReasonMapperTests`（新）、`ProgressViewModelTests`（+12 条）、`ProgressWindowXamlTests`（+2 条）、`CountdownParserTests`（新）
> - `docs/PLAN.md` 登记行更新（规则 1）
>
> **Estimated Effort**: Large（12–16h）
> **Parallel Execution**: NO — 主要改动集中在 `ProgressViewModel` / `ProgressWindow` / `App.axaml.cs`，顺序执行
> **Critical Path**: Wave 1 Core 通道 → Wave 2 VM 聚合 → Wave 3 关闭链 → Wave 4 XAML/设置/倒计时 → Wave 5 后置开关 → FINAL 验证

---

## Context

### Original Request（用户原话演进，2026-10-11）

1. 「状态栏和错误摘要显示测试结果 + 三种情况进度窗口完成后不自动关闭（KeepOpenToggle / 出错 / 测试流弹出的窗口）」
2. 「后两个不关闭都是通过自动按下 KeepOpenToggle 完成的，这样应该会避免一些问题。测试失败时那个既有模态框移除吧」
3. 「自动关闭的时间放在设置里由用户设置，自动关闭时某处显示倒计时」→ 按钮行左侧 + 行内可临时改（仅本次，不回写设置）；默认 3 秒
4. 「再加两个切换按钮，分别对应设置里的『解压后打开对应目录』和『解压后把原压缩包移入回收站』，压缩端隐藏」→ 持久性拍板 A（仅本次生效不回写设置）

（状态行下移/可复制/红黄分层/枚举映射为本日早前轮次已拍板内容，完整演进见 spec §1–§8。）

### Research Findings（2026-10-11 快照，执行时以 rg 复核为准）

- **布局**：`ProgressWindow.axaml` 7 行——Row 3 状态消息 TextBlock（:541-547，不可复制）、Row 4 下方信息区 Border（:553-859）、Row 5 `ErrorSummaryBox`（:861-874，只读 TextBox 已可选中，`IsVisible={HasErrors}`）、Row 6 按钮行（:876-921，4 列 `Auto,*,Auto,Auto`：📌 / 弹性空白 / 暂停 / 取消）；`CopyToast` RowSpan=7 末位叠加（:929）
- **`AutoCloseOrWaitAsync(int delayMs, Action closeAction)`**（`ProgressWindow.axaml.cs:206-228`）：先查 `KeepOpenOnComplete` → `WaitForManualCloseAsync`；否则 100ms 轮询延时（轮询中 pressing📌 会切换到手动等待）。调用点延时硬编码：`App.axaml.cs:1262/:1430/:1622/:2377` 传 `2500`，`MainWindow.axaml.cs:307` 与 `ExtractFlow.cs:112` 传 `0`
- **关闭链现状**：`MainWindow.axaml.cs:331-334` 与 `ExtractFlow.cs:132-135` `finally { pw.Close(); }` **无条件关窗（失败也关）**；`RunWithProgress` 通用 `catch (Exception)`(:325) 吞异常（不写摘要不落日志）；App CLI 四处失败收口已用 `SetErrorSummary + CompleteWithErrors + WaitForWindowCloseAsync`（`App.axaml.cs:1252-1257` 等，`WaitForWindowCloseAsync` 定义 :1629）
- **`TestArchive`**（`MainWindowViewModel.cs:2836-2877`）：`testOk` 在窗口关闭后才写主窗口状态栏；失败弹 `AppMessageBox`（:2872-2875，本次移除）；`RunWithProgress` 委托类型 `Func<string, IReadOnlyList<string>?, Func<IProgress<ArchiveProgress>, CancellationToken, Task>, Task<bool>>`（:139），窗口由闭包内部创建（`MainWindow.axaml.cs:268-336`），调用方事后无 `pw` 引用——既有 `PendingCompressFlow`/`PendingCompressRequest`（:145/:151）就是为解决这类「闭包内窗口、调用方要传状态」的 pending 模式
- **`SetProgress` 逐条目分支**（`ProgressViewModel.cs:591-598`）：`EntryStatus` 报告专用分支，其后早返回不进速度采样——红/黄段聚合必须放该分支内
- **失败/跳过上报现状**：三引擎共 24 处 `ArchiveEntryStatus.Failed`（Zip 12 / TarGz 8 / SevenZip 4，`rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines`），catch 内异常对象在作用域但只进 CoreLog；压缩端 `ZipEngine.cs:1767` / `TarGzEngine.cs:285` 已报 `Skipped` 无原因（重试在 `ReadFileWithRetry:3371-3395` / `TarWriteFileWithRetry:360-378` 的 catch 内知道异常）；7z/加密 ZIP 预检 `ReadErrorHandler.FilterUnreadableFiles`（`ReadErrorHandler.cs:24-40`，调用点 `ZipEngine.cs:1381` / `SevenZipEngine.cs:599`）**零上报静默剔除**
- **后置动作**：`App.TryDeleteArchiveAfterExtract(path)`（`App.axaml.cs:962-989`）**方法内部读设置**（:965）——实例级覆盖必须改签名；打开目录消费点：`MainWindowViewModel:2351`（对话框流程读 `vm.OpenFolderAfterExtract`）/:2510（读 settings）、`DragDropService:129`（读 settings）；CLI `:1198/:1555` 调 TryDelete（**在自动关闭等待之前**执行——与主窗口流程「窗口关闭后执行」时序不一致，本次统一）；CLI 打开目录未接线（挂起计划 `cli-extract-open-folder.md`，本次不做）
- **设置字段已存在**：`AppSettings.OpenFolderAfterExtract`(:72) / `DeleteArchiveAfterExtract`(:90)；设置窗口解压页复选框（`SettingsWindow.axaml:452-459`）；解压对话框也有「打开文件夹」复选框（`ExtractSettingsWindow.axaml:122`）
- **图标/测试/格式**：`IconFolder`/`IconDelete` 已存在（`AppIcons.axaml:4/:13`，Rule 8 免）；`ProgressWindowXamlTests.cs:34` 断言 `RowDefinitions.Count == 7`（本次行数不变）；`ProgressViewModel.LocalizedStrings` 由构造函数内显式字典构建（`ProgressViewModel.cs:84+`，漏登记 = XAML 绑定空白且构建不报错）；Core 测试命名空间 `MantisZip.Tests.Utils`；设置窗口数字输入框模式抄 `MaxRecentFiles`（`SettingsWindow.axaml:528-538`）

---

## Work Objectives

### Core Objective

让用户在进度窗口里**看到发生了什么**（状态行可复制 + 失败/跳过明细带原因）、**控制窗口何时消失**（延时设置 + 倒计时 + 📌 单一机制）、**控制解压后置动作**（📁/🗑 开关），且失败时窗口必然留驻供查看。

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误
- [ ] `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` → 0 错误
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿（新增 `FailureReasonMapperTests` 3 条）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（新增 ~16 条；基线以执行时实测为准）
- [ ] GUI 手动验收（Task 12 清单 12 条逐条通过）
- [ ] `docs/PLAN.md` 已同步（规则 1）
- [ ] `git grep -n -E "AutoCloseOrWaitAsync\((2500|0)," -- 'src/*.cs'` → 无命中（魔法数字清除）
- [ ] `git grep -n "WaitForWindowCloseAsync" -- 'src/*.cs'` → 无命中（辅助方法已删）

### Must NOT Have（护栏，对应 spec D1–D19）

- 不透传 `ex.Message` 进摘要明细（整包异常的 `SetErrorSummary(ex.Message)` 既有语义除外）；Core 不新增中文用户可见文案（D2）
- 黄段不触发 `HasErrors`（D4）；解压冲突跳过（无原因 Skipped）不进黄段（D5）
- 不新造 `ShouldAutoClose` 判定——关闭判定唯一入口是 `KeepOpenOnComplete` + `AutoCloseOrWaitAsync`（D10）
- 倒计时/📁/🗑 的改动**不回写 `AppSettings`**（D16/D17）
- 不动 `ConflictStatsCounter` 语义、不改 `IArchiveEngine` 签名（D9）
- 取消（`OperationCanceledException`）不写摘要、不自动按下 📌、即时关窗（D13）
- 不移除 `ExtractFlow` 失败模态框（仅移除**测试失败**模态框，D12）
- 不做 CLI 打开目录接线（归 `cli-extract-open-folder.md`）、不做拖拽解压删原包接线（D18）

### Verification Strategy (MANDATORY)

**Test Decision**: TDD（Tasks 1/4/5/10/11 有新单测；Tasks 2/3 为机械埋点，以 rg 数量断言 + 构建验证）。

自动化验证命令（每任务末尾按需执行）：

```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines   # 24 处逐处带 EntryFailureReason
```

### 依赖与后续

- 关联挂起计划：`cli-extract-open-folder.md`（本次明确不做 CLI 打开目录接线，D18）
- Follow-ups（spec §12）：黄段扩解压冲突跳过 / 读写权限细分 / 测试逐条目定位等 6 项，另立计划

---

## 任务分解

> **执行方式**: superpowers:subagent-driven-development（推荐，每任务独立子代理）或 superpowers:executing-plans（本会话批量执行），checkbox 逐步勾选。
> **提交策略**: Tasks 1–12 默认**不 commit**；仅当用户明确要求提交时执行 Task 13，且按规则 3 先更新双轨进度文档。

### 执行波次

```
Wave 1（Core + i18n，顺序）
├── 任务 1: EntryFailureReason 枚举 + FailureReasonMapper（TDD）
├── 任务 2: 三语本地化 key 8 组（解锁后续任务的 T() 调用）
├── 任务 3: 解压端三引擎 24 处失败埋点带原因（机械，rg 驱动）
└── 任务 4: 压缩端跳过上报 + ReadErrorHandler 预检回调（TDD）

Wave 2（VM）
└── 任务 5: ProgressViewModel 红/黄段聚合 + 自动按下 📌（TDD，Blocked By: 1–4）

Wave 3（关闭链）
├── 任务 6: RunWithProgress/ExtractFlow/App CLI 收口统一 + AutoCloseOrWaitAsync 去参（Blocked By: 5）
└── 任务 7: TestArchive 结果进窗口 + 移除失败模态框（Blocked By: 6）

Wave 4（UI 层）
├── 任务 8: 布局重排 + 状态行/错误摘要复制按钮 + 黄段控件（Blocked By: 5）
├── 任务 9: AutoCloseDelaySeconds 设置项（可与 8 并行，无文件交集）
├── 任务 10: 倒计时显示 + 行内编辑状态机（TDD，Blocked By: 6、9）
└── 任务 11: 解压后置开关 📁🗑（VM+XAML+全流程接线，Blocked By: 6）

Wave FINAL
├── 任务 12: 全量验证 + GUI 人工验收（Blocked By: 1–11）
└── 任务 13: 进度文档同步与提交（仅用户要求时；Blocked By: 12）
```

---

- [ ] 1. Core：`EntryFailureReason` 枚举 + `FailureReasonMapper`（TDD）

  **Files:**
  - Modify: `src/MantisZip.Core/Abstractions/ArchiveEngine.cs`（`ArchiveEntryStatus` 枚举后 :313、`ArchiveProgress` 尾部 :368 后）
  - Create: `src/MantisZip.Core/Utils/FailureReasonMapper.cs`
  - Test: `tests/MantisZip.Tests/Utils/FailureReasonMapperTests.cs`

  **What to do**:

  (1a) 写失败测试 —— 新建 `tests/MantisZip.Tests/Utils/FailureReasonMapperTests.cs`（编译级红：类型尚不存在；命名空间对齐同目录既有 `Utils/FileConflictDetectorTests` 等）：

  ```csharp
  using MantisZip.Core.Abstractions;
  using MantisZip.Core.Utils;
  using Xunit;

  namespace MantisZip.Tests.Utils;

  /// <summary>
  /// 失败原因分类器（spec §5）：异常 → EntryFailureReason 三分类。
  /// 同一异常类型在三引擎埋点与 ReadErrorHandler 预检的映射必须一致。
  /// </summary>
  public class FailureReasonMapperTests
  {
      [Fact]
      public void UnauthorizedAccess_MapsToPermissionDenied()
          => Assert.Equal(EntryFailureReason.PermissionDenied,
              FailureReasonMapper.FromException(new UnauthorizedAccessException("denied")));

      [Fact]
      public void Io_MapsToIoFailed()
          => Assert.Equal(EntryFailureReason.IoFailed,
              FailureReasonMapper.FromException(new IOException("locked")));

      [Fact]
      public void Unknown_MapsToOther()
          => Assert.Equal(EntryFailureReason.Other,
              FailureReasonMapper.FromException(new InvalidOperationException("crc")));
  }
  ```

  (1b) 运行确认编译级红：

  ```powershell
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~FailureReasonMapperTests"
  ```

  预期：编译失败 `CS0246: The type or namespace name 'EntryFailureReason' could not be found`（预期红）。

  (1c) 新增枚举 —— `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` 在 `ArchiveEntryStatus` 枚举的收尾 `}` 之后（:313）插入：

  ```csharp
  /// <summary>
  /// 逐条目失败/跳过原因类别（spec §5）。Core 只产事实类别，
  /// 本地化文案由 UI 映射（规则 13：Core 禁止新增中文用户可见文案）。
  /// </summary>
  public enum EntryFailureReason
  {
      /// <summary>权限不足（UnauthorizedAccessException）。</summary>
      PermissionDenied,

      /// <summary>读写错误：文件被占用、磁盘满、路径过长等（IOException）。</summary>
      IoFailed,

      /// <summary>其它异常（CRC/数据损坏等，含 ZIP 并行路径通用 catch）。</summary>
      Other,
  }
  ```

  (1d) `ArchiveProgress` 追加可选字段 —— 同文件 `EntryCompressedBytes` 属性（:368）之后插入：

  ```csharp
      /// <summary>失败/带原因跳过的根因类别。仅 EntryStatus=Failed 或压缩端带原因 Skipped 时携带。</summary>
      public EntryFailureReason? EntryFailureReason { get; set; }
  ```

  > 注：属性名与类型名同名是合法的 C#「Color Color」模式，不要改名。

  (1e) 新增分类器 —— 新建 `src/MantisZip.Core/Utils/FailureReasonMapper.cs`：

  ```csharp
  using System.IO;
  using MantisZip.Core.Abstractions;

  namespace MantisZip.Core.Utils;

  /// <summary>
  /// 异常 → <see cref="EntryFailureReason"/> 分类器。
  /// 三引擎失败/跳过埋点与 ReadErrorHandler 预检共用，保证同一异常类型映射一致；
  /// 不透传 ex.Message（spec D2）。
  /// </summary>
  public static class FailureReasonMapper
  {
      /// <summary>按异常运行时类型分类；不在已知两类内一律归 Other。</summary>
      public static EntryFailureReason FromException(Exception ex) => ex switch
      {
          UnauthorizedAccessException => EntryFailureReason.PermissionDenied,
          IOException => EntryFailureReason.IoFailed,
          _ => EntryFailureReason.Other,
      };
  }
  ```

  (1f) 重跑 (1b) 同命令，预期：**3 条全部 Passed**。

  **Must NOT do**:
  - 不在 Core 写任何中文用户可见文案（D2）
  - 不动 `ArchiveEntryStatus` 既有语义、不改 `IArchiveEngine`

  **Recommended Agent Profile**: Category `quick`，Skills `[]`

  **Parallelization**: Can Run In Parallel NO（后续任务依赖本契约）；Blocks: 2/3/4/5

  **Acceptance Criteria**:
  - [ ] 3 条测试通过；`dotnet build src\MantisZip.Core\MantisZip.Core.csproj` 0 错误
  - [ ] 枚举含且仅含 3 个成员；`ArchiveProgress.EntryFailureReason` 为 `EntryFailureReason?`

---

- [ ] 2. 三语本地化 key（8 组，规则 13）

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`（各文件头 `{` 之后插入）

  **What to do**:

  (2a) 每个文件在第 1 行 `{` 之后、首个既有 key 之前插入以下 8 个 key（**三语 key 集必须完全一致**，`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 会校验；保持 UTF-8 无 BOM + CRLF + 2 空格缩进，末key补逗号规则与上下文一致）：

  | key | zh-CN | en | zh-TW |
  |---|---|---|---|
  | `Progress_Failed_EntryFmt` | `{0} 解压失败：{1}` | `{0}: extraction failed: {1}` | `{0} 解壓失敗：{1}` |
  | `Progress_Skipped_EntryFmt` | `{0} 未包含进压缩包：{1}` | `{0} not included in archive: {1}` | `{0} 未包含進壓縮包：{1}` |
  | `Error_Reason_PermissionDenied` | `权限不足` | `Access denied` | `權限不足` |
  | `Error_Reason_IoFailed` | `I/O 错误（文件被占用或磁盘异常）` | `I/O error (file in use or disk problem)` | `I/O 錯誤（檔案被佔用或磁碟異常）` |
  | `Error_Reason_Other` | `未知错误` | `Unknown error` | `未知錯誤` |
  | `Progress_ErrorSummary_More` | `…另 {0} 项` | `…and {0} more` | `…另 {0} 項` |
  | `Progress_AutoCloseSuffix` | `秒后自动关闭` | `s until auto-close` | `秒後自動關閉` |
  | `Settings_ProgressAutoCloseDelay` | `完成后自动关闭延时（秒，0–60，0 = 立即关闭）` | `Auto-close delay after completion (0–60 s, 0 = close immediately)` | `完成後自動關閉延時（秒，0–60，0 = 立即關閉）` |

  zh-CN 插入块示例（en/zh-TW 换对应文案）：

  ```json
    "Progress_Failed_EntryFmt": "{0} 解压失败：{1}",
    "Progress_Skipped_EntryFmt": "{0} 未包含进压缩包：{1}",
    "Error_Reason_PermissionDenied": "权限不足",
    "Error_Reason_IoFailed": "I/O 错误（文件被占用或磁盘异常）",
    "Error_Reason_Other": "未知错误",
    "Progress_ErrorSummary_More": "…另 {0} 项",
    "Progress_AutoCloseSuffix": "秒后自动关闭",
    "Settings_ProgressAutoCloseDelay": "完成后自动关闭延时（秒，0–60，0 = 立即关闭）",
  ```

  (2b) 插入前 grep 复核无重名：`rg -n "Progress_Failed_EntryFmt|Error_Reason_|Progress_AutoCloseSuffix|Settings_ProgressAutoCloseDelay|Progress_ErrorSummary_More|Progress_Skipped_EntryFmt" src\MantisZip.UI.Avalonia\Localization`——除本次新增外应无命中（若有近义 key 优先复用并在计划状态块注明）。

  (2c) 校验三语 key 集一致 + 无格式破坏：

  ```powershell
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~AllThreeLanguages_HaveSameKeySet"
  ```

  预期：Passed。

  **Must NOT do**:
  - 不重排既有 key、不改动既有 key 的文案
  - `Progress_AutoCloseSuffix` 刻意**不含 `{0}`**（数字由可编辑 TextBox 承载，spec §6 注）

  **Recommended Agent Profile**: Category `quick`，Skills `[]`

  **Parallelization**: Can Run In Parallel NO（三文件顺序改）；Blocks: 5/8/10/11

  **Acceptance Criteria**:
  - [ ] 三语各 +8 key；key 集测试绿；文件保持 UTF-8 无 BOM + CRLF

---

- [ ] 3. Core：解压端三引擎 24 处失败埋点带原因（机械，rg 驱动）

  **Files:**
  - Modify: `src/MantisZip.Core/Engines/ZipEngine.cs`（12 处）/ `TarGzEngine.cs`（8 处）/ `SevenZipEngine.cs`（4 处）

  **What to do**:

  (3a) 全量定位（2026-10-11 快照 24 处；以本命令实时结果为准，数量不符时以实际为准并更新下方分布）：

  ```powershell
  rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines
  ```

  快照分布：ZipEngine 12 处（:407/:417 顺序解压、:692/:702/:712 并行解压、:929/:937 附近、:1209/:1219/:1230 附近）、TarGzEngine 8 处（:149/:157/:200/:208/:743/:751/:806/:814）、SevenZipEngine 4 处（:534/:542/:929/:937）。

  (3b) 逐处按 **catch 类型**在**同一个** `new ArchiveProgress { ... }` 对象初始化器内追加 `EntryFailureReason = ...`（禁止拆成两次 Report——消费者按单报告读取，spec §5.2）：

  | catch 类型 | 追加 |
  |---|---|
  | `catch (UnauthorizedAccessException uax)` | `EntryFailureReason = EntryFailureReason.PermissionDenied,` |
  | `catch (IOException iox)` | `EntryFailureReason = EntryFailureReason.IoFailed,` |
  | 通用 `catch (Exception ex)`（仅 ZIP 并行路径 :704/:1230） | `EntryFailureReason = EntryFailureReason.Other,` |

  标准改法示例（`ZipEngine.cs:407`，其余 23 处同构——**逐处确认异常变量（uax/iox/ex）在作用域**；不在作用域的点位填 `EntryFailureReason = EntryFailureReason.Other,` 并加行尾注释 `// 异常对象不在作用域，归 Other（spec §5.2）`，不编造）：

  ```csharp
  // 旧
  progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize });
  // 新
  progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.PermissionDenied });
  ```

  变量名不同的点位（如 `entrySize` 换成其它字节字段或无字节字段）**只加 `EntryFailureReason` 一项**，其余初始化器成员原样保留。

  (3c) 纪律自查（spec §5.2）：
  - `catch (OperationCanceledException)` 分支**不动**（取消不是失败，`throw;` 语义不变）
  - 时间戳设置失败的被吞分支（`ZipEngine.cs:394` 类）**不动**
  - 每个文件顶部 `using MantisZip.Core.Abstractions;` 已存在（同命名空间内类型，通常无需加）

  (3d) 构建 + 全量搜索验证：

  ```powershell
  dotnet build src\MantisZip.Core\MantisZip.Core.csproj
  rg -n -B1 "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines | rg -c "EntryFailureReason"   # 应等于 Failed 上报点总数
  ```

  **Must NOT do**:
  - 不改上报点的字段集合以外的任何逻辑（计数器、日志、锁保持原样）
  - 不在并行路径把 Report 挪进/挪出锁（锁外上报纪律维持）

  **Recommended Agent Profile**: Category `quick`，Skills `[]`

  **Parallelization**: Can Run In Parallel NO（与任务 4 同文件）；Blocked By: 1；Blocks: 4/5

  **Acceptance Criteria**:
  - [ ] `rg` 验证：每个 `EntryStatus = ArchiveEntryStatus.Failed` 的 `ArchiveProgress` 初始化器都含 `EntryFailureReason`
  - [ ] Core 构建 0 错误；既有 Core 测试无回归

---

- [ ] 4. Core：压缩端跳过上报带原因 + `ReadErrorHandler` 预检回调（TDD）

  **Files:**
  - Modify: `src/MantisZip.Core/Utils/ReadErrorHandler.cs`（:24-40 `FilterUnreadableFiles`、:46-80 `EnsureReadable`）
  - Modify: `src/MantisZip.Core/Engines/ZipEngine.cs`（`ReadFileWithRetry`:3285 起、调用点 :1761-1769、预检 :1381）
  - Modify: `src/MantisZip.Core/Engines/TarGzEngine.cs`（`TarWriteFileWithRetry`:344 起、调用点 :281-287）
  - Modify: `src/MantisZip.Core/Engines/SevenZipEngine.cs`（预检 :599）
  - Test: `tests/MantisZip.Tests/Utils/ReadErrorHandlerSkipReasonTests.cs`（新）

  **What to do**:

  (4a) 写失败测试 —— 新建 `tests/MantisZip.Tests/Utils/ReadErrorHandlerSkipReasonTests.cs`（编译级红：`onSkipped` 参数不存在）：

  ```csharp
  using MantisZip.Core.Abstractions;
  using MantisZip.Core.Utils;
  using Xunit;

  namespace MantisZip.Tests.Utils;

  /// <summary>
  /// 预检剔除回调（spec §5.3）：不可读文件被 FilterUnreadableFiles 剔除时，
  /// 必须通过 onSkipped 上报「文件 + 原因类别」，补上 7z/加密 ZIP 预检的静默缺口。
  /// </summary>
  public class ReadErrorHandlerSkipReasonTests
    {
      [Fact]
      public void FilterUnreadableFiles_UnreadableFile_ReportsSkipWithReason()
      {
          // 构造不可读源文件：写入后以独占写锁持有，共享读打开将抛 IOException
          var dir = Path.Combine(Path.GetTempPath(), "mantiszip-test-" + Guid.NewGuid().ToString("N"));
          Directory.CreateDirectory(dir);
          var lockedFile = Path.Combine(dir, "locked.bin");
          File.WriteAllBytes(lockedFile, new byte[16]);
          try
          {
              using var exclusive = new FileStream(lockedFile, FileMode.Open, FileAccess.Write, FileShare.None);
              var reported = new List<(string Path, EntryFailureReason Reason)>();
              var options = new ArchiveOptions
              {
                  // Skip 决策：让 EnsureReadable 剔除而非抛出
                  ErrorResolver = _ => FileErrorAction.Skip,
              };

              var result = ReadErrorHandler.FilterUnreadableFiles(
                  new[] { lockedFile }, options, CancellationToken.None,
                  (path, reason) => reported.Add((path, reason)));

              Assert.Empty(result);
              var hit = Assert.Single(reported);
              Assert.Equal(lockedFile, hit.Path);
              Assert.Equal(EntryFailureReason.IoFailed, hit.Reason);
          }
          finally
          {
              try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录尽力清理 */ }
          }
      }

      [Fact]
      public void FilterUnreadableFiles_ReadbleFile_NoCallback()
      {
          var file = Path.GetTempFileName();
          try
          {
              var reported = new List<(string Path, EntryFailureReason Reason)>();
              var result = ReadErrorHandler.FilterUnreadableFiles(
                  new[] { file }, null, CancellationToken.None,
                  (path, reason) => reported.Add((path, reason)));

              Assert.Single(result);
              Assert.Empty(reported);
          }
          finally { File.Delete(file); }
      }
  }
  ```

  (4b) 运行确认编译级红：`dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ReadErrorHandlerSkipReasonTests"` → 预期 `CS1739/CS7036`（`FilterUnreadableFiles` 无 `onSkipped` 重载）。

  (4c) `ReadErrorHandler` 改造 —— `FilterUnreadableFiles` 签名加可选回调、`EnsureReadable` 加 `out` 原因：

  ```csharp
      /// <param name="onSkipped">可选回调：文件被剔除时上报（EntryKey, 原因类别），供引擎补报 Skipped 事件（spec §5.3）。</param>
      public static List<string> FilterUnreadableFiles(
          IEnumerable<string> filePaths,
          ArchiveOptions? options,
          CancellationToken cancellationToken,
          Action<string, EntryFailureReason>? onSkipped = null)
      {
          var result = new List<string>();
          foreach (var path in filePaths)
          {
              cancellationToken.ThrowIfCancellationRequested();
              // 目录条目无法以读方式打开文件（OpenRead 抛 UnauthorizedAccessException），
              // 且目录本身也不需要读权限校验 —— 只对真正的文件做可读性预检，目录原样保留。
              if (File.Exists(path) && !EnsureReadable(path, options, out var reason))
              {
                  onSkipped?.Invoke(path, reason ?? EntryFailureReason.Other);
                  continue;
              }
              result.Add(path);
          }
          return result;
      }
  ```

  `EnsureReadable` 改造（:46 起；方法首行与 catch 内赋值 `reason`）：

  ```csharp
      private static bool EnsureReadable(string fullPath, ArchiveOptions? options, out EntryFailureReason? reason)
      {
          reason = null;
          int retries = 3;
          while (true)
          {
              try
              {
                  // 共享读：与 ReadFileWithRetry 同款语义，源文件可被编辑器以写权限持有时不冲突
                  using var fs = SharedReadStream.OpenRead(fullPath);
                  return true;
              }
              catch (OperationCanceledException) { throw; }
              catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
              {
                  reason = FailureReasonMapper.FromException(ex); // ← 新增：捕获根因类别
                  retries--;
                  // ……以下 ErrorResolver 决策逻辑与原样保持一致（重试/跳过/中止）
  ```

  (4d) 新增 EntryKey 报告器（单点化相对路径逻辑）—— `ReadErrorHandler` 追加：

  ```csharp
      /// <summary>
      /// 为预检剔除构造 Skipped 上报回调：EntryKey 相对任一源目录前缀优先，退化为文件名（spec §5.3）。
      /// </summary>
      public static Action<string, EntryFailureReason> CreateSkippedReporter(
          IProgress<ArchiveProgress>? progress, IReadOnlyList<string> sourcePaths)
      {
          return (fullPath, reason) =>
          {
              var key = fullPath;
              foreach (var sp in sourcePaths)
              {
                  if (!string.IsNullOrEmpty(sp) && Directory.Exists(sp)
                      && fullPath.StartsWith(sp, StringComparison.OrdinalIgnoreCase))
                  {
                      key = Path.GetRelativePath(sp, fullPath);
                      break;
                  }
              }
              if (key == fullPath) key = Path.GetFileName(fullPath);
              progress?.Report(new ArchiveProgress
              {
                  EntryKey = key,
                  EntryStatus = ArchiveEntryStatus.Skipped,
                  EntryFailureReason = reason,
              });
          };
      }
  ```

  (4e) 两个预检调用点接线 —— `ZipEngine.cs:1381` 与 `SevenZipEngine.cs:599`：

  ```csharp
  // 旧
  var validated = ReadErrorHandler.FilterUnreadableFiles(sourceFilePaths, options, cancellationToken);
  // 新（SevenZipEngine 处第一个参数名为 files，sourcePaths 换成 files）
  var validated = ReadErrorHandler.FilterUnreadableFiles(sourceFilePaths, options, cancellationToken,
      ReadErrorHandler.CreateSkippedReporter(progress, sourceFilePaths));
  ```

  (4f) `ReadFileWithRetry` 带出跳过原因 —— `ZipEngine.cs:3285` 签名末尾加 `out EntryFailureReason? skipReason`，方法体：

  ```csharp
      private bool ReadFileWithRetry(string fullPath, string relativePath,
          ArchiveOptions options, ZipWriter zipWriter,
          ref long processedBytes, long totalBytes, int totalFiles, ref int processedFiles,
          CancellationToken ct, IProgress<ArchiveProgress>? progress,
          ref DateTime lastReportTime, Stream fsOut, out EntryFailureReason? skipReason)
      {
          skipReason = null;
          // ……方法体原样，仅 catch 内 Skip 分支前补一行：
          // catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) 内、
          // if (action == FileErrorAction.Skip) 之前：
                skipReason = FailureReasonMapper.FromException(ex);
  ```

  调用点 `ZipEngine.cs:1761-1769`：

  ```csharp
  // 旧
  if (!ReadFileWithRetry(fullPath, relativePath, options, zipWriter,
          ref processedBytes, totalBytes, totalFiles, ref processedFiles,
          cancellationToken, progress, ref lastReportTime, fsOut))
  // 新
  if (!ReadFileWithRetry(fullPath, relativePath, options, zipWriter,
          ref processedBytes, totalBytes, totalFiles, ref processedFiles,
          cancellationToken, progress, ref lastReportTime, fsOut, out var skipReason))
  {
      if (cancellationToken.IsCancellationRequested) break;
      // 逐条目状态（D2）：读取被跳过 → Skipped（spec §5.3：携带根因类别进黄段）
      progress?.Report(new ArchiveProgress { EntryKey = relativePath, EntryStatus = ArchiveEntryStatus.Skipped, FileTotalBytes = SafeFileSize(fullPath), EntryFailureReason = skipReason ?? EntryFailureReason.Other });
      continue;
  }
  ```

  (4g) `TarWriteFileWithRetry` 同构改造 —— `TarGzEngine.cs:344` 签名加 `out EntryFailureReason? skipReason`（方法首行 `skipReason = null;`、catch 的 Skip 分支前 `skipReason = FailureReasonMapper.FromException(ex);`），调用点 :281-287：

  ```csharp
  if (!TarWriteFileWithRetry(fullPath, relativePath, options, writer, cancellationToken, out var skipReason))
  {
      if (cancellationToken.IsCancellationRequested) break;
      // 逐条目状态（D2）：读取失败被跳过 → Skipped（携带根因类别进黄段）
      progress?.Report(new ArchiveProgress { EntryKey = relativePath, EntryStatus = ArchiveEntryStatus.Skipped, EntryFailureReason = skipReason ?? EntryFailureReason.Other });
      continue;
  }
  ```

  (4h) 重跑 (4b) 同命令 + 全量 Core 测试，预期：**2 条新测试 Passed + 既有全绿**。

  **Must NOT do**:
  - 不改 `EnsureReadable` 的重试/决策语义（仅加原因捕获）
  - 不在 `Abort`/重试耗尽抛出路径做任何上报（整包异常维持既有 `SetErrorSummary` 收口）
  - 被跳过的源文件计数**不并入** `ConflictStatsCounter`（D9）

  **Recommended Agent Profile**: Category `smart`，Skills `[]`

  **Parallelization**: Can Run In Parallel NO（与任务 3 同文件）；Blocked By: 1/3；Blocks: 5

  **Acceptance Criteria**:
  - [ ] 2 条新测试通过；`rg -n "out EntryFailureReason" src\MantisZip.Core\Engines` → ZipEngine/TarGzEngine 各 1 签名 + 2 调用点
  - [ ] Core 构建 0 错误；既有测试无回归

---

- [ ] 5. ProgressViewModel：红/黄段聚合 + 自动按下 📌（TDD）

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`
    - `SetProgress` EntryStatus 分支（:591-598）插入聚合调用
    - `SetErrorSummary`（搜方法名定位）改为写整包头 + 重算
    - `UpdateBatchItemStatus`（:1145 附近）加失败自动按下
    - `ClearEntryItems`（:744 附近）清空聚合状态
    - 新增 observable 字段（挨着 `_errorSummaryText`:182）
  - Test: `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs`

  **What to do**:

  (5a) 写失败测试 —— `ProgressViewModelTests.cs` 顶部 usings 补 `using MantisZip.Core.Abstractions;`（若已有则跳过），文件末尾 `}` 之前追加（编译级红：新成员不存在）：

  ```csharp
      // ═══════════════════════════════════════════════════════════
      //  失败/跳过摘要红黄分层聚合（spec §5/§8 D4–D6）
      // ═══════════════════════════════════════════════════════════

      [Fact]
      public void SetProgress_FailedWithReason_BuildsRedSummary_AndPinsOpen()
      {
          var vm = new ProgressViewModel();
          Assert.False(vm.KeepOpenOnComplete);

          vm.SetProgress(new ArchiveProgress
          {
              EntryKey = "a/b.txt",
              EntryStatus = ArchiveEntryStatus.Failed,
              EntryFailureReason = EntryFailureReason.PermissionDenied,
          });

          Assert.True(vm.HasErrors);
          Assert.False(vm.HasSkippedEntries);                    // 黄段不因红段出现
          Assert.Contains("a/b.txt", vm.ErrorSummaryText);       // 红段含条目
          Assert.Contains(LocalizationManager.T("Error_Reason_PermissionDenied"), vm.ErrorSummaryText);
          Assert.True(vm.KeepOpenOnComplete);                    // D10 触发点 1：红段自动按下 📌
      }

      [Fact]
      public void SetProgress_SkippedWithReason_BuildsYellowSummary_WithoutHasErrors()
      {
          var vm = new ProgressViewModel();

          vm.SetProgress(new ArchiveProgress
          {
              EntryKey = "c.dat",
              EntryStatus = ArchiveEntryStatus.Skipped,
              EntryFailureReason = EntryFailureReason.IoFailed,
          });

          Assert.False(vm.HasErrors);                            // D4：黄段不触发红段
          Assert.True(vm.HasSkippedEntries);
          Assert.Contains("c.dat", vm.SkippedSummaryText);
          Assert.False(vm.KeepOpenOnComplete);                   // 仅黄段不钉（D4）
      }

      [Fact]
      public void SetProgress_SkippedWithoutReason_NotCollected()
      {
          var vm = new ProgressViewModel();

          vm.SetProgress(new ArchiveProgress
          {
              EntryKey = "conflict.txt",
              EntryStatus = ArchiveEntryStatus.Skipped,          // 解压冲突跳过：无原因
          });

          Assert.False(vm.HasSkippedEntries);                    // D5：黄段只收带原因跳过
          Assert.False(vm.HasErrors);
      }

      [Fact]
      public void SetProgress_DuplicateEntryKey_CollapsedToSingleLine()
      {
          var vm = new ProgressViewModel();
          for (int i = 0; i < 3; i++)
              vm.SetProgress(new ArchiveProgress
              {
                  EntryKey = "dup.txt",
                  EntryStatus = ArchiveEntryStatus.Failed,
                  EntryFailureReason = EntryFailureReason.Other,
              });

          Assert.True(vm.HasErrors);
          int lines = vm.ErrorSummaryText!.Split('\n').Length;
          Assert.Equal(1, lines);                                // D6：按 EntryKey 去重
      }

      [Fact]
      public void SetProgress_MoreThan20Failures_TruncatesWithMoreLine()
      {
          var vm = new ProgressViewModel();
          for (int i = 0; i < 25; i++)
              vm.SetProgress(new ArchiveProgress
              {
                  EntryKey = $"f{i:D2}.bin",
                  EntryStatus = ArchiveEntryStatus.Failed,
                  EntryFailureReason = EntryFailureReason.IoFailed,
              });

          var lines = vm.ErrorSummaryText!.Split('\n');
          // 20 行上限（19 明细 + 1 溢出提示），无整包头 → 总行数 = 20
          Assert.Equal(20, lines.Length);
          Assert.Contains(LocalizationManager.T("Progress_ErrorSummary_More", 6), lines[^1]);
      }

      [Fact]
      public void CombinedSummaryText_RedThenYellow_SingleNewline()
      {
          var vm = new ProgressViewModel();
          vm.SetProgress(new ArchiveProgress { EntryKey = "bad.txt", EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.Other });
          vm.SetProgress(new ArchiveProgress { EntryKey = "skip.txt", EntryStatus = ArchiveEntryStatus.Skipped, EntryFailureReason = EntryFailureReason.IoFailed });

          var combined = vm.CombinedSummaryText!;
          Assert.StartsWith(vm.ErrorSummaryText, combined, StringComparison.Ordinal);
          Assert.Contains("\n" + vm.SkippedSummaryText!, combined, StringComparison.Ordinal);
          // 复制契约：红段 + 单换行 + 黄段，无空行
          Assert.DoesNotContain("\n\n", combined, StringComparison.Ordinal);
      }

      [Fact]
      public void SetErrorSummary_WholeArchiveError_PinsOpen_AndKeepsEntryLines()
      {
          var vm = new ProgressViewModel();
          vm.SetProgress(new ArchiveProgress { EntryKey = "x.txt", EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.IoFailed });

          vm.SetErrorSummary("disk full");

          Assert.True(vm.KeepOpenOnComplete);                    // D10 触发点 1
          Assert.StartsWith("disk full", vm.ErrorSummaryText!, StringComparison.Ordinal);
          Assert.Contains("x.txt", vm.ErrorSummaryText);         // 整包头 + 逐条目行并存
      }

      [Fact]
      public void UpdateBatchItemStatus_Failed_PinsOpen_OtherStatusDoesNot()
      {
          var vm = new ProgressViewModel();
          vm.InitBatchMode(new[] { "item" }, 1);                 // 签名不同则按既有测试用法播种

          vm.UpdateBatchItemStatus(0, BatchItemStatus.Failed, "boom");
          Assert.True(vm.KeepOpenOnComplete);                    // D10 触发点 2

          var vm2 = new ProgressViewModel();
          vm2.InitBatchMode(new[] { "item" }, 1);
          vm2.UpdateBatchItemStatus(0, BatchItemStatus.Completed);
          Assert.False(vm2.KeepOpenOnComplete);                  // 完成不钉
      }

      [Fact]
      public void IsTestFlow_True_PinsOpen()
      {
          var vm = new ProgressViewModel();
          Assert.False(vm.KeepOpenOnComplete);
          vm.IsTestFlow = true;
          Assert.True(vm.KeepOpenOnComplete);                    // D10 触发点 3
      }

      [Fact]
      public void ClearEntryOutcomes_ResetsSummaryAndCounts()
      {
          var vm = new ProgressViewModel();
          vm.SetProgress(new ArchiveProgress { EntryKey = "a", EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.Other });
          Assert.True(vm.HasErrors);

          vm.ClearEntryOutcomes();

          Assert.False(vm.HasErrors);
          Assert.False(vm.HasSkippedEntries);
          Assert.Null(vm.ErrorSummaryText);
          Assert.Null(vm.SkippedSummaryText);
          Assert.Null(vm.CombinedSummaryText);
      }
  ```

  > 注：`InitBatchMode`/`UpdateBatchItemStatus` 的实际签名以既有实现为准（rg `InitBatchMode` 定位），播种方式照抄既有 batch 测试；若已有等价播种方法（如 `SeedEntryItems`/`InitBatchMode` 变体）则用既有方法。

  (5b) 运行确认编译级红：`dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"` → 预期新用例因成员不存在而编译失败（预期红）。

  (5c) 实现 —— `ProgressViewModel.cs`：

  (5c-1) 新增 observable 字段（挨着 `_errorSummaryText`:182）：

  ```csharp
      [ObservableProperty]
      private string? _skippedSummaryText;

      [ObservableProperty]
      private bool _hasSkippedEntries;

      /// <summary>复制全文（红段 + 单换行 + 黄段；空段不产空行）。错误摘要复制按钮读此值。</summary>
      [ObservableProperty]
      private string? _combinedSummaryText;

      /// <summary>测试压缩包流程：置 true 时自动按下 📌（完成后不自动关闭，D10 触发点 3）。</summary>
      [ObservableProperty]
      private bool _isTestFlow;
  ```

  (5c-2) 自动按下钩子（D10：触发点收敛到失败事实写入处）：

  ```csharp
      // D10 触发点 1：红段写入（逐条目失败聚合 或 SetErrorSummary 整包异常）→ 自动按下 📌
      partial void OnHasErrorsChanged(bool value)
      {
          if (value) KeepOpenOnComplete = true;
      }

      // D10 触发点 3：测试流窗口创建后 → 自动按下 📌
      partial void OnIsTestFlowChanged(bool value)
      {
          if (value) KeepOpenOnComplete = true;
      }
  ```

  触发点 2（批行置 Failed）在 `UpdateBatchItemStatus` 内 `_batchItems[index].Status = status;` 之后加：

  ```csharp
          // D10 触发点 2：批处理行失败终态 → 自动按下 📌（含 PasswordRetryCancelledException 标记的行）
          if (status == BatchItemStatus.Failed)
              KeepOpenOnComplete = true;
  ```

  (5c-3) 红/黄段聚合 —— 新增私有字段与方法（放 `CompleteWithErrors`:1161 附近）：

  ```csharp
      // ════════════════════════════════════════════
      //  失败/跳过原因聚合（spec §5，红/黄分层 D4/D5，20 行上限 + 去重 D6）
      // ════════════════════════════════════════════

      /// <summary>红段逐条目失败（EntryKey → 原因；按 EntryKey 去重，first-wins）。</summary>
      private readonly Dictionary<string, EntryFailureReason> _failedEntries = new(StringComparer.Ordinal);

      /// <summary>黄段压缩端带原因跳过（仅带原因的 Skipped；解压冲突跳过不进，D5）。</summary>
      private readonly Dictionary<string, EntryFailureReason> _skippedEntries = new(StringComparer.Ordinal);

      /// <summary>整包异常头（SetErrorSummary 写入；红段首行）。</summary>
      private string? _wholeArchiveError;

      /// <summary>D6：红段与黄段各自最多 20 行，超出以溢出提示行收尾。</summary>
      private const int SummaryMaxLines = 20;

      /// <summary>
      /// 逐条目终态聚合（SetProgress EntryStatus 分支调用，早返回之前）。
      /// 红段收 Failed；黄段只收带原因的 Skipped（压缩端读失败，spec §5.3）。
      /// </summary>
      private void CollectEntryOutcome(string key, ArchiveEntryStatus status, EntryFailureReason? reason)
      {
          if (status == ArchiveEntryStatus.Failed)
          {
              if (_failedEntries.TryAdd(key, reason ?? EntryFailureReason.Other))
                  RebuildSummaryTexts();
          }
          else if (status == ArchiveEntryStatus.Skipped && reason.HasValue)
          {
              if (_skippedEntries.TryAdd(key, reason.Value))
                  RebuildSummaryTexts();
          }
      }

      /// <summary>重算红段/黄段/复制全文三个可见文本（失败事实写入的唯一出口）。</summary>
      private void RebuildSummaryTexts()
      {
          ErrorSummaryText = BuildSummaryText(_wholeArchiveError, _failedEntries,
              static (key, reason) => LocalizationManager.T("Progress_Failed_EntryFmt", key, ReasonText(reason)));
          SkippedSummaryText = BuildSummaryText(null, _skippedEntries,
              static (key, reason) => LocalizationManager.T("Progress_Skipped_EntryFmt", key, ReasonText(reason)));

          HasErrors = !string.IsNullOrEmpty(ErrorSummaryText);
          HasSkippedEntries = !string.IsNullOrEmpty(SkippedSummaryText);

          // 复制契约（spec §6）：红段 + 单换行 + 黄段；空段不出现在拼接中
          CombinedSummaryText = (ErrorSummaryText, SkippedSummaryText) switch
          {
              ({ } r, { } s) => r + "\n" + s,
              ({ } r, null) => r,
              (null, { } s) => s,
              _ => null,
          };
      }

      private static string BuildSummaryText(
          string? header,
          Dictionary<string, EntryFailureReason> entries,
          Func<string, EntryFailureReason, string> format)
      {
          var lines = new List<string>();
          if (!string.IsNullOrEmpty(header))
              lines.Add(header!);

          int shown = 0;
          foreach (var (key, reason) in entries)
          {
              // D6：明细最多占 SummaryMaxLines-1 行，留 1 行给溢出提示（无溢出时不占该行）
              if (lines.Count >= SummaryMaxLines - 1 && shown + 1 < entries.Count)
              {
                  lines.Add(LocalizationManager.T("Progress_ErrorSummary_More", entries.Count - shown));
                  return string.Join("\n", lines);
              }
              lines.Add(format(key, reason));
              shown++;
          }
          return lines.Count == 0 ? null! : string.Join("\n", lines);
      }

      private static string ReasonText(EntryFailureReason reason) => reason switch
      {
          EntryFailureReason.PermissionDenied => LocalizationManager.T("Error_Reason_PermissionDenied"),
          EntryFailureReason.IoFailed => LocalizationManager.T("Error_Reason_IoFailed"),
          _ => LocalizationManager.T("Error_Reason_Other"),
      };

      /// <summary>清空红/黄段聚合（清批次入口调用；不回退已按下的 📌——用户可见状态不隐式复位）。</summary>
      public void ClearEntryOutcomes()
      {
          _failedEntries.Clear();
          _skippedEntries.Clear();
          _wholeArchiveError = null;
          ErrorSummaryText = null;
          SkippedSummaryText = null;
          CombinedSummaryText = null;
          HasErrors = false;
          HasSkippedEntries = false;
      }
  ```

  > 截断计算（与实现一致）：无头 → 19 明细 + 1 提示 = 20 行，提示计数 = `25 - 19 = 6`；有头 → 1 头 + 18 明细 + 1 提示 = 20 行，计数 = `25 - 18 = 7`。恰好 `entries.Count == 19/20` 且无溢出时不产提示行（`shown + 1 < entries.Count` 守卫）。5a 的 25 条用例无头，断言 20 行 + `Progress_ErrorSummary_More(6)`。执行时若实际行为与此不符，**以实现为准回改测试期望值并在注释写明计算式**，勿为过测试而削弱实现。

  (5c-4) `SetProgress` EntryStatus 分支（:591-598）插入聚合（**在早返回之前**）：

  ```csharp
          if (p.EntryStatus.HasValue && !string.IsNullOrEmpty(p.EntryKey))
          {
              // 红/黄段聚合（spec §5）：必须在早返回前，分支部内不参与速度采样
              CollectEntryOutcome(p.EntryKey, p.EntryStatus.Value, p.EntryFailureReason);

              // fileSize 末参：……（既有注释原样保留）
              UpdateEntryStatus(p.EntryKey,
                  EntryProgressItem.MapEntryStatus(p.EntryStatus.Value), null, p.FileTotalBytes, p.EntryCompressedBytes);
              return;
          }
  ```

  (5c-5) `SetErrorSummary(string message)` 改写（保留既有公共签名，调用方零改动）：

  ```csharp
      public void SetErrorSummary(string message)
      {
          if (string.IsNullOrEmpty(message)) return;
          _wholeArchiveError = message;
          RebuildSummaryTexts();     // 会经 OnHasErrorsChanged 自动按下 📌（D10 触发点 1）
      }
  ```

  (5c-6) `ClearEntryItems`（:744 附近，清条目行的既有入口）末尾调用 `ClearEntryOutcomes();`，保证换批次时摘要不残留。

  (5d) XAML 绑定 key 的登记（规则 13）：红/黄段文本全部由 C# `T()` 生成，**不进 `LocalizedStrings` 字典**；`Progress_AutoCloseSuffix` 与两个开关 tooltip 的登记归任务 10/11。

  (5e) 重跑 (5b) 同命令，预期：**新增 11 条全绿**。

  **Must NOT do**:
  - 黄段写入**不得**触发 `HasErrors`（D4）——`HasErrors` 只由红段文本推导
  - 不用 `[NotifyPropertyChangedFor]` 逐字段标注，派生通知走既有集中模式
  - `OnHasErrorsChanged` 只在 false→true 方向按下 📌，用户抬起的 📌 不被回写（D11）
  - 不改 `HasErrors` 既有消费点（错误摘要红段可见性）的绑定语义

  **Recommended Agent Profile**: Category `smart`，Skills `["test-driven-development"]`

  **Parallelization**: Can Run In Parallel NO（与任务 10 同文件）；Blocked By: 1/2/3/4；Blocks: 6/7/8

  **Acceptance Criteria**:
  - [ ] 新增 11 条测试通过 + 既有 ProgressViewModelTests 无回归
  - [ ] 红段：`HasErrors=true` 且 `KeepOpenOnComplete=true`；纯黄段：`HasSkippedEntries=true` 且 `HasErrors=false`
  - [ ] UI 构建 0 错误

---

- [ ] 6. 关闭链统一：RunWithProgress / ExtractFlow / App CLI 收口 + `AutoCloseOrWaitAsync` 去参

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs`（`RunWithProgress` 闭包 :268-336）
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（新增 pending/引用属性 :145 附近）
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`（`AutoCloseOrWaitAsync`:206 删 `delayMs` 参数；新增 `IsTestFlow` passthrough）
  - Modify: `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs`（:132 finally 无条件关窗、:142 失败弹窗时序）
  - Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs`（CLI 四处失败收口 + 删 `WaitForWindowCloseAsync`:1629 + 删 2500 魔法数字）

  **What to do**:

  (6a) `MainWindowViewModel` 新增三个桥接属性（挨着 `PendingCompressFlow`:145）：

  ```csharp
      /// <summary>测试流标记：传给进度窗口自动按下 📌（D10 触发点 3，完成后不自动关闭）。</summary>
      public bool PendingTestFlow { get; set; }

      /// <summary>完成回调：RunWithProgress 在关窗决策前把最终状态写进进度窗口（spec §9.4 测试结果通道）。</summary>
      public Action<ProgressWindow>? PendingCompletedCallback { get; set; }

      /// <summary>最近一次 RunWithProgress 创建的进度窗口（后置开关消费点回读最终值用，spec §11.3）。</summary>
      public ProgressWindow? LastProgressWindow { get; set; }
  ```

  > `MainWindowViewModel` 需 `using MantisZip.UI.Avalonia.Dialogs;`（若已存在跳过）。

  (6b) `ProgressWindow` 加 `IsTestFlow` passthrough（挨着 `IsCompressFlow`:129）：

  ```csharp
      /// <summary>测试压缩包流程：置 true 自动按下 📌（完成后不自动关闭）。须在窗口 Show 前设置。</summary>
      public bool IsTestFlow
      {
          get => _vm.IsTestFlow;
          set => _vm.IsTestFlow = value;
      }
  ```

  (6c) `AutoCloseOrWaitAsync` 删 `delayMs` 参数，内部读设置 —— 本任务只改签名与延时来源（倒计时 UI 归任务 10）：

  ```csharp
      /// <summary>
      /// 完成后关闭策略：KeepOpenOnComplete（📌）为 true → 等待手动关闭；
      /// 否则按 AppSettings.AutoCloseDelaySeconds 倒计时后关闭（0 = 立即）。
      /// 倒计时期间按下 📌 → 立即转等待手动关闭。
      /// </summary>
      public async Task AutoCloseOrWaitAsync(Action closeAction)
      {
          if (_vm.KeepOpenOnComplete)
          {
              await WaitForManualCloseAsync();
              closeAction();
              return;
          }

          var delayMs = Math.Clamp(AppSettings.Load().AutoCloseDelaySeconds, 0, 60) * 1000;
          if (delayMs <= 0)
          {
              closeAction();
              return;
          }

          int remaining = delayMs;
          while (remaining > 0)
          {
              await Task.Delay(Math.Min(100, remaining));
              remaining -= 100;
              // 倒计时中按下 📌 → 立即转手动等待（按钮状态 = 实际行为，D11）
              if (_vm.KeepOpenOnComplete)
              {
                  await WaitForManualCloseAsync();
                  closeAction();
                  return;
              }
          }
          closeAction();
      }
  ```

  > `AppSettings` 需 `using MantisZip.UI.Avalonia.Models;`（若已存在跳过）。`WaitForManualCloseAsync` 为既有方法名（rg 复核；若实际名不同用实际名）。任务 10 在本方法上加倒计时上报与编辑冻结——**本任务先保证语义正确**（2500→3000、0→3000 的行为变化已获用户接受）。

  (6d) `MainWindow.axaml.cs` `RunWithProgress` 闭包改造（:268-336）——目标形态：

  ```csharp
          vm.RunWithProgress = async (title, filePaths, operation) =>
          {
              var pw = new ProgressWindow(title);
              pw.InitCancellation();
              // Task 6: 压缩流程标记（供压缩通道说明行显示；解压流程保持 false）
              pw.IsCompressFlow = vm.PendingCompressFlow;
              vm.PendingCompressFlow = false;
              // spec §9: 测试流 → 自动按下 📌（完成后不自动关闭）
              pw.IsTestFlow = vm.PendingTestFlow;
              vm.PendingTestFlow = false;
              // spec §9.4: 完成回调（测试结果写进窗口）；无回调时走默认完成文案
              var onCompleted = vm.PendingCompletedCallback;
              vm.PendingCompletedCallback = null;
              // spec §11.3: 消费点回读后置开关最终值
              vm.LastProgressWindow = pw;
              // 列表播种（既有逻辑原样保留）
              var seedRequest = vm.PendingCompressRequest;
              vm.PendingCompressRequest = null;
              if (seedRequest is not null)
                  CompressFlow.TrySeedEntryItemsInBackground(pw, seedRequest, pw.CancellationToken);
              var hasFileList = filePaths is { Count: > 0 };

              vm.BatchStatusReporter = (index, status) =>
              {
                  pw.SetCurrentBatchItem(index);
                  pw.UpdateBatchItemStatus(index, status);
              };

              try
              {
                  pw.Show();
                  // …… 既有 try 体内 SetCurrentBatchItem / CreateProgress / operation 调用原样保留

                  await operation(progress, pw.CancellationToken);

                  if (hasFileList)
                      pw.UpdateBatchItemStatus(0, BatchItemStatus.Completed);

                  if (onCompleted is not null)
                      onCompleted(pw);                                   // 测试结果写窗口，关窗决策前
                  else
                      pw.SetComplete(LocalizationManager.T("Cli_StatusDone"));

                  await pw.AutoCloseOrWaitAsync(() => pw.Close());       // 延时读设置；📌/失败 → 等手动关闭
                  return true;
              }
              catch (OperationCanceledException)
              {
                  // D13: 取消不算错误 —— 无摘要、不钉、即时关窗
                  pw.Close();
                  return false;
              }
              catch (PasswordRetryCancelledException)
              {
                  // 密码重试被取消 → 批行标 Failed（自动按下 📌），等手动关闭
                  if (hasFileList)
                      pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed);
                  return false;
              }
              catch (Exception ex)
              {
                  // spec §9.2: 补既有吞异常缺口 —— 写摘要（自动按下 📌）+ 落日志
                  pw.SetErrorSummary(ex.Message);
                  App.DebugLog($"[RunWithProgress] operation failed: {ex}");
                  if (hasFileList)
                      pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed);
                  return false;
              }
              finally
              {
                  vm.BatchStatusReporter = null;
              }
          };
  ```

  > **与既有代码的差异核对**（执行时以文件实际为准，**先读整个方法再改**，勿凭本片段覆盖）：① 原 `finally` 中的 `pw.Close()` 移除——关窗职责分派到各分支（成功/失败走 `AutoCloseOrWaitAsync`，取消 `pw.Close()`），finally 只清 reporter；② 原成功分支 `SetComplete(Cli_StatusDone)` 拆成 `onCompleted` 优先；③ 原 `catch (Exception)` 拆三段（`OperationCanceledException` / `PasswordRetryCancelledException` / `Exception`）——若既有代码已有密码取消的细分，保留其结构，本任务只补泛型 catch 的摘要+日志；④ `PasswordRetryCancelledException` 类型名以现场为准（rg 定位）。

  (6e) `ExtractFlow.RunSelectedItemsExtractionAsync` 收口改造（:40-153）——四处：

  1. 创建 `pw` 后加注释占位（真正接线在任务 11）：
     ```csharp
             // spec §11.2: 两个解压后置开关在此声明（任务 11 接线 SetExtractPostActions）
     ```
  2. 泛型 `catch (Exception ex)` 块内补：
     ```csharp
                 // spec §9.2: 摘要写入（自动按下 📌）+ 日志 —— 窗口留驻供查看/复制
                 pw.SetErrorSummary(ex.Message);
                 App.DebugLog($"[ExtractFlow] extraction failed: {ex}");
     ```
  3. **删 `finally { pw.Close(); }`**（:132-135）。
  4. 方法尾部按状态分支收口（失败弹窗保持在窗口关闭**之前**——D12 时序前移）：
     ```csharp
             if (status == SelectedItemsExtractStatus.Failed)
             {
                 // spec §9.3: 先弹既有失败模态框（摘要已写入并自动按下 📌），
                 // 模框关闭后进度窗口仍在 → 📌 已按下 → 转手动等待关闭
                 // （既有 AppMessageBox.Show 逻辑原样保留在此）
                 await pw.AutoCloseOrWaitAsync(() => pw.Close());
             }
             else if (status == SelectedItemsExtractStatus.Cancelled)
             {
                 pw.Close();     // D13: 取消即时关窗
             }
             // 成功路径：既有 try 内 AutoCloseOrWaitAsync(:112) 保持不动
             return new SelectedItemsExtractResult(status, errorMessage);
     ```
     > 关键：原 `finally` 无条件关窗导致摘要无处可看——删除后成功/失败/取消三路径各自收口；`status`/`errorMessage` 变量名以现场为准。`ExtractFlow` 还有第二个方法（`ExtractAsync`:155 起）——rg `pw.Close()` 于本文件，对**每个**无条件关窗点套用同一三分支模式。

  (6f) App CLI 失败收口 —— rg `WaitForWindowCloseAsync` 定位全部调用点（快照 :1257/:1424/:1616/:2361）+ 定义（:1629），统一改：

  ```csharp
             // 旧：SetErrorSummary(...) → CompleteWithErrors() → await WaitForWindowCloseAsync(progressWindow);
             // 新：摘要已自动按下 📌 → 统一入口等待手动关闭（延时被 📌 短路）
             progressWindow.SetErrorSummary(<既有异常取值表达式，原样保留>);
             progressWindow.CompleteWithErrors();
             await progressWindow.AutoCloseOrWaitAsync(() => progressWindow.Close());
  ```

  四处各自已有的异常取值/文案逻辑原样保留，只换等待方式；完成后**删除 `WaitForWindowCloseAsync` 方法定义**。等待完成后，后续既有 `desktop.Shutdown()`/返回逻辑**原样保留**（退出点不前移）。

  (6g) 删 2500/0 魔法数字 —— 四处成功路径 `AutoCloseOrWaitAsync(2500, ...)`（:1262/:1430/:1622/:2377）→ `AutoCloseOrWaitAsync(...)`；`MainWindow.axaml.cs:307` 与 `ExtractFlow.cs:112` 的 `(0, ...)` 同步去参。

  (6h) 构建 + 静态验证：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  git grep -n -E "AutoCloseOrWaitAsync\((2500|0)," -- 'src/*.cs'   # 期望 0 命中
  git grep -n "WaitForWindowCloseAsync" -- 'src/*.cs'               # 期望 0 命中
  ```

  **Must NOT do**:
  - 不移除 `ExtractFlow` 失败模态框（D12 保留；测试失败模态框归任务 7 移除）
  - 不给 `RunWithProgress` 委托加第 4 参（15 个调用点零改动，走 pending 模式）
  - 不在 `catch (OperationCanceledException)` 写摘要/钉 📌（D13）
  - 不改 `ExtractFlow` 成功路径既有关闭时序

  **Recommended Agent Profile**: Category `smart`，Skills `["systematic-debugging"]`

  **Parallelization**: Can Run In Parallel NO；Blocked By: 5；Blocks: 7/8/10/11

  **Acceptance Criteria**:
  - [ ] 两条 `git grep` 静态验证 0 命中
  - [ ] UI 构建 0 错误；既有测试无回归

---

- [ ] 7. TestArchive 结果进窗口 + 移除测试失败模态框

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（`TestArchive`:2836-2877）

  **What to do**:

  (7a) 改造 `TestArchive` —— `testOk` 捕获与密码前置逻辑保留，`RunWithProgress` 调用前设 pending，回调写窗口，删失败模态框：

  ```csharp
          // spec §9: 测试流自动按下 📌 + 结果在关窗前写进窗口（状态行/错误摘要）
          PendingTestFlow = true;
          PendingCompletedCallback = pw =>
          {
              if (testOk)
              {
                  pw.SetComplete(LocalizationManager.T("Status_TestOK"));   // key 名以既有为准
              }
              else
              {
                  // 失败：错误摘要（写入即自动按下 📌）+ 状态行完成文案（按钮翻转为关闭）
                  pw.SetErrorSummary(LocalizationManager.T("Status_ArchiveCorrupted"));
                  pw.SetComplete(LocalizationManager.T("Status_TestFailed"));
              }
          };

          var completed = await RunWithProgress(/* …… 既有三参原样保留 …… */);

          if (!completed)
              return; // 用户取消/异常：状态已在窗口内呈现

          // 既有主窗口状态栏文案保留（rg 既有 Status_* key，勿造新 key）
          StatusMessage = testOk
              ? LocalizationManager.T("Status_TestOK")
              : LocalizationManager.T("Status_TestFailed");

          // D12: 测试失败模态框已移除（结果由进度窗口状态行 + 错误摘要承载，📌 保证留窗）
          App.DebugLog($"[TestArchive] completed: ok={testOk}, archive={CurrentArchivePath}");
  ```

  > 执行核对：① 既有 `Status_*` 测试相关 key（rg `"Status_Test|Status_ArchiveCorrupted` 于 `strings.zh-CN.json`）——**用既有 key，勿造新**；确实缺失才三语补齐并在计划状态注明；② `RunWithProgress` 实际调用形状（委托 3 参、`TestArchiveAsync` 签名）以现场为准，本片段只表达 pending/回调/删模框三件事；③ `pw.SetComplete`（:185）与 `pw.SetStatus`（:172）语义先读——若 `SetComplete` 含「成功色」不合失败场景，改用 `SetStatus(失败色) + SetErrorSummary` 组合。

  (7b) 删除既有失败模态框（:2872-2875 附近 `if (!testOk) AppMessageBox.Show(...)` 块），以 `App.DebugLog` 替代。

  (7c) 构建验证：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误。

  **Must NOT do**:
  - 不动 `TryEnsureArchivePasswordAsync` 密码前置逻辑
  - 不给测试流接解压后置开关（D18：压缩/测试全藏）
  - `ExtractFlow` 失败模态框不动（D12 边界）
  - 不造新 i18n key（优先复用既有）

  **Recommended Agent Profile**: Category `quick`，Skills `[]`

  **Parallelization**: Can Run In Parallel NO；Blocked By: 6；Blocks: 12

  **Acceptance Criteria**:
  - [ ] UI 构建 0 错误；`TestArchive` 方法体内无 `AppMessageBox.Show`
  - [ ] GUI 验收（任务 12）：测试通过/失败两种情况窗口状态行显示结果、不自动关闭、📌 已按下

---

- [ ] 8. XAML：状态行下移 + 双复制按钮 + 错误摘要黄段

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`（Row 3/4 互换 :541-859、Row 5 错误摘要 :861-874）
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`（两个复制点击处理器）
  - Test: `tests/MantisZip.UI.Avalonia.Tests/ProgressWindowXamlTests.cs`

  **What to do**:

  (8a) 写失败测试 —— `ProgressWindowXamlTests.cs` 追加（先红：控件尚不存在）：

  ```csharp
      /// <summary>spec §2/§4：状态行 = 可复制只读 TextBox + 复制按钮；错误摘要 = 红/黄双段 + 复制按钮。</summary>
      [AvaloniaFact]
      public void ProgressWindow_StatusLineAndSummary_HaveCopyControls()
      {
          var window = new ProgressWindow("test");
          window.Show();

          Assert.NotNull(window.FindControl<TextBox>("StatusLineBox"));
          Assert.NotNull(window.FindControl<Button>("StatusCopyBtn"));
          Assert.NotNull(window.FindControl<TextBox>("ErrorSummaryRedBox"));
          Assert.NotNull(window.FindControl<TextBox>("ErrorSummaryYellowBox"));
          Assert.NotNull(window.FindControl<Button>("SummaryCopyBtn"));

          // 行数不变（布局重排只换行序，7 行断言继续有效）
          var grid = Assert.IsType<Grid>(window.Content);
          Assert.Equal(7, grid.RowDefinitions.Count);

          window.Close();
      }

      /// <summary>spec §11.2: 解压后置开关默认隐藏（能力位 false，压缩/测试流全藏）。</summary>
      [AvaloniaFact]
      public void ProgressWindow_PostExtractToggles_DefaultHidden()
      {
          var window = new ProgressWindow("test");
          window.Show();

          var openFolderToggle = window.FindControl<ToggleButton>("OpenFolderToggle");
          var deleteArchiveToggle = window.FindControl<ToggleButton>("DeleteArchiveToggle");
          Assert.NotNull(openFolderToggle);
          Assert.NotNull(deleteArchiveToggle);
          Assert.False(window.GetViewModel().ShowOpenFolderToggle);
          Assert.False(window.GetViewModel().ShowDeleteArchiveToggle);

          window.Close();
      }
  ```

  > `GetViewModel()` 若不存在则 `window.FindControl<...>` 后断言 `IsVisible` 或改用 VM 单测（任务 11 的 VM 测试覆盖默认值），执行时以可编译的最小断言为准。**开关断言若依赖任务 11 的控件，把 `ProgressWindow_PostExtractToggles_DefaultHidden` 移到任务 11 再写**——任务 8 只写前半（状态行 + 摘要控件 + 行数）。

  (8b) 布局重排 —— `ProgressWindow.axaml`：

  1. **行序互换**：把 Row 3 的状态消息 `TextBlock`（:541-547）**移到 Row 4**，把原 Row 4 的信息区 `Border`（:553 起，含 `Grid.Row="4"`）**改为 `Grid.Row="3"`**。`RowDefinitions` 7 行**不动**（`Auto,*,Auto,Auto,Auto,Auto,Auto` 顺序不变——两行都是 Auto，行数与高度语义不受影响）。
  2. **状态行控件替换**（新 Row 4 内容，14px 可复制，spec §2）：

  ```xml
      <!-- ═══ Row 4: 状态行（原 Row 3 下移到信息区之后；只读可选中，14px，复制按钮见列 1）═══ -->
      <Grid Grid.Row="4" ColumnDefinitions="*,Auto">
        <TextBox x:Name="StatusLineBox"
                 Text="{Binding StatusMessage}"
                 IsReadOnly="True"
                 TextWrapping="Wrap"
                 FontSize="14"
                 FontWeight="SemiBold"
                 Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                 Background="Transparent"
                 BorderThickness="0"
                 Padding="0"
                 IsVisible="{Binding HasStatusMessage}"
                 IsTabStop="False" />
        <!-- 状态行复制按钮：复制 StatusMessage 全文，成功后 ✓ 反馈 + toast（复用 ShowCopyFeedback） -->
        <Button x:Name="StatusCopyBtn"
                Grid.Column="1"
                Click="StatusCopyBtn_Click"
                IsVisible="{Binding HasStatusMessage}"
                Background="{DynamicResource ThemeButtonBgBrush}"
                BorderBrush="{DynamicResource ThemeBorderBrush}"
                BorderThickness="1"
                Height="{DynamicResource ControlHeightSm}"
                Padding="6,2"
                VerticalAlignment="Center">
          <PathIcon Data="{StaticResource IconCopy}" Width="14" Height="14"
                    Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
        </Button>
      </Grid>
  ```

  > `HasStatusMessage` = 新增 VM `[ObservableProperty] bool`，由 `SetStatus`/`SetComplete`/状态写入处推导（`!string.IsNullOrEmpty(StatusMessage)`）；若既有状态行始终可见（准备态占位），可省略该绑定保持常显——执行时按既有行为决定，注释说明。

  3. **错误摘要改双段 + 复制按钮**（Row 5，:861-874 替换）：

  ```xml
      <!-- ═══ Row 5: 错误摘要（红段失败 + 黄段跳过，两行并存；复制按钮聚合两段，spec §4/§6）═══ -->
      <Grid Grid.Row="5" ColumnDefinitions="*,Auto">
        <StackPanel Spacing="0">
          <!-- 红段：整包异常 + 逐条目失败（HasErrors 驱动；既有绑定语义不变） -->
          <TextBox x:Name="ErrorSummaryRedBox"
                   Text="{Binding ErrorSummaryText}"
                   IsReadOnly="True"
                   TextWrapping="Wrap"
                   FontSize="12"
                   Foreground="{DynamicResource ThemeErrorTextBrush}"
                   Background="Transparent"
                   BorderThickness="0"
                   Padding="0"
                   IsVisible="{Binding HasErrors}" />
          <!-- 黄段：压缩端带原因跳过（仅压缩流程出现；不触发 HasErrors，D4） -->
          <TextBox x:Name="ErrorSummaryYellowBox"
                   Text="{Binding SkippedSummaryText}"
                   IsReadOnly="True"
                   TextWrapping="Wrap"
                   FontSize="12"
                   Foreground="{DynamicResource ThemeWarningTextBrush}"
                   Background="Transparent"
                   BorderThickness="0"
                   Padding="0"
                   IsVisible="{Binding HasSkippedEntries}" />
        </StackPanel>
        <!-- 摘要复制按钮：复制 CombinedSummaryText（红段 + 换行 + 黄段） -->
        <Button x:Name="SummaryCopyBtn"
                Grid.Column="1"
                Click="SummaryCopyBtn_Click"
                IsVisible="{Binding HasErrors}"
                Background="{DynamicResource ThemeButtonBgBrush}"
                BorderBrush="{DynamicResource ThemeBorderBrush}"
                BorderThickness="1"
                Height="{DynamicResource ControlHeightSm}"
                Padding="6,2"
                VerticalAlignment="Top">
          <PathIcon Data="{StaticResource IconCopy}" Width="14" Height="14"
                    Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
        </Button>
      </Grid>
  ```

  > 资源键核对：`ThemeErrorTextBrush`/`ThemeWarningTextBrush`/`IconCopy` 是否存在（rg 于 `Resources/` 与 `ThemeLight.axaml`）——**用既有语义键**（错误色/警告色可能叫 `ThemeErrorBrush`/`ThemeAccentBrush` 等）；缺失才在 `ThemeLight.axaml`+`ThemeDark.axaml` 成对加（规则 4）。`IsVisible` 语义按规则 6：红段空 → 整个红段隐藏；复制按钮在红段与黄段**任一**非空时显示（若绑定表达式写不下则加 VM 派生 `HasAnySummary`，走集中通知模式）。

  (8c) code-behind 复制处理器（`ProgressWindow.axaml.cs`，挨着既有复制逻辑 :585-640）：

  ```csharp
      /// <summary>状态行复制：复制 StatusMessage 全文，复用既有 toast 反馈。</summary>
      private async void StatusCopyBtn_Click(object? sender, RoutedEventArgs e)
      {
          var text = _vm.StatusMessage;
          if (string.IsNullOrEmpty(text)) return;
          var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
          if (clipboard != null)
              await clipboard.SetTextAsync(text);
          ShowCopyFeedback(sender);
      }

      /// <summary>错误摘要复制：红段 + 换行 + 黄段（CombinedSummaryText，spec §6 复制契约）。</summary>
      private async void SummaryCopyBtn_Click(object? sender, RoutedEventArgs e)
      {
          var text = _vm.CombinedSummaryText;
          if (string.IsNullOrEmpty(text)) return;
          var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
          if (clipboard != null)
              await clipboard.SetTextAsync(text);
          ShowCopyFeedback(sender);
      }
  ```

  > `_vm` 字段名以现场为准（rg `private.*ProgressViewModel` 于本文件）；`ShowCopyFeedback`（:612）与 toast（:929 `CopyToast`）为既有设施，**不新造 toast**。`ShowCopyFeedback` 原本接收 sender 做图标翻转——确认其内部对非预期 sender 的容错，必要时传 `StatusCopyBtn` 本身。

  (8d) 重跑 (8a) + 全量 XAML 测试：`dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressWindowXamlTests"` → 全绿（含既有 7 行断言）。

  **Must NOT do**:
  - 不增删 `RowDefinitions` 行数（7 行断言保持有效）
  - 不动 `CopyToast` 的 RowSpan=7 叠加结构
  - 不新造 toast/复制反馈机制
  - 状态行/摘要**不设 `Cursor="Hand"` 之类花活**——TextBox 自带文本选择

  **Recommended Agent Profile**: Category `smart`，Skills `["test-driven-development"]`

  **Parallelization**: Can Run In Parallel YES（与任务 9 并行，无文件交集）；Blocked By: 5；Blocks: 12

  **Acceptance Criteria**:
  - [ ] XAML 测试全绿（含 7 行断言 + 新增控件断言）
  - [ ] UI 构建 0 错误
  - [ ] 规则 14：新增控件区域均有中文 `<!-- -->` 注释

---

- [ ] 9. 设置：`AutoCloseDelaySeconds`（AppSettings + 设置窗口外观页）

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（:72/:90 旁新增字段）
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs`（load :970 附近、save :1501 附近、文本属性 :796 附近）
  - Modify: `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml`（外观页 :528-538 `MaxRecentFiles` 小节后）

  **What to do**:

  (9a) `AppSettings` 新增字段（放解压分区附近，:90 后）：

  ```csharp
      // ===== 进度窗口 =====
      /// <summary>完成后自动关闭延时（秒，0–60；0 = 立即关闭）。📌/失败/测试流不受此影响（始终留窗）。</summary>
      public int AutoCloseDelaySeconds { get; set; } = 3;
  ```

  (9b) `SettingsWindowViewModel` 三处（抄 `MaxRecentFiles` 既有模式）：

  ```csharp
      // 文本属性（挨着 ExtractOpenFolderAfterText:796）
      public string AppearanceAutoCloseDelayText => LocalizationManager.T("Settings_ProgressAutoCloseDelay");

      // 字段 + observable（挨着既有 int 型设置字段，抄 MaxRecentFiles 的声明形式）
      [ObservableProperty]
      private int _autoCloseDelaySeconds = 3;

      // LoadFromSettings（:970 附近）
      _autoCloseDelaySeconds = _settings.AutoCloseDelaySeconds;

      // Save（:1501 附近，挨着 OpenFolderAfterExtract 赋值）
      _settings.AutoCloseDelaySeconds = Math.Clamp(AutoCloseDelaySeconds, 0, 60);
  ```

  (9c) `SettingsWindow.axaml` 外观页 —— 在 `MaxRecentFiles` 小节 `Border`（:528-538）之后插入同构小节：

  ```xml
            <!-- 进度窗口：完成后自动关闭延时（秒，0–60，0 = 立即关闭；📌/失败/测试流始终留窗） -->
            <Border BorderBrush="{DynamicResource ThemeBorderBrush}" BorderThickness="1"
                    CornerRadius="{DynamicResource BorderRadius}" Padding="{DynamicResource SpacingSmThk}" Background="{DynamicResource ThemeSurfaceBgBrush}">
              <StackPanel Spacing="{DynamicResource SpacingXs}">
                <TextBlock Text="{Binding AppearanceAutoCloseDelayText}" FontWeight="SemiBold" />
                <TextBox Text="{Binding AutoCloseDelaySeconds}"
                         Width="120" HorizontalAlignment="Left"
                         Background="{DynamicResource ThemeWindowBgBrush}"
                         Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                         BorderBrush="{DynamicResource ThemeBorderBrush}" />
              </StackPanel>
            </Border>
  ```

  > 该 key 已在任务 2 加入三语 JSON（`Settings_ProgressAutoCloseDelay`）。控件为裸 `TextBox` + VM `int` 绑定（与 `MaxRecentFiles` 一致），clamp 在 Save 收口——不引入 NumericUpDown（D16 否决项）。

  (9d) 构建：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误。

  **Must NOT do**:
  - 不在设置页加「延时」以外的进度窗口新控件（倒计时归任务 10 的进度窗口内）
  - 默认值必须是 **3**（用户拍板）

  **Recommended Agent Profile**: Category `quick`，Skills `[]`

  **Parallelization**: Can Run In Parallel YES（与任务 8 并行）；Blocks: 10

  **Acceptance Criteria**:
  - [ ] UI 构建 0 错误；设置保存后 `settings.json` 出现 `AutoCloseDelaySeconds`
  - [ ] GUI：设置页外观区显示新小节，0/3/60/非法输入 保存行为正确

---

- [ ] 10. 倒计时显示 + 行内编辑状态机（TDD）

  **Files:**
  - Create: `src/MantisZip.UI.Avalonia/Utils/CountdownParser.cs`
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`（`AutoCloseOrWaitAsync` 加倒计时上报 + 编辑态）
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`（按钮行 Column 1 插入倒计时控件）
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`（`AutoCloseCountdownVisible` + `LocalizedStrings` 登记 `Progress_AutoCloseSuffix`）
  - Test: `tests/MantisZip.UI.Avalonia.Tests/CountdownParserTests.cs`（新）

  **What to do**:

  (10a) 写失败测试 —— 新建 `tests/MantisZip.UI.Avalonia.Tests/CountdownParserTests.cs`：

  ```csharp
  using MantisZip.UI.Avalonia.Utils;
  using Xunit;

  namespace MantisZip.UI.Avalonia.Tests;

  /// <summary>
  /// 倒计时行内编辑输入解析（spec §10.4）：0–60 夹紧、非法回退当前剩余值。
  /// 纯函数，无 UI 依赖。
  /// </summary>
  public class CountdownParserTests
  {
      [Theory]
      [InlineData("5", 5)]
      [InlineData(" 12 ", 12)]      // 空白容忍
      [InlineData("0", 0)]          // 0 = 立即关闭，合法
      [InlineData("60", 60)]        // 上界
      [InlineData("999", 60)]       // 超上界 → 夹紧
      [InlineData("-3", 0)]         // 负数 → 夹紧 0
      public void ValidNumbers_ClampToRange(string input, int expected)
          => Assert.Equal(expected, CountdownParser.Parse(input, 1));

      [Theory]
      [InlineData(null)]
      [InlineData("")]
      [InlineData("abc")]
      [InlineData("3.5")]           // 非整数 → 回退
      public void InvalidInput_FallsBackToRemaining(string? input)
          => Assert.Equal(7, CountdownParser.Parse(input, 7));
  }
  ```

  (10b) 运行确认红：`dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~CountdownParserTests"` → 编译失败（类型不存在）。

  (10c) 新建 `src/MantisZip.UI.Avalonia/Utils/CountdownParser.cs`：

  ```csharp
  namespace MantisZip.UI.Avalonia.Utils;

  /// <summary>
  /// 自动关闭倒计时行内编辑的输入解析（spec §10.4）：
  /// 有效整数 → 夹紧到 0–60；非法输入 → 回退当前剩余值（fallback 同样夹紧）。
  /// 实例级临时修改，不回写 AppSettings（D16）。
  /// </summary>
  public static class CountdownParser
  {
      public static int Parse(string? text, int fallbackSeconds)
      {
          if (!int.TryParse(text?.Trim(), out var value))
              return Math.Clamp(fallbackSeconds, 0, 60);
          return Math.Clamp(value, 0, 60);
      }
  }
  ```

  (10d) 重跑 (10b) → **全绿**。

  (10e) VM 侧 —— `ProgressViewModel`：

  ```csharp
      /// <summary>自动关闭倒计时可见（仅延时等待期间 true；📌/失败/测试流为手动等待不显示）。</summary>
      [ObservableProperty]
      private bool _autoCloseCountdownVisible;
  ```

  并在 `LocalizedStrings` 字典（:84 起）登记（规则 13，漏登记 = XAML 绑定空白）：

  ```csharp
              ["Progress_AutoCloseSuffix"] = LocalizationManager.T("Progress_AutoCloseSuffix"),
  ```

  (10f) `AutoCloseOrWaitAsync` 升级（在任务 6 版本上加倒计时与编辑冻结）——目标形态：

  ```csharp
      // 倒计时编辑态（仅 UI 线程访问）
      private bool _countdownEditing;
      private int _countdownRemainingMs;

      public async Task AutoCloseOrWaitAsync(Action closeAction)
      {
          if (_vm.KeepOpenOnComplete)
          {
              await WaitForManualCloseAsync();
              closeAction();
              return;
          }

          _countdownRemainingMs = Math.Clamp(AppSettings.Load().AutoCloseDelaySeconds, 0, 60) * 1000;
          if (_countdownRemainingMs <= 0)
          {
              closeAction();
              return;
          }

          _vm.AutoCloseCountdownVisible = true;
          try
          {
              int lastShownSecs = -1;
              while (_countdownRemainingMs > 0)
              {
                  await Task.Delay(100);
                  // D11：倒计时中按下 📌 → 立即转手动等待（按钮状态 = 实际行为）
                  if (_vm.KeepOpenOnComplete)
                  {
                      await WaitForManualCloseAsync();
                      closeAction();
                      return;
                  }
                  // §10.4：行内编辑期间时间冻结（不在编辑时回写文本，避免覆盖用户输入）
                  if (_countdownEditing)
                      continue;

                  _countdownRemainingMs -= 100;
                  int secs = (_countdownRemainingMs + 999) / 1000;   // 向上取整 → 首显满值
                  if (secs != lastShownSecs)
                  {
                      lastShownSecs = secs;
                      UpdateCountdownText(secs);
                  }
              }
              _vm.AutoCloseCountdownVisible = false;
              closeAction();
          }
          finally
          {
              _vm.AutoCloseCountdownVisible = false;
          }
      }

      /// <summary>回写倒计时文本（编辑态不回写，防覆盖输入）。</summary>
      private void UpdateCountdownText(int secs)
      {
          if (this.FindControl<TextBox>("CountdownBox") is { } box && box.IsFocused)
              return;
          box!.Text = secs.ToString();
      }
  ```

  (10g) 编辑态状态机（code-behind，事件处理器）：

  ```csharp
      /// <summary>聚焦即冻结（§10.4）：停止倒数流逝，全选便于直接键入。</summary>
      private void CountdownBox_GotFocus(object? sender, GotFocusEventArgs e)
      {
          _countdownEditing = true;
          if (sender is TextBox box)
              box.SelectAll();
      }

      /// <summary>离开编辑态：以最终有效值重置倒数起点（非法输入回退冻结时的剩余值），恢复流逝。</summary>
      private void CountdownBox_LostFocus(object? sender, RoutedEventArgs e) => CommitCountdownEdit();

      private void CountdownBox_KeyDown(object? sender, KeyEventArgs e)
      {
          if (e.Key == Key.Enter)
          {
              CommitCountdownEdit();
              e.Handled = true;
          }
          else if (e.Key == Key.Escape)
          {
              // Esc = 放弃修改，恢复冻结时剩余值并提交
              if (sender is TextBox box)
                  box.Text = ((_countdownRemainingMs + 999) / 1000).ToString();
              CommitCountdownEdit();
              e.Handled = true;
          }
      }

      private void CommitCountdownEdit()
      {
          if (!_countdownEditing) return;
          _countdownEditing = false;
          if (this.FindControl<TextBox>("CountdownBox") is not { } box) return;

          var frozenSecs = (_countdownRemainingMs + 999) / 1000;   // 冻结期间未流逝 → 等价当前值
          var secs = CountdownParser.Parse(box.Text, frozenSecs);
          _countdownRemainingMs = secs * 1000;                     // 以最终有效值重置起点
          box.Text = secs.ToString();                              // 规范化显示（夹紧后值）
      }
  ```

  > `GotFocusEventArgs`/`RoutedEventArgs`/`KeyEventArgs` 的具体命名空间与类型以现场为准（Avalonia 11：`Avalonia.Input.GotFocusEventArgs`、`Avalonia.Interactivity.RoutedEventArgs`、`Avalonia.Input.KeyEventArgs`）。

  (10h) XAML —— 按钮行（:876 起）Column 1（当前是 `*` 弹性空白）插入倒计时，列定义改 `Auto,*,Auto,Auto`：

  ```xml
        <!-- Column 0:📌 保持打开（既有 ToggleButton 原样） -->
        <!-- Column 1: 倒计时（自动关闭剩余秒数，仅延时等待期显示；可点击临时修改，不回写设置） -->
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4"
                    VerticalAlignment="Center"
                    IsVisible="{Binding AutoCloseCountdownVisible}">
          <TextBox x:Name="CountdownBox"
                   Width="44"
                   TextAlignment="Right"
                   FontSize="13"
                   GotFocus="CountdownBox_GotFocus"
                   LostFocus="CountdownBox_LostFocus"
                   KeyDown="CountdownBox_KeyDown"
                   Background="{DynamicResource ThemeSurfaceBgBrush}"
                   Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                   BorderBrush="{DynamicResource ThemeBorderBrush}" />
          <TextBlock Text="{Binding LocalizedStrings[Progress_AutoCloseSuffix]}"
                     VerticalAlignment="Center"
                     FontSize="12"
                     Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
        </StackPanel>
        <!-- Column 2/3: 暂停 / 取消（既有控件原样，Grid.Column 索引因插入不变——StackPanel 占 1 列） -->
  ```

  > 列序核对：原 4 列 `Auto,*,Auto,Auto` = 📌/空白/暂停/取消；新 5 列 `Auto,*,Auto,Auto`? **否** —— 📌 在列 0、倒计时占列 1（原空白列）、暂停列 2、取消列 3，列数**不变**，只需把原空白 `*` 列改 `Auto` 并在其中放 StackPanel。执行时读按钮行完整 XAML（:876-921）确认列索引。

  (10i) 构建 + 全测试：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  ```

  **Must NOT do**:
  - 不用 `{0}` 格式串包装可编辑数字（D16 否决项——`Progress_AutoCloseSuffix` 无占位符）
  - 不用 `NumericUpDown`（D16 否决项）
  - 行内改动**不回写 `AppSettings`**（D16）——每次 `AutoCloseOrWaitAsync` 进入时重新读设置
  - 编辑聚焦期间不回写文本框（防覆盖）

  **Recommended Agent Profile**: Category `smart`，Skills `["test-driven-development"]`

  **Parallelization**: Can Run In Parallel NO（与任务 6 同文件）；Blocked By: 6/9；Blocks: 12

  **Acceptance Criteria**:
  - [ ] `CountdownParserTests` 全绿；UI 测试全绿；构建 0 错误
  - [ ] GUI：完成态出现「N 秒后自动关闭」倒计时逐秒递减；点击修改/Enter/Esc/失焦行为符合 §10.4；改动不影响设置里的默认值

---

- [ ] 11. 解压后置开关 📁/🗑（VM + XAML + 全流程接线 + TryDelete 签名）

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`（4 个新属性 + 能力位）
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`（passthrough + `SetExtractPostActions`）
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`（按钮行 Column 0 加两个 ToggleButton）
  - Modify: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs`（`RunWithProgress` 消费 pending 能力位）
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（pending 属性 + 各解压入口声明 + 6 处 TryDelete 消费改读）
  - Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs`（`TryDeleteArchiveAfterExtract` 改签名 + CLI 两处挪时序 + CLI 显隐）
  - Modify: `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs`（声明能力位 + 结果记录携带开关值）
  - Modify: `src/MantisZip.UI.Avalonia/Services/DragDropService.cs`（消费改读 + 藏删包开关）
  - Test: `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` + `ProgressWindowXamlTests.cs`

  **What to do**:

  (11a) VM 属性（挨着任务 5 新字段）：

  ```csharp
      // ════════════════════════════════════════════
      //  解压后置动作开关（spec §11；实例级，仅本次生效，不回写设置，D17）
      // ════════════════════════════════════════════

      /// <summary>显示「解压后打开目录」开关（能力位；压缩/测试/CLI 拖拽场景 false，D18）。</summary>
      [ObservableProperty]
      private bool _showOpenFolderToggle;

      /// <summary>显示「解压后移入回收站」开关（能力位；压缩/测试/CLI 场景 false，D18）。</summary>
      [ObservableProperty]
      private bool _showDeleteArchiveToggle;

      /// <summary>本次解压完成后打开目标目录（初值镜像设置或对话框选择；不回写设置）。</summary>
      [ObservableProperty]
      private bool _openFolderAfterExtract;

      /// <summary>本次解压完成后把原压缩包移入回收站（同上）。</summary>
      [ObservableProperty]
      private bool _deleteArchiveAfterExtract;
  ```

  (11b) 写测试（追加到 `ProgressViewModelTests.cs`；先红）：

  ```csharp
      [Fact]
      public void PostExtractToggles_DefaultsAndNoPersist()
      {
          var vm = new ProgressViewModel();
          // 能力位默认 false（压缩/测试流全藏，D18）
          Assert.False(vm.ShowOpenFolderToggle);
          Assert.False(vm.ShowDeleteArchiveToggle);

          var settings = AppSettings.Load();
          vm.SetExtractPostActions(openFolder: true, deleteArchive: true, openFolderSeed: null);

          // 能力位置 true + 初值镜像设置（不回写——再读设置应不变）
          Assert.True(vm.ShowOpenFolderToggle);
          Assert.True(vm.ShowDeleteArchiveToggle);
          Assert.Equal(settings.OpenFolderAfterExtract, vm.OpenFolderAfterExtract);
          Assert.Equal(settings.DeleteArchiveAfterExtract, vm.DeleteArchiveAfterExtract);

          // 用户在窗口内翻转 → 仅实例级，设置不动
          vm.OpenFolderAfterExtract = !vm.OpenFolderAfterExtract;
          Assert.Equal(settings.OpenFolderAfterExtract, AppSettings.Load().OpenFolderAfterExtract);
      }

      [Fact]
      public void SetExtractPostActions_OpenFolderSeed_OverridesSettingMirror()
      {
          var vm = new ProgressViewModel();
          var settings = AppSettings.Load();

          vm.SetExtractPostActions(true, true, openFolderSeed: true);

          Assert.True(vm.OpenFolderAfterExtract);   // 对话框选择优先于设置镜像（§11.3）
      }
  ```

  任务 8 推迟到此处的开关控件断言 —— 本任务 11e XAML 落地后再追加到 `ProgressWindowXamlTests.cs`（`[AvaloniaFact]`；任务 8 阶段不引入，避免编译依赖未存在的控件）：

  ```csharp
      [AvaloniaFact]
      public void ProgressWindow_PostExtractToggles_DefaultHidden()
      {
          var window = new ProgressWindow("test");
          window.Show();

          var openFolderToggle = window.FindControl<ToggleButton>("OpenFolderToggle");
          var deleteArchiveToggle = window.FindControl<ToggleButton>("DeleteArchiveToggle");
          Assert.NotNull(openFolderToggle);
          Assert.NotNull(deleteArchiveToggle);
          Assert.False(window.GetViewModel().ShowOpenFolderToggle);    // 能力位默认 false → IsVisible=false
          Assert.False(window.GetViewModel().ShowDeleteArchiveToggle);

          window.Close();
      }
  ```

  > `GetViewModel()` 若不存在则改断言 `IsVisible`，或只保留 VM 单测——以可编译的最小断言为准。

  (11c) VM 实现 `SetExtractPostActions`（集中初值逻辑，对话框流程与非对话框流程共用）：

  ```csharp
      /// <summary>
      /// 声明解压后置开关能力位并镜像初值（spec §11.3）：
      /// openFolderSeed 非 null → 对话框流程，用对话框选择作初值；否则镜像 AppSettings。
      /// 全部为实例级，不回写设置（D17）。
      /// </summary>
      public void SetExtractPostActions(bool openFolder, bool deleteArchive, bool? openFolderSeed)
      {
          ShowOpenFolderToggle = openFolder;
          ShowDeleteArchiveToggle = deleteArchive;
          if (!openFolder && !deleteArchive) return;   // 能力位关 → 不镜像（保持默认 false）

          var settings = AppSettings.Load();
          OpenFolderAfterExtract = openFolderSeed ?? settings.OpenFolderAfterExtract;
          DeleteArchiveAfterExtract = settings.DeleteArchiveAfterExtract;
      }
  ```

  (11d) `ProgressWindow` passthrough（挨着 `IsTestFlow`）：

  ```csharp
      /// <summary>声明解压后置开关（能力位 + 初值）；须在 Show 前调用。非解压流程不调用 → 两开关隐藏。</summary>
      public void SetExtractPostActions(bool openFolder, bool deleteArchive, bool? openFolderSeed = null)
          => _vm.SetExtractPostActions(openFolder, deleteArchive, openFolderSeed);

      /// <summary>「完成后打开目录」开关当前值（消费点在窗口关闭后回读，spec §11.3）。</summary>
      public bool OpenFolderAfterExtractValue => _vm.OpenFolderAfterExtract;

      /// <summary>「完成后删原包」开关当前值（同上）。</summary>
      public bool DeleteArchiveAfterExtractValue => _vm.DeleteArchiveAfterExtract;
  ```

  (11e) XAML 按钮行 Column 0 —— 既有 📌 `ToggleButton` 外包 `StackPanel`，追加两个开关（图标 `IconFolder`/`IconDelete` 已存在，规则 8 免）：

  ```xml
        <!-- Column 0:左侧簇 = 📌 保持打开 + 解压后置开关（开关仅解压流程显示，D18 能力位控制） -->
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="{DynamicResource SpacingXs}">
          <!-- 📌 保持打开（既有 ToggleButton 原样迁入，IsChecked 绑定 KeepOpenOnComplete 不变） -->
          <!-- …… 既有 KeepOpenToggle 原样 …… -->

          <!-- 解压后打开目标目录（实例级；IsVisible 由能力位驱动 → 压缩/测试流隐藏，规则 6） -->
          <ToggleButton IsChecked="{Binding OpenFolderAfterExtract}"
                        IsVisible="{Binding ShowOpenFolderToggle}"
                        Background="{DynamicResource ThemeButtonBgBrush}"
                        BorderBrush="{DynamicResource ThemeBorderBrush}"
                        Height="{DynamicResource ControlHeightLg}"
                        ToolTip.Tip="{Binding LocalizedStrings[Settings_Extract_OpenFolderAfter]}">
            <PathIcon Data="{StaticResource IconFolder}" Width="16" Height="16"
                      Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
          </ToggleButton>

          <!-- 解压后把原压缩包移入回收站（同上） -->
          <ToggleButton IsChecked="{Binding DeleteArchiveAfterExtract}"
                        IsVisible="{Binding ShowDeleteArchiveToggle}"
                        Background="{DynamicResource ThemeButtonBgBrush}"
                        BorderBrush="{DynamicResource ThemeBorderBrush}"
                        Height="{DynamicResource ControlHeightLg}"
                        ToolTip.Tip="{Binding LocalizedStrings[Settings_Extract_DeleteArchiveAfterExtract]}">
            <PathIcon Data="{StaticResource IconDelete}" Width="16" Height="16"
                      Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
          </ToggleButton>
        </StackPanel>
  ```

  > 两个 tooltip key（`Settings_Extract_OpenFolderAfter`/`Settings_Extract_DeleteArchiveAfterExtract`）**必须登记**进 `ProgressViewModel.LocalizedStrings`（规则 13——XAML 直接绑定）。既有关闭 `CloseBtn`/`PauseBtn`/`CancelBtn` 的列索引核对：若它们在列 2/3 且 📌 原是列 0 单控件，包 StackPanel 后列索引不变。

  (11f) `RunWithProgress` 消费 pending 能力位 —— `MainWindowViewModel` 加：

  ```csharp
      /// <summary>待声明的解压后置开关能力位 + 对话框初值（RunWithProgress 消费，默认 false/null）。</summary>
      public (bool OpenFolder, bool DeleteArchive, bool? OpenFolderSeed)? PendingExtractPostActions { get; set; }
  ```

  `MainWindow.axaml.cs` 闭包内（`pw.IsTestFlow` 之后）：

  ```csharp
              var postActions = vm.PendingExtractPostActions;
              vm.PendingExtractPostActions = null;
              if (postActions is { } pa)
                  pw.SetExtractPostActions(pa.OpenFolder, pa.DeleteArchive, pa.OpenFolderSeed);
  ```

  各解压入口在调用 `RunWithProgress` **之前**设置（`MainWindowViewModel` 内 rg 定位解压命令：`ExtractArchive`/`ExtractHere`/`ExtractToName`/`ExtractTo`/`ExtractSelected`/`SmartExtract` 等）：

  ```csharp
              // 解压流程：两开关可见；对话框流程把「打开文件夹」选择作种子
              PendingExtractPostActions = (true, true, openFolderChoiceFromDialog);
              // 非对话框流程（原地解压/解压到命名目录等）：种子 null → 镜像设置
              PendingExtractPostActions = (true, true, null);
  ```

  (11g) `ExtractFlow.RunSelectedItemsExtractionAsync` —— 把任务 6 的注释占位替换为真实接线，并让结果携带开关值：

  ```csharp
              // spec §11.2: 选中条目解压 → 两开关可见；初值镜像设置（无对话框选择）
              pw.SetExtractPostActions(openFolder: true, deleteArchive: true, openFolderSeed: null);
  ```

  结果记录扩字段（`ExtractFlow.cs:26`）：

  ```csharp
      public sealed record SelectedItemsExtractResult(
          SelectedItemsExtractStatus Status,
          string? ErrorMessage,
          bool OpenFolderAfterExtract = false,
          bool DeleteArchiveAfterExtract = false);
  ```

  返回处把 `pw.OpenFolderAfterExtractValue` / `pw.DeleteArchiveAfterExtractValue` 带出（`return new SelectedItemsExtractResult(status, errorMessage, pw.OpenFolderAfterExtractValue, pw.DeleteArchiveAfterExtractValue);`）。

  (11h) 消费点改造 —— 三类：

  **① `MainWindowViewModel` 既有 6 处 `App.TryDeleteArchiveAfterExtract(...)` 调用**（rg 定位）改为读窗口最终值：

  ```csharp
              // D19: 读进度窗口开关最终值；窗口不存在（异常路径）回退设置
              App.TryDeleteArchiveAfterExtract(CurrentArchivePath!,
                  LastProgressWindow?.DeleteArchiveAfterExtractValue
                      ?? AppSettings.Load().DeleteArchiveAfterExtract);
  ```

  **② 打开目录消费点**（:2351 对话框流程、:2510、`DragDropService:129`）——`DragDropService` 改读结果记录：

  ```csharp
              case SelectedItemsExtractStatus.Success:
                  // D19: 开关值来自进度窗口实例（记录携带），不再直接读设置
                  if (result.OpenFolderAfterExtract)
                      /* 既有打开目录逻辑原样调用 */;
                  break;
  ```

  `MainWindowViewModel` 的打开目录点同理优先 `LastProgressWindow?.OpenFolderAfterExtractValue`，回退既有逻辑（对话框选择/设置）。

  **③ `TryDeleteArchiveAfterExtract` 改签名**（`App.axaml.cs:962`）：

  ```csharp
      /// <summary>
      /// 尝试在解压后删除原始压缩包（移动到回收站）。
      /// enabled 由调用方从进度窗口开关最终值传入（D19 实例级），本方法不再读设置。
      /// 重试 3 次（200ms 间隔），给 7z.dll 等外部组件释放文件句柄的时间。
      /// </summary>
      internal static void TryDeleteArchiveAfterExtract(string archivePath, bool enabled)
      {
          if (!enabled) return;
          if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return;
          // …… 既有重试循环原样保留（删去原 settings.DeleteArchiveAfterExtract 读取行）
      }
  ```

  (11i) CLI 接线（`App.axaml.cs`）—— **藏「打开目录」（接线归 `cli-extract-open-folder.md`，D18）、「删原包」显示 + 两处挪到关窗后**：

  CLI 进度窗口创建后（`RunCliExtractWithProgressAsync` 等 4 处中**解压**的路径，rg `new ProgressWindow` 于 App.axaml.cs 定位）：

  ```csharp
              // spec §11.2: CLI 解压 → 只显示「删原包」（打开目录的 CLI 接线属挂起计划，藏）
              progressWindow.SetExtractPostActions(openFolder: false, deleteArchive: true, openFolderSeed: null);
  ```

  两处删包时序挪移（:1198/:1555 的 `TryDeleteArchiveAfterExtract` 调用**从自动关闭等待之前挪到之后**，保证倒计时内切换生效，D19）：

  ```csharp
              // 旧（自动关闭前删包）：TryDeleteArchiveAfterExtract(archivePath); …… await AutoCloseOrWaitAsync(2500, …)
              // 新：先等窗口关闭（倒计时内用户可切换开关），再按窗口最终值删包
              await progressWindow.AutoCloseOrWaitAsync(() => progressWindow.Close());
              if (archivePath is not null)
                  TryDeleteArchiveAfterExtract(archivePath, progressWindow.DeleteArchiveAfterExtractValue);
  ```

  > 具体行号会因任务 6 的改造漂移——rg `TryDeleteArchiveAfterExtract` 于 `App.axaml.cs` 取全部调用点，**每个**都改签名传值 + 核对时序（删包必须在 `AutoCloseOrWaitAsync` 之后）。

  (11j) 静态验证 + 构建：

  ```powershell
  git grep -n "TryDeleteArchiveAfterExtract" -- 'src/*.cs'   # 每处均传第二参 enabled
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  ```

  **Must NOT do**:
  - 不回写 `AppSettings.OpenFolderAfterExtract`/`DeleteArchiveAfterExtract`（D17 选项 A）
  - CLI 不接「打开目录」（D18，归 `cli-extract-open-folder.md`）
  - 拖拽解压不显示「删原包」（D18）——能力位 false
  - 压缩/测试流两开关全藏（能力位 false，规则 6 隐藏而非禁用）

  **Recommended Agent Profile**: Category `smart`，Skills `[]`

  **Parallelization**: Can Run In Parallel NO；Blocked By: 6；Blocks: 12

  **Acceptance Criteria**:
  - [ ] `git grep TryDeleteArchiveAfterExtract` 每处均传 `enabled` 第二参
  - [ ] 新增 VM 测试通过；UI 测试全绿；构建 0 错误
  - [ ] GUI 验收（任务 12）：主窗口解压两开关可见、翻转后本次生效且设置不变；压缩/测试流隐藏；CLI 解压只见删包开关

---

- [ ] 12. 全量验证 + GUI 人工验收（Wave FINAL）

  **Files:** 无代码改动（只验证；发现 bug 回改对应任务产物）

  **What to do**:

  (12a) 全量构建 + 双测试套：

  ```powershell
  dotnet build src\MantisZip.Core\MantisZip.Core.csproj
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  ```

  (12b) 静态门禁（全部期望 0 命中/符合预期）：

  ```powershell
  git grep -n -E "AutoCloseOrWaitAsync\((2500|0)," -- 'src/*.cs'    # 0 命中
  git grep -n "WaitForWindowCloseAsync" -- 'src/*.cs'                # 0 命中
  git grep -n "TryDeleteArchiveAfterExtract" -- 'src/*.cs'           # 每处传 enabled
  rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines       # 每处带 EntryFailureReason
  git grep -n -E "OpenFilePickerAsync|SaveFilePickerAsync|OpenFolderPickerAsync" -- 'src/*.cs'   # 仅 SystemBrowse_Click 3 处（规则 16 未新增违规）
  ```

  (12c) GUI 人工验收清单（`dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`，12 条）：

  | # | 场景 | 预期 |
  |---|------|------|
  | 1 | 状态行文案（暂停/恢复/完成） | 位于信息区**之后**、按钮行之前，14px，可选中，复制按钮 ✓ 反馈 |
  | 2 | 解压含失败条目 | 错误摘要红段逐条目「路径：原因（权限不足/I-O/未知）」，20 行截断有溢出提示，复制=红+黄拼接 |
  | 3 | 压缩含跳过文件（占用后选跳过） | 黄段出现「路径：未包含进压缩包：原因」，**红段不出现**、窗口自动关闭不受影响 |
  | 4 | 解压失败（含模框） | 模框关后进度窗口仍在、📌 已按下、摘要可复制 |
  | 5 | 测试压缩包通过 | 状态行显示通过文案，📌 已按下，不自动关闭，无模框 |
  | 6 | 测试压缩包失败 | 状态行 + 红色摘要显示失败，📌 已按下，**无失败模框** |
  | 7 | 手动取消 | 窗口即时关闭，无摘要、📌 未按 |
  | 8 | 默认成功关闭 | 3 秒倒计时逐秒递减后关闭；📌 按下转手动 |
  | 9 | 设置改延时 0 → 成功 | 立即关闭；改 60 → 倒计时 60 起跳 |
  | 10 | 倒计时行内改 5 → Enter | 倒数从 5 重开；失焦后设置里值**不变**；Esc 回退 |
  | 11 | 主窗口解压 | 两开关可见、镜像设置初值；翻转后本次生效、设置不变 |
  | 12 | 压缩/测试/CLI 解压 | 压缩与测试：两开关全藏；CLI 解压：只显示删包开关，删包发生在窗口关闭后 |

  (12d) 把验收结果（通过/失败/bug 清单）汇报给用户；有 bug 则回改并重跑本任务。

  **Must NOT do**: 不为过验收而放宽断言或跳过场景。

  **Recommended Agent Profile**: Category `smart`，Skills `["verification-before-completion"]`

  **Parallelization**: Can Run In Parallel NO；Blocked By: 1–11；Blocks: 13

  **Acceptance Criteria**:
  - [ ] DoD 清单（本文档 Work Objectives）全绿
  - [ ] 12 条 GUI 验收逐条通过或 bug 已修复

---

- [ ] 13. 进度文档同步与提交（**仅用户明确要求提交时执行**）

  **Files:**
  - Modify: `docs/PROGRESS.md`（里程碑，Avalonia 版 `#### 2026-10` 下 `- **10-11** — 标题`）
  - Modify: `docs/progress-avalonia-detail.md`（细节，`**2026-10-11**` 段，按时间从晚到早）
  - Modify: `docs/PLAN.md`（规则 1：本计划行从「待实施」移除或标注完成 → 移至 PROGRESS.md 【历史设计方案索引】）

  **What to do**:

  (13a) **先更新进度文档（规则 3，commit 前置硬性要求）**：
  - `PROGRESS.md` 里程碑：本次属「架构级交互改造 + 大 bugfix」——加一条 `- **10-11** — 进度窗口状态行/失败明细/关闭链统一（自动按下 📌）+ 自动关闭倒计时 + 解压后置开关`
  - `progress-avalonia-detail.md` 细节：逐项列 Core 枚举通道、24 埋点、VM 聚合、关闭链收口、TestArchive 接线、XAML 重排、设置项、倒计时、后置开关、i18n key、测试新增数
  - 若本次变更属已有规划任务，在对应条目标注进度

  (13b) **再提交（conventional commits，规则 10）**，建议拆分：

  ```
  feat(core): 逐条目失败原因枚举通道（EntryFailureReason + 三引擎 24 埋点 + 压缩端跳过上报）
  feat(avalonia): 进度窗口状态行下移可复制 + 错误摘要红黄分层聚合 + 关闭链统一自动按下图钉
  feat(avalonia): 自动关闭延时设置 + 按钮行倒计时（行内可改不回写）+ 解压后置开关实例级化
  docs: PLAN/PROGRESS 同步进度窗口状态线计划与完成记录
  ```

  (13c) 若用户只让更新文档不让 commit —— 只做 (13a)。

  **Must NOT do**:
  - **绝不**在未更新进度文档时 commit（规则 3）
  - 不擅自变更版本号（规则 2）
  - 不主动 push（除非用户要求）

  **Recommended Agent Profile**: Category `quick`，Skills `["verification-before-completion"]`

  **Parallelization**: Can Run In Parallel NO；Blocked By: 12

  **Acceptance Criteria**:
  - [ ] 双轨进度文档（PROGRESS.md + progress-avalonia-detail.md）已按格式更新
  - [ ] `docs/PLAN.md` 计划行状态已同步（规则 1）
  - [ ] 提交信息符合 `<type>(<scope>): 中文描述`
