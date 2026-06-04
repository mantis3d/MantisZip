# 文件关联面板 — 扩展名级别设置

## TL;DR

> **Quick Summary**: 将设置窗口中的文件关联面板从统一开关改为逐扩展名的复选框列表，支持图标显示、当前关联程序检测、自定义扩展名添加，以及丰富的交互反馈。
>
> **Deliverables**:
> - 设置窗口「文件关联」Tab 全新 UI（ItemsControl + DataTemplate）
> - 每个扩展名独立的安装/卸载逻辑
> - 当前关联程序显示（读取注册表 UserChoice）
> - 自定义扩展名添加/删除功能
> - ShellIntegration 新增逐扩展名操作方法
> - 对应 xUnit 测试
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 3 waves
> **Critical Path**: AppSettings → ShellIntegration → UI → Tests

---

## Context

### Original Request
将设置窗口的文件关联面板从统一的开关改为针对每个扩展名的设置，参考其他解压缩软件的设计。

### Interview Summary
**Key Discussions**:
- 使用 `ItemsControl` + `DataTemplate` 展示复选框列表，每行显示图标、扩展名、描述、当前关联程序
- 图标来源：`SystemIconHelper.GetFileIcon(ext)`（已用于主窗口文件列表）
- 当前关联程序：读取 `HKCU\...\FileExts\{ext}\UserChoice\Progid` 映射为友好名称
- 自定义扩展名：弹出对话框输入 → 追加到列表末尾，带删除 (✕) 按钮
- 行点击切换勾选（不仅仅是点 CheckBox）
- 底部增加「打开默认应用」按钮（跳转 `ms-settings:defaultapps`）
- 按钮动态显示数量：「安装所选 (5)」
- 安装：只处理勾选的扩展名；卸载：始终清理全部

**Research Findings**:
- 7-Zip 使用表格列表，每个扩展名有 4 种状态切换
- WinRAR 使用分组复选框 + 自定义扩展名输入框
- 项目已有 `SystemIconHelper`（`SHGetFileInfo` + `ConcurrentDictionary` 缓存）
- 项目已有 7 个 `.ico` 文件在 `Resources/Icons/`
- 现有测试是 xUnit，8 个测试文件

### Metis Review
**Identified Gaps** (addressed):
- `.tar.gz / .tgz` 与 `.gz` 重叠 → 合并行只控制 `.tgz`，`.gz` 独立一行。`.tar.gz` 文件被 Windows 视为 `.gz` 扩展名，自然覆盖
- `.iso` 默认关联可能困惑用户 → 保留在列表但默认不勾选
- 右键菜单「打开默认应用设置」跨版本不稳定 → 改为底部「打开默认应用」按钮（`ms-settings:defaultapps`）
- 自定义扩展名上限 → 限制 20 个
- 旧版迁移 → 首次打开时检测 `AreAssociationsInstalled`，已安装则默认全勾

---

## Work Objectives

### Core Objective
实现设置窗口中可逐个扩展名控制文件关联的面板，包含完整的交互反馈和自定义扩展名支持。

### Concrete Deliverables
- 设置窗口「文件关联」Tab 全新 WPF UI
- `AppSettings` 新增 7 个 `Assoc*` 布尔属性 + `CustomAssocExtensions` 列表
- `ShellIntegration` 新增 `InstallAssociationForExtension`, `UninstallAssociationForExtension`, `IsExtensionAssociated`, `GetCurrentHandler` 方法
- `FormatAssocItem` 数据模型
- 本地化新增 15+ 个字符串键值
- xUnit 测试覆盖核心逻辑

### Definition of Done
- [ ] 所有 7 个内置扩展名可独立勾选/取消
- [ ] 自定义扩展名可通过对话框添加，带格式验证和去重
- [ ] 自定义扩展名可删除
- [ ] 每行显示系统文件类型图标
- [ ] 每行显示当前关联程序名称
- [ ] 点击行任意位置可切换勾选
- [ ] 「安装所选」按钮只处理勾选的扩展名，按钮文字显示数量
- [ ] 「卸载全部」按钮清理所有关联
- [ ] 「打开默认应用」按钮跳转系统设置
- [ ] 全选/取消全选按钮
- [ ] 状态栏显示正确
- [ ] 所有测试通过

### Must Have
- 逐扩展名安装/卸载
- 自定义扩展名添加/删除
- 系统图标显示
- 当前关联程序检测显示
- 行点击切换
- 按钮动态文字

### Must NOT Have (Guardrails)
- 不要修改 `ArchiveEngineFactory.SupportedExtensions`
- 不要修改上下文菜单面板（SettingsWindow 151-210 行）
- 不要写入 UserChoice 注册表键（只读）
- 不要尝试拖拽排序
- 不要移除现有 `Settings_Assoc_*` 本地化键（新增不删旧）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: YES (核心逻辑 TDD)
- **Framework**: xUnit

