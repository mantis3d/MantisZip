# 缩略图卡片格式角标 + 信息行数据条（假列头版） (thumbnail-card-badge-and-row-bars)

> **档位**: 直属用户需求（2026-11-06 提出并当场访谈定案；优先级高于 `thumbnail-list-cache-virtualization`，见该计划「暂缓」标注）
> status: ✅ **已完成**（2026-11-07 实施：T1–T5 五提交 `04438adf`/`d17e2842`/`beea0d04`/`e465ae70`/`f0d62063`；QA 人工项见文末清单。2026-11-07 第二轮访谈修订：行配置入口从设置窗改为「假列头右键菜单」，Tile/Content 配置分开；同日第三轮追加列排序——本文为修订后版本）
> 创建日期: 2026-11-06
> 适用范围: 仅 `MantisZip.UI.Avalonia`（规则 11）
> 依赖: `file-list-thumbnails`（已完成，本计划修正其显示层）；不依赖被暂缓的缓存/虚拟化计划
> 执行分支: `beta`（当前所在）
> 版本号: 不变更（规则 2）；完成时按规则 18 确认 RELEASE_NOTES 更新范围
> 交互参照实现（**照抄不重造**）: Details 列头右键菜单 `Controls/ArchiveContentView.Events.cs:23-65`（Opening 动态建项：ToggleIconBox 图标 + 文字 + Click 切 `IsVisible`）

## 背景与目标

v0.5.6 缩略图功能上线后两个用户可见问题：

1. **格式指示消失**：`ThumbnailContentCard` 三态互斥（图像缩略图 / 文字片段卡 / 兜底格式图标）——前两态显示时把格式图标完全盖住，用户能看到内容却不知道是什么格式。
2. **卡片信息太薄**：Tile 卡下方只有文件名一行；Details 有数据与数据条底纹、Content 有四行元数据（无底纹），折中模式的 Tile 什么都缺。

**目标**：① 内容卡加「格式角标」（图标 chip，仅内容卡态显示）；② 四条信息行（大小 / 压缩后大小 / 压缩率 / 修改日期）做成**数据条底纹 + 文字**的迷你行；③ Tile/Content 顶部各加一条**假列头**，**列头右键菜单切换各行显隐**（Tile 与 Content 配置分开）、左键**点击排序**（与 Details 共用一份窗口级排序状态），交互与 Details 列头同构。

## 已确认决策（2026-11-06 两轮访谈）

| # | 决策 | 结论 |
|---|------|------|
| 1 | 格式指示形态 | **图标角标**：小尺寸格式图标（复用 `IconSource`）+ 半透明底色 chip 叠在内容卡一角；仅缩略图/文字卡态显示，兜底图标态不显示（避免双图标） |
| 2 | 信息行形态 | 每行 = 数据条底纹 + 文字；**Tile 与 Content 配置分开**（第二轮访谈推翻「统一配置」） |
| 3 | 数据条开关 | 跟随现有 `ShowProgressBars`（机制上自动成立，见事实 #5） |
| 4 | **行显隐入口** | **假列头右键菜单**（仿 Details 列头右键），菜单为唯一入口；设置窗不加行开关 UI |
| 5 | **列头显隐** | 与被对应信息行**同步显隐**（隐藏行的列头一并消失）；四行全关时列头只剩空背景条——右键菜单入口**永在** |
| 6 | 持久化 | 行开关状态写 `AppSettings`（Tile 4 + Content 4 共 8 bool），重启恢复；持久化不占 window.json（`ColumnStates` 是 DataGrid 专属，不动） |
| 7 | Tile 默认行 | **大小 + 修改日期**；压缩后 / 压缩率默认关 |
| 8 | Content 默认行 | **四行全开**（保持现状观感） |
| 9 | i18n | 菜单项 / 列头文字全部复用现有 `DataGrid_Size / DataGrid_Compressed / DataGrid_Ratio / DataGrid_Modified` 三语 key ⇒ **本计划新增 i18n key = 0**（设置窗版拟的 5 个 key 撤销） |
| 10 | **列排序（2026-11-07 追加）** | 假列头**支持排序**：左键点列头单元排序（同名再点反转方向，与 Details 列头点击同语义）；排序状态与 Details **共用一份窗口级状态**（`_lastSortMemberPath`/`_lastSortDescending` 切回 Details 所见一致）；假列头最左侧加**恒显「名称」单元**（不可隐藏，只供排序——名称不是信息行，兼做全关时的排序/右键宿主） |

