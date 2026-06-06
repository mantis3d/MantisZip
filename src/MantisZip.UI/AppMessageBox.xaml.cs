using System.Windows;

namespace MantisZip.UI;

/// <summary>
/// 统一风格的对话框，替代原生 MessageBox。
/// </summary>
public partial class AppMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private Action? _action;

    private AppMessageBox(string message, string title, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;

        // 根据图标类型设置 emoji
        if (icon != MessageBoxImage.None)
        {
            IconText.Text = icon switch
            {
                MessageBoxImage.Error => "❌",
                MessageBoxImage.Warning => "⚠️",
                MessageBoxImage.Question => "❓",
                _ => "ℹ️",
            };
            IconText.Visibility = Visibility.Visible;
        }

        // 配置按钮
        switch (button)
        {
            case MessageBoxButton.OK:
                OkBtn.Visibility = Visibility.Visible;
                OkBtn.Focus();
                break;
            case MessageBoxButton.OKCancel:
                OkBtn.Visibility = Visibility.Visible;
                CancelBtn.Visibility = Visibility.Visible;
                CancelBtn.Focus();
                break;
            case MessageBoxButton.YesNo:
                YesBtn.Visibility = Visibility.Visible;
                NoBtn.Visibility = Visibility.Visible;
                NoBtn.Focus();
                break;
            case MessageBoxButton.YesNoCancel:
                YesBtn.Visibility = Visibility.Visible;
                NoBtn.Visibility = Visibility.Visible;
                CancelBtn.Visibility = Visibility.Visible;
                CancelBtn.Focus();
                break;
        }
    }

    private AppMessageBox(string message, string title, MessageBoxButton button, MessageBoxImage icon,
        string? actionButtonText, Action? action)
        : this(message, title, button, icon)
    {
        _action = action;
        if (actionButtonText != null && action != null)
        {
            ActionBtn.Content = actionButtonText;
            ActionBtn.Visibility = Visibility.Visible;
        }
    }

    public static MessageBoxResult Show(string message, string title = "",
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new AppMessageBox(message, title, button, icon);
        dialog.ShowDialog();
        return dialog._result;
    }

    public static MessageBoxResult Show(Window owner, string message, string title = "",
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new AppMessageBox(message, title, button, icon)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog._result;
    }

    public static void ShowWithAction(string message, string title,
        string actionButtonText, Action action,
        MessageBoxImage icon = MessageBoxImage.Information)
    {
        var dialog = new AppMessageBox(message, title, MessageBoxButton.OK, icon,
            actionButtonText, action);
        dialog.ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.OK;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        Close();
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Yes;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.No;
        Close();
    }

    private void ActionBtn_Click(object sender, RoutedEventArgs e)
    {
        _action?.Invoke();
        _result = MessageBoxResult.OK;
        Close();
    }
}
