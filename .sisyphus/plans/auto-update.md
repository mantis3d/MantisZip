# 自动更新检测功能

## TL;DR

> **Quick Summary**: 为 MantisZip Avalonia 版添加自动更新检测功能。通过 GitHub Releases API 检查新版本，在 AboutWindow 中展示更新信息，启动时后台检查并弹窗通知用户。
>
> **Deliverables**:
> - `UpdateService` (Core) — GitHub API 版本检查
> - `UpdateInfo` (Core) — 更新信息数据模型
> - `UpdateAvailableDialog` (Avalonia) — 新版本通知弹窗
> - AboutWindow 新增「更新」Tab
> - SettingsWindow Debug Tab 增加自动检查开关
> - 中英文 localization 字符串
> - Core 层单元测试
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 3 waves
> **Critical Path**: Task 1 (UpdateInfo) → Task 2 (UpdateService) → Task 4-6 (UI)

---

## Context

### Original Request
> 给 MantisZip 项目加上自动检测更新的功能。

### Interview Summary
**Key Decisions**:
- **更新源**: GitHub Releases API（无需额外服务器）
- **更新策略**: 仅检查 + 通知（发现新版本时提示用户，用户自行决定是否下载）
- **下载行为**: 点击「下载」按钮打开浏览器到 GitHub Releases 页面
- **更新通道**: 仅稳定版（忽略 Pre-release）
- **目标平台**: 仅 Avalonia 版（WPF 废弃前不加）
- **安装形态**: 安装版 + 便携版都支持（检测到安装模式后行为一致——打开浏览器）
- **UI 放置**: AboutWindow 新增「更新」Tab（版本状态 + 检查按钮）+ SettingsWindow Debug Tab 加「自动检查更新」开关
- **检查频率**: 每天最多一次（缓存 LastUpdateCheckTime，同一天内不重复检查）
- **测试策略**: Core 层 UpdateService 编写单元测试（手动 DelegatingHandler mock，无需额外 mock 库）

### Research Findings
- 当前 Avalonia 版本: `0.4.4` (src/MantisZip.UI.Avalonia/AppConstants.cs)
- GitHub 仓库: `mantis3d/MantisZip`
- Release workflow 通过 `v*` tag 触发，产物: installer + 自包含 installer + 便携 zip
- AboutWindow 现有 4 个 Tab（关于/作者/依赖库/致谢），code-behind 模式（DataContext = this）
- 已有 `OpenUrl` 方法通过 `TopLevel.Launcher.LaunchUriAsync` 打开浏览器
- AppSettings 存储到 `%LOCALAPPDATA%\MantisZip\settings.json`
- 本地化使用 `LocalizationManager.T(key)` 模式，字符串在 `strings.zh-CN.json` / `strings.en.json`
- 测试项目使用 xUnit，无 mock 库
- Core 层无 HTTP 客户端使用先例（但 `System.Net.Http` 是 .NET 9 内置）

### Metis Review
**Identified Gaps** (addressed):
- **Gap: SettingsWindow 没有 About 标签** → Settled: 更新信息展示在 AboutWindow 新增 Tab，SettingsWindow Debug Tab 加开关
- **Gap: GitHub API 60 req/h 未认证限制** → Managed: 缓存上次检查时间，每天最多检查一次
- **Gap: GitHub 在中国不可达** → Managed: try-catch 包裹，静默失败，不阻塞启动
- **Gap: 语义化版本比较** → Managed: 使用 `System.Version.Parse()`（去除 `v` 前缀后）
- **Gap: 无 mock 库** → Managed: 使用手动 `DelegatingHandler` 子类做测试替身

---

## Work Objectives

### Core Objective
实现 Avalonia 版自动更新检测：启动时/手动触发检查 GitHub Releases，发现新版本时通知用户。

