# 解压选择器「保留完整路径」开关 (Extract Preserve Full Path Toggle)

> **状态**: 📋 待实施 | **创建日期**: 2026-09-30 | **最近修正**: 2026-09-30
> **关联计划**: `preview-show-content-toggle.md`（✅ 同类「预览面板开关」范式：预览控件内 toggle + 状态驱动重建）、`drag-drop-direct-extract.md`（✅ 拖拽解压后置流程，本计划改动其兜底分支）
> **前置**: 无（Avalonia 直接实施；规则 11：新功能只进 Avalonia。`AppSettings.ExtractPreserveFullPath` 已存在且是本开关的**默认值来源**，不新增字段、不改设置窗口）
> **决策来源**: 2026-09-30 brainstorming 确认（生效范围=A「仅本次解压，不回写设置」、根目录行为=a「禁用开关 + ToolTip 提示」）
> **设计文档**: [`docs/superpowers/specs/2026-09-30-picker-preserve-full-path-toggle-design.md`](../../../docs/superpowers/specs/2026-09-30-picker-preserve-full-path-toggle-design.md)

## TL;DR

`CustomFilePickerDialog` 解压模式（`ShowExtractFolderAsync`）的预览面板标题行新增「保留完整路径」复选框。勾选变化经既有 `SchedulePreviewRebuild`（300ms 防抖）**实时重建预览树**；关键修复是让**实际解压使用本次勾选值**——当前对话框只返回目标路径字符串，调用方回头独立读 `settings.ExtractPreserveFullPath`，导致「预览所见 ≠ 实际落盘」。新增 `ExtractPickResult(DestPath, PreserveFullPath)` 作为返回通道贯穿三个消费点。

---

## 1. 现状（2026-09-30 逐行核实）

### 1.1 缺陷链路

```
CustomFilePickerDialog.ShowExtractFolderAsync(:136-139)  → Task<string?>   ← 只返回路径，丢弃勾选意图
  MainWindowViewModel.ExtractSelectedTo(:2341)
    :2354  var dest = await ShowExtractFolderPicker(entries, defaultDest, CurrentFolder ?? "", settings.ExtractPreserveFullPath);
    :2357  await ExtractSelectedEntriesCoreAsync(entries, dest);
  MainWindowViewModel.ExtractSelectedEntriesCoreAsync(:2365)
    :2375  CurrentFolder ?? "", settings.ExtractPreserveFullPath, settings.FileConflictAction,   ← ★ 缺陷：回读设置
              ↓
    ExtractFlow.RunSelectedItemsExtractionAsync(:67, static)
      → SelectedItemsExtractService.ExtractEntriesAsync(:29)   ← 真正决定落盘路径
```

```
DragDropService 拖拽解压(:75-107)
  :75   if (string.IsNullOrEmpty(targetDir))        ← 目标检测失败才弹对话框（兜底分支）
  :84     targetDir = await CustomFilePickerDialog.ShowExtractFolderAsync(...)   ← 同样是 string
  :103  ExtractFlow.RunSelectedItemsExtractionAsync(
  :105    _currentFolder, _settings.ExtractPreserveFullPath, _settings.FileConflictAction,   ← ★ 同一缺陷
```

### 1.2 对话框现状

| 事实 | 位置 |
|------|------|
| `_extractPreserveFullPath` | `Dialogs/CustomFilePickerDialog.axaml.cs:79`，`private readonly bool` |
| `_extractCurrentFolder` | 同文件 `:78`，`private readonly string` |
| 是否实现 `INotifyPropertyChanged` | **否**（`:249 DataContext = this`） |
| 预览重建入口 | `SchedulePreviewRebuild(string destDir)` @ `:772`；既有调用 `:683` |
| 当前目录字段 | `_currentDir`（由 `:1089 SelectedPath = _currentDir;` 佐证） |
| 预览调用 | `:791-792` `preserveFullPath: _extractPreserveFullPath, currentFolder: _extractCurrentFolder` |
| 确认结果字段 | `public string? SelectedPath { get; private set; }` @ `:82` |
| `ShowInternal` | `:153-164`，`return dialog.SelectedPath;` @ `:163` |
| ExtractFolderPanel 选项占位 | **无**（原以为有占位区，实为不存在）→ 放标题行右侧 |

### 1.3 消费点全清单

