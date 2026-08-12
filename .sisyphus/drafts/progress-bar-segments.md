# Draft: 进度窗口总进度条分段着色（按压缩包状态）

## 需求（用户原话）
- 窗口：`src\MantisZip.UI.Avalonia\Dialogs\ProgressWindow.axaml`
- 想法：总进度条（Row 4）局部根据压缩/解压状态改变颜色
- 例：从 5% 到 10% 代表一个压缩包；该压缩包压缩失败 → 5%~10% 这段显示为红色
- 用户以疑问方式提出（"你看能实现吗？"）→ 需先沟通可行性、确认需求后再规划

## 已探明的现状（代码阅读）
- `ProgressWindow.axaml` Row 4 总进度条：普通 `ProgressBar`，`Value="{Binding PercentComplete}"`，`Foreground={DynamicResource ThemeProgressFillBrush}`，`IsIndeterminate` 绑定
- `ProgressViewModel`：
  - `_batchItems`（`ObservableCollection<BatchItem>`）+ `_currentBatchIndex`
  - `BatchItem` 含 `Name/FullPath/Progress(0-100)/Status/ErrorMessage`
  - `BatchItemStatus`: Pending / InProgress / Completed / Skipped / Failed
  - `SetProgress` 批量加权公式：`completedWeight = _currentBatchIndex / count * 100` + `currentWeight = p.PercentComplete / count` → 每项占 `100/N` 百分比段
  - `UpdateBatchItemStatus(index, status)` 上报成功/失败/跳过；`SetCurrentBatchItem` 切换当前项
  - 失败项已被加权公式当作"已完成"计入 → 进度条会越过失败段 → 颜色覆盖层负责标红
- 批量调用方：`UpdateBatchItemStatus` / `SetCurrentBatchItem` / `FinalizeBatch` / `CompleteWithErrors` 在压缩/解压批量流程中调用（explore 代理确认具体位置）

## 可行性初步判断
- ✅ 数据完备：每项的状态、进度、索引都存在；段范围可由索引 × (100/N) 推出
- ⚠️ Avalonia 原生 `ProgressBar` 不支持分段着色 → 需要自定义控件（DrawingContext 绘制 / 组合布局）
- 待确认：单文件模式是否也要标红？完成段的颜色语义？跳过/待处理段颜色？

## 用户已确认的决策
1. **作用范围**：所有模式都分段（单文件也视为 1 段，单文件失败整条变红）
2. **完成段颜色**：成功用绿色（与 BatchStatusConverters Completed=绿 一致）
3. **边界状态**：全部上色，跳过/失败区分（Pending 段显示背景色）
4. **分段细节**：段间细缝分隔（1-2px 背景色细缝）
5. **实现方案**：方案 A —— 自定义 `SegmentProgressBar : Control`（Render 绘制）
6. **颜色来源**：复用现有状态色（红 #F44336 / 绿 #4CAF50 / 青 #00BCD4 / 蓝 #42A5F5）
7. **无列表失败**：支持 —— ViewModel 加 `HasOverallFailed` 标志，catch 分支设置；无列表时单段变红
8. **测试策略**：单元测试（颜色映射/段区间/退化逻辑）+ Agent QA 截图

## Metis 审查发现（需修正/确认）
1. **数据源**：控件必须绑 `BatchItems` 直接推导段，**不能绑 PercentComplete**（`SetComplete` 会强制 100%，用 PercentComplete 会让失败时显示满条绿）
2. **颜色 alpha**：现有 `ProgressStatusToBackgroundConverter` 是 **35% 半透明**（为列表行叠加设计），实心分段需要**不透明**版本 → 需用户确认
3. **`IsIndeterminate` 是死绑定**（探索证实）：ProgressWindow 总条全项目从未被设为 true，批量始终确定进度 → 新控件无需处理 indeterminate
4. **`HasOverallFailed` 缩小范围**（探索证实）：
   - `RunWithProgress`(MainWindow.axaml.cs:234) catch **已**调 `UpdateBatchItemStatus(0, Failed)`
   - `CompressWithProgress`(App.axaml.cs:1952) catch **已**调 `UpdateBatchItemStatus(0, Failed)`
   - 唯一不标记的是 **`DragDropService.cs`(:118-123)**，失败只弹错误窗、清状态，且从未调 `SetCurrentBatchItem(0)`
   - → 不需要新增 `HasOverallFailed` 标志！只需在 DragDropService 失败路径补 `SetCurrentBatchItem(0) + UpdateBatchItemStatus(0, Failed)`，分段条即有数据标红