## 已验证事实（2026-11-06 逐项核对）

| # | 项 | 事实 | 位置 |
|---|----|------|------|
| 1 | 三态卡模板 | `ThumbnailContentCard` 是 Tile(:669) 与 Content(:761) **共用的单一资源**——加角标改一处，两模式都受益 | `Controls/ArchiveContentView.axaml:32-76` |
| 2 | 角标用图标 | `ArchiveItemModel.IconSource`（按扩展名的 Shell 图标）一直在模型里；模型另有 `HasThumbnailContent`（缩略图或文字卡非空）可直接做角标可见性判定 | `ArchiveContentView.axaml:62-73`；`Models/ArchiveItemModel.cs` |
| 3 | **列头右键先例（照抄）** | Details 列头 `ContextMenu Opening` 动态构建：`ToggleIconBox`（可见=强调色底 / 隐藏=空心，16×16）+ `Header` 文字 + `Click` 即切；菜单项文字/图标从列头 StackPanel 提取。**本计划的 Tile/Content 列头菜单完全同构**，只是数据源从「DataGrid.Columns」换成「四个行描述（enum 固定清单）」 | `Controls/ArchiveContentView.Events.cs:23-65` |
| 4 | Details 数据条结构 | 每列 = 双 `Rectangle`（文件色 / 目录色，`UseDirProgressColor` 切换）底纹左对齐 + 宽度 = `RatioToWidthConverter(比例, ContentPresenter.Bounds.Width)` + 文字覆盖 | `ArchiveContentView.axaml:444-586` |
| 5 | 比例数据源与开关机制 | `SizeRatio` / `CompressedSizeRatio` / `DateRatio` / `RatioBarValue`（模型现成）；`ShowProgressBars` 关闭时 `ClearProgressBarRatios()` 归零 → Rectangle 宽 0 → 自然不可见。**卡片条复用同一批比例属性 ⇒ 跟随行为零接线** | `Navigation.cs:302-348`；`MainWindowViewModel.cs:2303-2325` |
| 6 | 外观资源机制 | 缩略图外观三开关由 `App.Theming.ApplyThumbnailStyle` 注入 App 级 `DynamicResource`；行可见性走**同一机制**，绑定不经过 VM | `App.Theming.cs:282-297` |
| 7 | **重新解码隔离（关键勘误）** | `RefreshThumbnailStyle()` 会触发 `ThumbnailStyleChanged` → 主窗清空位图重解码。行显隐**不该**走这条路。设置窗已不在写入链上 ⇒ 新增独立入口 `App.RefreshThumbnailThumbnailRowResources()`（只注入 8 行资源，**不触发** `ThumbnailStyleChanged`），列头菜单 Click 调用它。判定口径：切行显隐缩略图**不闪烁不重解码**（DebugLog 无新批次） | `App.Theming.cs:311-325`（先例）；`MainWindowViewModel.cs:1482-1483` |
| 8 | AppSettings 写入惯例 | VM 有 `SaveSetting(s => s.X = val)` 双写（内存单例 + 落盘）；菜单 Click 在控件层，经 `DataContext` 拿 VM 调 Relay 命令即可沿用 | `MainWindowViewModel.cs:2306-2308` |
| 9 | 日期显示宽度 | `LastModifiedDisplay` 固定 `yyyy-MM-dd HH:mm:ss`（19 字符），Tile 格宽 104~168 放不下 ⇒ 日期行取 `yyyy-MM-dd` | `ArchiveItemModel.LastModifiedDisplay` |
| 10 | Content 行结构 | Content 四行目前是「label（`LocalizedStrings[DataGrid_*]`）+ 值」横排 StackPanel（每行 label 宽度不齐）；假列头单元要与行内字段对齐 ⇒ 行与列头共用 Grid 列结构（标签列对齐——用 `Grid SharedSizeGroup`，实测不可用则标签列定宽兜底，两方案都给） | `ArchiveContentView.axaml:750-836` |
| 11 | 现有右键菜单冲突？ | Tile 的 `ScrollViewer.ContextMenu` 已是解压/复制菜单；假列头条是**独立元素**自挂 `ContextMenu`，不与条目区菜单冲突（不同视觉树结点） | `ArchiveContentView.axaml:606-642` |
| 12 | i18n 机制 | 复用的 4 个 `DataGrid_*` key 已在 `UpdateLocalizedStrings` 登记数组内（现状已生效，无需动）；设置窗版 5 key 撤销后 `AboutWindowTests` 三语校验自动覆盖「无新增」 | 规则 13 |
| 13 | **排序是 VM 侧的（照抄复用）** | `ApplyCurrentSort()` 直接对 `CurrentEntries` Clear+重排（「..」置顶 + 目录分离 + `_lastSortMemberPath` 字段排序），DataGrid 饰 Sorting 事件只是入口；键值映射 `GetSortValue` 五字段齐全（含压缩率 `RatioSort` 特判）；Tile/Content 的卡片顺序 = `CurrentEntries` 顺序 ⇒ **点假列头排序后卡片顺序自动变化，无需新排序实现**；排序状态经 `WindowStateManager`（window.json）持久化、关窗重开自动恢复 | `Views/MainWindow.axaml.cs:1270-1326` |

