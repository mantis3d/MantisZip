using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 「插入占位符」按钮：点一下弹出下拉菜单，列出 <see cref="FileNameTemplate.Placeholders"/>
/// 的全部占位符，点其中一项就把该占位符插入 <see cref="TargetTextBox"/> 的光标处
/// （有选区则替换选区）。
/// <para>
/// 光标在弹出前捕获：菜单一打开输入框就失焦，失焦时 <c>CaretIndex</c> 未必可靠，
/// 因此这里在 Tunnel 阶段的 PointerPressed 里先把光标与选区记下来。
/// 从未聚焦过的输入框（例如刚打开对话框直接点按钮）按「插到文本末尾」处理 ——
/// 按 <c>CaretIndex</c> 的 0 会插到最前面，那不是用户期望的「接着写」。
/// </para>
/// <para>
/// 菜单项文案在每次弹出时重建，切换语言后自动跟随。
/// </para>
/// </summary>
public class PlaceholderInsertButton : Button
{
    /// <summary>要插入到哪个输入框。</summary>
    public static readonly StyledProperty<TextBox?> TargetTextBoxProperty =
        AvaloniaProperty.Register<PlaceholderInsertButton, TextBox?>(nameof(TargetTextBox));

    /// <summary>弹出前捕获的光标位置；负数 = 尚未交互过。</summary>
    private int _caret = -1;

    /// <summary>弹出前捕获的选区起点；负数 = 无选区。</summary>
    private int _selectionStart = -1;

    /// <summary>弹出前捕获的选区终点；负数 = 无选区。</summary>
    private int _selectionEnd = -1;

    private MenuFlyout? _flyout;

    /// <summary>要插入到哪个输入框。</summary>
    public TextBox? TargetTextBox
    {
        get => GetValue(TargetTextBoxProperty);
        set => SetValue(TargetTextBoxProperty, value);
    }

    /// <summary>
    /// 派生自 <see cref="Button"/> 但复用 Button 的默认主题。
    /// 不给这个重写时 Avalonia 会按「自身类型」找 ControlTheme，找不到就没有模板 ⇒
    /// 按钮既没有内边距也不可点击（实测：Click 完全不触发，Flyout 也就不出来）。
    /// </summary>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 菜单项文案依赖当前语言：每次弹出前重建一次（切换语言后无需重新挂载）。
        // 打开本身由 Button 内建逻辑负责（OnClick → OpenFlyout），这里不覆盖 Flyout 以外的行为。
        _flyout = new MenuFlyout();
        Flyout = _flyout;
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(PointerPressedEvent, OnPreviewPointerPressed);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>在菜单弹出前记下光标与选区（失焦后可能不再可信）。</summary>
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TargetTextBox is not { } textBox) return;

        // 只有输入框当前真的持有焦点时，CaretIndex 才是用户看到的位置。
        // 从未聚焦过的输入框 CaretIndex 恒为 0，照它插入会插到最前面，
        // 不是用户期望的「接着写」⇒ 记 -1，由 ResolveInsertIndex 兜底到末尾。
        if (!textBox.IsFocused)
        {
            _caret = -1;
            _selectionStart = -1;
            _selectionEnd = -1;
            return;
        }

        _caret = textBox.CaretIndex;
        _selectionStart = textBox.SelectionStart;
        _selectionEnd = textBox.SelectionEnd;
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        // 必须先把菜单项建好再交给基类：基类的 OnClick 会打开 Flyout，
        // 而弹层是按「打开那一刻的 Items」测尺寸的 —— 在 Opening 事件里补条目太晚，
        // 真机上会弹出一个 2×32 的空条（实测）。
        RebuildItems();
        base.OnClick();
    }

    /// <summary>按当前语言重建菜单项：每项 = 占位符本身 + 一句说明。</summary>
    private void RebuildItems()
    {
        if (_flyout is null) return;
        _flyout.Items.Clear();
        foreach (var token in FileNameTemplate.Placeholders)
        {
            var item = new MenuItem
            {
                Header = token + "  —  " + DescribeToken(token),
            };
            item.Click += (_, _) => InsertToken(token);
            _flyout.Items.Add(item);
        }
    }

    /// <summary>占位符的一句话说明（走本地化；未知 token 只显示自身）。</summary>
    private static string DescribeToken(string token) => token switch
    {
        "{name}" => LocalizationManager.T("Compress_FileNameTemplate_Token_Name"),
        "{name:auto}" => LocalizationManager.T("Compress_FileNameTemplate_Token_NameAuto"),
        "{ext}" => LocalizationManager.T("Compress_FileNameTemplate_Token_Ext"),
        "{parent}" => LocalizationManager.T("Compress_FileNameTemplate_Token_Parent"),
        "{date}" => LocalizationManager.T("Compress_FileNameTemplate_Token_Date"),
        "{datetime}" => LocalizationManager.T("Compress_FileNameTemplate_Token_DateTime"),
        "{seq}" => LocalizationManager.T("Compress_FileNameTemplate_Token_Seq"),
        "{seq:0000}" => LocalizationManager.T("Compress_FileNameTemplate_Token_SeqWidth"),
        _ => string.Empty,
    };

    /// <summary>把占位符插进输入框，光标落在占位符之后。</summary>
    private void InsertToken(string token)
    {
        if (TargetTextBox is not { } textBox) return;

        // 有捕获到的选区就替换它；没有则插在捕获到的光标处（从未交互过 → 文本末尾）
        var insertIndex = PlaceholderInsertion.ResolveInsertIndex(textBox.Text, _caret);
        var selectionStart = _selectionStart >= 0 ? _selectionStart : insertIndex;
        var selectionEnd = _selectionEnd >= 0 ? _selectionEnd : insertIndex;

        textBox.Text = PlaceholderInsertion.Insert(
            textBox.Text, selectionStart, selectionEnd, token, out var caretIndex);
        textBox.CaretIndex = caretIndex;
        textBox.Focus();
    }
}
