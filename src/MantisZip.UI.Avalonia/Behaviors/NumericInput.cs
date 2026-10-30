using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MantisZip.UI.Avalonia.Behaviors;

/// <summary>
/// TextBox 的整数输入附加行为：让普通输入框也能安全直绑 <c>int</c>，外观保持 TextBox 原样。
///
/// <para>
/// 背景：<c>TextBox.Text</c> 直接绑定 <c>int</c> / <c>long</c> 时，用户敲入字母或粘贴非数字文本会让
/// 绑定转换失败，Avalonia 默认的 <c>DataValidationErrors</c> 模板会在控件下方渲染红色校验文本。
/// 本行为在输入层拦截非法字符，并在文本变化时清洗、失焦时 clamp / 还原，使绑定源始终收到合法整数。
/// </para>
///
/// <para>三个附加属性：</para>
/// <list type="bullet">
///   <item><description><c>Enabled</c>（bool）：设为 <c>true</c> 时挂上行为；设为 <c>false</c> 时摘除并停止干预。</description></item>
///   <item><description><c>Minimum</c>（int?）：允许的最小值，默认 <c>null</c> 表示不限制；只有显式设为负数时才允许输入前导负号。</description></item>
///   <item><description><c>Maximum</c>（int?）：允许的最大值，默认 <c>null</c> 表示不限制。</description></item>
/// </list>
///
/// <para>四项行为保证：</para>
/// <list type="number">
///   <item><description><c>TextInput</c> 阶段拒绝非数字字符（<c>Minimum &lt; 0</c> 时允许位于开头的一个 <c>-</c>）。</description></item>
///   <item><description>粘贴或程序化写入的文本含非法字符时，在 <c>TextChanged</c> 中清洗，只保留数字与允许的负号；清洗期间用重入标志避免递归。</description></item>
///   <item><description><c>LostFocus</c> 时把数值 clamp 到 <c>[Minimum, Maximum]</c>；文本为空或无法解析时还原为进入编辑前的上一个合法值。</description></item>
///   <item><description>不让含非法字符的文本留在绑定上，<c>TextBox.Text</c> 始终是规范整数文本（编辑过程中允许短暂的空串 / 单独负号，失焦即修正）。</description></item>
/// </list>
///
/// <para>
/// 选型建议：需要上下调值按钮时用 <c>NumericUpDown Classes="intOnly"</c>；需要外观像普通输入框
/// （紧凑单元格、表格行内、与相邻文本框对齐）时用本行为：
/// <c>&lt;TextBox behaviors:NumericInput.Enabled="True" behaviors:NumericInput.Minimum="0" Text="{Binding X}" /&gt;</c>。
/// </para>
/// </summary>
public static class NumericInput
{
    /// <summary>
    /// 附加属性：是否启用整数输入行为。默认 <c>false</c>；设为 <c>true</c> 后立即挂接
    /// <c>TextInput</c> / <c>TextChanged</c> / <c>LostFocus</c> 三个处理器。
    /// </summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(NumericInput));

    /// <summary>
    /// 附加属性：允许的最小值（含）。默认 <c>null</c> 表示不限制；
    /// 显式设为负数时，输入阶段才允许前导负号。
    /// </summary>
    public static readonly AttachedProperty<int?> MinimumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int?>("Minimum", typeof(NumericInput));

    /// <summary>
    /// 附加属性：允许的最大值（含）。默认 <c>null</c> 表示不限制。
    /// </summary>
    public static readonly AttachedProperty<int?> MaximumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int?>("Maximum", typeof(NumericInput));

    /// <summary>
    /// 每个 TextBox 的编辑状态。用 <see cref="ConditionalWeakTable{TKey,TValue}"/> 存放，
    /// 控件被回收时状态随之释放，不会形成静态引用泄漏。
    /// </summary>
    private static readonly ConditionalWeakTable<TextBox, InputState> States = new();