## 设计

### 1. 格式角标（T1，ThumbnailContentCard 模板内追加一层）

```xml
<!-- ④ 格式角标：缩略图 / 文字卡态把兜底态的格式图标盖住了，这里以 chip 形式还给用户。
     仅在卡片有内容（HasThumbnailContent = 缩略图或文字卡非空）时显示；兜底图标态
     本身就是格式图标，不显示角标避免双图标。目录条目只有兜底态，天然不受影响。 -->
<Border Width="{DynamicResource ControlHeightSm}"
        Height="{DynamicResource ControlHeightSm}"
        HorizontalAlignment="Right" VerticalAlignment="Top"
        Margin="0,0,2,2"
        Background="{DynamicResource ThemeSurfaceBgBrush}"
        Opacity="0.9"
        CornerRadius="{DynamicResource BorderRadius}"
        IsVisible="{Binding HasThumbnailContent}">
  <Image Source="{Binding IconSource}"
         Width="14" Height="14"
         Stretch="Uniform"
         HorizontalAlignment="Center" VerticalAlignment="Center" />
</Border>
```

位置：`ThumbnailContentCard` 的 `Panel` 内、兜底图标 `Image` 之后（层级最顶）。`Margin="0,0,2,2"` 为 chip 内部定位微调，2px 属于 chip 定位常量不属于间距体系资源管辖（注释里已写明豁免原因）。角标位置默认右上角；chip 底色用主题表面色 + `Opacity=0.9` 保证任意亮/暗图像上可读（规则 4 显式画刷）。

### 2. 行状态与资源（T2）

**AppSettings**（`Models/AppSettings.cs`，`ThumbnailSize`(:241) 之后插入；规则 23：消费者 = T2 资源注入 + T3 模板绑定，同提交落地）：

```csharp
// ── 卡片信息行显示状态（2026-11-06 访谈：交互唯一入口 = Tile/Content 假列头右键菜单；
//    设置窗不加 UI；持久化重启恢复。命名 Tile/Content 前缀区分两模式独立配置 ──

/// <summary>Tile 迷你信息行：大小行。默认开（访谈默认 大小+修改日期）。</summary>
public bool TileRowShowSize { get; set; } = true;
/// <summary>Tile 迷你信息行：压缩后大小行。默认关。</summary>
public bool TileRowShowCompressedSize { get; set; } = false;
/// <summary>Tile 迷你信息行：压缩率行。默认关。</summary>
public bool TileRowShowRatio { get; set; } = false;
/// <summary>Tile 迷你信息行：修改日期行（短日期 yyyy-MM-dd）。默认开。</summary>
public bool TileRowShowDate { get; set; } = true;

/// <summary>Content 元数据行：四行默认全开（保持 v0.5.6 观感）。</summary>
public bool ContentRowShowSize { get; set; } = true;
public bool ContentRowShowCompressedSize { get; set; } = true;
public bool ContentRowShowRatio { get; set; } = true;
public bool ContentRowShowDate { get; set; } = true;
```

**资源注入**：`App.Theming.cs` 拆两个方法——

```csharp
/// <summary>
/// 卡片信息行可见性资源（Tile 4 + Content 4）：由假列头右键菜单写入设置后调用，
/// 只注入资源、**不触发** <see cref="ThumbnailStyleChanged"/>（行显隐与解码无关，
/// 触发会导致整包缩略图重解码闪一下——见设计 #7）。
/// </summary>
internal static void RefreshThumbnailRowResources()
{
    if (Current is not App app) return;
    var s = AppSettings.Load();
    app.Resources["ThumbnailRowSizeVisible"]              = s.TileRowShowSize;
    app.Resources["ThumbnailRowCompressedSizeVisible"]    = s.TileRowShowCompressedSize;
    app.Resources["ThumbnailRowRatioVisible"]             = s.TileRowShowRatio;
    app.Resources["ThumbnailRowDateVisible"]              = s.TileRowShowDate;
    app.Resources["ThumbnailContentRowSizeVisible"]           = s.ContentRowShowSize;
    app.Resources["ThumbnailContentRowCompressedSizeVisible"] = s.ContentRowShowCompressedSize;
    app.Resources["ThumbnailContentRowRatioVisible"]          = s.ContentRowShowRatio;
    app.Resources["ThumbnailContentRowDateVisible"]           = s.ContentRowShowDate;
}
```

