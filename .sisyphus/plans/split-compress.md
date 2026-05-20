# 拆分"快速压缩"为两个菜单项

## TL;DR

> **Quick Summary**: 将现有的单个右键"快速压缩"菜单项拆分为两个独立项——"压缩到独立的（文件名）"（每个目录依次压缩为独立压缩包）和"压缩到（父目录名）"（所有目录打包为一个以公共父目录命名的压缩包）。两个新模式均引入 IPC 合并机制，避免多选时启动多个进程。
>
> **Deliverables**:
> - --compress-separate CLI handler（IPC 合并 + 依次压缩每个选中项）
> - --compress-combined CLI handler（IPC 合并 + 公共父目录检测 + 名称输入弹窗）
> - Shell 菜单注册（动词重编号，#5→#5+#6, #6→#7）
> - AppSettings 两个独立开关
> - SettingsWindow 复选框替换
> - 本地化字符串
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 2 waves
> **Critical Path**: Localization → AppSettings → CLI handlers → Shell/Settings

---

## Context

### Original Request
右键"快速压缩"菜单选择多个目录时会分别启动多个压缩进程，需要改进为依次压缩（独立模式）或打包为一个（合并模式），同时保留旧的 --compress 对话框模式。

### Interview Summary
**Key Decisions**:
- 替换旧的单个 --compress-quick 为两个独立的新菜单
- **压缩到独立的（文件名）**: IPC 合并路径后，依次为每个选中项创建独立压缩包
- **压缩到（父目录名）**: IPC 合并路径后，将所有选中项打包为一个以公共父目录命名的压缩包
- 无公共父目录时（跨驱动器）：弹输入框让用户手动输入压缩包名
- 两个菜单项在单选/多选时都显示
- 失败处理：跳过失败项继续处理，最后汇总
- 进度窗口：简洁版，显示"正在压缩第 N/M 个目录"
- **EnableCompressSeparate** / **EnableCompressCombined**：各自独立开关，默认 true
- 旧的 EnableQuickCompress 保留不动但不再使用

---

## Work Objectives

### Core Objective
将单一点击即压缩的右键菜单拆分为两个语义明确的项，并在多选时通过 IPC 合并避免多进程并发。

### Concrete Deliverables
- AppSettings.EnableCompressSeparate / EnableCompressCombined 属性
- --compress-separate + --compress-combined CLI 处理程序（含 IPC 合并）
- ShellIntegration：动词名重构 + 重编号 + 新旧清理
- SettingsWindow：复选框替换
- 本地化字符串（中/英）

### Must Have
- IPC 合并：多选时仅启动一个收集进程，其余通过管道传递路径后退出
- 分离模式依次压缩，不并发
- 合并模式的公共父目录算法正确
- 跨驱动器时弹出输入框让用户命名
- 完成进度窗口汇总成功/失败数量

### Must NOT Have
- 不修改现有 --compress（对话框模式）的行为
- 不修改 MainWindow 工具栏
- 不删除旧的 EnableQuickCompress（仅不再读取）

---

## Verification Strategy

> **ALL verification is agent-executed.**

### Test Decision
- **Infrastructure**: YES (xUnit)
- **Automated tests**: CLI handlers in UI project → build + registry checks
- **Manual smoke**: WPF UI scenarios for name-prompt dialog

### QA Policy
Every task MUST include agent-executed QA scenarios:
- Registry: eg query to verify verb registration
- Build: dotnet build → 0 errors
- File output: Create test dirs, run --compress-separate, verify .zip files

---

## Execution Strategy

### Parallel Execution Waves

`
Wave 1 (Foundation — parallel):
├── Task 1: Add localization strings (zh/en + L.cs)
├── Task 2: Add AppSettings properties
└── Task 3: Rename + renumber ShellIntegration verb constants + Uninstall cleanup

Wave 2 (Core logic + UI — depends on Wave 1, partial parallel):
├── Task 4: Add --compress-separate CLI handler (IPC merge + sequential batch)
├── Task 5: Add --compress-combined CLI handler (IPC merge + common parent logic + name prompt)
├── Task 6: Update ShellIntegration InstallCascadeFor/InstallVerbs for new verbs
└── Task 7: Update SettingsWindow checkbox (replace old + add two new)

Wave FINAL (Verification):
└── F1: dotnet build + dotnet test + registry verification
`

