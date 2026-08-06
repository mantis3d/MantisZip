# Office 文档内容预览（Avalonia 版）

> **状态**: ✅ 已完成（Tasks 1-6 全部完成；后续增强项 DOCX 表格提取 / Markdown 表格渲染 / PPTX Canvas 定位预览 已实现）
> **基于**: Avalonia 端口（`avalonia-port` 分支，Phase 0-10 已完成）
> **替代**: `.sisyphus/plans/office-content-preview.md`（WPF 版计划，已过时）

## TL;DR

**核心目标**: 将 Avalonia 端口的 Office 文档预览从"仅元数据"升级为"实质性内容预览"。

**渲染架构（三轨制）**：

```
优先：Avalonia.Controls.WebView（各平台原生浏览器引擎）
  ├── DOCX    → Mammoth → HTML
  ├── Markdown → Markdig → HTML
  └── HTML    → 直接传入
        ↓ WebView 不可用时
降级：纯文本/原生控件方案（已实现）
  ├── DOCX    → DocumentFormat.OpenXml 提取大纲+全文（左右分栏 TextBlock）
  ├── Markdown → Markdig AST → Avalonia 控件树
  └── HTML    → ReverseMarkdown → Markdown 管线
```

- WebView 后端跨平台：Win→WebView2 / Mac→WKWebView / Linux→WebKit GTK
- 表格/粗体/列表等格式问题由浏览器引擎解决，无需自定义渲染
- 现有纯文本方案保留为 fallback，不浪费

| 格式 | WebView 方案 | 纯文本 fallback |
|:---|:---|:---|
| **DOCX** | Mammoth → HTML → WebView | ✅ 左右分栏（大纲 + 全文）+ 表格提取（`| a | b |` 分隔符） |
| **XLSX** | — | ✅ ClosedXML → DataGrid（不变） |
| **PPTX** | — | ✅ `a:sp` xfrm 坐标 + Canvas 定位预览（翻页 + 字号/加粗） |
| **Markdown** | Markdig → HTML → WebView | ✅ MarkdownPreviewBuilder 控件树（含表格 Grid 渲染） |
| **HTML** | 直接 HTML → WebView | 现有 ReverseMarkdown 管线 |

## 设计理念

### DOCX 左右分栏

```
┌───────────────────────────┬───────────────────────────────────┐
│ GridSplitter(可拖拽)│                                   │
│  ▼                        │                                   │
│ ┌──────────────┬──────────┬─────────────────────────────────┐ │
│ │ 文档大纲     │ ║        │ 全文                          │ │
│ │              │ ║        │                                 │ │
│ │ ● 第一章     │ ║        │ 第一章 概述                     │ │
│ │   ● 1.1 背景 │ ║  ← 点  │                                 │ │
│ │   ● 1.2 目的 │ ║  击跳  │ 随着信息技术的快速发展...        │ │
│ │ ● 第二章     │ ║  转    │                                 │ │
│ │   ● 2.1 方法 │ ║        │ 1.1 背景                        │ │
│ │ ● 第三章     │ ║        │                                 │ │
│ │              │ ║        │ 近年来...                        │ │
│ │              │ ║        │                                 │ │
│ │              │ ║        │ 1.1.1 研究现状                  │ │
│ │              │ ║        │                                 │ │
│ │              │ ║        │ 国内外学者对此...                │ │
│ └──────────────┴──────────┴─────────────────────────────────┘ │
└───────────────────────────┴───────────────────────────────────┘
```

优势：
- **信息密度高** — 大纲和全文同时可见，不需要滚动来回看
- **GridSplitter 可调** — 用户自由控制左右宽度
- **点击跳转** — 点击大纲条目，全文滚动到对应标题位置
- **未来复用** — EPUB（`toc.ncx` + HTML 正文）、Markdown（`#` 标题 + 正文）等文档格式都可复用同一左右分栏模型

### 不处理信息面板

信息面板（`PreviewInfoBorder`/`FormatMetadata`）不在本计划范围内，将有单独的计划处理。

---

## 当前状态

Avalonia 预览系统现有架构：

- `PreviewType.Office` 枚举 + `IsOfficeVisible` 绑定（`PreviewService.cs`）
- `ShowOffice(filePath)` 方法（`PreviewViewModel.cs:1247`）— 仅用 `OfficeParser.Parse()` 显示元数据
- Office 面板（`PreviewPanel.axaml:283`）— 只有 `PreviewHeaderText` + `FormatMetadata` ItemsControl
- 已支持的其他格式：Text, Csv, Pe, Image, Gif, Svg, Font, Audio, Sqlite, Iso, Torrent, Video, Html, Markdown
- **无 Mammoth、ClosedXML、DocumentFormat.OpenXml 依赖**

## 架构决策

### 拆分 PreviewType

当前 `PreviewType.Office` 拆为三种独立类型：

