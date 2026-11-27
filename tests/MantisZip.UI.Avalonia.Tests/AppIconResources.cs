using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 把 <c>Resources/Icons/AppIcons.axaml</c>（<c>Geometry</c> 图标字典）临时并进应用级资源，
/// 用完移除。
///
/// 为什么要它：无头测试宿主只有一个裸 <see cref="Application"/>，<c>App.axaml</c> 里的
/// <c>ResourceInclude</c> 都不生效。于是 <c>this.FindResource("IconEye")</c> 返回
/// <see cref="AvaloniaProperty.UnsetValue"/>，而调用处是 <c>(Geometry?)…</c> 的强转 ——
/// 直接 <c>InvalidCastException</c>（2026-11-27 实测：构造 <c>PasswordDialog</c> 即抛，
/// 堆栈停在 <c>ApplyRevealState</c>）。这不是产品缺陷，而是「测试宿主缺少应用资源」。
///
/// 用法：<c>using var icons = new AppIconResources();</c>
/// </summary>
public sealed class AppIconResources : IDisposable
{
    private readonly ResourceInclude _include;
    private readonly bool _added;
    private bool _disposed;

    public AppIconResources()
    {
        var app = Application.Current;
        if (app is null) return;

        _include = new ResourceInclude(new Uri(AppIconsUri))
        {
            Source = new Uri(AppIconsUri),
        };
        app.Resources.MergedDictionaries.Add(_include);
        _added = true;
    }

    private const string AppIconsUri = "avares://MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            Application.Current?.Resources.MergedDictionaries.Remove(_include);
        }
    }
}
