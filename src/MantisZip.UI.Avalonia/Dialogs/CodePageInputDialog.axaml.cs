using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 「其他代码页…」输入框：让用户填一个代码页数字（如 950 = Big5、932 = Shift-JIS），
/// 供主工具栏的文件名代码页下拉使用。
/// <para>
/// 只用数字（<see cref="TextInput"/> 里拦掉非数字字符），确定后经 <see cref="ResultCodePage"/> 回传。
/// </para>
/// </summary>
public partial class CodePageInputDialog : Window
{
    /// <summary>用户确定时填的代码页；取消或非法输入时为 null。</summary>
    public int? ResultCodePage { get; private set; }

    /// <summary>窗口标题。</summary>
    public string WinTitle => LocalizationManager.T("Toolbar_NameEncoding_OtherTitle");

    /// <summary>说明文字（同时也是输入框占位提示）。</summary>
    public string HintText => LocalizationManager.T("Toolbar_NameEncoding_OtherHint");

    /// <summary>确定按钮文字。</summary>
    public string OkText => LocalizationManager.T("Common_OK");

    /// <summary>取消按钮文字。</summary>
    public string CancelText => LocalizationManager.T("Common_Cancel");

    /// <summary>供 XAML 加载器使用的无参构造（规则 17：x:Class 必须有公共无参构造）。</summary>
    public CodePageInputDialog()
    {
        InitializeComponent();
        DataContext = this;

        // 只接受数字：避免用户输入 "Big5" 这类文字后静默失败
        CodePageTextBox.TextInput += OnCodePageTextInput;
    }

    /// <summary>枚举：返回用户填的代码页，取消返回 null。</summary>
    public static async Task<int?> ShowAsync(Window? owner)
    {
        var dialog = new CodePageInputDialog();
        if (owner is not null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return dialog.ResultCodePage;
    }

    /// <summary>拦掉非数字按键（粘贴路径走 TextChanged 校验，见 <see cref="Ok_Click"/>）。</summary>
    private void OnCodePageTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (!char.IsAsciiDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>确定：解析失败（非数字 / ≤0）就当取消，不写入结果。</summary>
    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (int.TryParse(CodePageTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var codePage)
            && codePage > 0)
        {
            ResultCodePage = codePage;
            Close();
        }
    }

    /// <summary>取消：结果保持 null。</summary>
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
