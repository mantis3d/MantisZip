# QuickPathPicker 自包含路径速选控件 — 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Commits are small and frequent.
>
> 设计文档：`docs/superpowers/specs/2026-08-03-quickpath-picker-design.md`
> 分支：`AvaloniaAlpha` · 范围：`MantisZip.UI.Avalonia` + `MantisZip.Core`（仅 Localization 无改动）

## 目标

把三处重复的「路径输入框 + ⭐🕐🪟📁 + 单 Tab 浮层 + 手写 light-dismiss」抽成自包含可复用控件 `QuickPathPicker`，并让三家宿主接入。控件永远只收**目录**（浏览到文件自动取父目录），文件名交给其它控件。

## 关键复用点（务必先读这些现有代码）

- **`QuickPathControl`** (`src/.../Controls/QuickPathControl.axaml.cs`) — 现有四 Tab 速选面板。公开 API：`SingleTab` (PathTab?)，`ApplySingleTabMode()`，`SetCurrentPath(string)`，`RefreshSources()`，`SelectTab(PathTab)`。三单 Tab 模式：`SingleTab=favorites/value/Windows` + `ApplySingleTabMode()`。
- **`CustomFilePickerDialog`** (`src/.../Dialogs/CustomFilePickerDialog.axaml.cs`) — 静态入口 `ShowFolderAsync(owner, initialPath)`（默认浏览）、`ShowSaveFileAsync`、`ShowExtractFolderAsync`。`InitPathAutoComplete()`（L396）是现成的 AutoCompleteBox 补全逻辑样板。
- **三个 `PathTab` 值**：`Favorites`/`History`/`Windows`（`QuickPathControl.axaml.cs:427`）。
- **`FavoritePathManager`/`PathHistoryManager`**（Core）— 控件依赖的数据源，无需改。
- **现有快捷按钮 ToolTip keys**：`QuickPath_TabFavorites/History/Windows` 已在 `strings.zh-CN.json`/`strings.en.json`，**复用**。
- **主题资源键**（AGENTS 规则 4/5/7）：`ThemeSurfaceBgBrush`、`ThemeTextPrimaryBrush`、`ThemeBorderBrush`、`ThemeButtonBgBrush/Hover/Pressed`、`ControlHeightSm`。空间键 `SpacingXxsThk` 等。

## 文件结构

**新增**：
- `src/MantisZip.UI.Avalonia/Controls/QuickPathPicker.axaml` — 控件 XAML（两行布局）
- `src/MantisZip.UI.Avalonia/Controls/QuickPathPicker.axaml.cs` — 控件逻辑（Path 双向绑定、BrowseAction、light-dismiss、三浮层）
- `tests/MantisZip.UI.Avalonia.Tests/QuickPathPickerDirectoryNormalizationTests.cs` — 目录归一化纯函数单测

**修改**：
- `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` — 加 `QuickPath_Browse`
- `src/MantisZip.UI.Avalonia/Localization/strings.en.json` — 加 `QuickPath_Browse`
- `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` + `.axaml.cs` — 替换自定义路径区为 `<QuickPathPicker>`
- `src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml` + `.axaml.cs` — 替换手写快捷区为 `<QuickPathPicker>`（注入 SaveFile 浏览）
- `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml` + `.axaml.cs` — 替换手写快捷区为 `<QuickPathPicker>`（注入 ExtractFolder 浏览）
- `docs/PLAN.md`（已加，无需再改）、`docs/PROGRESS.md`（commit 前更新）

## 任务分解（TDD，每个任务独立可提交）

### 任务 1 — 目录归一化纯函数（TDD 先测后实现）