5. **`HasFailures` 已存在**（ProgressViewModel:134）→ 不用再造重叠标志
6. **count==0/1 边界**：必须无除零崩溃；count==1 单段全宽
7. **主题/尺寸/DPI**：DynamicResource 渲染时读取不缓存、ArrangeOverride 处理 resize
8. **Skipped 语义**：CompleteWithErrors 不计 Skipped；全 Skipped 批次显示青色无错误摘要，可接受
9. 关键失败路径：
   - DragDropService.cs:99-127 —— 失败 catch 不调用 UpdateBatchItemStatus，不清状态（需补）
   - RunWithProgress:231-236 —— catch 只标 item0 Failed（丢消息）
   - CompressWithProgress:1949-1957 —— captureException 标 item0 Failed + SetErrorSummary + CompleteWithErrors

## 待向用户确认（修正后）
- ~~颜色 alpha~~ **已确认**：使用不透明版本（同色值 alpha=FF，红#F44336/绿#4CAF50/青#00BCD4/蓝#42A5F5）

## 最终决策汇总（全部确认）
1. 所有模式都分段（单文件=1 段，失败整条红）
2. 成功用绿色
3. 全部上色，跳过/失败区分（Pending=背景色）
4. **段间动态预算制缝隙**（用户确认，2026-08-12）：缝隙总和恒 ≤ 条宽 15%，单条 `gap = min(2px, 条宽×15%/(N-1))`；N≈50→~1.4px，N≈100→~0.7px，N≥200→<0.5px 无缝，内容 ≥85% 不被挤占（原固定 1-2px 在 N≥100 时内容被严重挤占，用户提出后改为动态预算制）
5. 方案 A：自定义 SegmentProgressBar : Control（Render 绘制）
6. 复用现有状态色（不透明版本）
7. 无列表失败标红：只需 DragDropService 失败路径补 SetCurrentBatchItem(0)+UpdateBatchItemStatus(0,Failed)，无需新标志
8. 测试：逻辑单测（颜色映射/段区间/缝隙公式/退化）+ Agent QA 截图

## 设计要点（确认版）
- 控件只绑定 `ItemsSource={Binding BatchItems}`（段几何/填充全部由 item.Status + item.Progress 推导）；**不绑 PercentComplete**（SetComplete 强制 100% 会让失败时显示满条绿）；右侧 42px 百分比数字保留在现有 TextBlock（已绑 PercentComplete，不改）
- 段 i 区间 = [i/N, (i+1)/N)；InProgress=蓝填充至 item.Progress、Completed=绿实心、Failed=红实心、Skipped=青实心、Pending=背景色
- 段间缝隙：动态预算制 `gap = min(2px, Bounds.Width × 0.15 / (N-1))`（N≤1 时 gap=0）；`segW = (Bounds.Width - gap×(N-1)) / N`，段 i 起点 `x = i×(segW+gap)`；缝隙用轨道背景色；圆角与现有 ProgressBar 一致（~4px）
- 重绘触发：BatchItems.CollectionChanged + item.PropertyChanged（item.Progress 驱动 InProgress 段填充）→ InvalidateVisual
- 无 BatchItems → 单段退化：成功绿/失败红/进行中蓝
- 文件改动：新增 Controls/SegmentProgressBar.cs；修改 ProgressWindow.axaml(Row4)、BatchStatusConverters.cs(抽 GetColor)、DragDropService.cs(失败补状态)
- 不新增文案（规则 13 无需）；非图标控件（规则 8 不适用）；主题资源复用现有（规则 4）

