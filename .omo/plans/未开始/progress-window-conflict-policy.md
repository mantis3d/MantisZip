# 进度窗口「文件冲突自动处理」控件（TeraCopy 式）

> **状态**: 📋 待实施 | **创建**: 2026-11-01 | **优先级**: B 档（🟡中） | **预估工时**: 12-16h
> **来源**: 用户 2026-11-01 提出「模仿 TeraCopy，在 `ProgressWindow.axaml` 里加一个设置文件冲突自动处理的控件」
> **已定口径（2026-11-01 / 11-02 用户确认）**: ① 运行中随时切换、立即生效；② 解压 + 压缩都要；③ 保留现有 `ConflictDialog`（默认「询问」仍弹窗，切到自动策略后不再弹）；④ 不写回全局设置（实例级）；⑤ 布局 = 信息区首行与「少/中/完整」同排（2026-11-02 定稿）。
> **可交互原型**: [docs/prototypes/progress-window-conflict-policy.html](../../docs/prototypes/progress-window-conflict-policy.html)（三种布局 / 两套档位 / 弹窗与「应用到全部」联动 / 暂停改策略，浏览器直接打开）

## 背景

现在的冲突策略是一个**启动前快照**：用户在解压/压缩设置对话框里选一次，进引擎后 UI 再也影响不了它。运行到一半发现「这个包我其实想全部跳过」，只能取消重来。而 Ask 模式下的「应用到全部」虽然能让后续冲突自动处理，但它存在一个闭包里（`applyToAll` / `chosenAction`），用户看不见当前策略是什么、也没法改回来。

TeraCopy 的做法是把这件事变成进度窗口里的一个可见控件：冲突策略始终可见、可改，改了立刻对后续冲突生效；弹窗只负责「问我一次」，勾「应用到全部」= 把控件切到那个动作。本计划把 MantisZip 的冲突处理补齐到同一形态。

## 现状核实（2026-11-01，按代码坐标）

### 解压链（同一决策点，6 个入口共用）

| 环节 | 坐标 | 说明 |
|---|---|---|
| 策略枚举（6 档） | `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:273-287` | `Overwrite` / `Rename` / `Skip` / `Ask` / `OverwriteIfOlder` / `OverwriteIfSmaller` |
| 静态策略字段 | `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:58` | `ArchiveOptions.ConflictAction`，默认 `Overwrite` |
| 询问回调 | `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:65,73` | 仅 `Ask` + resolver 非空时调用 |
| **唯一读取点** | `src/MantisZip.Core/Utils/FileConflictHelper.cs:72`（异步）/ `:24`（同步） | 每次冲突重新读 `options?.ConflictAction`；`File.Exists` 不存在时先短路返回 |
| 引擎调用点 | `ZipEngine.Extract.cs:112/853/1508`、`SevenZipEngine.cs:555/1110`、`TarGzEngine.cs:101/188/858/975` | 三引擎全部经 `FileConflictHelper`，没有第二处读策略 |
| 选项构造 | `src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs:127-168` | 字符串 → `ArchiveOptions`；Ask 时在闭包里维护 ApplyToAll 记忆 |

> **可行性结论**：引擎每个冲突都重读一次策略（而不是缓存），所以「运行中改策略、对接下来的冲突立即生效」在引擎侧天然成立，**不需要动解压引擎**。

### 压缩链（另一套枚举、另一条路径）

| 环节 | 坐标 | 说明 |
|---|---|---|
| 枚举（4 档） | `src/MantisZip.Core/Abstractions/CompressEnums.cs:6-12` | `Overwrite` / `Add` / `Rename` / `Cancel`（无 Ask、无按时间/大小） |
| 冲突判定 | `src/MantisZip.Core/Services/CompressService.cs:453-472` | **每个输出包判定一次**（不是每个文件）；文件不存在/resolver 为 null → 直接 Overwrite |
| resolver 工厂 | `src/MantisZip.UI.Avalonia/Services/CompressFlow.cs:123-158` | `defaultAction` 非 ask 时直接映射免弹窗；`add` 在 `!CanAdd` 时回退弹窗 |
| 会话接线 | `src/MantisZip.UI.Avalonia/Services/CompressSession.cs:191` | `CreateResolver(..., request.ConflictAction)` |
| 弹窗 | `src/MantisZip.UI.Avalonia/Services/CompressFlow.cs:196-260` | 5 个按钮 + 应用到全部 + 暂停/取消整轮；`Skip`/`Cancel` 都落为 Core `Cancel` |

