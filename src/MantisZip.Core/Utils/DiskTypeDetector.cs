using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MantisZip.Core.Utils;

/// <summary>磁盘类型。</summary>
public enum DiskType
{
    /// <summary>固态硬盘（无寻道惩罚）。</summary>
    Ssd,
    /// <summary>机械硬盘（有寻道惩罚）。</summary>
    Hdd,
    /// <summary>未知（检测失败 / 网络盘 / 非 Windows）。</summary>
    Unknown
}

/// <summary>
/// 磁盘类型检测（Windows DeviceIoControl IOCTL_STORAGE_QUERY_PROPERTY +
/// StorageDeviceSeekPenaltyProperty）。结果按盘符缓存（磁盘类型不会变）。
/// 非 Windows / 检测失败 / UNC / 相对路径 → Unknown。
/// </summary>
public static class DiskTypeDetector
{
    private static readonly ConcurrentDictionary<string, DiskType> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
    {
        public int Version;
        public int Size;
        public byte IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref STORAGE_PROPERTY_QUERY lpInBuffer, uint nInBufferSize,
        ref DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    private const uint FILE_SHARE_READWRITE = 3;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>根据路径提取盘符根（如 "C:\Users\x" → "C:"）。无盘符（相对/UNC）返回 null。</summary>
    public static string? ExtractDriveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            // 相对路径不是绝对路径，直接判定为无盘符（GetFullPath 会按当前目录展开成绝对路径）
            if (!Path.IsPathRooted(path)) return null;

            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;
            // "C:\" → "C:"；UNC → null
            if (root.Length >= 2 && root[1] == ':') return root[..2];
            return null;
        }
        catch (Exception ex)
        {
            CoreLog.Trace($"DiskTypeDetector.ExtractDriveRoot failed for path '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>检测路径所在磁盘类型。非 Windows 返回 Unknown。</summary>
    public static DiskType GetDiskType(string? path)
    {
        if (!OperatingSystem.IsWindows()) return DiskType.Unknown;

        var root = ExtractDriveRoot(path);
        if (root == null) return DiskType.Unknown;

        // Unknown 不缓存（瞬态失败可重试），仅缓存确定的 Ssd/Hdd
        if (Cache.TryGetValue(root, out var cached)) return cached;

        var type = QuerySeekPenalty(root);
        if (type != DiskType.Unknown) Cache[root] = type;
        return type;
    }

    private static DiskType QuerySeekPenalty(string driveRoot)
    {
        try
        {
            // 卷设备句柄 \\.\C: 才服务存储 IOCTL；目录句柄会 CreateFile 失败或 ERROR_INVALID_PARAMETER。
            // dwDesiredAccess=0：非管理员对 \\.\C: 请求 GENERIC_READ 会 ACCESS_DENIED (err=5)
            using var handle = CreateFile(
                @"\\.\" + driveRoot,
                0,
                FILE_SHARE_READWRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                CoreLog.Trace($"DiskTypeDetector: CreateFile failed for {driveRoot}, win32error={Marshal.GetLastWin32Error()}");
                return DiskType.Unknown;
            }

            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceSeekPenaltyProperty,
                QueryType = 0 // PropertyStandardQuery
            };
            var descriptor = new DEVICE_SEEK_PENALTY_DESCRIPTOR();

            if (!DeviceIoControl(
                    handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    ref query, (uint)Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                    ref descriptor, (uint)Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(),
                    out _, IntPtr.Zero))
            {
                CoreLog.Trace($"DiskTypeDetector: DeviceIoControl SeekPenalty failed for {driveRoot}, win32error={Marshal.GetLastWin32Error()}");
                return DiskType.Unknown;
            }

            return descriptor.IncursSeekPenalty != 0 ? DiskType.Hdd : DiskType.Ssd;
        }
        catch (Exception ex)
        {
            CoreLog.Trace($"DiskTypeDetector.QuerySeekPenalty failed for {driveRoot}: {ex.Message}");
            return DiskType.Unknown;
        }
    }
}
