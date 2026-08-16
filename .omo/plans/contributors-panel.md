# Contributors Panel — 致谢贡献者名单（双分类版）

> **状态**: ✅ 已完成（v0.4.3+）| **阶段**: [████████████████████] (全部完成)

## TL;DR

> **Quick Summary**: 在 AboutWindow 的「致谢」Tab 下新增两个贡献者名单区域——**技术贡献者**和**资金支持者**。各读取独立的 CSV 文件（UTF-8 BOM, 名字,分数），按分数降序 + 名字字母序排列，WrapPanel 每行4个名字对齐显示。创建默认数据文件供分发。
>
> **Deliverables**:
> - 修改 `AboutWindow.xaml` — 致谢 Tab 内新增两个贡献者名单区域（技术 + 资金）
> - 修改 `AboutWindow.xaml.cs` — CSV 加载/解析/排序/填充逻辑
> - 新建 `src/MantisZip.UI/contributors-technical.csv` — 技术贡献者默认数据
> - 新建 `src/MantisZip.UI/contributors-financial.csv` — 资金支持者默认数据
> - 修改 `Localization/L.cs` + `Resources/strings.zh.json` + `Resources/strings.en.json` — 3 个新本地化键
> - 修改 `tests/MantisZip.Tests/AboutWindowTests.cs` — 新增键校验，计数 21→24
> - 修改 `installer.iss` — 安装包包含两个 CSV
>
> **Estimated Effort**: Quick
> **Parallel Execution**: NO — 6 sequential tasks, small scope
> **Critical Path**: CSV files → localization keys → XAML → Code-behind → tests → installer

---

## Context

### Original Request
在 AboutWindow 的「致谢」面板下方增加可更新的人员名单，Grid 样式每行4个名字，对齐排列。数据从 CSV 读取（名字+整数分数），按分数排序显示。开发者直接编辑 CSV 即可增删改。

### Interview Summary
**Decisions Made**:
- **数据格式**: CSV, UTF-8 with BOM, 每行 `名字,分数`
- **文件位置**: 应用目录下 (`BaseDirectory + "contributors-technical.csv"` / `contributors-financial.csv`)
- **展示方式**: **分两区先后展示**——先「技术贡献者」、再「资金支持者」。各用 WrapPanel 自动换行，显示名字（不显示分数），各自按分数降序排列，同分按名字字母序
- **空状态**: 每个区独立处理——有数据显示 WrapPanel，无数据则显示「暂无贡献者」
- **编辑方式**: 开发者直接用 Excel/VS Code 编辑 CSV，不提供 UI 编辑入口
- **分发**: 随安装包分发，两个 CSV 均预设示例数据
- **主题**: 使用 StaticResource（与现有 AboutWindow 一致）

### Metis Review
**Gaps Identified and Addressed**:
- L.cs 是 auto-generated — 确认需要同时修改 L.cs + strings.zh.json + strings.en.json 三处
- 测试硬编码 ExpectedAboutKeys 数组 — 需要追加新键并 bump count
- BOM 处理 — 使用 StreamReader 自动检测，避免 `File.ReadAllLines` 对 BOM 敏感
- WrapPanel vs UniformGrid — 选择 WrapPanel 获得更好的响应式布局
- 空状态行为 — 确认显示标题 + 占位文字
- 文件锁定异常 — 需要 try-catch IOException
- installer.iss — 需要添加 contributors.csv 的安装行

---

## Work Objectives

### Core Objective
在 AboutWindow 的「致谢」Tab 中新增贡献者名单区域，读取外部 CSV 数据并按分数排序显示。

### Concrete Deliverables
- `src/MantisZip.UI/contributors-technical.csv` — 技术贡献者数据文件
- `src/MantisZip.UI/contributors-financial.csv` — 资金支持者数据文件
- `AboutWindow.xaml` — 新增两个贡献者名单 UI 区域（技术 + 资金）
- `AboutWindow.xaml.cs` — CSV 读取/排序/填充逻辑（共用方法）
- `MantisZip.UI.csproj` — 两个 CSV 的 CopyToOutputDirectory
- `strings.zh.json` + `strings.en.json` + `L.cs` — About_Contributors_Technical / About_Contributors_Financial / About_Contributors_None
- `AboutWindowTests.cs` — 新增键校验 + 计数更新 21→24
- `installer.iss` — 安装程序包含两个 CSV