| 调用点 | 是否有对话框 | 新行为 |
|--------|:-----------:|--------|
| `MainWindowViewModel.ExtractSelectedHere` `:2333` | ❌ 无 | **保持按设置值**（无勾选值可用） |
| `MainWindowViewModel.ExtractSelectedTo` `:2357` | ✅ 有 | 用 `pick.PreserveFullPath` |
| `DragDropService` 目标已检测到 分支 | ❌ 无（不弹窗） | **保持 `_settings.ExtractPreserveFullPath`** |
| `DragDropService` 兜底分支 `:84` | ✅ 有 | 用 `pick.PreserveFullPath` |

> `MainWindow.axaml.cs:183-184` 闭包是表达式体且无显式返回类型，返回类型随 `ShowExtractFolderAsync` 自动流转 —— **已核实无需改动**（2026-09-30 逐行确认，依据见 §6 注）。

---

## 2. 决策（已确认）

### 2.1 生效范围 = A（仅本次，不回写设置）

勾选只作用于本次解压，不写 `AppSettings.ExtractPreserveFullPath`。理由：对话框是单次操作入口，持久化会让「临时改一次」污染全局默认。设置窗口仍是该值的唯一来源，作为对话框的**初始勾选值**传入。

### 2.2 根目录行为 = a（禁用 + ToolTip）

`currentFolder` 为空（压缩包根目录）时**禁用**复选框并显示提示。理由：`ExtractPathResolver.TrimCurrentFolderPrefix` 在根目录无可裁剪前缀，两种设置生成**完全相同**的路径——开关无效会造成用户困惑。（此等价性由 §4 契约测试第 3 条实测锁定。）

### 2.3 已否决备选（留档）

| 备选 | 否决原因 |
|------|---------|
| 持久化到 `AppSettings` | 违反决策 A；且设置窗口已有入口，无需两处 |
| 根目录仍可勾选（只不改结果） | 无效控件比禁用更糟，用户会以为勾选生效了 |
| 引入 `INotifyPropertyChanged` 支持运行中切语言 | YAGNI：对话框每次 `ShowExtractFolderAsync` 都 `new` 实例，`LocalizationManager.T(...)` 构造期求值即为当前语言；模态生命周期短，运行中切语言非本功能引入的问题 |
| 为可测性给 `ExtractFlow` 加接口/DI | 与本功能无关的架构重构，成本远超收益 |

---

## 3. 实现方案

### 3.1 新增返回通道 `ExtractPickResult`

新建 `Dialogs/ExtractPickResult.cs`（从 1263 行的对话框文件中拆出，聚焦职责）：

```csharp
namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 解压目录选择结果：目标目录 + 本次解压是否保留压缩包内完整路径。
/// 决策 A：仅本次生效，不回写 <c>AppSettings.ExtractPreserveFullPath</c>。
/// </summary>
public sealed record ExtractPickResult(string DestPath, bool PreserveFullPath);
```

### 3.2 `ShowExtractFolderAsync` 改自建窗

不再走 `ShowInternal`（后者返回 `string?`，无法承载勾选值）：

```csharp
public static async Task<ExtractPickResult?> ShowExtractFolderAsync(
    Window owner, IReadOnlyList<ArchiveItem> entries, string? initialPath = null,
    string currentFolder = "", bool preserveFullPath = true)
{
    var dialog = new CustomFilePickerDialog(PickerMode.ExtractFolder, entries, null, initialPath, null,
        currentFolder, preserveFullPath)
    { WindowStartupLocation = WindowStartupLocation.CenterOwner };
    await dialog.ShowDialog(owner);
    return dialog.SelectedPath is { } path
        ? new ExtractPickResult(path, dialog.SelectedPreserveFullPath)
        : null;
}
```

`ShowInternal` 函数体保持不动（已无调用方传 `ExtractFolder`，其 `currentFolder`/`preserveFullPath` 参数保留供未来模式）。

### 3.3 对话框 UI（标题行右侧）

`ExtractFolderPanel` 标题行 `Grid` 改 `ColumnDefinitions="*,Auto"`，右侧放复选框（原标题 TextBlock 全部属性保留，仅补 `Grid.Column="0"`）：

