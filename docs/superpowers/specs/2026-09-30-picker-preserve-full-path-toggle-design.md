# 解压对话框「保留完整路径」实时开关设计规格

- 日期：2026-09-30
- 状态：决策已定，可进入实现（决策记录见 §8）
- 关联提交：`1c60b4a`（选择器新增 fileTypes/suggestedFileName）、`663605d`（拖拽解压兜底改用带预览对话框）

## 1. 概述

### 1.1 目标

在 `CustomFilePickerDialog` 的解压预览面板中提供一个「保留完整路径」开关，用户在**选择目标目录的过程中**可实时切换，右侧预览树立即反映裁剪/保留的差异。

### 1.2 设计决策摘要

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| 1 | 开关语义 | **A：仅本次解压生效，不落盘** | 对话框是"这次解到哪、怎么解"的临时决策场所；设置窗口仍是唯一默认值来源 |
| 2 | 返回通道 | **返回结果对象 `ExtractPickResult`** | 由决策 1 的 A 推导而来（选 A/C 需要，选 B 则不需要）。类型系统保证开关值一定传到解压侧，杜绝"预览 ≠ 实际" |
| 3 | 开关位置 | 预览面板标题行右对齐 | 不动 `RowDefinitions`，面板高度不膨胀，未来仍可插入独立选项行 |
| 4 | 根目录下（`currentFolder` 为空） | **a：禁用开关 + ToolTip 说明** | 此场景下两种模式产出完全相同，开关无效；禁用优于让用户困惑 |
| 5 | i18n key | 新建 `Picker_PreserveFullPath`（三语） | 不复用 `Settings_Extract_PreserveFullPath`，避免依赖设置窗口文案语义 |

### 1.3 非目标

- 不改 `ExtractPathResolver` / `ResultPreviewService.BuildExtractPreview` 的现有语义 —— 它们已是纯函数且预览侧与解压侧共用同一 resolver
- 不动设置窗口已有的 `ExtractPreserveFullPath` 复选框（`SettingsWindow.axaml:274`）
- 不给 `ExtractSettingsWindow` 加此开关（它当前没有该选项，见 §6.1）
- 不改 CLI（`--extract-here` / `--extract-to-name`）路径行为

## 2. 现状核实

以下均为本次实测行号，实现前若已漂移需重新定位。

### 2.1 对话框内部

| 事实 | 位置 |
|---|---|
| `_extractPreserveFullPath` 是 `readonly bool`，由构造参数灌入 | `CustomFilePickerDialog.axaml.cs:79`、`:246` |
| 唯一消费点：预览重建 | `CustomFilePickerDialog.axaml.cs:791` |
| 预览重建方法已具备防抖（300ms）+ try/catch，可直接复用 | `CustomFilePickerDialog.axaml.cs:772-801` |
| ExtractFolder 模式隐藏「系统浏览」按钮与文件名行 | `CustomFilePickerDialog.axaml.cs:298-311` |
| 面板宽度来自设置（clamp 200–800，默认 260），高度 620 | `CustomFilePickerDialog.axaml.cs:314-319` |

### 2.2 AXAML 结构

`CustomFilePickerDialog.axaml`（452 行）：

```
RootGrid RowDefinitions="Auto,*,Auto,Auto"
├─ Row 0  地址栏 + 后退/前进/上级/系统浏览/收藏
├─ Row 1  BrowserGrid  ColumnDefinitions="220,5,*,5,Auto"
│         ├─ Col 0 快速路径面板
│         ├─ Col 2 文件列表
│         └─ Col 4 ExtractFolderPanel  RowDefinitions="Auto,Auto,*"
│                ├─ Row 0 ExtractPreviewTitleText（Auto，左右留白）
│                ├─ Row 1 PreviewSplitter（Auto，5px）
│                └─ Row 2 PreviewTree（*）
├─ Row 2  FileNameArea（仅 SaveFile / OpenFile 模式）
└─ Row 3  确定 / 取消
```

**结论：不存在预留的选项区占位**（无 `*OptionsPanel`、无 `IsVisible=False` 的空 panel）。可用空位为 ExtractFolderPanel Row 0 标题行右侧的留白。

### 2.3 返回通道断点

| 事实 | 位置 |
|---|---|
| 委托签名 `Func<IReadOnlyList<ArchiveItem>, string?, string, bool, Task<string?>>`，**只返回目标路径** | `MainWindowViewModel.cs:78` |
| 委托在 View 侧闭包接线 | `MainWindow.axaml.cs:183` |
| 主窗口把设置值作为初值传入，确认后丢弃结果 | `MainWindowViewModel.cs:2354` |
| 实际解压时**独立重读** `settings.ExtractPreserveFullPath` | `MainWindowViewModel.cs:2375` |
| 拖拽兜底同样：`:86` 传初值，`:105` 传 `_settings.ExtractPreserveFullPath` | `DragDropService.cs:86`、`:105` |