- [ ] 写失败测试：`QuickPathPickerDirectoryNormalizationTests.cs` 测 `QuickPathPicker.CoerceToDirectory(string)`：
  - 目录路径 `C:\foo` → 原样
  - 文件路径 `C:\foo\bar.txt` → `C:\foo`
  - 非法串 `xyz` → 原样透传
  - 根路径 `C:\` → 原样
  - null/空 → 透传（或空串，按现定）
- [ ] 运行测试确认失败
- [ ] 实现静态方法 `internal static string CoerceToDirectory(string picked)`（`Directory.Exists`→原样；`File.Exists`→`Path.GetDirectoryName`；null 安全；否则原样）
- [ ] 运行测试通过
- [ ] commit: `feat(QuickPathPicker): add directory coercion helper`

### 任务 2 — 本地化 key

- [ ] `strings.zh-CN.json` 加 `"QuickPath_Browse": "📁 浏览…"`
- [ ] `strings.en.json` 加 `"QuickPath_Browse": "📁 Browse…"`
- [ ] lsp 无错；不提交（随控件一起）

### 任务 3 — 控件 XAML + code-behind 骨架

- [ ] `QuickPathPicker.axaml`：两行 Grid
  - Row 0：`AutoCompleteBox x:Name="PathInput"`
  - Row 1：`Grid ColumnDefinitions="Auto,Auto,Auto,*,Auto"`；`QuickFavButton`/`QuickHistButton`/`QuickWinButton`（复用 icon + ToolTip keys）+ 分隔 + `QuickBrowseButton`
- [ ] `QuickPathPicker.axaml.cs`：
  - `Path` StyledProperty（TwoWay，`OnPathChanged` → 同步 `PathPicker.Text` 与 `RefreshSources`/`SetCurrentPath`）
  - 构造：加载 3 个 `QuickPathControl`（SingleTab+ApplySingleTabMode），3 个 Popup 包住（Placement、Width 320/MaxHeight 420、Border），`AddHandler(PointerPressed, Tunnel→CloseAllPopups)`
  - `BrowseAction` 属性（`Func<Window?, string?, Task<string?>>?`）默认 null → `ShowFolderAsync`
  - 三个按钮 Click → 开对应 Popup（先关其它）
  - 📁 Click → `OnBrowseAsync()`（注入/null 分支 + `CoerceToDirectory` 回填 Path）
  - QuickPathItem 选中事件 → 写 Path + 关浮层
- [ ] `lsp_diagnostics` 干净
- [ ] build `dotnet build src\...\MantisZip.UI.Avalonia.csproj`
- [ ] **commit** `feat(QuickPathPicker): add self-contained reusable path picker`

（此任务较大；推荐拆子步骤逐个验证再 commit，见下方细化）

### 任务 4 — SettingsWindow 接入（默认浏览 = 零配置）

- [ ] `settings.axaml` 找到「默认路径优先级」手动路径 TextBox+旧📁，替换为 `<controls:QuickPathPicker x:Name="CustomPathPicker" Path="{Binding CustomPath}" />`
- [ ] 移除旧 single-panel + 关联 handled code（若存在于 SettingsWindow.axaml.cs）
- [ ] 删除为此临时加的 `DefaultPathQuickTip` VM 属性（若仅被旧 QuickPath 用，verify 后删）
- [ ] lsp 干净 + `dotnet build`
- [ ] **commit** `refactor(settings): use QuickPathPicker for custom default path`

### 任务 5 — CompressSettingsWindow 接入（注入 SaveFile 浏览）

- [ ] Code-behind 里把现有 `DestFav/Hist/Win` Popup+`AddHandler`+三个 QuickPathControl 拆掉
- [ ] 加一个 `QuickPathPicker` 实例（或 XAML），`Path` 绑定/赋值 `ViewModel.OutputDirectory`，`BrowseAction = (owner, cur) =>` 回调原 SaveFile（`LocalizedStrings[QuickPath_SelectSaveFolder]`）逻辑后返回目录
- [ ] 移除 `QuickFavButton_Click/QuickBrowseButton_Click/CloseDestPopups` 等被替代 handler
- [ ] lsp + build
- [ ] commit `refactor(compress): use QuickPathPicker for output dir`

### 任务 6 — ExtractSettingsWindow 接入（注入 ExtractFolder 浏览）

- [ ] 移除现有 `DestFav/DestHist/DestWin` Popup+`AddHandler`+三控件+handler
- [ ] 用 `QuickPathPicker` 替换解压目标输入区，`Path` bind `DestinationPath`，`BrowseAction= (owner,cur) => ShowExtractFolderAsync(owner,_entries,cur)`
- [ ] lsp + build
- [ ] commit `refactor(extract): use QuickPathPicker for destination path`

### 任务 7 — 全量验收 + 回归

- [ ] `dotnet build src\MantisZip.UI.Avalonia.csproj`（确保运行实例已 kill，避免文件锁）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests`（含新 CoerceToDirectory 测试；旧 VM 测试不应破坏）
- [ ] 手动验收三家窗口：⭐🕐🪟 浮层开关、light-dismiss、选中写回、📁/浏览目录归一化、AutoComplete 提示
- [ ] 更新 `docs/PROGRESS.md`（三轨制 Avalonia 2026-08-03 条目，追加快捷速选控件）
- [ ] commit（如需要）

### 任务 8 — 收尾

- [ ] 确认 `git status` 干净、无遗留临时文件
- [ ] 汇总改动给用户，标注手动验收项待他确认

## 测试策略

- 纯逻辑（`CoerceToDirectory`）— xunit 单测（tests 项目已引用 `MantisZip.U.Avalonia`）
- 控件集成（浮层/light-dismiss/binding）— **手动验收**（项目现状无 headless UI 测试基建；VM 测试已覆盖 UI 无关逻辑）

## 风险 / 注意事项

- **构建锁**：运行中的 `MantisZip.UI.Avalonia` 会锁 Core/Shell DLL → build 误报 MSB3021/3027。测试前 `Stop-Process -Id <PID> -Force`。
- **任务 3 较大**：若明确想更小粒度，可拆为「3a：XAML+控件壳」→「3b：Browse prego」（默认浏览）+「3c：五按钮+light-dismiss」三个子 commit。
- **命名**：控件内部 Popup 用 `x:Name` 引用；`PathList` 选中 handler 需转成 QuickPath 控件事件。
- **不新增依赖**；不迁移 WPF/不动 QuickViewControl 四 Tab。

## 验收标准

- 三家宿主都能看到 ⭐🕐🪟 浮层 + 📁 浏览 + 输入框 AutoComplete
- 浏览选到文件时，输入框显示父目录
- 手输目录 + Enter 接受
- 三处重复代码删除，控件面板一行集成