- `PreviewType.Docx` — 左右分栏大纲 + 全文
- `PreviewType.Xlsx` — 表格预览
- `PreviewType.Pptx` — 幻灯片文本

（旧 `PreviewType.Office` 可保留但不再被分类映射使用，或直接移除。）

### DOCX 大纲 + 全文（核心设计）

**数据提取** — 用 `DocumentFormat.OpenXml` 一次遍历完成两件事：

```csharp
using var doc = WordprocessingDocument.Open(path, false);
var body = doc.MainDocumentPart.Document.Body;

List<DocxOutlineItem> outline = new();
StringBuilder fullText = new();

foreach (var para in body.Elements<Paragraph>())
{
    var text = string.Concat(para.Descendants<Text>().Select(t => t.Text));
    if (string.IsNullOrWhiteSpace(text)) continue;
    
    var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    if (styleId != null && styleId.StartsWith("Heading"))
    {
        var level = int.TryParse(styleId["Heading".Length..], out var l) ? l : 1;
        // 记录该标题在全文中的字符偏移量（用于点击跳转）
        outline.Add(new DocxOutlineItem
        {
            Text = text,
            Level = level,
            CharOffset = fullText.Length
        });
    }
    
    fullText.AppendLine(text);
}
```

**显示布局** — 左右分栏：

```xml
<!-- DOCX preview: left-right split -->
<Grid IsVisible="{Binding IsDocxVisible}"
      ColumnDefinitions="Auto,4,*">
  <!-- 左栏：大纲 -->
  <ScrollViewer Grid.Column="0"
                MinWidth="120" MaxWidth="400">
    <ItemsControl ItemsSource="{Binding DocxOutline}">
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="vm:DocxOutlineItem">
          <TextBlock Text="{Binding Text}"
                     Margin="{Binding Indent}"
                     Foreground="{DynamicResource ThemeAccentBrush}"
                     FontWeight="Bold"
                     PointerPressed="OnOutlineItemClicked" />
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </ScrollViewer>

  <!-- GridSplitter -->
  <GridSplitter Grid.Column="1"
                Width="4"
                Background="{DynamicResource ThemeBorderBrush}"
                ResizeBehavior="PreviousAndNext" />

  <!-- 右栏：全文 -->
  <ScrollViewer x:Name="DocxFullTextScroller"
                Grid.Column="2">
    <TextBlock Text="{Binding DocxFullText}"
               TextWrapping="Wrap"
               Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
  </ScrollViewer>
</Grid>
```

**点击跳转机制**：

```
OutlineItemClicked:
  1. 计算：scrollOffset = (item.CharOffset / DocxFullText.Length) * DocxFullTextScroller.Extent.Height
  2. DocxFullTextScroller.ScrollToVerticalOffset(scrollOffset)
```

这是一种近似跳转（按字符比例映射到滚动位置），不是精确的行级定位，但对大纲导航来说效果足够好。

### 渲染架构：WebView 优先 + 纯文本降级

本计划最初走纯文本路线（不依赖 WebView），但后续发现表格、粗体、列表等格式问题的自定义渲染工作量远超预期。因此改为 **WebView 优先方案**：

| 层级 | 方案 | 依赖 | 覆盖格式 |
|:---|:---|:---|:---:|
| 🥇 主路线 | `Avalonia.Controls.WebView` 渲染 HTML | 系统原生浏览器引擎 | DOCX / Markdown / HTML |
| 🥈 降级 | 纯文本/原生控件树 | 已有实现（DocumentFormat.OpenXml / Markdig AST） | DOCX / Markdown / HTML |

**WebView 优势**：
- 表格、列表、粗体、图片等格式由浏览器引擎原生渲染，零代码
- 跨平台统一：Win→WebView2, Mac→WKWebView, Linux→WebKit GTK
- 一条管线解决三种格式（DOCX + Markdown + HTML），避免碎片化

**降级策略**：
- 启动时检测 `WebView` 控件是否可用（如 WebView2 Runtime 未安装）
- 可用 → 走 WebView 路线
- 不可用 → 走纯文本 fallback，已有代码不浪费
- 运行时提供切换按钮（可选）

**Mammoth 角色**：
- 仅作为 DOCX → HTML 转换器（Mammoth 本身是很小的纯 C# 库）
- 不用于富文本渲染（渲染交给 WebView）

### 未来复用：左右分栏模型

```
左右分栏模型（DocumentPreviewLayout）:
├── DOCX（本计划）— Heading 样式 → 大纲
├── EPUB（将来）  — toc.ncx / nav.xhtml → 大纲
├── Markdown（将来）— # ~ ###### 标题 → 大纲
└── HTML（将来）  — h1 ~ h6 → 大纲
```

ViewModel 侧只需保证 `DocxOutline`（`ObservableCollection<DocxOutlineItem>`）和 `DocxFullText`（string）接口一致，XAML 面板即可复用。