### Concrete Deliverables
- `MantisZip.Core/Services/UpdateInfo.cs` — 更新信息数据模型
- `MantisZip.Core/Services/UpdateService.cs` — 核心更新检查服务
- `MantisZip.UI.Avalonia/Dialogs/UpdateAvailableDialog.axaml` + `.cs` — 新版本通知弹窗
- `MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml` + `.cs` — 新增「更新」Tab（5th Tab）
- `MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` + `SettingsWindowViewModel.cs` — 自动检查开关
- `MantisZip.UI.Avalonia/Models/AppSettings.cs` — 新增 `EnableAutoUpdateCheck` + `LastUpdateCheckTime` + `LastSkippedVersion`
- `MantisZip.UI.Avalonia/App.axaml.cs` — 启动时触发后台检查
- `MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` — 新增 keys
- `tests/MantisZip.Tests/Services/UpdateServiceTests.cs` — 单元测试

### Definition of Done
- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 编译通过 0 错误
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全部通过
- [ ] 启动 Avalonia 版，打开 AboutWindow 能看到「更新」Tab
- [ ] 关闭网络后不崩溃、不阻塞启动

### Must Have
- GitHub Releases API 获取最新稳定版
- 版本号语义化比较（`System.Version`）
- 每天最多检查一次（`LastUpdateCheckTime` 缓存）
- 发现新版本时弹出 `UpdateAvailableDialog`
- AboutWindow 新增「更新」Tab（展示状态、检查按钮）
- SettingsWindow 增加「自动检查更新」开关
- 手动「检查更新」按钮（AboutWindow 中）
- 「不再提示此版本」功能（`LastSkippedVersion` 缓存）
- 全部网络异常静默处理（不崩溃、不阻塞 UI）

### Must NOT Have (Guardrails)
- **不实现** 应用内自动下载/安装（仅打开浏览器）
- **不实现** 便携版独立检测逻辑（Avalonia 尚未实现便携版模式）
- **不实现** Gitee 备用镜像（后续可加）
- **不实现** DI/IHttpClientFactory（使用静态 HttpClient）
- **不实现** Pre-release 版本检测
- **不实现** WPF 版（仅 Avalonia）

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: YES (xUnit test project)
- **Automated tests**: Tests-after (unit tests for Core UpdateService)
- **Framework**: xUnit
- **Mock strategy**: Manual `DelegatingHandler` subclass (no Moq dependency)

### QA Policy
Every task MUST include agent-executed QA scenarios. Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation — parallel):
├── Task 1: Create UpdateInfo data model [quick]
├── Task 2: Create UpdateService with GitHub API [quick]
└── Task 3: Add settings fields to AppSettings [quick]

Wave 2 (UI — parallel):
├── Task 4: Create UpdateAvailableDialog [visual-engineering]
├── Task 5: Add update check to AboutWindow + startup [unspecified-high]
└── Task 6: Add auto-check toggle to SettingsWindow [unspecified-high]

Wave 3 (Localization + Tests — parallel):
├── Task 7: Add localization strings (zh + en) [writing]
├── Task 8: Write unit tests for UpdateService [quick]

