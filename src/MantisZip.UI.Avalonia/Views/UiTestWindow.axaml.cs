using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

/// <summary>
/// UI 控件测试窗口（开发者诊断工具，文案豁免本地化，见 AGENTS.md 规则 13 豁免条款）。
/// </summary>
public partial class UiTestWindow : Window
{
    public UiTestWindow()
    {
        InitializeComponent();
        DataContext = new UiTestViewModel();
    }

    /// <summary>
    /// 「TextBox + 数字过滤」演示：拒绝非数字字符输入（最朴素的数字输入方案，无上下调值）。
    /// 仅用于测试窗口的选型对比，业务代码如需复用建议抽成附加行为（Behavior）。
    /// </summary>
    private void NumericOnlyTextBox_TextInput(object? sender, TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text) && !e.Text.All(char.IsDigit))
            e.Handled = true;
    }
}