### Definition of Done
- [x] 两个 CSV 文件存在且格式正确
- [x] 打开 AboutWindow → 致谢 Tab → 先显示技术贡献者名单，再显示资金支持者名单，每行约4个名字
- [x] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 无错误
- [x] `dotnet test tests\MantisZip.Tests\` 全部通过

### Must Have
- 两个独立 WrapPanel 区域，分别排序，分别处理空状态
- 每个区域各自按分数降序排列，同分按名字字母排序
- 空文件或文件不存在时对该区显示"暂无贡献者"
- BOM 头兼容（UTF-8 with BOM 和 without BOM 都支持）
- 文件读取异常保护（IOException 时优雅降级为空状态）
- 安装包分发两个 CSV 文件

### Must NOT Have (Guardrails)
- ❌ 不得添加 UI 编辑器或编辑按钮（直接编辑 CSV）
- ❌ 不得显示分数或排名
- ❌ 不得添加点击/交互/Hover 效果
- ❌ 不得添加头像/图标/GitHub 链接
- ❌ 不得添加搜索/筛选功能
- ❌ 不得使用第三方 CSV 解析库（内联解析即可）
- ❌ 不得创建新 XAML 文件或新窗口
- ❌ 不得修改 TabControl 标签页结构

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: Tests-after (更新现有测试)
- **Framework**: xUnit

### QA Policy
Every task MUST include agent-executed QA scenarios.

---

## Execution Strategy

### Task Sequence
```
Task 1: Create contributors.csv
    └─► Task 2: Add localization keys (JSON + L.cs + tests)
            └─► Task 3: Update AboutWindow.xaml (UI)
                    └─► Task 4: Update AboutWindow.xaml.cs (logic)
                            └─► Task 5: Build + Test verification
