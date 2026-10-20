# 进度窗口状态行 + 失败原因明细（方案 B 两行并存）— 设计规格

- **日期**: 2026-10-11
- **状态**: 已获用户确认（分节呈现，方案 B / 枚举映射 / 记结果不记过程 / 范围边界均逐项拍板）
- **交互原型**: `docs/prototypes/progress-window-status-line.html`（4 场景切换、两行复制实测）
- **关联计划**: 本规格获批后经 writing-plans 产出 `.omo/plans/未开始/progress-window-status-line.md` 并按规则 1 登记 `docs/PLAN.md`

## 1. 目标与非目标

### 目标

1. **状态行下移 + 可复制**：现 Row 3 的 `StatusMessage`（普通 TextBlock，位于下方信息区之上、不可复制）下移到**总进度信息区与按钮行之间**，改为可选中控件 + 复制按钮，字号加大到 14px。
2. **错误摘要补复制**：现 Row 5 `ErrorSummaryBox` 已可选中，追加同款复制按钮。
3. **错误摘要红/黄分层**：红色 = 真实失败（现状 `HasErrors`），黄色 = **最终没进包/没解出的条目**（新增，独立显隐，不触发 `HasErrors`）。
4. **解压端失败原因通道（Core）**：新增 `EntryFailureReason` 枚举，三引擎全部 `EntryStatus = Failed` 上报点携带原因，UI 映射本地化文案拼入红色摘要。
5. **压缩端跳过结果上报（Core）**：被 `ErrorResolver` 决策跳过（含 7z/加密 ZIP 预检剔除这一静默缺口）的源文件，上报 `EntryStatus = Skipped + EntryFailureReason`，UI 聚合为黄色摘要。
6. **测试压缩包结果进窗口**：`TestArchiveAsync` 的通过/失败结果显示在状态行与错误摘要（失败时），而非仅在窗口关闭后写主窗口状态栏。
7. **三条件不自动关闭，统一走「自动按下 📌」**：出错 / 测试流两种情况通过**自动置 `KeepOpenOnComplete = true`** 实现不关闭，复用 `AutoCloseOrWaitAsync` 既有判定——全窗口只有一个关闭判定机制，杜绝「窗口行为与图钉按钮状态不一致」的状态分叉。
8. **自动关闭延时可配置 + 按钮行倒计时（含行内临时修改）**：延时秒数进设置（外观页，默认 3 秒）；按钮行左侧显示「N 秒后自动关闭」倒计时，且用户可**在当前窗口内直接修改该数字**（临时调整本次倒数，不回写默认设置）。
9. **解压后置动作开关进窗口**：「解压后打开对应目录」「解压后将原压缩包移到回收站」两个切换按钮放进按钮行（📌 右侧），初值镜像设置/对话框选择，**切换仅本次解压生效（不回写设置）**；压缩端与测试流隐藏。

### 非目标

- **不透传 `ex.Message` 原文**——已拍板枚举映射（`PermissionDenied / IoFailed / Other`），Core 只产事实，原因文案三语 key 由 UI 映射；原文继续只进 `CoreLog`。
- **不记录过程动作**：点了几次重试、选了哪个按钮一概不进摘要；只记「最终缺席」这一结果（重试成功 = 无事发生）。
- **解压端冲突跳过（`ResolvePathAsync` 返回 null）不进黄段**——它已有统计卡 `SkippedFiles` + 列表 ⏭ 行，重复进摘要是噪音。黄段只收录**带 `EntryFailureReason` 的 Skipped**（即压缩端跳过）。
- 不改 `IArchiveEngine` 签名、不动 `ArchiveOptions` 既有语义。
- 不做 `StatusMessage` 的语言即时刷新（`progress-window-bytes-i18n.md` 明确排除，另行处理）。
- 不提交代码——本规格获批后先出实施计划；任何 `git commit` 先按规则 3 更新进度文档并经用户确认。
- **不移除 `ExtractFlow` 失败模态框**（`ExtractFlow.cs:142`）——用户本次仅指定移除**测试失败**模态框（§9.4）；`ExtractFlow` 模态框保留但收口时序需前移（§9.3），若评审时决定一并移除再改。

### 成功判据

- 解压某个文件因权限失败：错误摘要红字显示「`video-04.mp4` 解压失败：权限不足」，可一键复制；
- 压缩中用户跳过 2 个被占用文件（含勾选应用到全部）：完成后黄色摘要列出缺席文件与原因，**统计/状态行不显示红错**（操作成功语义不被污染）；
- 两行在总进度信息区之下、按钮行之上，均可选中 + 复制按钮 + toast；
- 自动关闭倒计时在按钮行左侧可见、行内改值仅影响本次窗口、设置页默认 3 秒不被行内操作改动；
- 构建 0 错误、既有测试全过（基线以执行时 `dotnet test` 实测为准），三语 key 集测试绿。

## 2. 布局：方案 B 行序