### 进度窗口与入口矩阵

| 入口 | 进度窗口构造点 | 现在的冲突策略来源 | 本计划 |
|---|---|---|---|
| 主窗口「解压到…」对话框 | `Services/MainWindowProgressPresenter.cs:59`（经 `ExtractSession`） | `ExtractSettings.ConflictAction`（`Models/ExtractSettings.cs:77`） | 控件 + 实时源 |
| 主窗口「解压到此处」`ExtractArchiveHere` | 同上（`Views/MainWindow.axaml.cs:353` 的 `RunWithProgress` 委托） | **无**（`MainWindowViewModel.Extract.cs:59` 不传 options → 静默覆盖） | 控件 + 实时源（同时修复 KI-010） |
| 「解压到同名目录」`ExtractArchiveToName` | 同上 | **无**（`:104`） | 同上 |
| 「智能解压」`SmartExtract` | 同上 | **无**（`:335`） | 同上 |
| 右键/拖拽「解压选中项」 | `Services/ExtractFlow.cs:85`（自建窗口） | `AppSettings.FileConflictAction`（`Models/AppSettings.cs:85`） | 控件 + 实时源 |
| 主窗口压缩对话框 | `MainWindowProgressPresenter.cs:59`（经 `CompressSession`） | `CompressSettings.ConflictAction` | 控件 + 实时源 |
| CLI 解压（单文件） | `App.CliExtract.cs:569` | `AppSettings.FileConflictAction` | 控件 + 实时源 |
| CLI 解压（批处理） | `App.CliExtract.cs:740` | 同上 | 控件 + 实时源 |
| CLI 压缩 | `App.CliCompress.cs:217`（+ `CliProgressPresenter.cs:63` 可复用已有窗） | `CompressSettings` / CLI 参数 | 控件 + 实时源 |
| 测试压缩包 / 删除条目 / 测试菜单窗口 | `MainWindowViewModel.Extract.cs:381`、`MainWindowViewModel.Compress.cs:151`、`Controls/Menus/TestMenuView.axaml.cs:93` | 无冲突概念 | **不显示**（能力位 false） |
| 「添加文件到当前压缩包」 | 同上 `RunWithProgress` | `AddFilesToArchiveAsync` 走 `CreateExtractOptions`，但冲突是**包内条目名**冲突（`AddConflictHelper.cs:42,87`） | **非目标**（明确不做） |

> ⚠ **附带发现（已登 KI-010）**：`ExtractArchiveHere` / `ExtractArchiveToName` / `SmartExtract` 三条路径调用 `new ExtractService().ExtractAsync(...)` 时不传 `ArchiveOptions`，引擎按默认 `Overwrite` 静默覆盖——**完全不读 `AppSettings.FileConflictAction`**。本计划的控件若在后三条路径显示却不接线，就是「点了没反应的死开关」（规则 23 的典型事故），因此它们必须一并接线；接线的副作用是这三条路径开始真正遵守冲突设置（属于修复，需写进 RELEASE_NOTES）。

## 目标 / 非目标

**目标**

1. 进度窗口出现「冲突时:」下拉（解压 6 档 / 压缩 5 档），初始值 = 本次运行的策略；运行中改选**立即**影响后续冲突，已处理条目不受影响。
2. 「询问」档下保持现有 `ConflictDialog` 行为；勾「应用到全部」时**把下拉同步成该策略**，用户可见、可再改回「询问」。
3. 控件是**实例级**的：不回写 `AppSettings`，与 📌 保持打开 / 📁 打开目录 / 🗑 删原包三个开关同款。
4. 控件可见 = 该流程真的已接线（同一个能力位置位，杜绝死开关）。

**非目标**

