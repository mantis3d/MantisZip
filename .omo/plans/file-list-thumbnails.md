# 文件列表缩略图查看方式 (file-list-thumbnails)

> status: 待实施
> 创建日期: 2026-08-21
> 适用范围: 仅 `MantisZip.UI.Avalonia`（规则 11，WPF 维护模式不动）
> 依赖: 无（不依赖 preview-quick-modes / embedded-thumbnail-preview 先行）

## 背景与目标

给文件列表增加"缩略图查看方式"。用户通过工具栏按钮在三种布局模式间循环切换（详情 / 平铺 / 内容），选择持久化到 AppSettings。

- **详情 Details**：现状 DataGrid（16px 图标列），唯一保留 DataGrid 的模式。
- **平铺 Tile**：ItemsRepeater + UniformGridLayout 平铺网格，每项 = 128px 内容卡 + 下方文件名。
- **内容 Content**：自定义垂直行布局（**非 DataGrid**），每行 = [128px 内容卡] + 右侧元数据块（文件名加粗 + 文件大小/压缩后大小/压缩率/修改日期 标签值行，显示名与预览信息面板一致）。

**内容卡三档**（每项在 Tile/Content 模式下的显示）：
1. **图像类**（PreviewType.Image / AnimatedImage / Svg / IcoGallery）→ 真实缩略图 128px（SKCodec 首帧 / Svg.Skia / IcoParser）
2. **文本类**（PreviewType.Text / Csv / Markdown / Html）→ **文字卡片**：解压头部 4KB → `TextEncodingDetector.DecodeText` → 前 ~150 字符
3. **其余** → 现有系统文件图标（`IconSource` 兜底）

## 已确认决策（访谈记录）

| # | 决策 | 结论 |
|---|------|------|
| 1 | 出图范围 | 图像类真缩略图 + 文本类文字卡片；其余图标（用户确认"所有文本类"、"两模式都用"） |
| 2 | 切换入口 | 工具栏按钮循环切换 + AppSettings 持久化 |
| 3 | 平台 | 仅 Avalonia |
| 4 | 尺寸 | 固定 128px，无尺寸选择器（不做 preview-quick-modes §7.1 的 32-128 选择器） |
| 5 | 视图形态 | 三态（Details/Tile/Content），用户可选；Content 为自定义行布局（非 DataGrid），与 Tile 共用选择层 |
| 6 | Content 元数据 | 仅公共字段（内存已有，零提取）；格式专属元数据（尺寸/页数/PE 等）不做，仍由预览面板提供 |
| 7 | 排除格式 | Magick.NET 格式（PSD/HDR/EXR/TIFF/TGA）不做——插件尚不存在；PDF/视频/Office 不做内容卡 |
| 8 | 排序 | Tile/Content 无排序 UI；Details 保留列排序 |

## 已验证事实（探索结论）