---

## Detailed TODOs

## 1. Add localization strings

**What to do**:
- Add key Shell_CompressSeparate with value "压缩到独立的（文件名）" to Resources/strings.zh.json
- Add key Shell_CompressSeparate with value "Compress to Separate (File Name)" to Resources/strings.en.json
- Add key Shell_CompressCombined with value "压缩到（父目录名）" to Resources/strings.zh.json
- Add key Shell_CompressCombined with value "Compress to (Parent Folder)" to Resources/strings.en.json
- Add key App_CompressSeparateProgress with value "正在压缩第 {0}/{1} 个目录" to Resources/strings.zh.json
- Add key App_CompressSeparateProgress with value "Compressing {0}/{1} items" to Resources/strings.en.json
- Add key App_CompressSeparateComplete with value "已压缩 {0} 个，失败 {1} 个" to Resources/strings.zh.json
- Add key App_CompressSeparateComplete with value "Compressed {0}, failed {1}" to Resources/strings.en.json
- Add key App_CompressCombinedPromptTitle with value "输入压缩包名称" to Resources/strings.zh.json
- Add key App_CompressCombinedPromptTitle with value "Enter archive name" to Resources/strings.en.json
- Add key App_CompressCombinedPromptLabel with value "压缩包名称：" to Resources/strings.zh.json
- Add key App_CompressCombinedPromptLabel with value "Archive name:" to Resources/strings.en.json
- Add key Settings_Menu_EnableCompressSeparate with value "启用压缩到独立的（文件名）" to Resources/strings.zh.json
- Add key Settings_Menu_EnableCompressSeparate with value "Enable compress to separate" to Resources/strings.en.json
- Add key Settings_Menu_EnableCompressCombined with value "启用压缩到（父目录名）" to Resources/strings.zh.json
- Add key Settings_Menu_EnableCompressCombined with value "Enable compress to combined" to Resources/strings.en.json

Add constants to Localization/L.cs (in alpha order):
- public const string App_CompressCombinedPromptLabel = "App_CompressCombinedPromptLabel"; after App_CompressCombinedPromptTitle
- public const string App_CompressCombinedPromptTitle = "App_CompressCombinedPromptTitle"; after App_CompressCombinedProgress
- public const string App_CompressSeparateComplete = "App_CompressSeparateComplete"; after App_CompressSeparateProgress
- public const string App_CompressSeparateProgress = "App_CompressSeparateProgress"; after App_CompressFailed
- public const string Settings_Menu_EnableCompressCombined = "Settings_Menu_EnableCompressCombined"; after Settings_Menu_EnableCompress
- public const string Settings_Menu_EnableCompressSeparate = "Settings_Menu_EnableCompressSeparate"; after Settings_Menu_EnableCompressCombined
- public const string Shell_CompressCombined = "Shell_CompressCombined"; after Shell_Compress
- public const string Shell_CompressSeparate = "Shell_CompressSeparate"; after Shell_CompressCombined

(Note: Remove Shell_QuickCompress constant from L.cs, or simply leave it as dead code.)

**Must NOT do**:
- Don't regenerate L.cs from scratch — just add the new constants manually
- Don't delete existing keys that might still be used

**Recommended Agent Profile**:
- **Category**: \quick\
- **Skills**: \[]\

**Parallelization**:
- **Can Run In Parallel**: YES
- **Parallel Group**: Wave 1
- **Blocks**: Tasks 4, 5, 6, 7
- **Blocked By**: None

**References**:
- \src/MantisZip.UI/Resources/strings.zh.json\ — Existing zh entries for Shell_QuickCompress (approx line 433)
- \src/MantisZip.UI/Resources/strings.en.json\ — Mirror of zh
- \src/MantisZip.UI/Localization/L.cs\ — Constants at top of file

