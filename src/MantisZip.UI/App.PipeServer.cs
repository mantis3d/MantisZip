using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MantisZip.UI;

/// <summary>
/// 命名管道 IPC 子系统 — 多实例进程间通信
/// Windows 每选一个文件启动一个独立进程，
/// 第一个作为收集器，后续实例通过管道发送路径后退出。
/// </summary>
public partial class App : Application
{
    private static string CompressMutexName = "MantisZip-CompressMutex";
    private static string CompressPipeName = "MantisZip-Compress";
    private static readonly ManualResetEventSlim _compressPipeReady = new(false);

    private static string CompressSeparateMutexName = "MantisZip-CompressSeparateMutex";
    private static string CompressSeparatePipeName = "MantisZip-CompressSeparate";
    private static readonly ManualResetEventSlim _compressSeparatePipeReady = new(false);

    private static string CompressCombinedMutexName = "MantisZip-CompressCombinedMutex";
    private static string CompressCombinedPipeName = "MantisZip-CompressCombined";
    private static readonly ManualResetEventSlim _compressCombinedPipeReady = new(false);

    private static void StartCompressPipeServer(List<string> allPaths, CancellationToken ct)
    {
        Task.Run(async () =>
        {
            try
            {
                _compressPipeReady.Set();
                while (!ct.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(
                        CompressPipeName, PipeDirection.In, -1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.WaitForConnectionAsync(ct);
                        using var reader = new StreamReader(pipe);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            lock (allPaths)
                            {
                                if (!allPaths.Contains(line) && (File.Exists(line) || Directory.Exists(line)))
                                    allPaths.Add(line);
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    finally { pipe.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception pipeEx) { LogDebug("StartCompressPipeServer: connection error: {0}", pipeEx.Message); }
        });
    }

    private static void SendPathsToFirstInstance(List<string> paths)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", CompressPipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            foreach (var p in paths)
                writer.WriteLine(p);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Log("SendPathsToFirstInstance 失败: {0}", ex.Message);
        }
    }

    private static void StartPipeServer(List<string> allPaths, CancellationToken ct, string pipeName, ManualResetEventSlim readyEvent)
    {
        Task.Run(async () =>
        {
            try
            {
                readyEvent.Set();
                while (!ct.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, -1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.WaitForConnectionAsync(ct);
                        using var reader = new StreamReader(pipe);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            lock (allPaths)
                            {
                                if (!allPaths.Contains(line) && (File.Exists(line) || Directory.Exists(line)))
                                    allPaths.Add(line);
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    finally { pipe.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception pipeEx) { LogDebug("PipeServer ({0}): connection error: {1}", pipeName, pipeEx.Message); }
        });
    }

    private static void SendPathsThroughPipe(List<string> paths, string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            foreach (var p in paths)
                writer.WriteLine(p);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Log("SendPathsThroughPipe ({0}) 失败: {1}", pipeName, ex.Message);
        }
    }
}