- 不改设置窗口 / 解压设置对话框 / 压缩设置对话框里已有的冲突下拉。
- 不写回 `AppSettings.FileConflictAction` / `CompressFileConflictAction`（不做第三个策略存储点，规则 23）。
- 不动「添加文件到压缩包」的**条目名**冲突（`AddConflictHelper`，语义是包内重名不是磁盘文件冲突）。
- 不做 TeraCopy 的冲突清单 / 事后「只重试跳过项」。
- 不改 `ConflictDialog` / `CompressConflictDialog` 内部布局与按钮语义。

## 总体设计

### 1. 单一词汇表：沿用设置里已在用的字符串码

不引入新枚举。控件选项与 `AppSettings.FileConflictAction` 用同一套字符串码（`ask` / `overwrite` / `overwrite-if-older` / `overwrite-if-smaller` / `rename` / `skip` / `add`），映射函数已存在：

- 解压：`SelectedItemsExtractService.MapConflictActionString`（`:108-119`，`internal`）
- 压缩：`CompressFlow.MapDefaultConflictAction`（`:169-188`，改为 `internal` 复用）

两套选项集合按流程给出：

| 流程 | 选项（顺序 = 展示顺序） |
|---|---|
| 解压 | 询问 / 覆盖 / 仅覆盖较旧 / 仅覆盖较小 / 重命名 / 跳过 |
| 压缩 | 询问 / 覆盖 / 追加到已有包 / 重命名 / 跳过 |

`add` 在格式不支持追加时（`CompressConflictInfo.CanAdd == false`）回退弹窗——沿用 `MapDefaultConflictAction` 返回 `null` 的既有语义。

### 2. Core 唯一改动：`ArchiveOptions.ConflictActionProvider`

在 `ArchiveOptions` 增加一个**动态策略源**（默认为 `null`，行为与旧版逐字节一致）：

```csharp
/// <summary>解压时文件已存在的处理方式（静态默认值）。</summary>
public FileConflictAction ConflictAction { get; set; } = FileConflictAction.Overwrite;

/// <summary>
/// 解压时文件冲突处理方式的动态来源，优先于 <see cref="ConflictAction"/>。
/// 每个冲突实时读取（进度窗口可在运行中改写）；返回 <see cref="FileConflictAction.Ask"/>
/// 且装有 <see cref="ConflictResolverAsync"/>/<see cref="ConflictResolver"/> 时走询问回调。
/// null（默认）= 沿用静态值，与未接该属性前逐字节一致。
/// </summary>
public Func<FileConflictAction>? ConflictActionProvider { get; set; }
```

`FileConflictHelper.ResolvePath`（`:24`）与 `ResolvePathAsync`（`:72`）各改一行：

```csharp
var action = options?.ConflictActionProvider?.Invoke()
             ?? options?.ConflictAction
             ?? FileConflictAction.Overwrite;
```

**为什么不用「恒 Ask + resolver 自行决定」的偷懒写法**：`FileConflictHelper.BuildConflictInfo`（`:101-122`）每次都会做 `FileInfo` + `GetUniquePath`（目录探测）。若恒 Ask，则「覆盖 1 万个同名文件」也要为每个冲突算一次建议名，纯浪费；provider 保证只有真的落到「询问」才构建 info。压缩侧不受影响（冲突按包判定，本来每个包一次）。

### 3. 进度窗口：信息区首行「冲突时:」

**位置（2026-11-02 定稿）**：放在信息区 `Border`（`ProgressWindow.axaml` 的 Row 3）内首行，与「少 / 中 / 完整」信息量分级**同一行**：左密度单选、右冲突控件。**不新增 `RowDefinition`**（`RootGrid` 保持 7 行），Toast 的 `RowSpan` 也不变。

三个候选位置都在原型里可切换，结论与理由：
- **vs 底部按钮行**：按钮行已固定占宽 ≈ 3 个 44px 开关 + 倒计时 + 80 + 80，`MinWidth=500` 时再加「标签 + 下拉」必然挤爆；
- **vs 新增独立行**：不增加窗口高度（`SizeToContent=Height` 下约 28px 很值），且 `ProgressWindowXamlTests` 的「7 行」断言、Toast `RowSpan` 都无需改；
- **语义成立**：「少/中/完整」是本次运行的显示偏好，冲突策略是本次运行的行为偏好，同属运行期设置；比塞进底部操作区更合适；
- **500px 允许折行**：密度行与冲突控件会折成两行（冲突控件右对齐），不挤压按钮、不横向溢出（原型已按此排版）。

