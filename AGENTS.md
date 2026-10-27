# MantisZip — Agent Guide

> 本文件只放**每次 session 必读**的硬性规则与速查。架构细节、契约、历史踩坑、拖拽/预览/Shell 实现见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## Project overview

基于 Avalonia 的跨平台压缩/解压桌面应用。

| 项目 | 框架 | 状态 |
|------|------|------|
| `MantisZip.UI.Avalonia` | Avalonia (`net10.0`) | 🟢 主力开发 |

两个共享库：`MantisZip.Core` (class library) + `MantisZip.ShellExt` (COM 组件 class library)。WPF 版本已在迁移完成后删除。

## Quick start

```powershell
# 一键自检（版本号 6 处 + 三语 key + 规则守卫，不构建，毫秒级）
pwsh -File scripts\verify-repo.ps1 -Task check

# 构建 Avalonia 版（主力）
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# 运行（Windows）
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# Tests
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

- 产物：`src/MantisZip.UI.Avalonia/bin/Debug/net10.0/MantisZip.UI.Avalonia.exe`；`bin/`、`obj/` 已 gitignore
- CI：`.github/workflows/ci.yml`（Windows，先跑 `version` / `i18n` / `guards` 三项静态自检，再「依赖漏洞审计 restore → 产品代码分析器门禁 → build → test」，但 `--filter "FullyQualifiedName!~SevenZipEngine"` 把 7z 相关用例过滤掉了）；**无 pre-commit**；`tests/` 有 Core 与无头 Avalonia 两个测试项目（非全量覆盖）；**CI 红了先读 [docs/MAINTENANCE.md](docs/MAINTENANCE.md) §5.1 的判定流程**（取作业日志的配方 + 已知上游 flake 的判据与处置，别从零摸索）
- 漏洞审计与代码风格：根目录 `Directory.Build.props` 开 `NuGetAuditMode=all`（本地只告警，CI 把 NU1901~NU1904 升为错误，分号要写 `%3B`）；`.editorconfig` 提供风格基线，CI 只对 `src/` 跑 `dotnet format analyzers --verify-no-changes --severity warn`（**whitespace/style 暂不设门禁**——存量 700+/40 处待重排）；`.gitattributes` 锁定行尾（索引 LF / 工作区 CRLF，二进制不转换），新文件不必手工调行尾
- **分支角色**（2026-10-22 明确）：`alpha` = **新功能分支**（日常新功能开发）；`beta` = **调试除错分支**（缺陷复现 / 修错 / 验证）；`main` = **发版分支**（只在发版合并时前进）；`plans` = **计划分支**（只写计划与文档，不改产品代码——见规则 19）
- CI 触发分支：`main` + `alpha` + `beta`（三条都要过门禁；新功能在 `alpha`、除错在 `beta`，CI 配置与本节必须同步）；`plans` 不在 CI 触发列表内（不产生产品代码，无需门禁）
- ShellExt COM 已注册时 Explorer 会锁 Debug DLL → 构建/测试用 `-c Release` 绕开

## 维护脚本（唯一实现）

`scripts/verify-repo.ps1` 是仓库自检逻辑的**唯一实现**——DSH 插件工具 `mantiszip_*`、未来的 CI 步骤、人工 shell 全部调它，改逻辑只改这一处：

| 命令 | 作用 |
|------|------|
| `pwsh -File scripts\verify-repo.ps1 -Task check` | 版本号 6 处 + 三语 key + 规则守卫，无构建 |
| `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia` | 构建并把 MSBuild 输出压成「文件:行:列 + 错误码」（项目级诊断无行列号时只显示 csproj 路径，如 restore/NU1301 失败） |
| `pwsh -File scripts\verify-repo.ps1 -Task test -Target ui-tests` | 跑测试 |
| `pwsh -File scripts\verify-repo.ps1 -Task i18n` / `-Task version` | 单项检查 |
| `pwsh -File scripts\verify-repo.ps1 -Task guards` | 规则守卫：快捷键登记（规则 10.1）/ i18n 绑定登记（规则 13）/ 文件选择器（规则 16）/ 图标登记（规则 8）/ 意图-执行接缝（规则 20） |
| 追加 `-Json` | 机器可读输出（插件内部用） |

- `-Target`：`avalonia`（默认）/ `core` / `shell` / `tests` / `ui-tests` / `solution`
- `-ExtraArgs <args>` 追加 dotnet 参数（如 `--no-restore`）；参数内含逗号/空格时用 `-ExtraArgsJson '["-c","Release"]'`
- **退出码 0 = 通过，1 = 有问题** ⇒ CI 可直接用；`-Json` 时 `ok` 字段同义
- `-Task test` 有一条**兜底**：`dotnet test` 失败且没有解析到任何编译诊断（= 测试根本没跑起来，如 VSTest testhost 在受限环境里无法打开父进程句柄）时，自动改用 `dotnet exec <测试程序集>` 直跑 xunit v3 的 MTP 入口，绕开 testhost；JSON 里 `runner` 为 `mtp`、`fallbackFrom` 为 `vstest`。编译错误或真实用例失败**不会**触发兜底
- agent 面板里对应工具名：`mantiszip_verify` / `mantiszip_i18n` / `mantiszip_version` / `mantiszip_guards`（DSH 本地插件 `tools/dsh-plugin-mantiszip`，薄壳而已）
- **换机器 / 换 agent 接手，或要改这套门禁时，先读 [docs/MAINTENANCE.md](docs/MAINTENANCE.md)**：组成图、每个 task 干什么、五条守卫的覆盖边界、CI 七步门禁、反直觉实现（删掉会复发的写法）、环境坑、有意留白

## Architecture（速查）

```
MantisZip.Core ──────┬── MantisZip.UI.Avalonia ──reference── MantisZip.ShellExt (COM)
(引擎/解析)          │   (Avalonia MVVM)                     (Explorer.exe 宿主)
```

- **引擎**：`IArchiveEngine`（List/Extract/Compress/Test）+ `ArchiveEngineFactory` 按扩展名分发；`ZipEngine` / `SevenZipEngine` / `TarGzEngine` 全在 Core，与 UI 框架无关
- **UI**：`CommunityToolkit.Mvvm`（`[ObservableProperty]` / `[RelayCommand]`）；ViewModels + Services 分层；对话框经 VM 回调委托与 View 解耦
- **路径契约**：预览树与实际解压共用 `ExtractPathResolver`（单一事实来源），路径逻辑不得另写
- **派生属性通知**：多个计算属性依赖同一组源属性时，**禁止**逐字段 `[NotifyPropertyChangedFor]`，用 `partial void OnXxxChanged` → 集中 `NotifyXxxProperties()`

其余（依赖流向、引擎契约、进度上报、并行解压、ZIP 加速、目录聚合、ExtractPathResolver、SmartOpenPathResolver、设置系统、预览子系统、Shell/COM、CLI 路由表、图标服务、开发约束与历史踩坑、拖拽、日志隐私）见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## Version bump checklist

When releasing a new version, update the version string in ALL of these locations:

| # | File | Content |
|---|------|---------|
| 1 | `src/MantisZip.UI.Avalonia/AppConstants.cs` | `public const string Version = "x.y.z"` |
| 2 | `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` | `<Version>x.y.z</Version>` |
| 3 | `docs/PLAN.md` | `**当前版本**: x.y.z` |
| 4 | `docs/PROGRESS.md` | `**当前版本**: x.y.z` |
| 5 | `installer.iss` | `#define MyAppVersion "x.y.z"`（兜底默认值） |
| 6 | `installer-selfcontained.iss` | `#define MyAppVersion "x.y.z"`（兜底默认值） |