```xml
<!-- 解压预览面板标题行：左侧标题，右侧「保留完整路径」开关（决策 A：仅本次生效，不回写设置） -->
<Grid ColumnDefinitions="*,Auto">
    <!-- 面板标题：保留原有 TextBlock 及其 Text 绑定，仅补 Grid.Column="0" 与 VerticalAlignment -->
    <TextBlock Grid.Column="0" VerticalAlignment="Center"
               Text="{Binding ExtractPanelTitleText}" />

    <!-- 保留完整路径开关：勾选变化实时重建预览树；压缩包根目录时自动禁用并提示 -->
    <CheckBox Grid.Column="1" x:Name="PreserveFullPathCheck"
              VerticalAlignment="Center"
              Margin="{DynamicResource SpacingSmThk}"
              IsEnabled="{Binding IsPreserveFullPathToggleAvailable}"
              Content="{Binding PreserveFullPathText}"
              ToolTip.Tip="{Binding PreserveFullPathDisabledHint}" />
</Grid>
```

勾选状态**不用** `IsChecked` 双向绑定（对话框无 INPC），由 `IsCheckedChanged` 事件单向驱动。根元素补 `x:CompileBindings="False"`。

### 3.4 对话框 code-behind

`_extractPreserveFullPath`（`:79`）去 `readonly`；构造函数订阅事件；新增分区：

```csharp
// ── Extract preserve-full-path toggle ─────────────────────────────────────

/// <summary>开关当前勾选值。仅本次解压生效，不回写 AppSettings（决策 A）。</summary>
public bool SelectedPreserveFullPath => _extractPreserveFullPath;

/// <summary>根目录时无前缀可裁，两模式结果相同 → 禁用开关（决策 a）。</summary>
public bool IsPreserveFullPathToggleAvailable => !string.IsNullOrEmpty(_extractCurrentFolder);

public string PreserveFullPathText => LocalizationManager.T("Picker_PreserveFullPath");
public string PreserveFullPathDisabledHint => LocalizationManager.T("Picker_PreserveFullPathDisabledHint");
public string ExtractPanelTitleText => LocalizationManager.T("Picker_ExtractPreviewTitle");

/// <summary>开关勾选变化 → 重建解压预览树（复用既有 SchedulePreviewRebuild 300ms 防抖）。</summary>
private void OnPreserveFullPathChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
{
    _extractPreserveFullPath = PreserveFullPathCheck.IsChecked ?? _extractPreserveFullPath;
    SchedulePreviewRebuild(_currentDir);
}
```

### 3.5 消费点改造

**`MainWindowViewModel`**：

- `:78` 委托改返回类型 → `Func<IReadOnlyList<ArchiveItem>, string?, string, bool, Task<ExtractPickResult?>>?`
- `:2354`（`ExtractSelectedTo`，有对话框）→ 消费 `pick`，语义 `if (pick is null || string.IsNullOrEmpty(pick.DestPath)) return;`，调用传 `pick.PreserveFullPath`
- `:2333`（`ExtractSelectedHere`，**无对话框**）→ 传 `AppSettings.Load().ExtractPreserveFullPath` 保持原行为
- `:2365` `ExtractSelectedEntriesCoreAsync` 签名加 `bool preserveFullPath`；`:2375` 改传该参数
- `var settings = AppSettings.Load()`（`:2371`）**保留**——`FileConflictAction`（`:2375`）与 `OpenFolderAfterExtract`（`:2385`）仍在用

**`DragDropService`**：在 `:75` 的 `if` **之前**声明 `var preserveFullPath = _settings.ExtractPreserveFullPath;`（非兜底分支默认值），兜底分支内用 `pick` 覆盖 `targetDir` 与 `preserveFullPath`，`:105` 改传局部变量。

---

## 4. 测试策略（含对设计文档 §10.1 的纠正）

> ⚠ **设计文档 §10.1 承诺的三条 mock 透传测试不可写**，实施时按本节替代方案执行，并回写 spec §10.1。

### 4.1 不可写的原因（已核验）

| 原承诺 | 阻塞原因 |
|--------|---------|
| mock 委托断言 `ExtractSelectedTo` 透传 | `ExtractFlow.RunSelectedItemsExtractionAsync` 是 `static` 且内部 `new ProgressWindow(...)`（`:78`），无 DI 缝隙无法拦截 |
| mock 断言 `DragDropService` 兜底透传 | `DragDropService` 是 `internal class`；UI 项目 `InternalsVisibleTo` 仅授予 `MantisZip.Tests`，**测试项目编译期即不可引用** |
| mock 断言非兜底用 settings | 同上 |

### 4.2 替代方案

**A. 行为契约测试**（真实可跑，新建 `tests/MantisZip.UI.Avalonia.Tests/ExtractPreserveFullPathContractTests.cs`）

用 `SelectedItemsExtractService`（`public sealed`，`:29`）真解压到临时目录，断言磁盘布局：