---

## 工作目标

### 交付物

- `PreviewService.cs` — `PreviewType` 增加 `Docx`/`Xlsx`/`Pptx`
- `PreviewViewModel.cs` — 新增 `ShowDocx`/`ShowXlsx`/`ShowPptx` 方法 + 新增属性
- `PreviewPanel.axaml` — 新增三种格式的内容区面板（DOCX 左右分栏）
- `PreviewPanel.axaml.cs` — 大纲点击跳转处理 + XLSX DataGrid 列生成
- `MantisZip.UI.Avalonia.csproj` — 新增 NuGet 依赖
- `MainWindowViewModel.cs` — `ShowPreviewAsync` 分发改造
- `strings.zh.json` / `strings.en.json` — 新增翻译键

### 必须包含

- [x] DOCX: 左右分栏（大纲左 | GridSplitter | 全文右），点击大纲跳转
- [x] XLSX: ClosedXML → DataTable → DataGrid
- [x] PPTX: 手动解析 `ppt/slides/slideN.xml` → `a:t` 文本 → 文本列表
- [x] 大文件保护（>50MB 仅显示元数据）
- [x] 三种格式通过 `PreviewType` 切换自动互斥显示

### 必须不包含（护栏）

- [x] 不处理信息面板（有单独计划）
- [x] 不解析 DOCX 嵌入图片
- [x] 不支持 .docm/.xlsm/.pptm
- [x] 不支持 .doc/.xls/.ppt（二进制格式）
- [x] PPTX 不解析 SmartArt/dgm、图表/c:、数学/m: 命名空间
- [x] XLSX 只读第一个工作表
- [x] 不实现 Mammoth → HtmlRenderer（备选，未来再做）
- [x] 不修改 `OfficeParser.cs`

---

## 执行策略

```
Wave 1（可并行 3 路）:
├── Task 1: NuGet 集成（DocumentFormat.OpenXml + ClosedXML）
├── Task 2: DOCX 大纲 + 全文预览（左右分栏 + 跳转）
├── Task 3: XLSX 表格预览
├── Task 4: PPTX 幻灯片文本预览

Wave 2（集成）:
├── Task 5: PreviewType 拆分 + ShowPreviewAsync 分发改造
└── Task 6: 本地化字符串 + 构建 + 集成验证
```

---

## TODO

### Task 1: NuGet 依赖集成

**做什么**:
- 在 `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` 中添加：
  - `dotnet add src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj package DocumentFormat.OpenXml`
  - `dotnet add src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj package ClosedXML`
- 运行 `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 确认编译通过

**不做什么**:
- 不添加到 `MantisZip.Core.csproj`（仅在 UI 层使用）
- 不添加 Mammoth（备选，未来再决定）

**验收标准**:
- [x] `dotnet build` 通过
- [x] DocumentFormat.OpenXml 和 ClosedXML 在包列表中

**提交**: YES
- Message: `dep(avalonia): add DocumentFormat.OpenXml + ClosedXML NuGet packages for Office content preview`

---

### Task 2: DOCX 大纲 + 全文预览（左右分栏）

**做什么**:

**新增模型** — `DocxOutlineItem`（`PreviewViewModel.cs` 底部或 `Models/`）:
```csharp
public class DocxOutlineItem
{
    public string Text { get; set; } = string.Empty;
    public int Level { get; set; }        // 1-6
    public int CharOffset { get; set; }   // 在全文中的字符偏移量（用于跳转）
    public Thickness Indent => new Thickness((Level - 1) * 20, 2, 0, 2);
}
```

**PreviewViewModel.cs 新增属性**:
```csharp
[ObservableProperty]
private ObservableCollection<DocxOutlineItem> _docxOutline = [];

[ObservableProperty]
private string _docxFullText = string.Empty;

