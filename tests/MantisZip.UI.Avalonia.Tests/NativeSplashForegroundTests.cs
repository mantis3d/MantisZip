using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// NativeSplash.Hook 置前修复（startup-native-splash 计划「主窗口被糊住」）回归测试（无头 Avalonia）：
/// Opened 触发 EnsureForeground + Hide 的组合链路不得抛异常（headless 下平台句柄缺失路径 must no-op）。
/// Win32 SetForegroundWindow 行为本身无法在无头环境断言，GUI 取证见 detail 条目（isfg=True 探针）。
/// </summary>
public class NativeSplashForegroundTests
{
    [AvaloniaFact]
    public void Hook_Opened_RunsEnsureForegroundThenHide_WithoutException()
    {
        var window = new Window();
        NativeSplash.Hook(window);

        // Opened 由 Show() 触发：EnsureForeground（headless 无真实 hwnd，应静默 no-op）+ Hide（幂等）
        window.Show();

        // Opened 已触发且未抛异常即通过；拆窗清理
        window.Close();
    }
}
