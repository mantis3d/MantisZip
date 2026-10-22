# 启动优化：主窗口 UI 延迟加载（过滤栏 / 内容区 / 菜单子项）

> **状态**: 🟢 进行中（2026-10-21 启动）| **优先级**: P2 | **预估工时**: 3–4 天
> **来源**: 2026-10-21 启动余量实测——`MainWindow.axaml` 1393 行 / 485 元素，`Win.Xaml` 段 ~330ms；启动时**默认隐藏**的过滤栏（50 元素）与内容区（171 元素）合计 221 元素（46%）白白膨胀。校准：预览面板延迟创建（297 元素）历史上带来整体启动 −9% → **≈0.4–0.5ms/元素**。
> **范围**: 仅「延迟创建」，不改功能、不改视觉；每阶段独立 commit + A/B 测量 + **用户手动验证后才进入下一阶段**。

## 阶段划分（每阶段做完停下等用户验证）

| 阶段 | 内容 | 元素 | 预估 | 状态 |
|---|---|---|---|---|
| **1** | **过滤栏延迟**（默认隐藏）→ 抽 `Controls/MainFilterBar.axaml` + `EnsureFilterBar()` | 50 | 20–25ms | ✅ 完成（XamlSpan 363.2 → 335.8ms，−27ms） |
| **2** | **内容区延迟**（`ArchiveContentGrid` 默认隐藏：目录树 / 文件列表 DataGrid / 地址栏 / 列头） | 171 | 70–90ms | ✅ 完成（XamlSpan 338.7 → 230.7ms，**−108ms**） |
| **3** | **菜单子项延迟**（顶级菜单常驻，子项首次展开时构建） | ~170 | 60–80ms | ⏳ 待做（快捷键已迁到 `MainWindow.KeyBindings`，无风险） |

预计合计 **150–195ms**（当前 TOTAL ~1500ms 的 10–13%）。

## 关键前提（已就绪）

