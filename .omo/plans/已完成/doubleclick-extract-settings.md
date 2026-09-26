# 双击行为 + 解压后删原包 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 为 MantisZip 添加两个用户可配置功能：1) 资源管理器双击压缩包的行为四选一；2) 解压后将原包移到回收站。

**Architecture:** 两个功能都从 AppSettings 添加属性开始，通过 SettingsWindow UI 暴露给用户配置。Feature 1 修改 App.xaml.cs 的 CLI 分发逻辑；Feature 2 在所有解压完成点调用回收站辅助方法。

**Tech Stack:** .NET 9 / WPF / C# / Microsoft.VisualBasic (回收站)

---

## 涉及文件总览

| 文件 | 改动 |
|------|------|
| `src/MantisZip.UI/AppSettings.cs` | 新增 `DoubleClickAction` (string) + `DeleteArchiveAfterExtract` (bool) |
| `src/MantisZip.UI/App.xaml.cs` | `--open` 分支改为按 `DoubleClickAction` 分发 |
| `src/MantisZip.UI/AppPartials/App.Extract.cs` | 新增 `TryDeleteArchiveAfterExtract` + 在批量/单文件解压后调用 |
| `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs` | `ExtractAsync()` 末尾调用 TryDeleteArchiveAfterExtract |
| `src/MantisZip.UI/MainWindow/MainWindow.UI.cs` | `ExtractSelectedAsync()` 末尾调用 TryDeleteArchiveAfterExtract |
| `src/MantisZip.UI/Dialogs/SettingsWindow.xaml` | 文件关联 Tab 新增 GroupBox+ComboBox；解压 Tab 新增 CheckBox |
| `src/MantisZip.UI/Dialogs/SettingsWindow.xaml.cs` | LoadSettings/SaveSettings 读写新属性 |
| `src/MantisZip.UI/Localization/strings.zh.json` | 新增 7 个中文键 |
| `src/MantisZip.UI/Localization/strings.en.json` | 新增 7 个英文键 |

---

## TODOs

- [ ] 1. AppSettings — 新增 `DoubleClickAction` 和 `DeleteArchiveAfterExtract`

  **What to do**:
  在 AppSettings.cs 的 `// ===== 交互 =====` 区域（现有 `EnableDragExtract` 旁）添加：

  ```csharp
  /// <summary>资源管理器双击压缩包时的行为：open / extract-here / smart-extract / extract-dialog</summary>
  public string DoubleClickAction { get; set; } = "open";

  /// <summary>解压完成后将原压缩包移到回收站</summary>
  public bool DeleteArchiveAfterExtract { get; set; } = false;
  ```

  **Recommended Agent Profile**:
  - Category: `quick`
  - Reason: 纯粹新增两个属性，一行代码

  **Acceptance Criteria**:
  - [ ] AppSettings.cs 包含新属性
  - [ ] 默认值为 "open" 和 false

  **Evidence**: 文件读写确认

  **Commit**: YES
  - Message: `feat: add DoubleClickAction and DeleteArchiveAfterExtract settings`

---

- [ ] 2. 本地化字符串 — 新增 7 个键的中英文

  **What to do**:
  在 `strings.zh.json` 和 `strings.en.json` 中添加：

  **中文 (strings.zh.json)**:
  ```json
  "Settings_Assoc_DoubleClickGroup": "双击行为",
  "Settings_Assoc_DoubleClickLabel": "资源管理器双击压缩包时",
  "Settings_Assoc_DoubleClick_Open": "打开（浏览压缩包）",
  "Settings_Assoc_DoubleClick_ExtractHere": "原地解压",
  "Settings_Assoc_DoubleClick_SmartExtract": "智能原地解压",
  "Settings_Assoc_DoubleClick_ExtractDialog": "打开解压窗口",
  "Settings_Extract_DeleteAfterExtract": "解压完成后将原压缩包移到回收站"
  ```

  **英文 (strings.en.json)**:
  ```json
  "Settings_Assoc_DoubleClickGroup": "Double-click behavior",
  "Settings_Assoc_DoubleClickLabel": "When double-clicking archives in Explorer",
  "Settings_Assoc_DoubleClick_Open": "Open (browse archive)",
  "Settings_Assoc_DoubleClick_ExtractHere": "Extract here",
  "Settings_Assoc_DoubleClick_SmartExtract": "Smart extract",
  "Settings_Assoc_DoubleClick_ExtractDialog": "Open extract dialog",
  "Settings_Extract_DeleteAfterExtract": "Move original archive to Recycle Bin after extraction"
  ```

  **Recommended Agent Profile**:
  - Category: `quick`

  **Acceptance Criteria**:
  - [ ] strings.zh.json 新增 7 个键
  - [ ] strings.en.json 新增 7 个键

  **Commit**: YES (group with Task 1)
  - Message: `feat: add DoubleClickAction and DeleteArchiveAfterExtract settings`

