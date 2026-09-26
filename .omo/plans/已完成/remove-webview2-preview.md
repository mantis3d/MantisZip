# 移除 WebView2 依赖 — 跨平台预览方案

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 去除 `Avalonia.Controls.WebView` 依赖，使 MantisZip 不再需要 WebView2 Runtime，实现跨平台预览。替换 PDF / HTML / Markdown 三种预览实现。

**Architecture:** 三项预览分别采用不同策略：(1) **Markdown** — 利用已有 Markdig 的 AST 输出直接构建 Avalonia 控件树，无需转 HTML；(2) **HTML** — 先用 `ReverseMarkdown` 转为 Markdown，再走 Markdig→控件树管线；(3) **PDF** — 新增 `PdfPig` + `PdfPig.Rendering.Skia`，将 PDF 逐页渲染为 SKBitmap，复用现有图片预览的 ScrollViewer + ZoomFit 交互。三项均不再经 WebView2。

**Tech Stack:** .NET 9, Avalonia 11, SkiaSharp, Markdig, UglyToad.PdfPig, ReverseMarkdown

**新增依赖：**
| 包 | 版本 | 用途 | 许可证 |
|---|---|---|---|
| `UglyToad.PdfPig` | latest | PDF 文档解析（纯 .NET） | Apache-2.0 |
| `UglyToad.PdfPig.Rendering.Skia` | latest | PDF 页 → SKBitmap 渲染 | Apache-2.0 |
| `ReverseMarkdown` | latest | HTML → Markdown 转换 | MIT |

**移除依赖：**
| 包 | 原因 |
|---|---|
| `Avalonia.Controls.WebView` | 不再需要 WebView2 |
| (间接) `Microsoft.Web.WebView2` 运行时 | 不再需要安装在用户机器上 |

---

## 文件映射

| 文件 | 操作 | 职责 |
|------|------|------|
| `MantisZip.UI.Avalonia.csproj` | 修改 | 添加 PdfPig / ReverseMarkdown，移除 WebView |
| `ViewModels/PreviewViewModel.cs` | 修改 | 新增 ShowMarkdownFromAst、ShowPdfPage、ShowHtmlAsMarkdown；移除 HtmlContent 依赖 |
| `ViewModels/MainWindowViewModel.cs` | 修改 | switch 中更新对应的 case（Markdown/HTML/Pdf 分支） |
| `Views/PreviewPanel.axaml` | 修改 | 移除 WebView 节，替换为 Markdown 控件树节、PDF 翻页节、纯文本备用节 |
| `Views/PreviewPanel.axaml.cs` | 修改 | 移除 UpdateWebViewContent、数据订阅；新增 PDF 翻页绑定、Markdown 控件树构建 |
| `Services/MarkdownPreviewBuilder.cs` | 新建 | Markdig AST → Avalonia 控件树转换器 |
| `Localization/strings.*.json` | 修改 | 增加/更新相关 key |
| `Models/AppSettings.cs` | 可能修改 | 如有 PDF 翻页大小等新设置 |
| `App.axaml.cs` | 修改 | 如果移除了 WebView 相关初始化 |

---

### Task 1: Markdown 预览（Markdig AST → Avalonia 控件树）

这是三个替换中最核心的——把 Markdig AST 转成 Avalonia `Control` 集合，放在 ScrollViewer 中。完全替代 WebView2 渲染 HTML 的路径。

**关键设计：**
- Markdig 的 `MarkdownPipeline.Build()` 产出 `MarkdownDocument`（AST 根节点），`Markdown.ToHtml()` 不再需要
- 遍历 AST 节点：`HeadingBlock` → `TextBlock`(不同字号)，`ParagraphBlock` → `SelectableTextBlock`(可复制)，`CodeBlock` → 带背景色的 `Border`+`TextBlock`(等宽字体)
- 实际上，为防止过于复杂：**优先用 `SelectableTextBlock` 并将 Markdown 按段落拆分为独立 TextBlock，用 StackPanel 排列**。不需要完美渲染表格/列表——纯文本可读就行