**结论：即使对话框内让预览随开关变化，实际解压仍走设置值 —— 这正是 AGENTS.md 中 `ExtractPathResolver` 契约禁止的"预览 ≠ 实际"。新增返回通道是本次改动的必要组成部分，不是可选项。**

### 2.4 预览侧可行性

`ResultPreviewService.BuildExtractPreview`（`ResultPreviewService.cs:33-42`）为**纯静态同步函数**，内部走

```csharp
ExtractPathResolver.ResolveRelativePath(item.FullPath ?? item.Name, currentFolder, preserveFullPath)
```

与解压侧同一 resolver。切换开关重跑该函数无副作用，安全。

## 3. 数据流

### 3.1 改动后

```
对话框勾选/取消
   └→ _extractPreserveFullPath = 复选框状态        (字段改为可写)
   └→ SchedulePreviewRebuild(_currentDir)            (复用现有防抖重建)
        └→ BuildExtractPreview(..., preserveFullPath: 新值, currentFolder: _extractCurrentFolder)
             └→ ExtractPathResolver.ResolveRelativePath  ← 与解压侧同一实现
                  └→ 右侧预览树刷新

用户点「选择文件夹」
   └→ 返回 ExtractPickResult(DestPath, PreserveFullPath)   ← 新增返回通道
        └→ ExtractSelectedTo / DragDropService 取 r.PreserveFullPath
             └→ ExtractFlow.RunSelectedItemsExtractionAsync(preserveFullPath: r.PreserveFullPath)
                  └→ SelectedItemsExtractService.ExtractEntriesAsync(preserveFullPath)
                       └→ ExtractPathResolver.ResolveRelativePath  ← 同一实现，与预览逐字一致
```

### 3.2 单一事实来源不变

`ExtractPathResolver.ResolveRelativePath` 仍是唯一计算点。本次不新增任何路径拼接逻辑。

## 4. 新增类型

```csharp
// Dialogs/ExtractPickResult.cs
namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>解压目标目录选择结果（路径 + 本次解压的路径保留语义）。</summary>
/// <param name="DestPath">目标目录绝对路径。</param>
/// <param name="PreserveFullPath">本次解压是否保留压缩包内完整路径（覆盖 AppSettings 默认值，仅本次生效）。</param>
public sealed record ExtractPickResult(string DestPath, bool PreserveFullPath);
```

放在 `Dialogs/` 下与对话框同域，不进 `Core` —— 它是 UI 层返回值契约，不含引擎逻辑。

## 5. 对话框改动

### 5.1 `CustomFilePickerDialog.axaml.cs`

| 改动 | 说明 |
|---|---|
| `_extractPreserveFullPath` 由 `readonly` 改为可写 | `:79` |
| 新增 `public bool SelectedPreserveFullPath { get; private set; }` | 供调用方读取最终值；初值 = 构造参数 |
| ExtractFolder 模式初始化 CheckBox（勾选态 = 初值）并挂 `Checked`/`Unchecked` | 模式判断沿用 `:298` 处的既有模式 |
| 勾选处理器 | 赋值 `SelectedPreserveFullPath` + `SchedulePreviewRebuild(_currentDir)` |
| 仅 ExtractFolder 模式显示该开关 | 其余模式 `IsVisible = false` |
| `currentFolder` 为空时禁用开关 + ToolTip 说明 | 见 §8 决策 4 |
| `Ok_Click` 返回 `ExtractPickResult` 而非 `string?` | `:1085`/`:1089` 等 ExtractFolder 分支 |

**复用点**：`SchedulePreviewRebuild` 完全不改签名、不改逻辑。

### 5.2 `CustomFilePickerDialog.axaml`

`ExtractFolderPanel` Row 0 由单个 `TextBlock` 改为两列 Grid，**`RowDefinitions` 保持 `Auto,Auto,*` 不变**：

```xml
<Grid Grid.Row="0" ColumnDefinitions="*,Auto" ColumnSpacing="{DynamicResource SpacingXs}">
    <TextBlock x:Name="ExtractPreviewTitleText" ... />   <!-- 原样保留 -->
    <CheckBox Grid.Column="1"
              x:Name="PreserveFullPathCheck"
              MinHeight="{DynamicResource ControlHeightSm}"
              VerticalAlignment="Center"
              IsVisible="False"
              Foreground="{DynamicResource ThemeTextPrimaryBrush}"
              Content="{Binding PreserveFullPathText}"
              ToolTip.Tip="{Binding PreserveFullPathDisabledHint}" />
</Grid>
```

