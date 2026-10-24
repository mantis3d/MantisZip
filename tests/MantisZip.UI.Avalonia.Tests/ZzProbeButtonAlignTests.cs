using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Xunit.Abstractions;

namespace MantisZip.UI.Avalonia.Tests;

public class ZzProbeButtonAlignTests
{
    private readonly ITestOutputHelper _out;
    public ZzProbeButtonAlignTests(ITestOutputHelper output) => _out = output;

    [AvaloniaFact]
    public void Probe_ToolbarButton_TextVerticalPosition()
    {
        Application.Current!.Styles.Add(new FluentTheme());

        var btn = new Button
        {
            Content = "添加规则",
            Height = 42,
            Padding = new Thickness(8, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };

        var win = new Window { Content = btn, Width = 300, Height = 200 };
        win.Show();
        win.UpdateLayout();

        _out.WriteLine($"button bounds = {btn.Bounds}");
        _out.WriteLine($"VerticalContentAlignment = {btn.VerticalContentAlignment}");
        _out.WriteLine($"HorizontalContentAlignment = {btn.HorizontalContentAlignment}");

        foreach (var d in btn.GetVisualDescendants())
        {
            if (d is ContentPresenter cp)
                _out.WriteLine($"presenter = {cp.Bounds} align={cp.VerticalContentAlignment}");
            if (d is TextBlock tb)
                _out.WriteLine($"textblock = {tb.Bounds} (relative to button: {tb.TranslatePoint(new Point(0, 0), btn)})");
        }
    }
}