- 两个 `.iss` 的版本由 release workflow 从 git tag 经 `/dMyAppVersion=` 传入，`#define` 仅作本地手工编译兜底；**仍建议同步**，否则静默打出错误版本号且无编译报错
- 改完 6 处必须核对：`pwsh -File scripts\verify-repo.ps1 -Task version`（agent 面板里是 `mantiszip_version`）；脚本不可用时逐处人工比对

## 每次 session 自动执行规则（必需）

以下规则对所有 session 生效，无需用户每次提醒：

### 规则 0：描述不清时必须追问

如果我的需求描述不清晰、有歧义、或者缺少关键信息，**必须**停下来向我提问澄清，直到完全确定后再执行。绝对不能猜测、假设或用「合理默认值」擅自推进。

如果用户是以疑问的方式描述某个问题，则先找到问题所在和解决方案后，必须先与用户沟通，待用户完全确定问题与解决方案后，再执行。

回复用户的文本必须是中文，除非是引用的其他语言。

### 规则 1：Plan 变更同步

新增计划文本需要放在 `.omo/plans/未开始` 内，格式与同目录计划相同。
每当新增或修改 `.omo/plans/` 内的计划文件时，**必须同步更新** `docs/PLAN.md`：

- 新增计划 → 在 PLAN.md 对应优先级区域（P2/P3/待实现）添加一行 `| 任务 | 说明 |` 引用新计划，保持与已存在行格式一致
- 修改计划 → 更新 PLAN.md 中对应任务的说明、优先级或状态
- 执行计划 → 若计划文档不在 `.omo\plans\进行中` 内，先将对应计划移入 `.omo\plans\进行中` 目录再执行
- 计划完成 → 将条目从 PLAN.md 移至 PROGRESS.md 的【历史设计方案索引】章节；先将对应计划移入 `.omo\plans\已完成` 中