### QA Policy
Every task MUST include agent-executed QA scenarios.

- **Registry operations**: PowerShell (`Get-ItemProperty HKCU:\...`) verify OpenWithProgids
- **UI**: Verify XAML structure (compile check)
- **Library**: `dotnet test` for xUnit tests

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation):
├── Task 1: AppSettings — 新增属性和自定义扩展名存储
├── Task 2: ShellIntegration — 拆分为逐扩展名方法 + 当前处理器检测
└── Task 3: FormatAssocItem 数据模型

Wave 2 (UI + Localization):
├── Task 4: SettingsWindow.xaml — 新面板 UI
├── Task 5: SettingsWindow.xaml.cs — 加载/保存/交互逻辑
└── Task 6: 本地化字符串

Wave 3 (Tests + Final):
├── Task 7: xUnit 测试
└── Final Verification Wave
```

### Dependency Matrix
- **1-3**: Foundation — independent of each other, can run in parallel
- **4-6**: UI — depend on 1-3
- **7**: Tests — depend on 1-2 (requires ShellIntegration API to test against)

---

## TODOs

- [x] 1. AppSettings — 新增属性

  **What to do**:
  - 在 `AppSettings.cs` 中添加 7 个 `Assoc*` 布尔属性，默认值如下：
    ```csharp
    public bool AssocZip { get; set; } = true;
    public bool Assoc7z { get; set; } = true;
    public bool AssocRar { get; set; } = true;
    public bool AssocTar { get; set; } = true;
    public bool AssocTarGz { get; set; } = true;   // 控制 .tgz
    public bool AssocGz { get; set; } = true;       // 控制 .gz（也覆盖 .tar.gz）
    public bool AssocIso { get; set; } = false;     // ISO 默认不勾选
    ```
  - 添加自定义扩展名存储：
    ```csharp
    public List<string> CustomAssocExtensions { get; set; } = new();
    ```
  - 确保旧 `settings.json` 文件反序列化时兼容（`System.Text.Json` 默认忽略缺失属性即可正常工作）

  **Must NOT do**:
  - 不要修改现有属性的默认值
  - 不要使用 `[JsonRequired]`

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3)
  - **Blocks**: Tasks 4, 5
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/AppSettings.cs` — 现有属性模式（第 14-78 行），照此添加新属性
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:280` — `SupportedExtensions`，数据源

  **Acceptance Criteria**:
  - [ ] `dotnet build src/MantisZip.UI/MantisZip.UI.csproj` — 编译通过
  - [ ] `AppSettings.Instance.AssocZip == true`
  - [ ] `AppSettings.Instance.AssocIso == false`
  - [ ] `AppSettings.Instance.CustomAssocExtensions` is empty list, not null

  **QA Scenarios**:
  ```
  Scenario: 新属性默认值
    Tool: Bash (dotnet test / script)
    Preconditions: Fresh AppSettings instance
    Steps:
      1. Create new AppSettings()
      2. Verify AssocZip == true
      3. Verify AssocIso == false
      4. Verify CustomAssocExtensions is empty list
    Expected Result: All defaults correct
    Evidence: .sisyphus/evidence/task-1-defaults.json

  Scenario: 旧 JSON 兼容（缺失属性）
    Tool: Bash
    Preconditions: settings.json without new fields
    Steps:
      1. Write minimal JSON without Assoc* / CustomAssocExtensions
      2. Deserialize → new AppSettings with defaults
    Expected Result: No exception, defaults applied
    Evidence: .sisyphus/evidence/task-1-compat.json
  ```

  **Commit**: YES
  - Message: `feat(settings): add per-extension assoc properties to AppSettings`
  - Files: `src/MantisZip.UI/AppSettings.cs`

---

- [x] 2. ShellIntegration — 逐扩展名操作 + 当前处理器检测

  **What to do**:

  1. **新增 `InstallAssociationForExtension(string ext)`**（从 `InstallAssociations` 提取单扩展名逻辑）：
     - 写入 `{ext}\OpenWithProgids` 的 `MantisZip.Archive` 值（REG_NONE）
     - 写入 `{ext}\DefaultIcon`（调用 `GetIconPath(ext)`）
     - 跳过 `.tar.gz`（保持现有行为，由 `.gz` 覆盖）

  2. **新增 `UninstallAssociationForExtension(string ext)`**（从 `UninstallAssociations` 提取）：
     - 删除 `{ext}\OpenWithProgids` 的 `MantisZip.Archive` 值
     - 如果 `{ext}` 的 DefaultIcon 指向我们的图标则清理

  3. **修改 `InstallAssociations()`** — 保留向后兼容签名，内部遍历调用 `InstallAssociationForExtension`
  4. **修改 `UninstallAssociations()`** — 保留向后兼容签名
  5. **新增 `AreAssociationsInstalledForExtension(string ext)`**：
     - 检查 `{ext}\OpenWithProgids` 是否有 `MantisZip.Archive`
     - 返回 `bool`

  6. **新增 `GetInstalledExtensionCount()`**：
     - 遍历 `SupportedExtensions`，统计 `AreAssociationsInstalledForExtension` 为 true 的数量

  7. **新增 `GetCurrentHandler(string ext)`**：
     - 读取 `HKCU\...\FileExts\{ext}\UserChoice\Progid`
     - 如果 null → 返回 `"未设置"`
     - 如果包含 `"MantisZip"` → 返回 `"MantisZip"`
     - 如果是 `"Applications\xxx.exe"` 格式 → 提取文件名
     - 否则去掉格式后缀（如 `"Bandizip.zip"` → `"Bandizip"`）
     - 返回 `string`

  8. **修改 `AreAssociationsInstalled`** 属性 — 改为统计所有已安装格式数量，保持向后兼容

  **Must NOT do**:
  - 不要修改现有 `InstallAssociations()` / `UninstallAssociations()` 签名（有其他调用方）
  - 不要写入 UserChoice 键（只读）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3)
  - **Blocks**: Tasks 5, 7
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/ShellIntegration.cs` — 现有 InstallAssociations 方法（第 433-474 行），拆解
  - `src/MantisZip.UI/ShellIntegration.cs:418-427` — `AreAssociationsInstalled` 现有逻辑
  - `src/MantisZip.UI/ShellIntegration.cs:588-608` — `GetIconPath` 扩展名→图标映射
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:280` — `SupportedExtensions`

  **Acceptance Criteria**:
  - [ ] `dotnet build` — 编译通过
  - [ ] 安装单个扩展名后，对应 `OpenWithProgids` 出现 `MantisZip.Archive`
  - [ ] 卸载单个扩展名后，对应 `OpenWithProgids` 的 `MantisZip.Archive` 被移除
  - [ ] `GetCurrentHandler(".zip")` 返回非空字符串（不报错）
  - [ ] `AreAssociationsInstalledForExtension(".zip")` 正确反映实际状态
  - [ ] 现有调用方仍在工作

  **QA Scenarios**:
  ```
  Scenario: 安装单个扩展名
    Tool: Bash (PowerShell)
    Preconditions: Clean registry state
    Steps:
      1. Call InstallAssociationForExtension(".zip")
      2. Read HKCU:\Software\Classes\.zip\OpenWithProgids
      3. Assert MantisZip.Archive value exists
    Expected Result: Only .zip is associated
    Evidence: .sisyphus/evidence/task-2-install-single.txt

  Scenario: 卸载单个扩展名
    Tool: Bash
    Preconditions: .zip has MantisZip.Archive
    Steps:
      1. Call InstallAssociationForExtension(".zip")
      2. Call UninstallAssociationForExtension(".zip")
      3. Read HKCU:\Software\Classes\.zip\OpenWithProgids
      4. Assert MantisZip.Archive removed
    Expected Result: .zip no longer associated
    Evidence: .sisyphus/evidence/task-2-uninstall-single.txt

  Scenario: GetCurrentHandler returns string (no crash)
    Tool: Bash (script)
    Preconditions: Any Windows state
    Steps:
      1. Call GetCurrentHandler(".zip")
      2. Ensure returns non-null string
    Expected Result: Returns something (possibly "未设置")
    Evidence: .sisyphus/evidence/task-2-current-handler.txt

  Scenario: tar.gz is skipped
    Tool: Bash
    Preconditions: Clean state
    Steps:
      1. Call InstallAssociationForExtension(".tar.gz")
      2. Check OpenWithProgids for .tar.gz
    Expected Result: No entry created (skipped)
    Evidence: .sisyphus/evidence/task-2-targz-skip.txt
  ```

  **Commit**: YES (groups with Task 1)
  - Message: `feat(shell): add per-extension install/uninstall + current handler detection`
  - Files: `src/MantisZip.UI/ShellIntegration.cs`

---

- [x] 3. FormatAssocItem — 数据模型

  **What to do**:
  在 `SettingsWindow.xaml.cs` 中（或新建单独文件）添加内部数据类：

  ```csharp
  private class FormatAssocItem : INotifyPropertyChanged
  {
      public string Extension { get; init; } = "";        // 如 ".zip"
      public string Description { get; init; } = "";      // 如 "ZIP 压缩包"
      public ImageSource? Icon { get; set; }               // 从 SystemIconHelper 获取
      public string SettingsProperty { get; init; } = "";  // 对应 AppSettings 属性名
      public bool IsCustom { get; init; }                  // true=用户添加
      
      private bool _isEnabled;
      public bool IsEnabled
      {
          get => _isEnabled;
          set { _isEnabled = value; OnPropertyChanged(); }
      }

      private string _currentHandler = "";
      public string CurrentHandler
      {
          get => _currentHandler;
          set { _currentHandler = value; OnPropertyChanged(); }
      }

      public event PropertyChangedEventHandler? PropertyChanged;
      protected void OnPropertyChanged([CallerMemberName] string? name = null)
          => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }
  ```

  需要实现 `INotifyPropertyChanged` 以支持 UI 自动更新（`IsEnabled` 改变时按钮计数刷新，`CurrentHandler` 改变时界面刷新）。

  **Must NOT do**:
  - 不要放在单独文件中（保持与 SettingsWindow 一起，方便访问）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2)
  - **Blocks**: Task 4, 5
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/SettingsWindow.xaml.cs` — 添加到文件底部
  - `src/MantisZip.UI/SystemIconHelper.cs` — 图标来源

  **Acceptance Criteria**:
  - [ ] `dotnet build` — 编译通过
  - [ ] 属性变更触发 `PropertyChanged` 事件

  **QA Scenarios**:
  ```
  Scenario: INotifyPropertyChanged fires
    Tool: Bash (test)
    Preconditions: FormatAssocItem instance
    Steps:
      1. Subscribe to PropertyChanged
      2. Set IsEnabled = true
      3. Assert event fired
    Expected Result: Event fired with "IsEnabled"
    Evidence: .sisyphus/evidence/task-3-inpc.txt
  ```

  **Commit**: YES (groups with Tasks 1, 2)
  - Message: same commit
  - Files: `src/MantisZip.UI/SettingsWindow.xaml.cs`

