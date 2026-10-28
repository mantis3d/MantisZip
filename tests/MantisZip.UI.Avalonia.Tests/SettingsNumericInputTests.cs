using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 设置窗口「数字输入框」回归测试。
///
/// 背景（2026-10-28 报告）：自动关闭延时原本是普通 <c>TextBox</c> 直绑 <c>int</c>，用户敲
/// 任意字符 → 绑定转换失败 → Avalonia 默认 DataValidationErrors 模板在控件下方渲染一条**红色报错文本**。
/// 现在所有数字项统一用 <c>NumericUpDown Classes="intOnly"</c>（只解析整数，非法输入不进入绑定）。
/// 本测试同时盯住「源码不再退回 TextBox」和「NumericUpDown 本身不产生校验错误」两件事。
/// </summary>
public class SettingsNumericInputTests
{
    /// <summary>设置窗口里直绑 int / long 的属性名（改绑定方式时同步这里）。</summary>
    private static readonly string[] NumericProperties =
    [
        "AutoCloseDelaySeconds",
        "MaxRecentFiles",
        "MaxTablePreviewRows",
        "MaxTablePreviewCols",
        "DoubleClickOpenThresholdMB",
        "Row",
        "Order",
    ];

    [AvaloniaFact]
    public void SettingsWindow_NumericSettings_BindThroughNumericUpDown()
    {
        var xaml = ReadRepoFile("src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml");

        foreach (var property in NumericProperties)
        {
            Assert.DoesNotContain($"Text=\"{{Binding {property}}}\"", xaml);

            // delay / 数字项必须出现 Value="{Binding 属性" 的写法（TextBox 用 Text=，NumericUpDown 用 Value=）
            Assert.Contains($"Value=\"{{Binding {property}", xaml);
        }

        // 整数解析样式靠 .intOnly（见 App.axaml），漏挂会让 NumericUpDown 接受小数并显示 8.5
        Assert.Contains("NumericUpDown Classes=\"intOnly\"", xaml);
    }

    [AvaloniaFact]
    public void NumericUpDown_InvalidText_DoesNotRaiseValidationError()
    {
        var up = new NumericUpDown
        {
            ParsingNumberStyle = System.Globalization.NumberStyles.Integer,
            Minimum = 0,
            Maximum = 60,
            Increment = 1,
            Value = 30,
        };

        var host = new Window { Content = up };
        host.Show();

        // 用户敲进非数字：值保持合法，且不出现 DataValidationErrors 的红色文本
        up.Text = "abc";
        Dispatcher.UIThread.RunJobs();

        Assert.False(DataValidationErrors.GetHasErrors(up));
        if (up.Value is { } value)
        {
            Assert.InRange(value, 0, 60);
        }

        // 数字文本可正常解析
        up.Text = "15";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(15, up.Value);

        host.Close();
    }

    /// <summary>
    /// NumericUpDown 的 <c>Value</c> 是 <c>decimal?</c>，而设置项里有 <c>long</c>（双击阈值字节数）。
    /// 双向绑定靠 Avalonia 的隐式数值转换，这条用例把「decimal? ↔ long」钉死，避免哪天静默失灵。
    /// </summary>
    [AvaloniaFact]
    public void NumericUpDown_TwoWayBinding_ToLongProperty_Works()
    {
        var vm = new LongHolder { Bytes = 1024 * 1024 };
        var up = new NumericUpDown
        {
            ParsingNumberStyle = System.Globalization.NumberStyles.Integer,
            Minimum = 0,
            DataContext = vm,
        };
        up.Bind(NumericUpDown.ValueProperty,
            new global::Avalonia.Data.Binding(nameof(LongHolder.Bytes)) { Mode = global::Avalonia.Data.BindingMode.TwoWay });

        var host = new Window { Content = up };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        // 控件 → 模型
        up.Value = 42;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(42L, vm.Bytes);

        // 模型 → 控件
        vm.Bytes = 7L;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7m, up.Value);

        host.Close();
    }

    /// <summary>绑定时用的最小可观察对象（只测绑定转换，不需要 AvaloniaProperty）。</summary>
    private sealed class LongHolder : System.ComponentModel.INotifyPropertyChanged
    {
        private long _bytes;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public long Bytes
        {
            get => _bytes;
            set
            {
                _bytes = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Bytes)));
            }
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件: {relativePath}");
    }
}