### 规则 2：版本号变更

- 只有在用户明确说明变更版本号时才会变更，不要未经用户允许擅自变更版本号。如果你觉得应该变更版本号，需要向用户询问。
- 当变更版本号时，需遵循 Version bump checklist 的部分全部更新。

### 规则 3：提交前更新进度文档（里程碑 + 细节双轨）

在每次执行 `git commit` 之前，**必须先更新**进度文档：

- **里程碑**（`docs/PROGRESS.md`）— 仅记录新功能上线、架构级变更、重大 bug 修复：
  - Avalonia 版（`### MantisZip.UI.Avalonia（主力版）`）按月分组（`#### 2026-08`），条目格式 `- **08-19** — 标题`（同月从新到旧）
  - 共享层（`### 共享层（Core / ShellExt / 构建）`）按版本分组（`#### v0.5.0`），条目格式同上
- **细节**（`docs/progress-avalonia-detail.md`）— 逐条详细变更（小 bugfix / i18n / 样式 / 测试 / 计划类）：
  - Avalonia 以日期为标识（`**2026-07-16**`）、共享层以版本号为标识（`#### v0.x.x (2026-07-16)`）
  - 同一日期多条按从晚到早排列（最新的在最上方）
- 一次变更若属里程碑级，PROGRESS 加一行 + 细节文档同步追加；涉及多个领域（如 Core）在各对应文档各加一条；属于已有规划任务的标注进度

### 规则 4：新 UI 控件必须应用主题样式

新增任何 UI 控件，**必须显式设置主题样式键**，禁止系统默认颜色：

- `Background` → `ThemeSurfaceBgBrush`（或对应语义色）；`Foreground` → `ThemeTextPrimaryBrush` / `ThemeTextSecondaryBrush`；`BorderBrush` → `ThemeBorderBrush` / `ThemeBorderLightBrush`；按钮 → `ThemeButtonBgBrush` / `ThemeButtonHoverBrush` / `ThemeButtonPressedBrush`
- **Avalonia 资源键名均以 `Brush` 结尾**（`ThemeWindowBgBrush`，不是 `Theme_WindowBg`）；新控件类型无对应资源时在 `ThemeLight.axaml` + `ThemeDark.axaml` **成对添加**
- **优先挂全局 Classes**（定义于 `App.axaml`，勿重复设置尺寸/圆角/边框）：

| 类名 | 适用控件 | 效果 |
|------|---------|------|
| `ToolbarButton` | `Button` / `ToggleButton` | 高 `ControlHeightLg`、Padding `8,2`、背景 `ThemeHeaderBgBrush` |
| `ToolbarIcon` | `Button` / `ToggleButton` | 方形紧凑（`ControlHeightSm` / Padding 6），常组合 `Classes="ToolbarButton ToolbarIcon"` |
| `ToolbarButtonIcon` | `TextBlock` | FontSize 20、水平居中（按钮图标字符） |
| `ToolbarButtonLabel` | `TextBlock` | FontSize 13、水平居中（按钮文字） |
| `compactTab` | `TabItem` | Padding `8,6`、MinHeight 36、FontSize 18 |
| `ToggleIconBox` | `Border` | 16×16、圆角 3、边框 1.5、背景过渡（菜单切换指示） |

- **`PathIcon` 的 `Foreground` 必须显式给**：Avalonia 12 的 `IconElement.ForegroundProperty` 元数据是 `Inherits = true`，但实测工具栏 / 小按钮里的 `PathIcon` 仍取不到父控件的语义画刷（深色主题下呈默认黑、几乎不可见），所以一律按「显式设置」执行。**统一入口**：工具栏类图标挂 `Classes="ToolbarButtonIcon"`，命中 `App.axaml` 的 `PathIcon.ToolbarButtonIcon` 样式；该样式**必须写在 `ToggleButton:checked PathIcon` 之前**（Avalonia 无 CSS specificity、同名属性按定义顺序后者覆盖，放后面会把选中态的白图标覆盖成 Primary）。全局 `TextBlock` 样式**故意不设 Foreground**（继承父控件避免 emoji 压成单色），除非有明确理由不要显式设置
- 不设置固定 `Height` 除非有充分理由；`ToggleIconBox` / 菜单图标槽位的详细用法见 ARCHITECTURE

### 规则 5：紧凑度模式资源键名约定

紧凑度模式三档间距/控件高度均通过 `{DynamicResource}` 引用。**注意 double 与 Thickness 的类型差异**：

