using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Converts Markdown text into a pure Avalonia control tree using Markdig AST.
/// Replaces the previous Markdig → HTML → WebView2 pipeline for Markdown preview.
/// </summary>
public static class MarkdownPreviewBuilder
{
    /// <summary>
    /// Parse the given Markdown string and return an Avalonia <see cref="StackPanel"/> control tree.
    /// </summary>
    public static StackPanel Build(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras()
            .UseTaskLists()
            .Build();
        var doc = Markdown.Parse(markdown, pipeline);

        var panel = new StackPanel { Spacing = 4 };

        foreach (var block in doc)
        {
            if (TryBuildBlock(block, markdown) is Control c)
                panel.Children.Add(c);
        }

        return panel;
    }

    #region Block builders

    /// <summary>
    /// <paramref name="source"/> is the original markdown source, required for code block
    /// text extraction via <see cref="MarkdownObject.Span"/> positions (Markdig 0.40 API).
    /// </summary>
    private static Control? TryBuildBlock(Block block, string source)
    {
        switch (block)
        {
            case HeadingBlock h:
                return BuildHeading(h);
            case ParagraphBlock p:
                return BuildParagraph(p);
            case FencedCodeBlock fcb:
                return CreateCodeBorder(ExtractFencedCode(source, fcb.Span.Start, fcb.Span.End));
            case CodeBlock cb:
                return CreateCodeBorder(ExtractIndentedCode(source, cb.Span.Start, cb.Span.End));
            case ListBlock lb:
                return BuildList(lb, source);
            case QuoteBlock qb:
                return BuildQuote(qb, source);
            case Table table:
                return BuildTable(table, source);
            case ThematicBreakBlock:
                return new Separator { Margin = new Thickness(0, 8) };
            default:
                return null;
        }
    }

    private static Control BuildHeading(HeadingBlock heading)
    {
        var fontSize = heading.Level switch
        {
            1 => 24.0,
            2 => 20.0,
            3 => 18.0,
            4 => 16.0,
            5 => 15.0,
            _ => 14.0
        };
        var fontWeight = heading.Level <= 2 ? FontWeight.Bold : FontWeight.Normal;

        var tb = new SelectableTextBlock
        {
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap,
        };
        BuildInlines(heading.Inline, tb.Inlines);
        return tb;
    }

