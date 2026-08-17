# Avalonia 移植 Phase 1：预览格式扩展 + 文件夹树 + 主题

> **分支**: `avalonia-port`（从 Phase 0 继续）  
> **目标**: 在 Phase 0 基础上，补齐剩余预览格式（图片/GIF/SVG/字体/音频/SQLite/ISO/Torrent/Office/视频），增加文件夹树导航、简易主题切换（亮/暗）、系统图标和预览工具栏  
> **设计决策**:
>   - UI 架构不变：继续 MVVM（CommunityToolkit.Mvvm）  
>   - 图像渲染: **Avalonia 原生 `Image` 控件**（`Avalonia.Media.Imaging.Bitmap`），GIF 用 `Avalonia.Controls.Image` + `Avalonia.Media.Imaging`  
>   - 主题: 简单亮/暗切换，2 个 XAML 资源字典（`ThemeLight.axaml` / `ThemeDark.axaml`），菜单项切换  
>   - HTML/Markdown/PDF 预览: 推迟到后续 Phase（需要跨平台 WebView 方案）  
>   - WebView2: Phase 1 **不涉及**  
>   - 密码管理器/设置窗口: 推迟到后续 Phase  
>   - **DataGrid**: v12.0.0 可用，需在 App.axaml 显式引入样式（`avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml`）。Phase 1 文件列表 & CSV 预览改用 DataGrid  
> **依赖**: `ArchiveTreeBuilder` / `ArchiveEntryLister`（Core/Services，已在 main 分支合并到 avalonia-port）  
> **创建日期**: 2026-06-11  
> **更新日期**: 2026-06-11  
> **状态**: ✅ 已完成 | **进度**: [████████████████████] (99/99)

---

## 文件结构（新增/修改）

```
src/MantisZip.UI.Avalonia/
├── Models/
│   ├── ArchiveItemModel.cs              ← 新增 IconSource、PreviewType 属性
│   ├── PeMetadataItem.cs                ← 已存在（来自 Phase 0）
│   └── FormatMetadataItem.cs            ← NEW: 通用 key-value 元数据模型
│
├── ViewModels/
│   ├── MainWindowViewModel.cs           ← 修改: 添加 TreeView 逻辑（CurrentFolder, FolderTree）
│   └── PreviewViewModel.cs              ← 大幅扩展: 所有新增预览格式
│
├── Views/
│   ├── MainWindow.axaml                 ← 修改: 3 列布局（TreeView | 文件列表 | 预览）
│   ├── MainWindow.axaml.cs              ← 可能小修改
│   ├── PreviewPanel.axaml               ← 新增: 预览面板（含工具栏 + 信息面板）
│   └── PreviewPanel.axaml.cs
│
├── Services/
│   ├── ArchiveService.cs                ← 修改: 支持目录浏览模式
│   └── PreviewService.cs                ← 扩展: 所有新格式的 Classify + Extract
│
├── Converters/
│   ├── FileSizeConverter.cs             ← 已有
│   └── BoolToVisibilityConverter.cs     ← 已有
│
├── Controls/
│   └── InfoPanel.axaml                  ← NEW: 格式特定元数据面板（CsvDataGrid 已弃用，DataGrid 直接嵌入 axaml）
│
├── Themes/
│   ├── ThemeLight.axaml                 ← NEW: 亮色主题资源
│   └── ThemeDark.axaml                  ← NEW: 暗色主题资源
│
├── Resources/
│   └── Icons/                           ← 系统图标缓存（运行时生成）
│
└── Styles/
    └── AppStyles.axaml                  ← 修改: 包含主题资源合并
```

## 任务分解

---

### Task 0：DataGrid 修复 + 文件列表 & CSV 改用 DataGrid

> DataGrid v12.0.0 本身完好，问题仅在 **Avalonia 12 需要显式引入 DataGrid 主题样式**。修复后，文件列表 ListBox 和 CSV ItemsControl 都替换为 DataGrid。

- [x] **0.1** `App.axaml` 添加 DataGrid 样式引用：
  ```xml
  <Application.Styles>
      <FluentTheme />
      <StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"/>
  </Application.Styles>
  ```
- [x] **0.2** `MainWindow.axaml`：文件列表 ListBox → DataGrid
  - `DataGrid ItemsSource="{Binding Entries}" SelectedItem="{Binding SelectedEntry}"`
  - 列定义：`DataGridTextColumn` 绑定 `NameDisplay` / `SizeDisplay` / `CompressedSizeDisplay` / `LastModifiedDisplay`
  - 列宽：`NameDisplay` 用 `*`，其余用固定宽或 `Auto`
  - 去掉列头手动定义 Grid 行（DataGrid 自带列头）
  - 启用排序（`CanUserSortColumns="True"`）