**结构**（信息区 `Border` 内 `StackPanel` 的首行改为 `Grid`；全部走主题画刷与紧凑度资源键，规则 4/5；控件要有中文注释，规则 14）：

```
Grid ColumnDefinitions="Auto,*,Auto"      <!-- 信息区首行 -->
├─ 列 0：现有「少 / 中 / 完整」三个 RadioButton（原位，靠左）
└─ 列 2：StackPanel(Orientation=Horizontal, Spacing=SpacingSm,
│                    IsVisible={Binding ShowConflictPolicyControl})
   ├─ TextBlock  「冲突时:」   FontSize=12  Foreground=ThemeTextSecondaryBrush
   ├─ ComboBox  ItemsSource={Binding ConflictPolicyOptions}
   │            SelectedItem={Binding SelectedConflictPolicyOption}
   │            DisplayMemberBinding={Binding Display}
   │            MinWidth=132  Height={DynamicResource ControlHeightLg}
   │            Background={DynamicResource ThemeSurfaceBgBrush}
   │            ToolTip.Tip={Binding LocalizedStrings[Progress_ConflictPolicy_Tip]}
   └─ (可选) TextBlock  已自动处理 N 个   FontSize=11  ThemeTextSecondaryBrush   ← 待确认，见 §11
```

两个实现细节：
1. 信息区底色是 `ThemeSplitterBgBrush`，`ComboBox` 必须显式给 `ThemeSurfaceBgBrush` 背景 + `ThemeBorderBrush` 边框，否则会和底色糊在一起；
2. 控件隐藏（`ConflictPolicyFlow.None`，测试/删除流程）时，密度单选会从「居中」变为「靠左」。**有意接受**：同一种行结构不随流程跳动，比动态居中更稳。

ComboBox 绑定写法与 `Dialogs/ExtractSettingsWindow.axaml:118-121` 一致（对象绑定 + `DisplayMemberBinding`）。

**ViewModel（`ProgressViewModel`）新增**：

| 成员 | 说明 |
|---|---|
| `ShowConflictPolicyControl` | 能力位；**只有真正接线时才置 true**（见 §3 不变量） |
| `ConflictPolicyOptions` | `ObservableCollection<Option>`（复用 `ViewModels/SettingsWindowViewModel.cs:2393` 的 `Option(Display, Value)`） |
| `SelectedConflictPolicyOption` | 写回 `ConflictPolicyCode` |
| `ConflictPolicyCode` | 线程安全读出（内部 `int` 下标 + `Volatile.Read/Write`，读方是引擎后台线程） |
| `SetConflictPolicyControl(ConflictPolicyFlow flow, string? initialCode)` | 能力位 + 选项集 + 初值；`flow = None` 时整行隐藏 |
| `SyncConflictPolicyFromDialog(string code)` | 「应用到全部」时同步下拉（UI 线程） |
| `OnCultureChanged` 分支 | 语言切换时重建选项文案并保持当前选中码 |

**`ProgressWindow` 暴露**（供执行层读，与 `SetExtractPostActions` 同款风格）：

```csharp
public void SetConflictPolicyControl(ConflictPolicyFlow flow, string? initialCode); // 须在 Show 前
public FileConflictAction ConflictPolicy { get; }   // 解压侧实时源（"ask" → Ask）
public string ConflictPolicyCode { get; }           // 压缩侧实时源
```

### 4. 解压接线

`CreateExtractOptions` 增加**可选**第三种参数，不传时行为与现在完全一致：

```csharp
internal static ArchiveOptions CreateExtractOptions(
    string conflictAction,
    Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? conflictDialog,
    Func<FileConflictAction>? livePolicy = null)
```

live 模式（`livePolicy != null`）返回：

```csharp
new ArchiveOptions {
    ConflictAction = FileConflictAction.Ask,     // 兜底：provider 返回 Ask 时走询问
    ConflictActionProvider = livePolicy,          // 每个冲突实时读进度窗口下拉
    ConflictResolverAsync = async info => { ...现有 ApplyToAll 记忆 + conflictDialog(info)... },
}
```