| 用途 | 资源键名 | 类型 | 适用属性 |
|------|---------|------|---------|
| 间距（数字） | `SpacingXxs` / `Xs` / `Sm` / `Md` / `Lg` / `Xl` | `double` | `Spacing` |
| 间距（厚度） | `SpacingXxsThk` / `XsThk` / `SmThk` / `MdThk` / `LgThk` / `XlThk` | `Thickness` | `Margin` / `Padding` |
| 控件高度 | `ControlHeightSm` / `ControlHeight` / `ControlHeightMd` / `ControlHeightLg` / `ControlHeightXl` / `ControlHeightXxl` | `double` | `Height` / `MinHeight` / `MaxHeight` |
| 圆角 | `BorderRadius` | `CornerRadius` | `CornerRadius` |
| 对话框内边距 | `DialogPadding` | `Thickness` | `Padding`（对话框级） |

- `Margin`/`Padding` **必须用** `SpacingXxxThk` 后缀变体（`DynamicResource` 无法隐式 double→Thickness）；`Spacing` 用无后缀版本
- 禁止硬编码间距/高度/圆角；全部由 `App.axaml.cs` 的 `ApplyCompactness()` 注入，运行时切换无需重启

### 规则 6：开关控制面板区域时统一隐藏（方案 A）

用一个 CheckBox/开关控制一组控件（过滤条件、加密选项等）可用性时，**统一用隐藏（`IsVisible`）而非禁用（`IsEnabled`）**：关闭 → 整块内容从视觉树移除、不占布局；打开 → 正常显示。**例外**需在评审时说明理由。

实现：把受控内容包进命名容器（`StackPanel`/`Border`），在开关事件里切 `IsVisible`。参考：`Controls/FileFilterEditor.axaml`（`FilterContentPanel` + `SyncControlStates()`）、`Dialogs/CompressSettingsWindow.axaml`（`IsVisible="{Binding Encrypt}"`）。逐个 `IsEnabled = false` 的方案 B 已废弃。

### 规则 7：列表/树形/表格控件必须使用紧凑度感知的行高

新增 `ListBox` / `DataGrid` / `TreeView` / `ItemsControl` 等列表类控件时，**必须**设置行高/项最小高度为紧凑度资源键，禁止固定数值：

| 控件类型 | 属性 | 资源键 | 三档值 |
|---------|------|-------|-------|
| `ListBox` | `ItemContainerTheme` → `Setter MinHeight` | `ControlHeightMd` | 28/32/38 |
| `DataGrid` | `RowHeight` | `ControlHeightMd` | 28/32/38 |
| `TreeView` | `Style TreeViewItem` → `Setter MinHeight` | `ControlHeightSm` | 22/26/30 |
| `ItemsControl` | 项模板最外层容器 `MinHeight` | `ControlHeightSm` | 22/26/30 |

```xml
<ListBox ItemsSource="{Binding ...}">
  <ListBox.ItemContainerTheme>
    <ControlTheme TargetType="ListBoxItem">
      <Setter Property="MinHeight" Value="{DynamicResource ControlHeightMd}" />
    </ControlTheme>
  </ListBox.ItemContainerTheme>
</ListBox>
```

**例外**：非数据行类列表（WrapPanel 标签云、图标画廊等）豁免。其余控件示例见 ARCHITECTURE。

### 规则 8：新增图标必须注册到 IconTestViewModel

在 `src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml` 添加新的 `Geometry` 图标资源后，**必须同步**在 `src/MantisZip.UI.Avalonia/ViewModels/IconTestViewModel.cs` 的 `LoadAllIcons()` 中添加对应 `Add()` 调用，否则图标测试窗口不显示该图标。

### 规则 9：Windows 环境下禁止 Unix 风格 shell 命令

开发环境为 **Windows + PowerShell 7**（pwsh）。所有 shell 命令必须用 PowerShell 语法，禁止 `grep`/`cat`/`ls`/`rm`/`cp`/`mv`/`which` 等 Unix 风格命令。

- **搜索优先用内置工具**（`grep`/`glob`/`ast_grep_search`/`codegraph`），跨平台且不依赖 shell
- 必须用 shell 时，用 PowerShell 原生 cmdlet 或已安装的 `rg`：

| Unix 习惯 | Windows 正确写法 |
|-----------|-----------------|
| `grep "xxx" dir` | `rg "xxx" dir` 或 `Select-String -Path ... -Pattern "xxx"` |
| `ls` / `ls -la` | `Get-ChildItem` / `Get-ChildItem -Force` |
| `cat file` | `Get-Content file` |
| `which cmd` | `Get-Command cmd` |
| `rm -rf dir` | `Remove-Item -Recurse -Force dir` |
| `cp a b` | `Copy-Item a b` |
| `mv a b` | `Move-Item a b` |
| `head` / `tail` | `Get-Content -Head 5` / `-Tail 5` |