**QA Scenarios**:
\\\
Scenario: All localization keys resolve correctly
  Tool: Bash (grep)
  Steps:
    1. grep 'Shell_CompressSeparate' strings.zh.json → Chinese text
    2. grep 'Shell_CompressSeparate' strings.en.json → English text
    3. grep 'App_CompressSeparateProgress' L.cs → constant exists
    4. grep 'Settings_Menu_EnableCompressCombined' SettingsWindow.xaml → checkbox uses this key
  Expected Result: All items found
\\\

**Commit**: YES
- Message: \i18n: add compress-separate/compress-combined keys\
- Files: \src/MantisZip.UI/Resources/strings.zh.json\, \src/MantisZip.UI/Resources/strings.en.json\, \src/MantisZip.UI/Localization/L.cs\

---

## 2. Add AppSettings properties

**What to do**:
- Add property to \AppSettings\ class (after the existing \EnableQuickCompress\ / \EnableCompressMenu\ properties):
  - \public bool EnableCompressSeparate { get; set; } = true;\
  - \public bool EnableCompressCombined { get; set; } = true;\
- The existing \EnableQuickCompress\ property stays (backward compat) but is no longer read by ShellIntegration

**Must NOT do**:
- Don't delete EnableQuickCompress (keep for backward compatibility with saved settings files)
- Don't change any other existing settings

**Recommended Agent Profile**:
- **Category**: \quick\
- **Skills**: \[]\

**Parallelization**:
- **Can Run In Parallel**: YES
- **Parallel Group**: Wave 1
- **Blocks**: Task 7
- **Blocked By**: None

**References**:
- \src/MantisZip.UI/AppSettings.cs:27\ — Pattern: existing EnableQuickCompress property

**QA Scenarios**:
\\\
Scenario: Properties exist and default to true
  Tool: Bash (grep + dotnet build)
  Steps:
    1. grep 'EnableCompressSeparate' AppSettings.cs → verify property exists
    2. grep 'EnableCompressCombined' AppSettings.cs → verify property exists
    3. dotnet build → verify 0 errors
  Expected Result: Properties found, build clean
\\\

**Commit**: YES
- Message: \eat(settings): add EnableCompressSeparate and EnableCompressCombined\
- Files: \src/MantisZip.UI/AppSettings.cs\

---

## 3. Rename + renumber ShellIntegration verb constants

**What to do**:
1. Rename constant \QuickVerb\ → \CompressSeparateVerb\ and update its value from \ 5_MantisZipQuick\ → \ 5_MantisZipCompressSeparate\
2. Add new constant \CompressCombinedVerb\ = \ 6_MantisZipCompressCombined\ (after CompressSeparateVerb)
3. Renumber \CompressVerb\ from \ 6_MantisZipCompress\ → \ 7_MantisZipCompress\
4. Rename display name:
   - \QuickDisplay\ → \CompressSeparateDisplay\ using localize (L.T(L.Shell_CompressSeparate))
   - Add \CompressCombinedDisplay\ using localize (L.T(L.Shell_CompressCombined))
5. Update \Uninstall\: add \DeleteRegistryKey(...)\ for \CompressSeparateVerb\ + \CompressCombinedVerb\ + old \MantisZipQuick\ cleanup (old key still in use in some versions)
6. Update \IsInstalled\: add check for \CompressSeparateVerb\ key alongside existing checks
7. Update comment block at top of class documenting menu order

**References**:
- \src/MantisZip.UI/ShellIntegration.cs:30-36\ — Current verb constants
- \src/MantisZip.UI/ShellIntegration.cs:38-45\ — Display name constants
- \src/MantisZip.UI/ShellIntegration.cs:48-62\ — IsInstalled
- \src/MantisZip.UI/ShellIntegration.cs:86-123\ — Uninstall

**Parallelization**:
- **Can Run In Parallel**: YES
- **Parallel Group**: Wave 1
- **Blocks**: Task 6
- **Blocked By**: Task 1 (needs localization)

**QA Scenarios**:
\\\
Scenario: Constants renamed correctly
  Tool: Bash (grep)
  Steps:
    1. grep 'CompressSeparateVerb' ShellIntegration.cs → found
    2. grep 'CompressCombinedVerb' ShellIntegration.cs → found
    3. grep '05_MantisZipCompressSeparate' ShellIntegration.cs → found (verb value)
    4. grep '06_MantisZipCompressCombined' ShellIntegration.cs → found
    5. grep '07_MantisZipCompress' ShellIntegration.cs → found (old 06→07)
    6. dotnet build → 0 errors
  Expected Result: All constants present, build clean
