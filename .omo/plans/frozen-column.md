# 冻结列功能

## TL;DR

> **Quick Summary**: 为文件列表 DataGrid 添加「冻结列」功能——水平滚动时，指定列保持可见不动。使用 WPF DataGrid 原生 `FrozenColumnCount` 属性实现。
>
> **Deliverables**:
> - DataGrid 支持右键列标题 → 冻结至当前列 / 取消冻结
> - 冻结列与滚动列之间有一条浅色竖线分隔
> - FrozenColumnCount 持久化到 window.json（跨会话保持）
> - 字符串本地化（中/英）
>
> **Estimated Effort**: Quick
> **Parallel Execution**: NO — all tasks are in a single file-set, sequential
> **Critical Path**: 字符串 → XAML + 菜单逻辑 → 分隔线 → 持久化 → 测试

---

## Context

### Original Request
用户想在主窗口文件列表（FileListGrid DataGrid）添加「冻结列」功能，类似 Excel 的冻结窗格——水平滚动时，指定列固定不动始终可见。

### Interview Summary
**Key Decisions**:
- **冻结逻辑**: 右键列标题 →「冻结至当前列」→ 冻结该列及左侧所有列（FrozenColumnCount = DisplayIndex + 1）
- **取消冻结**: 右键已冻结列 →「取消冻结」→ 从此列向右取消，左侧保持冻结（FrozenColumnCount = 该列 DisplayIndex）
- **默认**: 不冻结（FrozenColumnCount = 0）
- **视觉分隔线**: 需要一条浅色竖线分隔冻结区和滚动区
- **测试**: 需要（序列化方向）

**Technical Environment**:
- FileListGrid 是 WPF DataGrid（不是 ListView）
- 列：名称、大小、压缩后、比率、CRC32、日期、加密（7列）
- `CanUserReorderColumns="False"`，`RowHeaderWidth="0"`
- 已有列标题右键菜单（ColumnHeaderContextMenu）控制列显隐
- 窗口设置持久化到 `%LOCALAPPDATA%\MantisZip\window.json`
- 本地化通过 L.cs + strings.zh.json / strings.en.json

### Metis Review
**Identified Gaps** (addressed):
- 分隔线位置需要在列拖拽调整宽度时更新 ✓（通过 LayoutUpdated 事件节流处理）
- 分隔线需要在冻结区内的列显隐切换时更新 ✓（同一机制处理）
- 分隔线颜色需绑 DynamicResource 支持运行时主题切换 ✓
- FrozenColumnCount 不可超过 Columns.Count - 1 ✓（设上限）
- 冻结状态跨会话/跨存档自动保持 ✓（通过 window.json 持久化）
- L.cs 修改方式 ✓（手动添加常量，无自动生成脚本）
- 取消冻结语义 ✓（用户确认：从此列向右取消）

---

## Work Objectives

### Core Objective
为 FileListGrid DataGrid 添加基于 FrozenColumnCount 的列冻结功能。

### Concrete Deliverables
- DataGrid 列标题右键菜单增加「冻结至当前列」/「取消冻结」项
- 冻结/滚动区分隔线（overlay Border）
- FrozenColumnCount 持久化到 window.json
- 中英文本地化字符串
- 序列化方向单元测试

### Definition of Done
- [ ] 右键非冻结列 →「冻结至当前列」→ 该列及左侧列冻结
- [ ] 右键已冻结列 →「取消冻结」→ 从此列向右取消冻结
- [ ] 分隔线在冻结/滚动列之间正确显示
- [ ] 列拖拽宽度后分隔线位置更新
- [ ] 列显隐切换后分隔线位置更新
- [ ] 关闭/重启窗口后冻结状态保持
- [ ] 切换主题后分隔线颜色跟随
- [ ] `dotnet test` 通过

### Must Have
- 右键菜单正确显示「冻结至当前列」或「取消冻结」
- FrozenColumnCount 设置生效（列在水平滚动时不滚动）
- 分隔线可见且定位正确
- 设置持久化
- 中英文显示

### Must NOT Have
- 不改变现有的列排序、显隐、宽度功能
- 不引入第三方依赖
- 不改动 ArchiveItem 或数据模型
- 不新增 XAML 文件，只改现有文件

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: YES (after)
- **Framework**: xUnit
- **Test type**: 序列化 round-trip 验证

