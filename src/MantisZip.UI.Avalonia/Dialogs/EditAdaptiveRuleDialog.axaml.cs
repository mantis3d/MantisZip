using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs
{
    public partial class EditAdaptiveRuleDialog : Window
    {
        public string RuleName { get; private set; } = "";
        public AdaptiveLevel SelectedLevel { get; private set; } = AdaptiveLevel.Store;
        public int? CustomLevel { get; private set; }
        public List<string> SelectedFormatIds { get; private set; } = new();

        // ── Localized string properties ──
        public string DialogTitle { get; private set; } = LocalizationManager.T("Settings_AdaptiveRules_AddTitle");
        public string PromptText => LocalizationManager.T("Settings_AdaptiveRules_AddTitle");
        public string NameLabelText => LocalizationManager.T("Settings_AdaptiveRules_NamePrompt");
        public string LevelLabelText => LocalizationManager.T("Settings_AdaptiveRules_LevelPrompt");
        public string FormatsLabelText => LocalizationManager.T("Settings_AdaptiveRules_Formats");
        public string FormatsHintText => LocalizationManager.T("Settings_AdaptiveRules_FormatsHint");
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
        /// 填充格式多选列表。由调用方在 ShowDialog 前调用。
        /// </summary>
        /// <param name="allFormats">所有可用格式（内置 + 自定义）。</param>
        /// <param name="selectedIds">已选中的格式 ID（编辑模式），可为 null。</param>
        public void PopulateFormats(IReadOnlyList<FormatDefinition> allFormats, List<string>? selectedIds = null)
        {
            FormatsPanel.Children.Clear();
            foreach (var fmt in allFormats)
            {
                var cb = new CheckBox
                {
                    Content = $"{fmt.DisplayName} ({string.Join(", ", fmt.Extensions)})",
                    Tag = fmt.Id,
                    IsChecked = selectedIds?.Contains(fmt.Id) ?? false,
                    Foreground = Brushes.Black,
                };
                FormatsPanel.Children.Add(cb);
            }
        }

        /// <summary>
        /// 编辑模式下预填充已有规则信息
        /// </summary>
        public void SetExistingValues(string name, AdaptiveLevel level, List<string>? formatIds = null)
        {
            RuleNameTextBox.Text = name;
            var idx = Array.IndexOf(Enum.GetValues<AdaptiveLevel>(), level);
            if (idx >= 0) LevelComboBox.SelectedIndex = idx;

            // 预选格式
            if (formatIds != null)
            {
                foreach (var child in FormatsPanel.Children)
                {
                    if (child is CheckBox cb && cb.Tag is string id)
                        cb.IsChecked = formatIds.Contains(id);
                }
            }
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

            // 收集选中的格式 ID
            SelectedFormatIds.Clear();
            foreach (var child in FormatsPanel.Children)
            {
                if (child is CheckBox cb && cb.IsChecked == true && cb.Tag is string id)
                    SelectedFormatIds.Add(id);
            }

            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
