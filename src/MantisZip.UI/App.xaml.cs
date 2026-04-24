using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MantisZip.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // 捕获 Debug 输出到文件
        var listener = new TextWriterTraceListener(LogFile);
        listener.Name = "FileLogger";
        Trace.Listeners.Add(listener);
        Trace.AutoFlush = true;

        // 删除旧日志
        try { File.Delete(LogFile); } catch { }

        base.OnStartup(e);
        Log("程序启动");
    }

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            Debug.WriteLine(msg);
        }
        catch { }
    }

    public static void Log(string fmt, params object[] args)
    {
        Log(string.Format(fmt, args));
    }
}