| 用例 | 断言 |
|------|------|
| `preserveFullPath=true, currentFolder="docs"` | `["docs/img/logo.png", "docs/readme.txt"]` |
| `preserveFullPath=false, currentFolder="docs"` | `["img/logo.png", "readme.txt"]` |
| `currentFolder=""` 两模式 | 两者**完全相等**且为 `["docs/img/logo.png", "docs/readme.txt"]` ← 锁定决策 a |

第三条同时是「两模式在根目录等价」的**可执行证明**，比文字论证更硬。

> 引擎取法：用 `new ZipEngine()`（`public class`，`Core/Engines/ZipEngine.cs:21`）。**不要**用 `ArchiveEngineFactory.GetEngine(path)` —— 该方法不存在，唯一工厂方法是 `GetEngineByExtension(string filePath, IArchiveEngine fallback)`（`Core/Abstractions/ArchiveEngine.cs:502`），需额外 fallback 参数。

**B. 架构守卫测试**（新建 `ExtractPreserveFullPathWiringTests.cs`）

对**精确旧代码串**断言不存在（锁死「调用方偷偷回读设置」这一真实回归）：

| 守卫 | 断言 |
|------|------|
| `MainWindowViewModel_DoesNotFeedSettingsIntoExtractFlow` | 不含 `settings.ExtractPreserveFullPath, settings.FileConflictAction` |
| `MainWindowViewModel_CoreMethodTakesPreserveFullPathParameter` | 含 `ExtractSelectedEntriesCoreAsync(List<ArchiveItem> entries, string destinationPath, bool preserveFullPath)` |
| `DragDropService_DoesNotFeedSettingsIntoExtractFlow` | 不含 `_currentFolder, _settings.ExtractPreserveFullPath, _settings.FileConflictAction` |

> **不可**断言整个 `ExtractPreserveFullPath` token 消失——`:2354` 与 `DragDropService:86` 把 settings 传给对话框作**初始勾选值**是合法保留项。

**C. 人工验证**（无法单测的分支走向，9 项，见 §7 DoD 第 6 条）

---

## 5. i18n 新增 key（规则 13，三语成对）

| Key | zh-CN | zh-TW | en |
|-----|-------|-------|-----|
| `Picker_PreserveFullPath` | 保留完整路径 | 保留完整路徑 | Preserve full paths |
| `Picker_PreserveFullPathDisabledHint` | 在压缩包根目录，勾选与否结果相同 | 在壓縮包根目錄，勾選與否結果相同 | At the archive root, both settings produce the same result |
| `Picker_ExtractPreviewTitle` | 解压预览 | 解壓預覽 | Extract preview |

> 插入 `strings.zh-CN.json` / `strings.zh-TW.json` / `strings.en.json` 文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进。三语 key 集由 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 校验。
> 对话框用 code-behind 属性绑定（§2.3 决策 4），**无需**登记进 `MainWindowViewModel.UpdateLocalizedStrings()`。

---

## 6. 变更文件清单

| 文件 | 改动 |
|------|------|
| `Dialogs/ExtractPickResult.cs` | **新建**：返回通道 record |
| `Dialogs/CustomFilePickerDialog.axaml` | ExtractFolderPanel 标题行改 `*,Auto` + 复选框；根元素 `x:CompileBindings="False"` |
| `Dialogs/CustomFilePickerDialog.axaml.cs` | `:79` 去 `readonly`；`:136-139` `ShowExtractFolderAsync` 改自建窗返回 `ExtractPickResult?`；新增 5 个属性 + `OnPreserveFullPathChanged`；构造函数订阅事件 |
| `ViewModels/MainWindowViewModel.cs` | `:78` 委托返回类型；`:2333` 补设置值参数；`:2354`/`:2357` 消费 `pick`；`:2365` 签名加参数；`:2375` 改传参数 |
| `Services/DragDropService.cs` | `:75` 前声明 `preserveFullPath` 局部变量；`:84-92` 消费 `pick`；`:105` 改传局部变量 |
| `Localization/strings.{zh-CN,zh-TW,en}.json` | +3 key（规则 13） |
| `tests/MantisZip.UI.Avalonia.Tests/ExtractPreserveFullPathContractTests.cs` | **新建**：契约测试 ×3 |
| `tests/MantisZip.UI.Avalonia.Tests/ExtractPreserveFullPathWiringTests.cs` | **新建**：架构守卫 ×3 |

