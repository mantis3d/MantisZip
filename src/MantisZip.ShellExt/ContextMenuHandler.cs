using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MantisZip.ShellExt;

#region COM interface definitions (implemented by our CCW, not ComImport)

/// <summary>
/// IShellExtInit — receives the selected file list from Explorer.
/// GUID: 000214E8-0000-0000-C000-000000000046
/// </summary>
[Guid("000214E8-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellExtInit
{
    /// <param name="pidlFolder">PIDL of the folder (or null for file context).</param>
    /// <param name="pDataObj">IDataObject with the selected items.</param>
    /// <param name="hKeyProgId">Registry key for the ProgID.</param>
    /// <returns>HRESULT</returns>
    [PreserveSig]
    int Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgId);
}

/// <summary>
/// IContextMenu — builds and dispatches the context menu.
/// GUID: 000214E4-0000-0000-C000-000000000046
/// </summary>
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    /// <summary>Adds menu items to the HMENU.</summary>
    [PreserveSig]
    int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    /// <summary>Executes the command identified by idCmd.</summary>
    [PreserveSig]
    int InvokeCommand(IntPtr pici);

    /// <summary>Returns help text or verb name for a command.</summary>
    [PreserveSig]
    int GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved, [Out] StringBuilder commandString, int cch);
}

#endregion

#region Win32 structures

/// <summary>
/// CMINVOKECOMMANDINFO — passed to IContextMenu.InvokeCommand.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct CmInvokeCommandInfo
{
    public int cbSize;          // sizeof(CMINVOKECOMMANDINFO)
    public int fMask;           // CMIC_MASK_* flags
    public IntPtr hwnd;         // owner window
    public IntPtr lpVerb;       // either int ID (HIWORD=0) or string pointer
    [MarshalAs(UnmanagedType.LPStr)]
    public string? lpParameters; // optional parameters
    [MarshalAs(UnmanagedType.LPStr)]
    public string? lpDirectory;  // working directory
    public int nShow;            // SW_* show command
    public int dwHotKey;
    public IntPtr hIcon;
}

/// <summary>
/// MENUITEMINFO — used to configure menu items with icons.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MenuItemInfo
{
    public int cbSize;
    public uint fMask;
    public uint fType;          // MFT_*
    public uint fState;         // MFS_*
    public uint wID;            // command ID
    public IntPtr hSubMenu;     // HMENU for submenu (MF_POPUP)
    public IntPtr hbmpChecked;
    public IntPtr hbmpUnchecked;
    public IntPtr dwItemData;
    public IntPtr dwTypeData;   // string pointer for MIIM_STRING
    public uint cch;            // string length
    public IntPtr hbmpItem;     // HBITMAP for icon (MIIM_BITMAP)
}

#endregion

