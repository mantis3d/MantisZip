using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 原生 Win32 启动 Splash（startup-native-splash 计划）。
/// 覆盖 Program.Main → Avalonia 终端窗口出现之间的冷启动空窗（Avalonia 未就绪，任何 Avalonia 反馈都不可用）。
/// 独立后台线程 + 全独立消息泵，零 Avalonia 依赖；Hide() 幂等、线程安全（在 UI 线程安全调用，不阻塞 UI——
/// 最短显示时长的等待发生在 splash 消息泵线程内）。
/// </summary>
internal static class NativeSplash
{
    /// <summary>Win32 窗口过程委托类型。</summary>
    private delegate nint WndProcHandler(nint hwnd, uint uMsg, nint wParam, nint lParam);

    // WndProc 委托保存在静态字段防止被 GC 回收
    private static readonly WndProcHandler WndProcDelegate = WndProc;

    // ── 状态字段 ──

    private static readonly object Sync = new();
    private static Thread? _thread;
    private static nint _hwnd;
    private static nint _hIcon;
    private static string _text = string.Empty;
    private static bool _dark;
    private static int _paintedAt;
    private static bool _closePending;

    // 最短显示时长（防冷启动已预热时 splash 闪一下即关）
    private const int MinShowMs = 300;
    private const string ClassName = "MantisZipNativeSplash";

    /// <summary>
    /// 显示启动 Splash（Program.Main 里、Avalonia 初始化前调用）。
    /// 初始化失败静默降级（不影响主流程），文案由调用方按 AppSettings 语言 + 启动方式选 key 得到。
    /// </summary>
    /// <param name="text">已本地化的文案（如「正在启动…」）</param>
    /// <param name="dark">深色与否（据 AppSettings.Theme；System 视为深色）</param>
    internal static void Show(string text, bool dark)
    {
        if (!OperatingSystem.IsWindows()) return; // 跨平台安全护栏：非 Windows 直接跳过
        lock (Sync)
        {
            if (_thread is not null) return; // 已显示，幂等
            _text = text;
            _dark = dark;
            _closePending = false;
            _thread = new Thread(ThreadProc)
            {
                IsBackground = true, // 保证 desktop.Shutdown 后不阻塞进程退出
                Name = "NativeSplash",
            };
            _thread.Start();
        }
    }

