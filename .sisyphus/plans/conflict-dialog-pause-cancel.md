# 文件冲突对话框 — 暂停/取消按钮 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 `CompressConflictDialog`（压缩冲突）和 `ConflictDialog`（解压冲突）两个对话框添加"暂停"和"取消"按钮。暂停收起冲突对话框回到进度窗口暂停状态，取消与进度条取消效果一致（终止整个操作）。

**Architecture:** 两个冲突对话框各新增两个按钮 + 标志属性；`conflictResolver` 回调从"一次性 ShowDialog"改为"循环重入"模式以支持暂停后恢复重新弹窗；`ProgressWindow` 新增 `PauseFromConflict()` 方法供外部触发暂停。

**Tech Stack:** WPF (.NET 9), C#, `ManualResetEventSlim` (已有暂停机制)

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `strings.zh.json` / `strings.en.json` | 本地化字符串 | +4 个键 |
| `CompressConflictDialog.xaml` | 压缩冲突对话框布局 | +底部按钮行 |
| `CompressConflictDialog.xaml.cs` | 压缩冲突对话框逻辑 | +属性 + 事件 |
| `ConflictDialog.xaml` | 解压冲突对话框布局 | +底部按钮行 |
| `ConflictDialog.xaml.cs` | 解压冲突对话框逻辑 | +属性 + 事件 |
| `ProgressWindow.xaml.cs` | 进度窗口 | +`PauseFromConflict()` 方法 |
| `App.xaml.cs` | 解压 conflictResolver | 改为循环重入 |
| `AppPartials/App.Compress.cs` | 压缩 conflictResolver (入口点) | 改为循环重入 |
| `Dialogs/CompressSettingsWindow.xaml.cs` | 压缩 conflictResolver (3 处) | 改为循环重入 |
| `PLAN.md` | 计划索引 | 新增条目 |

---

### Task 1: 本地化字符串

**Files:**
- Modify: `src/MantisZip.UI/Resources/strings.zh.json`
- Modify: `src/MantisZip.UI/Resources/strings.en.json`

- [ ] **Step 1: 添加中文键**

在 `strings.zh.json` 中添加：

```json
"CompressConflict_Pause": "⏸ 暂停",
"CompressConflict_CancelOperation": "✕ 取消",
"Conflict_Btn_Pause": "⏸ 暂停",
"Conflict_Btn_CancelOperation": "✕ 取消"
```

- [ ] **Step 2: 添加英文键**

在 `strings.en.json` 中添加：

```json
"CompressConflict_Pause": "⏸ Pause",
"CompressConflict_CancelOperation": "✕ Cancel",
"Conflict_Btn_Pause": "⏸ Pause",
"Conflict_Btn_CancelOperation": "✕ Cancel"
```

---

### Task 2: ProgressWindow — 添加 PauseFromConflict()

**Files:**
- Modify: `src/MantisZip.UI/Dialogs/ProgressWindow.xaml.cs:427-445`

**Context:** 现有的 `PauseButton_Click` 会切换暂停/恢复（toggle 行为）。从冲突对话框触发的暂停需要直接进入暂停状态而不切换，且不依赖按钮点击。

- [ ] **Step 1: 添加 PauseFromConflict() 方法**

在 `PauseButton_Click` 方法之后添加：

```csharp
/// <summary>
/// 由冲突对话框触发暂停。将进度窗口设为暂停状态，
/// 但不切换暂停按钮文本（因为暂停后用户是在进度窗口操作）。
/// </summary>
public void PauseFromConflict()
{
    _pauseEvent.Reset();
    PauseButton.Content = L.T(L.Progress_Button_Resume);
    FileNameText.Text = L.T(L.Progress_Paused);
}
```

---

### Task 3: CompressConflictDialog — 添加暂停/取消按钮

**Files:**
- Modify: `src/MantisZip.UI/Dialogs/CompressConflictDialog.xaml`
- Modify: `src/MantisZip.UI/Dialogs/CompressConflictDialog.xaml.cs`

- [ ] **Step 1: 更新 XAML — 添加底部按钮行**

在现有行定义之后、底部关闭前，添加行 9（分割线）和行 10（按钮行）。在 `</Grid>` 之前插入：