**文件：**
- Create: `Services/MarkdownPreviewBuilder.cs`
- Modify: `ViewModels/PreviewViewModel.cs`
- Modify: `Views/PreviewPanel.axaml`
- Remove from: `Views/PreviewPanel.axaml.cs`

<details>
<summary>实现详情</summary>

#### Step 1.1: 创建 `MarkdownPreviewBuilder`

```csharp
// Services/MarkdownPreviewBuilder.cs
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 将 Markdown 文本转换为 Avalonia 控件树。
/// 替代 WebView2 的 data: URI 注入方案，实现纯 .NET 跨平台渲染。
/// 支持：Heading 1-6、段落、代码块、内联粗体/斜体、链接（显示URL）。
/// </summary>
public static class MarkdownPreviewBuilder
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().Build();

    public static Panel Build(string markdownText)
    {
        var doc = Markdown.Parse(markdownText, Pipeline);
        var stack = new StackPanel { Spacing = 4 };

        foreach (var block in doc)
        {
            var control = BlockToControl(block);
            if (control != null)
                stack.Children.Add(control);
        }

        return stack;
    }

    private static Control? BlockToControl(Block block)
    {
        return block switch
        {
            HeadingBlock h => BuildHeading(h),
            ParagraphBlock p => BuildParagraph(p),
            FencedCodeBlock c => BuildCodeBlock(c),
            CodeBlock c => BuildCodeBlock(c),
            ListBlock l => BuildList(l),
            QuoteBlock q => BuildQuote(q),
            ThematicBreakBlock => new Separator { Margin = new(0, 8) },
            _ => null,
        };
    }

    private static Control BuildHeading(HeadingBlock heading)
    {
        var fontSize = heading.Level switch
        {
            1 => 24, 2 => 20, 3 => 18,
            4 => 16, 5 => 15, _ => 14,
        };
        var tb = new SelectableTextBlock
        {
            FontSize = fontSize,
            FontWeight = heading.Level <= 2 ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
        };
        tb.Inlines.AddRange(InlineToControls(heading.Inline));
        return tb;
    }

    private static Control BuildParagraph(ParagraphBlock para)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 2),
        };
        tb.Inlines.AddRange(InlineToControls(para.Inline));
        return tb;
    }

    private static Control BuildCodeBlock(CodeBlock code)
    {
        var text = string.Join("\n", code.Lines.Lines.Select(l => l.ToString()));
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            CornerRadius = new(4),
            Padding = new(12, 8),
            Child = new SelectableTextBlock
            {
                Text = text,
                FontFamily = new("Consolas, Courier New, monospace"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

    private static Control BuildList(ListBlock list)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new(16, 4, 0, 4) };
        int index = 0;
        foreach (var item in list)
        {
            if (item is ListItemBlock lib)
            {
                var bullet = list.IsOrdered
                    ? $"{++index}. "
                    : "• ";
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock
                {
                    Text = bullet,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Top,
                });
                foreach (var child in lib)
                {
                    var childCtrl = BlockToControl(child);
                    if (childCtrl != null)
                        panel.Children.Add(childCtrl);
                }
                stack.Children.Add(panel);
            }
        }
        return stack;
    }

    private static Control BuildQuote(QuoteBlock quote)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#888")),
            BorderThickness = new(3, 0, 0, 0),
            Padding = new(12, 4),
            Margin = new(0, 4),
        };
        var stack = new StackPanel { Spacing = 2 };
        foreach (var child in quote)
        {
            var ctrl = BlockToControl(child);
            if (ctrl != null) stack.Children.Add(ctrl);
        }
        border.Child = stack;
        return border;
    }

    private static IEnumerable<Avalonia.Controls.Documents.Inline> InlineToControls(
        Markdig.Syntax.Inlines.ContainerInline? inlines)
    {
        if (inlines == null) yield break;
        foreach (var inline in inlines)
        {
            if (inline is LiteralInline lit)
            {
                yield return new Avalonia.Controls.Documents.Run(lit.Content.ToString());
            }
            else if (inline is LineBreakInline)
            {
                yield return new Avalonia.Controls.Documents.Run("\n");
            }
            else if (inline is EmphasisInline emp)
            {
                var run = new Avalonia.Controls.Documents.Run();
                // 递归构建子内联的文本
                foreach (var child in emp)
                {
                    if (child is LiteralInline lit2)
                        run.Text += lit2.Content;
                }
                if (emp.DelimiterCount == 1) // *italic*
                {
                    run.FontStyle = FontStyle.Italic;
                }
                else if (emp.DelimiterCount == 2) // **bold**
                {
                    run.FontWeight = FontWeight.Bold;
                }
                yield return run;
            }
            else if (inline is LinkInline link)
            {
                yield return new Avalonia.Controls.Documents.Run(
                    $"[{link.Url}]");
            }
            else if (inline is CodeInline code)
            {
                yield return new Avalonia.Controls.Documents.Run(code.Content)
                {
                    FontFamily = new("Consolas, Courier New, monospace"),
                };
            }
        }
    }
}
```