```

---

## TODOs

- [x] 1. Create CSV data files

  **What to do**:
  Create two CSV files in `src/MantisZip.UI/` with UTF-8 with BOM encoding.

  **File 1: `contributors-technical.csv`**
  ```csv
  # 技术贡献者 — 每行: 名字,分数（整数，从高到低排序显示）
  mantis3d,10000
  ```

  **File 2: `contributors-financial.csv`**
  ```csv
  # 资金支持者 — 每行: 名字,分数（整数，从高到低排序显示）
  赞助者A,500
  赞助者B,200
  ```

  **Add to `MantisZip.UI.csproj`** (after existing Content items):
  ```xml
  <Content Include="contributors-technical.csv">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
  <Content Include="contributors-financial.csv">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
  ```

  **Must NOT do**:
  - Don't use BOM-less UTF-8

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (foundation)
  - **Blocks**: Task 4 (code-behind needs files)
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] `contributors-technical.csv` created with UTF-8 BOM
  - [ ] `contributors-financial.csv` created with UTF-8 BOM
  - [ ] Both have header comments starting with #
  - [ ] .csproj contains both Content items

  **QA Scenarios**:
  ```
  Scenario: Check BOM encoding on both files
    Tool: Bash
    Preconditions: Files created
    Steps:
      1. `powershell -Command "[System.IO.File]::ReadAllBytes('src/MantisZip.UI/contributors-technical.csv') | Select-Object -First 3"`
      2. `powershell -Command "[System.IO.File]::ReadAllBytes('src/MantisZip.UI/contributors-financial.csv') | Select-Object -First 3"`
    Expected Result: Both show 0xEF, 0xBB, 0xBF
    Evidence: .sisyphus/evidence/task-1-bom-check.txt

  Scenario: Verify .csproj Content items
    Tool: Bash
    Preconditions: None
    Steps:
      1. `Select-String -Pattern 'contributors-technical\.csv' 'src/MantisZip.UI/MantisZip.UI.csproj'`
      2. `Select-String -Pattern 'contributors-financial\.csv' 'src/MantisZip.UI/MantisZip.UI.csproj'`
    Expected Result: Both found with CopyToOutputDirectory
    Evidence: .sisyphus/evidence/task-1-csproj.txt
  ```

  **Commit**: YES
  - Message: `feat: add contributors-technical.csv and contributors-financial.csv`
  - Files: `src/MantisZip.UI/contributors-technical.csv`, `src/MantisZip.UI/contributors-financial.csv`, `src/MantisZip.UI/MantisZip.UI.csproj`

---

- [x] 2. Add localization keys (3 new keys)

  **What to do**:
  Add 3 new keys to the localization system.

  **strings.zh.json** (after `About_Thanks_7Zip`):
  ```json
  "About_Contributors_Technical": "技术贡献者",
  "About_Contributors_Financial": "资金支持者",
  "About_Contributors_None": "暂无贡献者"
  ```

  **strings.en.json** (after `About_Thanks_7Zip`):
  ```json
  "About_Contributors_Technical": "Technical Contributors",
  "About_Contributors_Financial": "Financial Supporters",
  "About_Contributors_None": "No contributors yet"
  ```

  **L.cs** (after line 67 `About_Thanks_7Zip`, following same formatting style):
  ```csharp
  public const string About_Contributors_Technical                    = "About_Contributors_Technical";
  public const string About_Contributors_Financial                    = "About_Contributors_Financial";
  public const string About_Contributors_None                        = "About_Contributors_None";
  ```

  **Must NOT do**:
  - Don't modify any existing keys or values
  - Don't change formatting/indentation of L.cs

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (foundation)
  - **Blocks**: Task 3 (XAML), Task 4 (code-behind)
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] zh.json has all 3 new keys with Chinese values
  - [ ] en.json has all 3 new keys with English values
  - [ ] L.cs has all 3 `public const string` declarations
  - [ ] `dotnet build` compiles

  **QA Scenarios**:
  ```
  Scenario: Verify zh.json keys
    Tool: Bash
    Steps: Select-String for each key in strings.zh.json
    Expected: All 3 found with non-empty values
    Evidence: .sisyphus/evidence/task-2-zh-keys.txt

  Scenario: Verify en.json keys
    Preconditions: None
    Steps:
      1. `Select-String -Pattern '"About_Contributors"' 'src/MantisZip.UI/Resources/strings.en.json'`
      2. `Select-String -Pattern '"About_Contributors_None"' 'src/MantisZip.UI/Resources/strings.en.json'`
    Expected Result: Both keys found with non-empty values
    Evidence: .sisyphus/evidence/task-2-en-keys.txt

  Scenario: Verify L.cs constants
    Tool: Bash
    Preconditions: None
    Steps:
      1. `Select-String -Pattern 'About_Contributors' 'src/MantisZip.UI/Localization/L.cs'`
    Expected Result: Both constants declared (2 matches)
    Evidence: .sisyphus/evidence/task-2-lcs-keys.txt
  ```

  **Commit**: YES (groups with task 3, 4)
  - Message: `feat: add About_Contributors and About_Contributors_None localization keys`
  - Files: `src/MantisZip.UI/Resources/strings.zh.json`, `src/MantisZip.UI/Resources/strings.en.json`, `src/MantisZip.UI/Localization/L.cs`

---

- [x] 3. Update AboutWindow.xaml — two contributor sections

  **What to do**:
  In `AboutWindow.xaml`, locate the Acknowledgments TabItem (lines 373-388). After the `About_Thanks_AI` TextBlock (line 383-385), expand the StackPanel to include **two sections** (Technical + Financial), each following the same pattern.

  Replace the current StackPanel content inside the Acknowledgments Tab with:

  ```xml
  <StackPanel Margin="24">
      <TextBlock Text="{l:L About_Thanks_OSS}" TextWrapping="Wrap"
                 FontSize="13"
                 Foreground="{StaticResource Theme_TextPrimary}"/>
      <TextBlock Text="{l:L About_Thanks_7Zip}" TextWrapping="Wrap"
                 FontSize="13" Margin="0,12,0,0"
                 Foreground="{StaticResource Theme_TextPrimary}"/>
      <TextBlock Text="{l:L About_Thanks_AI}" TextWrapping="Wrap"
                 FontSize="13" Margin="0,12,0,0"
                 Foreground="{StaticResource Theme_TextSecondary}"/>

      <!-- ─── 技术贡献者 / Technical Contributors ─── -->
      <Border Height="1" Margin="0,16,0,8"
              Background="{StaticResource Theme_BorderLight}"/>
      <TextBlock Text="{l:L About_Contributors_Technical}"
                 FontSize="14" FontWeight="Bold" Margin="0,0,0,8"
                 Foreground="{StaticResource Theme_TextPrimary}"/>
      <ItemsControl x:Name="ContributorsTechnicalList"
                    Background="Transparent" BorderThickness="0">
          <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate>
                  <WrapPanel Orientation="Horizontal"
                             ItemWidth="140" ItemHeight="26"/>
              </ItemsPanelTemplate>
          </ItemsControl.ItemsPanel>
          <ItemsControl.ItemTemplate>
              <DataTemplate>
                  <TextBlock Text="{Binding Name}"
                             Foreground="{StaticResource Theme_TextPrimary}"
                             FontSize="13"
                             VerticalAlignment="Center"
                             Margin="4,2"/>
              </DataTemplate>
          </ItemsControl.ItemTemplate>
      </ItemsControl>
      <TextBlock x:Name="ContributorsTechnicalEmpty"
                 Text="{l:L About_Contributors_None}"
                 Foreground="{StaticResource Theme_TextSecondary}"
                 FontSize="13"
                 Visibility="Collapsed"
                 Margin="0,4,0,0"/>

      <!-- ─── 资金支持者 / Financial Supporters ─── -->
      <Border Height="1" Margin="0,16,0,8"
              Background="{StaticResource Theme_BorderLight}"/>
      <TextBlock Text="{l:L About_Contributors_Financial}"
                 FontSize="14" FontWeight="Bold" Margin="0,0,0,8"
                 Foreground="{StaticResource Theme_TextPrimary}"/>
      <ItemsControl x:Name="ContributorsFinancialList"
                    Background="Transparent" BorderThickness="0">
          <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate>
                  <WrapPanel Orientation="Horizontal"
                             ItemWidth="140" ItemHeight="26"/>
              </ItemsPanelTemplate>
          </ItemsControl.ItemsPanel>
          <ItemsControl.ItemTemplate>
              <DataTemplate>
                  <TextBlock Text="{Binding Name}"
                             Foreground="{StaticResource Theme_TextPrimary}"
                             FontSize="13"
                             VerticalAlignment="Center"
                             Margin="4,2"/>
              </DataTemplate>
          </ItemsControl.ItemTemplate>
      </ItemsControl>
      <TextBlock x:Name="ContributorsFinancialEmpty"
                 Text="{l:L About_Contributors_None}"
                 Foreground="{StaticResource Theme_TextSecondary}"
                 FontSize="13"
                 Visibility="Collapsed"
                 Margin="0,4,0,0"/>
  </StackPanel>
  ```

  **Why WrapPanel with ItemWidth=140**: At the default window width (~680px - margins ≈ 600px usable), each item occupies ~140px, yielding ~4 items per row. Responsive to window resizing.

  **Must NOT do**:
  - Don't modify other TabItems
  - Don't change TabItem ordering or header text
  - Don't add click handlers
  - Don't use DynamicResource (match StaticResource pattern)

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocked By**: Task 2 (localization keys)

  **Acceptance Criteria**:
  - [ ] Two ItemsControls: `ContributorsTechnicalList` + `ContributorsFinancialList`
  - [ ] Two empty-state TextBlocks: `ContributorsTechnicalEmpty` + `ContributorsFinancialEmpty`
  - [ ] Both use WrapPanel with ItemWidth=140
  - [ ] Theme bindings use StaticResource
  - [ ] `dotnet build` succeeds

  **QA Scenarios**:
  ```
  Scenario: Verify both sections exist in XAML
    Tool: Bash
    Steps:
      1. Select-String 'ContributorsTechnicalList' AboutWindow.xaml
      2. Select-String 'ContributorsFinancialList' AboutWindow.xaml
      3. Select-String 'ContributorsTechnicalEmpty' AboutWindow.xaml
      4. Select-String 'ContributorsFinancialEmpty' AboutWindow.xaml
    Expected: All 4 found
    Evidence: .sisyphus/evidence/task-3-xaml-sections.txt

  Scenario: Build succeeds
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-3-build.txt
  ```

  **Commit**: YES (groups with task 2, 4)
  - Message: `feat: add technical and financial contributors sections to Acknowledgments tab`
  - Files: `src/MantisZip.UI/Dialogs/AboutWindow.xaml`

---

- [x] 4. Update AboutWindow.xaml.cs — shared CSV loader for both categories

  **What to do**:
  Add a `Contributor` model class and a shared loader method that supports both CSV files.

  **1. Contributor model** (at bottom of file, inside namespace before class closing):
  ```csharp
  private class Contributor
  {
      public string Name { get; init; } = "";
      public int Score { get; init; }
  }
  ```

  **2. Constructor update** (add `LoadContributors()` call):
  ```csharp
  public AboutWindow()
  {
      InitializeComponent();
      VersionText.Text = "v" + AppConstants.Version;
      LoadContributors();
  }
  ```

  **3. Shared loader methods**:
  ```csharp
  private void LoadContributors()
  {
      LoadContributorList("contributors-technical.csv", ContributorsTechnicalList, ContributorsTechnicalEmpty);
      LoadContributorList("contributors-financial.csv", ContributorsFinancialList, ContributorsFinancialEmpty);
  }

  private void LoadContributorList(string fileName, ItemsControl listControl, TextBlock emptyControl)
  {
      try
      {
          var csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
          if (!File.Exists(csvPath))
          {
              ShowEmptyState(listControl, emptyControl);
              return;
          }

          var contributors = new List<Contributor>();
          var lines = File.ReadAllLines(csvPath, Encoding.UTF8);

          foreach (var line in lines)
          {
              if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                  continue;

              var parts = line.Split(',');
              if (parts.Length < 2)
                  continue;

              var name = parts[0].Trim();
              if (string.IsNullOrEmpty(name))
                  continue;

              if (int.TryParse(parts[1].Trim(), out var score))
              {
                  contributors.Add(new Contributor { Name = name, Score = score });
              }
          }

          if (contributors.Count == 0)
          {
              ShowEmptyState(listControl, emptyControl);
              return;
          }

          contributors = contributors
              .OrderByDescending(c => c.Score)
              .ThenBy(c => c.Name, StringComparer.Ordinal)
              .ToList();

          listControl.ItemsSource = contributors;
          listControl.Visibility = Visibility.Visible;
          emptyControl.Visibility = Visibility.Collapsed;
      }
      catch (IOException ex)
      {
          App.LogDebug("Contributors: failed to read {0}: {1}", fileName, ex.Message);
          ShowEmptyState(listControl, emptyControl);
      }
      catch (UnauthorizedAccessException ex)
      {
          App.LogDebug("Contributors: access denied {0}: {1}", fileName, ex.Message);
          ShowEmptyState(listControl, emptyControl);
      }
  }

  private static void ShowEmptyState(ItemsControl listControl, TextBlock emptyControl)
  {
      listControl.Visibility = Visibility.Collapsed;
      emptyControl.Visibility = Visibility.Visible;
  }
  ```

  **4. Add usings** (add if not already present):
  ```csharp
  using System.Linq;
  using System.Text;
  ```

  **Must NOT do**:
  - Don't use CsvHelper or any NuGet package
  - Don't display score in UI
  - Don't modify constructor signature
  - Don't add async (small files, synchronous is fine)

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocked By**: Task 1 (CSV files), Task 2 (localization), Task 3 (XAML names)

  **Acceptance Criteria**:
  - [ ] Both CSV files loaded independently via shared loader
  - [ ] One file's failure doesn't affect the other
  - [ ] `dotnet build` succeeds
  - [ ] Sort order: score descending, then name ascending

  **QA Scenarios**:
  ```
  Scenario: Build succeeds
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-4-build.txt
  ```

  **Commit**: YES (groups with task 2, 3)
  - Message: `feat: implement shared CSV loader for technical and financial contributors`
  - Files: `src/MantisZip.UI/Dialogs/AboutWindow.xaml.cs`

---

- [x] 5. Update tests and installer

  **What to do**:

  **Part A — Update AboutWindowTests.cs**:
  - Add 3 new keys to `ExpectedAboutKeys` array (after line 55, before the closing `];`):
    ```csharp
    "About_Contributors_Technical",
    "About_Contributors_Financial",
    "About_Contributors_None"
    ```
  - Bump minimum count from `21` to `24` (line 152):
    ```csharp
    Assert.True(count >= 24, $"About_* 键的数量 ({count}) 小于 24");
    ```

  **Part B — Update installer.iss**:
  - Add two lines in `[Files]` section (after the Resources/strings.*.json lines):
    ```
    Source: "publish_output\contributors-technical.csv"; DestDir: "{app}"; Flags: ignoreversion
    Source: "publish_output\contributors-financial.csv"; DestDir: "{app}"; Flags: ignoreversion
    ```

  **Must NOT do**:
  - Don't modify existing test cases
  - Don't change formatting of existing entries

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2
  - **Blocked By**: Task 2 (localization keys)

  **Acceptance Criteria**:
  - [ ] `ExpectedAboutKeys` has 24 entries (21 old + 3 new)
  - [ ] `AboutKeyCount_MeetsMinimum` checks `>= 24`
  - [ ] `installer.iss` includes both CSV files
  - [ ] `dotnet test` passes all tests

  **QA Scenarios**:
  ```
  Scenario: Verify test keys
    Tool: Bash
    Steps: Select-String "About_Contributors" in AboutWindowTests.cs
    Expected: 3 new keys found in ExpectedAboutKeys
    Evidence: .sisyphus/evidence/task-5-test-keys.txt

  Scenario: Count check updated
    Tool: Bash
    Steps: Select-String "count >= " in AboutWindowTests.cs
    Expected: Shows "count >= 24"
    Evidence: .sisyphus/evidence/task-5-count.txt

  Scenario: Installer includes both CSVs
    Tool: Bash
    Steps: Select-String "contributors-" in installer.iss
    Expected: Both technical and financial found
    Evidence: .sisyphus/evidence/task-5-installer.txt
  ```

  **Commit**: YES
  - Message: `test: update ExpectedAboutKeys (21→24); update installer.iss for both CSV files`
  - Files: `tests/MantisZip.Tests/AboutWindowTests.cs`, `installer.iss`

---

- [x] 6. Build and test verification

  **What to do**:
  1. Run `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` — verify 0 errors
  2. Run `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` — verify all tests pass
  3. Fix any compilation errors or test failures found

  **Must NOT do**:
  - Don't skip any failing tests
  - Don't modify tests to make them pass (fix the source code instead)

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocked By**: All previous tasks

  **Acceptance Criteria**:
  - [ ] `dotnet build` returns exit code 0 with no errors
  - [ ] `dotnet test` returns exit code 0 with all tests passing
  - [ ] No warnings about missing XML comments (or pre-existing only)

  **QA Scenarios**:
  ```
  Scenario: Full build succeeds
    Tool: Bash
    Preconditions: All source changes applied
    Steps:
      1. `dotnet build src\MantisZip.UI\MantisZip.UI.csproj 2>&1`
    Expected Result: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-6-build.txt

  Scenario: All tests pass
    Tool: Bash
    Preconditions: Build succeeds
    Steps:
      1. `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj 2>&1`
    Expected Result: All tests passed, 0 failures
    Evidence: .sisyphus/evidence/task-6-tests.txt
  ```

  **Commit**: NO (verify-only task)

---

## Final Verification Wave

> 4 review agents run in PARALLEL. ALL must APPROVE.

- [x] F1. **Plan Compliance Audit** — `oracle`
  - Both CSV files exist with correct encoding
  - CopyToOutputDirectory set for both in .csproj
  - All 3 localization keys in zh.json, en.json, L.cs
  - ExpectedAboutKeys has 24 entries
  - installer.iss has both CSV entries
  - **Must NOT have**: UI editor, score display, click handlers

- [x] F2. **Code Quality Review** — `unspecified-high`
  - `dotnet build` → PASS, `dotnet test` → PASS
  - No empty catches, no unused imports

- [x] F3. **Real Manual QA** — `unspecified-high`
  - Both files populated → both lists display correctly (Technical first, Financial second)
  - Delete technical CSV → technical shows empty, financial still shows
  - Delete both → both show "暂无贡献者"
  - 1 entry, 5 entries, 9 entries → WrapPanel wrapping behavior
  - Sorting: highest score first, alphabetical tiebreaker

- [x] F4. **Scope Fidelity Check** — `deep`
  - Each task's output matches its spec
  - No scope creep

---

## Commit Strategy

- **Commit 1** (Task 1): `feat: add contributors-technical.csv and contributors-financial.csv`
- **Commit 2** (Tasks 2, 3, 4 grouped): `feat: add technical and financial contributors panel to AboutWindow`
- **Commit 3** (Task 5): `test: update ExpectedAboutKeys (21→24); update installer.iss`

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
# Expected: Build succeeded. 0 warnings, 0 errors

dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
# Expected: Passed! - Failed: 0, Passed: N, Skipped: 0
```

### Final Checklist
- [x] Two CSV files exist with proper configuration (technical + financial)
- [x] 3 localization keys in all 3 files (zh.json, en.json, L.cs)
- [x] XAML with two WrapPanel sections (Technical + Financial)
- [x] Code-behind with shared CSV loader
- [x] Tests updated (ExpectedAboutKeys 24 entries, count >= 24)
- [x] Installer includes both CSV files
- [x] Build succeeds
- [x] All tests pass