    /// <summary>注册 <c>Enabled</c> 的变更回调；静态构造函数由首次访问该类型时触发。</summary>
    static NumericInput()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>(static (textBox, args) =>
        {
            if (args.GetNewValue<bool>())
            {
                Attach(textBox);
            }
            else
            {
                Detach(textBox);
            }
        });
    }

    /// <summary>获取是否启用整数输入行为。</summary>
    public static bool GetEnabled(TextBox element) => element.GetValue(EnabledProperty);

    /// <summary>设置是否启用整数输入行为。</summary>
    public static void SetEnabled(TextBox element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>获取允许的最小值；<c>null</c> 表示不限制。</summary>
    public static int? GetMinimum(TextBox element) => element.GetValue(MinimumProperty);

    /// <summary>设置允许的最小值；<c>null</c> 表示不限制。</summary>
    public static void SetMinimum(TextBox element, int? value) => element.SetValue(MinimumProperty, value);

    /// <summary>获取允许的最大值；<c>null</c> 表示不限制。</summary>
    public static int? GetMaximum(TextBox element) => element.GetValue(MaximumProperty);

    /// <summary>设置允许的最大值；<c>null</c> 表示不限制。</summary>
    public static void SetMaximum(TextBox element, int? value) => element.SetValue(MaximumProperty, value);

    /// <summary>挂接三个事件处理器，并清洗挂接前已存在的非法文本。</summary>
    private static void Attach(TextBox textBox)
    {
        var state = States.GetOrCreateValue(textBox);

        // 记录挂接瞬间的合法文本，作为首次失焦还原的兜底值
        state.LastValidText = TryParse(textBox.Text, out _) ? Normalize(textBox.Text) : null;

        textBox.AddHandler(
            InputElement.TextInputEvent,
            OnTextInput,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        textBox.TextChanged += OnTextChanged;
        textBox.LostFocus += OnLostFocus;

        var cleaned = Sanitize(textBox.Text, GetMinimum(textBox));
        if (!string.Equals(cleaned, textBox.Text, StringComparison.Ordinal))
        {
            SetText(textBox, cleaned);
        }
    }

    /// <summary>摘除处理器并清除该控件的编辑状态。</summary>
    private static void Detach(TextBox textBox)
    {
        textBox.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        textBox.TextChanged -= OnTextChanged;
        textBox.LostFocus -= OnLostFocus;
        States.Remove(textBox);
    }

    /// <summary>
    /// <c>TextInput</c> 阶段拦截：只放行 ASCII 数字；<c>Minimum &lt; 0</c> 时额外放行
    /// 位于文本开头、且当前文本尚未含负号的单个 <c>-</c>。拦截发生在 TextBox 写入之前
    /// （Tunnel 路由），非法字符不会先进入控件再被删掉。
    /// </summary>
    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox textBox || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (!IsAllowedInput(textBox, e.Text))
        {
            e.Handled = true;
        }
    }

    /// <summary>判断一次 <c>TextInput</c> 的文本是否全部由允许字符组成。</summary>
    private static bool IsAllowedInput(TextBox textBox, string input)
    {
        var allowNegative = GetMinimum(textBox) is < 0;

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (character is >= '0' and <= '9')
            {
                continue;
            }

            if (character == '-' && allowNegative && index == 0)
            {
                // 负号只能出现在开头：光标在文本起始处，且当前文本还没有负号
                var current = textBox.Text ?? string.Empty;
                if (textBox.CaretIndex == 0 && current.IndexOf('-') < 0 && !current.StartsWith('-'))
                {
                    continue;
                }
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// <c>TextChanged</c> 清洗：去掉数字与允许负号之外的字符。若整段文本全是非法字符
    /// （例如粘贴 <c>"abc"</c>），优先还原上一个合法值，避免把空串写进 <c>int</c> 绑定。
    /// 设置文本时用 <see cref="InputState.SuppressTextChanged"/> 防止递归。
    /// </summary>
    private static void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var state = States.GetOrCreateValue(textBox);
        if (state.SuppressTextChanged)
        {
            return;
        }

        var raw = textBox.Text ?? string.Empty;
        var cleaned = Sanitize(raw, GetMinimum(textBox));

        if (!string.Equals(raw, cleaned, StringComparison.Ordinal))
        {
            if (cleaned.Length == 0 && raw.Length > 0 && !string.IsNullOrEmpty(state.LastValidText))
            {
                cleaned = state.LastValidText;
            }

            SetText(textBox, cleaned);
        }

        if (TryParse(cleaned, out _))
        {
            state.LastValidText = cleaned;
        }
    }

    /// <summary>
    /// <c>LostFocus</c> 收口：解析成功则 clamp 到 <c>[Minimum, Maximum]</c> 并回写规范文本；
    /// 空串或解析失败则还原上一个合法值（没有历史值时回退为 0 按边界 clamp）。
    /// </summary>
    private static void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var state = States.GetOrCreateValue(textBox);
        if (state.SuppressTextChanged)
        {
            return;
        }

        var minimum = GetMinimum(textBox);
        var maximum = GetMaximum(textBox);

        if (TryParse(textBox.Text, out var value))
        {
            var result = Clamp(value, minimum, maximum).ToString(CultureInfo.InvariantCulture);
            SetText(textBox, result);
            state.LastValidText = result;
            return;
        }

        var restore = state.LastValidText;
        if (!TryParse(restore, out var restoredValue))
        {
            restoredValue = 0;
        }

        var fallback = Clamp(restoredValue, minimum, maximum).ToString(CultureInfo.InvariantCulture);
        SetText(textBox, fallback);
        state.LastValidText = fallback;
    }

    /// <summary>带重入保护的写文本：置位标志后赋值，内部的 <c>TextChanged</c> 直接返回。</summary>
    private static void SetText(TextBox textBox, string text)
    {
        if (string.Equals(textBox.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        var state = States.GetOrCreateValue(textBox);
        var caret = textBox.CaretIndex;

        state.SuppressTextChanged = true;
        try
        {
            textBox.Text = text;
            textBox.CaretIndex = Math.Clamp(caret, 0, text.Length);
        }
        finally
        {
            state.SuppressTextChanged = false;
        }
    }

    /// <summary>清洗文本：仅保留 ASCII 数字；<paramref name="minimum"/> 为负数时保留一个前导负号。</summary>
    private static string Sanitize(string? text, int? minimum)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var source = text.Trim();
        var allowNegative = minimum is < 0;
        var builder = new StringBuilder(source.Length);
        var index = 0;

        if (allowNegative && source.Length > 0 && source[0] == '-')
        {
            builder.Append('-');
            index = 1;
        }

        for (; index < source.Length; index++)
        {
            var character = source[index];
            if (character is >= '0' and <= '9')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>按不变文化解析整数（清洗后的文本不含空白，行为稳定）。</summary>
    private static bool TryParse(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>把文本规范化为整数文本；无法解析时返回空串（仅用于挂接瞬间的记录）。</summary>
    private static string Normalize(string? text) =>
        TryParse(text, out var value) ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>把数值夹到 nullable 边界内；边界为 <c>null</c> 时该侧不限制。</summary>
    private static int Clamp(int value, int? minimum, int? maximum)
    {
        if (minimum is { } min && value < min)
        {
            value = min;
        }

        if (maximum is { } max && value > max)
        {
            value = max;
        }

        return value;
    }

    /// <summary>单个 TextBox 的编辑状态。</summary>
    private sealed class InputState
    {
        /// <summary>最近一次成功解析为整数的文本，用于失焦时还原。</summary>
        public string? LastValidText { get; set; }

        /// <summary>清洗 / 回写文本期间的重入标志，防止 <c>TextChanged</c> 递归。</summary>
        public bool SuppressTextChanged { get; set; }
    }
}