## ~~待确认问题（Open Questions）~~ 已全部解决（见上方「最终决策汇总」）
1. ✅ 所有模式都分段（单文件=1 段）
2. ✅ 成功用绿色
3. ✅ 全部上色，Pending=背景色
4. ✅ 保留右侧 42px 百分比数字
5. ✅ 段间缝隙：动态预算制（gap = min(2px, 条宽×15%/(N-1))）；圆角 ~4px（用户 2026-08-12 提出大批次数挤占问题后确认）

## 技术方案候选
- A. 自定义 `Control`（DrawingContext 绘制分段）— 推荐，完全可控
- B. Grid + 多 Border 组合 — 实现繁琐、圆角/动画差
- C. 覆盖 ProgressBar 模板 — 侵入性强

## 研究结论（explore 代理，已完成）
### BatchItem / 状态模型（数据完备性 ✅）
- `BatchItem` / `BatchItemStatus`（Pending/InProgress/Completed/Skipped/Failed）定义在 **Core 层**：`src\MantisZip.Core\Models\ProgressBatchItem.cs`（含 `Progress` 0-100、`ErrorMessage`，INotifyPropertyChanged）
- Avalonia `ProgressViewModel` 持有 `ObservableCollection<BatchItem>` + `_currentBatchIndex`；`ProgressWindow.axaml.cs` 是线程安全薄封装（DispatchIfNeeded + Background 优先级）
- 批量加权公式：每项占 `100/N` 百分比段（`completedWeight = idx/count*100` + `currentWeight = pct/count`）→ 与用户"5%-10% 一段"完全吻合
- 失败上报：Core `CompressService.CompressSeparateAsync` try/catch → `onItemStatus(i, Failed)`（**无错误消息**，签名 `Action<int,BatchItemStatus>`）；CLI 解压批量 → `UpdateBatchItemStatus(i, Failed, ex.Message)`（带消息）；GUI `RunWithProgress` catch → item-0 Failed（丢消息）

### 调用链（失败状态已完整驱动到 VM）
- GUI 压缩：`MainWindowViewModel.ExecuteCompressFromSettings`(:1990) → `BatchStatusReporter`(:129) → `MainWindow.axaml.cs` RunWithProgress(:200-204) → `SetCurrentBatchItem + UpdateBatchItemStatus`
- CLI 压缩：`App.axaml.cs` CompressWithProgress(:1900-1905) 同模式
- CLI 解压批量：`RunCliExtractBatchWithProgressAsync`(:1003-1056) / `RunCliDirectExtractBatchAsync`(:1134-1198) 逐项 SetCurrentBatchItem + Failed/Completed
- 收尾：`FinalizeBatch()`（InProgress→Completed）、`CompleteWithErrors()`、`SetComplete()`

### 渲染基础设施（无现成分段控件）
- 总进度条 = 普通 `ProgressBar`（Row 4），`Foreground={ThemeProgressFillBrush}`；无自定义 ControlTheme（App.axaml 只有全局 Style Selector="ProgressBar"）
- 全仓库 **无** 自定义绘制/分段/多色进度条控件（无 ProgressBar 子类、无 Render/DrawingContext 覆盖）
- 现成颜色语义（BatchStatusConverters.cs `ProgressStatusToBackgroundConverter`）：
  - Failed=红 `#F44336`、Completed=绿 `#4CAF50`、Skipped=青 `#00BCD4`、InProgress=蓝 `#42A5F5`（半透明 35% 叠加）
  - → 分段着色可直接复用同一套颜色，视觉一致
- 主题资源：`ThemeProgressFillBrush`/`ThemeProgressBgBrush` 存在于 ThemeLight/ThemeDark

## Scope Boundaries
- IN: Avalonia 版 ProgressWindow 总进度条分段着色
- OUT: WPF 版（维护模式，规则 11）；文件进度条不改