`IsEnabled` 不在 XAML 绑定 —— 按 §8 决策 2 由 code-behind 依 `_extractCurrentFolder` 动态设置（`IsEnabled = !string.IsNullOrEmpty(_extractCurrentFolder)`）。ToolTip 常驻绑定即可，禁用时自然显示。

### 5.3 绑定策略

沿用本文件既有做法：`x:CompileBindings="False"` + code-behind 暴露属性（`BackText` / `OkText` / `ExtractPreviewTitle` 等均为此模式），刷新在 `OnCultureChanged` 一并处理，避免漏登记导致的空白文案（AGENTS.md 规则 13）。

## 6. 调用方改动

### 6.1 `MainWindowViewModel.cs`

| 行 | 现状 | 改为 |
|---|---|---|
| 78 | `Func<IReadOnlyList<ArchiveItem>, string?, string, bool, Task<string?>>?` | `Func<IReadOnlyList<ArchiveItem>, string?, string, bool, Task<ExtractPickResult?>>?` |
| 2354 | `var dest = await ShowExtractFolderPicker(...)` | `var pick = await ShowExtractFolderPicker(...); if (pick == null) return;` |
| 2357 | `ExtractSelectedEntriesCoreAsync(entries, dest)` | `ExtractSelectedEntriesCoreAsync(entries, pick.DestPath, pick.PreserveFullPath)` |
| 2365 | `CoreAsync(List<ArchiveItem>, string)` | 增加 `bool preserveFullPath` 参数 |
| 2375 | `settings.ExtractPreserveFullPath` | `preserveFullPath`（参数） |

### 6.2 `MainWindow.axaml.cs:183`

闭包返回值 `string?` → `ExtractPickResult?`，其余签名不变。

### 6.3 `DragDropService.cs`

| 行 | 现状 | 改为 |
|---|---|---|
| 86 | 兜底对话框返回 `dest` | 接 `ExtractPickResult`，取 `.DestPath` |
| 105 | `ExtractFlow.RunSelectedItemsExtractionAsync(..., _settings.ExtractPreserveFullPath, ...)` | 传入对话框返回的 `.PreserveFullPath` |

`:105` 位于 `targetDir` 非空的正常拖拽分支 —— 该分支**不经对话框**，必须继续使用 `_settings.ExtractPreserveFullPath`。两处不可混用。

### 6.4 不需要改动

`ExtractFlow.RunSelectedItemsExtractionAsync` 与 `SelectedItemsExtractService.ExtractEntriesAsync` **已接受 `preserveFullPath` 参数**（见 `DragDropService.cs:105` 调用形态），无需签名变更。

## 7. i18n

| key | zh-CN | en | zh-TW |
|---|---|---|---|
| `Picker_PreserveFullPath` | 保留完整路径 | Keep full path | 保留完整路徑 |
| `Picker_PreserveFullPathDisabledHint` | 在压缩包子目录中才可用 | Only available inside archive subfolders | 僅在壓縮包子目錄中可用 |

- 三份 JSON key 集必须完全一致（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 校验）
- 插入文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进
- **不复用** `Settings_Extract_PreserveFullPath`：设置页与对话框可能需要不同措辞，耦合会产生隐性依赖

## 8. 决策记录（2026-09-30 已确认）

两项决策均已确认，实现范围即 §4–§7 基线 + 以下两条增量，无需再次设计。

### 决策 1：A —— 仅本次解压生效，不落盘

勾选状态只写入对话框内存字段与 `SelectedPreserveFullPath`，**不触碰** `AppSettings.ExtractPreserveFullPath`，不落盘。初值仍取自设置，用户在设置窗口的全局偏好不受影响。

**已否决的备选**（留档备查）：

| 备选 | 否决理由 |
|---|---|
| B 写回全局设置 | 会让用户在对话框的一次临时调整静默改掉命令行解压、拖到 Explorer、`ExtractSettingsWindow` 的行为 |
| C 临时 + 「记住此选择」二级勾选 | 面板默认宽仅 260px，两个 CheckBox 会使标题行拥挤；收益不足以抵消布局代价 |

**连带确定**：因决策为 A（而非 B），返回通道**必须**实现 —— §4 的 `ExtractPickResult`、§5.1 的 `SelectedPreserveFullPath`、§6.1、§6.3 全部在本设计范围内。

### 决策 2：a —— 根目录下禁用开关 + ToolTip 说明

`ExtractPathResolver` 的裁剪语义是「`preserveFullPath=false` **且 `currentFolder` 非空时**裁剪前缀」。故在压缩包根目录时两种模式产出**完全相同**的树，开关形同失效。

实现增量：