- **快捷键无风险**：2026-10-21 已把 15 个菜单快捷键迁到 `MainWindow.KeyBindings`（AGENTS 规则 10.1）→ 菜单子项何时创建都不影响快捷键
- **可复用的延迟先例**：预览面板 `EnsurePreviewPanel()`（ContentControl 宿主 + 延迟创建 + null 检查）
- **XAML 编译约束**：UI 文件走 XamlIl 预编译（csproj `AvaloniaXaml Include **\*.axaml Exclude Resources\**\*.axaml`）→ 拆出的 UserControl 仍是编译期代码；**不要**把 UI 文件放进 `Resources\`（会变成运行时解析）

## 阶段 1 实施记录（2026-10-21）

- **抽出** `Controls/MainFilterBar.axaml`（161 行内容）+ `MainFilterBar.axaml.cs`（搬入 4 个 picker 处理器 `PickDateFrom/To_Click`、`PickSizeMin/Max_Click` 与 `WritePickerTrace`）
- **局部资源迁移**：`NullableLongConverter` 原声明在 `MainWindow.axaml` 的 `Window.Resources` → 随过滤栏迁入 `MainFilterBar.axaml` 的 `UserControl.Resources`
- **宿主**：`MainWindow.axaml` Row2 改为 `<ContentControl Grid.Row="2" x:Name="FilterBarHost" />`；`MainWindow.EnsureFilterBar()` 在 `IsFilterBarVisible` 首次为 true 时创建（`vm.PropertyChanged` 钩子）
- **编译绑定**：新文件根节点显式 `x:CompileBindings="False"`（Avalonia 12 默认编译绑定，抽出后需显式声明；与其它对话框一致）
- **测试**：`MainFilterBarLazyLoadTests` 2 条（默认不创建 / 显示时创建且只创建一次）
- **测量**：`XamlSpan` **363.2 → 335.8ms（−27ms，8 轮中位）**；TOTAL 1653.7 → 1533.2ms（环境噪声较大，以 XamlSpan 为准）
- **验证**：UI 全量 **344 通过 / 3 跳过 / 0 失败**

## 阶段 2 实施记录（2026-10-21）

- **抽出** `Controls/ArchiveContentView.axaml`（505 行）+ `ArchiveContentView.axaml.cs`（公开具名控件）+ `ArchiveContentView.Events.cs`（随迁事件处理器）
  - 根 Grid 即拆分前的 `ArchiveContentGrid`（`ColumnDefinitions="1*,5,2.5*,5,3*"` / `RowDefinitions="*,0,0"` 与子元素坐标**原样保留**）→ 布局零变化
  - 切割范围 = 原 `MainWindow.axaml` 673..1163 行（**按 `<Grid`/`</Grid>` 深度扫描定位**，勿用肉眼下标；文件每改一次行号就漂移）
  - **⚠️ 编码陷阱**：`git show HEAD:file` 的输出经 PowerShell 5.1 用 `[Console]::OutputEncoding`（OEM 代码页）解码 → **中文注释全部变乱码**，写回 UTF-8 就把文件改坏了。**必须用 `[System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)` 读磁盘文件**（显式 UTF-8），写回用 `New-Object System.Text.UTF8Encoding($false)`（无 BOM）+ CRLF；改完立刻校验乱码计数为 0
  - **⚠️ PowerShell 5.1 保留变量**：`$host` / `$pid` / `$input` 不可赋值（`$host = @(...)` 抛「无法给 Host 赋值」）→ 换名 `$hostLines`
- **宿主**：`MainWindow.axaml` Row3 保留「空态 Border（`BoolInvertConverter`）+ `<ContentControl x:Name="ArchiveContentHost" />`」；`MainWindow.EnsureArchiveContent()` 在 `IsArchiveLoaded` 首次为 true 时创建（`vm.PropertyChanged` 钩子）
- **访问器**：`MainWindow` 保留全部业务逻辑，通过 `ArchiveContentGrid` / `FileListGrid` / `PreviewPanelHost` / `DragAddOverlay*` / 5 个列头箭头等**私有属性**转发到视图（属性名加 `Control` 后缀，避免与 XAML 编译器生成的 `x:Name` 字段重名）
- **启动期恢复动作延后**：`ApplyColumnStates` / `UpdateSortArrows` / `ApplySavedLayout` / `ApplyPreviewLayout` 从构造期挪到 `EnsureArchiveContent()` 内（构造期只保留 `WindowStateManager.Load` 读盘 → `_pendingColumnStates`）
- **随迁事件**：`ColumnHeaderContextMenu_Opening`（+4 个辅助方法）与 `AddressBar_KeyDown` 迁入视图（依赖列头 PathIcon 几何 / VM 导航）；4 个 DataGrid 事件（`SelectionChanged` / `DoubleTapped` / `KeyDown` / `Sorting`）**保留在 MainWindow**，由 `EnsureArchiveContent()` 手动订阅
- **拖拽接线迁移**：原构造期 `this.FindControl<DataGrid>("FileListGrid")` 在拆出后取不到（namescope 变更）→ 抽成 `SetupFileListDragDrop()`，在内容区创建时调用
- **列状态回写保护**：`CaptureColumnStates()` 在内容区未创建时**原样回写**启动时读到的快照（否则空列表会覆盖用户已保存的列宽/显隐）
- **测试**：`MainArchiveContentLazyLoadTests` 3 条（默认不创建 / 加载后创建且只创建一次 / 视图含关键控件）
- **测量**（A/B 交错 8 轮 × 3 次，基线 = HEAD `4d2b6417` 的 worktree 发布）：**XamlSpan 338.7→230.7 / 300.4→232.6 / 335.4→258.9ms（−68 ~ −108ms）**；末次 CtorSpan 419.5→323.9ms（−95.6ms）、WinSpan 860.8→795.5ms（−65.3ms）；InitDone 持平（收益全部来自内容区不再膨胀）
- **验证**：UI 全量通过（`verify-repo.ps1 -Task test -Target ui-tests` ok=true）；`dotnet format analyzers` exit 0；`-Task check` 全过

## 阶段 3 注意事项（菜单子项）

- 菜单子项 ~170 元素：顶级菜单（文件/编辑/视图/工具/帮助）常驻，**子项**在首次展开时构建
- 需注意 `MenuItem` 的 `Icon` / `InputGesture` 展示与 `SubmenuItems` 的动态挂载
- 快捷键已迁到 `MainWindow.KeyBindings`（规则 10.1）→ 子项延迟不影响快捷键生效