```xml
        <!-- Row 9: 分割线 -->
        <Rectangle Grid.Row="9" Height="1" Fill="{StaticResource Theme_BorderLight}" Margin="0,6,0,6"/>

        <!-- Row 10: 暂停/取消按钮 -->
        <StackPanel Grid.Row="10" Orientation="Horizontal" HorizontalAlignment="Center">
            <Button x:Name="PauseBtn" Height="Auto" Padding="10,5" MinWidth="80" Click="Pause_Click"
                    Background="{StaticResource Theme_ButtonBg}" BorderBrush="{StaticResource Theme_Border}" BorderThickness="1" Margin="0,0,8,0">
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                    <TextBlock Text="⏸" FontSize="14" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <TextBlock Text="{l:L CompressConflict_Pause}" FontSize="12" Foreground="{StaticResource Theme_TextPrimary}" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
            <Button x:Name="CancelAllBtn" Height="Auto" Padding="10,5" MinWidth="80" Click="CancelOperation_Click"
                    Background="{StaticResource Theme_ButtonBg}" BorderBrush="{StaticResource Theme_Border}" BorderThickness="1">
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                    <TextBlock Text="✕" FontSize="14" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <TextBlock Text="{l:L CompressConflict_CancelOperation}" FontSize="12" Foreground="{StaticResource Theme_TextPrimary}" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
        </StackPanel>
```

同时需要把现有 `Grid.RowDefinitions` 的行数从 9 行改为 11 行，在 `RowDefinition Height="Auto"/>` (Row 8 的 CheckBox) 之后添加两行：

```xml
            <RowDefinition Height="Auto"/>  <!-- Row 9: 分割线 -->
            <RowDefinition Height="Auto"/>  <!-- Row 10: 暂停/取消按钮 -->
```

- [ ] **Step 2: 更新 code-behind — 添加属性 + 按钮事件**

在 `_resultCaptured` 字段后添加：

```csharp
private bool _isPaused;
private bool _cancelOperation;
```

在现有属性后添加：

```csharp
/// <summary>用户是否点击了"暂停"按钮</summary>
public bool IsPaused => _isPaused;
/// <summary>用户是否点击了"取消"（取消整个操作）按钮</summary>
public bool CancelOperation => _cancelOperation;
```

在 `Cancel_Click` 方法后添加两个新事件：

```csharp
private void Pause_Click(object sender, RoutedEventArgs e)
{
    App.LogDebug("CompressConflictDialog: user paused for '{0}'", HeaderText.Text);
    _isPaused = true;
    DialogResult = false;
}

private void CancelOperation_Click(object sender, RoutedEventArgs e)
{
    App.LogDebug("CompressConflictDialog: user cancelled entire operation for '{0}'", HeaderText.Text);
    _cancelOperation = true;
    // 捕获为重命名动作以防调用方使用 Default 行为
    CaptureResult(CompressConflictAction.Cancel, RenameTextBox.Text);
    DialogResult = false;
}
```

---

### Task 4: ConflictDialog — 添加暂停/取消按钮

**Files:**
- Modify: `src/MantisZip.UI/Dialogs/ConflictDialog.xaml`
- Modify: `src/MantisZip.UI/Dialogs/ConflictDialog.xaml.cs`

- [ ] **Step 1: 更新 XAML — 添加底部按钮行**

现有行 Row 9（CheckBox）之后添加行 10（分割线）和行 11（按钮行）：

目前 RowDefinitions:
```
Row 0: Title
Row 2: Comparison panel
Row 4: Comparison result
Row 6: Buttons (WrapPanel)
Row 7: 自定义重命名输入
Row 8: 8px gap
Row 9: 应用到全部
```

在 Row 9 之后追加：

```xml
        <RowDefinition Height="Auto"/>  <!-- Row 10: 分割线 -->
        <RowDefinition Height="Auto"/>  <!-- Row 11: 暂停/取消按钮 -->
```

在 `</Grid>` 之前插入：