- **DataGrid FileListGrid**：`MainWindow.axaml` 行 959-1150，位于 `FileListPanel` Grid（RowDefinitions `Auto,Auto,*`）的 Grid.Row=2。列：Icon(22) / Name(250) / Size(80) / Compressed(70) / Ratio(70) / Modified(100)。右键菜单行 981-1005（Extract/ExtractHere/ExtractTo/SmartExtract/CopyName/CopyPath/Test/Delete）。`SelectedItem="{Binding SelectedEntry}"` 双向绑定。
- **代码后置事件**：`FileListGrid_SelectionChanged`（MainWindow.axaml.cs:1231，清空后重建 `SelectedEntries`）、`FileListGrid_DoubleTapped`（:962）、`FileListGrid_KeyDown`（:979）、`FileListGrid_Sorting`（:1007）。`OnSelectedEntryChanged`（MainWindowViewModel.cs:726）→ `ShowPreviewAsync`。
- **拖拽管线**（MainWindow.axaml.cs:381-603）：`PointerPressed`（Tunnel）记录 `_dragStartPoint` + `HitTestPressedRowItem` 判定命中行 + `_dragPreservedSelection` 保存多选 → `PointerMoved`（4px 阈值 + `EnableDragExtract` 开关）→ `CustomOleDragDrop.PerformDragDrop` + `OverlayController` + 状态光标 + `DragDropService.ExecuteAfterDropAsync`。**必须抽成可复用方法供 Tile/Content 共用**。
- **扩展名分类**：`PreviewService.ClassifyPreview(string ext)`（PreviewService.cs:174）返回 PreviewType（Text/Csv/Image/AnimatedImage/Svg/Html/Markdown 等）。缩略图分类直接复用，不重写。
- **内存提取**：`ArchiveEntryExtractor.ExtractHeadAsync`（Core）ZIP 实现为整条目解压进内存再取头 → 图像提取必须设 **8MB 条目大小上限**；7z 固实自动降级全量提取 → 批量场景**主动跳过固实包**（`IsSevenZipSolid`）。密码传 `GetSessionPassword(archivePath)`。
- **文本解码**：`TextEncodingDetector.DecodeText(byte[], codePage=0)`（Core/Utils/TextEncodingDetector.cs:80）字节数组直接解码（UTF-8 BOM → 严格 UTF-8 → 系统 ANSI/GBK），无需临时文件。
- **元数据公共字段**（全部为 `ArchiveItemModel` 现成属性，零提取）：`NameDisplay` / `SizeDisplay` / `CompressedSizeDisplay` / `RatioDisplay`（"75.0%"）/ `LastModifiedDisplay`（"yyyy-MM-dd HH:mm:ss"）。`CompressedSizeAvailable=false`（7z/RAR/tgz/gz）时压缩后大小/比率自然显示空，与列行为一致。
- **元数据 i18n key 已存在**：`Metadata_Key_FileName/FileSize/CompressedSize/CompressionRatio/FileModifiedDate`（strings.zh-CN.json:1065-1069、strings.en.json:1067-1071）。标签显示名复用，不新增。
- **ViewMode 循环模式参照**：`FileListViewMode` 枚举（MainWindowViewModel.cs:664）+ `OnViewModeChanged`（:669）+ `ViewModeLabel`（:686）+ `CycleViewModeCommand`。工具栏按钮在 MainWindow.axaml:862-872，分隔线 874。
- **虚拟化**：Avalonia 12.0.4 **无内置 VirtualizingWrapPanel**（PR #11251 未合入）；官方推荐 `ItemsRepeater` + `UniformGridLayout`（Tile）/ `StackLayout`（Content 垂直行）。ItemsRepeater 无内置选择 → 自建选择层。
- **`UpdateLocalizedStrings`**（MainWindowViewModel.cs ~203）：XAML 用 `LocalizedStrings[Key]` 绑定的 key 必须登记进该数组，否则空白不报错。
- **项目约定**：规则 4（主题资源键，Avalonia 均以 `Brush` 结尾）、5（紧凑度资源，`SpacingXxxThk` 用于 Margin/Padding）、7（列表项 MinHeight 用 `ControlHeight*`）、8（新图标注册 `IconTestViewModel.LoadAllIcons`）、13（本地化成对 key + UpdateLocalizedStrings 登记）、12（构建验证）。

## 设计

### 1. 枚举与设置

```csharp
// MainWindowViewModel.cs（FileListViewMode 附近）
public enum FileListLayoutMode { Details = 0, Tile = 1, Content = 2 }
```