---

- [x] 4. SettingsWindow.xaml — 新文件关联面板 UI

  **What to do**:
  替换文件关联 Tab（当前第 407-433 行）为新的 UI：

  ```xml
  <!-- ═══════ 文件关联 ═══════ -->
  <TabItem>
      <TabItem.Header>
          <!-- 保持现有的 emoji + 标题 -->
      </TabItem.Header>
      <ScrollViewer VerticalScrollBarVisibility="Auto" Background="{StaticResource Theme_SurfaceBg}">
          <StackPanel Margin="10">
              <GroupBox Header="{l:L Settings_Assoc_GroupHeader}" Padding="8" Margin="0,0,0,12">
                  <StackPanel>
                      <!-- 描述文字 -->
                      <TextBlock Text="{l:L Settings_Assoc_Desc}" TextWrapping="Wrap"
                                 Foreground="{StaticResource Theme_TextSecondary}"
                                 FontSize="12" Margin="0,0,0,10"/>

                      <!-- 全选/取消全选 -->
                      <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                          <Button x:Name="SelectAllBtn" Content="{l:L Settings_Assoc_SelectAll}"
                                  Width="80" Height="24" FontSize="12" Click="SelectAllBtn_Click"/>
                          <Button x:Name="DeselectAllBtn" Content="{l:L Settings_Assoc_DeselectAll}"
                                  Width="80" Height="24" FontSize="12" Margin="6,0,0,0"
                                  Click="DeselectAllBtn_Click"/>
                      </StackPanel>

                      <!-- 格式列表 -->
                      <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="260"
                                    BorderBrush="{StaticResource Theme_Border}"
                                    BorderThickness="1" CornerRadius="4">
                          <ItemsControl x:Name="AssocFormatList"
                                        Grid.IsSharedSizeScope="True"
                                        Margin="4">
                              <ItemsControl.ItemTemplate>
                                  <DataTemplate>
                                      <Grid Margin="2,5,2,5" 
                                            MouseLeftButtonUp="FormatRow_MouseLeftButtonUp">
                                          <Grid.ColumnDefinitions>
                                              <ColumnDefinition Width="Auto"/>
                                              <ColumnDefinition Width="20"/>
                                              <ColumnDefinition Width="Auto" SharedSizeGroup="Ext"/>
                                              <ColumnDefinition Width="*"/>
                                              <ColumnDefinition Width="Auto"/>
                                          </Grid.ColumnDefinitions>

                                          <CheckBox IsChecked="{Binding IsEnabled}"
                                                    VerticalAlignment="Center"/>

                                          <Image Grid.Column="1" Source="{Binding Icon}"
                                                 Width="16" Height="16"
                                                 VerticalAlignment="Center"/>

                                          <TextBlock Grid.Column="2"
                                                     Text="{Binding Extension}"
                                                     FontWeight="SemiBold"
                                                     VerticalAlignment="Center"
                                                     Margin="6,0,0,0" FontSize="13"/>

                                          <TextBlock Grid.Column="3"
                                                     Text="{Binding Description}"
                                                     Foreground="{StaticResource Theme_TextSecondary}"
                                                     VerticalAlignment="Center"
                                                     Margin="8,0,0,0" FontSize="12"
                                                     TextTrimming="CharacterEllipsis"/>

                                          <!-- 当前关联程序 -->
                                          <TextBlock Grid.Column="4"
                                                     Text="{Binding CurrentHandler}"
                                                     VerticalAlignment="Center"
                                                     FontSize="11"
                                                     Margin="8,0,4,0"/>

                                          <!-- 自定义扩展名的删除按钮 -->
                                          <Button Grid.Column="4"
                                                  x:Name="DeleteCustomBtn"
                                                  Content="✕"
                                                  Width="20" Height="20" FontSize="10"
                                                  VerticalAlignment="Center"
                                                  Margin="8,0,0,0"
                                                  Visibility="{Binding IsCustom, Converter={StaticResource BoolToVis}}"
                                                  Command="{Binding DeleteCommand}"
                                                  ToolTip="删除此自定义格式"/>
                                      </Grid>
                                  </DataTemplate>
                              </ItemsControl.ItemTemplate>
                          </ItemsControl>
                      </ScrollViewer>

                      <!-- 添加自定义扩展名按钮 -->
                      <Button x:Name="AddCustomBtn"
                              Content="{l:L Settings_Assoc_AddCustom}"
                              Width="160" Height="26"
                              HorizontalAlignment="Left" Margin="0,8,0,0"
                              Click="AddCustomBtn_Click"/>

                      <!-- 状态 -->
                      <TextBlock x:Name="AssocStatusText" Text="" FontSize="11"
                                 Foreground="{StaticResource Theme_TextSecondary}"
                                 Margin="0,8,0,0"/>

                      <!-- 底部按钮行 -->
                      <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
                          <Button x:Name="OpenDefaultAppsBtn"
                                  Content="{l:L Settings_Assoc_OpenDefaultApps}"
                                  Width="120" Height="28"
                                  Click="OpenDefaultAppsBtn_Click"/>
                          <Button x:Name="InstallAssocBtn"
                                  Content="{l:L Settings_Assoc_Install}"
                                  Width="100" Height="28" Margin="8,0,0,0"
                                  Click="InstallAssoc_Click"/>
                          <Button x:Name="UninstallAssocBtn"
                                  Content="{l:L Settings_Assoc_Uninstall}"
                                  Width="100" Height="28" Margin="8,0,0,0"
                                  Click="UninstallAssoc_Click"/>
                      </StackPanel>
                  </StackPanel>
              </GroupBox>
          </StackPanel>
      </ScrollViewer>
  </TabItem>
  ```

  注意：
  - 需要添加 `BoolToVis` 转换器到 Window.Resources（或将可见性绑定改为 Trigger）
  - `Grid.IsSharedSizeScope="True"` 保证列对齐
  - 行点击事件用 `MouseLeftButtonUp` 触发切换

  **Must NOT do**:
  - 不要修改其他 Tab 的 XAML
  - 不要移除旧的 `x:Name` 引用（`InstallAssocBtn`, `UninstallAssocBtn`, `AssocStatusText` 名称保留）

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (sequential with Task 5)
  - **Blocks**: Task 5
  - **Blocked By**: Tasks 1, 3

  **References**:
  - `src/MantisZip.UI/SettingsWindow.xaml` — 现有文件关联面板（第 407-433 行）
  - `src/MantisZip.UI/SettingsWindow.xaml` — 现有 GroupBox 样式（上下文菜单面板第 163-210 行）
  - `src/MantisZip.UI/SystemIconHelper.cs` — 图标来源

  **Acceptance Criteria**:
  - [ ] `dotnet build` — 编译通过
  - [ ] Tab 显示正确的 7 行内置格式 + 自定义扩展名行
  - [ ] 每行显示图标
  - [ ] 每行显示当前关联程序
  - [ ] 点击行任意位置切换勾选
  - [ ] 「全选」「取消全选」正常工作
  - [ ] 自定义扩展名行显示 ✕ 删除按钮，内置格式的不显示

  **QA Scenarios**:
  ```
  Scenario: UI 渲染正确
    Tool: dotnet build (compile check)
    Preconditions: XAML and C# files compiled together
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: No build errors
    Evidence: .sisyphus/evidence/task-4-build.txt

  Scenario: 点击行切换勾选
    Tool: dotnet build (compile + static analysis)
    Preconditions: XAML event wired to FormatRow_MouseLeftButtonUp
    Steps:
      1. Check FormatRow_MouseLeftButtonUp handler exists in .cs
      2. Manual code review: handler toggles the checkbox
    Expected Result: Handler exists and toggles
    Evidence: .sisyphus/evidence/task-4-row-click.txt
  ```

  **Commit**: YES (groups with Task 5)
  - Message: `feat(settings): new per-extension file association UI`
  - Files: `src/MantisZip.UI/SettingsWindow.xaml`