启动注入点：`ApplyThumbnailStyle(AppSettings settings)` 末尾把同样八行赋值直接写进去（参数 `settings` 已在手，**不要**在启动路径里调 `RefreshThumbnailRowResources()` 二次读盘——读盘方法留给菜单 Click 路径专用）。`IsVisible={DynamicResource}` 对 bool 资源的适用性在 T2 最小样例先验证（风险 #1）。

**VM 切换命令**（`MainWindowViewModel.cs`，供列头菜单 Click 调用；一个命令 + 枚举参数）：

```csharp
public enum CardRowKind { Size, CompressedSize, Ratio, Date }   // 与四行一一对应（可放别处，主 VM 使用即可）

/// <summary>
/// 切换卡片信息行显示（Tile / Content 假列头右键菜单唯一入口）。
/// 写入 AppSettings 双写持久化 + 重注入资源；**禁止**在此触发重解码事件。
/// </summary>
[RelayCommand]
private void ToggleCardRow(bool isTileLayout, CardRowKind kind)
{
    bool newValue = !GetCardRowValue(isTileLayout, kind);
    SetCardRowValue(isTileLayout, kind, newValue);
    App.RefreshThumbnailRowResources();
}

// 读写映射各自 8 个 case，集中在这两个私有方法里（不许反射/表达式树）：
private bool GetCardRowValue(bool isTile, CardRowKind kind) => (isTile, kind) switch
{
    (true, CardRowKind.Size) => _appSettings.TileRowShowSize,
    (true, CardRowKind.CompressedSize) => _appSettings.TileRowShowCompressedSize,
    (true, CardRowKind.Ratio) => _appSettings.TileRowShowRatio,
    (true, CardRowKind.Date) => _appSettings.TileRowShowDate,
    (false, CardRowKind.Size) => _appSettings.ContentRowShowSize,
    (false, CardRowKind.CompressedSize) => _appSettings.ContentRowShowCompressedSize,
    (false, CardRowKind.Ratio) => _appSettings.ContentRowShowRatio,
    (false, CardRowKind.Date) => _appSettings.ContentRowShowDate,
};

private void SetCardRowValue(bool isTile, CardRowKind kind, bool value)
{
    switch (isTile, kind)
    {
        case (true, CardRowKind.Size): SaveSetting(s => s.TileRowShowSize = value); break;
        case (true, CardRowKind.CompressedSize): SaveSetting(s => s.TileRowShowCompressedSize = value); break;
        case (true, CardRowKind.Ratio): SaveSetting(s => s.TileRowShowRatio = value); break;
        case (true, CardRowKind.Date): SaveSetting(s => s.TileRowShowDate = value); break;
        case (false, CardRowKind.Size): SaveSetting(s => s.ContentRowShowSize = value); break;
        case (false, CardRowKind.CompressedSize): SaveSetting(s => s.ContentRowShowCompressedSize = value); break;
        case (false, CardRowKind.Ratio): SaveSetting(s => s.ContentRowShowRatio = value); break;
        case (false, CardRowKind.Date): SaveSetting(s => s.ContentRowShowDate = value); break;
    }
}
```

（`SaveSetting` 是 VM 既有双写助手，内存单例 + 落盘，惯例见 `ToggleProgressBars` :2306-2308。）

### 3. 假列头条（T3，Tile 与 Content 各一条）

**结构**（Tile 版；Content 同构但单元宽度走标签对齐列）：

