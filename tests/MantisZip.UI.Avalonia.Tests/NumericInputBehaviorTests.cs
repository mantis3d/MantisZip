using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MantisZip.UI.Avalonia.Behaviors;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// <see cref="NumericInput"/> 附加行为的回归测试。
///
/// <para>
/// 背景（2026-10-28 事故）：<c>TextBox</c> 直绑 <c>int</c> 时用户敲字母会让绑定转换失败，
/// Avalonia 默认模板在控件下方渲染红色校验文本。行为在输入层拦截非法字符、失焦 clamp，
/// 本文件锁住四个可观察承诺：挂接即清洗、<c>TextInput</c> 拒收非数字、失焦 clamp 与还原、
/// 以及关闭后不再干预。
/// </para>
/// </summary>
public class NumericInputBehaviorTests
{
    /// <summary>创建一个已挂行为、可指定边界与初始文本的 <see cref="TextBox"/>。</summary>
    private static TextBox CreateBox(int? minimum = null, int? maximum = null, string? text = null)
    {
        var box = new TextBox();
        if (minimum is { } min)
        {
            NumericInput.SetMinimum(box, min);
        }

        if (maximum is { } max)
        {
            NumericInput.SetMaximum(box, max);
        }

        if (text is not null)
        {
            box.Text = text;
        }

        NumericInput.SetEnabled(box, true);
        return box;
    }

    [AvaloniaFact]
    public void Enabled_AttachingSanitizesExistingText()
    {
        // 挂接前就带非法字符（例如从旧版本配置或程序化写入），挂接瞬间必须清洗成规范整数文本
        var box = CreateBox(text: "12a3");

        Assert.Equal("123", box.Text);
    }

    [AvaloniaFact]
    public void LostFocus_ClampsToMinimum()
    {
        // 低于下界的值在失焦时被抬到 Minimum（挂接阶段的清洗只处理非法字符，不处理越界）
        var box = CreateBox(minimum: 10, maximum: 60, text: "5");
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent) { Source = box });

        Assert.Equal("10", box.Text);
    }

    [AvaloniaFact]
    public void LostFocus_ClampsToMaximum()
    {
        var box = CreateBox(minimum: 0, maximum: 60, text: "999");
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent) { Source = box });

        Assert.Equal("60", box.Text);
    }

    [AvaloniaFact]
    public void LostFocus_RestoresLastValidValue_WhenTextIsEmpty()
    {
        var box = CreateBox(text: "15");

        // 清空文本（用户全选删除），失焦时应还原为上一个合法值而不是写空串
        box.Text = string.Empty;
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent) { Source = box });

        Assert.Equal("15", box.Text);
    }

    [AvaloniaFact]
    public void Minimum_Negative_KeepsLeadingMinus_OthersStripIt()
    {
        // Minimum < 0 才允许负号；未设负边界的输入框会把负号当非法字符剥掉
        Assert.Equal("-5", CreateBox(minimum: -100, text: "-5").Text);
        Assert.Equal("5", CreateBox(text: "-5").Text);
    }

    [AvaloniaFact]
    public void Disabled_LeavesTextUntouched()
    {
        var box = new TextBox { Text = "12a3" };
        NumericInput.SetEnabled(box, true);
        NumericInput.SetEnabled(box, false);

        // 关闭后不再干预：此时写入非法文本不会被清洗
        box.Text = "x9";
        Assert.Equal("x9", box.Text);
    }
}
