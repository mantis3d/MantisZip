# 元数据信息面板重构（可配置系统）

> **状态**: 📋 计划中
> **独立于**: Office 内容预览，互不阻塞

## TL;DR

将现有硬编码的信息面板改为可配置系统，让用户控制每个文件类型显示哪些字段、显示位置、同行排列。

**设计决定**（已确认）：
1. 通用字段作为独立类型"通用文件信息"与其他类型平级
2. 内容区顶部横条位于格式自身内容头部 **之下**
3. `enabled` 开关只控制该类型特有字段，不影响通用文件信息
4. PE 预览的现有 `PeTitle`/`PeSubtitle` 将被新系统替代（不再重复）
5. 内容区顶部是统一的横条，高度自适应，随内容滚动

## 信息面板布局

```
Common enabled = true, Format enabled = true:
┌──────────────────────────────────┐
│ ── 文件信息 ──                    │
│ 文件名: report.docx              │
│ 大小: 1.2 MB | 压缩后: 856 KB    │
│                                  │
│ ── 文档信息 ──                    │
│ 标题: 项目报告                    │
│ 作者: 张三 | 创建日期: 2025-03-01│
└──────────────────────────────────┘

Common enabled = true, Format disabled:
┌──────────────────────────────────┐
│ ── 文件信息 ──                    │
│ 文件名: report.docx              │
│ 大小: 1.2 MB | 压缩后: 856 KB    │
└──────────────────────────────────┘
```

## 内容区布局

```
┌──────────────────────────────────┐
│ 工具栏                            │
├──────────────────────────────────┤
│ PE 产品名（格式自身头部）          │ ← 格式自己的内容
│ 架构: x64 | 子系统: GUI           │
├──────────────────────────────────┤
│ 修改日期: 2025-03-15             │ ← contentTop 横条（滚动时跟随）
├──────────────────────────────────┤
│ PE 元数据列表...                  │ ← 主要内容
│                                  │
│    (滚动...)                      │
│                                  │
└──────────────────────────────────┘
```

注意：contentTop 横条放在内容区 ScrollViewer **内部**，跟随主内容滚动，不固定。

## 数据模型

### 存储

```jsonc
{
  "metadataPanel": {
    // 每个类型一个配置，包括"通用文件信息"
    "common": {
      "enabled": true,
      "fields": {
        "FileName":         { "position": "infoPanel", "row": 1 },
        "FileSize":         { "position": "infoPanel", "row": 2 },
        "CompressedSize":   { "position": "infoPanel", "row": 2 },
        "CompressionRatio": { "position": "hidden" },
        "FileModifiedDate": { "position": "infoPanel", "row": 3 }
      }
    },
    "image": {
      "enabled": true,
      "fields": {
        "Dimensions": { "position": "infoPanel", "row": 1 },
        "ImageDpi":   { "position": "infoPanel", "row": 2 },
        "FileSize":   { "position": "hidden" }   // 可以引用通用字段但不推荐
      }
    },
    "docx": {
      "enabled": true,
      "fields": {
        "Title":     { "position": "infoPanel", "row": 1 },
        "Author":    { "position": "infoPanel", "row": 2 },
        "PageCount": { "position": "infoPanel", "row": 2 },
        "Subject":   { "position": "hidden" },
        "CreatedDate":{"position": "infoPanel", "row": 3 }
      }
    }
    // ... audio, video, pptx, xlsx, font, torrent, iso, sqlite, pe
  }
}
```

注意：`"common"` 是一个特殊类型，与其他格式平级存放。渲染时信息面板先渲染 `common` 的 infoPanel 字段，再渲染当前格式的 infoPanel 字段，中间用分隔线隔开。

```csharp
public class MetadataPanelSettings
{
    public Dictionary<string, TypeMetadataConfig> Types { get; set; } = new();
    // 键："common" | "image" | "docx" | "audio" | "video" | "pptx"
    //      | "xlsx" | "font" | "torrent" | "iso" | "sqlite" | "pe"
}

public class TypeMetadataConfig
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, FieldConfig> Fields { get; set; } = new();
}

public class FieldConfig
{
    public string Position { get; set; } = "infoPanel";
    public int Row { get; set; }
}
```

### 枚举字段键