```xml
<!-- ══════ 平铺布局假列头：仿 Details 列头；右键 = 行显隐菜单，左键 = 切排序 ══════
     最左「名称」单元恒显（名称不是信息行、但也需要排序入口与全关时的宿主）；
     信息行单元显隐与对应迷你行同步（IsVisible DynamicResource 行资源）；
     全关信息行时剩「名称」+ 空背景条，排序与右键入口均永在 -->
<Border Grid.Row="2" IsVisible="{Binding IsTileLayout}"
        Tag="TileRowHeader"
        Background="{DynamicResource ThemeHeaderBgBrush}"
        BorderBrush="{DynamicResource ThemeBorderLightBrush}" BorderThickness="0,0,0,1"
        VerticalAlignment="Top" ZIndex="1"
        Height="{DynamicResource ControlHeightMd}">
  <Border.ContextMenu>
    <ContextMenu Opening="CardRowHeaderContextMenu_Opening" />
  </Border.ContextMenu>
  <StackPanel Orientation="Horizontal" Spacing="{DynamicResource SpacingXxs}">
    <!-- ①「名称」：恒显（不可隐藏），Tapped = 名称排序（DataGrid_Name key） -->
    <Border Width="{DynamicResource ThumbnailTileCellWidth}" Tag="Sort:Name">
      <StackPanel Orientation="Horizontal" Spacing="3">
        <TextBlock Text="{Binding LocalizedStrings[DataGrid_Name]}" FontSize="12" FontWeight="SemiBold" />
        <TextBlock x:Name="TileSortArrowName" Width="12" FontSize="10" Text="" />
      </StackPanel>
    </Border>
    <!-- ② 五个信息行单元（四个对应信息行）：图标（Details 列头同款 Geometry）+ 文字
          （DataGrid_* 三语 key）+ 排序箭头；IsVisible="{DynamicResource ThumbnailRow*Visible}"
          与行同步；Tapped = 对应字段排序 -->
    <Border IsVisible="{DynamicResource ThumbnailRowSizeVisible}"
            Width="{DynamicResource ThumbnailTileCellWidth}" Tag="Sort:Size">
      <StackPanel Orientation="Horizontal" Spacing="3">
        <PathIcon ... />
        <TextBlock Text="{Binding LocalizedStrings[DataGrid_Size]}" ... />
        <TextBlock x:Name="TileSortArrowSize" Width="12" FontSize="10" Text="" />
      </StackPanel>
    </Border>
    <!-- …压缩后（DataGrid_Compressed）/ 压缩率（DataGrid_Ratio）/ 日期（DataGrid_Modified）同理 -->
  </StackPanel>
</Border>
```

要点：

- **列头单元图标**：直接 `<StaticResource>` 引用 Details 列头同款 `IconCompress` / `IconCalendar` / `IconDataBarVertical` / 大小列图标（:487/:531/:563 同款），规则 4 `Foreground` 显式给。
- **列排序接线（照抄 VM 侧机制，零新排序）**：单元挂 `Tapped`（左键；右键被整条 Border 的 ContextMenu 吃掉，语义自然分流）→ 调 MainWindow 侧公共方法：

```csharp
/// <summary>
/// 假列头排序（Tile/Content 各 5 个单元挂此入口）。与 Details 列头点击同语义：
/// 同字段再点反转方向，异字段升序；共用窗口级 _lastSortMemberPath/_lastSortDescending
/// ⇒ 切回 Details 列表所见一致（访谈决策 10）；ApplyCurrentSort 重排 CurrentEntries
/// ⇒ 卡片顺序自动变化（事实 #13）。排序状态落 window.json，零新增持久化。
/// </summary>
private void OnCardHeaderSortTapped(string sortPath)
{
    if (_lastSortMemberPath == sortPath)
        _lastSortDescending = !_lastSortDescending;
    else
    {
        _lastSortMemberPath = sortPath;
        _lastSortDescending = false;
    }
    ApplyCurrentSort();
    UpdateSortArrows();       // Details 五个箭头照常刷新（列不可见时无害）
    UpdateCardSortArrows();   // 新增：Tile/Content 两个列头共 10 个箭头 TextBlock 按同榜刷新
}
```

   `UpdateCardSortArrows` 结构照 `UpdateSortArrows`（:1298-1307）：Tile 5 个 + Content 5 个箭头 TextBlock 按 `_lastSortMemberPath` 匹配（Name 单元 = "Name"、Size/CompressedSize/RatioSort/LastModified 与 Details 同路径，`GetSortValue` 零改动）。箭头控件在 `ArchiveContentView` 里按 x:Name 用 `FindControl` 定位（与既有 `FindThumbnailLayoutViewer` 同模式）。**字段映射钉子**：`sortPath` 直接用 Details 的现有成员路径（`Name`/`Size`/`CompressedSize`/`RatioSort`/`LastModified`），不接受新字符串——通路完全踩在既有 `GetSortValue` switch 上。