决策顺序（**唯一事实源 = 进度窗口下拉**）：

1. provider 返回非 `Ask` → 引擎直接按该动作处理，**不弹窗**；
2. provider 返回 `Ask` → resolver → 弹 `ConflictDialog`；
3. 弹窗勾「应用到全部」→ `ProgressWindow.SyncConflictPolicyFromDialog` 把下拉改成该动作（后续冲突走 1），闭包里的 `applyToAll` 记忆保留仅作「窗口已关/无窗」的退路。

链路改动点：

- `ExtractFlow.ExtractAsync(..., Func<FileConflictAction>? liveConflictPolicy = null)` → 透传给 `CreateExtractOptions`（`:199`）。
- `ExtractSession`：`ConfigureWindow` 里 `window.SetConflictPolicyControl(Extract, settings.ConflictAction)`；`Operation` 里构造 `live = () => context.Presenter.ActiveWindow?.ConflictPolicy ?? FileConflictAction.Ask`，传给三条执行分支（`:182` / `:220` / `:259`）。
- `ExtractFlow.RunSelectedItemsExtractionAsync`（拖拽/右键）：自建窗口后 `pw.SetConflictPolicyControl(Extract, conflictAction)`，把 `() => pw.ConflictPolicy` 传给 `SelectedItemsExtractService.ExtractEntriesAsync`。
- `MainWindowViewModel.Extract.cs` 三条 legacy 路径：`RunWithProgress` 的 `ConfigureWindow` 里按 `PendingConflictPolicySeed`（新增 VM 侧通道，与 `PendingExtractPostActions` 同款）设能力位；操作闭包内取 `vm.LastProgressWindow` 构造 provider，经 `CreateExtractOptions` 传给 `ExtractService.ExtractAsync(..., options)`。**顺带修复 KI-010**。
- CLI：`App.CliExtract.cs:569` / `:740` 两处已直接持有 `progressWindow`，各加「设能力位 + 传 provider」。

### 5. 压缩接线

`CompressFlow.CreateResolver` 增加可选实时源，不传时行为不变：

```csharp
public static CompressConflictResolver CreateResolver(
    Func<CompressConflictInfo, Task<(CompressConflictAction Action, string? CustomName, bool ApplyToAll)>> showDialog,
    string? defaultAction = null,
    Func<string?>? livePolicy = null);
```

resolver 内部顺序：记忆 → `livePolicy()` 拿字符串码 → 非 `ask` 时 `MapDefaultConflictAction` 映射（`add` 不可用回退）→ `defaultAction` 兜底 → 弹窗。压缩侧**不改 Core**：`CompressService` 一次冲突只判一次，resolver 前端多一次字符串比较可忽略。

「应用到全部」同步：在 `CompressFlow.ShowConflictDialogAsync`（`:196`，已持有 `pw`）返回前，`outcome.ApplyToAll` 且非 `Cancel` 时调 `pw.SyncConflictPolicyFromDialog(code)`（`Skip` → `"skip"`、`Add` → `"add"`、`Rename` → `"rename"`、`Overwrite` → `"overwrite"`）。

接线点：`CompressSession`（`ConfigureWindow` 设能力位 + `:191` 传 provider）、`App.CliCompress.cs:217/444`。

### 6. 暂停 / 恢复的语义

现有逻辑在「弹窗里点暂停」后 `continue` 重弹同一冲突（`ExtractFlow.cs:322-344`、`CompressFlow.cs:235-253`）。**恢复后重弹前先查一次下拉**：若用户趁暂停把下拉改成自动策略，则不再弹窗，直接按新策略返回。否则暂停就成了「只能暂停不能改主意」的死角。

### 7. 不变量（写进代码注释与评审清单）