7 行布局**行数不变，仅行序调整**（状态行 Row 3 与信息区/错误摘要换位）：

| 行 | 现状 | 本设计 |
|---|---|---|
| 0 | 批处理区 | 不变 |
| 1 | 内容模式切换 | 不变 |
| 2 | 内容区（简约/详细/列表） | 不变 |
| **3** | **状态消息行（TextBlock）** | **下方信息区（原 Row 4 上移）** |
| 4 | 下方信息区 | **状态行（可复制，14px，原 Row 3 下移至此）** |
| 5 | 错误摘要 | 错误摘要（红/黄两段 + 复制按钮） |
| 6 | 按钮行 | 不变 |

约束与影响点：

- `ProgressWindowXamlTests.cs:34` 断言 `RowDefinitions.Count == 7`——行数不变，该断言保持有效；执行时复核该测试是否有行序相关断言。
- 状态行为空文案时隐藏（`StatusMessage` 初始为 null，Rule 6），不占布局空间。
- 间距/高度/圆角全部走规则 5 资源键，颜色全部 `Theme*Brush` 结尾键（规则 4）。

## 3. 状态行（①）

- **控件**：仿 `ErrorSummaryBox` 既有模式——只读 `TextBox`（`IsReadOnly`、`TextWrapping=Wrap`、`IsTabStop=False`），`FontSize=14`、`FontWeight=SemiBold`，前景 `ThemeTextSecondaryBrush`（保持现状色，字号加大已足以区分层级）。
- **文案**：`StatusMessage` 不动（来源仍是 `SetStatus`/`SetComplete`/准备态/暂停态），本次只改位置、控件、字号。
- **复制**：`Button` + `PathIcon IconCopy`，点击走剪贴板 + 复用既有 `ShowCopyFeedback(sender)`（`ProgressWindow.axaml.cs:612`，图标翻转 ✓ 1.2s + 底部 `CopyToast` 1.6s，`Progress_Toast_Copied` key 三语已存在）。
- **绑定 key 登记**：无新增 XAML 绑定 key（`StatusMessage` 是 VM 直连属性，不走 `LocalizedStrings`）。

## 4. 错误摘要（②）：红/黄分层 + 复制

### 结构

```
Row 5 (Grid)
├─ StackPanel
│  ├─ 红段 TextBox  IsVisible={HasErrors}        Foreground=ThemeStatusErrorBrush
│  └─ 黄段 TextBox  IsVisible={HasSkippedEntries} Foreground=ThemeStatusWarningBrush
└─ 复制 Button（右上/行尾）→ 复制「红段全文 + 换行 + 黄段全文」
```

- 复制按钮同样走 `ShowCopyFeedback`。
- 黄段控件属性与红段一致（只读、可选中、Wrap），**独立 `IsVisible`**，不并入 `HasErrors`。

### 内容聚合（`ProgressViewModel`）

**红色段 = 两类来源拼接**：

1. 整包异常：既有 `SetErrorSummary(message)`（`App.axaml.cs` 5 处调用点），语义不变；
2. **解压端逐文件失败（新增）**：收集 `EntryStatus=Failed` 的 `(EntryKey, EntryFailureReason)`。

拼接规则：整包异常（若有）在前，逐文件失败逐行附后；逐文件失败按 `EntryKey` **去重**（保留首次出现），**上限 20 行**，超出部分用 `Progress_ErrorSummary_More`（「…另 {0} 项」）收尾。

**黄色段 = 压缩端缺席（新增）**：收集 `EntryStatus=Skipped && EntryFailureReason != null` 的 `(EntryKey, EntryFailureReason)`，同样去重 + 20 行上限 + 溢出 key。

**收集位置（关键）**：`SetProgress` 已在 L591-596 对带 `EntryStatus` 的报告有专门分支（其后即早返回，不进速度采样）——**原因聚合必须放进该分支内**（早返回之前），与 `UpdateEntryStatus` 同点位，勿挂到常规采样路径。

聚合结构仅 UI 线程访问（`SetProgress`/`UpdateEntryStatus` 均经 dispatch），沿用 L338 既有注释的线程约定，无需加锁。

### 复制内容契约

```
video-04.mp4 解压失败：权限不足
archive-x.zip 内 big.bin 解压失败：I/O 错误（文件被占用或磁盘异常）
photos.psd 未包含进压缩包：权限不足
big-dataset.bin 未包含进压缩包：I/O 错误（文件被占用或磁盘异常）
```

红段在前、黄段在后，段间单换行；某段为空则不产空行。

## 5. Core 通道：`EntryFailureReason`

### 5.1 定义（`Abstractions/ArchiveEngine.cs`，紧邻 `ArchiveEntryStatus`）