#### Step 1.2: 修改 PreviewViewModel — 添加 Markdown 方法

在 `PreviewViewModel` 中添加：

```csharp
/// <summary>
/// Markdown 控件树根面板（替代 WebView2）。
/// </summary>
private Panel? _markdownPreviewPanel;

public Panel? MarkdownPreviewPanel
{
    get => _markdownPreviewPanel;
    set => SetProperty(ref _markdownPreviewPanel, value);
}
```

修改 `ShowMarkdownPreview`：

```csharp
public void ShowMarkdownPreview(string filePath)
{
    var markdown = File.ReadAllText(filePath);
    var panel = MarkdownPreviewBuilder.Build(markdown);
    MarkdownPreviewPanel = panel;
    PreviewType = PreviewType.Markdown;
    IsPreviewVisible = true;
    IsToolbarVisible = false;
}
```

> Markdown 的 `PreviewType` 不变（仍用 `Markdown`），但 XAML 中不再绑定 WebView，改为绑定 `MarkdownPreviewPanel`。

#### Step 1.3: 修改 PreviewPanel.axaml

替换 WebView2 的 ScrollViewer 区域：

```xml
<!-- 替换前 -->
<ScrollViewer IsVisible="{Binding IsWebViewVisible}"
              Background="{DynamicResource ThemeSurfaceBgBrush}">
  <wv2:NativeWebView x:Name="HtmlWebView" />
</ScrollViewer>

<!-- 替换后 -->
<!-- Markdown 预览（控件树，支持选择/复制） -->
<ScrollViewer IsVisible="{Binding IsMarkdownVisible}"
              Background="{DynamicResource ThemeSurfaceBgBrush}"
              Padding="16">
  <ContentControl Content="{Binding MarkdownPreviewPanel}" />
</ScrollViewer>
```

添加 `IsMarkdownVisible` 计算属性：

```csharp
public bool IsMarkdownVisible => PreviewType == PreviewType.Markdown;
```

</details>

---

### Task 2: HTML 预览（ReverseMarkdown → Markdig → 控件树）

HTML 文件先通过 `ReverseMarkdown.Converter` 转换为 Markdown 文本，再复用上面的 Markdig→控件树管线。这是最简单的方式，不需要完整 HTML 渲染器。

**文件：**
- Modify: `MantisZip.UI.Avalonia.csproj`（添加 `ReverseMarkdown` 依赖）
- Modify: `ViewModels/PreviewViewModel.cs`
- Remove: `Views/PreviewPanel.axaml` 中的 WebView 相关代码

<details>
<summary>实现详情</summary>

#### Step 2.1: 添加 ReverseMarkdown 依赖

```xml
<!-- MantisZip.UI.Avalonia.csproj -->
<PackageReference Include="ReverseMarkdown" Version="*" />
```

#### Step 2.2: 修改 PreviewViewModel — 添加 Html 方法

```csharp
public void ShowHtmlPreview(string filePath)
{
    var html = File.ReadAllText(filePath);
    // HTML → Markdown
    var converter = new ReverseMarkdown.Converter();
    var markdown = converter.Convert(html);
    // Markdown → 控件树
    var panel = MarkdownPreviewBuilder.Build(markdown);
    MarkdownPreviewPanel = panel;
    PreviewType = PreviewType.Html;
    IsPreviewVisible = true;
    IsToolbarVisible = false;
}
```