// 只读计算属性
public bool IsDocxVisible => PreviewType == PreviewType.Docx;
public bool HasDocxOutline => DocxOutline.Count > 0;
```

**方法 `ShowDocx(string filePath)`**:
1. 检查文件大小 > 50MB → `ShowUnsupported("文档过大，无法预览")`
2. 用 `WordprocessingDocument.Open(filePath, false)` 打开
3. 遍历 `body.Elements<Paragraph>()`：
   - 对每个 paragraph 提取 `<w:t>` 文本（`string.Concat(para.Descendants<Text>().Select(t => t.Text))`）
   - 检查 `ParagraphStyleId?.Val?.Value` 是否以 `"Heading"` 开头
     - 是 → 解析标题级别（`"Heading1"` → `1`），记录 `CharOffset = fullText.Length`，加入 `DocxOutline`
   - 全文追加 `text + "\n"`（标题文本也出现在全文中）
4. 设置 `PreviewType = PreviewType.Docx`
5. `IsPreviewVisible = true`
6. 异常捕获 → `ShowUnsupported("无法解析 Word 文档")` + `CoreLog.Trace`

**PreviewPanel.axaml.cs 新增事件处理**:
```csharp
// 在 OnDataContextChanged 或直接绑定到 ViewModel Command
private void OnOutlineItemClicked(object? sender, PointerPressedEventArgs e)
{
    if (sender is TextBlock tb && tb.DataContext is DocxOutlineItem item && _vm != null)
    {
        var totalLen = _vm.DocxFullText.Length;
        if (totalLen == 0) return;
        var ratio = (double)item.CharOffset / totalLen;
        var offset = ratio * DocxFullTextScroller.ScrollBarMaximum;
        DocxFullTextScroller.ScrollToVerticalOffset(offset);
    }
}
```

**PreviewPanel.axaml 新增面板** — 在现有 Office 面板位置插入：
```xml
<!-- DOCX preview: 大纲（左）+ GridSplitter + 全文（右） -->
<Grid IsVisible="{Binding IsDocxVisible}"
      ColumnDefinitions="Auto,4,*">
  <!-- 大纲 -->
  <Border Grid.Column="0"
          Background="{DynamicResource ThemeSurfaceBgBrush}">
    <ScrollViewer MinWidth="120" MaxWidth="400">
      <StackPanel Spacing="2">
        <TextBlock Text="文档大纲"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                   Margin="4" />
        <ItemsControl ItemsSource="{Binding DocxOutline}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:DocxOutlineItem">
              <TextBlock Text="{Binding Text}"
                         Margin="{Binding Indent}"
                         Foreground="{DynamicResource ThemeAccentBrush}"
                         FontWeight="Bold"
                         TextWrapping="NoWrap"
                         PointerPressed="OnOutlineItemClicked" />
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
        <!-- 无大纲时的回退 -->
        <TextBlock Text="{Binding DocxNoOutlineText}"
                   IsVisible="{Binding !HasDocxOutline}"
                   Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                   Margin="4" />
      </StackPanel>
    </ScrollViewer>
  </Border>

  <!-- Splitter -->
  <GridSplitter Grid.Column="1" Width="4"
                Background="{DynamicResource ThemeBorderBrush}"
                ResizeBehavior="PreviousAndNext" />

  <!-- 全文 -->
  <Border Grid.Column="2"
          Background="{DynamicResource ThemeSurfaceBgBrush}">
    <ScrollViewer x:Name="DocxFullTextScroller">
      <TextBlock Text="{Binding DocxFullText}"
                 TextWrapping="Wrap"
                 Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                 Margin="8" />
    </ScrollViewer>
  </Border>
