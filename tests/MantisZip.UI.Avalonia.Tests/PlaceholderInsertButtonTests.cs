using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「插入占位符」按钮的行为测试：菜单项来源于 <c>FileNameTemplate.Placeholders</c>，
/// 点其中一项要把占位符插到目标输入框的光标处。
/// </summary>
public class PlaceholderInsertButtonTests
{
    [AvaloniaFact]
    public void ClickingButton_OpensFlyoutWithAllPlaceholders()
    {
        var (window, button, _) = Show(initialText: string.Empty);

        Assert.NotNull(button.Flyout);
        Click(window, button);

        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        Assert.True(flyout.IsOpen, $"点击后下拉菜单应当打开（attached={button.IsAttachedToVisualTree()}, items={flyout.Items.Count}）");
        Assert.Equal(8, flyout.Items.Count);
        var headers = flyout.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Contains("{name}", headers[0]);
        Assert.Contains("{seq:0000}", headers[7]);
    }

    [AvaloniaFact]
    public void InsertingToken_AtCaret_InsertsThereAndMovesCaretAfterIt()
    {
        var (window, button, textBox) = Show(initialText: "report.zip");
        // 真实场景里用户先在输入框里打字（持有焦点），再点「插入占位符」
        textBox.Focus();
        textBox.CaretIndex = 6;
        textBox.SelectionStart = 6;
        textBox.SelectionEnd = 6;

        Click(window, button);
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        var item = flyout.Items.OfType<MenuItem>().First();
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("report{name}.zip", textBox.Text);
        Assert.Equal(12, textBox.CaretIndex);
    }

    [AvaloniaFact]
    public void InsertingToken_WithoutFocus_AppendsAtEnd()
    {
        var (window, button, textBox) = Show(initialText: "report");

        Click(window, button);
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        var item = flyout.Items.OfType<MenuItem>().First();
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("report{name}", textBox.Text);
        Assert.Equal(12, textBox.CaretIndex);
    }

    private static (Window Window, PlaceholderInsertButton Button, TextBox TextBox) Show(string initialText)
    {
        var app = Application.Current ?? new Application();
        if (app.Styles.Count == 0) app.Styles.Add(new FluentTheme());

        var textBox = new TextBox { Text = initialText, Width = 300 };
        var button = new PlaceholderInsertButton { TargetTextBox = textBox, Content = "insert" };
        var panel = new StackPanel();
        panel.Children.Add(textBox);
        panel.Children.Add(button);
        var window = new Window { Width = 420, Height = 240, Content = panel };
        window.Show();
        Pump();
        return (window, button, textBox);
    }

    private static void Click(Window window, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        Pump();
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}