- **Tile 列头与格子对齐**：列头条 `Padding` 与 ScrollViewer 容器 padding 相同（`SpacingXsThk`）、单元 `Width = ThumbnailTileCellWidth`（与 WrapPanel 格宽同资源）——近似像素级对齐；不求列内微调（「假的也行」在场内访谈已明确）。
- **Content 列头对齐**：Content 行从 StackPanel 改为 `Grid`（标签列 / 值列），标签列用 `Grid.SharedSizeGroup="ContentRowLabel"`（Avalonia 支持；**T3 前置 spike 一个最小样例确认**；不可用则标签列定宽——按最长标签 `修改日期:` 的 10.5pt 宽度估计 `Width ≈ 72`，实施以实测为准，定宽是**兜底不是主案**）。列头五单元与该列结构一一镜像。
- **列头显隐与行同步**：信息行单元 `IsVisible` 绑同一行资源——隐藏行的列头同步消失（访谈决策）；「名称」单元**恒显**（排序宿主）。全关信息行时剩名称单元 + 空背景条，排序与右键入口均永在（这一轮修订顺带消灭了「全关空条死角」）。
- **菜单**：挂在整条 `Border`（不是单元）⇒ 任意位置（含名称单元）右键都能开菜单。菜单实现放 `Controls/ArchiveContentView.Events.cs`（照抄 :23-65 的骨架）：

```csharp
/// <summary>
/// 卡片信息行假列头右键菜单：Opening 动态构建，与 Details 列头菜单同构
/// （ToggleIconBox 可见态图标 + Header 文字 + Click 切换）。
/// 数据源 = 固定四行（CardRowKind），Tile 与 Content 两个列头各一份菜单、各行独立状态。
/// 文字/图标复用 Details 列头同款（规则 11 视觉一致）。
/// </summary>
private void CardRowHeaderContextMenu_Opening(object? sender, CancelEventArgs e)
{
    if (sender is not ContextMenu menu) return;
    var isTile = menu.PlacementTarget is Border { Tag: "TileRowHeader" }; // Tag 区分两份列头
    menu.Items.Clear();

    foreach (var (kind, header, iconData) in CardRowCatalog.All(isTile))
    {
        var visible = CardRowStorage.Read(isTile, kind);   // 从 VM/设置读当前态
        var iconBox = new Border { Classes = { "ToggleIconBox" }, Background = ToggleIconBackground(visible) };
        var menuItem = new MenuItem { Icon = iconBox, Header = header, Tag = kind };
        menuItem.Click += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            vm.ToggleCardRowCommand.Execute((isTile, kind));   // 多参数 RelayCommand 生成元组命令，直调元组
            iconBox.Background = ToggleIconBackground(!visible);
        };
        menu.Items.Add(menuItem);
    }
}
```

   `CardRowCatalog` 静态表（kind → `DataGrid_*` 标题 key + 列头图标 Geometry key + 资源名）与存储读写各 30 行小类，随实施落位（Tile 版在 `Services/` 或 `Controls/` 均可，纯逻辑可单测）。
   **菜单项的透明与 VM 兜底**：菜单在控件层构建、无 DataContext 继承细节差异，`Click` 里 `DataContext as MainWindowViewModel` 判空返回（与 :126 处既有模式一致）。

### 4. Tile 迷你信息行 + Content 行底纹（T4）

前一版设计的 §3/§4 原样保留（换开关资源名 + Content 行改 Grid 对齐）：

- 四行内容映射（画刷与文字源全为现有资源/属性）：

| 行 | Tile 开关资源（Content 版前缀 `ThumbnailContentRow…`） | 底纹比例 | 底纹画刷（文件/目录） | 文字 |
|----|---------|---------|----------------------|------|
| 大小 | `ThumbnailRowSizeVisible` | `SizeRatio` | `ProgressBarSizeBrush` / `…SizeDirBrush` | `SizeDisplay` |
| 压缩后 | `ThumbnailRowCompressedSizeVisible` | `CompressedSizeRatio` | `ProgressBarCompressedSizeBrush` / `…DirBrush` | `CompressedSizeDisplay` |
| 压缩率 | `ThumbnailRowRatioVisible` | `RatioBarValue` | `ProgressBarRatioBrush`（无目录变体） | `RatioDisplay` |
| 日期 | `ThumbnailRowDateVisible` | `DateRatio` | `ProgressBarDateBrush`（无目录变体） | `LastModifiedShortDisplay`（新模型属性） |

