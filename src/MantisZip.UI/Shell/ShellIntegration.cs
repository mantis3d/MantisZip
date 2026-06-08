using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// Shell 右键菜单集成。
/// 写入 HKCU\Software\Classes，无需管理员权限。
/// 始终使用层叠子菜单模式（动词模式已在 v0.4.0 移除）。
/// 菜单顺序：
///   1. 用MantisZip打开
///   2. 用MantisZip解压到此处
///   3. 用MantisZip解压到（压缩包名）
///   4. 用MantisZip解压到……
///   5. 压缩到独立的（文件名）
///   6. 压缩到（父目录名）
///   7. 用MantisZip压缩
/// </summary>
internal static partial class ShellIntegration
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private static readonly string[] ArchiveExtensions =
        ArchiveEngineFactory.SupportedExtensions;

    // Registry key identifiers — must stay as fixed English strings (language-independent)
    private const string CascadeRoot = "MantisZip";
    private const string OldProgId = "MantisZip.Archive";       // 旧版单 ProgId（迁移用）
    private const string CustomProgId = "MantisZip.Custom";    // 自定义扩展名

    /// <summary>返回扩展名对应的独立 ProgId（类似 Bandizip 的 per-format 方案）。</summary>
    private static string GetProgId(string ext) => ext.ToLowerInvariant() switch
    {
        ".zip"    => "MantisZip.Zip",
        ".7z"     => "MantisZip.7z",
        ".rar"    => "MantisZip.Rar",
        ".tar"    => "MantisZip.Tar",
        ".tgz" or ".tar.gz" => "MantisZip.TarGz",
        ".gz"     => "MantisZip.Gz",
        ".iso"    => "MantisZip.Iso",
        _         => CustomProgId,
    };

    // Verb names (仅用于 Uninstall 清理旧注册，动词模式已在 v0.4.0 移除)
    private const string OpenVerb = "01_MantisZipOpen";
    private const string ExtractHereVerb = "02_MantisZipExtractHere";
    private const string ExtractSmartVerb = "02_MantisZipSmartExtract";
    private const string ExtractToNamedVerb = "03_MantisZipExtractToNamed";
    private const string ExtractVerb = "04_MantisZipExtract";
    private const string CompressSeparateVerb = "05_MantisZipCompressSeparate";
    private const string CompressCombinedVerb = "06_MantisZipCompressCombined";
    private const string CompressVerb = "07_MantisZipCompress";

    // Display names (shown in context menu — localized)
    private static readonly string OpenDisplay = L.T(L.Shell_Open);
    private static readonly string ExtractHereDisplay = L.T(L.Shell_ExtractHere);
    private static readonly string ExtractSmartDisplay = L.T(L.Shell_SmartExtract);
    private static readonly string ExtractToNamedDisplay = L.T(L.Shell_ExtractToNamed);
    private static readonly string ExtractDisplay = L.T(L.Shell_ExtractTo);
    private static readonly string CompressSeparateDisplay = L.T(L.Shell_CompressSeparate);
    private static readonly string CompressCombinedDisplay = L.T(L.Shell_CompressCombined);
    private static readonly string CompressDisplay = L.T(L.Shell_Compress);

    // MantisZip.ShellExt 组件 CLSID（必须与 ContextMenuHandler.cs 的 [Guid] 一致）
    private const string ComClsid = "{C90B2A1E-5E4F-4A7A-9B0F-8C1D3E5F7A9B}";
    private const string ComProgId = "MantisZip.ContextMenu";
    private const string ComHandlerKey = "MantisZip";

    #region Registry helpers

    private static void SetRegistryValue(string subKey, string? valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey);
        key?.SetValue(valueName, value);
    }

    private static void SetRegistryDword(string subKey, string valueName, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey);
        key?.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    private static void DeleteRegistryKey(string subKey)
    {
        Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    private static string GetExePath()
    {
        // Assembly 所在目录（兼容 dotnet run 场景）
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var dir = Path.GetDirectoryName(assemblyPath);
        if (dir != null)
        {
            var exePath = Path.Combine(dir, "MantisZip.UI.exe");
            if (File.Exists(exePath))
                return exePath;
        }
        return assemblyPath;
    }

    #endregion
}