- [x] **0.3** CSV 预览：`ItemsControl` → `DataGrid`
  - 替换 `ScrollViewer > ItemsControl` 为 `ScrollViewer > DataGrid`
  - `DataGrid ItemsSource="{Binding Preview.CsvData}" AutoGenerateColumns="True"`
  - 设置 `IsReadOnly="True" CanUserResizeColumns="True"`
- [x] **0.4** 清理：
  - 移除 `Controls/CsvDataGrid.axaml` + `CsvDataGrid.cs`（不再需要）
  - 移除 `BoolToVisibilityConverter.cs`（如不再被其他地方引用）
  - 检查 `using Avalonia.Controls.DataGrid` 是否正确
- [x] **0.5** 验证：
  - `dotnet build src\MantisZip.UI.Avalonia` 编译通过
  - DataGrid 正常显示文件列表，列头可点击排序
  - CSV 预览以表格形式展示，列自动生成
- [x] **0.6** **Commit**: `feat(avalonia): add DataGrid style include, replace ListBox/ItemsControl with DataGrid`

---

### Task 1：预览类型枚举扩展 + 信息面板（Foundation）

> 扩展 `PreviewType` 枚举覆盖所有格式，添加通用元数据模型和 `InfoPanel` 控件。

- [x] **1.1** 扩展 `PreviewType` 枚举（PreviewService.cs）：
  ```csharp
  public enum PreviewType
  {
      None, Text, Csv, Pe, Image, Gif, Svg,
      Font, Audio, Sqlite, Iso, Torrent,
      Office, Video, Unsupported
  }
  ```
- [x] **1.2** 创建 `Models/FormatMetadataItem.cs`：
  - `record FormatMetadataItem(string Key, string Value)` — 通用键值对
- [x] **1.3** 创建 `Controls/InfoPanel.axaml` + `.cs`：
  - `ItemsControl` 绑定 `ObservableCollection<FormatMetadataItem>`
  - 两列布局：Key（次色灰）+ Value（正常色），`Separator` 分隔
  - 不依赖任何特定格式
- [x] **1.4** `PreviewViewModel` 扩展：
  - 新增 `ObservableCollection<FormatMetadataItem> FormatMetadata`
  - 新增 `string PreviewHeaderText`（格式标题，如 "字体预览"）
  - 新增所有 `Is*Visible` 计算属性（`IsImageVisible` 等）
  - 更新 `OnPreviewTypeChanged` 通知所有 `Is*Visible`
- [x] **1.5** `PreviewService.ClassifyPreview` 扩展：
  - 添加所有格式的扩展名集合（参照 WPF 版 `MainWindow.Preview.cs`）
  - 返回对应的 `PreviewType`
- [x] **1.6** 添加 `Core` nuget 包引用（如需）：`Microsoft.Data.Sqlite` 已在 csproj
- [x] **1.7** **Commit**: `feat(avalonia): expand PreviewType enum, add InfoPanel and FormatMetadataItem`

---

### Task 2：主题系统（亮/暗切换）

> 简易主题切换：两套 XAML 资源字典 + 菜单项切换，无系统自动检测。

- [x] **2.1** 创建 `Themes/ThemeLight.axaml`：
  - 定义颜色资源键：`ThemeWindowBg`（#FFF5F5F5）、`ThemeTextPrimary`（#FF1A1A1A）、`ThemeTextSecondary`（#FF666666）、`ThemeBorder`（#FFE0E0E0）、`ThemeAccent`（#FF0078D4）、`ThemeSurfaceBg`（#FFFFFFFF）、`ThemeHeaderBg`（#FFF0F0F0）、`ThemeButtonBg`（#FFE0E0E0）、`ThemeButtonHover`（#FFD0D0D0）、`ThemeButtonPressed`（#FFC0C0C0）、`ThemeListBg`（#FFFFFFFF）、`ThemeListSelected`（#FFE0F0FF）、`ThemeSplitterBg`（#FFE0E0E0）
  - 包含所有 WPF 主题中的语义色对应