- `AppSettings.FileListLayoutMode`（`Models/AppSettings.cs`，int，默认 0）。**仅 Avalonia 侧**（WPF 的 AppSettings 不加，保持两边字段同步约定仅限共享字段——此为 Avalonia 专属新增，与 Theme/CompactnessMode 同例）。
- VM：`[ObservableProperty] FileListLayoutMode layoutMode`；`OnLayoutModeChanged` → 通知 `IsDetailsLayout/IsTileLayout/IsContentLayout/LayoutModeLabel` + 写入 AppSettings；`[RelayCommand] CycleLayoutMode()`（Details→Tile→Content→Details）；`LayoutModeLabel` 用 `LocalizationManager.T("LayoutMode_Details"/"LayoutMode_Tile"/"LayoutMode_Content")`。
- 模式切换时选择状态迁移：`SelectedEntry`/`SelectedEntries` 为 VM 层数据天然跨模式保留；切回 Details 时 DataGrid `SelectedItem` 经既有绑定自动恢复；Tile/Content 的 `IsSelected` 视觉由选择控制器同步。

### 2. 模型扩展（Models/ArchiveItemModel.cs）

新增：
```csharp
[ObservableProperty] private Bitmap? _thumbnailSource;  // 图像卡
[ObservableProperty] private string? _textSnippet;      // 文字卡
[ObservableProperty] private bool _isSelected;          // Tile/Content 选择视觉
public bool HasThumbnailContent => ThumbnailSource != null || !string.IsNullOrEmpty(TextSnippet);
```

### 3. ThumbnailCache（新 Services/ThumbnailCache.cs）

- LRU 有界缓存，上限 **300 项**（300×128×128×4 ≈ 19MB，可接受）。
- `Task<ThumbnailResult?> GetOrCreateAsync(string archivePath, string fullPath, Func<CancellationToken, Task<ThumbnailResult?>> factory, CancellationToken ct)`——工厂模式，缓存命中直接返回；并发请求同 key 去重（`ConcurrentDictionary` + per-key Lazy/Task）。
- `record ThumbnailResult(Bitmap? Bitmap, string? Text)`。
- `Clear()`：关包/切包时调用。

### 4. ThumbnailService（新 Services/ThumbnailService.cs）

```csharp
public enum ThumbnailKind { None, Image, Text }

// 纯静态逻辑（可单测）
public static ThumbnailKind ClassifyKind(PreviewType type)
    => type is PreviewType.Image or PreviewType.AnimatedImage or PreviewType.Svg or PreviewType.IcoGallery ? ThumbnailKind.Image
     : type is PreviewType.Text or PreviewType.Csv or PreviewType.Markdown or PreviewType.Html ? ThumbnailKind.Text
     : ThumbnailKind.None;

public const int ThumbnailSize = 128;
public const long MaxThumbnailEntrySize = 8 * 1024 * 1024; // 图像条目大小上限
public const int TextHeadSize = 4096;
public const int TextSnippetMaxChars = 150;

// 文本截断（纯静态，可单测）：DecodeText 后取前 150 字符，去首尾空白
public static string? TruncateTextSnippet(byte[] head);
```

- 实例管线：`Task GetOrCreateThumbnailAsync(ArchiveItemModel item)`：
  1. `var type = PreviewService.ClassifyPreview(Path.GetExtension(item.Name))` → `ClassifyKind`；`None` 直接返回。
  2. `Kind.Image`：`item.Size > MaxThumbnailEntrySize` 或固实 7z（`IsSevenZipSolid`）→ 返回 null（图标兜底）。`ExtractHeadAsync(archivePath, item.FullPath, maxBytes: min(entry.Size, 8MB), format, password, ct)` → 完整字节 → SKCodec 首帧解码（GIF/WebP 动画取首帧；静态直接解码）→ 等比缩放至 `ThumbnailSize` 内（最长边 128，保持宽高比）→ `Avalonia.Media.Imaging.Bitmap`。`.ico` 特判：走现有 `IcoParser` 取首帧。`.svg` 特判：走 Svg.Skia。
  3. `Kind.Text`：`ExtractHeadAsync(..., TextHeadSize, ...)` → `TextEncodingDetector.DecodeText` → `TruncateTextSnippet`。
  4. 结果写入 `ThumbnailCache` + `item.ThumbnailSource` / `item.TextSnippet`。