```csharp
/// <summary>逐条目失败/跳过原因类别。Core 只产类别，本地化文案由 UI 映射（规则 13）。</summary>
public enum EntryFailureReason
{
    /// <summary>权限不足（UnauthorizedAccessException）</summary>
    PermissionDenied,
    /// <summary>读写错误：文件被占用、磁盘满、路径过长等（IOException）</summary>
    IoFailed,
    /// <summary>其它异常（CRC/数据损坏等，含 ZIP 并行路径通用 catch）</summary>
    Other,
}

// ArchiveProgress 追加（可选字段，向后兼容，不动 IArchiveEngine）：
/// <summary>失败/带原因跳过的原因类别。仅 EntryStatus=Failed 或压缩端跳过时携带。</summary>
public EntryFailureReason? EntryFailureReason { get; set; }
```

### 5.2 解压端埋点（三引擎全部 `ArchiveEntryStatus.Failed` 上报点）

以 `grep -n "ArchiveEntryStatus.Failed" src/MantisZip.Core/Engines` 为准（2026-10-11 快照 24 处：ZipEngine 12 / TarGzEngine 8 / SevenZipEngine 4），逐处按 catch 类型赋值：

| catch | 赋值 | 代表点位 |
|---|---|---|
| `UnauthorizedAccessException` | `PermissionDenied` | `ZipEngine.cs:401/:684/:927/:1209`、`TarGzEngine.cs:149/:200/:743/:806`、`SevenZipEngine.cs:534/:929` |
| `IOException` | `IoFailed` | 各引擎紧邻分支（`ZipEngine.cs:409/:694/:935/:1219`、`TarGzEngine.cs:157/:208/:751/:814`、`SevenZipEngine.cs:542/:937`） |
| 通用 `catch (Exception ex)` | `Other` | 仅 ZIP 并行路径 `ZipEngine.cs:704/:1230` |

纪律：

- 每处赋值与 `EntryStatus = Failed` 同一 `ArchiveProgress` 构造（同对象），禁止拆成两个报告（消费者按单报告读取）。
- 执行时逐处确认**异常对象确在作用域**；不在作用域的点位填 `Other` 并注释说明，不编造。
- `OperationCanceledException` 分支**不填**（取消不是失败，`throw;` 语义不变）。
- 时间戳设置失败（`ZipEngine.cs:394` 类被吞分支）**不动**——不算失败。

### 5.3 压缩端跳过上报（含 7z 预检静默缺口）

三个 `ErrorResolver` 决策点，在 catch 内先记 `EntryFailureReason`（映射同 5.2 表），用户选 Skip / 重试耗尽后随既有 `Skipped` 上报携带：

| 点位 | 现状 | 改动 |
|---|---|---|
| `ZipEngine.ReadFileWithRetry:3371-3395` → 调用点 `:1761/:1767` | 已报 `Skipped`（带 EntryKey），无原因 | 报告携带 `EntryFailureReason` |
| `TarGzEngine.TarWriteFileWithRetry:360-374` → 调用点 `:281/:284` | 已报 `Skipped`，无原因 | 同上 |
| `ReadErrorHandler.FilterUnreadableFiles:35`（7z / 加密 ZIP 预检，`SevenZipEngine.cs:599`、`ZipEngine.cs:1381` 调用） | **静默剔除，零上报** | `FilterUnreadableFiles` 增加可选回调参数（如 `Action<string, EntryFailureReason>? onSkipped`，Core 内部工具，不涉 `IArchiveEngine`），引擎用其补报 `Skipped + reason`；EntryKey 用可得的最佳键（相对路径优先，退化为文件名） |

- 被跳过的源文件计数是否并入 `ConflictStatsCounter`：**不做**（该计数器是解压冲突统计，压缩路径保持 `SkippedFiles = null`，统计卡对压缩继续隐藏——黄段是独立呈现，勿混用通道）。
- Abort / 重试耗尽抛出 → 整包异常，维持既有 `SetErrorSummary` 收口，不属本节。

## 6. 本地化 key（规则 13：三语成对，插入 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格）

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

> `Progress_AutoCloseSuffix` 是**静态后缀**（数字由可编辑 `TextBox` 承载，见 §10.4），en 后缀语序 `3` + `s until auto-close` 读作 `3s until auto-close` 成句——三语均无需 `{0}` 格式串。