---

- [x] 5. SettingsWindow.xaml.cs — 加载/保存/交互逻辑

  **What to do**:

  1. **`LoadSettings()`** 中扩展文件关联加载逻辑：
     - 清空 `AssocFormatList.Items`，为每个内置扩展名创建 `FormatAssocItem`
     - 从 `AppSettings` 读取对应 `Assoc*` 属性设置初始勾选状态
     - 调用 `ShellIntegration.GetCurrentHandler(ext)` 填充当前关联程序
     - 从 `AppSettings.CustomAssocExtensions` 读取自定义扩展名列表并添加

  2. **`SaveSettings()`** 中保存新属性

  3. **新增方法**：
     - `PopulateAssocList()` — 构建并刷新列表
     - `SelectAllBtn_Click` / `DeselectAllBtn_Click` — 全选/取消全选
     - `FormatRow_MouseLeftButtonUp` — 行点击切换勾选
     - `AddCustomBtn_Click` — 弹出对话框添加自定义扩展名
     - `DeleteCustomExtension(FormatAssocItem item)` — 删除自定义扩展名
     - `OpenDefaultAppsBtn_Click` — `Process.Start("ms-settings:defaultapps")`
     - `UpdateAssocButtonState()` — 更新安装按钮文字和启用状态
     - `UpdateAssocStatus()` — 更新状态文本（已安装 N/M 个）

  4. **自定义扩展名添加对话框**：
     - 简单的 `customInputDialog`（或用 `Microsoft.VisualBasic.Interaction.InputBox` 风格）
     - 输入验证：必须以 `.` 开头，长度 ≤ 10，不重复（内置 + 已有自定义），≤ 20 个上限
     - 格式规范化：去空格、转小写、补 `.` 前缀

  5. **迁移逻辑**：`LoadSettings` 中，如果旧 `ShellIntegration.AreAssociationsInstalled` 为 true 但新属性都是默认值，则全部勾选（兼容首次升级）

  **Must NOT do**:
  - 不要修改现有的 `LoadSettings` / `SaveSettings` 的非关联部分

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (after Task 4)
  - **Blocks**: Task 6 (localization references method names)
  - **Blocked By**: Tasks 1, 2, 3, 4

  **References**:
  - `src/MantisZip.UI/SettingsWindow.xaml.cs` — 现有 `UpdateAssocStatus`（第 289-297 行）、`InstallAssoc_Click`（第 299-315 行）、`UninstallAssoc_Click`（第 317-332 行）
  - `src/MantisZip.UI/SettingsWindow.xaml.cs` — `LoadSettings`（第 71-178 行）、`SaveSettings`（第 181-251 行）
  - `src/MantisZip.UI/SystemIconHelper.cs` — `GetFileIcon(string extension)`

  **Acceptance Criteria**:
  - [ ] 打开设置 → 文件关联 Tab → 显示 7 个内置格式
  - [ ] 勾选变化时安装按钮文字动态更新
  - [ ] 点击「安装所选」→ 只安装勾选的格式
  - [ ] 点击「卸载全部」→ 清理所有
  - [ ] 点击「打开默认应用」→ 打开系统设置
  - [ ] 添加自定义扩展名 → 出现在列表，可勾选，可删除
  - [ ] 重复扩展名被拒绝
  - [ ] 超过 20 个时被拒绝
  - [ ] 保存设置后重新打开 → 勾选状态持久化

  **QA Scenarios**:
  ```
  Scenario: 打开面板显示格式列表
    Tool: dotnet run (Windows) — 编译后检查 UI 不崩溃
    Preconditions: App compiled
    Steps:
      1. Build and launch
      2. Open Settings → File Assoc tab
      3. Verify no crash, 7 items visible
    Expected Result: Panel renders
    Evidence: .sisyphus/evidence/task-5-open-panel.txt

  Scenario: 安装所选扩展名
    Tool: Bash (PowerShell)
    Preconditions: Clean registry
    Steps:
      1. Check .7z only
      2. Click Install
      3. Check HKCU:\Software\Classes\.7z\OpenWithProgids
      4. Check HKCU:\Software\Classes\.zip\OpenWithProgids (should NOT have MantisZip)
    Expected Result: Only .7z installed
    Evidence: .sisyphus/evidence/task-5-install-selected.txt

  Scenario: 添加自定义扩展名
    Tool: Simulated (verify dialog logic in code)
    Preconditions: CustomAssocExtensions empty
    Steps:
      1. Input ".zipx"
      2. Verify added to list
      3. Input ".zip" (duplicate with built-in)
      4. Verify rejected
      5. Input ".001" x 21 times
      6. Verify 21st rejected (max 20)
    Expected Result: Validation works
    Evidence: .sisyphus/evidence/task-5-custom-add.txt
  ```

  **Commit**: YES (groups with Task 4)
  - Message: same commit
  - Files: `src/MantisZip.UI/SettingsWindow.xaml.cs`