- 不确定命令是否存在时先 `Get-Command` 验证

### 规则 10：提交信息必须使用 conventional commits 风格

`<type>(<scope>): 中文描述`，`type` 用 `feat`/`fix`/`docs`/`refactor`/`test`/`chore` 等，`scope` 标明影响项目（`core`/`avalonia`/`shell` 等，可组合）。

示例：
- `feat(avalonia): 文件选择器地址栏新增收藏当前路径入口`
- `fix(core,avalonia): 拖拽/右键解压流程统一`
- `docs: AGENTS.md 新增 ComputeDirectoryStats 目录聚合契约说明`

### 规则 10.1：菜单快捷键的唯一事实源是 `MainWindow.KeyBindings`

Avalonia 的 `MenuItem.InputGesture` **只显示**快捷键文字、**不处理按键**（官方文档：Setting this property does not cause the input gesture to be handled by the menu item, it simply displays the gesture text in the menu）。真正生效的绑定必须登记在 `MainWindow.KeyBindings`（Avalonia 12 的 API 名是 `KeyBindings`；11 版的 `InputBindings` 已移除）。

- **登记处**：`Views/MainWindow.axaml.cs` 的 `RegisterKeyBindings(MainWindowViewModel vm)` —— 用**命令实例直绑**（`KeyBinding` 不是 Control，拿不到 DataContext）
- **显示处**：对应 `MenuItem` 上写同样的 `InputGesture="Ctrl+…"`（仅供菜单里显示）
- **新增快捷键三步**：① `RegisterKeyBindings` 登记（生效）② `MenuItem.InputGesture` 写同样文字（显示）③ 更新 `tests/MantisZip.UI.Avalonia.Tests/MainWindowKeyBindingsTests.cs` 的期望清单
- **不要**在 `MenuItem` 上单独挂 `InputGesture` 就以为快捷键生效了（2026-10-21 修复：5 个原有快捷键一直只显示不生效）

### 规则 11：新功能只在 Avalonia 开发

WPF 版本已在迁移完成后删除。所有新功能仅在 `MantisZip.UI.Avalonia` 中实现。共享层（`MantisZip.Core`/`MantisZip.ShellExt`）改动需评估兼容性。

### 规则 12：修改后必须构建验证

完成任何代码修改后，必须运行对应项目的构建验证（测试项目改动另跑测试）。**首选维护脚本**（唯一实现，CI/插件/人工共用）：

```powershell
pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia
pwsh -File scripts\verify-repo.ps1 -Task build -Target core        # 仅当修改 Core
pwsh -File scripts\verify-repo.ps1 -Task test  -Target tests
pwsh -File scripts\verify-repo.ps1 -Task test  -Target ui-tests
```

它把 MSBuild 输出压成「文件:行:列 + 错误码」，退出码 0 = 通过、1 = 有问题。agent 面板里等价工具是 `mantiszip_verify`（`target`：avalonia/core/shell/tests/ui-tests/solution；`action`：build/test），内部同样调这个脚本。

脚本不可用时回退到原始命令，判定标准不变：

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet build src\MantisZip.Core\MantisZip.Core.csproj            # 仅当修改 Core
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

只有构建通过（`ok=true` 或 exit 0）任务才算完成。

### 规则 13：新增 UI 文案必须走本地化（禁止硬编码）

新增任何用户可见字符串（中文或英文），**禁止**在 `.cs` / `.axaml` 中写死：