</Grid>
```

**处理边缘情况**:
- 无标题文档 → 大纲区显示"（无标题结构）"，全文区正常
- 标题级别不连续（Heading1 → Heading3）→ 缩进按实际级别，不强制连续
- 空文档 → `DocxFullText` 为空，显示"此文档为空"
- 大文件（>50MB）→ 回退到 `ShowUnsupported`
- 损坏文件 → catch 异常

**不做什么**:
- 不处理页眉/页脚/脚注
- 不处理文本框、表格内的文本
- 不处理 SmartArt

**验收标准**:
- [x] DOCX 预览为左右分栏，中间有可拖拽的 GridSplitter
- [x] 大纲显示缩进层级（Heading1 无缩进，Heading2 缩进 20px...）
- [x] 点击大纲条目 → 全文区滚动到对应标题位置
- [x] 无标题文档 → 大纲区显示回退提示，全文区正常
- [x] >50MB 文档 → 显示"文档过大"回退

**提交**: YES
- Message: `feat(avalonia): add DOCX outline + full text preview with left-right split layout and click-to-scroll`

---

### Task 3: XLSX 表格预览

**做什么**:
- 新增 `ShowXlsx(string filePath)` 方法

  1. 用 `new XLWorkbook(filePath)` 打开
  2. 读取第一个工作表 `workbook.Worksheet(1)`
  3. 获取 `RangeUsed()` → 确定有效数据区域
  4. 限制 100 行 × 100 列
  5. 构建 `DataTable`：
     - 首行作为列名
     - 后续行作为数据行
     - 单元格用 `.GetFormattedString()` 获取显示值
  6. 存为 `_xlsxDataTable` + `XlsxData`
  7. 设置 `PreviewType = PreviewType.Xlsx`
  8. `IsPreviewVisible = true`
  9. `finally` 中 `workbook.Dispose()`

  新增属性：
  ```csharp
  private DataTable? _xlsxDataTable;
  public DataTable? XlsxDataTable => _xlsxDataTable;
  [ObservableProperty] private DataView? _xlsxData;
  public bool IsXlsxVisible => PreviewType == PreviewType.Xlsx;
  ```

  **XAML**:
  ```xml
  <!-- XLSX preview -->
  <DataGrid x:Name="XlsxDataGrid"
            IsVisible="{Binding IsXlsxVisible}"
            ItemsSource="{Binding XlsxData}"
            AutoGenerateColumns="False"
            IsReadOnly="True"
            CanUserResizeColumns="True"
            CanUserSortColumns="True"
            GridLinesVisibility="All" />
  ```

  **XlsxDataGrid 列生成** — `PreviewPanel.axaml.cs` 中监听 `IsXlsxVisible` + `XlsxData` 变化，与现有 `CsvDataGrid`、`SqliteDataGrid` 一致模式：
  ```csharp
  if (args.PropertyName == nameof(PreviewViewModel.IsXlsxVisible) && vm.IsXlsxVisible)
      SetupDataGridColumns(XlsxDataGrid, vm.XlsxDataTable);
  ```

**处理边缘情况**:
- 空工作表（`RangeUsed()` 为 null）→ 设置 `PreviewType = PreviewType.Xlsx`，`TextContent = "此工作表中没有数据"`
- 密码保护的 xlsx → `XLWorkbook` 抛出异常 → 捕获，`ShowUnsupported("工作表受密码保护")`
- 合并单元格 → ClosedXML 返回左上角的值

**不做什么**:
- 不读取图表、不刷新外部数据、只读第一个工作表
- 不依赖信息面板

**验收标准**:
- [x] 有数据的 .xlsx → DataGrid 显示前 100 行 × 100 列
- [x] 列名取自首行
- [x] 空工作表显示回退
- [x] 密码保护的 .xlsx 不崩溃

**提交**: YES
- Message: `feat(avalonia): add XLSX worksheet preview via ClosedXML`

---

### Task 4: PPTX 幻灯片文本预览

**做什么**:
- 新增 `ShowPptx(string filePath)` 方法

  1. 用 `ZipFile.OpenRead(filePath)` 打开
  2. 遍历 `ppt/slides/slideN.xml`（`StartsWith("ppt/slides/slide")` + `EndsWith(".xml")`）
  3. 每个 slide 用 `XDocument.Load`：
     ```csharp
     XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
     var texts = slideDoc.Descendants(a + "t")
         .Select(t => t.Value)
         .Where(v => !string.IsNullOrWhiteSpace(v));
     ```
  4. `a:br` 换行处理：`a:t` 之间插入 `\n`
  5. 构建显示文本：
     ```
     ── 幻灯片 1 ──
     标题文字
     正文段落...

     ── 幻灯片 2 ──
     （此幻灯片无文字）
     ```
  6. 设置 `PreviewText = 结果`
  7. 设置 `PreviewType = PreviewType.Pptx`
  8. `IsPreviewVisible = true`

  新增属性：
  ```csharp
  [ObservableProperty] private string _previewText = string.Empty;
  public bool IsPptxVisible => PreviewType == PreviewType.Pptx;
  ```

  **XAML**:
  ```xml
  <!-- PPTX preview -->
  <Border IsVisible="{Binding IsPptxVisible}"
          Background="{DynamicResource ThemeSurfaceBgBrush}">
    <ScrollViewer>
      <TextBox Text="{Binding PreviewText}"
               IsReadOnly="True"
               TextWrapping="Wrap"
               Background="Transparent"
               Foreground="{DynamicResource ThemeTextPrimaryBrush}"
               BorderThickness="0"
               Margin="8" />
    </ScrollViewer>
  </Border>
  ```

**处理边缘情况**:
- 仅图片/SmartArt 的幻灯片 → "(此幻灯片无文字)"
- 空演示文稿 → "此演示文稿为空"
- 损坏的 slide XML → try-catch，跳过该幻灯片并记录日志

**不做什么**:
- 不解析 SmartArt、图表、数学命名空间
- 不解析表格内的 `a:t`
- 不解析页脚/字段

**验收标准**:
- [x] 含文字的 .pptx → 每张幻灯片文本分段显示，带 `── 幻灯片 N ──` 分隔线
- [x] 纯图片的 .pptx 对应幻灯片 → "(此幻灯片无文字)"
- [x] 空演示文稿 → "此演示文稿为空"

**提交**: YES
- Message: `feat(avalonia): add PPTX slide text preview via manual XML parsing`

---

### Task 5: PreviewType 拆分 + 分发改造

**做什么**:

**PreviewService.cs**:
- `PreviewType` 枚举新增 `Docx`, `Xlsx`, `Pptx`
- `ClassifyPreview()` 拆开 Office：
  ```csharp
  if (ext == ".docx") return PreviewType.Docx;
  if (ext == ".xlsx") return PreviewType.Xlsx;
  if (ext == ".pptx") return PreviewType.Pptx;
  ```
- `OfficeExtensions` 集合可保留或移除（不再被 `ClassifyPreview` 使用）

**PreviewViewModel.cs**:
- 添加计算属性通知：
  ```csharp
  partial void OnPreviewTypeChanged(PreviewType value)
  {
      // ... 现有通知 ...
      OnPropertyChanged(nameof(IsDocxVisible));
      OnPropertyChanged(nameof(IsXlsxVisible));
      OnPropertyChanged(nameof(IsPptxVisible));
      OnPropertyChanged(nameof(HasDocxOutline));
  }
  ```
- `Clear()` 中清理新字段：
  ```csharp
  DocxOutline.Clear();
  DocxFullText = string.Empty;
  XlsxData = null;
  _xlsxDataTable = null;
  PreviewText = string.Empty;
  ```

**MainWindowViewModel.cs**:
- `ShowPreviewAsync` 中 `case PreviewType.Office` 替换为三个分支：
  ```csharp
  case PreviewType.Docx:
      Preview.ShowDocx(tempFile);
      StatusMessage = L.T("Preview_Docx", entry.DisplayName);
      break;
  case PreviewType.Xlsx:
      Preview.ShowXlsx(tempFile);
      StatusMessage = L.T("Preview_Xlsx", entry.DisplayName);
      break;
  case PreviewType.Pptx:
      Preview.ShowPptx(tempFile);
      StatusMessage = L.T("Preview_Pptx", entry.DisplayName);
      break;
  ```

**PreviewPanel.axaml**:
- 将现有的 `IsOfficeVisible` 面板替换为三个新面板（DOCX/ XLSX/ PPTX）
- 或在现有面板后追加（`Grid` 内多个面板互斥显示，不会有冲突）

**验收标准**:
- [x] `.docx` → `PreviewType.Docx` → `ShowDocx` → 左右分栏
- [x] `.xlsx` → `PreviewType.Xlsx` → `ShowXlsx` → DataGrid
- [x] `.pptx` → `PreviewType.Pptx` → `ShowPptx` → 文本列表
- [x] 现有非 Office 格式预览不受影响

**提交**: YES
- Message: `refactor(avalonia): split PreviewType.Office into Docx/Xlsx/Pptx with content preview dispatch`

---

### Task 6: 本地化字符串 + 构建 + 集成验证

**做什么**:
- `strings.zh.json` / `strings.en.json` 新增键：
  - `Preview_Docx` — "Word 文档大纲" / "Word Document Outline"
  - `Preview_Xlsx` — "Excel 工作表" / "Excel Worksheet"
  - `Preview_Pptx` — "PowerPoint 演示文稿" / "PowerPoint Presentation"
  - `Preview_DocxFailed` — "无法解析 Word 文档" / "Failed to parse Word document"
  - `Preview_XlsxFailed` — "无法加载 Excel 工作表" / "Failed to load Excel sheet"
  - `Preview_PptxFailed` — "无法解析演示文稿" / "Failed to parse presentation"
  - `Preview_PptxSlideEmpty` — "（此幻灯片无文字）" / "(No text on this slide)"
  - `Preview_PptxEmpty` — "此演示文稿为空" / "This presentation is empty"
  - `Preview_XlsxEmpty` — "此工作表中没有数据" / "No data in this worksheet"
  - `Preview_XlsxProtected` — "工作表受密码保护" / "Worksheet is password protected"
  - `Preview_DocxNoOutline` — "（无标题结构）" / "(No heading structure)"
  - `Preview_DocxEmpty` — "此文档为空" / "This document is empty"
  - `Preview_DocxTooLarge` — "文档过大（超过 50MB 限制）" / "Document too large (exceeds 50MB limit)"

- 运行 `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 确认通过