    private static Control BuildParagraph(ParagraphBlock paragraph)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        BuildInlines(paragraph.Inline, tb.Inlines);
        return tb;
    }

    private static Border CreateCodeBorder(string code)
    {
        var isDark = IsDarkTheme();
        var bgColor = isDark ? Color.Parse("#2d2d2d") : Color.Parse("#f0f0f0");
        var fgColor = isDark ? Color.Parse("#e0e0e0") : Color.Parse("#1a1a1a");

        return new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Child = new SelectableTextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(fgColor),
                TextWrapping = TextWrapping.Wrap,
            }
        };
    }

    /// <summary>
    /// Extract code text from a fenced code block (```...```) using source spans.
    /// Strips the opening fence line (e.g. "```csharp") and closing fence ("```").
    /// </summary>
    private static string ExtractFencedCode(string source, int start, int end)
    {
        var length = end - start + 1;
        var text = source.Substring(start, length);
        var lines = text.Split('\n');

        // Strip opening fence (first line) and closing fence (last line)
        int startIdx = 1;
        int endIdx = lines.Length - 1;

        // Handle the case where closing fence is the last line
        while (endIdx >= startIdx && lines[endIdx].TrimStart().StartsWith("```"))
            endIdx--;

        return string.Join("\n", lines, startIdx, endIdx - startIdx + 1);
    }

    /// <summary>
    /// Extract code text from an indented code block using source spans.
    /// Removes the leading 4-space indent from each line.
    /// </summary>
    private static string ExtractIndentedCode(string source, int start, int end)
    {
        var length = end - start + 1;
        var text = source.Substring(start, length);
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length >= 4)
                lines[i] = lines[i].Substring(4);
        }

        return string.Join("\n", lines);
    }

    private static Control BuildList(ListBlock listBlock, string source)
    {
        var listPanel = new StackPanel { Spacing = 2 };
        int index = 1;

        foreach (var item in listBlock)
        {
            if (item is ListItemBlock listItem)
            {
                var itemPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4
                };

                var bullet = listBlock.IsOrdered ? $"{index}. " : "\u2022 ";
                itemPanel.Children.Add(new TextBlock
                {
                    Text = bullet,
                    VerticalAlignment = VerticalAlignment.Top,
                    FontSize = 14,
                });

                // Each ListItemBlock contains block children (usually ParagraphBlock)
                foreach (var childBlock in listItem)
                {
                    if (TryBuildBlock(childBlock, source) is Control c)
                        itemPanel.Children.Add(c);
                }

                listPanel.Children.Add(itemPanel);
                if (listBlock.IsOrdered) index++;
            }
        }

        return listPanel;
    }

    private static Control BuildQuote(QuoteBlock quoteBlock, string source)
    {
        var isDark = IsDarkTheme();
        var borderColor = isDark ? Color.Parse("#555") : Color.Parse("#ddd");

        var innerPanel = new StackPanel { Spacing = 4 };
        foreach (var child in quoteBlock)
        {
            if (TryBuildBlock(child, source) is Control c)
                innerPanel.Children.Add(c);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 4),
            Child = innerPanel,
        };
    }

    /// <summary>
    /// Build an Avalonia Grid from a Markdig pipe-table <see cref="Table"/> block.
    /// Header row uses ThemeHeaderBgBrush, body rows use ThemeSurfaceBgBrush,
    /// column widths follow the parsed table's percent/auto definitions.
    /// Merged cells (colspan/rowspan) are not supported.
    /// </summary>
    private static Control BuildTable(Table table, string source)
    {
        // Column count: prefer parsed definitions, fall back to widest row
        var colCount = table.ColumnDefinitions.Count;
        if (colCount == 0)
        {
            foreach (var row in table)
            {
                if (row is TableRow tr && tr.Count > colCount)
                    colCount = tr.Count;
            }
        }
        if (colCount == 0) return new TextBlock { Text = string.Empty };

        var borderBrush = GetThemeBrush("ThemeBorderBrush");
        var headerBg = GetThemeBrush("ThemeHeaderBgBrush");
        var cellBg = GetThemeBrush("ThemeSurfaceBgBrush");

        var grid = new Grid { Margin = new Thickness(0, 4) };

        // Column definitions: percent width (float > 0) → Star with ratio, otherwise Auto
        for (int i = 0; i < colCount; i++)
        {
            if (i < table.ColumnDefinitions.Count)
            {
                var width = table.ColumnDefinitions[i].Width;
                if (width > 0)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(
                        new GridLength(width, GridUnitType.Star)));
                    continue;
                }
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        int rowIdx = 0;
        foreach (var rowObj in table)
        {
            if (rowObj is not TableRow row) continue;

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            bool isHeader = row.IsHeader;

            for (int colIdx = 0; colIdx < colCount; colIdx++)
            {
                var cellObj = colIdx < row.Count ? row[colIdx] : null;
                var content = new StackPanel();

                if (cellObj is TableCell cell)
                {
                    foreach (var block in cell)
                    {
                        if (TryBuildBlock(block, source) is Control c)
                            content.Children.Add(c);
                    }
                }

                var cellBorder = new Border
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Background = isHeader ? headerBg : cellBg,
                    Padding = new Thickness(6, 3),
                    Child = content,
                };
                // TextBlocks inside inherit Foreground from the content panel's visual
                // ancestors, which are set by the preview ScrollViewer — no explicit
                // Foreground needed (Border has no Foreground property in Avalonia).
                Grid.SetRow(cellBorder, rowIdx);
                Grid.SetColumn(cellBorder, colIdx);
                grid.Children.Add(cellBorder);
            }
            rowIdx++;
        }

        return grid;
    }

    /// <summary>
    /// Resolve a theme resource brush by key (e.g. "ThemeBorderBrush").
    /// Returns null when the resource is missing, letting callers fall back.
    /// </summary>
    private static IBrush? GetThemeBrush(string key)
    {
        if (Application.Current is not global::Avalonia.Controls.IResourceHost host)
            return null;
        return host.TryFindResource(key, out var value) ? value as IBrush : null;
    }



    /// <summary>
    /// Recursively build Avalonia Inline elements from a Markdig inline container.
    /// </summary>
    private static void BuildInlines(ContainerInline? container, InlineCollection target)
    {
        if (container?.FirstChild == null) return;

        var inline = container.FirstChild;
        while (inline != null)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    target.Add(new Run { Text = lit.Content.ToString() });
                    break;

                case EmphasisInline emp:
                    BuildEmphasis(emp, target);
                    break;

                case CodeInline code:
                    var isDark = IsDarkTheme();
                    var codeBg = isDark ? Color.Parse("#2d2d2d") : Color.Parse("#e8e8e8");
                    target.Add(new Run
                    {
                        Text = code.Content,
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(codeBg),
                    });
                    break;

                case LinkInline link:
                    // Show URL as plain text (no click handling per spec)
                    target.Add(new Run { Text = link.Url ?? string.Empty });
                    break;

                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
            }

            inline = inline.NextSibling;
        }
    }

    private static void BuildEmphasis(EmphasisInline emp, InlineCollection target)
    {
        var isBold = emp.DelimiterCount == 2;
        var isItalic = emp.DelimiterCount == 1;

        if (isBold)
        {
            var bold = new Bold();
            BuildInlines(emp, bold.Inlines);
            target.Add(bold);
        }
        else if (isItalic)
        {
            var italic = new Italic();
            BuildInlines(emp, italic.Inlines);
            target.Add(italic);
        }
        else
        {
            // Fallback for other delimiter counts (e.g. 3 = bold+italic)
            // Treat as bold for simplicity
            var bold = new Bold();
            BuildInlines(emp, bold.Inlines);
            target.Add(bold);
        }
    }

    #endregion

    /// <summary>
    /// 判断当前是否为暗色主题。基于 RequestedThemeVariant（App.ApplyTheme 已解析
    /// System 模式并设置该值），避免直接读取 AppSettings.Theme 漏判 "System"。
    /// </summary>
    private static bool IsDarkTheme()
        => Application.Current?.RequestedThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark;
}
