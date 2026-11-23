using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 命名预设选择器（可复用控件）：一个可编辑下拉框 + 两个纯图标按钮。
/// <para>
/// 下拉框里装的是<b>预设名</b>：既能从列表点选已有预设，也能直接打一个新名字。
/// 「保存」「删除」都只把框里的名字交回宿主（<see cref="SaveRequested"/> / <see cref="DeleteRequested"/>），
/// <b>控件不碰 <c>AppSettings</c>、也不碰模板文本</b> —— 写哪份设置、存什么模板由宿主决定。
/// </para>
/// <para>
/// 控件<b>从不回写绑定属性</b>：预设名由 <c>ComboBox.Text</c> 双向绑定到宿主的
/// <see cref="PresetName"/>，用户在框里打字或点选时由控件自己把文本推上去。
/// 「选了某条预设 → 把模板填进上方输入框」通过 <see cref="PresetSelected"/> 交给宿主处理。
/// </para>
/// <para>
/// 选预设只在「下拉打开期间选中项真的换了一条」时通知宿主：<c>ComboBox</c> 在 <c>TextChanged</c>
/// 里按名字<b>精确</b>匹配选中项，用户打字打出一个与已有预设一模一样的名字也会匹配上，
/// 但那种情况不能把模板框顶成该预设的旧内容（用户在编辑模板，不是选预设）。
/// </para>
/// </summary>
public partial class TemplatePresetPicker : UserControl
{
    /// <summary>可供选择的预设集合（下拉项）。为空时下拉仍可打字，只是没有可选项。</summary>
    public static readonly StyledProperty<IEnumerable<FileNameTemplatePreset>?> PresetsProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, IEnumerable<FileNameTemplatePreset>?>(nameof(Presets));