### QA Policy
Every task includes agent-executed QA scenarios. Evidence saved to `.omo/evidence/task-{N}-{scenario-slug}.{ext}`.

- **UI/DataGrid**: Build + run app, verify via code analysis + window.json
- **Unit tests**: `dotnet test` 
- **Serialization**: Read/Write window.json, verify frozenField value

---

## Execution Strategy

This is a straightforward feature with no parallel execution needed — all changes touch the same set of files.

```
Task 1: 字符串资源 + 常量（strings.zh.json, strings.en.json, L.cs）
Task 2: XAML 分隔线（MainWindow.xaml）+ DataGrid FrozenColumnCount
Task 3: 右键菜单冻结项 + 事件处理（MainWindow.UI.cs）
Task 4: 分隔线定位 UpdateFrozenSeparator（MainWindow.xaml.cs）
Task 5: 持久化 Save/Load（MainWindow.xaml.cs）
Task 6: 单元测试
```

No blocking dependencies — each task builds on the previous.

---

## TODOs

- [ ] 1. 新增字符串资源（中英文 + L.cs 常量）

  **What to do**:
  - 在 `strings.zh.json` 和 `strings.en.json` 中添加两个新条目：
    - `"Main_Col_FreezeToHere"` → "冻结至当前列" / "Freeze to This Column"
    - `"Main_Col_Unfreeze"` → "取消冻结" / "Unfreeze Column"
  - 在 `L.cs` 中添加对应常量，放在 `Main_Col_Encrypted` 下方（字母序区域）：
    - `public const string Main_Col_FreezeToHere = "Main_Col_FreezeToHere";`
    - `public const string Main_Col_Unfreeze = "Main_Col_Unfreeze";`

  **Must NOT do**:
  - 不要修改已有条目的值

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 简单的字符串编辑，无需复杂逻辑
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Blocks**: Task 3 (菜单项需要字符串常量)
  - **Blocked By**: None

  **References**:
  - `strings.zh.json:172-180` — 现有 Main_Col_* 和 Main_Ctx_* 条目格式
  - `strings.en.json:170-178` — 英文对应条目格式
  - `L.cs:210-212` — Main_Col_* 常量定义位置和格式

  **Acceptance Criteria**:
  - [ ] strings.zh.json 包含 `Main_Col_FreezeToHere` 和 `Main_Col_Unfreeze`
  - [ ] strings.en.json 包含对应条目
  - [ ] L.cs 包含两条新常量
  - [ ] `dotnet build` 通过

  **QA Scenarios**:
  ```
  Scenario: 验证字符串文件格式正确
    Tool: Bash
    Preconditions: 文件已修改
    Steps:
      1. `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` → 编译成功
    Expected Result: exit code 0, no build errors
    Evidence: .omo/evidence/task-1-build.txt

  Scenario: 验证字符串内容
    Tool: Bash
    Preconditions: 文件已修改
    Steps:
      1. `Select-String "Main_Col_FreezeToHere" src\MantisZip.UI\Resources\strings.zh.json` → 找到匹配
      2. `Select-String "Main_Col_FreezeToHere" src\MantisZip.UI\Resources\strings.en.json` → 找到匹配
      3. `Select-String "Main_Col_FreezeToHere" src\MantisZip.UI\Localization\L.cs` → 找到匹配
    Expected Result: 所有搜索均返回匹配结果
    Evidence: .omo/evidence/task-1-strings.txt
  ```

  **Commit**: YES
  - Message: `feat(l10n): add freeze column string resources`
  - Files: `src/MantisZip.UI/Resources/strings.zh.json`, `src/MantisZip.UI/Resources/strings.en.json`, `src/MantisZip.UI/Localization/L.cs`

---