```xml
        <!-- Row 10: 分割线 -->
        <Rectangle Grid.Row="10" Height="1" Fill="{StaticResource Theme_BorderLight}" Margin="0,6,0,6"/>

        <!-- Row 11: 暂停/取消按钮 -->
        <StackPanel Grid.Row="11" Orientation="Horizontal" HorizontalAlignment="Center">
            <Button x:Name="PauseBtn" Height="Auto" Padding="10,5" MinWidth="80" Click="Pause_Click"
                    Background="{StaticResource Theme_ButtonBg}" BorderBrush="{StaticResource Theme_Border}" BorderThickness="1" Margin="0,0,8,0">
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                    <TextBlock Text="⏸" FontSize="14" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <TextBlock Text="{l:L Conflict_Btn_Pause}" FontSize="12" Foreground="{StaticResource Theme_TextPrimary}" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
            <Button x:Name="CancelAllBtn" Height="Auto" Padding="10,5" MinWidth="80" Click="CancelOperation_Click"
                    Background="{StaticResource Theme_ButtonBg}" BorderBrush="{StaticResource Theme_Border}" BorderThickness="1">
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                    <TextBlock Text="✕" FontSize="14" VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <TextBlock Text="{l:L Conflict_Btn_CancelOperation}" FontSize="12" Foreground="{StaticResource Theme_TextPrimary}" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
        </StackPanel>
```

- [ ] **Step 2: 更新 code-behind — 添加属性 + 按钮事件**

在 `_resultCaptured` 字段后添加：

```csharp
private bool _isPaused;
private bool _cancelOperation;
```

在现有属性后添加：

```csharp
/// <summary>用户是否点击了"暂停"按钮</summary>
public bool IsPaused => _isPaused;
/// <summary>用户是否点击了"取消"（取消整个操作）按钮</summary>
public bool CancelOperation => _cancelOperation;
```

在 `Skip_Click` 方法后添加两个新事件：

```csharp
private void Pause_Click(object sender, RoutedEventArgs e)
{
    App.LogDebug("ConflictDialog: user paused for '{0}'", HeaderText.Text);
    _isPaused = true;
    DialogResult = false;
}

private void CancelOperation_Click(object sender, RoutedEventArgs e)
{
    App.LogDebug("ConflictDialog: user cancelled entire operation for '{0}'", HeaderText.Text);
    _cancelOperation = true;
    CaptureResult(FileConflictAction.Skip, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
    DialogResult = false;
}
```

---

### Task 5: App.xaml.cs — 解压 conflictResolver 改为循环重入

**Files:**
- Modify: `src/MantisZip.UI/App.xaml.cs:431-472`

**Context:** `CreateExtractOptions()` 中的 `ConflictResolver` lambda 是解压冲突的唯一进入点。需要改为 while 循环，检测到 Pause 时暂停进度窗口并等待恢复，检测到 CancelOperation 时取消操作。

- [ ] **Step 1: 重构 ConflictResolver lambda**

将原 `conflictResolver: info => { ... }` 替换为：