Wave FINAL (Verification — parallel):
├── F1: Plan Compliance Audit [oracle]
├── F2: Code Quality Review [unspecified-high]
├── F3: Real Manual QA [unspecified-high]
└── F4: Scope Fidelity Check [deep]
```

### Dependency Matrix
- **1-3**: - - 4-6
- **4-6**: 1, 2, 3 - 7, 8
- **7, 8**: 4 - F1-F4
- **F1-F4**: All - Done

### Agent Dispatch Summary
- **Wave 1**: 3 agents parallel
- **Wave 2**: 3 agents parallel
- **Wave 3**: 2 agents parallel
- **FINAL**: 4 agents parallel

---

## TODOs

- [ ] 1. Create UpdateInfo data model

  **What to do**:
  - Create `src/MantisZip.Core/Services/UpdateInfo.cs` with `readonly record struct` or simple class
  - Fields:
    - `LatestVersion` (string) — tag_name without `v` prefix, e.g. `"0.4.5"`
    - `DownloadUrl` (string) — URL to GitHub release page: `https://github.com/mantis3d/MantisZip/releases/tag/v{version}`
    - `ReleaseNotesUrl` (string) — same as DownloadUrl (GitHub release page serves as release notes)
    - `ReleaseDate` (DateTime?) — from `published_at` field
    - `PublishDateDisplay` (string, computed) — formatted date string for UI display
  - Place in `MantisZip.Core.Services` namespace (matching existing service pattern)
  - No external dependencies needed

  **Must NOT do**:
  - Don't make it mutable (use `readonly` or `init`-only properties)
  - Don't add unnecessary fields (keep minimal — only what the UI needs)

  **Recommended Agent Profile**:
  - **Category**: `quick` — Simple data model, no logic

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3)
  - **Blocks**: Tasks 4, 5, 6
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Services/` — Existing services directory for placement pattern
  - GitHub API response format: `https://docs.github.com/en/rest/releases/releases#get-the-latest-release` — `tag_name`, `prerelease`, `published_at` fields

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: UpdateInfo can be constructed with key fields
    Tool: Bash
    Preconditions: File exists at src/MantisZip.Core/Services/UpdateInfo.cs
    Steps:
      1. Create a simple test script or verify compilation: dotnet build src/MantisZip.Core/MantisZip.Core.csproj
    Expected Result: Build succeeds, no errors
    Evidence: .sisyphus/evidence/task-1-build.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add UpdateInfo data model`
  - Files: `src/MantisZip.Core/Services/UpdateInfo.cs`

- [ ] 2. Create UpdateService with GitHub API

  **What to do**:
  - Create `src/MantisZip.Core/Services/UpdateService.cs`
  - **Static class** with static `HttpClient` (for socket reuse, best practice)
  - Key method: `Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, bool includePreRelease = false)`
    - Calls `GET https://api.github.com/repos/mantis3d/MantisZip/releases/latest`
    - Sets `User-Agent: MantisZip/0.4.4` (GitHub API requires User-Agent)
    - Sets `Accept: application/vnd.github+json`
    - Parses JSON response with `System.Text.Json`
    - Checks `prerelease` field — skip if `true` (unless `includePreRelease` is true)
    - Compares version using `System.Version.Parse(tag_name.TrimStart('v'))` against `currentVersion`
    - Returns `null` if current version >= latest version
    - Returns `UpdateInfo` if latest version > current version
  - **Throttle helper**: `static bool ShouldCheck(DateTime? lastCheckTime)` — returns false if last check was within 24 hours
  - **Cache support**: Methods accept `lastCheckTime` and `lastSkippedVersion` as parameters (pure logic, no AppSettings dependency)
  - **Exception safety**: Wrap all HTTP/parse in try-catch, return null on any failure
  - Set HttpClient timeout to 5 seconds
  - Use `HttpClient.DefaultRequestHeaders` for static headers
  - **Testability**: Add `internal static void SetTestHandler(HttpMessageHandler handler)` method that replaces the static `HttpClient` with one using the test handler. This keeps the static pattern while allowing test injection. Reset with `SetTestHandler(null)` to restore default.

  **Must NOT do**:
  - Don't reference any UI project types
  - Don't throw exceptions to callers (return null instead)
  - Don't use `IHttpClientFactory` or DI (project has no DI container)
  - Don't access AppSettings directly (pass values as parameters)

  **Recommended Agent Profile**:
  - **Category**: `quick` — Single service class, straightforward HTTP logic

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3)
  - **Blocks**: Tasks 4, 5, 6, 8
  - **Blocked By**: Task 1 (uses UpdateInfo)

  **References**:
  - `src/MantisZip.Core/Services/` — Service placement pattern
  - GitHub API docs: `https://docs.github.com/en/rest/releases/releases#get-the-latest-release`
  - .NET HttpClient best practices: `https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines`
  - `System.Version.Parse()` docs: `https://learn.microsoft.com/en-us/dotnet/api/system.version.parse`

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: ShouldCheck returns false within 24 hours
    Tool: Bash (dotnet script or inline test)
    Preconditions: UpdateService compiled
    Steps:
      1. Call UpdateService.ShouldCheck(DateTime.UtcNow.AddHours(-1)) — should return false
      2. Call UpdateService.ShouldCheck(DateTime.UtcNow.AddHours(-25)) — should return true
      3. Call UpdateService.ShouldCheck(null) — should return true (never checked before)
    Expected Result: All return expected booleans
    Evidence: .sisyphus/evidence/task-2-shouldcheck.txt

  Scenario: Version comparison works correctly
    Tool: Bash (dotnet script)
    Preconditions: UpdateService compiled
    Steps:
      1. Verify: "0.4.10" > "0.4.4" (using System.Version)
      2. Verify: "0.4.4" == "0.4.4"
      3. Verify: "v0.4.5".TrimStart('v') == "0.4.5"
    Expected Result: Semantic version comparison works correctly
    Evidence: .sisyphus/evidence/task-2-version-compare.txt

  Scenario: CheckForUpdateAsync fails gracefully on network error
    Tool: Bash
    Preconditions: Create test project reference
    Steps:
      1. Mock HttpClient with DelegatingHandler that throws HttpRequestException
      2. Call CheckForUpdateAsync("0.0.0")
      3. Assert returns null (no crash)
    Expected Result: Null returned, no exception propagated
    Evidence: .sisyphus/evidence/task-2-network-error.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add UpdateService for GitHub release checking`
  - Files: `src/MantisZip.Core/Services/UpdateService.cs`

- [ ] 3. Add auto-update settings to AppSettings

  **What to do**:
  - Edit `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`
  - Add these fields with defaults:
    - `EnableAutoUpdateCheck` (bool, default: `true`) — enable startup check
    - `LastUpdateCheckTime` (DateTime?, default: `null`) — timestamp of last check (for throttle)
    - `LastSkippedVersion` (string?, default: `null`) — version user chose to skip/dismiss
  - Follow existing pattern: auto-property with `{ get; set; }` and default value
  - `LastUpdateCheckTime` should be serialized as ISO 8601 string (use `string?` with manual parse, or DateTime? with JsonConverter)

  **Must NOT do**:
  - Don't change existing field names or types
  - Don't add UI logic to AppSettings

  **Recommended Agent Profile**:
  - **Category**: `quick` — Simple field additions

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2)
  - **Blocks**: Tasks 5, 6
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` — Existing pattern for settings fields
  - JSON serialization pattern for DateTime in AppSettings (check existing fields)

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: AppSettings compiles with new fields
    Tool: Bash
    Preconditions: File modified
    Steps:
      1. dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeds
    Evidence: .sisyphus/evidence/task-3-build.txt

  Scenario: New fields serialize/deserialize correctly
    Tool: Bash
    Preconditions: Build succeeds
    Steps:
      1. Verify EnableAutoUpdateCheck defaults to true
      2. Verify LastUpdateCheckTime defaults to null
      3. Verify LastSkippedVersion defaults to null
    Expected Result: All defaults correct
    Evidence: .sisyphus/evidence/task-3-defaults.txt
  ```

  **Commit**: YES
  - Message: `feat(avalonia): add auto-update settings to AppSettings`
  - Files: `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`

- [ ] 4. Create UpdateAvailableDialog

  **What to do**:
  - Create `src/MantisZip.UI.Avalonia/Dialogs/UpdateAvailableDialog.axaml` + `.axaml.cs`
  - **Dialog layout** (center-owner, modal):
    - Title: "发现新版本" / "Update Available"
    - Show current version vs new version comparison
    - Show link to GitHub release page for release notes
    - Three buttons:
      - **"下载"** — calls `TopLevel.Launcher.LaunchUriAsync` to open `https://github.com/mantis3d/MantisZip/releases/tag/v{version}` in browser, then closes dialog
      - **"稍后提醒"** — closes dialog (next check will re-prompt)
      - **"不再提示此版本"** — sets `LastSkippedVersion` in AppSettings, closes dialog
  - Dialog receives `UpdateInfo` as constructor parameter
  - Follow code-behind pattern of AboutWindow (DataContext = this, theme resources)
  - Size: ~500x400, CenterOwner, ShowInTaskbar=False

  **Must NOT do**:
  - Don't implement in-app download (browser only)
  - Don't use hardcoded colors (use DynamicResource theme resources)

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering` — Dialog UI

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 5, 6)
  - **Blocks**: Task 7
  - **Blocked By**: Task 1 (UpdateInfo), Task 2 (UpdateService)

  **References**:
  - `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml` — Dialog layout pattern
  - `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml.cs:OpenUrl()` — Browser launch via Launcher.LaunchUriAsync
  - `src/MantisZip.Core/Services/UpdateInfo.cs` — Data model

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: Dialog compiles and builds
    Tool: Bash
    Preconditions: Files created
    Steps:
      1. dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeds, 0 errors
    Evidence: .sisyphus/evidence/task-4-build.txt
  ```

  **Commit**: YES
  - Message: `feat(avalonia): add UpdateAvailableDialog`
  - Files: `src/MantisZip.UI.Avalonia/Dialogs/UpdateAvailableDialog.axaml`, `src/MantisZip.UI.Avalonia/Dialogs/UpdateAvailableDialog.axaml.cs`