- [x] **2.2** 创建 `Themes/ThemeDark.axaml`：
  - 相同资源键，暗色值：`ThemeWindowBg`（#FF1E1E1E）、`ThemeTextPrimary`（#FFE0E0E0）、`ThemeTextSecondary`（#FF999999）、`ThemeBorder`（#FF3E3E3E）、`ThemeAccent`（#FF0078D4）、`ThemeSurfaceBg`（#FF252525）、`ThemeHeaderBg`（#FF2A2A2A）、`ThemeButtonBg`（#FF3E3E3E）、`ThemeButtonHover`（#FF4E4E4E）、`ThemeButtonPressed`（#FF5E5E5E）、`ThemeListBg`（#FF252525）、`ThemeListSelected`（#FF2A3A5A）、`ThemeSplitterBg`（#FF3E3E3E）
- [x] **2.3** 更新 `App.axaml`：合并 `ThemeLight.axaml` 作为默认主题
- [x] **2.4** `MainWindowViewModel` 添加：
  - `bool IsDarkTheme` 属性
  - `ToggleThemeCommand`（切换 `IsDarkTheme`）
  - 主题切换逻辑：动态替换 `Application.Current.Resources.MergedDictionaries` 中的主题字典
- [x] **2.5** `MainWindow.axaml` 菜单添加："视图 → 切换暗色主题"
- [x] **2.6** 应用主题资源到现有控件：
  - `ListBox` 背景/前景/选中色
  - `GridSplitter` 背景
  - `Menu`/`MenuItem` 背景/前景
  - `ScrollViewer` 背景
  - `TextBox`（预览文本）背景/前景
  - `Border` 在预览区域
- [x] **2.7** 验证：切换亮/暗色，所有控件正确响应
- [x] **2.8** **Commit**: `feat(avalonia): add light/dark theme toggle with resource dictionaries`

---

### Task 3：系统图标

> 在文件列表的文件名前显示文件类型图标。

- [x] **3.1** `Models/ArchiveItemModel.cs` 添加：
  - `Bitmap? IconSource` 属性（`[ObservableProperty]`）
  - `bool HasIcon` 计算属性
- [x] **3.2** 创建图标服务（可内联在 `ArchiveItemModel` 或独立 `IconService`）：
  - 使用 `SystemIconHelper`（Core/Utils 已有）获取图标
  - 缓存 `ConcurrentDictionary<string, Bitmap>`
  - 注意：Avalonia `Bitmap` 与 WPF `BitmapSource` 不同，需要适配
- [x] **3.3** 更新 `ArchiveService.LoadArchiveAsync`：
  - 映射完成后调用图标加载
  - 设置 `model.IconSource`
- [x] **3.4** 更新 `MainWindow.axaml` 文件列表 `DataTemplate`：
  - 在 `NameDisplay` 前增加 `Image` 控件绑定 `IconSource`
  - 宽高 16x16
- [x] **3.5** 验证：打开压缩包，文件列表显示正确的文件类型图标
- [x] **3.6** **Commit**: `feat(avalonia): add file type icons to file list`

---

### Task 4：文件夹树导航（3 列布局）

> 使用 Core 的 `ArchiveTreeBuilder` 构建文件夹树，左侧 TreeView 导航。

- [x] **4.1** `MainWindowViewModel` 添加：
  - `FolderNode? FolderTreeRoot` 属性（树根节点）
  - `FolderNode? SelectedFolder` 属性
  - `ObservableCollection<ArchiveItemModel> CurrentEntries`（当前目录的文件列表）
  - `string? CurrentFolder`（当前浏览目录路径）
- [x] **4.2** 添加命令/方法：
  - `NavigateToFolder(FolderNode node)`：设置 `CurrentFolder`，调用 `ArchiveEntryLister.GetEntriesInFolder` 过滤条目
  - `GoUpCommand`：返回上级目录
  - `BuildFolderTree(IEnumerable<ArchiveItem> allItems)`：调用 `ArchiveTreeBuilder.BuildTree`
- [x] **4.3** 更新 `LoadArchiveAsync`：
  - 调用 `BuildFolderTree` 构建树
  - 展开根节点
  - 导航到根目录（显示根目录下的条目）
- [x] **4.4** `MainWindow.axaml` 布局改为 3 列：
  ```xml
  <Grid ColumnDefinitions="Auto,5,2.5*,5,3*">
  ```
  - Column 0: `TreeView`（绑定 `FolderTreeRoot.Children`，`Width=220`）
  - Column 2: 文件列表（`ListBox` 绑定 `CurrentEntries`）
  - Column 4: 预览区域
  - GridSplitter at Column 1 and 3