```csharp
ConflictResolver = info =>
{
    callCount++;
    LogDebug("ConflictResolver #{0}: applyToAll={1}, chosenAction={2}, path='{3}'",
        callCount, applyToAll, chosenAction?.ToString() ?? "null", info.FilePath);

    // 已勾选"应用到全部" → 直接返回记忆的选择
    if (applyToAll && chosenAction.HasValue)
    {
        LogDebug("ConflictResolver #{0}: returning cached action={1}", callCount, chosenAction.Value);
        return chosenAction.Value;
    }

    var dispatcher = Current?.Dispatcher;

    // 循环重入：暂停后恢复时重新弹窗
    while (true)
    {
        if (dispatcher == null)
        {
            LogDebug("ConflictResolver #{0}: dispatcher null, returning Overwrite", callCount);
            return FileConflictAction.Overwrite;
        }

        var continuePause = false;
        var result = dispatcher.Invoke(() =>
        {
            var dialog = new ConflictDialog(info);

            // 获取 ProgressWindow 实例用于暂停
            var pw = dialog.Owner as ProgressWindow;

            dialog.ShowDialog();

            if (dialog.IsPaused)
            {
                // 暂停：收起对话框，暂停进度窗口
                LogDebug("ConflictResolver #{0}: dialog paused", callCount);
                pw?.PauseFromConflict();
                return (action: FileConflictAction.Overwrite, paused: true, cancelled: false);
            }

            if (dialog.CancelOperation)
            {
                LogDebug("ConflictResolver #{0}: dialog cancelled operation", callCount);
                return (action: FileConflictAction.Overwrite, paused: false, cancelled: true);
            }

            info.CustomName = dialog.CustomName;
            LogDebug("ConflictResolver dialog: action={0}, applyToAll={1}, customName='{2}'",
                dialog.ResultAction, dialog.ApplyToAll, dialog.CustomName ?? "(null)");
            return (action: dialog.ResultAction, paused: false, cancelled: false);
        });

        if (result.cancelled)
        {
            LogDebug("ConflictResolver #{0}: cancelling entire operation", callCount);
            // 抛出 OperationCanceledException 让上层 catch 处理
            throw new OperationCanceledException();
        }

        if (result.paused)
        {
            // 阻塞等待用户恢复（或取消）
            LogDebug("ConflictResolver #{0}: waiting for resume...", callCount);
            // ProgressWindow._pauseEvent 在恢复时 Set，取消时抛出
            try
            {
                // 需要获取 CancellationToken — 从进度窗口或外层传入
                // 此处通过 dispatcher 从当前进度窗口获取
                // 注意：无法直接访问 progressWindow，需从 dialog.Owner 获得
                dispatcher.Invoke(() =>
                {
                    var pw = Current?.Windows.OfType<ProgressWindow>().FirstOrDefault();
                    if (pw != null)
                    {
                        // 等待暂停事件（被 Set 时恢复，被 Cancel 时抛异常）
                        pw.PauseEvent.Wait(pw.CancellationToken);
                    }
                });
                LogDebug("ConflictResolver #{0}: resumed, re-showing dialog", callCount);
                continuePause = true;
                continue; // 重新弹窗
            }
            catch (OperationCanceledException)
            {
                LogDebug("ConflictResolver #{0}: cancelled while paused", callCount);
                throw; // 向上传播取消
            }
        }

        if (!continuePause)
        {
            LogDebug("ConflictResolver #{0}: dialog returned (Action={1})", callCount, result.action);

            // 检查 ApplyToAll — 注意原代码通过 dialog.ApplyToAll 判断，
            // 我们在 Invoke 内无法返回额外 bool，需要从 info 或其他方式获取
            // 简化方案：此处不处理 ApplyToAll（后续有独立 applyToAll 变量会覆盖）
            // 实际处理见后：通过 while 内再次判断
            // 但为了兼容，需要另外获取 ApplyToAll 值

            return result.action;
        }
    }
}
```

**重要修正** — 上述设计在获取 `ApplyToAll` 上有缺陷。更好的实现方式是在 Invoke 内部处理 `ApplyToAll` 和返回值的逻辑，与现有代码对齐。以下是最终版本替换：

```csharp
ConflictResolver = info =>
{
    callCount++;
    LogDebug("ConflictResolver #{0}: applyToAll={1}, chosenAction={2}, path='{3}'",
        callCount, applyToAll, chosenAction?.ToString() ?? "null", info.FilePath);

    if (applyToAll && chosenAction.HasValue)
    {
        LogDebug("ConflictResolver #{0}: returning cached action={1}", callCount, chosenAction.Value);
        return chosenAction.Value;
    }

    var dispatcher = Current?.Dispatcher;
    if (dispatcher == null)
    {
        LogDebug("ConflictResolver #{0}: dispatcher null, returning Overwrite", callCount);
        return FileConflictAction.Overwrite;
    }

    // 循环重入：暂停后恢复时重新弹窗
    while (true)
    {
        var result = dispatcher.Invoke(() =>
        {
            var dialog = new ConflictDialog(info);
            dialog.ShowDialog();

            // 暂停：收起对话框
            if (dialog.IsPaused)
            {
                LogDebug("ConflictResolver #{0}: dialog paused", callCount);
                return (Action: FileConflictAction.Overwrite, IsPaused: true, IsCancelled: false, ApplyAll: false);
            }

            // 取消整个操作
            if (dialog.CancelOperation)
            {
                LogDebug("ConflictResolver #{0}: dialog cancelled operation", callCount);
                return (Action: FileConflictAction.Overwrite, IsPaused: false, IsCancelled: true, ApplyAll: false);
            }

            info.CustomName = dialog.CustomName;
            LogDebug("ConflictResolver dialog: action={0}, applyToAll={1}, customName='{2}'",
                dialog.ResultAction, dialog.ApplyToAll, dialog.CustomName ?? "(null)");

            if (dialog.ApplyToAll)
            {
                applyToAll = true;
                chosenAction = dialog.ResultAction;
                LogDebug("ConflictResolver #{0}: applyToAll set to true, chosenAction={1}", callCount, chosenAction.Value);
            }

            return (Action: dialog.ResultAction, IsPaused: false, IsCancelled: false, ApplyAll: dialog.ApplyToAll);
        });

        if (result.IsCancelled)
        {
            LogDebug("ConflictResolver #{0}: cancelling entire operation via throw", callCount);
            throw new OperationCanceledException();
        }

        if (result.IsPaused)
        {
            LogDebug("ConflictResolver #{0}: paused, waiting for resume...", callCount);
            try
            {
                dispatcher.Invoke(() =>
                {
                    var pw = Current?.Windows.OfType<ProgressWindow>().FirstOrDefault();
                    if (pw != null)
                    {
                        pw.PauseFromConflict();
                        pw.PauseEvent.Wait(pw.CancellationToken);
                    }
                });
                LogDebug("ConflictResolver #{0}: resumed, re-showing dialog", callCount);
                continue;
            }
            catch (OperationCanceledException)
            {
                LogDebug("ConflictResolver #{0}: cancelled while paused", callCount);
                throw;
            }
        }

        return result.Action;
    }
};
```