---

- [ ] 3. SettingsWindow.xaml — 文件关联 Tab 新增双击行为 GroupBox

  **What to do**:
  在文件关联 Tab 的 `StackPanel` 内，`GroupBox`（关联格式）**之后**、`StackPanel` 结束之前添加：

  ```xml
  <!-- 双击行为 -->
  <GroupBox Header="{l:L Settings_Assoc_DoubleClickGroup}" Padding="8" Margin="0,12,0,0">
      <StackPanel>
          <TextBlock Text="{l:L Settings_Assoc_DoubleClickLabel}"
                     FontWeight="SemiBold" Margin="0,0,0,8"
                     Foreground="{DynamicResource Theme_TextPrimary}"/>
          <ComboBox x:Name="DoubleClickActionCombo" Width="280" HorizontalAlignment="Left"
                    Background="{DynamicResource Theme_WindowBg}"
                    Foreground="{DynamicResource Theme_TextPrimary}">
              <ComboBoxItem Tag="open" Content="{l:L Settings_Assoc_DoubleClick_Open}"/>
              <ComboBoxItem Tag="extract-here" Content="{l:L Settings_Assoc_DoubleClick_ExtractHere}"/>
              <ComboBoxItem Tag="smart-extract" Content="{l:L Settings_Assoc_DoubleClick_SmartExtract}"/>
              <ComboBoxItem Tag="extract-dialog" Content="{l:L Settings_Assoc_DoubleClick_ExtractDialog}"/>
          </ComboBox>
      </StackPanel>
  </GroupBox>
  ```

  这个 GroupBox 放在 `</GroupBox>`（关联格式的结束）和 `</StackPanel>`（外层 ScrollViewer 的 StackPanel 结束）之间。

  **Recommended Agent Profile**:
  - Category: `visual-engineering`
  - Reason: WPF XAML UI 布局，需使用 `DynamicResource Theme_*` 绑定主题色

  **Acceptance Criteria**:
  - [ ] 文件关联 Tab 底部出现"双击行为"GroupBox
  - [ ] ComboBox 包含 4 个选项

---

- [ ] 4. SettingsWindow.xaml — 解压 Tab 新增 CheckBox

  **What to do**:
  在解压 Tab 的 `StackPanel` 内，`ExtractPreservePathCheck` 之后、`Border`（双击阈值）之前添加：

  ```xml
  <CheckBox x:Name="DeleteAfterExtractCheck"
            Content="{l:L Settings_Extract_DeleteAfterExtract}"
            Margin="0,8,0,0"/>
  ```

  **Recommended Agent Profile**:
  - Category: `visual-engineering`

  **Acceptance Criteria**:
  - [ ] 解压 Tab 出现"解压完成后将原压缩包移到回收站"CheckBox

  **Commit**: YES (group with Task 3)
  - Message: `feat: add double-click action and delete-after-extract UI`

---