- **C#**：`LocalizationManager.T("Key")`；占位符用 `T("Key", arg1, arg2)`（`{0}` 格式）
- **XAML 静态文案**：绑定 VM 属性或 `LocalizedStrings[Key]`（字典索引器绑定需 VM 实现 `LocalizedStrings` 并在 `OnCultureChanged` 刷新）
- **Window/UserControl code-behind（DataContext=self）**：暴露 `public string XxxText => LocalizationManager.T("Key")` 并绑定，同时加 `x:CompileBindings="False"`
- **新增 key 必须成对添加**到 `strings.zh-CN.json`、`strings.en.json`、`strings.zh-TW.json`（三语 key 集必须完全同步，`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 校验）；插入到文件头 `{` 之后（不排序），保持 **UTF-8 with BOM** + CRLF + 2 空格缩进（三份文件现状即带 BOM，`.editorconfig` 的 `[*.json] charset = utf-8-bom` 为准；改成无 BOM 会与既有文件不一致）
- **XAML `LocalizedStrings[Key]` 绑定的 key 必须登记**到 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组（约 line 203 起的 `new[] { ... }`）。该字典**不是全量加载** JSON——漏登记会导致菜单/工具栏文字空白（图标仍在）且构建不报错
- 完成后 `dotnet build` 无新增错误，并扫描确认无遗漏硬编码
- **改完文案必须跑** `pwsh -File scripts\verify-repo.ps1 -Task i18n`（agent 面板里是 `mantiszip_i18n`）：校验三语 key 集合、`{0}` 占位符、重复 key、空值。工具不可用时手工比对三份文件——现有测试只校验解析后的 key 集合，**测不出重复 key**（JSON 解析会折叠）

**豁免**（须在代码注释中注明原因）：开发者诊断工具、启发式匹配逻辑（`message.Contains("密码")` 等）、内容数据与语言本族原生名、`App.DebugLog` 日志与仅作控制流用的异常消息。

### 规则 14：新增 AXAML 控件必须添加中文注释

新增任何 `.axaml` 文件（Controls/、Dialogs/、Views/ 下）时，**必须为主要控件/区域加中文 `<!-- -->` 注释**，说明用途/功能/关键属性；覆盖：根容器与主布局、每个功能分区、数据绑定控件、转换器、覆层/弹窗、工具栏按钮、菜单项。完成后 `dotnet build` 验证。注释示例见 ARCHITECTURE。

### 规则 15：CoreLog 初始化必须走 Initialize() 方法

Core 层 `CoreLog` 的诊断开关与脱敏委托**必须**经 `CoreLog.Initialize()` 统一注入，**禁止**直接设置 `CoreLog.DiagnosticsEnabled` 或 `CoreLog.RedactOverride`：

```csharp
// ✅
CoreLog.Initialize(appSettings.EnableDebugLogging, appSettings.LogPrivacyMode);

// ❌ 分散且容易遗漏脱敏委托
CoreLog.DiagnosticsEnabled = appSettings.EnableDebugLogging;
CoreLog.RedactOverride = msg => LogRedactor.RedactPaths(msg, ...);
```

注入时机两处：`OnFrameworkInitializationCompleted`（启动首次）+ `RefreshDebugLogSettings`（设置保存后）。原因：Core 无法引用 AppSettings（依赖倒置），绕过 `Initialize()` 会让 `CoreLog` 日志不脱敏、完整路径泄露到 debug.log。

### 规则 16：文件/目录选择必须使用 CustomFilePickerDialog（禁用原生 StorageProvider）

**核心禁令**：任何文件/目录选择入口，**禁止**使用 Avalonia 原生 `StorageProvider` 对话框。

被禁用 API（经 `TopLevel.GetTopLevel(x).StorageProvider`）：`OpenFilePickerAsync`、`SaveFilePickerAsync`、`OpenFolderPickerAsync`、`FilePicker*Options`、`FilePickerFileType`、`IStorageFile`/`IStorageItem` + `TryGetLocalPath()`。

> 唯一豁免：`CustomFilePickerDialog.SystemBrowse_Click` 内的原生调用（用户主动点「系统浏览」的降级逃生通道）。**不得新增其他豁免点。**

模式选择表（`CustomFilePickerDialog` 静态入口）：

| 场景 | 入口 |
|------|------|
| 选文件夹 | `ShowFolderAsync(owner, initialPath)` |
| 保存单个文件 | `ShowSaveFileAsync(owner, initialPath, defaultExtension, fileTypes, suggestedFileName)` |
| 打开单个文件 | `ShowOpenFileAsync(owner, initialPath, fileExtensions, fileTypes)` |
| 选文件**或**文件夹（多选累积） | `ShowOpenItemsAsync(owner, initialPath)` → `IReadOnlyList<string>?` |
| **解压目标目录**（带路径 + 冲突预览） | `ShowExtractFolderAsync(owner, entries, initialPath, currentFolder, preserveFullPath)` |

- **解压场景首选 `ShowExtractFolderAsync`**，且**必须实参 `currentFolder` + `preserveFullPath`**（取自 `MainWindowViewModel.CurrentFolder` 与 `AppSettings.ExtractPreserveFullPath`）——漏传会导致预览树与实际解压路径不一致
- 限定扩展名用 `fileTypes: [new FileTypeOption(标签, 模式数组)]`；`Patterns` 空数组 = 不过滤；支持 `"*.json"` / `".json"` / `"json"` 三种写法；**不要**改 `MatchesFileFilter` 的 `SelectedIndex` 硬编码
- 文件类型标签走 `LocalizationManager.T()`（规则 13）
- `initialPath` 传上下文目录（压缩包所在目录等）；`ResolveInitialPath` 按 `AppSettings.DefaultPathOrder` 解析，目标目录不存在安全（回退到最近存在父目录）
- 表里没有合适模式 → 先扩展 `CustomFilePickerDialog`，不得退回原生对话框

新增/修改后自查：

```powershell
# 期望：仅 CustomFilePickerDialog.axaml.cs 内 SystemBrowse_Click 的 3 处命中
git grep -n -E "OpenFilePickerAsync|SaveFilePickerAsync|OpenFolderPickerAsync" -- 'src/*.cs'
```

### 规则 17：帮助/介绍弹窗必须复用 HelpDialog 外壳

**核心禁令**：任何用户可见帮助/介绍弹窗**必须**复用 `Dialogs/HelpDialog.axaml`，**禁止**另建一次性 `Window`。

- 外壳：`new HelpDialog(title, content, closeText?)`（`title` 走 `LocalizationManager.T()`；尺寸经对象初始化器补设）
- 内容优先 **Markdown 驱动**：`new MarkdownHelpContent(LocalizationManager.T("Xxx_Markdown"))`（Markdig AST → 原生控件树，支持表格、可选中复制）；仅在 Markdown 表达不了时用手写 `UserControl`
- **⚠️ `x:Class` 类型必须有公共无参构造**（`AVLN3000`）；带参构造加 `public Xxx() : this(defaults) { }`。增量构建会掩盖该错，务必 `--no-incremental` 验证
- **原因**：`HelpDialog` 在构造函数内、`DataContext = this` 之前完成 title/content 赋值且只读；另建 Window 或构造后再赋值（无变更通知）会导致整窗静默空白（曾实际发生）
- 已迁移消费者：压缩加速 / 并行解压 / 密码管理器 / 日志隐私帮助（四个 `*_Markdown` 三语 key）
- 回归测试：`tests/MantisZip.UI.Avalonia.Tests/HelpDialogRenderTests.cs`

### 规则 18：计划完成后必须确认 RELEASE_NOTES 更新范围

每个计划（新功能 / 修复 / 性能优化）完成时，**必须主动向用户确认**是否需要更新 `RELEASE_NOTES.md` 以及更新范围，不得擅自写入或遗漏。

**核心判据：RELEASE_NOTES 只记录已发布版本之间的用户可见差异**（上一个 tag → 本次发布）。

- **同周期内引入并修复的问题不进发布说明**：功能本身是本版本新增、其 bug 也在本版本内修掉（从未有用户拿到带该 bug 的版本）→ 不写「修复」条目，只记入 `docs/progress-avalonia-detail.md` 与计划归档
- **「修复」条目仅限**：上一版本已发布、用户实际可能遇到的问题
- **性能类改动**归入「性能 / Performance」节按收益描述，不要写成「修复 XXX」；同一功能的多项子改进合并为一条带 ①② 的条目
- **格式与既有章节一致**：`### 文件说明 / File Description`（中英各一段）+ `### 更新内容 / Changelog`（分类加粗小标题 `**中文 / English**`，每条先中文后英文、`**功能名** — 说明`）
- 更新后同步 `docs/PROGRESS.md` 与 `docs/progress-avalonia-detail.md`（规则 3）

### 规则 19：`plans` 分支只制定计划，不改产品代码

`plans` 分支是**计划分支**：只写计划、改文档、做优先级与方案讨论，不承载产品代码改动。

- **默认禁止**在 `plans` 分支上修改 `src/`、`tests/` 下的任何文件
- **可在 `plans` 分支改**：`docs/`、`.omo/plans/`、`AGENTS.md`、`CLAUDE.md`、`RELEASE_NOTES.md` 等文档；以及不参与产品构建与 CI 门禁的**只读勘察 / 评审辅助脚本**（如 `scripts/render-icon-montage.ps1`）——它们服务于「把计划写清楚」，不是产品功能
- **把代码改动放对分支**：新功能 → `alpha`；缺陷复现 / 修错 / 验证 → `beta`；发版 → `main`
- **用户在 `plans` 分支上要求改代码时，必须先停下来问**，给出两个选项、**不许替用户默认选**：
  1. **忘记切分支**（最常见）→ 建议先 `git switch alpha`（或 `beta`）再动手，等用户确认
  2. **确实要在 `plans` 分支改**（临时验证思路、只为计划服务的一次性试验）→ 得到明确确认后再改，并在回复与提交信息里注明「在 plans 分支改代码」及其原因
- 提问模板：「当前在 `plans`（计划）分支，你要改的是 `src/…` 下的代码。是忘记切到 `alpha` / `beta`，还是确实要在 `plans` 分支改？」

### 规则 20：意图与执行之间只走契约

设置窗口点了「确认」、世界却没反应——这类 bug 编译器不管、运行时不报、测试不覆盖，症状恒定是**界面一切正常，点下去什么也没发生**。所以接缝上只许传契约：确认自带载荷、执行只吃不可变载荷、结局必须能显示、进度只经一个 presenter。

- **设置/确认窗口的唯一出口是 `ConfirmOutcome<T>`**：`Confirmed<T>(value)`（载荷已完整）/ `Rejected<T>(reason)`（`reason` 必须非空且**已本地化**，窗口保持打开并把原因显示给用户）/ `Cancelled<T>`（关窗、Esc、「取消」，不弹任何提示）。**禁止** `ShowDialog<bool>` 之后回读窗口状态：`CopyExecutionSettingsFrom` / `GetFilter` / `GetFilteredEntryKeys` / `GetPlanForExecution` 这类方法**只要被调用方调用就是违规**——它们存在就是在给「搬运参数」留后门。

```csharp
// ❌ 关窗 + 补读：bool 只表示「点了确认」，真参数在窗口公开属性上另取，漏一项就静默失效
if (await dlg.ShowDialog<bool>(owner)) { vm.CopyExecutionSettingsFrom(dlg.ViewModel); vm.FileFilter = dlg.GetFilter(); }

// ✅ 确认即载荷：窗口在确认事务内一次产出快照，执行器拿到的就是用户确认的那一刻
switch (await ViewModel.TryConfirmAsync())
{
    case Confirmed<CompressSettings> c: Close(); await _session.RunAsync(c.Value, _ctx); break;
    case Rejected<CompressSettings> r: await ShowMessage(r.Reason); break;   // 留窗，原因必须让用户看见
    case Cancelled<CompressSettings>: break;                                 // 不弹任何提示
}
```

- **执行只接受不可变载荷**：执行器签名固定为 `Task<XxxOutcome> RunAsync(<XxxSettings>, <XxxRunContext>, CancellationToken = default)`（`ICompressSession` / `IExtractSession`）。`CompressSettings` / `ExtractSettings` 是 `sealed record` + 只读集合：**不许**出现 `Window` / `Presenter` / `Visibility` 之类宿主或呈现概念，也**不许**当作可变回传通道；`CompressRunContext` / `ExtractRunContext` 只装宿主差异（`Owner` / `Presenter` / `ExitPolicy` / `DisplayPaths`），**不许**装业务数据（目标路径、密码、冲突策略等随载荷走）。
- **结局只有四种**：`Completed` / `Cancelled` / `Failed(reason)` / `NotStarted(reason)`（`RunOutcomeState` 枚举 + `CompressOutcome` / `ExtractOutcome` 的静态工厂，不是 bool）。`Failed` / `NotStarted` 的原因必须非空（`OutcomeReasonGuard` 抛 `ArgumentException` + `Debug.Fail` 兜底）；`NotStarted` 的调用方**必须把原因展示给用户**——静默失败是本规则要消灭的头号缺陷。`Cancelled` 不弹提示；`Failed` 的错误已由 presenter 展示，调用方不得重复弹窗。
- **进度呈现只经 `IProgressPresenter`**：进度窗口生命周期（建窗 → 配置 → 显示 → 收尾 → 自动关闭）只在 presenter 实现里写一次，`Task<bool> RunAsync(ProgressRunRequest)` 是唯一入口（`true` = 跑完、`false` = 用户取消）。契约里**不许**出现「是否压缩」「是否 CLI」这类分支：宿主差异用 `ExitPolicy` / `DisplayPaths` / `Owner` / `ConfigureWindow` / `OnCompleted` 表达。presenter 里**不许**弹 `AppMessageBox`、**不许**自造用户文案（标题、说明行、完成文案都由调用方给出且已本地化）。
- **守卫**：`pwsh -File scripts\verify-repo.ps1 -Task guards` 的 `dialog-seam` 检查四类 —— ① `new (Compress|Extract)SettingsWindow(` 的文件与处数必须与脚本里的登记表一致（**双向**：未登记即失败，登记了却找不到也失败）；② 非注释行出现 `CloseAction` / `DialogResult` 双轨回传**命中即失败**；③ `new (Compress|Extract)Request` 只允许出现在 `Services/` 与 CLI 直通路径（`App.CliCompress.cs` / `App.CliExtract.cs`）；④ 设置窗口构造函数必须含 session 形参（`ICompressSession` / `IExtractSession`）。**违规时先改设计，不要改守卫**；确需改守卫，必须同时更新 `docs/MAINTENANCE.md` 与本节。

## 未来工作

见 [docs/PLAN.md](docs/PLAN.md) 和 `.omo/plans/` 下的设计文档。