- 并发：`SemaphoreSlim(4, 4)` 全局限流 + 每批 `CancellationTokenSource`；切目录/刷新/关包时 `Cancel()` 旧批 + `Clear()` 缓存。
- 分类用**扩展名**（无 I/O），不调用 `ClassifyPreviewByMagicAsync`（避免每条目双重解压）。

### 5. 共享选择层（新 Services/ThumbnailSelectionController.cs）

纯逻辑类（可单测），Tile 与 Content 共用。核心 API：

```csharp
public static SelectionResult HandlePointerPressed(
    ArchiveItemModel? pressedItem,
    bool ctrl, bool shift,
    IReadOnlyList<ArchiveItemModel> orderedItems,  // CurrentEntries 当前顺序
    ArchiveItemModel? anchor,                       // shift 范围的锚点（上次单击项）
    IReadOnlySet<ArchiveItemModel> currentSelection)
```

- 返回 `(Selected, Anchor, AllItemsSelectedFlags)`：无修饰键 → 单选 pressedItem（若已选中则保持）；Ctrl → 切换 pressedItem；Shift → pressedItem 到 anchor 的范围选择（anchor 为 null 时退化为单选）。
- 消费方（MainWindow.axaml.cs）用结果更新 `SelectedEntry`、`SelectedEntries`、逐项 `IsSelected`；双击/右键（先选择再弹菜单）/键盘（Enter=打开或进目录、Back=GoUp、Delete=DeleteFilesCommand，删除用现有确认流程）。
- **选择与预览联动**：更新 `SelectedEntry` 即走既有 `OnSelectedEntryChanged` → `ShowPreviewAsync`，无需新逻辑。

### 6. MainWindow.axaml 变更

**工具栏**（行 874 分隔线前，紧接 ViewMode 组后）新增布局模式按钮组：
```xml
<Button Classes="ToolbarButton ToolbarIcon"
        Command="{Binding CycleLayoutModeCommand}"
        IsEnabled="{Binding IsArchiveLoaded}"
        ToolTip.Tip="{Binding LocalizedStrings[Toolbar_LayoutMode]}">
  <PathIcon Data="{StaticResource IconThumbnails}" Width="13" Height="13"
            Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
</Button>
<TextBlock Text="{Binding LayoutModeLabel}" FontSize="10"
           Foreground="{DynamicResource ThemeTextSecondaryBrush}"
           VerticalAlignment="Center" />
```

**Grid.Row=2 容器区**（DataGrid 行 959-1150 保留不动）：新增两个兄弟容器，三者在同一 Grid.Row=2 按模式切换 `IsVisible`：

1. **Tile**（ScrollViewer + ItemsRepeater，`IsVisible="{Binding IsTileLayout}"`）：
```xml
<ScrollViewer Grid.Row="2" IsVisible="{Binding IsTileLayout}">
  <ItemsRepeater ItemsSource="{Binding CurrentEntries}">
    <ItemsRepeater.ItemsLayout>
      <UniformGridLayout MinItemWidth="136" MinItemHeight="164"
                         ItemsJustification="Start" ItemsStretch="None" />
    </ItemsRepeater.ItemsLayout>
    <ItemsRepeater.ItemTemplate>
      <DataTemplate>
        <!-- 项模板：内容卡 + 文件名 + 选中高亮；PointerPressed/DoubleTapped/ContextMenu
             事件接线见代码后置；选中背景用 IsSelected 绑定主题资源 -->
      </DataTemplate>
    </ItemsRepeater.ItemTemplate>
  </ItemsRepeater>
</ScrollViewer>
```
2. **Content**（ScrollViewer + ItemsRepeater，`IsVisible="{Binding IsContentLayout}"`）：`ItemsLayout` 用 `StackLayout Orientation=Vertical`，行模板为横向布局：左 128px 内容卡 + 右元数据块（`NameDisplay` SemiBold 14px + 4 行"标签: 值"）。元数据标签用 `{Binding LocalizedStrings[Metadata_Key_FileSize], RelativeSource={RelativeSource AncestorType=Window}}` 形式绑定（在 Window 的 `LocalizedStrings` 字典中取值），值绑定 `SizeDisplay/CompressedSizeDisplay/RatioDisplay/LastModifiedDisplay`。
3. DataGrid 加 `IsVisible="{Binding IsDetailsLayout}"`。

