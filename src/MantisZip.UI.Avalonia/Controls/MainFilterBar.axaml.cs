using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 主窗口过滤栏（文字搜索 / 匹配模式 / 排除 / 日期范围 / 大小范围 / 清除全部）。
///
/// 2026-10-21 启动优化：从 <c>MainWindow.axaml</c> 抽出为独立 UserControl，**按需创建**——
/// 过滤栏默认隐藏，启动时无需实例化（省 ~50 个元素的 XAML 膨胀）；
/// 由 <c>MainWindow</c> 在 <c>IsFilterBarVisible</c> 首次为 true 时创建并放入宿主
/// （见 <c>MainWindow.EnsureFilterBar()</c>）。
/// </summary>
public partial class MainFilterBar : UserControl
{
    public MainFilterBar()
    {
        InitializeComponent();
    }

    // ── 过滤 picker 按钮（🧪 从选中条目取值）──

    private void PickDateFrom_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;
        try
        {
            if (vm.SelectedEntries.Count == 0)
            {
                WritePickerTrace("PickDateFrom: SelectedEntries is empty");
                return;
            }
            var dates = vm.SelectedEntries
                .Where(i => i.LastModified > DateTime.MinValue)
                .Select(i => i.LastModified)
                .ToList();
            if (dates.Count == 0)
            {
                WritePickerTrace("PickDateFrom: no valid dates, entries=" + vm.SelectedEntries.Count);
                return;
            }
            var minDate = dates.Min();
            WritePickerTrace($"PickDateFrom: minDate={minDate:O}, picker.IsNull={FilterDateFromPicker == null}");
            vm.FilterDateFrom = minDate;
            if (FilterDateFromPicker != null)
                FilterDateFromPicker.SelectedDate = minDate;
        }
        catch (Exception ex)
        {
            WritePickerTrace($"PickDateFrom ERROR: {ex}");
        }
    }

    private void PickDateTo_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;
        try
        {
            if (vm.SelectedEntries.Count == 0)
            {
                WritePickerTrace("PickDateTo: SelectedEntries is empty");
                return;
            }
            var dates = vm.SelectedEntries
                .Where(i => i.LastModified > DateTime.MinValue)
                .Select(i => i.LastModified)
                .ToList();
            if (dates.Count == 0)
            {
                WritePickerTrace("PickDateTo: no valid dates");
                return;
            }
            var maxDate = dates.Max();
            vm.FilterDateTo = maxDate;
            if (FilterDateToPicker != null)
                FilterDateToPicker.SelectedDate = maxDate;
        }
        catch (Exception ex)
        {
            WritePickerTrace($"PickDateTo ERROR: {ex}");
        }
    }

    private void PickSizeMin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedEntries.Count == 0) return;
        var sizes = vm.SelectedEntries
            .Where(i => !i.IsDirectory)
            .Select(i => i.Size)
            .ToList();
        if (sizes.Count == 0) return;
        vm.FilterSizeMin = sizes.Min();
        vm.FilterSizeUnit = "B";
    }

    private void PickSizeMax_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedEntries.Count == 0) return;
        var sizes = vm.SelectedEntries
            .Where(i => !i.IsDirectory)
            .Select(i => i.Size)
            .ToList();
        if (sizes.Count == 0) return;
        vm.FilterSizeMax = sizes.Max();
        vm.FilterSizeUnit = "B";
    }

    private static void WritePickerTrace(string msg)
    {
        var logPath = Path.Combine(
            Models.AppSettings.DataDir, "debug.log");
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PN] {msg}\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow log write failed: {ex.Message}");
        }
    }
}
