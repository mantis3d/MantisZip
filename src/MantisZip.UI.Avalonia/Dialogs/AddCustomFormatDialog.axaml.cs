using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs
{
    public partial class AddCustomFormatDialog : Window
    {
        public string FormatName { get; private set; } = "";
        public string ExtensionsRaw { get; private set; } = "";
        public string MagicHex { get; private set; } = "";

        // ── Localized string properties ──
        public string DialogTitle { get; private set; } = LocalizationManager.T("Settings_FormatCatalog_AddTitle");
        public string EditTitle => LocalizationManager.T("Settings_FormatCatalog_EditTitle");
        public string PromptText => LocalizationManager.T("Settings_FormatCatalog_AddPrompt");
        public string CancelText => LocalizationManager.T("MsgBox_Cancel");
        public string OkText => LocalizationManager.T("MsgBox_Ok");

        public AddCustomFormatDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// 编辑模式下预填充已有格式信息
        /// </summary>
        public void SetExistingValues(string name, List<string> extensions, string? magicHex)
        {
            NameTextBox.Text = name;
            ExtensionsTextBox.Text = string.Join(", ", extensions);
            MagicHexTextBox.Text = magicHex ?? "";
        }

        private void Ok_Click(object? sender, RoutedEventArgs e)
        {
            // 验证格式名称不能为空
            var name = (NameTextBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                _ = AppMessageBox.Show(LocalizationManager.T("Settings_FormatCatalog_AddPrompt"), "", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            // 解析扩展名列表：按逗号分割，每个转小写并补前缀 "."
            var raw = (ExtensionsTextBox.Text ?? "").Trim();
            ExtensionsRaw = raw;
            MagicHex = (MagicHexTextBox.Text ?? "").Trim();
            FormatName = name;
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