**内容卡共用模板**（Tile 卡片位 / Content 左卡位，可用共享资源 `UserControl` 或 DataTemplate 内三态切换）：
- `ThumbnailSource != null` → Image（128 内等比，居中）
- 否则 `TextSnippet` 非空 → Border 卡片（`ThemeSurfaceBgBrush` 底 + `ThemeBorderBrush` 边框）内 TextBlock（`ThemeTextPrimaryBrush`，11-12px，`TextWrapping=Wrap`，`TextTrimming=CharacterEllipsis`，MaxHeight 约束）显示文字卡
- 否则 → `IconSource` 图标居中（兜底）

### 7. MainWindow.axaml.cs 变更

- **拖拽抽取**：把行 423-603 的 `PointerMoved` 闭包主体抽成 `private async Task StartFileDragAsync(PointerPressedEventArgs pressEvent, IReadOnlyList<ArchiveItemModel> items, IReadOnlyList<ArchiveItem> preservedSelection)`；DataGrid 闭包改为调用它；Tile/Content 容器各自挂 `PointerPressed`（Tunnel，记录起点 + 命中项 + 保留选区）+ `PointerMoved`（阈值/开关判断后调用同一方法）。命中项判定：`InputHitTest` 后沿 `VisualTree` 向上找 DataContext 为 `ArchiveItemModel` 的容器。
- **双击**：抽 `private void HandleItemDoubleTap(ArchiveItemModel item)`（进目录/打开文件，复用 FileListGrid_DoubleTapped 逻辑）；Tile/Content 项模板 `DoubleTapped` 接入。
- **键盘**：Tile/Content ScrollViewer 挂 `KeyDown`（Enter/Back/Delete，复用 FileListGrid_KeyDown 逻辑，抽共享方法）。
- **右键菜单**：定义一个共享 `ContextMenu` 资源（Window.Resources，MenuItems 与 DataGrid 现有 981-1005 相同，DataContext=窗口 VM）；DataGrid 与 Tile/Content 均使用；Tile/Content 在右键（PointerPressed 右键或 `ContextRequested`）时先经选择控制器选中命中项，再在命中容器上打开菜单。

### 8. 缩略图加载触发

- `PopulateEntries`（MainWindowViewModel.cs:1412）末尾（仅当当前模式非 Details）调用 `ThumbnailService.StartBatch(CurrentEntries)`。
- 切目录（`OnCurrentFolderChanged` / LoadArchive 完成）、刷新、关包：`Cancel()` + `Clear()`。
- 懒加载：ItemsRepeater 虚拟化天然只实现可见项 → 请求队列自动限于可见项；滚出项取消（`ElementPrepared`/`ElementClearing` 或 `EffectiveViewportChanged` 驱动，worker 按 Avalonia 12 实测选择，见风险 1）。
- 目录项（`IsDirectory`）：跳过（图标）。

### 9. i18n

新增 key（strings.zh-CN.json + strings.en.json 成对，插入文件头 `{` 后，2 空格缩进 UTF-8 无 BOM CRLF）：
- `Toolbar_LayoutMode`（"缩略图布局" / "Thumbnail layout"）
- `LayoutMode_Details`（"详情" / "Details"）、`LayoutMode_Tile`（"平铺" / "Tiles"）、`LayoutMode_Content`（"内容" / "Content"）

全部 5 个 key 登记进 `UpdateLocalizedStrings` 数组（规则 13：XAML 绑定 key 漏登记会空白）。`Metadata_Key_*` 4 个标签 key 若经 `LocalizedStrings` 绑定，同样登记（已存在，不新增）。

