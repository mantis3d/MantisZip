# 拖拽添加（Drag-In Add）功能移植 + 添加路径覆层

## 背景

WPF 版支持「拖拽文件到程序窗口 → 添加到当前压缩包」，Avalonia 移植时只实现了「拖入压缩包 → 打开」，非压缩包文件被静默忽略。本次补全：

1. 完整移植 WPF 三分支拖入行为
2. 新增 Avalonia 窗口内覆层：拖拽悬停期间显示「添加到 {压缩包内当前目录}」绿色/红色两色状态
3. 支持文件夹拖入（`IStorageFolder`，WPF FileDrop 天然支持）
4. 复用刚完成的冲突处理策略（`CreateExtractOptions` + `ShowAddFileConflictDialogAsync`）

用户已确认：全做三分支 / 文件夹带上 / 冲突处理直接复用 / 确认框沿用 WPF 文案 / 覆层走方案 A（固定 CurrentFolder + 两色）。

## 决策要点

- **覆层不用 Win32 `OverlayController`**：拖拽解压的 OverlayController 检测的是外部 Explorer 窗口（`WindowFromPoint` + 窗口分类），而拖拽添加目标在 MantisZip 窗口内部，Avalonia 的 `DragOver`/`DragLeave` 事件能拿到位置与状态。窗口内原生 Border 覆层（与 `Main_DropHint` 同技术）即可，简单可控。
- **方案 A**：覆层固定显示 `CurrentFolder`（当前浏览的压缩包内目录），不跟随目录行悬停（方案 B 留作后续增强）。
- **两色语义**：绿 = 可添加（已打开压缩包 + 引擎支持添加）；红 = 不可添加（已打开压缩包但格式不支持，如 RAR/ISO）。
- **压缩包切换保留**：已打开压缩包时拖入单个压缩包 → 直接切换打开（与 WPF 一致）。
- **未打开压缩包**：拖入压缩包 → 打开；拖入非压缩包 → 打开 `CompressSettingsWindow` 预填源文件（WPF 行为）。

## 实现步骤

### 1. ViewModel：抽取 `AddFilesToArchiveAsync`

`MainWindowViewModel.AddFiles()`（:2369）已含完整添加流程（密码、冲突处理、entryBasePath、进度、刷新），但入口是 `GetOpenFilePaths()` 文件选择器。抽取公共方法供拖拽复用：

```csharp
public async Task<bool> AddFilesToArchiveAsync(IReadOnlyList<string> files)
{
    if (CurrentArchivePath == null || RunWithProgress == null || files.Count == 0) return false;
    // ... 原 AddFiles 主体（engine 获取、密码、CreateExtractOptions、AddToArchiveAsync、RefreshArchive）
    return completed;
}
```

`AddFiles()` 改为：拿选择器路径 → 调用 `AddFilesToArchiveAsync(files)`。返回 bool 供拖拽分支判断成功与否（对齐 WPF 的 SetStatus 语义）。

### 2. 本地化 key（两文件成对，插入文件头 `{` 之后）

| Key | zh-CN | en |
|-----|-------|----|
| `Main_DragAddConfirm` | `将 {0} 个文件/文件夹添加到「{1}」？` | `Add {0} file(s)/folders to "{1}"?` |
| `DragAdd_OverlayAddTo` | `添加到 {0}` | `Add to {0}` |
| `DragAdd_OverlayUnsupported` | `此格式不支持添加文件` | `This format does not support adding files` |

`CompressConflict_Add`（确认框标题，= "添加到压缩包"）已在两文件存在（:231/:233），复用。

### 3. MainWindow.axaml

- Window 上追加 `DragDrop.DragLeave="OnWindowDragLeave"`（:16-18 三行之后）
- Row 3 内容 Grid 内、`Main_DropHint` 之后添加覆层 Border：

```xml
<Border x:Name="DragAddOverlay" IsVisible="False" IsHitTestVisible="False"
        Background="{DynamicResource ThemeWindowBgBrush}"
        BorderBrush="#4CAF50" BorderThickness="2" CornerRadius="{DynamicResource BorderRadius}"
        HorizontalAlignment="Center" VerticalAlignment="Center"
        Padding="24,16" ZIndex="10">
  <StackPanel Spacing="{DynamicResource SpacingXs}" HorizontalAlignment="Center">
    <PathIcon Data="{StaticResource IconFolder}" Width="40" Height="40"
              HorizontalAlignment="Center" Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
    <TextBlock x:Name="DragAddOverlayText" FontSize="14"
               Foreground="{DynamicResource ThemeTextPrimaryBrush}"
               TextWrapping="Wrap" HorizontalAlignment="Center" />
  </StackPanel>
</Border>
```

- 颜色切换在 code-behind 直接改 `BorderBrush`（绿 `#4CAF50` / 红 `#F44336`），背景沿用主题色，不动资源文件。