- [ ] 2. 添加 FrozenColumn 视觉分隔线（MainWindow.xaml）

  **What to do**:
  - 在 MainWindow.xaml 中找到 DataGrid 所在的 Grid（`Grid.Row="1" Grid.Column="2"`）
  - DataGrid 已经在这个 Grid 中（与 NoResultsText 共存），不需要额外包装
  - 在 DataGrid 之后添加一个 Border 元素作为冻结列分隔线：
    ```xml
    <Border x:Name="FrozenColumnSeparator"
            Width="2"
            Background="{DynamicResource Theme_Border}"
            IsHitTestVisible="False"
            Visibility="Collapsed"
            HorizontalAlignment="Left"/>
    ```
  - **关键**: 使用 `DynamicResource`（不是 `StaticResource`）以支持运行时主题切换

  **Must NOT do**:
  - 不要改变现有 Grid 的布局结构
  - 不要设置 Height 固定值（随 DataGrid 高度动态变化）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单文件单元素修改
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (与 Task 1 无依赖)
  - **Blocks**: Task 4 (分隔线定位需要 Border 元素)
  - **Blocked By**: None

  **References**:
  - `MainWindow.xaml:420-587` — DataGrid 所在的 Grid 结构
  - `MainWindow.xaml:20-40` — 现有主题资源引用模式（DynamicResource Theme_Border 等）

  **Acceptance Criteria**:
  - [ ] XAML 中包含 `x:Name="FrozenColumnSeparator"` 的 Border 元素
  - [ ] `Background` 绑定 `DynamicResource Theme_Border`
  - [ ] `IsHitTestVisible="False"`
  - [ ] `Visibility="Collapsed"`（默认隐藏）
  - [ ] `dotnet build` 通过

  **QA Scenarios**:
  ```
  Scenario: 验证 XAML 编译和元素存在
    Tool: Bash
    Preconditions: 任务 1 已完成
    Steps:
      1. `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` → 编译成功
      2. `Select-String "FrozenColumnSeparator" src\MantisZip.UI\MainWindow\MainWindow.xaml` → 找到定义
    Expected Result: 编译通过，元素定义存在
    Evidence: .omo/evidence/task-2-xaml.txt
  ```

  **Commit**: YES (groups with Task 3-5)
  - Message: `feat(ui): add frozen column separator overlay to file list DataGrid`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.xaml`

---

- [ ] 3. 冻结列右键菜单（MainWindow.UI.cs）

  **What to do**:
  - 在 `ColumnHeaderContextMenu_Opened` 方法中（约第883行），在循环结束后：
    1. 添加 `Separator` 菜单项
    2. 获取当前右键所点击的列：
       - 通过 `ContextMenu.PlacementTarget` 获取 `DataGridColumnHeader`
       - 从 header 获取对应的 `DataGridColumn`
       - 判断其 `DisplayIndex` 是否 < `FileListGrid.FrozenColumnCount`
    3. 如果列在冻结区内（DisplayIndex < FrozenColumnCount）：
       - 添加「取消冻结」菜单项
       - Header = `L.T(L.Main_Col_Unfreeze)` + 列名，如 "取消冻结「名称」"
    4. 如果列不在冻结区内（DisplayIndex >= FrozenColumnCount）：
       - 添加「冻结至当前列」菜单项
       - Header = `L.T(L.Main_Col_FreezeToHere)` + 列名，如 "冻结至当前列「大小」"
    5. 创建 `ColumnFreezeMenuItem_Click` 事件处理方法：
       - 判断点击的是冻结还是取消冻结
       - 冻结: `FrozenColumnCount = clickedColumn.DisplayIndex + 1`
       - 取消冻结: `FrozenColumnCount = clickedColumn.DisplayIndex`
       - **上限 clamp**: `Math.Min(FrozenColumnCount, FileListGrid.Columns.Count)`
       - **下限 clamp**: `Math.Max(FrozenColumnCount, 0)`
       - 调用 `UpdateFrozenSeparator()`

  **细节**:
  - 获取当前右键列的方式：`(menu.PlacementTarget as DataGridColumnHeader)?.Column`
  - 菜单项用 emoji ❄️ 作为图标（冻结状态），半透明/不透明逻辑与显隐菜单一致
  - 注意：列名要取原始文字（去掉排序标记 ▲▼），与显隐菜单项一致

  **Must NOT do**:
  - 不要删除或修改现有的列显隐逻辑
  - FrozenColumnCount 不要设为负值

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单个方法内新增逻辑，结构简单
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: None
  - **Blocked By**: Task 1 (需要字符串常量)

  **References**:
  - `MainWindow.UI.cs:883-943` — `ColumnHeaderContextMenu_Opened` 和 `ColumnVisibilityMenuItem_Click`，粘帖此模式的完整代码
  - `MainWindow.xaml.cs:30-40` — `FileListGrid` 声明

  **Acceptance Criteria**:
  - [ ] 列标题右键菜单在分隔线后有冻结/取消冻结项
  - [ ] 冻结项点击后设置 FrozenColumnCount 正确
  - [ ] 非冻结列显示「冻结至当前列」
  - [ ] 已冻结列显示「取消冻结」
  - [ ] `dotnet build` 通过

  **QA Scenarios**:
  ```
  Scenario: 验证冻结菜单项逻辑
    Tool: Bash
    Preconditions: 代码已修改，编译通过
    Steps:
      1. 使用 ast_grep_search 验证菜单逻辑代码存在:
         ast_grep_search "ColumnFreezeMenuItem_Click" --lang csharp
    Expected Result: 找到事件处理方法的定义
    Evidence: .omo/evidence/task-3-menu.txt
  ```

  **Commit**: YES (groups with Task 2, 4, 5)
  - Message: `feat(ui): add freeze column context menu to DataGrid headers`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.UI.cs`