```csharp
public static class MetadataKeys
{
    // ── 通用文件信息 ──
    public const string FileName = "FileName";
    public const string FileSize = "FileSize";
    public const string CompressedSize = "CompressedSize";
    public const string CompressionRatio = "CompressionRatio";
    public const string FileModifiedDate = "FileModifiedDate";

    // ── 文档类 ──
    public const string Title = "Title";
    public const string Author = "Author";
    public const string Subject = "Subject";
    public const string PageCount = "PageCount";
    public const string SheetCount = "SheetCount";
    public const string SlideCount = "SlideCount";
    public const string CreatedDate = "CreatedDate";
    public const string DocModifiedDate = "DocModifiedDate";

    // ── 多媒体 ──
    public const string Duration = "Duration";
    public const string SampleRate = "SampleRate";
    public const string Channels = "Channels";
    public const string Bitrate = "Bitrate";
    public const string BitDepth = "BitDepth";
    public const string Artist = "Artist";
    public const string Album = "Album";
    public const string Resolution = "Resolution";
    public const string Codec = "Codec";

    // ── 图片 ──
    public const string Dimensions = "Dimensions";
    public const string ImageDpi = "ImageDpi";
    public const string FrameCount = "FrameCount";

    // ── 种子 ──
    public const string InfoHash = "InfoHash";
    public const string MagnetLink = "MagnetLink";
    public const string TrackerUrl = "TrackerUrl";
    public const string FileCount = "FileCount";
    public const string TotalSize = "TotalSize";
    public const string IsPrivate = "IsPrivate";
    public const string CreatedBy = "CreatedBy";

    // ── 数据库 ──
    public const string TableCount = "TableCount";

    // ── ISO ──
    public const string VolumeLabel = "VolumeLabel";
    public const string IsoFormat = "IsoFormat";

    // ── PE ──
    public const string ProductName = "ProductName";
    public const string CompanyName = "CompanyName";
    public const string FileVersion = "FileVersion";
    public const string ProductVersion = "ProductVersion";
    public const string Architecture = "Architecture";
    public const string Subsystem = "Subsystem";
    public const string Description = "Description";

    // ── 字体 ──
    public const string FontName = "FontName";
    public const string FontStyle = "FontStyle";
    public const string GlyphCount = "GlyphCount";
}
```

### 字段注册

```csharp
public static class MetadataRegistry
{
    private static readonly Dictionary<string, MetadataFieldDef[]> _fields = new();

    public record MetadataFieldDef(
        string Key,
        string DisplayName,
        string Category          // 用于设置 UI 分组
    );

    static MetadataRegistry()
    {
        Register("common", new[]
        {
            new("FileName",          "文件名",       "文件信息"),
            new("FileSize",          "大小",         "文件信息"),
            new("CompressedSize",    "压缩后大小",   "文件信息"),
            new("CompressionRatio",  "压缩率",       "文件信息"),
            new("FileModifiedDate",  "修改日期",     "文件信息"),
        });

        Register("docx", new[]
        {
            new("Title",            "标题",         "文档信息"),
            new("Author",           "作者",         "文档信息"),
            new("Subject",          "主题",         "文档信息"),
            new("PageCount",        "页数",         "文档信息"),
            new("CreatedDate",      "创建日期",     "文档信息"),
            new("DocModifiedDate",  "修改日期",     "文档信息"),
        });

        Register("pe", new[]
        {
            new("ProductName",     "产品名称",     "PE 信息"),
            new("CompanyName",     "公司",         "PE 信息"),
            new("FileVersion",     "文件版本",     "PE 信息"),
            new("ProductVersion",  "产品版本",     "PE 信息"),
            new("Architecture",    "架构",         "PE 信息"),
            new("Subsystem",       "子系统",       "PE 信息"),
            new("Description",     "说明",         "PE 信息"),
        });

        // ... audio, video, image, font, torrent, iso, sqlite, etc.
    }
}
```

## 渲染引擎

### 流程