- 新增前先 grep 复核是否已有可复用 key（`Status_*` 系列、`Progress_Toast_Copied` 均已存在，勿重复）。
- **VM 直连属性优先**：红/黄段文案由 VM 内经 `T()` 拼接后暴露为 `ErrorSummaryText`/`SkippedSummaryText` 直连绑定，不走 `LocalizedStrings` 索引器。**若执行时确需 XAML 直接引用上述任一 key，则必须同步登记进 `ProgressViewModel.LocalizedStrings` 数组**（漏登记 = 空白文案，规则 13）——以「XAML 是否直接引用」为判定标准。
- 完成后跑 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet`。

## 7. 测试与验证

### Core（`tests/MantisZip.Tests`）

- 引擎埋点断言：构造不可读输出路径/被占用源文件场景，断言收到的 `ArchiveProgress` 携带 `EntryStatus=Failed` 且 `EntryFailureReason` 正确映射（三类各至少 1 例）；压缩端 Skip 场景断言 `Skipped + reason`。
- 7z 预检回调：`FilterUnreadableFiles` 传入不可读文件 + Skip 决策 → 回调收到该文件与 `PermissionDenied/IoFailed`。

### UI（`tests/MantisZip.UI.Avalonia.Tests`）

- `ProgressViewModelTests`：
  - `SetProgress` 喂 `Failed + reason` → `ErrorSummaryText` 含格式化文案、`HasErrors=true`；
  - 喂 `Skipped + reason` → `SkippedSummaryText` 有值且 **`HasErrors` 仍为 false**（红黄独立，核心断言）；
  - 同 `EntryKey` 重复上报 → 去重；>20 条 → 溢出文案；
  - 复制全文 = 红段 + `\n` + 黄段，单段为空不产空行。
  - **自动按下 📌（D10）**：`SetErrorSummary` → `KeepOpenOnComplete` 变 true；批行置 `Failed` → 同上；`OperationCanceledException` 路径 → 保持 false（D13）。
- `ProgressWindowXamlTests`：行数仍为 7；状态行控件类型/字号断言（若既有测试覆盖行内容需同步更新）。
- `MainWindowViewModel`/流程测试：`TestArchive` 失败不再调用 `AppMessageBox.Show`（既有测试若有模态断言同步更新）；`WaitForWindowCloseAsync` 删除后无引用残留（grep 验证）。
- **延时与倒计时（§10）**：`AutoCloseDelaySeconds` 默认 3、保存时 clamp 0–60（越界输入 → 0/60）；「提交解析 + clamp」纯静态方法（非法串/空串/负数/超上限 各 1 例）；`AutoCloseOrWaitAsync` 无 `delayMs` 参数残留（grep 各调用点无 2500/0 硬编码）。
- **解压后置开关（§11）**：VM 初始化从 `AppSettings` 镜像（true/false 各 1 例）；切换开关后断言 `AppSettings` 两个字段**值不变**（D17 核心断言）；能力位默认 false、声明后对应 `IsVisible` 为 true；`TryDeleteArchiveAfterExtract(path, enabled)`：`enabled=false` 时不触发回收站移动（现有行为断言 + 签名改造后 grep 无旧单参调用残留）。

### 手工验证

- 开 `docs/prototypes/progress-window-status-line.html` 四场景对照实窗（解压中/暂停/完成/有失败）；
- 只读文件解压到受保护目录 → 红字 + 复制；压缩时对被占用文件点跳过 → 黄字且完成态无红错。
- **不自动关闭三条件**：📌 按下时成功路径不关；解压/压缩失败 → 图钉自动亮起 + 错误摘要留窗可复制，抬起图钉可恢复自动关闭；`测试压缩包`（通过与失败两种）→ 图钉自动亮起、窗口停留；取消 → 照旧即时关窗。
- **测试结果**：通过 → 状态行显示测试通过；失败 → 状态行失败文案 + 红段摘要，**无模态框弹出**（回归断言 `AppMessageBox` 不再被 `TestArchive` 调用）。
- **倒计时（§10）**：按钮行左侧显示「N 秒后自动关闭」；📌 按下（手动/自动）即隐藏；设置 0 → 立即关无倒计时；倒计时中点进数字 → 数字冻结不被 tick 冲掉，输入 15 回车 → 从 15 重数，Esc → 回退继续；改 15 后完成关窗，**再开新窗口回默认 3 秒**（不回写设置）；设置页改 5 秒 → 新窗口倒计时从 5 起。
- **解压后置开关（§11）**：解压流程窗口显示 📁🗑（初值 = 设置值）；📁 开着解压 → 完成后打开目录，但设置页复选框**仍为关**（不回写）；🗑 开着原地解压 → 原包进回收站；倒计时内抬起 🗑 → 原包保留；压缩/测试窗口两开关隐藏；CLI 解压窗口只见 🗑、拖拽解压窗口只见 📁。
- 既有四流程回归：App CLI 批量失败仍留窗、成功按设置延时自动关（默认 3s，📌 语义未变）。

## 8. 决策记录（执行时不得偏离）

| # | 决策 |
|---|---|
| D1 | **方案 B 两行并存**（A 合并案已否决）：状态行下移 + 可复制 + 14px，错误摘要原位加复制按钮，7 行布局行数不变仅调行序 |
| D2 | **原因走枚举映射**（`PermissionDenied/IoFailed/Other`），不透传 `ex.Message`；Core 禁止新增中文用户可见文案 |
| D3 | **记结果不记过程**：只记「最终缺席的文件」；重试成功、按钮选择不记 |
| D4 | **红/黄分层**：黄段独立显隐、**不触发 `HasErrors`**；红色只留给真失败（避免「成功完成却红字」矛盾） |
| D5 | **黄段只收带原因的 Skipped**（压缩端）；解压冲突跳过维持统计卡+列表现状，不进摘要 |
| D6 | 摘要聚合上限 20 行 + 溢出 key；逐条目失败按 EntryKey 去重 |
| D7 | 复制交互复用 `ShowCopyFeedback`（✓ 翻转 1.2s + `CopyToast` 1.6s），不新造 toast 机制 |
| D8 | 摘要拼接全在 VM 内经 `LocalizationManager.T` 完成，Core 只产枚举与数值 |
| D9 | 本次范围 = 解压端失败原因 + 压缩端跳过上报（含 7z 预检缺口修复）；不扩展统计卡、不动 `ConflictStatsCounter` 语义 |
| D10 | **不自动关闭 = 自动按下 📌**（用户拍板）：不新造关闭判定属性，出错/测试流一律置 `KeepOpenOnComplete = true`，由 `AutoCloseOrWaitAsync` 单点消费；触发点集中在 VM 失败事实写入处，禁止散在各调用点 |
| D11 | **用户可抬起自动按下的图钉**（恢复自动关闭）：按钮状态与实际行为恒一致优先；测试流强制锁定会造成状态分叉，不做 |
| D12 | **移除测试失败模态框**（`MainWindowViewModel.cs:2872`）；`ExtractFlow` 失败模态框保留但时序前移到等待之前（§9.3） |
| D13 | **取消不算错误**：`OperationCanceledException` 不写摘要、不按下 📌、照旧即时关窗 |
| D14 | **自动关闭延时进设置**：`AutoCloseDelaySeconds` 默认 3 秒、0–60 clamp、0 = 立即关；放「外观」页；`AutoCloseOrWaitAsync` 删 `delayMs` 参数改内部读设置，删各调用点 2500/0 魔法数字（主窗口流程 0→3s 行为变化已获用户接受） |
| D15 | **倒计时放按钮行左侧独立控件**，不进状态行（可复制文本被倒计时污染=复制内容随时间变化，否决）；不增列、不动右侧按钮 |
| D16 | **行内临时修改 = 实例级**：编辑态冻结（焦点即停 tick）、离开编辑态以最终有效值重置起点、非法回退、0=立即关、范围 0–60 同设置；**不回写设置、不跨窗口**；用裸 `TextBox` 不用 `NumericUpDown` |
| D17 | **解压后置开关 = 实例级**（用户拍板 A）：初值镜像设置（对话框流程优先镜像对话框选择），切换仅本次解压生效、**不回写全局设置** |
| D18 | **能力位显隐 + 死开关零容忍**：`ShowOpenFolderToggle`/`ShowDeleteArchiveToggle` 各自独立声明；CLI 藏「打开目录」、拖拽解压藏「删原包」、压缩/测试全藏；禁止显示未接线的开关 |
| D19 | **消费点改读窗口开关值**：`TryDeleteArchiveAfterExtract` 改签名传 `enabled`（删方法内设置读取）；`RunWithProgress` 存 `pw` 引用供事后读取；窗口不可用时回退读设置（维持现状） |

## 9. 测试压缩包结果呈现 + 三条件不自动关闭（自动按下 📌）

> **现状盘点（2026-10-11 快照）**：条件 1（📌）三条流程的成功路径均已满足；**条件 2/3 缺口在主窗口两个流程**——`ExtractFlow.cs:132-135` 与 `MainWindow.axaml.cs:331-334` 的 `finally { pw.Close() }` 无条件关窗（失败也关），且 `RunWithProgress` 通用 `catch (Exception)`(:325) 不写错误摘要、不落日志；App CLI 四处（`App.axaml.cs:1247-1262` 等）已用 `SetErrorSummary + CompleteWithErrors + WaitForWindowCloseAsync` 满足条件 2，本次借统一机制收编。测试流程（`MainWindowViewModel.TestArchive:2836-2877`）结果在窗口关闭**之后**才写主窗口状态栏，进度窗口内显示笼统「操作完成」。

### 9.1 单一机制：`KeepOpenOnComplete` 自动按下

**核心**：不新造 `ShouldAutoClose` 判定，三条件全部收敛为「把 `KeepOpenOnComplete` 置 true」，由既有 `AutoCloseOrWaitAsync`（`ProgressWindow.axaml.cs:206`）统一消费。

**自动按下的触发点（集中在 VM 失败事实写入点，禁止散在各调用点）**：

1. **红段写入**：`ProgressViewModel.SetErrorSummary`（`:1219`）内部置 `KeepOpenOnComplete = true`；
2. **失败终态写入**：`UpdateBatchItemStatus`/批行状态置 `BatchItemStatus.Failed` 的路径（含 `PasswordRetryCancelledException` 标记）同置；
3. **测试流**：`ProgressWindow` 构造/`Init` 时按 `IsTestFlow` 标志置 true（仿既有 `IsCompressFlow`，由 `MainWindowViewModel` 经 `vm.PendingTestFlow` 传递，消费模式同 `PendingCompressFlow`）。

因 `KeepOpenOnComplete` 是 VM 实例属性且绑定 `ToggleButton.IsChecked`，置 true 即**图钉按钮同步亮起**——用户能看到窗口为什么没关；窗口/VM 每次操作新建，**状态不跨操作泄漏**。

**用户覆盖语义（有意为之）**：自动按下后用户手动抬起 📌 → 恢复自动关闭。单一机制的意义即「按钮状态 = 实际行为」，若测试流强制锁定会造成按钮显示与行为不符，与本决策初衷相悖。副作用：测试流完成前用户抬起图钉则完成时照常自动关（可接受，见 D11）。

### 9.2 关闭路径改造（消灭无条件 `finally { pw.Close() }`）

| 位置 | 现状 | 改造 |
|---|---|---|
| `MainWindow.axaml.cs` RunWithProgress `finally:331` | 无条件 `pw.Close()` | 失败/测试流已自动按下 📌 → `finally` 改走 `await pw.AutoCloseOrWaitAsync(() => pw.Close())`（延时读设置、不再传实参，见 §10.2；成功路径已在 try 内关闭，二次 Close 无害） |
| `ExtractFlow.cs` `finally:132` | 同上 | 同上 |
| `App.axaml.cs` 4 处失败收口（`:1257/:1424/:1616/:2361`） | `WaitForWindowCloseAsync` 私有辅助 | 统一改「自动按下 📌 + `AutoCloseOrWaitAsync`」，删除 `WaitForWindowCloseAsync`（与 `WaitForManualCloseAsync` 二选一保留其一） |
| `MainWindow.axaml.cs:307` 等成功路径 | `AutoCloseOrWaitAsync` | 关闭判定不变（条件 1 已成立），仅延时来源改设置（§10.2） |

**`RunWithProgress` 通用 `catch (Exception)`(:325) 补洞**：`pw.SetErrorSummary(ex.Message)` + `App.TraceLog`/`CoreLog.Error` 落日志（消灭静默吞异常），随后由 9.1 触发点 1 自动按下 📌。

**时序要求**：失败路径必须**先写错误摘要（→ 自动按下 📌）再进入等待**；取消（`OperationCanceledException`）**不算错误**——不写摘要、不按下、照旧即时关窗（用户主动取消无需留窗口）。

### 9.3 `ExtractFlow` 失败模态框时序前移（框保留）

现状时序：`finally Close()` → 之后才 `AppMessageBox.Show`（`:138-147`）。窗口改为留开后，若保持原时序，模态框要等用户关掉进度窗口才弹——不可接受。改造：失败分支内**先** `SetErrorSummary`（自动按下）→ **再**弹既有模态框 → `finally` 等待手动关闭。用户关掉弹窗后进度窗口仍在，可查看/复制错误摘要。

### 9.4 测试流程结果呈现（含移除模态框）

- **接线方式**：`RunWithProgress` 增加**可选完成回调**参数（默认 `null`，既有 15 个调用点零改动）；`TestArchive`（`MainWindowViewModel.cs:2854`）传入回调，在关窗决策前把 `testOk` 写进窗口：
  - **通过** → 状态行 `SetComplete(T("Status_TestOK"))`（含条目数，若有现成 key；否则复用 `Status_TestOK` 原文），**错误摘要保持隐藏**（D4：红色只留给真失败）；
  - **失败** → 状态行 `T("Status_TestFailed")` + 错误摘要红段 `T("Status_ArchiveCorrupted")`（`TestArchiveAsync` 三引擎均在 catch 吞成 `return false`，**无逐条目损坏位置**，见 Follow-up 12.6）。
- **移除测试失败模态框**：删除 `MainWindowViewModel.cs:2872-2875` 的 `AppMessageBox.Show`（结果已进窗口且窗口不关，弹窗成为重复打断）；主窗口 `StatusMessage` 既有赋值保留。
- **测试流不自动关**：9.1 触发点 3 在窗口创建时即按下 📌，完成态经 `SetComplete` 把取消按钮变「关闭」，窗口停留供查看/复制。
- 取消测试（用户按取消/密码弹窗取消）不属结果呈现范围，照 9.2 取消语义即时关窗。



## 10. 自动关闭延时设置 + 按钮行倒计时（含行内临时修改）

### 10.1 设置项

- **字段**：`AppSettings.AutoCloseDelaySeconds`（int，**默认 3**，保存时 clamp 0–60）。
- **语义**：`0 = 不停留立即关`；「永不关闭」**不在本设置里**——那是 📌 的职责（含自动按下，§9.1），两控件各管一档不重叠。
- **UI**：设置窗口**「外观」页**追加「进度窗口」小节（`Border` 分组样式抄同页既有项）：标题 + 数字 `TextBox`，模式同 `MaxRecentFiles`；不放「高级」页（7z 路径/提权/路径优先级类技术配置，普通用户不逛），不新开「常规」页。走规则 4/5/13（`Theme*` 键、紧凑度资源、三语 key）。
- **行为变化声明（用户已接受默认 3 秒）**：主窗口流程（`ExtractFlow` / `RunWithProgress` 现状延时 `0` 立即关）→ 变为停留 3 秒；App CLI 四处现状 2500ms → 3000ms。

### 10.2 关闭链单点化

`AutoCloseOrWaitAsync`（`ProgressWindow.axaml.cs:206`）**删除 `delayMs` 参数**，内部读 `AppSettings.AutoCloseDelaySeconds`——删掉调用点硬编码的 `2500`（`App.axaml.cs:1262/:1430/:1622/:2377`）与 `0`（§9.2 各处），延时来源单点化。既有 100ms 轮询步进保留，整秒边界把剩余秒数上报到 VM。

### 10.3 按钮行倒计时显示（位置已拍板：按钮行左侧）

```
Row 6 列结构不变（Auto, *, Auto, Auto）
[📌] [3][秒后自动关闭]      [暂停/关闭] [取消]
      └ Column 1 弹性空白列内，左对齐 StackPanel
