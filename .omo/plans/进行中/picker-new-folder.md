# 文件选择器「新建文件夹」

> **状态**：实现与验证完成（2026-11-04，`beta` 分支），待用户确认 `RELEASE_NOTES.md` 范围后归档
> **关联代码**：`Controls/CustomFilePickerControl`、`Dialogs/CustomFilePickerDialog`、`Dialogs/InputDialog`（本轮新建）、`Controls/FileFilterEditor`

## 背景

起因是「解压设置窗的目标路径输入框和下方面包屑是不是重复」的讨论。结论是两者并不完全重复（输入框装的是**结果**，可以指向还不存在的目录；面包屑是**浏览游标**，只能停在已存在的目录）。真问题在另一头：

- 解压默认目标是 `包所在目录\包名`（`ExtractSettingsViewModel.cs:270-276`），这个目录**几乎一定还不存在**，解压时才由 `Directory.CreateDirectory` 建出来。
- 选择器目前只能导航到已存在目录：`PathCrumbs.NavigateRequested → Directory.Exists`（`CustomFilePickerControl.axaml.cs:832-843`）、`NavigateTo` 内 `!Directory.Exists(dir) return`（`:850`）。全仓没有「新建文件夹」。
- 唯一的绕过手段是地址行「系统浏览」→ 原生对话框（那里有新建文件夹）。但模态壳之外的三个内嵌宿主都没有订阅 `SystemBrowseRequested`（全仓只有 `CustomFilePickerDialog.axaml.cs:225` 订阅），所以压缩输出目录 / 解压目标目录这两处**根本没有任何建目录的路**。

## 目标

一个「新建文件夹」入口，覆盖面里所有选择器模式，行为一致、失败有反馈。

## 非目标

- 不做列表内联改名（资源管理器那种「建完就地改名」），走输入框（决策 2）。
- 不做已有目录的重命名 / 删除。
- 不在这轮接「系统浏览」死按钮（另记 `docs/KNOWN_ISSUES.md`）。

## 覆盖范围（唯一实现点：`CustomFilePickerControl`）

| 宿主 | 模式 | 建完行为 |
|------|------|---------|
| `ExtractSettingsWindow.DestinationPicker` | PickFolder | 进入新目录（`PathChanged` → `DestinationPath`） |
| `CompressSettingsWindow.OutputPicker` | PickFolder | 进入新目录（`PathChanged` → `OutputDirectory`） |
| `CustomFilePickerDialog`（`ShowFolderAsync` / `ShowSaveFileAsync` / `ShowOpenFileAsync` / `ShowOpenItemsAsync` / `ShowExtractFolderAsync`） | 全部 | 见下方分流 |
| `QuickPathPicker`（设置里的自定义默认路径、收藏夹） | PickFolder | 同 PickFolder |

建完分流：

- **PickFolder / ExtractFolder / SaveFile** → `NavigateTo(新目录)`。前两者「当前目录即结果」，新建的目录立刻成为结果；保存模式进入新目录后焦点落文件名框。
- **PickItems / OpenFile** → **不导航**，只 `LoadDirectory` 刷新并选中新行。原因：PickItems 简单模式切目录会清空已累积选择（`NavigateTo:862`），OpenFile 进入空目录等于把用户关进空房间。

## 命名交互

1. 点按钮 / `Ctrl+Shift+N` → `InputDialog`，预填默认名（`新建文件夹`，重名自动 `新建文件夹 (2)`、`(3)`…，默认全选）。
2. 确认时校验：空白、非法字符、同名已存在（文件或目录）、无权限。
   - 校验失败 → **留窗**并在框内显示原因（不静默、不关窗）。
   - 成功 → 建目录 → 按上表分流。