```
ShowDocx(info)
  ├── 准备 allValues: Dictionary<string, string?>
  │     { "Title"→"项目报告", "Author"→"张三",
  │       "FileSize"→"1.2 MB", "CompressedSize"→"856 KB", ... }
  │
  └── MetadataRenderEngine.Render(allValues, configs)
        │
        ├── 信息面板 InfoPanelRows:
        │   ├── 通用段（configs["common"].enabled 时）
        │   │   └── 筛选 position=infoPanel → 按 row 分组
        │   ├── 分隔线（如果通用段和格式段都有内容）
        │   └── 格式段（configs["docx"].enabled 时）
        │       └── 筛选 position=infoPanel → 按 row 分组
        │
        └── 内容区顶部 ContentTopItems:
            ├── 通用段（contentTop 字段）
            └── 格式段（contentTop 字段）
            └── 合并为扁平列表，按(通用排前, 格式排后) + 注册顺序
```

### 输出模型

```csharp
// 信息栏的一个分区（通用一段 / 格式一段）
public class MetadataSection
{
    public string Title { get; set; } = string.Empty;   // "文件信息" / "文档信息"
    public ObservableCollection<InfoPanelRow> Rows { get; set; } = new();
}

// 单行（可能包含多个同行字段）
public class InfoPanelRow
{
    public int Row { get; set; }
    public ObservableCollection<FormatMetadataItem> Items { get; set; } = new();
}

// 单个键值对
public class FormatMetadataItem
{
    public string Key { get; set; }
    public string Value { get; set; }
}
```

### XAML 绑定

**信息面板**:
```xml
<!-- 信息栏（两个分区） -->
<ItemsControl ItemsSource="{Binding MetadataSections}">
  <ItemsControl.ItemTemplate>
    <DataTemplate x:DataType="vm:MetadataSection">
      <StackPanel>
        <!-- 分区标题 -->
        <TextBlock Text="{Binding Title}"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                   Margin="0,4,0,2" />

        <!-- 各行 -->
        <ItemsControl ItemsSource="{Binding Rows}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:InfoPanelRow">
              <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,2">
                <ItemsControl ItemsSource="{Binding Items}">
                  <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                      <StackPanel Orientation="Horizontal" Spacing="4" />
                    </ItemsPanelTemplate>
                  </ItemsControl.ItemsPanel>
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="models:FormatMetadataItem">
                      <TextBlock>
                        <Run Text="{Binding Key}" FontWeight="Bold" />
                        <Run Text=": " />
                        <Run Text="{Binding Value}" />
                      </TextBlock>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </StackPanel>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- 分区间的分隔线 -->
        <Separator Margin="0,4" />
      </StackPanel>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

**内容区顶部**（统一的横条，放在内容区 ScrollViewer 内部最上方）:
```xml
<ScrollViewer Grid.Row="1" Grid.Column="0">
  <StackPanel>
    <!-- 内容区顶部元数据横条（统一位置，随内容滚动） -->
    <Border IsVisible="{Binding IsContentTopVisible}"
            Background="{DynamicResource ThemeSurfaceBgBrush}"
            BorderBrush="{DynamicResource ThemeBorderBrush}"
            BorderThickness="0,0,0,1"
            Padding="8,4">
      <ItemsControl ItemsSource="{Binding ContentTopItems}">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate>
            <WrapPanel />
          </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="models:FormatMetadataItem">
            <TextBlock Margin="0,0,16,0"
                       Foreground="{DynamicResource ThemeTextSecondaryBrush}">
              <Run Text="{Binding Key}" FontWeight="Bold" />
              <Run Text=": " />
              <Run Text="{Binding Value}" />
            </TextBlock>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </Border>

    <!-- 现有预览内容区（每个格式的内容面板，互斥可见） -->
    <Grid>
      <!-- Text preview -->
      <!-- PE preview -->
      <!-- Image preview -->
      <!-- ... -->
    </Grid>
  </StackPanel>