```csharp
PreserveFullPathCheck.IsEnabled = !string.IsNullOrEmpty(_extractCurrentFolder);
```

Tooltip 常驻绑定 `Picker_PreserveFullPathDisabledHint`（见 §7）；`_extractCurrentFolder` 为构造期固定值，故可用性在对话框生命周期内无需动态刷新。

**已否决的备选**：

| 备选 | 否决理由 |
|---|---|
| b 加小字说明 | 让用户点一个明知无效的控件，不如直接禁用 |
| c 不处理 | 会被误判为功能损坏 |

注：此项**不适用** AGENTS.md 规则 6 —— 规则 6 约束的是「开关控制面板显隐」，而此开关控制的是预览树内容，预览树必须始终可见才能达成"实时切换看效果"的目标。

## 9. 规则合规检查

| 规则 | 落实 |
|---|---|
| 4 主题样式 | CheckBox `Foreground` 显式绑 `ThemeTextPrimaryBrush`；`MinHeight` 用资源键 |
| 5 紧凑度 | `MinHeight="{DynamicResource ControlHeightSm}"`；间距用 `SpacingXs` |
| 6 隐藏而非禁用 | 仅适用于「按模式显示」；决策 2 的禁用有明确 UX 理由（见 §8） |
| 7 行高 | 不涉及（非列表控件） |
| 12 构建验证 | `dotnet build` + `dotnet test tests\MantisZip.UI.Avalonia.Tests` |
| 13 本地化 | 新增 2 个 `Picker_` key，三语成对；code-behind 暴露属性并纳入 `OnCultureChanged` 刷新 |
| 15 选择器统一 | 本改动不新增任何文件/目录选择入口 |
| 14 AXAML 注释 | 新增控件加中文注释 |

## 10. 测试

### 10.1 单元测试（新增）

| 用例 | 断言 |
|---|---|
| `ExtractSelectedTo` 透传开关值 | Mock 委托返回 `PreserveFullPath=false`，断言传给 `ExtractFlow` 的是 `false` 而非 `settings.ExtractPreserveFullPath=true` |
| `DragDropService` 兜底分支透传 | 对话框返回 `false` → 解压调用收到 `false` |
| `DragDropService` 非兜底分支不受影响 | `targetDir` 非空时仍用 `_settings.ExtractPreserveFullPath` |
| 三语 key 集一致 | 由既有 `AllThreeLanguages_HaveSameKeySet` 覆盖 |

第一条是**核心回归锁** —— 它直接锁死 §2.3 描述的断点。

### 10.2 手动测试

| 用例 | 期望 |
|---|---|
| 子目录内勾选/取消 | 预览树 300ms 内刷新，前缀裁剪/保留正确切换 |
| 拖拽兜底场景 | 对话框带开关，切换后实际落盘路径与预览一致 |
| 根目录 | 按决策 2 的选定行为表现 |
| `ExtractSettingsWindow` | 行为不变（未接入开关） |
| 设置窗口开关 | 初始勾选态正确反映设置值；关闭对话框后设置值未被改动（决策 A） |

## 11. 风险

| 风险 | 缓解 |
|---|---|
| 漏改某条路径导致预览 ≠ 实际 | §10.1 的透传测试为每条路径单独断言；类型系统强制返回非空 |
| `:105` 与 `:86` 两分支混用 | 改动时逐行核对，并在测试中分别覆盖（§10.1 第 2、3 条） |
| 新 key 漏加某语言 | 既有 `AllThreeLanguages_HaveSameKeySet` 测试拦截 |
| 面板 260px 默认宽度下标题 + 复选框溢出 | CheckBox 用 `MinHeight` + `Auto` 列，实测确认；必要时给标题设 `TextTrimming` |

## 12. Definition of Done

- [ ] `ExtractPickResult` 定义于 `Dialogs/`
- [ ] `_extractPreserveFullPath` 可写，`SelectedPreserveFullPath` 暴露
- [ ] ExtractFolder 模式显示开关，其余模式隐藏
- [ ] 勾选/取消触发 `SchedulePreviewRebuild`，预览树即时更新
- [ ] `ShowExtractFolderAsync` / `ShowExtractFolderPicker` 返回类型改为 `ExtractPickResult?`
- [ ] `ExtractSelectedEntriesCoreAsync` 增加 `preserveFullPath` 参数，`:2375` 改用参数
- [ ] `DragDropService` 两分支分别正确取值
- [ ] 2 个新 i18n key 三语齐备
- [ ] §10.1 四条单测通过
- [ ] `dotnet build` 0 错误；`dotnet test` 全绿
- [ ] §10.2 手动用例全部通过
- [ ] 按 AGENTS.md 规则 3 更新 `docs/PROGRESS.md` 与 `docs/progress-avalonia-detail.md`
