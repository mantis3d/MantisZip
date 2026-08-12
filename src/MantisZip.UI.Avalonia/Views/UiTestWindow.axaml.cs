using Avalonia.Controls;
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
}