**验收标准**:
- [x] 构建通过
- [x] 新增翻译键在 zh 和 en 中均存在

**提交**: NO（与 Task 5 合并提交）

---

## 验证策略

### 构建
```bash
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```

### 功能验证
1. **DOCX**:
   - 准备含 Heading1/2/3 + 正文的 .docx
   - 左右分栏显示，GridSplitter 可拖拽
   - 点击大纲条目，全文滚动到对应位置
2. **XLSX**:
   - 准备 5 行 3 列表格
   - DataGrid 正确显示
3. **PPTX**:
   - 准备 3 张幻灯片
   - 文本分段显示
4. **切换验证**:
   - .docx ↔ .xlsx ↔ .pptx ↔ .txt ↔ .png
   - 无控件残留
5. **大文件**: >50MB .docx → 回退提示

---

## 后续增强

### DOCX 表格内容提取（WebView 路径）

当前 `ShowDocx()` 仅遍历 `body.Elements<Paragraph>()`，跳过表格（`Table`）内部的文本。很多文档使用表格组织内容。

**优先方案**（WebView 路线）：

Mammoth 在将 DOCX 转 HTML 时自动处理表格（`<table>` → `<tr>` → `<td>`）。WebView 渲染 HTML 表格无需任何额外代码。

```
DOCX → Mammoth → HTML（含 <table>）→ WebView（原生渲染）
```