- `LastModifiedShortDisplay`（`Models/ArchiveItemModel.cs`）：

```csharp
/// <summary>短日期显示（yyyy-MM-dd）：Tile 迷你行宽度放不下完整时间戳。依赖 LastModifiedDisplay 固定 yyyy-MM-dd 前缀。</summary>
public string LastModifiedShortDisplay => LastModifiedDisplay.Length >= 10
    ? LastModifiedDisplay[..10]
    : LastModifiedDisplay;
```

- Content 行改 Grid：**文字层绑定不动**（label/value 原样），只把横排 StackPanel 包进 Grid 以获得对齐列；行为（T4 后）= 底纹 Rectangle + 原 label/value 覆盖其上；行关闭时整格隐藏（`IsVisible` DynamicResource）⇒ Content 四行全关后只剩内容卡 + 名称（访谈决策）。
- ShowProgressBars 跟随：零接线（事实 #5）。

## 任务分解

| # | 任务 | 文件 | 验收标准 |
|---|------|------|---------|
| T1 | 角标层加入 `ThumbnailContentCard` + 中文注释 | `Controls/ArchiveContentView.axaml:32-76` | Tile/Content 卡片显示缩略图或文字卡时右上角出现格式图标；兜底图标态无角标；目录无角标 |
| T2 | 8 bool AppSettings + `RefreshThumbnailRowResources` + 启动注入 + `ToggleCardRow` 命令 + `CardRowKind` 枚举（**不含设置窗 UI、不含 i18n**） | `Models/AppSettings.cs`、`App.Theming.cs`、`ViewModels/MainWindowViewModel.cs` | 命令切一次 → 资源值取反且重启保持；**调试日志确认无新一轮缩略图批次** |
| T3 | Tile/Content 假列头条（两模式各 5 个排序单元 = 恒显「名称」+ 四信息行，各带箭头 TextBlock）+ 动态右键菜单 + 排序接线（`OnCardHeaderSortTapped` / `UpdateCardSortArrows`）+ Content 行 Grid 对齐 spike（SharedSizeGroup） | `Controls/ArchiveContentView.axaml`（两个滚动容器上方各插一条列头 Border）、`Controls/ArchiveContentView.Events.cs`、`Views/MainWindow.axaml.cs`（排序方法三件套）、`Controls/`（CardRowCatalog/存储小类） | 列头与卡片格对齐、行同步显隐、名称单元恒显；菜单项 ToggleIconBox 态正确、点击即切两模式独立；**左键点列头排序生效（含名称/压缩率 RatioSort 特判），翻向正确，箭头 ▲/▼ 同步，切回 Details 排序一致** |
| T4 | Tile 4 迷你行模板 + Content 行底纹/行开关 + `LastModifiedShortDisplay` | `Controls/ArchiveContentView.axaml`、`Models/ArchiveItemModel.cs` | 默认（Tile 大小+日期 / Content 全开）与访谈一致；各模式勾选独立生效 |
| T5 | 测试补齐 + QA + 文档双轨（规则 3）+ RELEASE_NOTES 确认（规则 18）+ 提交 | `tests/...`、`docs/PROGRESS.md`、`docs/progress-avalonia-detail.md` | 见 QA 清单 |

**预估工时**：~2 天（T1 ~1h，T2 ~2h，T3 ~4-5.5h（含 spike 与排序接线），T4 ~3h，T5 ~1.5-2h）。TDD 口径：可测点（AppSettings 默认值、`CardRowCatalog` 映射、`ToggleCardRow` 写回）先行红灯。

## QA 清单（人工验证）