- [ ] 5. SettingsWindow.xaml.cs — LoadSettings + SaveSettings

  **What to do**:

  在 `LoadSettings()` 的文件关联部分末尾（`AssocStatusText` 相关行附近）添加：
  ```csharp
  // 双击行为
  foreach (ComboBoxItem item in DoubleClickActionCombo.Items)
      if ((string)item.Tag == s.DoubleClickAction) { DoubleClickActionCombo.SelectedItem = item; break; }
  ```

  在 `SaveSettings()` 的文件关联部分末尾添加：
  ```csharp
  s.DoubleClickAction = (DoubleClickActionCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "open";
  ```

  在 `LoadSettings()` 的解压部分末尾（`DoubleClickThresholdBox.Text = ...` 行后）添加：
  ```csharp
  DeleteAfterExtractCheck.IsChecked = s.DeleteArchiveAfterExtract;
  ```

  在 `SaveSettings()` 的解压部分末尾（`s.DoubleClickOpenThreshold = ...` 行后）添加：
  ```csharp
  s.DeleteArchiveAfterExtract = DeleteAfterExtractCheck.IsChecked == true;
  ```

  **Recommended Agent Profile**:
  - Category: `quick`

  **Acceptance Criteria**:
  - [ ] LoadSettings 正确读取两个新属性到 UI
  - [ ] SaveSettings 正确将 UI 值写入 AppSettings

  **Commit**: YES (group with Task 3)

---

- [ ] 6. App.xaml.cs — 修改 `--open` 分发

  **What to do**:
  将 `case "--open":` 分支替换为：

  ```csharp
  case "--open":
  {
      var path = e.Args.Length > 1 ? e.Args[1] : null;
      if (string.IsNullOrEmpty(path)) { HandleOpen(null); return; }

      var action = AppSettings.Instance.DoubleClickAction;
      switch (action)
      {
          case "extract-here":
              HandleExtractHere(new[] { path });
              break;
          case "smart-extract":
              HandleExtractSmart(new[] { path });
              break;
          case "extract-dialog":
              HandleExtract(new[] { path });
              break;
          default: // "open"
              HandleOpen(path);
              break;
      }
      return;
  }
  ```

  **注意**：`AppSettings.Instance` 首次访问会触发 `Load()`，需要在 `OnStartup` 中确保设置已加载。查看现有代码，`OnStartup` 开头已经调用了 `InitializeApp()` 或其他初始化，AppSettings 的 Lazy 初始化会在首次访问时触发，所以这里直接访问 `AppSettings.Instance` 是安全的。但确认 `OnStartup` 中需先调用 `base.OnStartup(e)` 让 WPF 完成初始化后再访问。

  **Recommended Agent Profile**:
  - Category: `quick`
  - Reason: 简单的 switch 分发逻辑

  **Acceptance Criteria**:
  - [ ] 设置 DoubleClickAction="extract-here" 后双击 → 触发原地解压
  - [ ] 设置 DoubleClickAction="extract-dialog" 后双击 → 弹出解压窗口
  - [ ] 默认值 "open" → 行为不变

  **QA Scenarios**:
  - 无（运行逻辑验证需启动应用并修改设置后再双击文件，手动测试确认）

  **Commit**: YES
  - Message: `feat: dispatch --open based on DoubleClickAction setting`

---

