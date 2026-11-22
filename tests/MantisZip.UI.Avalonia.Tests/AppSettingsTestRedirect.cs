using System;
using System.IO;
using System.Runtime.CompilerServices;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 测试程序集级安全网：把 <see cref="AppSettings.Load"/> / <see cref="AppSettings.Save"/> 的落点
/// 在整个测试程序集生命周期内重定向到一次性临时目录。
///
/// 为什么要这一层（2026-11-22 实测事故）：UI 测试里有十几处「只 new 一个窗口/VM 就会在关闭时落盘」
/// 的用例（<c>CompressSettingsWindow.PersistGeometry</c> / <c>ExtractSettingsWindow.PersistGeometry</c> /
/// <c>ProgressWindow.OnClosed</c> / <c>MainWindowViewModel.SaveSetting</c> …），它们都没有用
/// <see cref="AppSettingsTestSandbox"/>。跑一次全量 ui-tests，<c>SaveTo</c> 一共被调用 79 次，
/// 其中 42 次直接落到真实的 <c>%LOCALAPPDATA%\MantisZip\settings.json</c>，
/// 而其中 35 次写的是「模板空、预设空」的裸实例 —— 用户保存的命名预设就是这样被测试冲掉的。
///
/// 所以单靠「谁需要就自己 using 一个沙箱」不够：**漏掉的用例必须失败在明处，而不是悄悄改写用户磁盘**。
/// 这一层保证「漏了沙箱」最多只污染本进程的临时目录；需要精确控制设置内容的用例仍应自己
/// <c>using var sandbox = new AppSettingsTestSandbox();</c>（它会在这一层之上再重定向、退出时还原）。
///
/// 与 <c>TestAssemblySetup</c>（编码注册）分开放：那个是「测试宿主缺的运行时前提」，
/// 这个是「测试不许碰用户数据」的不变量，出问题时该改哪个一目了然。
/// </summary>
internal static class AppSettingsTestRedirect
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mz-ui-tests-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        SandboxDir = dir;

        // 句柄刻意不释放：随进程结束一起消失，避免「某个测试先跑完就提前解除重定向」
        AppSettings.UseIsolatedSettingsFileForTests(Path.Combine(dir, "settings.json"));

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 进程退出阶段清理失败无影响 */ }
        };
    }

    /// <summary>本程序集的临时设置目录（调试用：跑测试时想确认写了哪里，看这个路径）。</summary>
    internal static string SandboxDir { get; private set; } = "";
}
