using CommunityToolkit.Mvvm.ComponentModel;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 参数区中的单个参数项：只描述「标签 + 值 + 可用性」，**不含任何业务语义**。
/// </summary>
/// <remarks>
/// 渲染层（<c>CustomFilePickerDialog.axaml</c> 的 <c>OptionsItemsControl</c>）只遍历参数注册表
/// 生成「标签 + 控件」，不认识任何具体 key——因此新增参数只需在对话框里注册一项，
/// 无需改动渲染层与布局。
/// <para>
/// 派生 <see cref="ObservableObject"/> 而非普通类：当前只有 CheckBox 双向回推
/// <see cref="IsChecked"/>，无需 PropertyChanged 也能工作；但若将来参数需要相互联动
/// （如「全选」批量改其它项），INPC 是唯一无需再改造的路径。
/// </para>
/// </remarks>
public sealed class PickerOptionItem : ObservableObject
{
    /// <summary>参数键，同时作为返回通道的取值键（如 <c>"preserveFullPath"</c>）。</summary>
    public required string Key { get; init; }

    /// <summary>显示标签（构造时已由 i18n key 求值为当前语言文案）。</summary>
    public required string Label { get; init; }

    /// <summary>
    /// 禁用时显示的提示文案；<c>null</c> = 不提示。
    /// </summary>
    /// <remarks>
    /// 呈现方式：绑定到 CheckBox 的 <c>ToolTip.Tip</c>，并置 <c>ToolTip.ShowOnDisabled="True"</c>
    /// —— Avalonia 中禁用控件不派发指针事件，ToolTip 默认永不弹出；该属性（本项目 Avalonia 12.0.4
    /// 已提供）是让禁用态提示可见的前提。值为 null 时提示服务不打开，故「可用时不显示提示」
    /// 天然满足，无需额外的 <c>IsVisible</c> 切换逻辑。
    /// </remarks>
    public string? DisabledHint { get; init; }

    private bool _isChecked;

    /// <summary>当前勾选值。</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    private bool _isEnabled = true;

    /// <summary>
    /// 当前可用性。为 <c>false</c> 时控件禁用且 <see cref="DisabledHint"/> 生效。
    /// </summary>
    /// <remarks>
    /// 本次改动中可用性是<b>静态</b>的（取决于构造期固定的 <c>currentFolder</c>），
    /// 仅在注册时求值一次。若将来需要动态刷新，直接改本属性即可（INPC 已就位），
    /// 无需给对话框加 INPC。
    /// </remarks>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}