1. **能力位与接线同源**：`SetConflictPolicyControl` 只允许在「同一处代码已把 `livePolicy` 传给执行层」时调用；未接线的流程必须传 `None`（隐藏）。禁止「先显示、后接线」。
2. **live 模式必须同时装 resolver**：provider 返回 `Ask` 而 resolver 为 null 时，引擎会按 `Ask` 落到 `ResolveByAction` 的 `_ => outputPath`（静默覆盖）——这是既有兜底，绝不能成为新控件的默认落点。
3. **线程可见性**：下拉值以 `int` 下标 `Volatile.Write`，引擎线程 `Volatile.Read`；不读 VM 的字符串属性。
4. **单一事实源**：ApplyToAll 不再单独维护「用户看不见的记忆」，写进下拉；闭包记忆降级为退路。
5. **实例级**：运行结束不回写设置（与 `SetExtractPostActions` / `SetCompressPostActions` 的 D17 约定一致）。
6. **例外边界**：`ConflictPolicyFlow.None` 的窗口（测试/删除/测试菜单）绝不显示该行。

### 8. 与既有契约的关系（规则 20）

- 实时策略**不进** `ExtractSettings` / `CompressSettings` / `CompressRequest` 载荷（那是「用户确认那一刻」的不可变快照）；
- 它作为**执行期端口**（`Func<...>`）经 presenter / 窗口句柄传递，与既有 `conflictDialog` 端口同款；
- Core 侧只多一个 `ArchiveOptions` 可选委托（`ArchiveOptions` 本来就是运行期可变配置对象，已有 `ConflictResolver` / `ErrorResolver` 两个委托先例）。

## 涉及文件

**Core（1 个文件）**
- `src/MantisZip.Core/Abstractions/ArchiveEngine.cs`（`ArchiveOptions` 新增 `ConflictActionProvider`）
- `src/MantisZip.Core/Utils/FileConflictHelper.cs`（两处读策略改为 provider 优先）

**UI（新增 1 + 修改 9）**
- 新增 `src/MantisZip.UI.Avalonia/Models/ConflictPolicyFlow.cs`（枚举：`None` / `Extract` / `Compress`）
- `ViewModels/ProgressViewModel.cs`（能力位 / 选项集合 / 线程安全码 / 同步方法 / 语言切换）
- `Dialogs/ProgressWindow.axaml`（信息区首行改 `Grid` + 冲突控件；**不新增行**，Toast `RowSpan` 不变）
- `Dialogs/ProgressWindow.axaml.cs`（`SetConflictPolicyControl` + 两个读出口）
- `Services/SelectedItemsExtractService.cs`（`CreateExtractOptions` 第三参 + `MapConflictActionString` 保持 internal）
- `Services/ExtractFlow.cs`（`ExtractAsync` 透传 + 拖拽路径设能力位 + 暂停恢复前查下拉）
- `Services/ExtractSession.cs`（`ConfigureWindow` 能力位 + Operation 内 provider）
- `Services/CompressFlow.cs`（`CreateResolver` 第三参 + ApplyToAll 同步 + 暂停恢复前查下拉）
- `Services/CompressSession.cs`（能力位 + provider）
- `ViewModels/MainWindowViewModel.Extract.cs` + `Views/MainWindow.axaml.cs`（三条 legacy 路径接线）
- `App.CliExtract.cs` / `App.CliCompress.cs`（两处 CLI 窗口接线）

**i18n**
- `Resources/Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`：新增 2 个 key
  - `Progress_ConflictPolicy_Label`（「冲突时:」）
  - `Progress_ConflictPolicy_Tip`（「运行中切换会立即影响之后的冲突」）
  - 选项文案复用既有 key：`Extract_Conflict_Ask` / `Extract_Conflict_Overwrite` / `Settings_Extract_Conflict_OverwriteOlder` / `Settings_Extract_Conflict_OverwriteSmaller` / `Extract_Conflict_Rename` / `Extract_Conflict_Skip` / `CompressConflict_Overwrite` / `CompressConflict_Add` / `CompressConflict_Skip`，**不新增**
- `ViewModels/ProgressViewModel.cs` 的 `FillLocalizedStrings`（`:150-206`）登记两个新 key（漏登记 = XAML 空白且构建不报错）

## 任务拆解

### Phase 1 — Core 动态策略源（约 1h）
- [ ] `ArchiveOptions.ConflictActionProvider` + 文档注释
- [ ] `FileConflictHelper.ResolvePath` / `ResolvePathAsync` 改为 provider 优先
- [ ] 单测：provider 返回 `Skip` → `null`；`Rename` → 唯一路径；两次调用间改 provider → 第二次生效；provider 为 null → 与旧语义一致

