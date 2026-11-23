using System;
using System.IO;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 设置文件的测试沙箱：把 <see cref="AppSettings.Load"/> / <see cref="AppSettings.Save"/> 的落点
/// 临时改到一次性目录，用完恢复。
///
/// 为什么要它：<c>AppSettings.SettingsFile</c> 由静态构造决定、指向真实用户目录
/// （<c>%LOCALAPPDATA%\MantisZip\settings.json</c>）。老写法是「读真实文件 → 改 → 写回 → finally 还原」，
/// 中途一旦抛异常就把本机设置带走；而进程级缓存 <c>_cached</c> 意味着同批次里的用例还会互相看到
/// 别人留下的中间态（<c>CompressSettingsViewModelTemplateTests</c> 的构造期断言曾因此偶发拿到空模板）。
/// 沙箱改成「每个用例自己的空目录 + 从头读盘」，既不再碰真实文件，也不再有跨用例残留。
///
/// 用法：<c>using var sandbox = new AppSettingsTestSandbox();</c>
/// 之后本用例内所有 <c>AppSettings.Load()</c> 读到的都是沙箱里的默认值，<c>Save()</c> 也只落到沙箱。
/// </summary>
public sealed class AppSettingsTestSandbox : IDisposable
{
    private readonly string _dir;
    private readonly IDisposable _scope;
    private bool _disposed;

    public AppSettingsTestSandbox()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mz-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SettingsFile = Path.Combine(_dir, "settings.json");
        _scope = AppSettings.UseIsolatedSettingsFileForTests(SettingsFile);
    }

    /// <summary>沙箱里的设置文件路径（不存在 = 还没 Save 过）。</summary>
    public string SettingsFile { get; }

    /// <summary>沙箱目录，测试需要自己的临时文件时可以直接用。</summary>
    public string Root => _dir;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _scope.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败不影响断言 */ }
    }
}