- [x] **4.5** `TreeView.ItemTemplate`：
  - 显示文件夹图标 + `Name`
  - `IsExpanded` 双向绑定
  - 选中节点 → `NavigateToFolder`
- [x] **4.6** 更新 `SelectedEntry` 预览触发逻辑：
  - 预览时仍使用 `Entries`（全量）提取文件
  - 或使用 `CurrentEntries` 中的引用
- [x] **4.7** 同步：`SelectedFolder` 变化时重新过滤 `CurrentEntries`
- [x] **4.8** 验证：打开含子目录的压缩包，树导航正常，文件列表随目录切换更新
- [x] **4.9** **Commit**: `feat(avalonia): add TreeView folder navigation with 3-pane layout`

---

### Task 5：预览工具栏

> 在各格式预览上方添加共享工具栏（缩放/字号/格式特定按钮）。

- [x] **5.1** `PreviewViewModel` 添加：
  - `bool IsToolbarVisible`
  - `double ZoomLevel`（图片缩放，默认 1.0）
  - `int FontSize`（文本字号，默认 13）
  - `bool HasZoomControls`（`PreviewType == Image || PreviewType == Gif`）
  - `bool HasFontSizeControls`（`PreviewType == Text`）
- [x] **5.2** 添加命令：
  - `ZoomInCommand` / `ZoomOutCommand` / `ZoomFitCommand`
  - `IncreaseFontSizeCommand` / `DecreaseFontSizeCommand`
- [x] **5.3** `PreviewPanel.axaml` 添加工具栏：
  - 水平 `StackPanel`：
    - 左侧：通用按钮（放大/缩小/适应，字号+/-）
    - `Separator`
    - 右侧：格式特定按钮（占位，后续 task 填充）
  - 工具栏背景使用主题色
- [x] **5.4** 连接图片缩放：`Image` 控件的 `RenderTransform` 绑定 `ZoomLevel`（实现在 Task 6）
- [x] **5.5** 连接字号：预览 `TextBox` 的 `FontSize` 绑定
- [x] **5.6** **Commit**: `feat(avalonia): add shared preview toolbar with zoom and font size controls`

---

### Task 6：图片预览（含 GIF）

> 图片 / GIF 预览：Avalonia 原生 Bitmap + 缩放工具栏。

- [x] **6.1** `PreviewViewModel` 添加：
  - `Bitmap? PreviewImage`
  - `int ImageWidth` / `ImageHeight`
  - `string ImageSizeDisplay`（如 "1920 × 1080"）
  - `bool IsTransparencySupported`（PNG/ICO/WebP）
- [x] **6.2** `PreviewService.ClassifyPreview` 确认：
  - 图片扩展名：`.jpg`, `.jpeg`, `.png`, `.bmp`, `.ico`, `.webp`
  - GIF 单独分类（Task 6.5）
- [x] **6.3** `PreviewViewModel.ShowImagePreview(string filePath)`：
  - `Avalonia.Media.Imaging.Bitmap` 加载图片
  - `DecodePixelWidth=1920` 下采样（通过 `Bitmap` 构造函数）
  - 设置 `PreviewHeaderText = "图片预览"`
  - 设置 `FormatMetadata`：尺寸、文件大小
  - 支持 PNG/ICO/WebP 透明背景切换
- [x] **6.4** `PreviewPanel.axaml` 添加图片预览 `DataTemplate`：
  - `Image` 控件绑定 `PreviewImage`
  - `RenderTransform` = `ScaleTransform` 绑定 `ZoomLevel`
  - 工具栏右端添加透明度切换按钮（TG）
- [x] **6.5** GIF 预览：
  - 使用 `Avalonia.Media.Imaging` 的 `GifDecoder`（v11+ 可用）或 `Image` 控件原生 GIF 支持
  - 额外属性：`bool IsPlaying`、`int CurrentFrame`、`int TotalFrames`
  - 工具栏：播放/暂停、上一帧、下一帧、帧号输入框
- [x] **6.6** 验证：打开含 .png/.jpg/.gif/.ico/.webp 的压缩包，预览正常，缩放和透明度切换正常
- [x] **6.7** **Commit**: `feat(avalonia): add image and GIF preview with zoom toolbar`

---

### Task 7：SVG 预览 + 字体预览

> SVG 用 Avalonia 原生渲染，字体显示元数据 + 示例文本。