    /// <summary>
    /// 关闭 splash（幂等）。PostMessage(WM_CLOSE) 是异步投递，实际销毁发生在 splash 消息泵线程；
    /// 若 splash 尚未显示完成，_closePending 保证窗口不悬置。最短显示时长在 splash 线程里等待。
    /// </summary>
    internal static void Hide()
    {
        lock (Sync)
        {
            if (_thread is null) return; // 从未显示或已关闭，幂等
            _closePending = true;
        }
        var hwnd = _hwnd;
        if (hwnd != nint.Zero)
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, nint.Zero, nint.Zero);
    }

    /// <summary>
    /// 给任意 Avalonia 窗口挂 Opened → Hide() 钩子（终端窗口枚举见计划文档）。
    /// 运行期在已关闭的 splash 上调用是 no-op。
    /// </summary>
    internal static void Hook(Window window)
    {
        window.Opened += (_, _) =>
        {
            EnsureForeground(window); // splash 修复：Avalonia Show() 不激活，NOACTIVATE splash 吞掉前台后主窗被糊住
            Hide();
        };
    }

    /// <summary>
    /// 终端窗口显示后强制置前（startup-native-splash 计划「主窗口被糊住」修复）。
    /// 背景机制：Windows 把「进程启动后第一个可见窗口」的前台激活机会给了 splash，但 splash
    /// 是 WS_EX_NOACTIVATE 不接激活；splash 销毁时系统把前台回落给既有前台窗口，
    /// Avalonia 的 Show() 又不主动 SetForegroundWindow——主窗口因此留在 z-order 后层。
    /// 兼容手法：AttachThreadInput 挂到前台线程解锁前台转换权限 → SetForegroundWindow（官方
    /// 认可的跨进程激活 HACK，浏览器类应用通例）。
    /// </summary>
    private static void EnsureForeground(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero) return;

            var fg = NativeMethods.GetForegroundWindow();
            if (fg == hwnd) return; // 已经在前台（首帧激活成功的场景），no-op

            var fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
            var me = NativeMethods.GetCurrentThreadId();
            var attached = false;
            try
            {
                if (fg != nint.Zero && fgThread != nint.Zero && fgThread != me)
                    attached = NativeMethods.AttachThreadInput(fgThread, me, true);
                NativeMethods.BringWindowToTop(hwnd);
                if (!NativeMethods.SetForegroundWindow(hwnd))
                    App.DebugLog($"EnsureForeground: SetForegroundWindow failed (fgThread={fgThread})");
            }
            finally
            {
                if (attached)
                    NativeMethods.AttachThreadInput(fgThread, me, false);
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"EnsureForeground crashed: {ex.Message}"); // 置前失败不影响启动流程
        }
    }

    // ── 消息泵线程 ──

    private static void ThreadProc()
    {
        nint hwnd = nint.Zero;
        try
        {
            // DPI 感知：Avalonia UsePlatformDetect 之后会再次调用（失败无害，仅首个生效生效）
            try { NativeMethods.SetProcessDpiAwarenessContext(new nint(-4)); } catch { }

            var dpi = NativeMethods.GetDpiForSystem();
            if (dpi <= 0) dpi = 96;
            var scale = dpi / 96.0;

            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
                hInstance = NativeMethods.GetModuleHandle(null),
                hCursor = NativeMethods.LoadCursor(nint.Zero, new nint((long)NativeMethods.IDC_ARROW)),
                hbrBackground = nint.Zero,
                lpszMenuName = nint.Zero,
                lpszClassName = Marshal.StringToHGlobalUni(ClassName),
            };
            try
            {
                RegisterAndLog(ref wc);
            }
            finally
            {
                Marshal.FreeHGlobal(wc.lpszClassName);
            }

            int w = (int)(360 * scale);
            int h = (int)(108 * scale);
            int x = (NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN) - w) / 2;
            int y = (NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN) - h) / 2;

            const uint exStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
            const uint style = NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE;

            // 应用图标（一次性从运行中的 exe 提取，沿用 <ApplicationIcon> 嵌入资源；失败则 Paint 退化为纯文本）
            // 必须在创建窗口之前完成——首帧 WM_PAINT 在 CreateWindowEx(WS_VISIBLE)+UpdateWindow 时同步发出，
            // 若此处 _hIcon 仍为 0 则那一帧就是纯文本，且之后不会再有重绘机会
            try
            {
                var exePath = Environment.ProcessPath;
                SendSplashLog($"icon path={(exePath?.Length <= 40 ? exePath : "…(" + exePath?.Length + ")")}");
                if (!string.IsNullOrEmpty(exePath))
                {
                    var big = new nint[1];
                    var small = new nint[1];
                    var n = NativeMethods.ExtractIconEx(exePath, 0, big, small, 1);
                    SendSplashLog($"ExtractIconEx n={n} big=0x{big[0]:x} small=0x{small[0]:x}");
                    if (n > 0 && big[0] != nint.Zero)
                        _hIcon = big[0];
                }
            }
            catch (Exception ex)
            {
                SendSplashLog($"icon extract crashed: {ex.Message}");
            }

            var pClassName = Marshal.StringToHGlobalUni(ClassName);
            hwnd = NativeMethods.CreateWindowEx(
                exStyle, pClassName, nint.Zero, style,
                x, y, w, h,
                nint.Zero, nint.Zero, NativeMethods.GetModuleHandle(null), nint.Zero);
            Marshal.FreeHGlobal(pClassName); // 创建完成后指针可释放（类由 OS 持有）
            if (hwnd == nint.Zero)
            {
                SendSplashLog($"CreateWindowEx failed (lastErr={Marshal.GetLastWin32Error()})");
                return; // 创建失败 → 静默降级（无 splash）
            }
            SendSplashLog($"splash visible hwnd=0x{hwnd:x}");
            _hwnd = hwnd;
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE); // 不抢焦点
            NativeMethods.UpdateWindow(hwnd);

            // Win11 圆角（DWMWCP_ROUND；Win10 无此属性 → 失败静默忽略，回退直角）
            try
            {
                var pref = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { }

            if (_closePending) // Hide() 在窗口显示前就调用的抖动窗口场景
            {
                NativeMethods.DestroyWindow(hwnd);
                return;
            }

            // 阻塞式消息泵：GetMessage 0 CPU 占用；WM_CLOSE → DestroyWindow → WM_DESTROY → PostQuitMessage → 0
            while (NativeMethods.GetMessage(out var msg, nint.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            SendSplashLog($"splash thread crashed: {ex.GetType().Name} {ex.Message}");
            // 静默降级：splash 是纯反馈增强，任何失败不影响主流程
        }
        finally
        {
            if (_hIcon != nint.Zero) { NativeMethods.DestroyIcon(_hIcon); _hIcon = nint.Zero; }
            _hwnd = nint.Zero;
            lock (Sync) { _thread = null; }
        }
    }

    /// <summary>诊断输出（debug.log；开关 off 时 no-op，静默降级语义保持）。</summary>
    private static void SendSplashLog(string msg) => App.DebugLog($"[Splash] {msg}");

    /// <summary>注册窗口类；诊断日志输出 atom 与 LASTERROR（幂等）。</summary>
    private static void RegisterAndLog(ref NativeMethods.WNDCLASSEX wc)
    {
        var atom = NativeMethods.RegisterClassEx(ref wc);
        SendSplashLog($"RegisterClassEx atom={atom} lastErr={Marshal.GetLastWin32Error()}");
    }

    // ── WndProc：WM_CLOSE（带最短显示时长）/ WM_PAINT（GDI 文本绘制）/ WM_DESTROY ──

    private static nint WndProc(nint hwnd, uint uMsg, nint wParam, nint lParam)
    {
        switch (uMsg)
        {
            case NativeMethods.WM_CLOSE:
                // 最短显示时长：MinShowMs 内到达的 WM_CLOSE 由本线程等待补足，不阻塞 Avalonia UI 线程
                var remain = MinShowMs - (Environment.TickCount - _paintedAt);
                if (remain > 0) Thread.Sleep(remain);
                NativeMethods.DestroyWindow(hwnd);
                return nint.Zero;

            case NativeMethods.WM_PAINT:
                Paint(hwnd);
                return nint.Zero;

            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return nint.Zero;

            default:
                return NativeMethods.DefWindowProc(hwnd, uMsg, wParam, lParam);
        }
    }

    // ── GDI 绘制：纯灰度配色（R=G=B 使 BGR/RGB 序无歧义） ──

    private static void Paint(nint hwnd)
    {
        var hdc = NativeMethods.BeginPaint(hwnd, out var ps);
        if (hdc == nint.Zero) return;
        try
        {
            var bg = NativeMethods.CreateSolidBrush((uint)(_dark ? 0x00252525 : 0x00FFFFFF));
            NativeMethods.FillRect(hdc, ref ps.rcPaint, bg);
            NativeMethods.DeleteObject(bg);

            NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
            NativeMethods.SetTextColor(hdc, (uint)(_dark ? 0x00EAEAEA : 0x00444444));

            var dpi = NativeMethods.GetDpiForSystem();
            if (dpi <= 0) dpi = 96;
            var scale = dpi / 96.0;
            int fontHeight = -(int)(scale * 18);

            // Microsoft YaHei UI：简中系统默认 UI 字体，同时完整渲染拉丁字符
            var font = NativeMethods.CreateFont(
                fontHeight, 0, 0, 0, NativeMethods.FW_SEMIBOLD,
                0, 0, 0,
                NativeMethods.DEFAULT_CHARSET, 0, 0,
                NativeMethods.CLEARTYPE_QUALITY, NativeMethods.DEFAULT_PITCH,
                "Microsoft YaHei UI");
            var oldFont = NativeMethods.SelectObject(hdc, font);

            int clientW = ps.rcPaint.Right - ps.rcPaint.Left;
            int clientH = ps.rcPaint.Bottom - ps.rcPaint.Top;

            // 图标尺寸（DPI 缩放；缺图标时按 0 处理 → 布局退化为纯文本）
            int iconSize = (_hIcon != nint.Zero) ? (int)(scale * 32) : 0;
            int gap = (_hIcon != nint.Zero) ? (int)(scale * 16) : 0;

            // 先量文本宽（DT_CALCRECT），归出总宽再居中；DT_END_ELLIPSIS 防超长文件名溢出
            var rcCalc = new NativeMethods.RECT { Left = 0, Top = 0, Right = clientW, Bottom = clientH };
            const uint calcFmt = NativeMethods.DT_LEFT | NativeMethods.DT_CALCRECT | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX | NativeMethods.DT_END_ELLIPSIS;
            NativeMethods.DrawText(hdc, _text, _text.Length, ref rcCalc, calcFmt);
            int textW = Math.Min(clientW - iconSize - gap, rcCalc.Right - rcCalc.Left);

            int totalW = iconSize + gap + textW;
            int startX = Math.Max(0, (clientW - totalW) / 2);
            int centerY = clientH / 2;

            // 图标左（垂直居中）
            if (_hIcon != nint.Zero)
            {
                NativeMethods.DrawIconEx(hdc, startX, centerY - iconSize / 2, _hIcon, iconSize, iconSize, 0, nint.Zero, NativeMethods.DI_NORMAL);
            }

            // 文字右（左对齐 + 垂直居中）
            var rc = new NativeMethods.RECT
            {
                Left = startX + iconSize + gap,
                Top = 0,
                Right = startX + iconSize + gap + textW, // 允许文本宽 = 量宽，DT_END_ELLIPSIS 在此边界截断
                Bottom = clientH,
            };
            const uint fmt = NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX | NativeMethods.DT_END_ELLIPSIS;
            NativeMethods.DrawText(hdc, _text, _text.Length, ref rc, fmt);

            NativeMethods.SelectObject(hdc, oldFont);
            NativeMethods.DeleteObject(font);
        }
        finally
        {
            NativeMethods.EndPaint(hwnd, ref ps);
        }
        _paintedAt = Environment.TickCount;
    }
}