/// <summary>
/// COM context menu handler for MantisZip.
/// Registered as a shell extension via ContextMenuHandlers.
/// Runs inside Explorer's process — NO WPF/MantisZip.UI references allowed.
/// </summary>
[ComVisible(true)]
[Guid("C90B2A1E-5E4F-4A7A-9B0F-8C1D3E5F7A9B")]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("MantisZip.ContextMenu")]
public class ContextMenuHandler : IShellExtInit, IContextMenu
{
    // ─── Archive extensions (to match ShellIntegration.BuildAppliesToFilter) ───
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".tgz", ".tar.gz", ".gz", ".iso"
    };

    // ─── Command ID offsets (must match order in QueryContextMenu) ───
    private const int CmdIdOpen = 0;
    private const int CmdIdExtractHere = 1;
    private const int CmdIdSmartExtract = 2;
    private const int CmdIdExtractToNamed = 3;
    private const int CmdIdExtractTo = 4;
    private const int CmdIdCompressSeparate = 5;
    private const int CmdIdCompressCombined = 6;
    private const int CmdIdCompress = 7;

    // ─── Menu text labels (same as ShellIntegration, but static since no L.cs access) ───
    // Notes from ShellIntegration.cs localization keys:
    //   Shell_Open, Shell_ExtractHere, Shell_SmartExtract,
    //   Shell_ExtractToNamed, Shell_ExtractTo, Shell_CompressSeparate,
    //   Shell_CompressCombined, Shell_Compress
    // We use Chinese here matching the localization. Can be made configurable later.
    private const string TextOpen = "用 MantisZip 打开";
    private const string TextExtractHere = "用 MantisZip 解压到此处";
    private const string TextSmartExtract = "智能解压到此处";
    private const string TextExtractToNamed = "用 MantisZip 解压到";
    private const string TextExtractTo = "用 MantisZip 解压到……";
    private const string TextCompressSeparate = "压缩到独立的";
    private const string TextCompressCombined = "压缩到";
    private const string TextCompress = "用 MantisZip 压缩";

    // ─── State ───
    private List<string> _selectedFiles = new();
    private string? _targetFolder; // Directory\Background mode
    private bool _isBackgroundMode;

    // ─── Settings cache (read from registry) ───
    private bool _cascadeMode = true;
    private bool _showIcons = true;
    private bool _enableOpen = true;
    private bool _enableExtractHere = true;
    private bool _enableSmartExtract = true;
    private bool _enableExtractToNamed = true;
    private bool _enableExtractTo = true;
    private bool _enableCompressSeparate = true;
    private bool _enableCompressCombined = true;
    private bool _enableCompress = true;

    #region IShellExtInit

    public int Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgId)
    {
        try
        {
            if (pDataObj == IntPtr.Zero)
                return NativeMethods.E_FAIL;

            // Read settings from registry (set by MantisZip UI via AppSettings sync)
            LoadSettingsFromRegistry();

            // Extract file paths from IDataObject via CF_HDROP
            var formatEtc = new NativeMethods.FormatEtc
            {
                cfFormat = NativeMethods.CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = NativeMethods.DVASPECT_CONTENT,
                lindex = -1,
                tymed = NativeMethods.TYMED_HGLOBAL
            };

            NativeMethods.STGMEDIUM medium;
            int hr = NativeMethods.GetData(pDataObj, ref formatEtc, out medium);
            if (hr != NativeMethods.S_OK)
                return NativeMethods.E_FAIL;

            try
            {
                IntPtr hDrop = medium.unionmember;
                int fileCount = NativeMethods.DragQueryFile(hDrop, -1, null, 0);
                _selectedFiles = new List<string>(fileCount);

                var sb = new StringBuilder(260);
                for (int i = 0; i < fileCount; i++)
                {
                    int len = NativeMethods.DragQueryFile(hDrop, i, sb, sb.Capacity);
                    if (len > 0)
                        _selectedFiles.Add(sb.ToString());
                }

                // Directory\Background: pidlFolder gives us the target directory
                if (fileCount == 0 && pidlFolder != IntPtr.Zero)
                {
                    _targetFolder = NativeMethods.GetPathFromPidl(pidlFolder);
                    _isBackgroundMode = !string.IsNullOrEmpty(_targetFolder);
                }

                return NativeMethods.S_OK;
            }
            finally
            {
                NativeMethods.ReleaseStgMedium(ref medium);
            }
        }
        catch
        {
            // Never throw to Explorer
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region IContextMenu.QueryContextMenu

    public int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags)
    {
        try
        {
            // Refresh settings each time (user may have changed them)
            LoadSettingsFromRegistry();

            uint idCmd = idCmdFirst;
            bool isArchive = _selectedFiles.Count > 0 &&
                ArchiveExtensions.Contains(Path.GetExtension(_selectedFiles[0]));

            // Build dynamic file name snippets
            string fileName = _selectedFiles.Count > 0
                ? Path.GetFileNameWithoutExtension(_selectedFiles[0])
                : _targetFolder != null
                    ? Path.GetFileName(_targetFolder.TrimEnd('\\'))
                    : "";

            string multiSuffix = _selectedFiles.Count > 1
                ? $" 等 {_selectedFiles.Count} 个文件"
                : "";

            // ─── Top separator ───
            NativeMethods.InsertMenu(hMenu, indexMenu++, NativeMethods.MF_SEPARATOR | NativeMethods.MF_BYPOSITION,
                idCmd, null);

            // ─── 1. Open (archive only) ───
            if (_enableOpen && isArchive)
            {
                InsertMenuItem(hMenu, indexMenu++, idCmd++, TextOpen + multiSuffix, CmdIdOpen);
            }

            // ─── Extract group (archive only) ───
            bool hasExtract = (_enableExtractHere || _enableSmartExtract || _enableExtractToNamed || _enableExtractTo);
            if (hasExtract && isArchive)
            {
                if (_cascadeMode)
                {
                    // Create submenu for extract operations
                    IntPtr hSubMenu = NativeMethods.CreatePopupMenu();
                    uint subId = 0;

                    if (_enableExtractHere)
                        InsertMenuItem(hSubMenu, subId++, subId, TextExtractHere, CmdIdExtractHere);
                    if (_enableSmartExtract)
                        InsertMenuItem(hSubMenu, subId++, subId, TextSmartExtract, CmdIdSmartExtract);
                    if (_enableExtractToNamed)
                        InsertMenuItem(hSubMenu, subId++, subId, $"{TextExtractToNamed} {fileName}", CmdIdExtractToNamed);
                    if (_enableExtractTo)
                        InsertMenuItem(hSubMenu, subId++, subId, TextExtractTo, CmdIdExtractTo);

                    NativeMethods.InsertMenu(hMenu, indexMenu++,
                        NativeMethods.MF_POPUP | NativeMethods.MF_BYPOSITION,
                        hSubMenu, "解压");
                }
                else
                {
                    // Flat verb mode: add separator then individual items
                    NativeMethods.InsertMenu(hMenu, indexMenu++, NativeMethods.MF_SEPARATOR | NativeMethods.MF_BYPOSITION,
                        idCmd, null);

                    if (_enableExtractHere)
                        InsertMenuItem(hMenu, indexMenu++, idCmd++, TextExtractHere + multiSuffix, CmdIdExtractHere);
                    if (_enableSmartExtract)
                        InsertMenuItem(hMenu, indexMenu++, idCmd++, TextSmartExtract + multiSuffix, CmdIdSmartExtract);
                    if (_enableExtractToNamed)
                        InsertMenuItem(hMenu, indexMenu++, idCmd++, $"{TextExtractToNamed} {fileName}{multiSuffix}", CmdIdExtractToNamed);
                    if (_enableExtractTo)
                        InsertMenuItem(hMenu, indexMenu++, idCmd++, TextExtractTo + multiSuffix, CmdIdExtractTo);
                }
            }

            // ─── Compress group ───
            bool hasCompress = _enableCompressSeparate || _enableCompressCombined || _enableCompress;
            if (hasCompress)
            {
                NativeMethods.InsertMenu(hMenu, indexMenu++, NativeMethods.MF_SEPARATOR | NativeMethods.MF_BYPOSITION,
                    idCmd, null);

                if (_cascadeMode)
                {
                    IntPtr hSubMenu = NativeMethods.CreatePopupMenu();
                    uint subId = 0;

                    if (_enableCompressSeparate)
                        InsertMenuItem(hSubMenu, subId++, subId, $"{TextCompressSeparate} {fileName}.zip", CmdIdCompressSeparate);
                    if (_enableCompressCombined)
                        InsertMenuItem(hSubMenu, subId++, subId, $"{TextCompressCombined} {fileName}.zip", CmdIdCompressCombined);
                    if (_enableCompress)
                        InsertMenuItem(hSubMenu, subId++, subId, TextCompress, CmdIdCompress);

                    NativeMethods.InsertMenu(hMenu, indexMenu++,
                        NativeMethods.MF_POPUP | NativeMethods.MF_BYPOSITION,
                        hSubMenu, "压缩");
                }
                else
                {
                    if (_enableCompressSeparate)
                        InsertMenuItem(hMenu, indexMenu++, idCmd++, $"{TextCompressSeparate} {fileName}.zip{multiSuffix}", CmdIdCompressSeparate);
                    if (_enableCompressCombined)
                        InsertMenuItem(hMenu, indexMenu++, idCmd++, $"{TextCompressCombined} {fileName}.zip{multiSuffix}", CmdIdCompressCombined);
                    if (_enableCompress)
                        InsertMenuItem(hMenu, indexMenu++, idCmd++, TextCompress + multiSuffix, CmdIdCompress);
                }
            }

            // ─── Bottom separator ───
            NativeMethods.InsertMenu(hMenu, indexMenu++, NativeMethods.MF_SEPARATOR | NativeMethods.MF_BYPOSITION,
                idCmd, null);

            // Clean up icon cache after building the menu
            CleanupIconCache();

            // Return the number of menu items added (offset from idCmdFirst)
            return (int)(idCmd - idCmdFirst);
        }
        catch
        {
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region IContextMenu.InvokeCommand

    public int InvokeCommand(IntPtr pici)
    {
        try
        {
            var ici = Marshal.PtrToStructure<CmInvokeCommandInfo>(pici);

            // Determine command ID from lpVerb (could be string or int)
            int cmdId;
            if ((int)ici.lpVerb.HIWORD() != 0)
            {
                // String verb — not expected in our implementation
                return NativeMethods.E_FAIL;
            }
            cmdId = (int)ici.lpVerb.LOWORD();

            // Build the executable path (same directory as our assembly = MantisZip install dir)
            string exePath = Path.Combine(
                Path.GetDirectoryName(typeof(ContextMenuHandler).Assembly.Location) ?? ".",
                "MantisZip.UI.exe");

            string cmdLine;
            string paths = string.Join(" ", _selectedFiles.Select(p => $@"""{p}"""));
            string bgTarget = _isBackgroundMode && _targetFolder != null ? $@"""{_targetFolder}""" : "";
            string singlePath = _selectedFiles.Count > 0 ? $@"""{_selectedFiles[0]}""" : bgTarget;

            // Use _isBackgroundMode to decide single vs multi path
            if (_isBackgroundMode && cmdId >= CmdIdCompressSeparate && cmdId <= CmdIdCompress)
            {
                // Background mode: use _targetFolder as the single argument
                singlePath = bgTarget;
                paths = bgTarget;
            }

            switch (cmdId)
            {
                case CmdIdOpen:
                    cmdLine = $"--open {singlePath}";
                    break;
                case CmdIdExtractHere:
                    cmdLine = $"--extract-here {singlePath}";
                    break;
                case CmdIdSmartExtract:
                    cmdLine = $"--extract-smart {singlePath}";
                    break;
                case CmdIdExtractToNamed:
                    cmdLine = $"--extract-to-name {singlePath}";
                    break;
                case CmdIdExtractTo:
                    cmdLine = $"--extract {singlePath}";
                    break;
                case CmdIdCompressSeparate:
                    cmdLine = $"--compress-separate {paths}";
                    break;
                case CmdIdCompressCombined:
                    cmdLine = $"--compress-combined {paths}";
                    break;
                case CmdIdCompress:
                    cmdLine = $"--compress {paths}";
                    break;
                default:
                    return NativeMethods.E_FAIL;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = cmdLine,
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            Process.Start(psi);
            return NativeMethods.S_OK;
        }
        catch
        {
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region IContextMenu.GetCommandString

    public int GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved, StringBuilder commandString, int cch)
    {
        try
        {
            if (uFlags != NativeMethods.GCS_HELPTEXT && uFlags != NativeMethods.GCS_VERB)
                return NativeMethods.E_NOTIMPL;

            int cmdId = (int)idCmd;
            string text;

            if (uFlags == NativeMethods.GCS_HELPTEXT)
            {
                text = cmdId switch
                {
                    CmdIdOpen => "用 MantisZip 打开压缩包",
                    CmdIdExtractHere => "将压缩包内容解压到当前目录",
                    CmdIdSmartExtract => "智能解压到合适的目录",
                    CmdIdExtractToNamed => "解压到以压缩包名命名的目录",
                    CmdIdExtractTo => "选择解压目标目录",
                    CmdIdCompressSeparate => "将每个文件分别压缩为独立的压缩包",
                    CmdIdCompressCombined => "将所有文件压缩到一个压缩包",
                    CmdIdCompress => "打开压缩对话框",
                    _ => ""
                };
            }
            else // GCS_VERB
            {
                text = cmdId switch
                {
                    CmdIdOpen => "open",
                    CmdIdExtractHere => "extracthere",
                    CmdIdSmartExtract => "smartextract",
                    CmdIdExtractToNamed => "extracttonamed",
                    CmdIdExtractTo => "extract",
                    CmdIdCompressSeparate => "compressseparate",
                    CmdIdCompressCombined => "compresscombined",
                    CmdIdCompress => "compress",
                    _ => ""
                };
            }

            if (string.IsNullOrEmpty(text))
                return NativeMethods.E_FAIL;

            if (commandString != null && cch > 0)
            {
                commandString.Clear();
                commandString.Append(text);
            }

            return NativeMethods.S_OK;
        }
        catch
        {
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region Private helpers

    // Cache the app icon bitmap for reuse across menu items within the same right-click.
    // The bitmap is loaded once per QueryContextMenu call.
    private static IntPtr _cachedIconBitmap = IntPtr.Zero;
    private static readonly object _iconLock = new();

    private void InsertMenuItem(IntPtr hMenu, uint position, uint id, string text, int commandId)
    {
        var mii = new MenuItemInfo
        {
            cbSize = Marshal.SizeOf<MenuItemInfo>(),
            fMask = NativeMethods.MIIM_STRING | NativeMethods.MIIM_ID | NativeMethods.MIIM_FTYPE,
            fType = NativeMethods.MFT_STRING,
            fState = NativeMethods.MFS_ENABLED,
            wID = id,
            dwTypeData = Marshal.StringToCoTaskMemUni(text),
            cch = (uint)text.Length
        };

        // Add icon if enabled (load once, reuse)
        if (_showIcons)
        {
            if (_cachedIconBitmap == IntPtr.Zero)
            {
                _cachedIconBitmap = NativeMethods.LoadAppIconBitmap();
            }
            if (_cachedIconBitmap != IntPtr.Zero)
            {
                mii.fMask |= NativeMethods.MIIM_BITMAP;
                mii.hbmpItem = _cachedIconBitmap;
            }
        }

        NativeMethods.InsertMenuItem(hMenu, position, true, ref mii);

        // Free the string memory we allocated
        Marshal.FreeCoTaskMem(mii.dwTypeData);
    }

    /// <summary>Clean up the cached icon bitmap. Called at the end of QueryContextMenu.</summary>
    private void CleanupIconCache()
    {
        if (_cachedIconBitmap != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_cachedIconBitmap);
            _cachedIconBitmap = IntPtr.Zero;
        }
    }

    /// <summary>Read settings from HKCU\Software\MantisZip\ContextMenu (set by AppSettings sync).</summary>
    private void LoadSettingsFromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\MantisZip\ContextMenu", writable: false);
            if (key == null) return; // use defaults

            _cascadeMode = ReadDword(key, "EnableCascadingMenu", 1) != 0;
            _showIcons = ReadDword(key, "ShowMenuIcons", 1) != 0;
            _enableOpen = ReadDword(key, "EnableOpenMenu", 1) != 0;
            _enableExtractHere = ReadDword(key, "EnableExtractHereMenu", 1) != 0;
            _enableSmartExtract = ReadDword(key, "EnableSmartExtractMenu", 1) != 0;
            _enableExtractToNamed = ReadDword(key, "EnableExtractToNamedMenu", 1) != 0;
            _enableExtractTo = ReadDword(key, "EnableExtractToMenu", 1) != 0;
            _enableCompressSeparate = ReadDword(key, "EnableCompressSeparate", 1) != 0;
            _enableCompressCombined = ReadDword(key, "EnableCompressCombined", 1) != 0;
            _enableCompress = ReadDword(key, "EnableCompressMenu", 1) != 0;
        }
        catch
        {
            // Use defaults
        }
    }

    private static int ReadDword(Microsoft.Win32.RegistryKey key, string name, int defaultValue)
    {
        var val = key.GetValue(name);
        if (val is int i) return i;
        return defaultValue;
    }

    #endregion
}