\\\

**Commit**: YES (groups with Task 6)
- Message: \efactor(shell): rename QuickVerb→CompressSeparateVerb, add CompressCombinedVerb\
- Files: \src/MantisZip.UI/ShellIntegration.cs\

---

## 4. Add --compress-separate CLI handler (IPC merge + sequential batch)

**What to do**:
In \App.xaml.cs\:

1. Add \case "--compress-separate": HandleCompressSeparate(e.Args.Skip(1).ToArray()); return;\ after the existing \--compress-quick\ case in \OnStartup\

2. Add \CompressSeparateMutexName\ / \CompressSeparatePipeName\ constants (alongside existing \CompressMutexName\ / \CompressPipeName\)

3. Add \_compressSeparatePipeReady\ ManualResetEventSlim (alongside \_compressPipeReady\)

4. Add \HandleCompressSeparate(string[] paths)\ method:
   - Same IPC pattern as \HandleCompress\:
     - Create Mutex, check firstInstance
     - If first: start pipe server (\StartCompressSeparatePipeServer\), wait 800ms, collect all paths
     - If subsequent: send paths via pipe (\SendPathsToCompressSeparate\), then Shutdown

5. Add \StartCompressSeparatePipeServer\ / \SendPathsToCompressSeparate\ (same pattern as existing but using separate pipe name)

6. After collecting all paths → sequential batch compress:
   - Single ProgressWindow
   - Loop through paths:
     - Determine output path: \parentDir/{name}.{defaultFormat}\ where name = Path.GetFileNameWithoutExtension (or KeepOriginalExtension variant)
     - Update progress text: \"正在压缩第 N/M 个目录"\ using L.T(L.App_CompressSeparateProgress) with format args
     - Call \compressEngine.CompressAsync\ (individual path to individual archive)
     - If conflict (File.Exists output): show CompressConflictDialog (same as current --compress-quick)
     - On failure (exception): log, increment fail counter, continue
   - After batch: show summary using L.T(L.App_CompressSeparateComplete)
   - Auto-exit after 2.5s

7. Error handling per item:
   \\\csharp
   try {
     // compress one item
   } catch (OperationCanceledException) { break; }
   catch (Exception ex) {
     Log("--compress-separate: item failed: {0}", ex.Message);
     failedCount++;
     // update progress text showing failure, continue to next
   }
   \\\

8. Report final summary:
   \\\csharp
   if (failedCount > 0)
     progressWindow.SetComplete(L.TF(L.App_CompressSeparateComplete, succeededCount, failedCount));
   else
     progressWindow.SetComplete(L.T(L.App_CompressComplete));
   \\\

**Must NOT do**:
- Don't modify existing HandleCompress or HandleCompressQuick behavior
- Don't use async void in new handlers (follow existing patterns: Task.Run inside)
- Don't add WPF UI dependencies to the batch loop (keep progress updates via Dispatcher)

**Parallelization**:
- **Can Run In Parallel**: NO (depends on Task 1, 3)
- **Blocks**: None
- **Blocked By**: Task 1, Task 3

**References**:
- \src/MantisZip.UI/App.xaml.cs:966-1154\ — HandleCompressQuick as base pattern
- \src/MantisZip.UI/App.xaml.cs:189-293\ — HandleCompress IPC merge pattern
- \src/MantisZip.UI/App.xaml.cs:1006-1048\ — Conflict dialog pattern (CompressConflictDialog)

**QA Scenarios**:
\\\
Scenario: --compress-separate with single directory
  Tool: Bash
  Preconditions: Temp dir with files exists
  Steps:
    1. Create C:\temp\testdir\ with some files
    2. Run: MantisZip.UI.exe --compress-separate C:\temp\testdir
    3. Check C:\temp\testdir.zip exists
  Expected Result: Single archive created
  Evidence: .sisyphus/evidence/task-4-separate-single.txt