</ScrollViewer>
```

### PE 迁移

现有 PE 内容区：
```xml
<TextBlock Text="{Binding PeTitle}" FontSize="20" FontWeight="Bold" />
<TextBlock Text="{Binding PeSubtitle}" FontSize="13" ... />
```

迁移后：
- `PeTitle`（产品名）和 `PeSubtitle`（架构/子系统）**不再以大字显示**
- 用户通过配置决定 ProductName / Architecture / Subsystem 显示在哪里
- 默认配置：ProductName + CompanyName → contentTop，其余 → infoPanel
- `PeMetadata` 集合被完全替代

## 执行阶段

### Phase 1：数据模型 + 注册 + 渲染引擎

**无设置 UI**，所有类型走默认配置运行。等 Phase 2 才开放自定义。

默认配置原则：
- 通用文件信息：全部启用，每字段独立一行
- 每个类型：全部启用，每字段独立一行
- contentTop：大部分类型默认为空，PE 的 ProductName/Architecture 默认 contentTop

**做什么**:
1. 定义数据模型（`MetadataPanelSettings`, `TypeMetadataConfig`, `FieldConfig`, `MetadataSection`, `InfoPanelRow`）
2. 建立 `MetadataRegistry`：注册 `common` + 所有类型
3. 实现 `MetadataRenderEngine`
4. `PreviewViewModel` 添加新属性 `MetadataSections`、`ContentTopItems`、`IsContentTopVisible`、`IsInfoPanelVisible`
5. 修改所有 Show* 方法：从直接操作 `FormatMetadata` 改为提供字段值字典 + 调用引擎
6. `PreviewPanel.axaml` 改造：
   - 内容区 ScrollViewer 内顶部加 contentTop 横条
   - 信息面板改为分区渲染
7. PE 迁移：移除 `PeTitle`/`PeSubtitle`，替换为引擎渲染

### Phase 2：设置 UI

预览标签页下新增"元数据面板"子标签：

```
预览
├── 通用             ← 预览行为（现有）
├── 图片
├── 文本
├── 字体
├── 元数据面板       ← 新增
│   ├── 类型: [通用文件信息 ▼]
│   │   [启用 ☑]
│   │   ┌──────────┬──────────────────┬─────┐
│   │   │ 字段     │ 显示位置         │ 行  │
│   │   ├──────────┼──────────────────┼─────┤
│   │   │ 文件名   │ 信息栏 ▼         │ 1   │
│   │   │ 大小     │ 信息栏 ▼         │ 2   │
│   │   │ 压缩后   │ 信息栏 ▼         │ 2   │
│   │   └──────────┴──────────────────┴─────┘
│   │
│   │   ↓ 实时预览
│   │   ┌──────────────────────────────┐
│   │   │ ── 文件信息 ──               │
│   │   │ 文件名: report.docx          │
│   │   │ 大小: 1.2 MB | 压缩后: 856 KB│
│   │   └──────────────────────────────┘
│   │
│   ├── 类型: [▼]
│   │   Word 文档
│   │   Excel 工作表
│   │   PowerPoint 演示文稿
│   │   图片
│   │   音频
│   │   视频
│   │   可执行文件 (PE)
│   │   字体
│   │   BT 种子
│   │   ISO 光盘镜像
│   │   SQLite 数据库
│   │
│   └── [恢复默认]
```

预览区使用渲染引擎相同代码 + 模拟示例值，切换类型/修改配置即时刷新。

## 受影响类型（12 个 + common）

| 类型键 | 名称 | 当前实现 | 字段数 | 特殊处理 |
|:---|:---|:---|:---:|:---|
| common | 通用文件信息 | `PreviewInfoBorder` 硬编码 | 5 | 所有格式共享 |
| image | 图片 | `FormatMetadata` 赋值 | 3 | — |
| docx | Word 文档 | `FormatMetadata` | 6 | — |
| xlsx | Excel 工作表 | `FormatMetadata` | 4 | — |
| pptx | PowerPoint | `FormatMetadata` | 4 | — |
| audio | 音频 | `FormatMetadata` | 7 | — |
| video | 视频 | `FormatMetadata` | 4 | — |
| font | 字体 | `FormatMetadata` | 3 | — |
| torrent | BT 种子 | `FormatMetadata` | 10 | — |
| iso | ISO 光盘镜像 | `FormatMetadata` | 3 | — |
| sqlite | SQLite 数据库 | `FormatMetadata` | 1 | — |
| pe | 可执行文件 | 独立 `PeMetadata` | 5 | 需要移除旧 `PeTitle`/`PeSubtitle` |

## 不做的

- 不设计拖拽排序（行号控制顺序）
- 不改动现有 `FileFormatInfo` 或各 Parser
- 不改动 `AppSettings` 现有结构（新增 `MetadataPanel` 节）
- 不改动字段键（固定为英文）