1. `-Task build -Target avalonia`（Release + `--no-incremental`）0 警告；`-Task test -Target ui-tests` 全绿；`-Task check` / `-Task guards`（settingswiring 8 属性消费 → 列头菜单与模板接线）/ `-Task i18n` 全绿（**0 个新增 key**）。
2. 打开混合包（图片/文本/ico/exe/目录）：Tile/Content 显示缩略图或文字卡的条目**右上角有格式图标**，exe/目录（兜底态）**无**角标。
3. Tile 假列头：与卡片格对齐；「大小 / 修改日期」默认显示、其它两项隐藏；Content 假列头四行全显。
4. 右键点击 Tile / Content 列头条：菜单含四项，ToggleIconBox 图标指示状态；切换即时生效且两模式**独立**（Tile 切日期关，Content 日期仍开）。
5. 假列头排序：Tile/Content 任意可见列头（含恒显「名称」）左键 → 卡片重排 + 箭头 ▲/▼；同字段再点反转方向；**压缩率列走 `RatioSort`**（与 Details 同键值）；切回 Details 列表排序一致；关窗重开排序恢复。
6. 排序/菜单入口随显隐：隐藏行的列头单元消失（其排序入口随之消失，与 Details 隐藏列一致）；「名称」单元恒显兜底——信息行全关时仍有名称排序可点、名称/空条右键菜单可用（无死锁）。
7. 数据条与文字：条=Details 同色（文件/目录双色含 SeparateDirBaseline 基线）；`ShowProgressBars` 关 → 条消失、**文字仍在**；Details 列为行为不变。
8. 深浅主题：列头条背景、ToggleIconBox 状态底色随主题（不写死颜色）。
9. 96/128/160 三档边长 + 紧凑度三档：列头高度用 `ControlHeightMd`、行高 `ControlHeightSm` 缩放不裁切。
10. **切行不重解码**：DebugLog 无新批缩略图（T2 口径）；改外观设置（尺寸/边框/垫底）仍正常重解码（回归验证）。
11. 压缩后大小 / 压缩率不可用格式（7z/RAR/tgz）：行文字空、条宽 0，与 Details 一致。
12. 选择/勾选/右键（条目菜单）/拖拽/键盘行为无回归；`.ico` 条目角标语义（通用 Shell 图标叠加自身图标缩略图）可接受。
13. 重启应用：两个模式各自的行配置（8 bool）恢复、排序状态恢复（window.json，现状机制回归验证）。

## Must-NOT-Have

- 不动 DataGrid（Details）任何模板、列头、右键菜单行为；`ShowProgressBars` 机制零改动。
- 不动三态卡互斥结构、缩略图解码/缓存管线（`ThumbnailService` / `ThumbnailCache` / `IcoIconLoader`）。
- **不做独立排序模型**：假列头排序只复用窗口级 `_lastSortMemberPath`/`ApplyCurrentSort` 单一事实源（ Details 的 Sorting 通路零改动）；不做列拖宽、不做每模式独立排序状态。
- 不加设置窗行开关 UI（列头菜单唯一入口 —— 访谈决策 4）。
- 不动 `WindowStateManager.ColumnStates`（DataGrid 专属持久化；行开关只写 `AppSettings`）。
- 不新增几何图标资源与 i18n key（图标/文字全部复用现有；规则 8/13 无触碰面）。
- 不在主 VM 新增行可见性状态（走资源机制）。

## 已知风险与回退

1. **DynamicResource bool → IsVisible**：Avalonia 12 支持 `{DynamicResource}` 给 `IsVisible` 传 bool 资源（值语义绑定），T2 首次构建验证；不支持则回退为主 VM 8 个 `[ObservableProperty]` + 通知链（改动面稍大但路径明确）。
2. **`Grid.SharedSizeGroup` 在 Avalonia 12**：T3 前置 spike 验证；不可用回退标签列定宽（按长标签实测宽度），只影响 Content 列头对齐精度。
3. **事件隔离回归**：`RefreshThumbnailRowResources` 绝不能调 `RefreshThumbnailStyle`；T2 实施注释 + QA 第 9 条双钉。
4. **日期切片回归**：`LastModifiedShortDisplay` 依赖 `yyyy-MM-dd` 前缀固定，格式变更会截错——注释钉住 + QA 第 6 条核对。
5. **深色图像上的角标可读性**：chip `Opacity=0.9` + 表面色已保底；若撞同色系图像，升级双描边（`ThemeBorderLightBrush`）再议。
6. **菜单态刷新**：菜单项 ToggleIconBox 在 `Click` 里手动翻转（照抄 :58-62 的模式）；菜单**打开时**重新 Opening 生效（Opening 每次动态重建）——不会出现「点完状态没变」的陈旧菜单。

## 提交与文档要求

- 提交粒度：T1 `feat(avalonia): 缩略图卡片新增格式角标`；T2 `feat(avalonia): 卡片信息行设置持久化与资源注入`；T3 `feat(avalonia): Tile/Content 假列头（行显隐右键菜单 + 列排序）`；T4 `feat(avalonia): Tile/Content 信息行数据条底纹`；T5 `test/docs`。
- 完成（规则 3）：PROGRESS.md 里程碑 + progress-avalonia-detail 细节条目。
- 完成（规则 18）：**主动确认 RELEASE_NOTES**——两项都是用户可感知改进，倾向入「更新内容/改进」节，待用户拍板。
