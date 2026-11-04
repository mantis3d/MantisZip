using System;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Threading;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 内嵌文件选择器「新建文件夹」（2026-11-04）的行为契约测试。
///
/// 覆盖六件事：①PickFolder 建完进入新目录并经 PathChanged 通知宿主
/// ②PickItems 简单模式建完**不清空**已累积选择、也不导航
/// ③同名已存在拒绝且不落盘 ④非法字符 / 空名拒绝 ⑤建议名自动去重 ⑥OpenFile 建完不导航。
///
/// 弹窗本身不在这里驱动：控件的 <c>TryCreateFolder</c> 是唯一落盘入口，
/// 输入框只负责收集名字（见 <c>Dialogs/InputDialog</c>）。
/// </summary>
public class CustomFilePickerNewFolderTests
{
    private static (Window Window, CustomFilePickerControl Picker, string Dir) Show(PickerMode mode)
    {
        EnsureTheme();

        var dir = Path.Combine(Path.GetTempPath(), "mz-newfolder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        var picker = new CustomFilePickerControl { Mode = mode };
        var window = new Window { Width = 900, Height = 620, Content = picker };
        window.Show();
        picker.Initialize(initialPath: dir);

        // 初始目录受 AppSettings.DefaultPathOrder 影响，这里显式落到测试目录
        Assert.True(picker.TryNavigate(dir));
        Pump(window);

        return (window, picker, dir);
    }

    private static void EnsureTheme()
    {
        var app = Application.Current!;
        if (app.Styles.OfType<global::Avalonia.Themes.Fluent.FluentTheme>().Any()) return;

        app.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        });
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void Cleanup(Window window, string dir)
    {
        window.Close();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果
        }
    }

    [AvaloniaFact]
    public void PickFolder_CreateFolder_NavigatesIntoItAndNotifiesHost()
    {
        var (window, picker, dir) = Show(PickerMode.PickFolder);

        string? notified = null;
        picker.PathChanged += (_, path) => notified = path;

        Assert.True(picker.TryCreateFolder("新目录", out var error));
        Assert.Null(error);

        var created = Path.Combine(dir, "新目录");
        Assert.True(Directory.Exists(created));
        // 「当前目录即结果」：必须真的导航进去，否则宿主（解压目标 / 压缩输出）拿不到新路径
        Assert.Equal(created, picker.CurrentPath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(created, notified, StringComparer.OrdinalIgnoreCase);

        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void PickItems_SimpleMode_CreateFolder_KeepsSelectionAndStaysPut()
    {
        var (window, picker, dir) = Show(PickerMode.PickItems);
        var fileA = Path.Combine(dir, "a.txt");
        picker.SetSelectedPaths([fileA]);

        Assert.True(picker.TryCreateFolder("新目录", out _));

        // 简单模式切目录会清空累积（NavigateTo:867），所以这里必须停在原目录、选择原样保留
        Assert.Equal(dir, picker.CurrentPath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { fileA }, picker.SelectedPaths);
        Assert.True(Directory.Exists(Path.Combine(dir, "新目录")));

        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void ExistingName_ReturnsErrorAndDoesNotCreate()
    {
        var (window, picker, dir) = Show(PickerMode.PickFolder);
        Directory.CreateDirectory(Path.Combine(dir, "已存在"));

        Assert.False(picker.TryCreateFolder("已存在", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));

        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void EmptyOrInvalidName_ReturnsError()
    {
        var (window, picker, dir) = Show(PickerMode.PickFolder);

        Assert.False(picker.TryCreateFolder("   ", out var emptyError));
        Assert.False(string.IsNullOrWhiteSpace(emptyError));

        Assert.False(picker.TryCreateFolder("bad?name", out var invalidError));
        Assert.False(string.IsNullOrWhiteSpace(invalidError));

        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void SuggestNewFolderName_DeduplicatesExistingFolder()
    {
        var (window, picker, dir) = Show(PickerMode.PickFolder);
        var baseName = LocalizationManager.T("Picker_NewFolderDefaultName");

        Assert.Equal(baseName, picker.SuggestNewFolderName());

        Directory.CreateDirectory(Path.Combine(dir, baseName));
        Assert.Equal($"{baseName} (2)", picker.SuggestNewFolderName());

        Directory.CreateDirectory(Path.Combine(dir, $"{baseName} (2)"));
        Assert.Equal($"{baseName} (3)", picker.SuggestNewFolderName());

        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void OpenFile_CreateFolder_DoesNotNavigate()
    {
        var (window, picker, dir) = Show(PickerMode.OpenFile);

        Assert.True(picker.TryCreateFolder("空目录", out _));

        // 新建的是空目录，进去没有可选文件 → 停在原目录，只刷新列表
        Assert.Equal(dir, picker.CurrentPath, StringComparer.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.Combine(dir, "空目录")));

        Cleanup(window, dir);
    }
}
