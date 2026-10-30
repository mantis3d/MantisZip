using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 无头探针：压缩窗口「文件冲突策略」下拉是否真的把选中项写回 ViewModel。
/// 若绑定失效，界面上显示「跳过」但 VM 仍是「每次询问」→ 压缩时照样弹冲突框。
/// </summary>
public sealed class CompressConflictComboProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mz_combo_{Guid.NewGuid():N}");

    public CompressConflictComboProbeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { /* 忽略清理失败 */ }
    }

    [AvaloniaFact]
    public void ConflictCombo_Selection_WritesBackToViewModel()
    {
        var source = Path.Combine(_dir, "data.txt");
        File.WriteAllText(source, "hello");

        // 探针只验证「下拉 ←→ VM」绑定，与源路径无关：用设计时无参构造（生产构造要求注入执行器）。
        var window = new CompressSettingsWindow();

        // 窗口的 ViewModel 属性已收窄为 private（接缝规则 20：生产调用方不得回读窗口构造请求）；
        // 测试走 DataContext —— 窗口构造里就是 `DataContext = ViewModel`，等价且不依赖诊断属性。
        var vm = Assert.IsType<CompressSettingsViewModel>((object?)window.DataContext);
        try { global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } catch { /* 无头环境 Screens 不可用，与断言无关 */ }
        var combo = window.FindControl<ComboBox>("ConflictActionCombo");
        Assert.NotNull(combo);

        var skipIndex = vm.ConflictActionOptions
            .Select((o, i) => (o, i))
            .First(x => x.o.Tag == "skip").i;

        combo!.SelectedIndex = skipIndex;

        Assert.Equal("skip", vm.ConflictAction);
        window.Close();
    }
}
