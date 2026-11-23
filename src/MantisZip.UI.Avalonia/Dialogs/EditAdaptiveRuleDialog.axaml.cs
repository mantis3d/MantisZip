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

        /// <summary>
        /// 用户在对话框里改过名字（与传入的显示文本不同）。
        /// 为 false 表示「没动名字」——调用方必须保留原来的 <c>NameKey</c>，让出厂规则继续跟随界面语言。
        /// </summary>
        public bool NameChanged { get; private set; }

        /// <summary>编辑模式传进来的原始显示文本，用于判断用户是否真的改了名字。</summary>
        private string _originalName = "";

        // ── Localized string properties（标题与提示在新增/编辑两种模式下文案不同，
        //    由构造与 SetExistingValues 直接写入控件；其余静态文案走绑定） ──
        public string NameLabelText => LocalizationManager.T("Settings_AdaptiveRules_NamePrompt");
        public string LevelLabelText => LocalizationManager.T("Settings_AdaptiveRules_LevelPrompt");
        public string FormatsLabelText => LocalizationManager.T("Settings_AdaptiveRules_Formats");
        public string FormatsHintText => LocalizationManager.T("Settings_AdaptiveRules_FormatsHint");
        public string CancelText => LocalizationManager.T("MsgBox_Cancel");
        public string OkText => LocalizationManager.T("MsgBox_Ok");

        /// <summary>
        /// AdaptiveLevel 枚举值到本地化显示文本（规则 13：三语 key；按调用时解析，支持运行期切语言）
        /// </summary>
        private static string LevelDisplay(AdaptiveLevel level) => level switch
        {
            AdaptiveLevel.Store => LocalizationManager.T("Settings_AdaptiveRuleLevel_Store"),
            AdaptiveLevel.Fast => LocalizationManager.T("Settings_AdaptiveRuleLevel_Fast"),
            AdaptiveLevel.Normal => LocalizationManager.T("Settings_AdaptiveRuleLevel_Normal"),
            AdaptiveLevel.Max => LocalizationManager.T("Settings_AdaptiveRuleLevel_Max"),
            AdaptiveLevel.Global => LocalizationManager.T("Settings_AdaptiveRuleLevel_Global"),
            AdaptiveLevel.GlobalPlusOne => LocalizationManager.T("Settings_AdaptiveRuleLevel_GlobalPlusOne"),
            AdaptiveLevel.GlobalMinusOne => LocalizationManager.T("Settings_AdaptiveRuleLevel_GlobalMinusOne"),
            AdaptiveLevel.Custom => LocalizationManager.T("Settings_AdaptiveRuleLevel_CustomName"),
            _ => LocalizationManager.T("Settings_AdaptiveRuleLevel_Unknown")
        };

        public EditAdaptiveRuleDialog()
        {
            InitializeComponent();
            DataContext = this;

            // 标题与说明行在新增/编辑两种模式下文案不同（编辑时由 SetExistingValues 覆盖）
            var addTitle = LocalizationManager.T("Settings_AdaptiveRules_AddTitle");
            Title = addTitle;
            PromptBlock.Text = addTitle;
            RuleNameTextBox.PlaceholderText = LocalizationManager.T("Settings_AdaptiveRules_NamePlaceholder");

            // 填充压缩级别下拉框
            foreach (var level in Enum.GetValues<AdaptiveLevel>())
            {
                LevelComboBox.Items.Add(LevelDisplay(level));
            }
            LevelComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// 取主题画刷（规则 4：暗色主题下硬编码黑色文字不可读）。
        /// 资源缺失时返回 null，让 TextBlock 走继承色而不是写死颜色。
        /// </summary>
        private IBrush? ResolveThemeBrush(string key)
            => this.TryFindResource(key, out var value) ? value as IBrush : null;

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
                    // 规则 4：文字颜色必须走主题画刷，硬编码黑色在深色主题下会看不见
                    Foreground = ResolveThemeBrush("ThemeTextPrimaryBrush"),
                };
                FormatsPanel.Children.Add(cb);
            }
        }

        /// <summary>
        /// 编辑模式下预填充已有规则信息
        /// </summary>
        /// <param name="name">当前**显示**的规则名（出厂规则是已本地化的文本）。</param>
        /// <param name="level">压缩级别。</param>
        /// <param name="formatIds">已选格式 ID。</param>
        public void SetExistingValues(string name, AdaptiveLevel level, List<string>? formatIds = null)
        {
            RuleNameTextBox.Text = name;
            _originalName = (name ?? "").Trim();

            // 标题：编辑模式用「编辑规则」，新增模式保留「添加规则」
            var editTitle = LocalizationManager.T("Settings_AdaptiveRules_EditTitle");
            Title = editTitle;
            PromptBlock.Text = editTitle;

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
            // 改名判定：只有文本真的变了才算用户自定义名。没改名时保持 false，
            // 调用方据此保留原 NameKey，让出厂规则继续跟随界面语言。
            NameChanged = !string.Equals(name, _originalName, StringComparison.Ordinal);

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