Scenario: --compress-separate with multiple directories
  Tool: Bash
  Steps:
    1. Create C:\temp\dir1\, C:\temp\dir2\ with files
    2. Run two instances simultaneously (simulating IPC):
       Start-Job { MantisZip.UI.exe --compress-separate C:\temp\dir1 }
       Start-Job { MantisZip.UI.exe --compress-separate C:\temp\dir2 }
       Wait 2s
    3. Check C:\temp\dir1.zip and C:\temp\dir2.zip exist
  Expected Result: Both archives created
  Evidence: .sisyphus/evidence/task-4-separate-multi.txt
\\\

**Commit**: NO (groups with Task 5)
- Message: later
- Files: \src/MantisZip.UI/App.xaml.cs\

---

## 5. Add --compress-combined CLI handler (IPC merge + common parent + name prompt)

**What to do**:
In \App.xaml.cs\:

1. Add \case "--compress-combined": HandleCompressCombined(e.Args.Skip(1).ToArray()); return;\ alongside other compress cases

2. Add \CompressCombinedMutexName\ / \CompressCombinedPipeName\ constants + \_compressCombinedPipeReady\ event

3. Add \HandleCompressCombined\ with same IPC merge pattern as HandleCompressSeparate (separate pipe name)

4. After collecting all paths → determine common parent directory + archive name:
   \\\csharp
   // 1. Get parent directory for each path
   // If path is a file: parent = Path.GetDirectoryName(path)
   // If path is a directory: parent = Path.GetDirectoryName(path.TrimEnd('\\', '/'))
   // 2. Find common prefix: take first parent, iterate, check if all others start with it
   // 3. If commonPrefix is root (e.g., "C:\" or "D:\"): prompt for name
   // 4. If no commonPrefix (different drives): prompt for name
   // 5. Archive name = Path.GetFileName(commonParent)
   // 6. Output = Path.Combine(commonParent, archiveName + extension)
   \\\

5. Determine common parent algorithm:
   \\\csharp
   private static string? FindCommonParent(List<string> paths) {
       if (paths.Count == 1) return Path.GetDirectoryName(paths[0].TrimEnd('\\', '/'));
       var parents = paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\', '/')) ?? "").ToList();
       if (parents.Any(string.IsNullOrEmpty)) return null;
       // Find longest common path prefix
       var first = parents[0];
       for (int i = 1; i < parents.Count; i++)
           while (!parents[i].StartsWith(first, StringComparison.OrdinalIgnoreCase)) {
               first = Path.GetDirectoryName(first);
               if (first == null) return null;
           }
       return first;
   }
   \\\

6. If common parent found:
   - \rchiveName = Path.GetFileName(commonParent)\
   - \outputPath = Path.Combine(commonParent, archiveName + extension)\
   - Standard compress flow with single ProgressWindow