> HTML 文件预览后 `PreviewType` 设为 `Html`。XAML 中可以让 `IsHtmlVisible` 也复用 Markdown 控件树的 ScrollViewer：
>
> `IsVisible="{Binding IsHtmlVisible}"` → 与 Markdown 共用 `IsMarkdownVisible || IsHtmlVisible`，或拆两个 ScrollViewer（简单起见可以把 HTML 和 Markdown 合到同一个 `IsWebViewVisible` 指向的 ScrollViewer）。

实际建议：**HTML 和 Markdown 共用同一个 ScrollViewer**，因为两者都产 `MarkdownPreviewPanel`。XAML 中：

```xml
<ScrollViewer IsVisible="{Binding IsMarkdownOrHtmlVisible}"
              Padding="16">
  <ContentControl Content="{Binding MarkdownPreviewPanel}" />
</ScrollViewer>
```

```csharp
public bool IsMarkdownOrHtmlVisible => PreviewType is PreviewType.Markdown or PreviewType.Html;
```

</details>

---

### Task 3: PDF 预览（PdfPig + SkiaSharp 逐页渲染）

用 PdfPig 解析 PDF，`PdfPig.Rendering.Skia` 将每页渲染为 `SKBitmap`，转成 `WriteableBitmap`，展示在 Image 控件中。支持翻页、缩放（复用现有 ZoomFit/ZoomIn/ZoomOut 工具栏）。

**文件：**
- Create / Modify: `MantisZip.UI.Avalonia.csproj`（添加 PdfPig 依赖）
- Modify: `ViewModels/PreviewViewModel.cs`
- Modify: `Views/PreviewPanel.axaml`
- Modify: `Views/PreviewPanel.axaml.cs`（可选，如果翻页命令在 code-behind）

<details>
<summary>实现详情</summary>

#### Step 3.1: 添加 PdfPig 依赖

```xml
<!-- MantisZip.UI.Avalonia.csproj -->
<PackageReference Include="UglyToad.PdfPig" Version="*" />
<PackageReference Include="UglyToad.PdfPig.Rendering.Skia" Version="*" />
```

#### Step 3.2: 修改 PreviewViewModel — 添加 PDF 翻页属性与方法

添加到 PreviewViewModel：

```csharp
// ── PDF 翻页 ──

private UglyToad.PdfPig.PdfDocument? _pdfDocument;
private int _pdfTotalPages;

[ObservableProperty]
private int _pdfCurrentPage = 1;

[ObservableProperty]
private string _pdfPageInfo = string.Empty;

public bool IsPdfVisible => PreviewType == PreviewType.Pdf;
public bool HasPdfNavigation => IsPdfVisible && _pdfTotalPages > 1;

partial void OnPdfCurrentPageChanged(int value)
{
    if (_pdfDocument != null && value >= 1 && value <= _pdfTotalPages)
    {
        LoadPdfPage(value);
    }
}

[RelayCommand]
private void PdfPreviousPage()
{
    if (PdfCurrentPage > 1)
        PdfCurrentPage--;
}

[RelayCommand]
private void PdfNextPage()
{
    if (PdfCurrentPage < _pdfTotalPages)
        PdfCurrentPage++;
}
```