---

- [x] 6. 本地化字符串

  **What to do**:
  在 `L.cs` 及 `strings.zh.json` / `strings.en.json` 中添加新键值：

  **L.cs 新增键**：
  ```csharp
  // 文件关联（新增）
  public const string Settings_Assoc_SelectAll           = "Settings_Assoc_SelectAll";
  public const string Settings_Assoc_DeselectAll         = "Settings_Assoc_DeselectAll";
  public const string Settings_Assoc_AddCustom           = "Settings_Assoc_AddCustom";
  public const string Settings_Assoc_OpenDefaultApps     = "Settings_Assoc_OpenDefaultApps";
  public const string Settings_Assoc_FormatDesc_Zip      = "Settings_Assoc_FormatDesc_Zip";
  public const string Settings_Assoc_FormatDesc_7z       = "Settings_Assoc_FormatDesc_7z";
  public const string Settings_Assoc_FormatDesc_Rar      = "Settings_Assoc_FormatDesc_Rar";
  public const string Settings_Assoc_FormatDesc_Tar      = "Settings_Assoc_FormatDesc_Tar";
  public const string Settings_Assoc_FormatDesc_TarGz    = "Settings_Assoc_FormatDesc_TarGz";
  public const string Settings_Assoc_FormatDesc_Gz       = "Settings_Assoc_FormatDesc_Gz";
  public const string Settings_Assoc_FormatDesc_Iso      = "Settings_Assoc_FormatDesc_Iso";
  public const string Settings_Assoc_StatusText          = "Settings_Assoc_StatusText";
  public const string Settings_Assoc_InstallBtnText      = "Settings_Assoc_InstallBtnText";
  public const string Settings_Assoc_CustomInputTitle    = "Settings_Assoc_CustomInputTitle";
  public const string Settings_Assoc_CustomInputPrompt   = "Settings_Assoc_CustomInputPrompt";
  public const string Settings_Assoc_CustomAlreadyExists = "Settings_Assoc_CustomAlreadyExists";
  public const string Settings_Assoc_CustomMaxReached    = "Settings_Assoc_CustomMaxReached";
  public const string Settings_Assoc_CustomInvalid       = "Settings_Assoc_CustomInvalid";
  public const string Settings_Assoc_CurrentHandler_None = "Settings_Assoc_CurrentHandler_None";
  public const string Settings_Assoc_CurrentHandler_Ours = "Settings_Assoc_CurrentHandler_Ours";
  public const string Settings_Assoc_UserCustom          = "Settings_Assoc_UserCustom";
  ```

  **中英文 JSON 各添加对应翻译**。

  **Must NOT do**:
  - 不要修改或删除已有的 `Settings_Assoc_*` 键

  **Recommended Agent Profile**:
  - **Category**: `writing`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 4, 5)
  - **Blocks**: None (final merge)
  - **Blocked By**: None (keys are independent)

  **References**:
  - `src/MantisZip.UI/Localization/L.cs` — 现有 `Settings_Assoc_*` 键（第 582-591 行）
  - `src/MantisZip.UI/Resources/strings.zh.json` — 中文字符串
  - `src/MantisZip.UI/Resources/strings.en.json` — 英文字符串

  **Acceptance Criteria**:
  - [ ] `dotnet build` — 编译通过
  - [ ] 所有新键在 zh.json 和 en.json 中都有对应翻译

  **QA Scenarios**:
  ```
  Scenario: 所有新键都有翻译
    Tool: Bash (script)
    Preconditions: L.cs and both JSON files
    Steps:
      1. Parse L.cs for Settings_Assoc_* new keys
      2. Check zh.json has all keys
      3. Check en.json has all keys
    Expected Result: All keys present in both languages
    Evidence: .sisyphus/evidence/task-6-localization.txt
  ```

  **Commit**: YES (groups with Tasks 4, 5)
  - Message: same commit
  - Files: `src/MantisZip.UI/Localization/L.cs`, `src/MantisZip.UI/Resources/strings.zh.json`, `src/MantisZip.UI/Resources/strings.en.json`