- [ ] 7. App.Extract.cs — 新增 `TryDeleteArchiveAfterExtract` 并调用

  **What to do**:

  **Step 1**: 在 `App.Extract.cs` 的 `App` partial class 内（`RunExtractStatic` 方法附近）添加辅助方法：

  ```csharp
  /// <summary>
  /// 解压成功后，将原压缩包移到回收站（如果设置启用）。
  /// 仅在删除成功时记录日志，失败时仅记录警告（不影响解压结果）。
  /// </summary>
  private static void TryDeleteArchiveAfterExtract(string archivePath)
  {
      if (!AppSettings.Instance.DeleteArchiveAfterExtract) return;
      if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return;
      try
      {
          Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
              archivePath,
              Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
              Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
          App.LogDebug("TryDeleteArchiveAfterExtract: moved '{0}' to recycle bin", archivePath);
      }
      catch (Exception ex)
      {
          App.LogDebug("TryDeleteArchiveAfterExtract: failed for '{0}': {1}", archivePath, ex.Message);
      }
  }
  ```

  **Step 2**: 在 `HandleExtractBatchCore` 中，每个文件解压成功后调用（在 `succeeded++` 之后、`UpdateBatchItemStatus(i, BatchItemStatus.Completed)` 之前或之后）：

  找到代码中 `succeeded++;` 的位置（约 line 482），在其后添加：
  ```csharp
  TryDeleteArchiveAfterExtract(archivePath);
  ```

  注意：应该在单个文件成功解压后立即调用，而不是在循环结束后批量调用。

  **Step 3**: 在 `RunExtractStatic` 中，解压成功后调用：

  找到 `await engine.ExtractAsync(...)` 完成后（约 line 697-698），`progressWindow.Dispatcher.InvokeAsync` 之前或之后添加：
  ```csharp
  TryDeleteArchiveAfterExtract(archivePath);
  ```

  **Step 4**: 在文件顶部添加 using（如果还没有）：
  ```csharp
  using Microsoft.VisualBasic.FileIO;
  ```

  **Recommended Agent Profile**:
  - Category: `quick`
  - Reason: 方法短小，调用点明确

  **Acceptance Criteria**:
  - [ ] 开启设置后，CLI 批量解压完成 → 原压缩包出现在回收站
  - [ ] 关闭设置后，解压完成 → 原压缩包保留
  - [ ] 删除失败时不影响解压结果（catch 仅记录日志）

  **Commit**: YES (group with Task 8)
  - Message: `feat: delete original archive after extraction (recycle bin)`

---

- [ ] 8. MainWindow — ExtractAsync + ExtractSelectedAsync 末尾调用

  **What to do**:

  **Step 1**: 在 `MainWindow.xaml.cs` 的 `ExtractAsync()` 方法末尾，`progressWindow.Close()` 之后、`SetStatus()` 之前添加：

  ```csharp
  TryDeleteArchiveAfterExtract(archivePath);
  ```

  注意：`ExtractAsync` 是 `MainWindow` 的实例方法，而 `TryDeleteArchiveAfterExtract` 是 `App` 的静态方法。调用方式为 `App.TryDeleteArchiveAfterExtract(archivePath)`。

  **Step 2**: 在 `MainWindow.UI.cs` 的 `ExtractSelectedAsync()` 方法末尾，`pw.SetComplete(L.T(...))` 之后、`App.LogDebug(...)` 之前添加：

  ```csharp
  App.TryDeleteArchiveAfterExtract(_currentArchivePath!);
  ```

  **Recommended Agent Profile**:
  - Category: `quick`

  **Acceptance Criteria**:
  - [ ] MainWindow 中点击解压完成 → 原包移到回收站（设置开启时）
  - [ ] 选中条目解压完成 → 原包移到回收站（设置开启时）

  **Commit**: YES (group with Task 7)

---

## Final Verification

- [ ] F1. **构建验证** — `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 无错误
- [ ] F2. **功能验证** — 打开设置 → 文件关联 Tab → 双击行为 GroupBox 可见且 ComboBox 可选
- [ ] F3. **功能验证** — 打开设置 → 解压 Tab → 复选框可见
- [ ] F4. **保存验证** — 修改设置后保存 → 重启 → 设置持久化
- [ ] F5. **双击分发验证** — 设置双击=原地解压 → 在资源管理器中双击 .zip → 触发解压而不是打开浏览

## Commit Strategy

| Commit | 包含任务 | 消息 |
|--------|---------|------|
| 1 | 1, 2 | `feat: add DoubleClickAction and DeleteArchiveAfterExtract settings` |
| 2 | 3, 4, 5 | `feat: add double-click action and delete-after-extract UI` |
| 3 | 6 | `feat: dispatch --open based on DoubleClickAction setting` |
| 4 | 7, 8 | `feat: delete original archive after extraction (recycle bin)` |