- [ ] 5. Add update tab to AboutWindow + startup check

  **What to do**:
  - **A) AboutWindow.axaml**: Add 5th TabItem "更新 / Updates" before `</TabControl>`
    - Tab header: `x:Name="TabUpdatesHeader"` (text set in code-behind)
    - Content: current version display, status label, "检查更新" button, status text
  - **B) AboutWindow.axaml.cs**: Add:
    - Update status properties (bound or directly set on TextBlocks)
    - `private async Task CheckForUpdateAsync()`:
      - Check `AppSettings.Instance.EnableAutoUpdateCheck`
      - Call `UpdateService.CheckForUpdateAsync(AppConstants.Version)`
      - If update found and version != LastSkippedVersion: show UpdateAvailableDialog
      - Save `AppSettings.Instance.LastUpdateCheckTime = DateTime.UtcNow`
    - `OnCheckUpdatesClick` handler for manual check button
  - **C) App.axaml.cs**: After `desktop.MainWindow = new MainWindow()`:
    - Fire-and-forget: `_ = CheckForUpdatesOnStartupAsync()`
    - Use `Task.Run` with 2s delay, dispatch to UI thread for dialog
    - Must NOT block startup

  **Must NOT do**:
  - Don't block startup with synchronous network call
  - Don't show dialog for already-skipped versions

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high` — UI + async

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 4, 6)
  - **Blocks**: Task 7
  - **Blocked By**: Tasks 1, 2, 3

  **References**:
  - `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml` — Tab structure
  - `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml.cs` — Code-behind pattern
  - `src/MantisZip.UI.Avalonia/App.axaml.cs` — Startup entry
  - `src/MantisZip.Core/Services/UpdateService.cs` — API
  - `src/MantisZip.UI.Avalonia/Dialogs/UpdateAvailableDialog.axaml` — Dialog

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: AboutWindow builds with 5th Updates tab
    Tool: Bash
    Preconditions: All deps ready
    Steps:
      1. dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeds
    Evidence: .sisyphus/evidence/task-5-build.txt

  Scenario: Check for updates button exists
    Tool: Bash + grep
    Preconditions: Build succeeds
    Steps:
      1. grep "OnCheckUpdatesClick" src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml.cs
      2. grep "检查更新\|Check for Updates" src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml
    Expected Result: Both found
    Evidence: .sisyphus/evidence/task-5-button.txt
  ```

  **Commit**: YES
  - Message: `feat(avalonia): add update tab to AboutWindow + startup check`
  - Files: `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml`, `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml.cs`, `src/MantisZip.UI.Avalonia/App.axaml.cs`