```csharp
public void ShowPdf(string filePath)
{
    // 解析元数据（已有 PdfParser）
    var info = PdfParser.Parse(filePath);
    if (info == null)
    {
        ShowUnsupported("无法解析 PDF 文件");
        return;
    }

    // 打开 PDF 文档
    _pdfDocument?.Dispose();
    _pdfDocument = UglyToad.PdfPig.PdfDocument.Open(filePath);
    _pdfTotalPages = _pdfDocument.NumberOfPages;
    PdfPageInfo = $"1 / {_pdfTotalPages}";

    // 渲染第一页
    PdfCurrentPage = 1;
    LoadPdfPage(1);

    PreviewType = PreviewType.Pdf;
    IsPreviewVisible = true;
    IsToolbarVisible = true;  // 复用缩放工具栏
    PreviewHeaderText = $"PDF {info.AdditionalInfo ?? ""}";

    FormatMetadata.Clear();
    if (info.Title != null) FormatMetadata.Add(new("标题", info.Title));
    if (info.Author != null) FormatMetadata.Add(new("作者", info.Author));
    if (info.PageCount.HasValue) FormatMetadata.Add(new("页数", info.PageCount.Value.ToString()));
    FormatMetadata.Add(new("加密", info.IsEncrypted == true ? "是" : "否"));
    // ...
}

private void LoadPdfPage(int pageNumber)
{
    try
    {
        if (_pdfDocument == null) return;

        var page = _pdfDocument.GetPage(pageNumber);
        using var skPage = UglyToad.PdfPig.Rendering.Skia.SkiaPdfDocument
            .Open(_pdfDocument)
            .GetPage(pageNumber);

        var width = (int)page.Width;
        var height = (int)page.Height;

        using var bitmap = skPage.Render(width, height, 96);
        // SKBitmap → WriteableBitmap
        // 复用现有字体预览的 Marshal.Copy 模式
        int stride = width * 4;
        byte[] pixelData = new byte[stride * height];
        System.Runtime.InteropServices.Marshal.Copy(
            bitmap.GetPixels(), pixelData, 0, pixelData.Length);

        var wb = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var locked = wb.Lock();
        System.Runtime.InteropServices.Marshal.Copy(
            pixelData, 0, locked.Address, pixelData.Length);

        PreviewImage = wb;
        ImageWidth = width;
        ImageHeight = height;
        PdfPageInfo = $"{pageNumber} / {_pdfTotalPages}";
        PdfCurrentPage = pageNumber;
        ZoomFit();
    }
    catch (Exception ex)
    {
        App.DebugLog($"[PDF] LoadPdfPage({pageNumber}) failed: {ex.Message}");
    }
}
```

#### Step 3.3: 修改 PreviewPanel.axaml — 添加 PDF 翻页工具栏

在 HTML/Markdown 控件树区域之后添加：

```xml
<!-- PDF 预览 -->
<ScrollViewer IsVisible="{Binding IsPdfVisible}"
              Background="{DynamicResource ThemeSurfaceBgBrush}">
  <Grid RowDefinitions="Auto,*">
    <!-- 翻页栏 -->
    <StackPanel Grid.Row="0" Orientation="Horizontal"
                HorizontalAlignment="Center" Spacing="8"
                IsVisible="{Binding HasPdfNavigation}">
      <Button Content="◀" Command="{Binding PdfPreviousPageCommand}"
              Width="28" Height="26" FontSize="14" Padding="0" />
      <TextBox Text="{Binding PdfCurrentPage}"
               Width="50" Height="26"
               HorizontalContentAlignment="Center"
               VerticalContentAlignment="Center" />
      <TextBlock Text="{Binding PdfPageInfo}"
                 VerticalAlignment="Center" />
      <Button Content="▶" Command="{Binding PdfNextPageCommand}"
              Width="28" Height="26" FontSize="14" Padding="0" />
    </StackPanel>
    <!-- PDF 页内容 -->
    <ScrollViewer Grid.Row="1"
                  HorizontalScrollBarVisibility="Auto"
                  VerticalScrollBarVisibility="Auto">
      <Image Source="{Binding PreviewImage}"
             Width="{Binding ScaledWidth}"
             Height="{Binding ScaledHeight}"
             Stretch="Uniform" />
    </ScrollViewer>
  </Grid>
</ScrollViewer>
```

#### Step 3.4: 修改 PreviewViewModel.OnPreviewTypeChanged

添加 `IsPdfVisible`、`HasPdfNavigation` 的通知：

```csharp
OnPropertyChanged(nameof(IsPdfVisible));
OnPropertyChanged(nameof(HasPdfNavigation));
```

`Clear()` 中释放 PDF 资源：

```csharp
_pdfDocument?.Dispose();
_pdfDocument = null;
_pdfTotalPages = 0;
PdfCurrentPage = 1;
PdfPageInfo = string.Empty;
```

</details>

---

### Task 4: 清理 WebView2 残余代码

WebView2 移除后，清理所有引用。