```

- **落位**：既有弹性空白列（Column 1）内左对齐放置，**不增列**——右侧按钮位置零变动，7 行布局不动。
- **结构**：`TextBox`（装数字，`IsReadOnly=False`，见 10.4）+ 静态后缀 `TextBlock`（绑 `Progress_AutoCloseSuffix`，需登记 `LocalizedStrings`，规则 13）；外层 `StackPanel` 的 `IsVisible` 绑 VM `AutoCloseCountdownVisible`（Rule 6 隐藏，不占空间）。
- **显示条件 = 「真正会自动关闭」**：倒计时运行中才显示。📌 按下（手动/自动，含测试流与失败留窗）、设置为 0（立即关，无倒计时可显示）→ 隐藏。**不进状态行**——状态行是可复制文本，倒计时拼入会让复制内容随时间变化（D15 否决理由）。
- **交互**：用户抬起 📌 → `AutoCloseOrWaitAsync` 既有轮询已支持切换到手动等待，倒计时组随之隐藏。

### 10.4 行内临时修改（仅当前窗口实例，不回写设置）

**核心坑**：倒计时每秒回写数字文本，会冲掉用户正在输入的字符、甚至输不完就关窗。必须有**编辑态状态机**：

| 状态 | 行为 |
|---|---|
| 倒计时中 | 每秒更新 `TextBox.Text`；剩余 0 → 执行关闭动作 |
| **获得焦点 → 冻结** | 停止 tick 与文本回写，记录此刻剩余秒数 `frozenRemaining`——用户有无限时间输入 |
| **离开编辑态**（Enter / 失焦 / Esc） | 以「最终有效值」**重置倒数起点**：输入成功 → clamp 0–60 后的新值从头数；输入非法/空/Esc → 回退 `frozenRemaining`（冻结期时间不流逝，回退继续 = 重置起点，规则统一为一条） |
| 提交 0 | 下一 tick 执行关闭（与设置 0 语义一致） |
| 编辑中发生终止事件（📌 按下 / 关闭） | 倒计时终止，整组隐藏，编辑作废 |

**持久性边界（用户明确要求）**：行内修改**只影响当前窗口的本次倒数**——不写 `AppSettings`、不调 Save、不跨窗口（窗口/VM 每次操作新建，天然隔离，与 D10 同构）。下次打开回设置默认值。

**实现形态取舍**：用裸 `TextBox` 而非 `NumericUpDown`（±微调按钮令按钮行交互控件从 5 个增至 7 个，过重；键入已覆盖需求）；数字与后缀拆开（数字在 `TextBox`、后缀静态 `TextBlock`），避免 `{0}` 格式串把可编辑数字包进绑定文本。

### 10.5 归属与不做的事

- 倒计时 tick / 编辑状态机逻辑在 `ProgressWindow` code-behind（与 `AutoCloseOrWaitAsync` 同层）；可见性与剩余秒数经 VM 暴露。**「提交解析 + clamp」抽为纯静态方法**供单测。
- 不做：倒计时到点前的音效/闪烁提醒；不改 📌 与倒计时的既有联动语义；不把行内改动传播到其他窗口或设置。



## 11. 解压后置动作开关（📁 打开目录 / 🗑 删原包进回收站）

### 11.1 控件与布局

```
[📌][📁][🗑]  3 秒后自动关闭      [暂停][取消]
 └ Column 0 左侧簇（新增两个 ToggleButton）