一旦 WebView 管线就绪，DOCX 表格问题自动解决。

**降级方案**（纯文本路线）：

WebView 不可用时，在现有 `ShowDocx()` 中补充表格提取，用 `|` 分隔符模拟表格：

```csharp
// 在现有 Paragraph 遍历后补充表格检测
foreach (var table in body.Descendants<Table>())
{
    foreach (var row in table.Descendants<TableRow>())
    {
        var cellTexts = new List<string>();
        foreach (var cell in row.Descendants<TableCell>())
        {
            var cellText = string.Concat(cell.Descendants<Text>().Select(t => t.Text ?? string.Empty));
            cellTexts.Add(cellText.Trim());
        }
        fullText.AppendLine("| " + string.Join(" | ", cellTexts) + " |");
    }
}
```

**状态**: ✅ 已实现（`ShowDocx` 按文档顺序遍历 `body.ChildElements`，表格行用 `"| a | b |"` 分隔符追加到全文，段落走原有大纲逻辑）

### 页眉/页脚/脚注内容提取

可选的增强方向，从对应的 OpenXml Parts 中提取文本：

| 内容 | OpenXml Part | 实现方式 |
|:---|:---|:---|
| 页眉 | `HeaderPart` | `doc.MainDocumentPart.HeaderParts` → `Descendants<Paragraph>()` |
| 页脚 | `FooterPart` | `doc.MainDocumentPart.FooterParts` → `Descendants<Paragraph>()` |
| 脚注/尾注 | `FootnotesPart` / `EndnotesPart` | `doc.MainDocumentPart.FootnotesPart` → `Descendants<Footnote>()` |

**状态**: 📋 待定（按需再实施）

---

### Markdown 表格渲染（WebView 路径）

当前 `MarkdownPreviewBuilder`（`Services/MarkdownPreviewBuilder.cs`）已启用 `.UsePipeTables()`（line 25），Markdig 正确解析表格为 `Table` AST block，但 `TryBuildBlock` 的 `switch` 中缺少 `case Table:`，被 `default: return null` 静默丢弃。

**原因**: 原计划（HTML 预览降级方案）中已知此缺失。

**优先方案**（WebView 路线）：

Markdig 可以直接输出 HTML（`markdig.ToHtml()`），WebView 渲染 HTML 表格原生支持。

```
Markdown → Markdig → HTML（含 <table>）→ WebView（原生渲染）
```

一旦 WebView 管线就绪，Markdown 表格问题自动解决。

**降级方案**（原生控件树路线）：

WebView 不可用时，在 `TryBuildBlock` 中添加 `case Table t:`，用 Avalonia Grid 构建表格：

```csharp
case Table t:
    return BuildTable(t, source);
```

详见 `.sisyphus/plans/html-preview-webview-fallback.md` Task 5（第 229–283 行）已有完整的 `BuildTable` 实现方案。

**状态**: ✅ 已实现（`MarkdownPreviewBuilder.TryBuildBlock` 新增 `case Table` → `BuildTable`，Grid 按 `TableColumnDefinition.Width` 生成 Star/Auto 列，表头行 `ThemeHeaderBgBrush`、正文行 `ThemeSurfaceBgBrush`、周围 `ThemeBorderBrush`）

---

### PPTX 文本按原始位置预览

当前 `ShowPptx()` 把幻灯片内所有 `a:t` 打平输出，丢失了文本在幻灯片上的位置信息。PPTX 的每个形状（`a:sp`）都包含 `a:xfrm` → `a:off`（x,y 坐标，单位 EMU），可以据此还原近似布局。

#### 方案：Canvas 绝对定位

**ViewModel 侧新增结构** — 每张幻灯片一个模型：

```csharp
public class PptxSlideModel
{
    public int SlideNumber { get; set; }
    public ObservableCollection<PptxTextItem> TextItems { get; set; } = [];
}

public class PptxTextItem
{
    public string Text { get; set; } = string.Empty;
    public double X { get; set; }    // 缩放后的 Canvas 坐标
    public double Y { get; set; }
    public double FontSize { get; set; } = 14;
    public bool IsBold { get; set; }
}
```

**ShowPptx 改造** — 提取每个形状的位置和文本：

```csharp
// EMU → 点（1pt = 12700 EMU ≈ 按 slide 尺寸归一化）
const double emuPerPt = 12700;
var slideWidth = 12192000;  // 默认 10" × 7.5" (12192000 × 9144000 EMU)
var slideHeight = 9144000;

var items = new List<PptxTextItem>();
foreach (var shape in slideDoc.Descendants(a + "sp"))
{
    var xfrm = shape.Descendants(a + "xfrm").FirstOrDefault();
    if (xfrm == null) continue;
    
    var xEmu = (int)(xfrm.Descendants(a + "off").FirstOrDefault()?.Attribute("x") ?? 0);
    var yEmu = (int)(xfrm.Descendants(a + "off").FirstOrDefault()?.Attribute("y") ?? 0);
    
    var text = string.Join("\n", shape.Descendants(a + "t").Select(t => t.Value));
    if (string.IsNullOrWhiteSpace(text)) continue;
    
    // 缩放比例：预览区域固定宽高比（如 960×540），按比例缩放
    double scaleX = 960.0 / slideWidth;
    double scaleY = 540.0 / slideHeight;
    
    items.Add(new PptxTextItem
    {
        Text = text,
        X = xEmu * scaleX,
        Y = yEmu * scaleY,
    });
}

// 按 y 排序（从上到下）
items = items.OrderBy(i => i.Y).ThenBy(i => i.X).ToList();
```