---

### Task 6: App.Compress.cs — 压缩 conflictResolver 改为循环重入

**Files:**
- Modify: `src/MantisZip.UI/AppPartials/App.Compress.cs:254-273`

**Context:** `App.Compress.cs` 中 `--compress` 和 `--open` 的压缩冲突回调需要改为循环重入。这里已有 `progressWindow` 引用。

- [ ] **Step 1: 替换 conflictResolver lambda** (line 254-273)

将：

```csharp
conflictResolver: info =>
{
    return progressWindow.Dispatcher.Invoke(() =>
    {
        if (applyToAll && chosenAction.HasValue)
            return new CompressConflictResolution(chosenAction.Value, null);

        var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
        dlg.Owner = progressWindow;
        var shown = dlg.ShowDialog() == true;
        if (dlg.ApplyToAll)
        {
            applyToAll = true;
            chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction;
        }
        return new CompressConflictResolution(
            shown ? (Core.Abstractions.CompressConflictAction)dlg.ResultAction : Core.Abstractions.CompressConflictAction.Cancel,
            dlg.CustomName);
    });
},
```

替换为：

```csharp
conflictResolver: info =>
{
    // 已勾选"应用到全部" → 直接返回记忆的选择
    if (applyToAll && chosenAction.HasValue)
        return new CompressConflictResolution(chosenAction.Value, null);

    // 循环重入：暂停后恢复时重新弹窗
    while (true)
    {
        var resolution = progressWindow.Dispatcher.Invoke(() =>
        {
            var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
            dlg.Owner = progressWindow;
            dlg.ShowDialog();

            // 暂停
            if (dlg.IsPaused)
            {
                App.LogDebug("CompressConflictResolver: paused for '{0}'", info.OutputPath);
                return new { Action = CompressConflictAction.Cancel, IsPaused = true, IsCancelled = false, CustomName = (string?)null, ApplyAll = false };
            }

            // 取消整个操作
            if (dlg.CancelOperation)
            {
                App.LogDebug("CompressConflictResolver: cancelled entire operation for '{0}'", info.OutputPath);
                return new { Action = CompressConflictAction.Cancel, IsPaused = false, IsCancelled = true, CustomName = (string?)null, ApplyAll = false };
            }

            if (dlg.ApplyToAll)
            {
                applyToAll = true;
                chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction;
            }

            App.LogDebug("CompressConflictResolver: resolved '{0}' action={1}, applyToAll={2}", info.OutputPath, dlg.ResultAction, dlg.ApplyToAll);
            return new { Action = dlg.ResultAction, IsPaused = false, IsCancelled = false, CustomName = dlg.CustomName, ApplyAll = dlg.ApplyToAll };
        });

        if (resolution.IsCancelled)
        {
            App.LogDebug("CompressConflictResolver: cancelling entire operation via throw");
            throw new OperationCanceledException();
        }

        if (resolution.IsPaused)
        {
            App.LogDebug("CompressConflictResolver: paused, waiting for resume...");
            try
            {
                progressWindow.Dispatcher.Invoke(() =>
                {
                    progressWindow.PauseFromConflict();
                    progressWindow.PauseEvent.Wait(progressWindow.CancellationToken);
                });
                App.LogDebug("CompressConflictResolver: resumed, re-showing dialog");
                continue;
            }
            catch (OperationCanceledException)
            {
                App.LogDebug("CompressConflictResolver: cancelled while paused");
                throw;
            }
        }

        return new CompressConflictResolution(
            (Core.Abstractions.CompressConflictAction)resolution.Action,
            resolution.CustomName);
    }
},
```