### Phase 2 — 进度窗口控件（约 3-4h）
- [ ] `ConflictPolicyFlow` 枚举；`ProgressViewModel` 八个新成员（§3）
- [ ] XAML：信息区内首行改 `Grid(Auto,*,Auto)` + 冲突控件 + 中文注释（不新增行，Toast `RowSpan` 不变）
- [ ] i18n 两 key × 三语 + `FillLocalizedStrings` 登记
- [ ] 无头测试：默认隐藏 / 置能力位后可见 / 选项集合按流程正确 / 选「询问」→ `ConflictPolicy == Ask`
- [ ] 回归：`ProgressWindowXamlTests` 的「7 行」断言应**保持通过**（不新增行）；补一条「控件在信息区首行」的断言

### Phase 3 — 解压接线（约 4-5h，含 KI-010 修复）
- [ ] `CreateExtractOptions` 第三参（不传行为不变）+ 单测
- [ ] `ExtractFlow.ExtractAsync` / `RunSelectedItemsExtractionAsync`
- [ ] `ExtractSession`（单包 / 多包 / 选中条目三条分支）
- [ ] 三条 legacy 路径（`ExtractArchiveHere` / `ExtractArchiveToName` / `SmartExtract`）→ 传 options，修复静默覆盖
- [ ] CLI 两处（`:569` / `:740`）
- [ ] 暂停恢复后查下拉

### Phase 4 — 压缩接线（约 1-2h）
- [ ] `CompressFlow.CreateResolver` 第三参 + `MapDefaultConflictAction` 改 internal
- [ ] `ShowConflictDialogAsync` 的 ApplyToAll 同步
- [ ] `CompressSession` / `App.CliCompress.cs`
- [ ] 单测：livePolicy 返回 `"skip"` → resolver 返回 `Cancel` 且不调 `showDialog`

### Phase 5 — 验收与文档（约 1-2h）
- [ ] `pwsh -File scripts\verify-repo.ps1 -Task check` / `-Task i18n` / `-Task guards`
- [ ] `-Task build -Target avalonia`、`-Task test -Target ui-tests`（Core 单测按 KI-002 走 CI 验证）
- [ ] 手工验收矩阵（§9）
- [ ] `docs/PROGRESS.md` + `docs/progress-avalonia-detail.md`（规则 3）
- [ ] KI-010 状态回填（规则 21）
- [ ] RELEASE_NOTES 范围确认（规则 18，含 KI-010 修复条目）

## 测试计划

**Core（`tests/MantisZip.Tests`）**
- `FileConflictHelper` provider 优先级 / 动态变更 / null 回退（大小写、按时间、按大小三档行为不变）

**UI 无头（`tests/MantisZip.UI.Avalonia.Tests`）**
- `ProgressViewModel.SetConflictPolicyControl` 两套选项集 / 初值 / `None` 隐藏
- `ConflictPolicyCode` 线程可见性（写后读）
- `SyncConflictPolicyFromDialog` 更新选中项与码
- 语言切换后选项文案重建、选中码不丢
- XAML 冒烟：首行控件默认隐藏、能力位后可见、`RootGrid` 仍为 7 行（不新增行）

**源码守卫（可选，若 Phase 3 出现漂移）**
- 断言 6 个 `new ProgressWindow(` 构造点中，凡是接了解压/压缩冲突的流程都调用了 `SetConflictPolicyControl`（反之测试/删除流程不得调用）——以「能力位与接线同源」不变量为判据

**手工验收矩阵（§9）**

## 手工验收矩阵