**XAML 侧 — Canvas 面板** — 替换当前 TextBox：

```xml
<!-- PPTX preview: Canvas 按位置渲染 -->
<Border IsVisible="{Binding IsPptxVisible}"
        Background="{DynamicResource ThemeSurfaceBgBrush}">
  <ScrollViewer>
    <Border Width="960" Height="540"  <!-- 16:9 比例 -->
            Background="White"
            CornerRadius="4">
      <Canvas Name="SlideCanvas">
        <!-- TextBlock 通过代码绑定 -->
      </Canvas>
    </Border>
  </ScrollViewer>
</Border>
```

**PreviewPanel.axaml.cs** — 监听 `IsPptxVisible`，构建 Canvas 子控件：

```csharp
if (args.PropertyName == nameof(PreviewViewModel.IsPptxVisible) && vm.IsPptxVisible)
{
    SlideCanvas.Children.Clear();
    if (vm.CurrentSlideItems != null)
    {
        foreach (var item in vm.CurrentSlideItems)
        {
            var tb = new TextBlock
            {
                Text = item.Text,
                FontSize = item.FontSize,
                FontWeight = item.IsBold ? FontWeight.Bold : FontWeight.Normal,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 200,  // 防止溢出
            };
            Canvas.SetLeft(tb, item.X);
            Canvas.SetTop(tb, item.Y);
            SlideCanvas.Children.Add(tb);
        }
    }
}
```

**翻页控制** — 新增 `PptxCurrentSlide` 属性 + 上一页/下一页按钮：

```csharp
[ObservableProperty] private int _pptxCurrentSlide = 1;
[ObservableProperty] private int _pptxTotalSlides = 0;
[ObservableProperty] private ObservableCollection<PptxSlideModel> _pptxSlides = [];

partial void OnPptxCurrentSlideChanged(int value)
{
    var slide = PptxSlides.ElementAtOrDefault(value - 1);
    CurrentSlideItems = slide?.TextItems;
}
```

**边缘情况**：
- 无文字幻灯片 → 显示空白 Canvas + "此幻灯片无文字" 叠加提示
- 文本过长 → `MaxWidth` 限制 + `TextWrapping.Wrap` 自动折行
- EMU → 像素缩放保持比例，不扭曲

**不做**：
- 不解析 SmartArt（dgm 命名空间）
- 不渲染图片/形状（仅文本）
- 不处理动画/过渡

**状态**: ✅ 已实现（`ShowPptx` 从 `presentation.xml` 读取 `p:sldSz` EMU 尺寸，固定 Canvas 宽 960 等比缩放；每个 `a:sp` 提取 `a:xfrm/a:off` 坐标 + 段落文本 + `a:rPr` 字号/加粗，按 Y 再按 X 排序；翻页栏 ◀ 页码/总页数 ▶ + `BuildPptxSlide` 在 `PreviewPanel.axaml.cs` 构建 TextBlock 子控件，空演示文稿显示 `Preview_PptxEmpty`、无文字幻灯片显示 `Preview_PptxSlideEmpty`）

## 未来可复用方向

左右分栏的 `OutlineItem + FullText` 模型天然适配：

| 格式 | 大纲来源 | 正文来源 |
|:---|:---|:---|
| EPUB | `toc.ncx` 或 `nav.xhtml` | 解包后的 HTML 正文 |
| Markdown | `#` ~ `######` 标题 | 纯文本 |
| HTML | `h1` ~ `h6` | 纯文本 |

只需实现不同的解析器，ViewModel 侧保持 `DocxOutline`/`DocxFullText` 接口不变即可。

---

## 备选方案

### Avalonia.HtmlRenderer（已废弃）

`Avalonia.HtmlRenderer` 能力有限（README 明确说明不支持现代 HTML 特性），且生态尚不成熟（总下载 166K）。**不采用此路线**——WebView 可用就走 WebView，不可用直接降级纯文本，中间方案两头不讨好。

### PPTX → WebView

PPTX 也可以通过 Mammoth 转 HTML？**不行**。Mammoth 只支持 DOCX，不支持 PPTX。PPTX 仍然走现有的 `a:t` 提取 + Canvas 定位预览方案。
