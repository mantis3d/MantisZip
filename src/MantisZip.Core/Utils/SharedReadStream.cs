using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// 共享读文件流：以「只读 + 允许他人继续读写删除」的方式打开源文件。
///
/// 背景：Windows 打开文件是双向共享契约——新句柄不仅要"申请的权限"被已有句柄允许，
/// 其声明的共享模式也必须覆盖已有句柄已持有的权限。<see cref="File.OpenRead(string)"/>
/// 隐含 FileShare.Read（禁止他人写），当源文件正被 Word/Excel 等编辑器以 ReadWrite
/// 权限持有时会直接冲突，抛 IOException「文件正由另一进程使用」，导致整个压缩任务终止。
///
/// 本方法改用 FileShare.ReadWrite | FileShare.Delete（7-Zip 同款语义）：
/// 压缩只需读，允许写入方继续持有写权限，绝大多数「文件占用」场景因此消失。
/// 代价是若对方恰在写入中，读到的是撕裂快照——归档工具的行业惯例，配合
/// 引擎的重试机制缓解。
///
/// 仅用于压缩侧读取用户源文件；读取压缩包本身（archivePath）仍用 File.OpenRead，
/// 保持原行为避免引入回归。
/// </summary>
public static class SharedReadStream
{
    /// <summary>
    /// 以共享读模式打开文件（允许其他进程同时读写删除）。
    /// </summary>
    public static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
