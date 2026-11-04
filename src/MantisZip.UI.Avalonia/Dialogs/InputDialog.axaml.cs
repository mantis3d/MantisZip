using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 通用「输入一行文本」弹窗：标题 + 提示 + 输入框 + 确定 / 取消。
/// <para>
/// 与旧版（纯代码窗口，位于 <c>Controls/FileFilterEditor.axaml.cs</c>）相比有三点变化：
/// ① 画刷全部走主题资源（规则 4，旧版是默认色，深色主题下白底黑字）；
/// ② 新增 <c>validator</c> 回调：返回非 null 文案即视为校验失败——**留窗**并把原因显示在输入框下方
///    （规则 20 的同一原则：失败必须让用户看得见，不许静默关窗）；
/// ③ 输入框获得焦点并全选初值，直接打字即可覆盖。
/// </para>
/// <para>
/// 调用方约定：<see cref="ShowDialog"/> 返回 <see langword="true"/> 时读 <see cref="InputText"/>；
/// 返回 <see langword="false"/> / <see langword="null"/>（取消、Esc、关闭按钮）一律视为放弃，
/// 不产生任何副作用。
/// </para>
/// </summary>
public partial class InputDialog : Window
{
    /// <summary>用户确定时的输入文本（未确定前为空串）。</summary>
    public string InputText { get; private set; } = string.Empty;

    /// <summary>窗口标题（调用方给定，通常已本地化）。</summary>
    public string WinTitle { get; } = string.Empty;

    /// <summary>输入框上方的提示语（已本地化）。</summary>
    public string PromptText { get; } = string.Empty;

    /// <summary>确定按钮文字。</summary>
    public string OkText => LocalizationManager.T("Common_OK");

    /// <summary>取消按钮文字。</summary>
    public string CancelText => LocalizationManager.T("Common_Cancel");

    /// <summary>校验回调：入参为输入框当前文本，返回本地化错误文案表示不通过；null = 通过。</summary>
    private readonly Func<string, string?>? _validator;

    /// <summary>供 XAML 加载器使用的无参构造（规则 17：x:Class 必须有公共无参构造）。</summary>
    public InputDialog() : this(string.Empty, string.Empty)
    {
    }

    /// <summary>
    /// 构造弹窗。
    /// </summary>
    /// <param name="title">窗口标题（已本地化）。</param>
    /// <param name="prompt">输入框上方提示语（已本地化）。</param>
    /// <param name="defaultValue">初值；非空时构造后全选，方便直接覆盖重打。</param>
    /// <param name="validator">可选的校验回调；返回文案表示不通过（留窗显示）。</param>
    public InputDialog(string title, string prompt, string defaultValue = "", Func<string, string?>? validator = null)
    {
        InitializeComponent();

        WinTitle = title;
        PromptText = prompt;
        _validator = validator;
        DataContext = this;

        InputTextBox.Text = defaultValue;
        Opened += (_, _) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };
    }

    /// <summary>确定：校验通过才写结果并关窗；不通过则留窗并把原因显示在输入框下方。</summary>
    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var text = (InputTextBox.Text ?? string.Empty).Trim();
        var error = _validator?.Invoke(text);
        if (error is not null)
        {
            ErrorTextBlock.Text = error;
            ErrorTextBlock.IsVisible = true;
            InputTextBox.Focus();
            InputTextBox.SelectAll();
            return;
        }

        InputText = text;
        Close(true);
    }

    /// <summary>取消：不写结果，调用方按放弃处理。</summary>
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