3. `InputDialog` 从 `Controls/FileFilterEditor.axaml.cs:720` 的纯代码窗口抽成 `Dialogs/InputDialog.axaml(.cs)`：补主题画刷（它目前裸默认色，违反规则 4）、加校验回调与错误行；`FileFilterEditor` 的两处调用（预设重命名 / 另存预设）改用它，行为不变。

## 可测接缝

```csharp
// CustomFilePickerControl
public bool TryCreateFolder(string? name, out string? error);   // error 已本地化，供 UI 直接显示
```

测试直接调它，不用真弹窗。

## 任务

- [x] **T1** 新建 `Dialogs/InputDialog.axaml(.cs)`（主题色 + `Func<string, string?>? validator` + 错误行 + Enter/Esc），删除 `FileFilterEditor.axaml.cs` 末尾的旧类，两个调用点改参数签名不变。
- [x] **T2** `CustomFilePickerControl.axaml`：地址行加 `NewFolderButton`（`IconFolderAdd`，在系统浏览之后，`Classes` 走 `addrRow`）；`.axaml.cs`：`NewFolder_Click`、`SuggestNewFolderName()`、`TryCreateFolder`、`Ctrl+Shift+N`（`AddHandler` 冒泡，不抢输入框按键）、建完分流 + `SaveFile` 焦点。
  - 实施补充：PickItems / OpenFile 的刷新分支必须包在 `_syncingSelection` 闸门内并按既有累积回填 `SelectedItems` —— `LoadDirectory` 会先置空 `SelectedItem`，其 `SelectionChanged` 会清空累积选择（测试 ② 抓到的正是这个）。
- [x] **T3** i18n：`Picker_NewFolder` / `Picker_NewFolderTitle` / `Picker_NewFolderPrompt` / `Picker_NewFolderDefaultName` / `Picker_NewFolderExists` / `Picker_NewFolderInvalidName` / `Picker_NewFolderFailed` / `Picker_NewFolderNoPath`（三语，BOM + CRLF，插在文件头；总数 1438 → 1446）。
- [x] **T4** `IconTestViewModel`：`IconFolderAdd` 由 `Defined` 改 `Converted` 并补使用位置（规则 8）。
- [x] **T5** ui-tests：`CustomFilePickerNewFolderTests` —— ①PickFolder 建完进入新目录且 `PathChanged` 抛新路径 ②PickItems 简单模式建完**不清空**已选 ③重名报错且不建 ④非法字符报错 ⑤默认名自动去重 ⑥`OpenFile` 建完不导航。
- [x] **T6** 验证：`build -Target avalonia` 0 错误、`test -Target ui-tests` 60.3s 全绿、`-Task check`（0.5.6 / 三语 1446 key）、`-Task guards`（8 条）；已更新 `docs/progress-avalonia-detail.md` 与 `docs/PROGRESS.md`；内嵌「系统浏览」死按钮与 `ShowExtractFolderAsync` 无调用方已登记 KI-014 / KI-015。
  - 待用户定：`RELEASE_NOTES.md` 是否更新及范围（规则 18）。

## 决策记录

| # | 决策 | 依据 |
|---|------|------|
| 1 | **所有模式都给**「新建文件夹」（含 PickItems / OpenFile） | 用户 2026-11-04 定 |
| 2 | 命名走 `InputDialog`，不做列表内联改名 | 内联编辑要额外的编辑态 + 焦点 + 重名处理，成本高一档；输入框可复用现有弹窗 |
| 3 | PickItems / OpenFile 建完不导航 | 避免清空累积选择 / 进入空目录 |
| 4 | 在 `beta` 分支实施 | 用户 2026-11-04 指定（与「新功能走 alpha」的分支约定不同，已知并接受） |

## 边界与风险

- 只处理「在当前浏览目录下新建一层」，不做多级路径输入。
- 无权限目录：捕获异常并给本地化原因，不抛栈。
- 新建后 `PathChanged` 会驱动解压窗重建预览（`ExtractSettingsWindow.OnViewModelPropertyChanged`），属预期。