---

- [x] 7. xUnit 测试

  **What to do**:
  在 `tests/MantisZip.Tests/` 中新增测试文件 `ShellIntegrationAssocTests.cs`：

  ```csharp
  public class ShellIntegrationAssocTests
  {
      // 注：注册表操作需要实际运行在 Windows 上，测试时忽略非 Windows 平台

      // ── InstallAssociationForExtension ──

      [Fact]
      public void Install_SingleExtension_WritesOpenWithProgids()
      {
          // 安装 .zip
          // 验证 HKCU\...\.zip\OpenWithProgids 有 MantisZip.Archive
          // cleanup
      }

      [Fact]
      public void Install_SkipTarGz_DoesNotWrite()
      {
          // 尝试安装 .tar.gz
          // 验证没有写入
      }

      [Fact]
      public void Install_MultipleExtensions_AllWritten()
      {
          // 安装 .zip + .7z
          // 验证两个都有
      }

      // ── UninstallAssociationForExtension ──

      [Fact]
      public void Uninstall_SingleExtension_RemovesOpenWithProgids()
      {
          // 先安装 .zip，再卸载
          // 验证已移除
      }

      // ── IsExtensionAssociated ──

      [Fact]
      public void IsAssociated_WhenInstalled_ReturnsTrue()
      {
          // 安装后检查
      }

      [Fact]
      public void IsAssociated_WhenUninstalled_ReturnsFalse()
      {
          // 卸载后检查
      }

      // ── GetCurrentHandler ──

      [Fact]
      public void GetCurrentHandler_ReturnsString()
      {
          var handler = ShellIntegration.GetCurrentHandler(".zip");
          Assert.NotNull(handler);
      }

      // ── Custom Extension Validation ──

      [Fact]
      public void NormalizeCustomExtension_AddsDot()
      {
          // "zipx" → ".zipx"
      }

      [Fact]
      public void NormalizeCustomExtension_Lowercases()
      {
          // ".ZIPX" → ".zipx"
      }

      [Fact]
      public void CustomExtension_Duplicate_Rejected()
      {
          // ".zip" 已在内置列表中 → 拒绝
      }
  }
  ```

  需要 `[Collection("RegistryTests")]` 确保注册表测试串行执行（避免并发冲突）。

  **Must NOT do**:
  - 不要修改现有测试文件
  - 注册表测试用完后必须 cleanup

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 3
  - **Blocks**: Final verification
  - **Blocked By**: Tasks 1, 2

  **References**:
  - `tests/MantisZip.Tests/` — 现有测试结构和模式
  - `src/MantisZip.UI/ShellIntegration.cs` — 被测方法

  **Acceptance Criteria**:
  - [ ] `dotnet test tests/MantisZip.Tests/` — 全部通过
  - [ ] 至少 8 个测试用例
  - [ ] 注册表操作后有 cleanup

  **QA Scenarios**:
  ```
  Scenario: 所有测试通过
    Tool: Bash
    Preconditions: Windows, compiled solution
    Steps:
      1. dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
      2. Check output for passed tests
    Expected Result: All tests pass
    Evidence: .sisyphus/evidence/task-7-test-results.txt
  ```

  **Commit**: YES
  - Message: `test(shell): add ShellIntegration association tests`
  - Files: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `oracle`
  For each task: verify implementation against spec. Check all files referenced in the plan exist and are modified correctly.
  Output: `Tasks [N/N] | Must Have [N/N] | Must NOT Have [N/N] | VERDICT`