7. If common parent NOT found (or is drive root like "C:\\"):
   - Show a simple input dialog to let user type a name (WPF window with TextBox + OK/Cancel)
   - Dialog title: L.T(L.App_CompressCombinedPromptTitle)
   - Label: L.T(L.App_CompressCombinedPromptLabel)
   - Default value: \"archive"\ (or first path's basename)
   - If user cancels → shutdown
   - If user enters name → \outputPath = Path.Combine(firstParent, name + extension)\
   - Show compress progress, then exit

8. Conflict handling: same as existing (CompressConflictDialog)

9. Location for output when no common parent: use the FIRST path's parent directory

**Must NOT do**:
- Don't modify existing compress patterns
- Don't add external dependencies for the name dialog (use simple WPF Window or MessageBox input)

**Parallelization**:
- **Can Run In Parallel**: NO (depends on Task 1, 3)
- **Blocks**: None
- **Blocked By**: Task 1, Task 3

**References**:
- \src/MantisZip.UI/App.xaml.cs:189-293\ — IPC merge pattern
- \src/MantisZip.UI/App.xaml.cs:966-1154\ — Compress flow

**QA Scenarios**:
\\\
Scenario: --compress-combined with items in same folder
  Tool: Bash
  Steps:
    1. Create C:\temp\mydata\dir1\ + C:\temp\mydata\dir2\
    2. Run: MantisZip.UI.exe --compress-combined C:\temp\mydata\dir1 C:\temp\mydata\dir2
    3. Check C:\temp\mydata\mydata.zip exists
  Expected Result: Combined archive created with both dirs inside
  Evidence: .sisyphus/evidence/task-5-combined-same.txt

Scenario: --compress-combined with cross-drive items prompts
  Tool: Bash (scripted)
  Steps:
    1. Run with items on C: and D: → should show prompt (script would auto-close)
    2. Verify exit behavior
  Expected Result: Name input dialog appears (or graceful exit)
  Evidence: .sisyphus/evidence/task-5-combined-cross.txt
\\\

**Commit**: YES (groups with Task 4)
- Message: \eat(cli): add --compress-separate and --compress-combined handlers\
- Files: \src/MantisZip.UI/App.xaml.cs\

---

## 6. Update ShellIntegration registration

**What to do**:
Update \ShellIntegration.cs\ to use the new constants and split the old QuickCompress item:

1. In \InstallCascadeFor\:
   - Replace existing "5. QuickCompress" block with two separate blocks:
     - \5. 压缩到独立的（文件名）\ → \{order:D2}_separate\ → \--compress-separate "%1"\ (guarded by \s.EnableCompressSeparate\)
     - \6. 压缩到（父目录名）\ → \{order:D2}_combined\ → \--compress-combined "%1"\ (guarded by \s.EnableCompressCombined\)
   - \7. 用MantisZip压缩\ → \{order:D2}_compress\ → \--compress "%1"\ (guarded by \s.EnableCompressMenu\, unchanged)
   - NOTE: the order values shift: old #5→#5+#6, old #6→#7

2. In \InstallVerbs\:
   - Replace QuickVerb with:
     \\\csharp
     if (s.EnableCompressSeparate)
         InstallVerb("*", CompressSeparateVerb, CompressSeparateDisplay, $@"""{exePath}"" --compress-separate ""%1""", s.ShowMenuIcons, exePath);
     if (s.EnableCompressCombined)
         InstallVerb("*", CompressCombinedVerb, CompressCombinedDisplay, $@"""{exePath}"" --compress-combined ""%1""", s.ShowMenuIcons, exePath);
     \\\
   - Keep CompressVerb unchanged (now renumbered to 07)
   - Also register CompressCombinedVerb for Directory target (combined compression also makes sense for directories)

3. In \Uninstall\:
   - Already handles old names via new constants
   - Add explicit cleanup for old \MantisZipQuick\ key (already present from earlier code!)

**Must NOT do**:
- Don't change the \InstallCascadeFor\ method signature
- Don't change other cascade entries (open, extract, etc.)

**Parallelization**:
- **Can Run In Parallel**: YES (with Tasks 4, 5, 7)
- **Blocks**: None
- **Blocked By**: Tasks 1, 3

**References**:
- \ShellIntegration.cs:135-240\ — InstallCascadeFor
- \ShellIntegration.cs:246-285\ — InstallVerbs
- \ShellIntegration.cs:86-123\ — Uninstall

**QA Scenarios**:
\\\
Scenario: New verbs registered after --install-shell
  Tool: Bash (reg query)
  Steps:
    1. Build and run --install-shell
    2. reg query HKCU\Software\Classes\*\shell\05_MantisZipCompressSeparate → exists
    3. reg query HKCU\Software\Classes\*\shell\06_MantisZipCompressCombined → exists
    4. reg query HKCU\Software\Classes\*\shell\07_MantisZipCompress → exists (old 06 renumbered)
    5. reg query HKCU\Software\Classes\*\shell\05_MantisZipQuick (should NOT exist)
  Expected Result: New verbs present, old QuickVerb absent
  Evidence: .sisyphus/evidence/task-6-verbs.txt
\\\

**Commit**: YES (groups with Task 3)
- Message: \eat(shell): register compress-separate and compress-combined verbs\
- Files: \src/MantisZip.UI/ShellIntegration.cs\

---

## 7. Update SettingsWindow checkbox

**What to do**:
1. In \SettingsWindow.xaml\, find the context menu section where \EnableQuickCheck\ currently exists
2. Replace \EnableQuickCheck\ with two new checkboxes:
   - \<CheckBox x:Name="EnableCompressSeparateCheck" Content="{l:L Settings_Menu_EnableCompressSeparate}" Margin="0,4"/>\
   - \<CheckBox x:Name="EnableCompressCombinedCheck" Content="{l:L Settings_Menu_EnableCompressCombined}" Margin="0,4"/>\

3. In \SettingsWindow.xaml.cs\:
   - Load: \EnableCompressSeparateCheck.IsChecked = s.EnableCompressSeparate;\
   - Load: \EnableCompressCombinedCheck.IsChecked = s.EnableCompressCombined;\
   - Save: \s.EnableCompressSeparate = EnableCompressSeparateCheck.IsChecked == true;\
   - Save: \s.EnableCompressCombined = EnableCompressCombinedCheck.IsChecked == true;\
   - Keep old EnableQuickCheck load/save lines (property still exists in AppSettings, just no longer used for anything)

**Must NOT do**:
- Don't delete EnableQuickCheck from XAML or code-behind — just leave it in the file (or remove if clean). Actually remove it since we're replacing.
- Don't redesign the settings layout

**Parallelization**:
- **Can Run In Parallel**: YES (with Tasks 4, 5, 6)
- **Blocks**: None
- **Blocked By**: Tasks 1, 2

**References**:
- \src/MantisZip.UI/SettingsWindow.xaml\ — Context menu section
- \src/MantisZip.UI/SettingsWindow.xaml.cs\ — Load/save methods

**QA Scenarios**:
\\\
Scenario: Build compiles and property binding is correct
  Tool: Bash (dotnet build + grep)
  Steps:
    1. dotnet build → verify 0 errors
    2. grep 'EnableCompressSeparate' SettingsWindow.xaml.cs → verify load/save
    3. grep 'EnableCompressCombined' SettingsWindow.xaml.cs → verify load/save
    4. grep 'EnableQuickCheck' SettingsWindow.xaml → should NOT exist (removed)
  Expected Result: Build clean, new checkboxes load/save correctly, old checkbox gone
  Evidence: .sisyphus/evidence/task-7-settings.txt
\\\

**Commit**: YES (groups with Task 2)
- Message: \eat(settings): replace EnableQuickCheck with separate+combined toggles\
- Files: \src/MantisZip.UI/SettingsWindow.xaml\, \src/MantisZip.UI/SettingsWindow.xaml.cs\

---

## 8. Final verification

**What to do**:
1. \dotnet build src\MantisZip.UI\MantisZip.UI.csproj\ → 0 errors, 0 warnings
2. \dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj\ → all pass
3. Check all new verbs registered correctly via registry
4. Quick sanity: verify old --compress still works

**Must NOT do**:
- Don't fix pre-existing issues unrelated to this feature

**Commit**: YES
- Message: \Final: build + test verification for split-compress feature\
- Files: (verification only, no code changes)

---

## Success Criteria

### Verification Commands
\\\ash
dotnet build src/MantisZip.UI/MantisZip.UI.csproj
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
\\\

### Final Checklist
- [ ] \Shell_CompressSeparate\ + \Shell_CompressCombined\ + progress/complete/prompt keys in zh/en/L.cs
- [ ] \EnableCompressSeparate\ + \EnableCompressCombined\ in AppSettings, both default true
- [ ] \--compress-separate\ works: individual archives for each path, sequential
- [ ] \--compress-combined\ works: combined archive with common parent name
- [ ] \--compress-combined\ cross-drive: shows name prompt dialog
- [ ] IPC merge works for both modes (multi-item selection)
- [ ] ProgressWindow shows sequential progress
- [ ] Failed items skipped, final count shown
- [ ] Old \ 5_MantisZipQuick\ verb key no longer created
- [ ] New verbs registered: \ 5_MantisZipCompressSeparate\, \ 6_MantisZipCompressCombined\, \ 7_MantisZipCompress\
- [ ] SettingsWindow checkboxes replace old \EnableQuickCheck\
- [ ] No regression: \--compress\ dialog mode unchanged