```

- 两个 `ToggleButton` 紧跟 📌，方形紧凑、样式抄 `KeepOpenToggle`（`ProgressWindow.axaml:886`：高度 `ControlHeightLg`、`ThemeButtonBgBrush`/`ThemeBorderBrush`）；图标用**既有** `IconFolder` / `IconDelete`（`AppIcons.axaml:4/:13`，Rule 8 无需新图标）；ToolTip 复用设置页既有文案 key（`SettingsWindowViewModel:799` 的 `Settings_Extract_DeleteArchiveAfterExtract` 同族；`ExtractOpenFolderAfterText` 对应的确切 key 执行时 grep 确认），**不新增 i18n key**（规则 13）。
- 显隐走 `IsVisible` 绑 VM（Rule 6）；Column 0 为 Auto，隐藏时不占空间；倒计时与右侧按钮零变动。

### 11.2 显隐矩阵（能力位，死开关零容忍）

新增两个能力位（仿 `IsCompressFlow` 模式，窗口创建点声明，**默认 false**）：`ShowOpenFolderToggle` / `ShowDeleteArchiveToggle`。**禁止显示无接线的开关**（按钮状态 = 实际行为，D10 同款哲学）：

| 流程 | 📁 打开目录 | 🗑 删原包 | 依据 |
|---|---|---|---|
| 主窗口解压六流程（对话框/原地/命名/到/智能/选中条目，含 `ExtractFlow`） | ✅ | ✅ | 两者既有接线齐备（`MainWindowViewModel:2351/:2510` + 六处 `TryDeleteArchiveAfterExtract`） |
| CLI 批处理解压（`App.axaml.cs`） | **藏** | ✅ | CLI 打开目录未接线（属挂起计划 `cli-extract-open-folder.md`，本次不扩张 CLI 行为） |
| 拖拽解压（`DragDropService`） | ✅ | **藏** | 拖拽解压本就不动原包，补线 = 新行为，不做 |
| 压缩 / 测试压缩包 / 其它 | **藏** | **藏** | 与解压后置动作无关 |

### 11.3 初值与消费接线

- **VM 属性**：`OpenFolderAfterExtractEnabled` / `DeleteArchiveAfterExtractEnabled`（bool）。
- **初值**：窗口创建时镜像 `AppSettings` 对应值；**对话框解压流程优先镜像对话框选择结果**（`ExtractSettingsWindow` 既有复选框保留不动，其值是本次操作的种子，`MainWindow.axaml.cs:176` 已有复制链路）。设置页复选框与窗口开关从此是「默认值 vs 单次覆盖」关系。
- **消费改造**（核心：调用点从读 `AppSettings` 改为读窗口开关最终值）：
  - `App.TryDeleteArchiveAfterExtract(string archivePath)` **改签名**为 `(string archivePath, bool enabled)`——删除方法内部 `:965` 的设置读取，值由调用点传入；
  - 主窗口流程经 `RunWithProgress` 创建窗口、事后无 `pw` 引用 → `RunWithProgress` 闭包内把 `pw` 存入 VM 字段（如 `LastProgressWindow`），后置动作读 `pw` 开关值，`pw` 为 null 或开关未显示时回退读设置（维持现状路径）；
  - `DragDropService:129` 的打开目录判断改读 pw 开关值；CLI `:1198/:1555` 改传 pw 删包开关值。
- **执行时机（统一时序）**：后置动作一律在**窗口关闭之后**执行——主窗口流程现状即然（后置代码在 `RunWithProgress` 返回后运行）；CLI 两处 `TryDeleteArchiveAfterExtract`（`App.axaml.cs:1198/:1555`）从完成时点**挪到自动关闭等待之后**对齐。因此开关读取的是**最终状态**：倒计时内最后一次切换在所有流程都有效，无流程差异；失败/取消不执行后置动作（现状语义不变，D13）。

### 11.4 持久性边界（D17）

窗口开关的切换**只影响本次解压**：不写 `AppSettings`、不调 Save、不跨窗口（窗口/VM 每次操作新建，天然隔离）。下次解压回设置默认值。与 §10.4 倒计时行内修改同一哲学。

## 12. Follow-ups（本次不做，记录备查）

1. **黄段扩到解压端冲突跳过**：当前 D5 排除；若用户日后希望摘要成为「全操作缺席总账」，只需放宽 D5 过滤条件（通道已就绪）。
2. **原因按读/写细分**：`PermissionDenied` 目前端无关（解压=写、压缩=读）；如需「没有写入权限」级精度，按端拆 key。
3. **`StatusMessage` 语言即时刷新**：归 `progress-window-bytes-i18n.md` 的既有排除项。
4. **压缩端 `Failed` 逐条目上报**：压缩无 `Failed` 语义（硬失败即整包异常），若 SharpSevenZip 日后支持逐文件错误回调再评估。
5. **顺序解压路径无通用 catch**：CRC/数据损坏在顺序模式下抛整包异常（非逐条目），属既有行为，本次不改。
6. **测试失败的逐条目定位**：三引擎 `TestArchiveAsync` 均在 catch 吞成 `return false`（`ZipEngine.cs:1985`、`SevenZipEngine.cs:775`、`TarGzEngine.cs:585`），丢失「哪个条目 CRC 坏了」；若要错误摘要精确到条目，需让测试路径复用 §5 的 `EntryFailureReason` 通道（失败点已在 catch 内，异常对象在作用域）。