### 4. MainWindow.axaml.cs 拖拽三分支

**`OnWindowDragOver`** 改为状态判定 + 覆层控制：

```csharp
private void OnWindowDragOver(object? sender, DragEventArgs e)
{
    if (e.DataTransfer == null || !e.DataTransfer.Formats.Contains(DataFormat.File))
    {
        e.DragEffects = DragDropEffects.None;
        HideDragAddOverlay();
        return;
    }

    var vm = DataContext as MainWindowViewModel;
    bool archiveLoaded = vm?.CurrentArchivePath != null && File.Exists(vm.CurrentArchivePath);

    if (archiveLoaded)
    {
        // 拖入单个压缩包 → 切换打开，不显示添加覆层
        var paths = GetDroppedLocalPaths(e);
        if (paths.Count == 1 && ArchiveFormatHelper.IsArchiveFile(paths[0]))
        {
            e.DragEffects = DragDropEffects.Copy;
            HideDragAddOverlay();
            return;
        }

        var engine = ArchiveEngineFactory.GetEngineByExtension(vm!.CurrentArchivePath!);
        bool canAdd = engine?.CanAdd(ArchiveFormatHelper.GetFormat(vm.CurrentArchivePath!)) == true;
        if (canAdd)
        {
            e.DragEffects = DragDropEffects.Copy;
            ShowDragAddOverlay(green, LocalizationManager.T("DragAdd_OverlayAddTo", BuildTargetDisplay(vm)));
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            ShowDragAddOverlay(red, LocalizationManager.T("DragAdd_OverlayUnsupported"));
        }
        return;
    }

    e.DragEffects = DragDropEffects.Copy;   // 未打开压缩包：Drop 时处理打开/压缩
    HideDragAddOverlay();
}
```

**`OnWindowDragLeave`**：`HideDragAddOverlay()`。

**`OnWindowDrop`** 重写为三分支（对齐 WPF `Window_Drop`）：

```csharp
private async void OnWindowDrop(object? sender, DragEventArgs e)
{
    HideDragAddOverlay();
    if (e.DataTransfer == null) return;

    var paths = GetDroppedLocalPaths(e);   // IStorageFile + IStorageFolder 均取本地路径
    if (paths.Count == 0) return;

    var vm = DataContext as MainWindowViewModel;
    if (vm == null) return;

    if (vm.CurrentArchivePath != null && File.Exists(vm.CurrentArchivePath))
    {
        // 分支 1：单个压缩包 → 切换打开
        if (paths.Count == 1 && ArchiveFormatHelper.IsArchiveFile(paths[0]))
        {
            await vm.LoadArchiveAsync(paths[0]);
            return;
        }
        // 分支 2：确认框 → 添加到当前压缩包
        var result = await AppMessageBox.Show(
            LocalizationManager.T("Main_DragAddConfirm", paths.Count, Path.GetFileName(vm.CurrentArchivePath)),
            LocalizationManager.T("CompressConflict_Add"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, this);
        if (result == MessageBoxResult.Yes)
            await vm.AddFilesToArchiveAsync(paths);
        return;
    }

    // 未打开压缩包
    if (ArchiveFormatHelper.IsArchiveFile(paths[0]))
        await vm.LoadArchiveAsync(paths[0]);                    // 分支 3a：打开
    else
    {
        var dialog = new CompressSettingsWindow(paths);         // 分支 3b：压缩对话框预填
        await dialog.ShowDialog(this);
    }
}
```

**辅助方法**：

- `GetDroppedLocalPaths(DragEventArgs e)`：遍历 `e.DataTransfer.Items`，`TryGetRaw(DataFormat.File)`，`raw is IStorageFile or IStorageFolder`（统一 `IStorageItem`）→ `TryGetLocalPath()`，去空。注意 `IStorageFolder` 也继承 `IStorageItem`，统一判断即可。
- `BuildTargetDisplay(vm)`：`Path.GetFileName(CurrentArchivePath)` + `/` + `CurrentFolder`（空则只显压缩包名）。
- `ShowDragAddOverlay(bool isGreen, string text)` / `HideDragAddOverlay()`：切换 `IsVisible` + `BorderBrush`（绿 `#4CAF50` / 红 `#F44336`）+ 文案。

### 5. 验证

- `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj -p:SkipShellExtCopy=true` 0 错误
- `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj -p:SkipShellExtCopy=true` 通过
- 手动场景：已打开 zip 拖入 txt → 绿覆层 + 确认框 + 添加成功；已打开 rar 拖入 txt → 红覆层 + 禁止；未打开压缩包拖入文件夹 → 压缩对话框预填

## 约束

- 只改 Avalonia（规则 11），WPF 不动
- 本地化 key 两文件成对（规则 13）
- 覆层用主题资源 + 现有类样式，不硬编码尺寸（规则 4/5）
- 提交前更新 PROGRESS.md（规则 3）