| # | 场景 | 期望 |
|---|---|---|
| 1 | 默认设置 = 询问，解压到有同名文件的目录 | 弹 `ConflictDialog`；勾「应用到全部」→ 后续不再弹，且下拉已变成该动作 |
| 2 | 运行中把下拉改成「跳过」 | 之后的冲突条目进「跳过」统计，不再弹窗 |
| 3 | 再改回「询问」 | 下一个冲突重新弹窗 |
| 4 | 设为「仅覆盖较旧」 | 包内较旧的条目跳过、较新的覆盖 |
| 5 | 解压中暂停 → 改成「覆盖」→ 继续 | 不再重弹原冲突，直接按覆盖继续 |
| 6 | 压缩 Separate 多包，第 1 个包勾「应用全部」 | 下拉同步；第 2..N 个包不再弹窗 |
| 7 | 压缩选「追加」，目标格式不支持（tar.gz） | 回退弹窗（不静默覆盖） |
| 8 | 测试压缩包 / 删除条目 | 冲突行不显示 |
| 9 | CLI `--extract` / `--compress` | 控件可见且生效 |
| 10 | 窗口拖到 `MinWidth=500` | 信息区首行折成两行（密度 / 冲突控件），不挤压按钮、不横向溢出 |
| 11 | 深色 / 浅色主题 + 三档紧凑度 | 画刷与间距正确（规则 4/5） |
| 12 | 三语切换（运行中） | 标签与选项文案即时刷新，选中策略不丢 |

## 风险与缓解

| 风险 | 缓解 |
|---|---|
| **死开关**（控件显示但未接线） | 不变量 1：能力位只在同一处接线代码里置位；测试/删除流程显式 `None` |
| `Ask` + 无 resolver → 静默覆盖 | 不变量 2：live 模式必须同时装 resolver；`CreateExtractOptions` 内保证成对出现 |
| 跨线程读写策略 | `Volatile.Read/Write` 的 `int` 下标，不读 VM 字符串属性 |
| ApplyToAll 与下拉双状态打架 | 合并为单一事实源（下拉），闭包记忆降级退路；`SyncConflictPolicyFromDialog` 是唯一写入口 |
| 压缩第一个冲突来不及改控件 | 首包仍靠弹窗「应用到全部」；控件价值在多包/后续切换（文档写明，不承诺「先改控件再首包」） |
| 冲突密集场景性能 | provider 保证非 Ask 不构建 `FileConflictInfo`（不触发 `GetUniquePath` 目录探测） |
| 接线点位多（10 处）导致漂移 | 收敛到 `ProgressWindow.SetConflictPolicyControl` + 两个读出口；入口矩阵表逐项核对 |
| 规则 20 接缝被判「载荷夹带宿主概念」 | 实时策略走端口不进 `ExtractSettings`/`CompressSettings`；评审时以 §3.7 为据 |
| 误加行 / 改错结构导致测试红 | 布局定稿为「信息区首行，不新增行」；`RootGrid` 7 行断言与 Toast `RowSpan` 保持不变，若未来确需改行结构再同步测试 |

## 待确认

1. **布局（已定 2026-11-02）**：采用**信息区首行、与「少 / 中 / 完整」同排**（密度靠左、冲突控件靠右），不新增行——见 §3。备选「状态行下独立一行」「底部按钮行左侧」已放弃；原型保留三档可切换供对照。
   > 取舍记录：独立行可完全避免语义混排，但多占约 28px 高度、要多改 `RowDefinitions`/`RowSpan`/7 行断言；底部行最像 TeraCopy，但 500px 下挤爆。信息区同行是唯一「不增高度 + 不改行结构」的方案，代价是「视图密度」与「运行行为」同排、控件隐藏时密度单选靠左。
2. **是否要「已自动处理 N 个」计数**：需要引擎侧新增「自动处理」事件或从统计差推算；不加则 UI 更简单（本计划默认不加）。
3. **CLI 是否显示控件**：当前设计是「有窗口且接了线就显示」。若认为 CLI 属半无人值守，可对 CLI 传 `None` 隐藏（改动量 2 行）。
4. **三条 legacy 路径是否一并接线**：不接线则控件在这三条路径必须隐藏，且 KI-010 的静默覆盖继续存在。本计划建议一并修。
5. **计划档位**：暂放 B 档（不排期）；若视为「用户可感知的高频交互」，可提到 A2。

## 相关

- 规则 4 / 5 / 6 / 7 / 13 / 14 / 20 / 23
- 现有冲突设计：`docs/ARCHITECTURE.md` 解压/压缩章节（ApplyToAll 记忆、conflict.log 诊断链）
- KI-010（三条 legacy 路径静默覆盖，本计划 Phase 3 修复）
- TeraCopy 参考：冲突处理可在偏好里预设、冲突弹窗可「应用全部」，且支持「仅覆盖较旧文件」这类自动化策略