### 10. 新图标

`Resources/Icons/AppIcons.axaml` 新增 `IconThumbnails` Geometry（缩略图网格意象）+ `IconTestViewModel.LoadAllIcons()` 登记（规则 8）。无对应矢量时可用 `IconGrid` 变体，但应视觉区分于现有 ViewMode 按钮的 `IconGrid`。

## 任务分解

| # | 任务 | 文件 | 验收标准 |
|---|------|------|---------|
| T1 | 枚举 + AppSettings + VM 布局模式命令/标签/IsXxxLayout + UpdateLocalizedStrings 登记 | MainWindowViewModel.cs、Models/AppSettings.cs | 工具栏按钮三态循环、Label 随语言刷新、重启后模式保持 |
| T2 | ArchiveItemModel 新增 4 属性 + i18n 5 key 成对 | Models/ArchiveItemModel.cs、strings.*.json | 构建通过；key 两文件成对 |
| T3 | ThumbnailCache + ThumbnailService（含纯静态 ClassifyKind/TruncateTextSnippet）+ 单测 | Services/ThumbnailCache.cs、Services/ThumbnailService.cs、tests | 单测通过：分类映射/文本截断/LRU 逐出/大小上限/固实跳过判定 |
| T4 | 工具栏按钮 + Tile/Content 容器 + DataGrid 可见性切换 | MainWindow.axaml | 三模式切换正常，Details 行为零回归 |
| T5 | ThumbnailSelectionController + Tile 项模板 + Tile 事件接线（选择/双击/键盘/右键） | Services/ThumbnailSelectionController.cs、MainWindow.axaml、MainWindow.axaml.cs、tests | 单选/Ctrl/Shift 正确；双击进目录；Enter/Back/Delete；右键菜单；选中项预览联动 |
| T6 | Content 项模板（元数据块）+ 事件接线（复用 T5 控制器） | MainWindow.axaml、MainWindow.axaml.cs | 行显示卡片+5 字段元数据；标签随语言切换 |
| T7 | 拖拽管线抽取 + Tile/Content 接线 | MainWindow.axaml.cs | 两模式拖出正常（覆层/光标/落点解压）；EnableDragExtract=off 不启动 |
| T8 | 缩略图加载触发 + 取消/清缓存 | MainWindowViewModel.cs、MainWindow.axaml.cs | 切模式/切目录后缩略图异步出现；滚出取消；关包清缓存；加密包走会话密码 |
| T9 | 图标 IconThumbnails + 注册 + 测试补齐 + PLAN.md 同步 + 构建/人工 QA | AppIcons.axaml、IconTestViewModel.cs、tests、docs/PLAN.md | 见下方 QA 清单 |

**预估工时：4–5 天**（T3 ~1.5d、T5 ~1.5d、T4+T6 ~1d、T1/T2/T7/T8/T9 ~1d 分摊）。

## Must-NOT-Have

- 非图像非文本类不出内容卡（图标兜底）；**不做格式专属元数据**（图片尺寸/PDF 页数/PE 等，预览面板继续提供）；不做 PDF 缩略图；不做 Magick.NET 格式。
- 缩略图尺寸固定 128px，无尺寸选择器。
- Tile/Content 无排序 UI、无方向键/Home/End/PageUp/PageDown 导航（Enter/Back/Delete 支持）。
- 不修改 WPF 项目；不修改 `MantisZip.Core` 的 `ArchiveEntryExtractor`/`TextEncodingDetector`（仅调用）。
- 不依赖 preview-quick-modes / embedded-thumbnail-preview 的未实施基建。
- 不新增设置 UI 项（布局按钮即入口，无需进设置窗口）。

## QA 清单（人工验证）

1. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过、`lsp_diagnostics` 无错误（规则 12）。
2. 打开含图片+文本+其他文件的 zip：三模式切换正常；图片显示真缩略图、txt/代码/md/csv/html 显示文字卡、其他显示图标。
3. 滚动大目录：Tile/Content 流畅（滚动不卡顿，缩略图渐进出现）。
4. 选择：单选/Ctrl 多选/Shift 范围；切换模式选择保留；切回 Details 高亮正确；选中项预览联动。
5. 双击图片 → 预览；双击目录 → 进入；Enter 同双击；Back 返回上级；Delete 走删除确认。
6. 右键菜单各项（解压/复制名/复制路径/测试/删除）在 Tile/Content 生效。
7. 拖拽出：Tile/Content 拖到资源管理器 → 覆层+光标+解压；`EnableDragExtract=false` 时不启动。
8. 加密压缩包：会话密码生效（缩略图/文字卡能出）。
9. 固实 7z：内容卡全部图标（无卡顿无全量解压）；>8MB 图片条目显示图标。
10. 语言切换：LayoutModeLabel、元数据标签随语言变化。
11. 重启应用：布局模式保持。
12. 空目录/全部目录项：无异常，显示图标。

## 提交与文档要求（AGENTS.md 规则）

- 提交信息 conventional commits，如 `feat(avalonia): 文件列表缩略图查看方式（详情/平铺/内容三态）`。
- **提交前**（规则 1/3）：PLAN.md 对应行更新状态；实施完成后在本计划中标注 ✅ + 在 `docs/PROGRESS.md`（Avalonia 月份条目）+ `docs/progress-avalonia-detail.md`（按日期）各追加一条。
- 版本号不变更（规则 2，用户未要求）。

### 实施开始时必须执行的 PLAN.md 同步（规则 1，计划撰写期无法直接改 docs/ 下文件）

在 `docs/PLAN.md` 待实现设计方案表的 P2 区域（`cli-extract-open-folder` 行之后、`archive-diff` 行之前）插入以下一行（含行首竖线与换行）：

```
| **P2** | 文件列表缩略图查看方式 | [file-list-thumbnails.md](.omo/plans/file-list-thumbnails.md) | 🟡中 | 4-5天 | 📋 2026-08-21 立项：三态布局（详情/平铺/内容）工具栏循环切换 + 持久化；内容卡三档（图像类真缩略图 128px / 文本类文字卡片 / 其余图标）；Content 为自定义行布局显示公共元数据块（复用信息面板 `Metadata_Key_*` 显示名与内存字段，零提取）；Tile/Content 共用 `ThumbnailSelectionController` 选择层 + 拖拽管线抽取复用；`ThumbnailCache` LRU + `ThumbnailService` 后台队列（8MB 上限/固实 7z 跳过/会话密码）；采纳 preview-quick-modes §7 的缓存/降级解码设计，排除 Magick.NET 格式 |
```

## 风险与回退

1. **ItemsRepeater 虚拟化实测**：Avalonia 12 中 ScrollViewer+ItemsRepeater 的 UniformGridLayout 虚拟化需实测确认；若不生效，回退为"滚动事件驱动可见范围请求"（`EffectiveViewportChanged` 计算可见索引，仅对可见项调 `GetOrCreateThumbnailAsync`，滚出项置 null）。**此为最高风险点，T8 前先用最小样例验证**。
2. **右键菜单 DataContext**：ContextMenu 在项模板内的命令绑定需 `RelativeSource` 或窗口级共享菜单资源（本计划已定共享资源方案）。
3. **Svg.Skia/IcoParser 线程**：解码在后台线程进行，Avalonia Bitmap 从流创建是线程安全的；完成后经 UI 线程写 `ThumbnailSource`（MVVM `[ObservableProperty]` 自动封送需确认——若属性更新不在 UI 线程，worker 用 `Dispatcher.UIThread.Post` 回写）。
4. **缓存内存**：300 项 × ~64KB ≈ 19MB，可接受；关包即清。