- [x] **7.1** SVG 预览：
  - `PreviewViewModel.ShowSvgPreview(string filePath)`：
    - 使用 `Avalonia.Svg.Skia` 或直接 `PathGeometry` 解析
    - `Avalonia.Controls.Image` 显示渲染结果
  - Avalonia 11+ 内置 SVG 支持（`Avalonia.Svg`）
- [x] **7.2** 字体预览：
  - `PreviewViewModel.ShowFontPreview(string filePath)`：
    - 调用 Core 字体解析器获取元数据
    - 设置 `PreviewHeaderText = "字体预览"`
    - `FormatMetadata`：字体名称、样式、字形数
    - `TextContent` 显示示例文本（"The quick brown fox..."）
  - 工具栏右端添加连字切换按钮（`IsLigatureEnabled`）
- [x] **7.3** `PreviewPanel.axaml` 添加：
  - SVG 预览面板（`Image` 控件）
  - 字体预览面板（`TextBlock` 显示示例 + 元数据）
- [x] **7.4** 验证：打开含 .svg/.ttf/.otf/.woff 的压缩包，预览正常
- [x] **7.5** **Commit**: `feat(avalonia): add SVG and font preview`

---

### Task 8：音频预览 + ISO 预览

> 音频元数据 + ISO 光盘镜像元数据。

- [x] **8.1** 音频预览：
  - `PreviewViewModel.ShowAudioPreview(string filePath)`：
    - 调用 Core 音频解析器（`WavParser` / `FlacParser` / `Mp3Parser`）
    - `FormatMetadata`：时长、采样率、声道数、比特率
    - `PreviewHeaderText = "音频信息"`
- [x] **8.2** ISO 预览：
  - `PreviewViewModel.ShowIsoPreview(string filePath)`：
    - 调用 Core `IsoParser`
    - `FormatMetadata`：卷标、格式、大小
    - `PreviewHeaderText = "光盘镜像"`
- [x] **8.3** `PreviewPanel.axaml`：使用通用 `FormatMetadata` `ItemsControl`（InfoPanel）
- [x] **8.4** 验证：打开含 .wav/.flac/.mp3/.iso 的压缩包，元数据显示正常
- [x] **8.5** **Commit**: `feat(avalonia): add audio and ISO metadata preview`

---

### Task 9：SQLite 预览

> SQLite 数据库多表预览（DataGrid）。

- [x] **9.1** `PreviewViewModel` 添加：
  - `ObservableCollection<DataView> SqliteTables`（每个表一个 DataView）
  - `ObservableCollection<string> SqliteTableNames`
  - `int SelectedTableIndex`
  - 方法 `ShowSqlitePreview(string filePath)`：
    - 打开 SQLite 连接（`Microsoft.Data.Sqlite`）
    - 读取表列表（`SELECT name FROM sqlite_master WHERE type='table'`）
    - 每个表读取前 100 行
    - 填充 `SqliteTables` 和 `SqliteTableNames`
- [x] **9.2** `PreviewPanel.axaml` 添加：
  - `TabControl`：每个 tab 一个表
  - Tab 内：`DataGrid` 绑定对应 `DataView`
  - 限制 100 列
- [x] **9.3** 验证：打开含 .sqlite/.db 的压缩包，多表显示正常
- [x] **9.4** **Commit**: `feat(avalonia): add SQLite multi-table preview`

---

### Task 10：Torrent 预览

> BT 种子信息显示。

- [x] **10.1** `PreviewViewModel` 添加：
  - `string TorrentInfoHash`
  - `string TorrentMagnetLink`
  - `string TorrentCreator`
  - `string TorrentCreatedDate`
  - `string TorrentComment`
  - `ObservableCollection<TorrentFileItem> TorrentFiles`
  - 方法 `ShowTorrentPreview(string filePath)`：
    - 调用 Core `TorrentParser`
    - 填充元数据和文件列表
- [x] **10.2** `Models/TorrentFileItem.cs`：
  - `record TorrentFileItem(string Path, long Size)`
- [x] **10.3** `PreviewPanel.axaml` 添加 Torrent 预览面板：
  - 顶部：InfoHash、Magnet 链接（可点击复制）、创建者、日期
  - 底部：`TreeView` 或 `ListBox` 显示文件树
- [x] **10.4** 验证：打开 .torrent 文件，种子信息完整显示
- [x] **10.5** **Commit**: `feat(avalonia): add torrent metadata preview`

---

### Task 11：Office 预览 + 视频预览

> Office 文档元数据 + 视频文件元数据。