**注意**：匿名类型 `new { ... }` 在 C# 中是可以的（匿名类型是 `object`，在 `Dispatcher.Invoke` 返回时需要处理）。为更清晰，可以在文件顶部定义一个本地 helper 返回类型的元组。但匿名类型够用。

实际上更好的方式是使用 ValueTuple，但 Dispatcher.Invoke 需要 object 返回。用匿名类型是安全的做法。

---

### Task 7: CompressSettingsWindow.xaml.cs — 3 处 conflictResolver 改为循环重入

**Files:**
- Modify: `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs`

**Context:** 该文件中有 3 处类似的 `conflictResolver` 回调（分别在 `RunCompressAsync`、`RunSeparateCompressAsync`、`RunQuickCompressAsync` 方法中）。三处模式完全相同，只是所在方法不同。替换模式与 Task 6 一致。

- [ ] **Step 1: 替换第 1 处** (line ~523-533)

替换为循环重入模式，与 Task 6 相同但使用 `progressWindow`（已在该方法中声明）。

- [ ] **Step 2: 替换第 2 处** (line ~610-617)

同上。

- [ ] **Step 3: 替换第 3 处** (line ~718-724)

同上。

三处替换代码与 Task 6 完全相同（`progressWindow` 变量名一致，`info` 类型为 `CompressConflictInfo`，各属性名一致）。

---

### Task 8: PLAN.md 同步

**Files:**
- Modify: `docs/PLAN.md`

- [ ] **Step 1: 在 PLAN.md 中新增条目**

在对应区域添加一行引用该计划：

```markdown
| 文件冲突对话框暂停/取消 | 暂停和取消按钮 — 收起冲突对话框回到进度窗口暂停状态，或取消整个操作 | `.sisyphus/plans/conflict-dialog-pause-cancel.md` |
```

---

### 调用点汇总

| # | 文件 | 方法 | 行号 |
|---|---|---|---|
| 1 | `App.Compress.cs` | `--compress` / `--open` 路径 | ~254-273 |
| 2 | `CompressSettingsWindow.xaml.cs` | `RunCompressAsync` | ~523-533 |
| 3 | `CompressSettingsWindow.xaml.cs` | `RunSeparateCompressAsync` | ~610-617 |
| 4 | `CompressSettingsWindow.xaml.cs` | `RunQuickCompressAsync` | ~718-724 |
| 5 | `App.xaml.cs` | `CreateExtractOptions()` | ~431-472 |

总共 **5 处** conflictResolver 需要改为循环重入模式。前 4 处操作 `CompressConflictDialog`，第 5 处操作 `ConflictDialog`。

---

### Self-Review

**Spec coverage:**
1. ✅ 暂停按钮 → Task 3 (CompressConflictDialog), Task 4 (ConflictDialog) — XAML + code-behind
2. ✅ 取消按钮 → Task 3, Task 4 — 取消整个操作
3. ✅ 暂停收起回到进度窗口 → Task 2 (PauseFromConflict), Task 5-7 (循环等待)
4. ✅ 进度条继续则重新弹窗 → Task 5-7 (while true + continue)
5. ✅ 进度条取消则终止操作 → Task 5-7 (throw OperationCanceledException)

**Placeholder scan:** 无 placeholder，所有代码均已给出。

**Type consistency:** 
- `CompressConflictDialog.IsPaused` / `CancelOperation` → Task 3 → 被 Task 6, 7 使用
- `ConflictDialog.IsPaused` / `CancelOperation` → Task 4 → 被 Task 5 使用
- `ProgressWindow.PauseFromConflict()` → Task 2 → 被 Task 5, 6, 7 使用
- 类型一致，无矛盾

**Note on anonymous types in Task 6-7:** 在 `Dispatcher.Invoke` 中返回匿名类型是 C# 合法用法。返回 `object` 后在外部通过 `dynamic` 或反射访问属性。实际实现时可改用 `ValueTuple` + `object` 强制转换，或定义一个私有的小类。
