# 启动优化：主窗口 UI 延迟加载（过滤栏 / 内容区 / 菜单子项）

> **状态**: 🟢 进行中（2026-10-21 启动）| **优先级**: P2 | **预估工时**: 3–4 天
> **来源**: 2026-10-21 启动余量实测——`MainWindow.axaml` 1393 行 / 485 元素，`Win.Xaml` 段 ~330ms；启动时**默认隐藏**的过滤栏（50 元素）与内容区（171 元素）合计 221 元素（46%）白白膨胀。校准：预览面板延迟创建（297 元素）历史上带来整体启动 −9% → **≈0.4–0.5ms/元素**。
> **范围**: 仅「延迟创建」，不改功能、不改视觉；每阶段独立 commit + A/B 测量 + **用户手动验证后才进入下一阶段**。

## 阶段划分（每阶段做完停下等用户验证）

| 阶段 | 内容 | 元素 | 预估 | 状态 |
|---|---|---|---|---|
| **1** | **过滤栏延迟**（默认隐藏）→ 抽 `Controls/MainFilterBar.axaml` + `EnsureFilterBar()` | 50 | 20–25ms | ✅ 完成（XamlSpan 363.2 → 335.8ms，−27ms） |
| **2** | **内容区延迟**（`ArchiveContentGrid` 默认隐藏：目录树 / 文件列表 DataGrid / 地址栏 / 列头） | 171 | 70–90ms | ⏳ 待做 |
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

## 阶段 2 注意事项（内容区）

- 内容区含 16 个 `x:Name`（`FileListGrid` / `AddressBar` / `TreeSearchBox` / 5 个列头箭头 / 3 个 Splitter / `FolderTreeBorder` / `FileListPanel` / `PreviewPanelHost` / `DragAddOverlay*` 4 个）→ 需统一改为「确保已创建」helper，参照 `EnsurePreviewPanel` 的 null 检查模式
- `DragDropItemExpander` / 列头右键菜单等 code-behind 路径依赖这些控件
- 打开压缩包时会多付一次膨胀（用户点「打开」到窗口内容出现之间）→ 需确认 `IsLoading` 遮罩（`MainWindow.axaml` 的加载遮罩）能覆盖这段时间