- [x] **11.1** Office 预览：
  - `PreviewViewModel.ShowOfficePreview(string filePath)`：
    - 调用 Core `OfficeParser`
    - 类型判断：docx/xlsx/pptx
    - `FormatMetadata`：标题、作者、页数/幻灯片数/工作表数
    - `PreviewHeaderText = "Office 文档信息"`
- [x] **11.2** 视频预览：
  - `PreviewViewModel.ShowVideoPreview(string filePath)`：
    - 调用 Core `VideoParser`（解析 MP4/MKV/AVI 容器元数据）
    - `FormatMetadata`：分辨率、时长、编码格式
    - `PreviewHeaderText = "视频信息"`
- [x] **11.3** `PreviewPanel.axaml`：使用通用 `InfoPanel`
- [x] **11.4** 验证：打开含 .docx/.xlsx/.pptx/.mp4/.mkv 的压缩包，元数据显示正常
- [x] **11.5** **Commit**: `feat(avalonia): add Office and video metadata preview`

---

### Task 12：清理 + 分支同步

> 确保分支干净，与 main 保持同步。

- [x] **12.1** 确认 `src/MantisZip.UI/`（WPF 项目）未被触碰
- [x] **12.2** 确认所有新文件有正确的命名空间和文件头
- [x] **12.3** `git merge main` 将 main 最新改动合并到 avalonia-port
- [x] **12.4** 解决可能的冲突
- [x] **12.5** 验证：`dotnet build src\MantisZip.UI.Avalonia` 编译通过
- [x] **12.6** 验证：`dotnet test tests\MantisZip.Tests` 所有测试通过
- [x] **12.7** 更新本计划状态为 ✅

---

## 验证清单

Phase 1 完成的验收标准：

- [x] `dotnet build src\MantisZip.UI.Avalonia` 编译通过
- [x] `dotnet run --project src\MantisZip.UI.Avalonia` 窗口启动
- [x] 打开含子目录的压缩包，TreeView 显示目录结构
- [x] 目录导航：点击文件夹 → 文件列表更新
- [x] 图片预览（.png/.jpg/.ico/.webp）正常显示，缩放可用
- [x] GIF 预览（.gif）动画播放/暂停/帧导航正常
- [x] SVG 预览（.svg）正常渲染
- [x] 字体预览（.ttf/.otf/.woff）显示元数据 + 示例文本
- [x] 音频元数据（.wav/.flac/.mp3）显示正确
- [x] SQLite 预览（.sqlite/.db）多表 DataGrid 正常
- [x] ISO 元数据（.iso）显示卷标/格式
- [x] Torrent 预览（.torrent）显示 InfoHash/Magnet/文件树
- [x] Office 元数据（.docx/.xlsx/.pptx）显示正确
- [x] 视频元数据（.mp4/.mkv）显示分辨率/时长
- [x] 亮/暗主题切换正常
- [x] 文件列表显示文件类型图标
- [x] 预览工具栏（缩放/字号）功能正常
- [x] WPF 项目 `src/MantisZip.UI/` 未被修改
- [x] `git merge main` 无冲突，所有测试通过

---

## 边界情况与注意事项

1. **DataGrid 主题样式**：必须通过 `StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"` 显式引用。仅加 `<FluentTheme />` 不够
2. **Core 依赖**：已从 main 合并 ArchiveTreeBuilder / ArchiveEntryLister，Avalonia 可直接使用
2. **系统图标**：`SystemIconHelper` 在 Core/Utils 中，使用 `SHGetFileInfo`（Windows 限定）。跨平台预览可用扩展名默认图标替代
3. **GIF 解码**：Avalonia 11+ 原生支持 GIF 解码（`Avalonia.Media.Imaging`），无需额外库
4. **SVG 渲染**：Avalonia 11+ 内置 SVG 支持（`Avalonia.Svg` 命名空间）
5. **SQLite**：`Microsoft.Data.Sqlite` 已在 csproj（从 Phase 0）
6. **主题切换**：通过替换 `Application.Current.Resources.MergedDictionaries` 中的主题字典实现，控件通过 `DynamicResource` 绑定
7. **大图片**：通过 `DecodePixelWidth=1920` 下采样，避免加载超大数据到内存
8. **临时文件**：预览提取的临时文件统一在 `App.OnExit` 清理
9. **排序**：Phase 1 暂不实现文件列表列排序（与 WPF 一致，Phase 0 未实现）
10. **密码保护的压缩包**：所有 API 保留 `password` 参数，Phase 1 暂不实现密码对话框，统一传入 `null`