**文件：**
- Modify: `MantisZip.UI.Avalonia.csproj` — 移除 `Avalonia.Controls.WebView`
- Modify: `Views/PreviewPanel.axaml` — 移除 `xmlns:wv2` 和 `NativeWebView`
- Modify: `Views/PreviewPanel.axaml.cs` — 移除 `UpdateWebViewContent` 和 `OnVmPropertyChanged` 中的 HTML 订阅
- Modify: `ViewModels/PreviewViewModel.cs` — 移除 `HtmlContent` 属性（如果不再使用）

<details>
<summary>实现详情</summary>

#### Step 4.1: csproj 移除 WebView 包

```xml
<!-- 移除此行 -->
<!-- <PackageReference Include="Avalonia.Controls.WebView" Version="*" /> -->
```

#### Step 4.2: PreviewPanel.axaml — 移除 WebView 命名空间和控件

```xml
<!-- 移除：xmlns:wv2="clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls.WebView" -->
<!-- 移除：<wv2:NativeWebView x:Name="HtmlWebView" /> -->
```

#### Step 4.3: PreviewPanel.axaml.cs — 移除 WebView 相关代码

删除 `UpdateWebViewContent` 方法，删除 `OnVmPropertyChanged` 中的 HTML/Pdf 订阅（改为只处理 `InfoPanelOrientation` 和 DataGrid 列），删除 `HtmlWebView` 字段引用。

#### Step 4.4: PreviewViewModel — 清理

如果没有任何其他代码使用 `HtmlContent`，移除 `[ObservableProperty] private string _htmlContent`。保留 `ShowHtmlPreview` 和 `ShowMarkdownPreview`，但它们现在生成 `MarkdownPreviewPanel` 而非设置 `HtmlContent`。

</details>

---

### Task 5: 验证与清理

确保三路替换后的预览正常工作，且项目不再引用 WebView2。

- [ ] `dotnet build` 通过，无 WebView 相关引用
- [ ] 打开一个含 `.md` 文件的压缩包，选中 → 预览控件树
- [ ] 打开一个含 `.html` 文件的压缩包，选中 → 预览转换后的 Markdown
- [ ] 打开一个含 `.pdf` 文件的压缩包，选中 → 预览 PDF 第一页，翻页正常
- [ ] WebView2 工具栏/缩放功能对 PDF 页仍然可用
- [ ] 主题切换（Dark/Light）对 Markdown 控件树生效
- [ ] `Avalonia.Controls.WebView` 包已移除

---

## 注意事项 / 边界情况

1. **HTML→Markdown 转换精度**: ReverseMarkdown 不是完美的 HTML 渲染器。复杂页面（嵌套表格、脚本、CSS）会丢失样式。这对于压缩包内预览场景是可行的——用户更多是快速查看文档内容而非精确排版。

2. **PDF 渲染质量**: PdfPig.Rendering.Skia 不支持透明混合（transparency group）、OcGs（图层）、部分高级 PDF 特性。遇到这些页面会渲染空白或异常。通过 try-catch 兜底，失败时显示「当前页无法渲染」而非崩溃。

3. **性能**: 大 PDF（100+页）首次解析较慢。PdfPig 是逐页解析，打开大文档首次渲染可能需要几百毫秒。可在加载页面时加 loading 状态（已有 `IsLoadingPreview` 机制）。

4. **HTML/PDF 场景退化**: 如果 PDF 渲染失败，降级为显示 PDF 元数据 + 提示"当前 PDF 版本不支持渲染"。

5. **`PreviewType` 保持**: `Html` / `Markdown` / `Pdf` 三种 PreviewType 枚举值不变，只改变底层渲染实现，不涉及 switch 的 case 修改。

6. **可访问性**: Markdown 控件树使用 `SelectableTextBlock`，文本可选中/复制，用户体验优于 WebView2 纯 show。

---

## 执行顺序建议

1. Task 1 (Markdown) — 基础，HTML 也依赖它
2. Task 2 (HTML) — 依赖 Task 1 的 `MarkdownPreviewBuilder`
3. Task 3 (PDF) — 独立，可并行开发
4. Task 4 (清理) — 依赖前三个完成
5. Task 5 (验证) — 最终确认

推荐 Task 1+3 并行，Task 2 紧随 Task 1。