- [x] F2. **Code Quality Review** — `unspecified-high`
  `dotnet build` — no errors. Code review for: empty catches, `as any`/`@ts-ignore` equivalents, unused variables, commented-out code.
  Output: `Build [PASS/FAIL] | Code Review [N issues] | VERDICT`

- [x] F3. **Real Manual QA** — `unspecified-high`
  On Windows: Open Settings → File Assoc tab. Toggle extensions, install, uninstall, add custom, verify registry.
  Output: `Scenarios [N/N pass] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  Compare each task's "What to do" against actual commits. Verify no scope creep.
  Output: `Tasks [N/N compliant] | Creep [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

- **1-3**: `feat(settings): add per-extension assoc properties + ShellIntegration methods + data model`
- **4-6**: `feat(settings): new per-extension file association UI + localization`
- **7**: `test(shell): add ShellIntegration association tests`

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj   # 编译通过
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj  # 所有测试通过
```

### Final Checklist
- [ ] 7 个内置扩展名可独立勾选
- [ ] 每行显示系统文件类型图标
- [ ] 每行显示当前关联程序
- [ ] 点击行切换勾选
- [ ] 全选/取消全选
- [ ] 自定义扩展名添加/删除（上限 20）
- [ ] 安装所选只处理勾选扩展名
- [ ] 卸载全部清理所有
- [ ] 打开默认应用按钮
- [ ] 状态显示正确
- [ ] 所有测试通过