---

- [ ] 4. 分隔线定位和更新逻辑（MainWindow.xaml.cs）

  **What to do**:
  - 在 `MainWindow.xaml.cs` 的 `MainWindow` 分部类中添加以下方法：

  ```csharp
  private void UpdateFrozenSeparator()
  {
      if (FileListGrid.FrozenColumnCount <= 0)
      {
          FrozenColumnSeparator.Visibility = Visibility.Collapsed;
          return;
      }

      // 获取最后一个可见的冻结列
      var lastFrozen = FileListGrid.Columns
          .Where(c => c.Visibility == Visibility.Visible)
          .OrderBy(c => c.DisplayIndex)
          .Take(FileListGrid.FrozenColumnCount)
          .LastOrDefault();

      if (lastFrozen == null)
      {
          FrozenColumnSeparator.Visibility = Visibility.Collapsed;
          return;
      }

      // 通过列标题头计算右边缘位置
      var colIndex = FileListGrid.Columns.IndexOf(lastFrozen);
      var header = GetColumnHeader(FileListGrid, colIndex);
      if (header != null)
      {
          var point = header.TranslatePoint(new Point(header.ActualWidth, 0), FileListGrid);
          FrozenColumnSeparator.Margin = new Thickness(point.X - 1, 0, 0, 0); // -1 让分隔线居中在边界
          FrozenColumnSeparator.Height = FileListGrid.ActualHeight;
          FrozenColumnSeparator.Visibility = Visibility.Visible;
      }
      else
      {
          FrozenColumnSeparator.Visibility = Visibility.Collapsed;
      }
  }

  private DataGridColumnHeader? GetColumnHeader(DataGrid grid, int columnIndex)
  {
      // 视觉树查找 DataGridColumnHeadersPresenter 下的 ColumnHeader
      var presenter = FindVisualChild<DataGridColumnHeadersPresenter>(grid);
      if (presenter == null) return null;

      for (int i = 0; i < VisualTreeHelper.GetChildrenCount(presenter); i++)
      {
          if (VisualTreeHelper.GetChild(presenter, i) is DataGridColumnHeader header
              && grid.Columns.IndexOf(header.Column) == columnIndex)
          {
              return header;
          }
      }
      return null;
  }

  private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
  {
      for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
      {
          var child = VisualTreeHelper.GetChild(parent, i);
          if (child is T t) return t;
          var result = FindVisualChild<T>(child);
          if (result != null) return result;
      }
      return null;
  }
  ```

  - 在构造函数中添加事件订阅：
    ```csharp
    FileListGrid.LayoutUpdated += (s, e) => UpdateFrozenSeparator();
    ```
    > 使用 `LayoutUpdated` 是因为列拖拽宽度、显隐切换、DataGrid 大小变化等都会触发它。
    > 由于只会在 `FrozenColumnCount > 0` 时做实际工作，性能开销可忽略。

  - 确保 `using System.Windows.Controls.Primitives;` 存在（DataGridColumnHeader 所在命名空间）
  - 如果需要，添加 `using System.Windows.Media;`

  **Must NOT do**:
  - 不要给 FrozenColumnSeparator 设固定 Height
  - 不要在 LayoutUpdated 中做高开销操作（已经只会在冻结生效时更新）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 新增辅助方法，逻辑清晰
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: None
  - **Blocked By**: Task 2 (需要 Border 元素 x:Name)

  **References**:
  - `MainWindow.xaml.cs:80-90` — 构造函数，添加 LayoutUpdated 订阅
  - `MainWindow.xaml.cs:30-40` — 文件顶部 using 区域

  **Acceptance Criteria**:
  - [ ] `UpdateFrozenSeparator()` 方法存在
  - [ ] `GetColumnHeader()` 辅助方法存在
  - [ ] `FindVisualChild<T>()` 辅助方法存在
  - [ ] FrozenColumnCount > 0 时分隔线可见，=0 时隐藏
  - [ ] `dotnet build` 通过

  **QA Scenarios**:
  ```
  Scenario: 验证 UpdateFrozenSeparator 方法存在
    Tool: Bash
    Preconditions: 代码已修改
    Steps:
      1. ast_grep_search "UpdateFrozenSeparator" --lang csharp
    Expected Result: 找到方法定义
    Evidence: .omo/evidence/task-4-separator.txt
  ```

  **Commit**: YES (groups with Task 2, 3, 5)
  - Message: `feat(ui): implement frozen column separator positioning logic`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs`

---

- [ ] 5. 冻结状态持久化（MainWindow.xaml.cs）

  **What to do**:
  - 在 `WindowSize` 类中添加属性：
    ```csharp
    public int FrozenColumnCount { get; set; }
    ```
  - 在 `SaveWindowSettings()` 方法中（约第271行），在列状态循环之后添加：
    ```csharp
    columnStatesObj.FrozenColumnCount = FileListGrid.FrozenColumnCount;
    ```
    （根据实际的序列化变量名调整）
  - 在 `LoadWindowSettings()` 方法中（约第176行），在列状态恢复之后添加：
    ```csharp
    if (obj?.FrozenColumnCount > 0)
    {
        FileListGrid.FrozenColumnCount = Math.Min(obj.FrozenColumnCount, FileListGrid.Columns.Count);
    }
    ```
  - 在设置 FrozenColumnCount 后调用 `UpdateFrozenSeparator()`

  **Must NOT do**:
  - 不要保存负值（默认 0）
  - 加载时做 clamp（不超过当前列数），因为存档时可能有更多列

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 简单的字段增删
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: Task 6 (测试需要持久化逻辑就位)
  - **Blocked By**: Task 2-4 (需要在同一文件中)

  **References**:
  - `MainWindow.xaml.cs:162-174` — `WindowSize` 类定义，直接在其后添加 FrozenColumnCount
  - `MainWindow.xaml.cs:271-340` — `SaveWindowSettings()`，在列状态保存后追加
  - `MainWindow.xaml.cs:176-270` — `LoadWindowSettings()`，在列状态恢复后追加

  **Acceptance Criteria**:
  - [ ] `WindowSize.FrozenColumnCount` 属性存在
  - [ ] `SaveWindowSettings` 保存 `FileListGrid.FrozenColumnCount`
  - [ ] `LoadWindowSettings` 恢复并 clamp
  - [ ] `dotnet build` 通过

  **QA Scenarios**:
  ```
  Scenario: 验证持久化字段存在
    Tool: Bash
    Preconditions: 代码已修改
    Steps:
      1. ast_grep_search "FrozenColumnCount" --lang csharp | Select-String "WindowSize"
    Expected Result: WindowSize 类中有 FrozenColumnCount 属性
    Evidence: .omo/evidence/task-5-persist.txt
  ```

  **Commit**: YES (groups with Task 2, 3, 4)
  - Message: `feat(ui): persist frozen column state in window.json`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs`

