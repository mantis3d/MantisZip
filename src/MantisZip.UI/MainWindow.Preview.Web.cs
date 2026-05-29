using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Markdig;
using Markdig.Extensions.Emoji;
using Microsoft.Web.WebView2.Core;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
    // ── HTML Preview ──

    private async Task ShowHtmlPreview(string filePath, ArchiveItem item)
    {
        _cachedHtmlPath = filePath;
        _cachedHtmlItem = item;
        _previewSourceFormat = PreviewSourceFormat.Html;

        await EnsureWebView2InitializedAsync();
        HideAllPreviewControls();

        if (_previewShowSource)
        {
            PreviewTextBox.Text = File.ReadAllText(filePath);
            PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
            PreviewTextBox.TextAlignment = TextAlignment.Left;
            PreviewTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewWebView2.CoreWebView2.Navigate(new Uri(filePath).AbsoluteUri);
            PreviewWebView2.Visibility = Visibility.Visible;
        }

        SetPreviewInfo(item, L.T(L.Preview_HtmlInfo));
        PreviewHeader.Text = L.TF(L.Preview_HtmlHeader, Path.GetFileName(filePath));
        SetToolbar(
            Array.Empty<ToolbarButton>(),
            new[] {
                new ToolbarButton { Text = "</>", Tooltip = L.T(L.Preview_ToggleSource), IsToggle = true, IsChecked = _previewShowSource, OnClickAsync = TogglePreviewSource },
            }
        );
        ShowPreviewPanel();
    }

    // ── Markdown Preview ──

    private async Task ShowMarkdownPreview(string filePath, ArchiveItem item)
    {
        try
        {
            // 缓存状态，供 emoji 切换/源码切换后重新渲染
            _cachedMarkdownPath = filePath;
            _cachedMarkdownItem = item;
            _previewSourceFormat = PreviewSourceFormat.Markdown;

            HideAllPreviewControls();

            if (_previewShowSource)
            {
                PreviewTextBox.Text = File.ReadAllText(filePath);
                PreviewTextBox.FontSize = _markdownPreviewFontSize;
                PreviewTextBox.TextAlignment = TextAlignment.Left;
                PreviewTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                var html = await RenderMarkdownToHtmlAsync(filePath);
                var tempHtml = Path.Combine(Path.GetDirectoryName(filePath) ?? _previewTempDir!, "markdown_preview.html");
                File.WriteAllText(tempHtml, html);
                await EnsureWebView2InitializedAsync();
                PreviewWebView2.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
                PreviewWebView2.Visibility = Visibility.Visible;
            }

            SetPreviewInfo(item, "📝 Markdown");
            PreviewHeader.Text = L.TF(L.Preview_MarkdownHeader, Path.GetFileName(filePath));
            // 工具栏：左侧字号 ± ，右侧 源码切换 + emoji 短代码切换
            SetToolbar(
                new[] {
                    new ToolbarButton { Text = "A−", Tooltip = L.T(L.Preview_FontDecrease), OnClickAsync = () => ChangeMarkdownFontSize(-1) },
                    new ToolbarButton { Text = "A+", Tooltip = L.T(L.Preview_FontIncrease), OnClickAsync = () => ChangeMarkdownFontSize(1) },
                },
                new[] {
                new ToolbarButton { Text = "</>", Tooltip = L.T(L.Preview_ToggleSource), IsToggle = true, IsChecked = _previewShowSource, OnClickAsync = TogglePreviewSource },
                    new ToolbarButton { Text = "😊", Tooltip = "Emoji 短代码转换", IsToggle = true, IsChecked = _markdownEmojiEnabled, OnClickAsync = ToggleMarkdownEmoji },
                }
            );
            ShowPreviewPanel();
        }
        catch (Exception mdEx)
        {
            App.LogDebug("ShowMarkdownPreview: failed: {0}", mdEx.Message);
            ShowUnsupportedPreview(null, "无法解析 Markdown 文件");
        }
    }

    /// <summary>
    /// 渲染 Markdown → HTML。若 <see cref="_markdownEmojiEnabled"/> 开启，
    /// 先替换 :emoji: 短代码为 Unicode 字符，再交由 Markdig 处理。
    /// 注意：不可直接在 pipeline 中使用 UseEmojiAndSmiley()，
    /// 因为它会把 :---: 表格对齐标记也误识别为 emoji 短代码，破坏所有 pipe table 渲染。
    /// </summary>
    private Task<string> RenderMarkdownToHtmlAsync(string filePath)
    {
        var mdContent = File.ReadAllText(filePath);
        if (_markdownEmojiEnabled)
            mdContent = ApplyEmojiShortcodes(mdContent);

        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras()
            .UseTaskLists()
            .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
            .Build();

        var html = Markdig.Markdown.ToHtml(mdContent, pipeline);

        // 根据应用主题选择颜色，使 markdown 渲染与 WPF 主题同步
        var isDark = AppSettings.Instance.Theme == "Dark";
        var bodyFg = isDark ? "#e0e0e0" : "#222";
        var bodyBg = isDark ? "#1e1e1e" : "#fff";
        var codeBg = isDark ? "#2d2d2d" : "#f0f0f0";
        var preBg = isDark ? "#2d2d2d" : "#f4f4f4";
        var borderColor = isDark ? "#444" : "#ccc";

        return Task.FromResult($@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/>
<style>
  body {{ font-family: system-ui, sans-serif; font-size: {_markdownPreviewFontSize}px; line-height: 1.6; padding: 16px; color: {bodyFg}; background: {bodyBg}; }}
  pre {{ background: {preBg}; padding: 12px; border-radius: 4px; overflow-x: auto; }}
  code {{ background: {codeBg}; padding: 2px 4px; border-radius: 2px; font-family: Consolas, monospace; }}
  pre code {{ background: none; padding: 0; }}
  img {{ max-width: 100%; }}
  table {{ border-collapse: collapse; }}
  td, th {{ border: 1px solid {borderColor}; padding: 6px 10px; }}
</style></head>
<body>{html}</body></html>");
    }

    /// <summary>
    /// 用 Markdig 内置的 emoji 短代码 → Unicode 映射表替换文本中的 :emoji: 模式。
    /// 替换前先保护代码块内容（行内反引号、围栏代码块），避免代码内的短代码被误转换。
    /// </summary>
    private static string ApplyEmojiShortcodes(string markdown)
    {
        var mapping = GetEmojiMapping();
        if (mapping == null || mapping.Count == 0) return markdown;

        // 1. 找出所有代码块片段并替换为占位符
        //    占位符用 %%EMOJI_PH_{n}%% ，不含 : 冒号，不会被 emoji 正则匹配到
        var fragments = new List<string>();
        var phRegex = new System.Text.RegularExpressions.Regex(@"```[\s\S]*?```|`[^`]*`");
        int phIndex = 0;
        markdown = phRegex.Replace(markdown, match =>
        {
            fragments.Add(match.Value);
            return $"%%EMOJI_PH_{phIndex++}%%";
        });

        // 2. 替换 emoji 短代码
        markdown = System.Text.RegularExpressions.Regex.Replace(markdown,
            @":[a-zA-Z0-9_+]+(?::[a-zA-Z0-9_+]+)*:",
            match => mapping.TryGetValue(match.Value, out var emoji) ? emoji : match.Value);

        // 3. 恢复代码块
        for (int i = 0; i < fragments.Count; i++)
            markdown = markdown.Replace($"%%EMOJI_PH_{i}%%", fragments[i]);

        return markdown;
    }

    private static Dictionary<string, string> GetEmojiMapping()
    {
        if (_emojiMapping == null)
            _emojiMapping = (Dictionary<string, string>)EmojiMapping.GetDefaultEmojiShortcodeToUnicode();
        return _emojiMapping;
    }

    private async Task ToggleMarkdownEmoji()
    {
        _markdownEmojiEnabled = !_markdownEmojiEnabled;
        await ReRenderMarkdownAsync();
    }

    /// <summary>
    /// 切换源码/渲染预览模式。
    /// 在源码模式下显示文件原始内容（PreviewTextBox），隐藏 WebView2；
    /// 切回渲染模式时重新渲染（Markdown）或重新导航（HTML）。
    /// </summary>
    private async Task TogglePreviewSource()
    {
        _previewShowSource = !_previewShowSource;

        if (_previewShowSource)
        {
            // 切到源码：确定当前显示的是哪个文件
            string? sourcePath = _previewSourceFormat switch
            {
                PreviewSourceFormat.Markdown => _cachedMarkdownPath,
                PreviewSourceFormat.Html => _cachedHtmlPath,
                _ => null
            };

            if (sourcePath != null && File.Exists(sourcePath))
            {
                PreviewWebView2.Visibility = Visibility.Collapsed;
                PreviewTextBox.Text = File.ReadAllText(sourcePath);
                PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
                PreviewTextBox.TextAlignment = TextAlignment.Left;
                PreviewTextBox.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // 切回渲染
            PreviewTextBox.Visibility = Visibility.Collapsed;

            switch (_previewSourceFormat)
            {
                case PreviewSourceFormat.Markdown:
                    await ReRenderMarkdownAsync();
                    PreviewWebView2.Visibility = Visibility.Visible;
                    break;
                case PreviewSourceFormat.Html:
                    if (PreviewWebView2.CoreWebView2 != null)
                        PreviewWebView2.CoreWebView2.Navigate(new Uri(_cachedHtmlPath!).AbsoluteUri);
                    PreviewWebView2.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private async Task ReRenderMarkdownAsync()
    {
        if (_cachedMarkdownPath == null || _cachedMarkdownItem == null)
            return;

        try
        {
            var html = await RenderMarkdownToHtmlAsync(_cachedMarkdownPath);
            var tempHtml = Path.Combine(Path.GetDirectoryName(_cachedMarkdownPath) ?? _previewTempDir!, "markdown_preview.html");
            File.WriteAllText(tempHtml, html);
            if (PreviewWebView2.CoreWebView2 != null)
                PreviewWebView2.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
        }
        catch (Exception ex)
        {
            App.LogDebug("ReRenderMarkdownAsync: failed: {0}", ex.Message);
        }
    }

    private async Task ChangeMarkdownFontSize(int delta)
    {
        // 通过修改 WebView2 页面字体大小的 CSS 来实现 — 更可靠的方式是重新渲染带不同字号样式的 HTML
        // 简单实现：重新渲染时修改 body 字号
        _markdownPreviewFontSize = Math.Clamp(_markdownPreviewFontSize + delta, 10, 32);
        await ReRenderMarkdownAsync();
    }
}
