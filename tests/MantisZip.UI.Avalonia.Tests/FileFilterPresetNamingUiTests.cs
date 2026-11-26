using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 过滤预设下拉的显示与选中接线（<see cref="FileFilterEditor"/>）。
///
/// 两组不变量：
/// ① 显示名——8 条内置预设走 i18n 键（英文界面不显示中文），用户预设原样显示；
/// ② 选中回找——运行期标识一律用 <see cref="FileFilterPreset.Key"/>。
///    内置预设的 <see cref="FileFilterPreset.Name"/> 恒为空串，若有人把比对改回 Name，
///    8 条内置预设会全部命中空串、下拉永远停在第 1 条——这层错了构建与门禁都发现不了。
/// </summary>
public sealed class FileFilterPresetNamingUiTests
{
    [AvaloniaFact]
    public void Combo_ShowsLocalizedNames_ForBuiltInPresets()
    {
        var editor = Show();
        var combo = FindCombo(editor);

        var labels = Items(combo).Select(i => i.DisplayName).ToList();

        Assert.Contains(LocalizationManager.T("FileFilter_BuiltInPreset_Image"), labels);
        Assert.Contains(LocalizationManager.T("FileFilter_BuiltInPreset_ExcludeTemp"), labels);
        // 中文内置名一旦被硬编码回来，英文界面下就会命中这条
        Assert.DoesNotContain("📷 仅图片", labels.Where(l => LocalizationManager.CurrentLanguage != AppLanguage.Chinese));
    }

    [AvaloniaFact]
    public void BuiltInPresetEntries_CarryTheKey_NotTheDisplayName()
    {
        var editor = Show();
        var combo = FindCombo(editor);

        var builtIn = Items(combo).FirstOrDefault(i => i.Preset?.NameKey == "FileFilter_BuiltInPreset_Image");
        Assert.NotNull(builtIn);
        // Key 是运行期标识；Name 留空是刻意的（显示名走 NameKey）
        Assert.Equal("FileFilter_BuiltInPreset_Image", builtIn!.Preset!.Key);
        Assert.Equal(string.Empty, builtIn.Preset.Name);
        // 下拉里显示的是本地化文本，不是键名
        Assert.NotEqual("FileFilter_BuiltInPreset_Image", builtIn.DisplayName);
    }

    [AvaloniaFact]
    public void SelectingBuiltInPreset_TracksItByKey_SoReselectFindsTheSameRow()
    {
        var editor = Show();
        var combo = FindCombo(editor);

        Pick(combo, "FileFilter_BuiltInPreset_Archive");

        // 选内置预设后控件必须记住「是这一条」：按 Name 比对的话这里会回落到第 0 项（"— 无 —"）。
        // 注意不能断言 Same：选中后控件会重建列表，实例换了但 Key 必须还在。
        var selected = combo.SelectedItem as PresetItem;
        Assert.NotNull(selected);
        Assert.Equal("FileFilter_BuiltInPreset_Archive", selected!.Preset?.Key);
        Assert.NotEmpty(editor.GetFilter().IncludeExtensions);
    }

    [AvaloniaFact]
    public void SwitchingBetweenBuiltInPresets_KeepsTheRightOneSelected()
    {
        var editor = Show();
        var combo = FindCombo(editor);

        Pick(combo, "FileFilter_BuiltInPreset_Audio");
        var afterAudio = (combo.SelectedItem as PresetItem)?.Preset?.Key;
        Assert.Equal("FileFilter_BuiltInPreset_Audio", afterAudio);

        // 每次选中后控件都会重建列表，所以必须重新从下拉里取条目，不能复用旧实例
        Pick(combo, "FileFilter_BuiltInPreset_Image");

        var reselected = combo.SelectedItem as PresetItem;
        Assert.NotNull(reselected);
        Assert.Equal("FileFilter_BuiltInPreset_Image", reselected!.Preset?.Key);
        Assert.Contains(".png", editor.GetFilter().IncludeExtensions);
    }

    [AvaloniaFact]
    public void UserPreset_IsDisplayedVerbatim_AndKeptByItsName()
    {
        var userPreset = new FileFilterPreset("我的规则", new FileFilterCriteria
        {
            IncludeExtensions = new List<string> { ".md" },
        });
        var editor = Show(new List<FileFilterPreset> { userPreset });
        var combo = FindCombo(editor);

        var item = Items(combo).SingleOrDefault(i => i.Preset == userPreset);
        Assert.NotNull(item);
        // 用户输入的名字是数据，不翻译
        Assert.Equal("我的规则", item!.DisplayName);
        Assert.Equal("我的规则", item.Preset!.Key);
    }

    [AvaloniaFact]
    public void EditingFilter_AfterChoosingBuiltInPreset_CreatesTempPresetNamedLocally()
    {
        var editor = Show();
        var combo = FindCombo(editor);

        Pick(combo, "FileFilter_BuiltInPreset_Image");

        // 改一个过滤项 → 控件会派生「（已修改）」临时预设。
        // 临时名必须是本地化显示名 + 后缀，绝不能把 i18n 键拼进去。
        var namePattern = editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NamePatternBox");
        namePattern.Text = "*.special";
        Pump();

        var temp = Items(combo).SingleOrDefault(i => i.IsTemporary);
        Assert.NotNull(temp);
        Assert.NotEqual("FileFilter_BuiltInPreset_Image（已修改）", temp!.DisplayName);
        Assert.StartsWith(LocalizationManager.T("FileFilter_BuiltInPreset_Image"), temp.DisplayName);
        Assert.Equal("FileFilter_BuiltInPreset_Image", temp.SourcePresetName);
    }

    // ── 辅助 ──

    private static FileFilterEditor Show(List<FileFilterPreset>? userPresets = null)
    {
        var app = Application.Current ?? new Application();
        if (app.Styles.Count == 0) app.Styles.Add(new FluentTheme());

        var editor = new FileFilterEditor();
        var window = new Window { Width = 900, Height = 600, Content = editor };
        window.Show();
        window.UpdateLayout();
        editor.LoadPresets(userPresets ?? new List<FileFilterPreset>());
        Pump();
        return editor;
    }

    private static ComboBox FindCombo(FileFilterEditor editor)
        => editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PresetCombo");

    private static List<PresetItem> Items(ComboBox combo)
        => (combo.ItemsSource as IEnumerable<PresetItem>)?.ToList() ?? new List<PresetItem>();

    /// <summary>
    /// 按 Key 在**当前**下拉列表里挑一条选中。
    /// 每次都重新读 ItemsSource：选中后控件会重建列表并把新实例塞回去，
    /// 复用旧实例选不上（而且那种失败会被"什么都没发生"掩盖）。
    /// </summary>
    private static void Pick(ComboBox combo, string key)
    {
        var target = Items(combo).Single(i => i.Preset?.Key == key);
        combo.SelectedItem = target;
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