---

- [ ] 6. 单元测试

  **What to do**:
  - 在 `tests/MantisZip.Tests/` 中找到或创建合适的测试文件
  - 搜索现有测试文件，看是否有窗口设置持久化测试
  - 如果没有，在现有测试类或新文件中添加测试方法：
    - 测试 `WindowSize` JSON 序列化/反序列化能正确保留 `FrozenColumnCount`
    - 测试保存值为正数 → 加载后值一致
    - 测试保存值为 0（默认）→ 加载后为 0
    - 测试加载时 clamp 到 `Columns.Count`（需要 mock 或集成测试，如果太复杂则测试序列化逻辑即可）

  **简化方案**（推荐）:
  由于 `WindowSize` 是 private 嵌套类，建议直接测试 JSON 序列化的 round-trip：
  - 使用 `JsonSerializer.Serialize/Deserialize` 模拟
  - 在测试项目中引用 `MainWindow.xaml.cs` 需要 `[InternalsVisibleTo]` 或将测试逻辑提取为可测试的 helper

  或者最简单的方案：只验证 `FrozenColumnCount` 被正确使用 `>= 0` 的整数值，使用 `DataGrid` 实例测试：
  ```csharp
  [Fact]
  public void FrozenColumnCount_CanBeSetAndRead()
  {
      var grid = new DataGrid();
      grid.FrozenColumnCount = 3;
      Assert.Equal(3, grid.FrozenColumnCount);
      
      grid.FrozenColumnCount = 0;
      Assert.Equal(0, grid.FrozenColumnCount);
  }
  ```

  **Must NOT do**:
  - 不要添加对 UI 渲染的依赖测试
  - 不要为了测试而大幅改动现有架构

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 简单的单元测试
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: None
  - **Blocked By**: Task 5 (持久化逻辑就位)

  **References**:
  - `tests/MantisZip.Tests/` — 现有测试项目
  - 运行方式: `dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj`

  **Acceptance Criteria**:
  - [ ] 测试文件包含至少一个关于 FrozenColumnCount 的测试
  - [ ] `dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj` → 全部通过（包括已有测试）
  - [ ] 测试验证 FrozenColumnCount 可以被设置和读取

  **QA Scenarios**:
  ```
  Scenario: 运行测试套件
    Tool: Bash
    Preconditions: 所有代码修改已完成
    Steps:
      1. `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 返回 exit code 0
    Expected Result: 所有测试通过（包括已有测试和新增测试）
    Evidence: .omo/evidence/task-6-tests.txt
  ```

  **Commit**: YES
  - Message: `test: add frozen column serialization tests`
  - Files: `tests/MantisZip.Tests/*.cs`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, grep for key methods). Check that `FrozenColumnSeparator` exists in XAML, `UpdateFrozenSeparator` is implemented, `WindowSize.FrozenColumnCount` is saved/loaded. Check evidence files exist.
  Output: `Must Have [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build` + `dotnet test`. Check for: `as any`/`@ts-ignore` (not applicable to C#), empty catches, excessive comments, over-abstraction.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Start from clean state. Build and run. Verify: right-click column header → freeze menu exists, freezing works, separator appears, theme change preserves separator color, window restart preserves state.
  Output: `Scenarios [N/N pass] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  Read "What to do" for each task, read actual diff. Verify 1:1 compliance. Check "Must NOT do" violations.
  Output: `Tasks [N/N compliant] | VERDICT`

---

## Commit Strategy

- **1**: `feat(l10n): add freeze column string resources` — strings.zh.json, strings.en.json, L.cs
- **2-5** (squash): `feat(ui): add frozen column feature with separator and persistence` — MainWindow.xaml, MainWindow.xaml.cs, MainWindow.UI.cs
- **6**: `test: add frozen column serialization tests` — tests/*.cs

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj  # Expected: exit code 0, no warnings
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj  # Expected: all tests pass
```

### Final Checklist
- [ ] 右键非冻结列→「冻结至当前列」可见
- [ ] 右键已冻结列→「取消冻结」可见
- [ ] 冻结后水平滚动，冻结列不动
- [ ] 分隔线在冻结/滚动列之间正确显示
- [ ] 列拖拽宽度后分隔线位置更新
- [ ] 切换亮/暗主题后分隔线颜色跟随
- [ ] 关闭重启后冻结状态保持
- [ ] 所有测试通过