- [ ] 6. Add auto-update toggle to SettingsWindow

  **What to do**:
  - **A) SettingsWindowViewModel.cs**: Add `EnableAutoUpdateCheck` property bound to `AppSettings.Instance.EnableAutoUpdateCheck`
  - **B) SettingsWindow.axaml**: In the Debug tab (last tab), find the appropriate section and add a border-group:
    - Title: "更新设置" / "Update Settings"
    - CheckBox: "启动时自动检查更新" / "Check for updates on startup"
    - Bound to `{Binding EnableAutoUpdateCheck}`
  - Follow existing SettingsWindow visual pattern (Border with CornerRadius, StackPanel spacing)

  **Must NOT do**:
  - Don't create a new Settings tab
  - Don't change existing settings layout or theme

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 4, 5)
  - **Blocks**: Task 7
  - **Blocked By**: Task 3 (AppSettings)

  **References**:
  - `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` — Tab layout (find Debug tab)
  - `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs` — Property binding pattern
  - `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` — EnableAutoUpdateCheck field

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: SettingsWindow builds with update toggle
    Tool: Bash
    Preconditions: All deps ready
    Steps:
      1. dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeds
    Evidence: .sisyphus/evidence/task-6-build.txt
  ```

  **Commit**: YES
  - Message: `feat(avalonia): add auto-update toggle to SettingsWindow`
  - Files: `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml`, `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs`

- [ ] 7. Add localization strings (zh + en)

  **What to do**:
  - Edit `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json`:
    - Add keys for update feature (Chinese translations):
      - `"Update_TabName": "更新"`
      - `"Update_CurrentVersion": "当前版本"`
      - `"Update_CheckNow": "检查更新"`
      - `"Update_Checking": "正在检查更新…"`
      - `"Update_UpToDate": "已是最新版本"`
      - `"Update_NewVersionAvailable": "发现新版本 v{0}"`
      - `"Update_CheckFailed": "检查更新失败，请检查网络连接"`
      - `"Update_Download": "下载"`
      - `"Update_RemindLater": "稍后提醒"`
      - `"Update_SkipThisVersion": "不再提示此版本"`
      - `"Update_LastCheckTime": "上次检查: {0}"`
      - `"Update_DialogTitle": "发现新版本"`
      - `"Update_NewVersion": "最新版本"`
      - `"Update_ReleaseNotes": "更新说明"`
      - `"Update_Settings_AutoCheck": "启动时自动检查更新"`
      - `"Update_Settings_SectionTitle": "更新设置"`
  - Edit `src/MantisZip.UI.Avalonia/Localization/strings.en.json`:
    - Same keys with English translations
  - Follow existing JSON formatting (same indentation, no trailing commas)

  **Must NOT do**:
  - Don't remove or rename existing keys
  - Don't change existing translation values

  **Recommended Agent Profile**:
  - **Category**: `writing` — Translation/localization

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 8)
  - **Blocks**: None (but should be done before final QA)
  - **Blocked By**: Tasks 4, 5, 6 (to know all key names needed)

  **References**:
  - `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` — Existing Chinese translations
  - `src/MantisZip.UI.Avalonia/Localization/strings.en.json` — Existing English translations
  - `src/MantisZip.UI.Avalonia/Services/LocalizationManager.cs:T()` — How keys are accessed

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: All update keys present in both languages
    Tool: Bash + grep
    Preconditions: Files edited
    Steps:
      1. For each "Update_" key in strings.zh-CN.json, verify same key exists in strings.en.json
      2. Verify JSON is valid (no parse errors)
    Expected Result: All keys present in both files, valid JSON
    Evidence: .sisyphus/evidence/task-7-keys.txt
  ```

  **Commit**: YES
  - Message: `i18n: add update-related localization strings`
  - Files: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json`, `src/MantisZip.UI.Avalonia/Localization/strings.en.json`

- [ ] 8. Write unit tests for UpdateService

  **What to do**:
  - Create `tests/MantisZip.Tests/Services/UpdateServiceTests.cs`
  - Test cases:
    1. `ShouldCheck_Within24Hours_ReturnsFalse` — verifies throttle
    2. `ShouldCheck_Over24Hours_ReturnsTrue`
    3. `ShouldCheck_NeverChecked_ReturnsTrue`
    4. `CheckForUpdateAsync_HttpClientReturnsNewVersion_ReturnsUpdateInfo` — using DelegatingHandler mock that returns a realistic GitHub API JSON response
    5. `CheckForUpdateAsync_HttpClientReturnsSameVersion_ReturnsNull` — mock returns version same as current
    6. `CheckForUpdateAsync_HttpClientThrows_ReturnsNull` — mock throws HttpRequestException
    7. `CheckForUpdateAsync_PreReleaseVersion_ReturnsNull` — mock returns prerelease=true
    8. `CheckForUpdateAsync_VersionComparison10GreaterThan4` — verify 0.4.10 > 0.4.4
  - Create a `MockHttpHandler : DelegatingHandler` helper class that returns canned JSON responses
  - Pass handler to `HttpClient` constructor used by tests (UpdateService must accept HttpClient in constructor or via a test-friendly factory method)

  **Must NOT do**:
  - Don't depend on external network (all HTTP mocked)
  - Don't add Moq or any NuGet mock package (use DelegatingHandler)

  **Recommended Agent Profile**:
  - **Category**: `quick` — Unit tests

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 7)
  - **Blocks**: F1-F4
  - **Blocked By**: Task 2 (UpdateService)

  **References**:
  - `tests/MantisZip.Tests/Services/` — Existing service tests pattern
  - `src/MantisZip.Core/Services/UpdateService.cs` — The service under test
  - xUnit `[Fact]` pattern docs

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: All update tests pass
    Tool: Bash
    Preconditions: Tests written
    Steps:
      1. dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected Result: All tests pass (8+ tests, 0 failures)
    Evidence: .sisyphus/evidence/task-8-tests.txt
  ```

  **Commit**: YES
  - Message: `test: add UpdateService unit tests`
  - Files: `tests/MantisZip.Tests/Services/UpdateServiceTests.cs`

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists. For each "Must NOT Have": search codebase for forbidden patterns. Check evidence files in .sisyphus/evidence/. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build` + `dotnet test`. Review for: empty catches, console.log in prod, unused imports, commented-out code. Check AI slop.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute EVERY QA scenario from EVERY task. Test cross-task integration. Save to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 — everything in spec was built, nothing beyond spec was built. Check "Must NOT do" compliance.
  Output: `Tasks [N/N compliant] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **1**: `feat(core): add UpdateInfo data model`
- **2**: `feat(core): add UpdateService for GitHub release checking`
- **3**: `feat(avalonia): add auto-update settings to AppSettings`
- **4**: `feat(avalonia): add UpdateAvailableDialog`
- **5**: `feat(avalonia): add update tab to AboutWindow + startup check`
- **6**: `feat(avalonia): add auto-update toggle to SettingsWindow`
- **7**: `i18n: add update-related localization strings`
- **8**: `test: add UpdateService unit tests`

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj   # Expected: Build succeeded
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj               # Expected: all tests pass
```

### Final Checklist
- [ ] UpdateService correctly parses GitHub API response
- [ ] Version comparison works: 0.4.3 < 0.4.4 < 0.4.10
- [ ] Pre-release versions are ignored
- [ ] Network errors handled gracefully (no crash)
- [ ] Daily throttle works (cached check within 24h)
- [ ] Auto-check off suppresses all API calls
- [ ] UpdateAvailableDialog shows version comparison + release notes URL
- [ ] Download button opens browser to GitHub Releases
- [ ] Skip-version feature persists across restarts
- [ ] All localization strings present in zh-CN and en
