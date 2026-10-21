using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 全局快捷键回归测试（2026-10-21）。
///
/// 背景：Avalonia 的 <c>MenuItem.InputGesture</c> **只显示**快捷键文字、不处理按键
/// （官方文档：Setting this property does not cause the input gesture to be handled by the
/// menu item, it simply displays the gesture text in the menu）→ 真正生效的绑定必须登记在
/// <c>KeyBindings</c>。本测试锁定「常用快捷键已登记」这一事实，防止回退成只显示不生效。
/// </summary>
public class MainWindowKeyBindingsTests
{
    [AvaloniaFact]
    public void MainWindow_Registers_Expected_KeyBindings()
    {
        var win = new MainWindow();

        var gestures = win.KeyBindings.OfType<KeyBinding>()
            .Select(k => k.Gesture?.ToString())
            .Where(g => g != null)
            .ToList();

        // 全部登记的快捷键（命令 → 手势）
        string[] expected =
        {
            "Ctrl+O",       // 打开压缩包
            "Ctrl+N",       // 新建压缩包
            "Ctrl+W",       // 关闭压缩包
            "F5",           // 刷新
            "Ctrl+Shift+L", // 打开压缩包所在目录
            "Ctrl+E",       // 解压对话框
            "Ctrl+Shift+E", // 原地解压
            "Ctrl+Shift+T", // 解压到同名文件夹
            "Ctrl+T",       // 测试压缩包
            "Ctrl+Shift+A", // 添加文件
            "Ctrl+P",       // 显示/隐藏预览面板
            "Ctrl+Shift+V", // 循环切换文件列表视图
            "Ctrl+Shift+I", // 显示/隐藏信息面板
            "Ctrl+Shift+C", // 复制条目完整路径
            "Ctrl+OemComma", // 设置（Ctrl+,）
        };

        foreach (var g in expected)
        {
            Assert.Contains(g, gestures);
        }

        // 每个绑定都必须挂上真实命令（否则按下无反应）
        Assert.All(win.KeyBindings.OfType<KeyBinding>(), kb => Assert.NotNull(kb.Command));
    }

    [AvaloniaFact]
    public void MainWindow_KeyBindings_Are_Not_Duplicated()
    {
        var win = new MainWindow();

        var gestures = win.KeyBindings.OfType<KeyBinding>()
            .Select(k => k.Gesture?.ToString())
            .ToList();

        Assert.Equal(gestures.Count, gestures.Distinct().Count());

        win.Close();
    }
}