    /// <summary>框里的预设名（双向；权威值就是框文本，<c>SelectedItem</c> 只是它的镜像）。</summary>
    public static readonly StyledProperty<string?> PresetNameProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, string?>(
            nameof(PresetName), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>水印（「预设名称」提示）的本地化 key；留空 = 不显示水印。</summary>
    public static readonly StyledProperty<string?> WatermarkKeyProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, string?>(nameof(WatermarkKey));

    /// <summary>「保存」按钮提示文案的本地化 key。</summary>
    public static readonly StyledProperty<string?> SaveToolTipKeyProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, string?>(
            nameof(SaveToolTipKey), "Compress_FileNameTemplate_SavePreset");

    /// <summary>「删除」按钮提示文案的本地化 key。</summary>
    public static readonly StyledProperty<string?> DeleteToolTipKeyProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, string?>(
            nameof(DeleteToolTipKey), "Compress_FileNameTemplate_DeletePreset");

    /// <summary>
    /// 操作结果消息（已保存 / 已更新 / 超过上限）。null = 不显示气泡。
    /// <para>
    /// 消息走<b>浮层气泡</b>（Popup），脱离布局流——出现和消失都不会把下面的行顶下去。
    /// 成功消息 <see cref="SuccessMessageDuration"/> 后自动收起并把属性清空；失败消息常驻，
    /// 直到宿主清空它或用户改动模板（宿主在模板变更时自己清）。
    /// </para>
    /// </summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, string?>(nameof(Message));

    /// <summary>消息是不是一条失败提示（决定气泡图标：红警告三角 vs 绿对勾）。</summary>
    public static readonly StyledProperty<bool> IsMessageErrorProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, bool>(nameof(IsMessageError));

    /// <summary>成功消息在气泡里停留的时长（毫秒）。失败消息不受此限制。</summary>
    public static readonly StyledProperty<TimeSpan> SuccessMessageDurationProperty =
        AvaloniaProperty.Register<TemplatePresetPicker, TimeSpan>(
            nameof(SuccessMessageDuration), TimeSpan.FromMilliseconds(1600));

    /// <summary>下拉打开那一刻的选中项，用来判断「打开期间是否真的换了一条预设」。</summary>
    private object? _selectionAtOpen;

    /// <summary>
    /// 事件与本地化订阅是否已挂上。控件会在「移出可视树」时退订（<see cref="OnDetachedFromVisualTree"/>），
    /// 所以必须记状态并在 <see cref="OnAttachedToVisualTree"/> 里补挂 —— 设置窗三行在 TabItem 里，
    /// 切标签页就会经历「分离 → 重新附着」，漏掉重挂会让选中预设从此不回填模板（静默失效）。
    /// </summary>
    private bool _subscribed;

    /// <summary>
    /// 预设集合的变更通知源（宿主给的通常是 <c>ObservableCollection</c>）。
    /// 「删除」按钮的可点状态取决于「框里的名字现在还存不存在」，而删预设时宿主的集合是
    /// 就地把项清掉、<c>Presets</c> 属性引用**不变** —— 不听集合事件，按钮会一直停在可点状态，
    /// 点下去宿主找不到同名项、默默返回，又变成「界面正常、点了没反应」。
    /// </summary>
    private System.Collections.Specialized.INotifyCollectionChanged? _presetsNotifier;

    /// <summary>成功消息的自动收起计时器（失败消息不启动；控件分离时停掉防泄漏）。</summary>
    private DispatcherTimer? _messageTimer;

    /// <summary>用户从下拉里点中了一条已有预设（宿主据此把模板填进上方输入框）。</summary>
    public event EventHandler<FileNameTemplatePreset>? PresetSelected;

    /// <summary>点了「保存」→ 宿主用（框里的名字 + 当前模板）写设置（参数 = 名字，可能是空串）。</summary>
    public event EventHandler<string>? SaveRequested;

    /// <summary>点了「删除」→ 宿主删掉该名字对应的预设（参数 = 框里的名字）。</summary>
    public event EventHandler<string>? DeleteRequested;

    /// <summary>
    /// 成功消息自动收起后触发：宿主应当把自己的消息属性清成 null。
    /// <para>
    /// 由宿主清而不是控件自己写回 <see cref="MessageProperty"/>：那是单向绑定的目标，
    /// 控件直接改会破坏绑定链；交给宿主清，消息属性与气泡状态永远一致。
    /// 清成 null 还有个必须性——宿主下次设同一个字符串时值没变，变更通知不发，气泡就不会再出现。
    /// </para>
    /// </summary>
    public event EventHandler? MessageDismissed;

    public TemplatePresetPicker()
    {
        InitializeComponent();

        Subscribe();

        ApplyLocalizedText();
        SyncDeleteEnabled();
    }

    public IEnumerable<FileNameTemplatePreset>? Presets
    {
        get => GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    public string? PresetName
    {
        get => GetValue(PresetNameProperty);
        set => SetValue(PresetNameProperty, value);
    }

    public string? WatermarkKey
    {
        get => GetValue(WatermarkKeyProperty);
        set => SetValue(WatermarkKeyProperty, value);
    }

    public string? SaveToolTipKey
    {
        get => GetValue(SaveToolTipKeyProperty);
        set => SetValue(SaveToolTipKeyProperty, value);
    }

    public string? DeleteToolTipKey
    {
        get => GetValue(DeleteToolTipKeyProperty);
        set => SetValue(DeleteToolTipKeyProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsMessageError
    {
        get => GetValue(IsMessageErrorProperty);
        set => SetValue(IsMessageErrorProperty, value);
    }

    public TimeSpan SuccessMessageDuration
    {
        get => GetValue(SuccessMessageDurationProperty);
        set => SetValue(SuccessMessageDurationProperty, value);
    }

    /// <summary>结果气泡当前是否弹出（只读，供宿主与回归测试观察）。</summary>
    public bool IsMessagePopupOpen => MessagePopup.IsOpen;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 控件自身既是绑定源又是绑定目标：写在 XAML 里要绕 RelativeSource，方向容易看反，改代码绑定。
        NameBox.Bind(ComboBox.ItemsSourceProperty, new Binding(nameof(Presets)) { Source = this });
        NameBox.Bind(ComboBox.TextProperty,
            new Binding(nameof(PresetName)) { Source = this, Mode = BindingMode.TwoWay });
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        SyncDeleteEnabled();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unsubscribe();
        StopMessageTimer();
        MessagePopup.IsOpen = false;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>挂事件与本地化订阅（幂等：重复附着不会叠加 handler）。</summary>
    private void Subscribe()
    {
        if (_subscribed) return;

        NameBox.DropDownOpened += OnDropDownOpened;
        NameBox.DropDownClosed += OnDropDownClosed;
        LocalizationManager.CultureChanged += OnCultureChanged;
        // 分离时集合订阅被拆掉了，重新附着要按当前值补回来（属性本身没变，OnPropertyChanged 不会再来一次）
        TrackPresetsCollection(null, Presets);
        _subscribed = true;
    }

    /// <summary>退订全部外部事件（避免分离后仍被本地化刷新或下拉通知命中）。</summary>
    private void Unsubscribe()
    {
        if (!_subscribed) return;

        NameBox.DropDownOpened -= OnDropDownOpened;
        NameBox.DropDownClosed -= OnDropDownClosed;
        LocalizationManager.CultureChanged -= OnCultureChanged;
        if (_presetsNotifier is not null)
        {
            _presetsNotifier.CollectionChanged -= OnPresetsCollectionChanged;
            _presetsNotifier = null;
        }
        _subscribed = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PresetNameProperty)
        {
            SyncDeleteEnabled();
        }
        else if (change.Property == PresetsProperty)
        {
            TrackPresetsCollection(change.GetOldValue<IEnumerable<FileNameTemplatePreset>?>(),
                change.GetNewValue<IEnumerable<FileNameTemplatePreset>?>());
            SyncDeleteEnabled();
        }
        else if (change.Property == WatermarkKeyProperty)
        {
            NameBox.PlaceholderText = WatermarkKey is { Length: > 0 } key
                ? LocalizationManager.T(key)
                : null;
        }
        else if (change.Property == SaveToolTipKeyProperty)
        {
            ToolTip.SetTip(SaveButton, SaveToolTipKey is { Length: > 0 } k ? LocalizationManager.T(k) : null);
        }
        else if (change.Property == DeleteToolTipKeyProperty)
        {
            ToolTip.SetTip(DeleteButton, DeleteToolTipKey is { Length: > 0 } k ? LocalizationManager.T(k) : null);
        }
        else if (change.Property == MessageProperty)
        {
            SyncMessagePopup(change.GetNewValue<string?>());
        }
        else if (change.Property == IsMessageErrorProperty)
        {
            // 宿主的赋值顺序是「先 Message、再 IsMessageError」（CompressSettingsViewModel.SaveTemplatePreset
            // 与 SettingsWindowViewModel.SetPresetMessage 都是这样），所以设消息那一刻标志还是旧值：
            // 失败消息会被当成成功消息起一个 1.6s 计时器，标志随后变成 true 却没人停表 ——
            // 用户看到「预设最多 20 条」的红色警告闪一下就没了。标志变化必须重新走一遍同步，
            // 让计时器按最新的 IsMessageError 重新判定（错误 → 不起表）。
            SyncMessagePopup(Message);
        }
    }

    /// <summary>
    /// 消息变化 → 弹/收气泡。成功消息按时长自动收起；失败消息常驻，等宿主清空。
    /// <para>
    /// 气泡是 <c>Popup</c>（脱离布局流）：它出现时不会让下面的「文件名」行下移，
    /// 也不会改变控件自身高度——这是 2026-11-21 用户明确要求的效果。
    /// </para>
    /// </summary>
    private void SyncMessagePopup(string? message)
    {
        StopMessageTimer();

        if (string.IsNullOrWhiteSpace(message))
        {
            MessageText.Text = null;
            MessagePopup.IsOpen = false;
            return;
        }

        MessageText.Text = message;
        UpdateMessageIcon();
        MessagePopup.IsOpen = true;

        // 失败消息常驻：用户必须读到「为什么存不上」（超上限 / 模板为空）。
        if (IsMessageError) return;

        var timer = new DispatcherTimer { Interval = SuccessMessageDuration };
        timer.Tick += (_, _) => DismissMessageNow();
        timer.Start();
        _messageTimer = timer;
    }

    /// <summary>
    /// 立刻收起气泡并通知宿主清空消息。
    /// <para>
    /// 生产路径由成功消息的 <see cref="DispatcherTimer"/> 到期时调用；无头测试里调度器定时器不会自行到期，
    /// 回归测试直接调本方法验证「收起 + 回调宿主」这半个行为（1.6s 的计时本身由人工验证）。
    /// </para>
    /// </summary>
    internal void DismissMessageNow()
    {
        StopMessageTimer();
        MessagePopup.IsOpen = false;
        MessageText.Text = null;

        // 成功消息是瞬时的：收起后通知宿主清空消息属性 ——
        // 否则宿主下次设同一个字符串（值没变）不会触发变更通知，气泡就再也不出来了。
        MessageDismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按 <see cref="IsMessageError"/> 切换气泡图标：失败 = 红警告三角，成功 = 绿对勾。</summary>
    private void UpdateMessageIcon()
    {
        var hasMessage = !string.IsNullOrWhiteSpace(Message);
        MessageErrorIcon.IsVisible = hasMessage && IsMessageError;
        MessageSuccessIcon.IsVisible = hasMessage && !IsMessageError;
    }

    private void StopMessageTimer()
    {
        _messageTimer?.Stop();
        _messageTimer = null;
    }

    /// <summary>切换预设集合时迁移集合变更订阅（旧集合退订、新集合订阅）。</summary>
    private void TrackPresetsCollection(IEnumerable<FileNameTemplatePreset>? oldValue,
        IEnumerable<FileNameTemplatePreset>? newValue)
    {
        if (_presetsNotifier is not null)
        {
            _presetsNotifier.CollectionChanged -= OnPresetsCollectionChanged;
            _presetsNotifier = null;
        }

        if (newValue is System.Collections.Specialized.INotifyCollectionChanged notifier)
        {
            notifier.CollectionChanged += OnPresetsCollectionChanged;
            _presetsNotifier = notifier;
        }
    }

    private void OnPresetsCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => SyncDeleteEnabled();

    private void OnDropDownOpened(object? sender, EventArgs e)
        => _selectionAtOpen = NameBox.SelectedItem;

    /// <summary>下拉关闭：只有「打开期间选中项真的换成了另一条预设」才通知宿主回填模板。</summary>
    private void OnDropDownClosed(object? sender, EventArgs e)
    {
        var selected = NameBox.SelectedItem;
        var changedWhileOpen = !ReferenceEquals(selected, _selectionAtOpen);
        _selectionAtOpen = null;

        if (!changedWhileOpen) return;
        if (selected is not FileNameTemplatePreset preset) return;
        if (string.IsNullOrEmpty(preset.Template)) return;

        PresetSelected?.Invoke(this, preset);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
        => SaveRequested?.Invoke(this, PresetName ?? string.Empty);

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var name = PresetName?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        DeleteRequested?.Invoke(this, name);
    }

    /// <summary>
    /// 「删除」只在框里的名字确实对应一条预设时可点：新起的名字、刚删掉的那条都保持禁用，
    /// 否则用户点下去没有任何反馈，看起来像坏键。
    /// </summary>
    private void SyncDeleteEnabled()
    {
        var name = PresetName?.Trim();
        DeleteButton.IsEnabled = !string.IsNullOrEmpty(name)
            && (Presets?.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? false);
    }

    private void OnCultureChanged(object? sender, EventArgs e) => ApplyLocalizedText();

    private void ApplyLocalizedText()
    {
        NameBox.PlaceholderText = WatermarkKey is { Length: > 0 } watermarkKey
            ? LocalizationManager.T(watermarkKey)
            : null;
        ToolTip.SetTip(SaveButton, SaveToolTipKey is { Length: > 0 } saveKey
            ? LocalizationManager.T(saveKey)
            : null);
        ToolTip.SetTip(DeleteButton, DeleteToolTipKey is { Length: > 0 } deleteKey
            ? LocalizationManager.T(deleteKey)
            : null);
    }
}