> `Views/MainWindow.axaml.cs:183-184` **零改动**（✅ 已核实 2026-09-30，非推测）：表达式体 lambda `(entries, initialPath, currentFolder, preserveFullPath) => ShowExtractFolderAsync(...)` 无显式返回类型，其自然类型跟随被调方法；§3.2 改 `ShowExtractFolderAsync` 返回值与 §3.5 改委托类型是**同一个动作的两端**，二者恒等匹配，故此闭包天然编译通过、无需触碰。

---

## 7. 验收标准（DoD）

### 自动验证

1. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 0 errors
2. `dotnet test tests\MantisZip.UI.Avalonia.Tests\...` — 现有 105 + 契约 3 + 守卫 3 = **111 passed / 0 failed / 2 skipped**
3. `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` 无新增失败
4. 三语 key 集一致（`AllThreeLanguages_HaveSameKeySet` PASS）
5. 原生 Picker 自查仅 3 处豁免（规则 15）：
   ```powershell
   git grep -n -E "OpenFilePickerAsync|SaveFilePickerAsync|OpenFolderPickerAsync" -- 'src/*.cs'
   ```
6. 架构守卫 3 条全 PASS

### 人工验证（逐项记录结果）

7. **核心验收点** —— 勾选/取消勾选后，**逐条比对最终落盘路径与预览树完全一致**
8. 取消勾选后预览树在 ~300ms 内重排（`CurrentFolder` 前缀被裁掉）
9. 压缩包**根目录**解压 → 开关呈**禁用**态 + 悬停显示提示文案，且勾选状态不影响结果
10. 决策 A 正向：设置 `ExtractPreserveFullPath=true`，子目录解压时**取消勾选** → 实际按**裁剪后**路径落盘（未回读设置）
11. 决策 A 反向：操作后重开设置窗口 → `ExtractPreserveFullPath` 仍为 `true`（未落盘）
12. `ExtractSelectedHere`（右键「解压选中项到此处」）路径**行为不变**（按设置值）
13. 拖拽到已检测到的目标目录（不弹窗）→ 按设置值落盘（兜底分支之外未受影响）
14. 拖拽目标检测失败 → 弹对话框 → 取消勾选 → 确定 → 落盘为裁剪后路径（兜底透传生效）
15. 三语切换后重开对话框 → 开关文案 / 面板标题 / 禁用 ToolTip 均为对应语言

---

## 8. 工时估算

**2-3h**（🟢低-🟡中）

| 项 | 工时 |
|----|------|
| 契约测试 + 架构守卫测试（§4.2 A/B） | 0.5h |
| `ExtractPickResult` + `ShowExtractFolderAsync` 返回通道 | 0.3h |
| 对话框 UI（标题行 + 复选框 + code-behind 状态/事件） | 0.5h |
| 三个消费点改造（含两个调用点区分） | 0.5h |
| 三语 key | 0.2h |
| 构建 + 全量测试 + 9 项人工验证 | 0.5h |

---

## 9. 边界

- **不改** `AppSettings` 字段、不改设置窗口（`ExtractPreserveFullPath` 已是既有字段，仅作为初始勾选值来源）
- **不改** `ExtractSettingsWindow`（压缩/解压设置窗口无此开关）
- **不改** `ExtractPathResolver` / `ResultPreviewService.BuildExtractPreview` / `SelectedItemsExtractService` —— 三者已正确消费 `preserveFullPath`，缺陷纯在对话框返回通道与调用方回读
- **不改** WPF（规则 11）
- 拖拽**非兜底**分支不弹对话框，保持按设置值（无勾选值可用）
- 不做「根目录下开关仍可勾选」的折中（决策 a）

---

## 10. 与设计文档的差异（实施后回写）

| spec §10.1 原承诺 | 实际处置 |
|------------------|---------|
| mock 委托断言 `ExtractSelectedTo` 透传 | ❌ 不可写（`ExtractFlow` static 无 DI）→ 守卫 #1/#2 + 人工验证 8/10 |
| mock 断言 `DragDropService` 兜底透传 | ❌ 不可写（internal + 无 `InternalsVisibleTo`）→ 守卫 #3 + 人工验证 14 |
| mock 断言非兜底用 settings | ❌ 不可写 → 人工验证 13 |
| 追加：契约测试 | ✅ `ExtractPreserveFullPathContractTests` ×3 |
| §5.2 开关实时驱动预览 | ✅ 保留（复用 `SchedulePreviewRebuild` 300ms 防抖） |
