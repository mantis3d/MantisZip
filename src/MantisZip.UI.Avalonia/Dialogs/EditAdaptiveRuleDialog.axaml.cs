using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs
{
    public partial class EditAdaptiveRuleDialog : Window
    {
        public string RuleName { get; private set; } = "";
        public AdaptiveLevel SelectedLevel { get; private set; } = AdaptiveLevel.Store;
        public int? CustomLevel { get; private set; }

        // ── Localized string properties ──
        public string DialogTitle { get; private set; } = LocalizationManager.T("Settings_AdaptiveRules_AddTitle");
        public string PromptText => LocalizationManager.T("Settings_AdaptiveRules_AddTitle");
        public string NameLabelText => LocalizationManager.T("Settings_AdaptiveRules_NamePrompt");
        public string LevelLabelText => LocalizationManager.T("Settings_AdaptiveRules_LevelPrompt");
        public string CancelText => LocalizationManager.T("MsgBox_Cancel");
        public string OkText => LocalizationManager.T("MsgBox_Ok");

        /// <summary>
        /// AdaptiveLevel 枚举值到中文显示文本的映射
        /// </summary>
        private static readonly Dictionary<AdaptiveLevel, string> LevelDisplayMap = new()
        {
            [AdaptiveLevel.Store] = "存储",
            [AdaptiveLevel.Fast] = "快速",
            [AdaptiveLevel.Normal] = "正常",
            [AdaptiveLevel.Max] = "最大",
            [AdaptiveLevel.Global] = "跟随全局",
            [AdaptiveLevel.GlobalPlusOne] = "全局+1",
            [AdaptiveLevel.GlobalMinusOne] = "全局-1",
            [AdaptiveLevel.Custom] = "自定义",
        };

        public EditAdaptiveRuleDialog()
        {
            InitializeComponent();
            DataContext = this;

            // 填充压缩级别下拉框
            foreach (var level in Enum.GetValues<AdaptiveLevel>())
            {
                LevelComboBox.Items.Add(LevelDisplayMap.GetValueOrDefault(level, level.ToString()));
            }
            LevelComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// 编辑模式下预填充已有规则信息
        /// </summary>
        public void SetExistingValues(string name, AdaptiveLevel level)
        {
            RuleNameTextBox.Text = name;
            var idx = Array.IndexOf(Enum.GetValues<AdaptiveLevel>(), level);
            if (idx >= 0) LevelComboBox.SelectedIndex = idx;
        }

        private void Ok_Click(object? sender, RoutedEventArgs e)
        {
            // 验证规则名称不能为空
            var name = (RuleNameTextBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                _ = AppMessageBox.Show(LocalizationManager.T("Settings_AdaptiveRules_NamePrompt"), "", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            RuleName = name;
            var selectedIndex = LevelComboBox.SelectedIndex;
            if (selectedIndex >= 0)
            {
                SelectedLevel = Enum.GetValues<AdaptiveLevel>()[selectedIndex];
            }
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
