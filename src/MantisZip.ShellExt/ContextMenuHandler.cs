using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
public interface IShellExtInit
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
public interface IContextMenu
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

#region IContextMenu2 / IContextMenu3 (submenu message routing)

// IMPORTANT: These interfaces are defined as FLAT (no managed inheritance from
// IContextMenu). .NET COM CCW does NOT correctly flatten inherited interface
// vtables — giving a derived interface its own IID but only including new methods
// breaks COM QueryInterface. By declaring ALL methods explicitly, each interface
// gets a proper standalone COM vtable that Explorer can call into.

/// <summary>
/// IContextMenu2 — extends IContextMenu with HandleMenuMsg for submenu message routing.
/// GUID: 000214F4-0000-0000-C000-000000000046
/// Without this interface, Explorer's submenu InvokeCommand routing breaks when
/// &gt;16 files are selected (GetCommandString for offset 0 kills subsequent routing).
/// </summary>
[Guid("000214F4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu2
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig]
    int InvokeCommand(IntPtr pici);
    [PreserveSig]
    int GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved, [Out] StringBuilder commandString, int cch);
    [PreserveSig]
    int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
}

/// <summary>
/// IContextMenu3 — extends IContextMenu2 with HandleMenuMsg2 (adds plResult).
/// GUID: 000214F8-0000-0000-C000-000000000046
/// Preferred over IContextMenu2 on Windows 2000+, returns the result of message processing.
/// </summary>
[Guid("000214F8-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu3
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig]
    int InvokeCommand(IntPtr pici);
    [PreserveSig]
    int GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved, [Out] StringBuilder commandString, int cch);
    [PreserveSig]
    int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    [PreserveSig]
    int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
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
public class ContextMenuHandler : IShellExtInit, IContextMenu, IContextMenu2, IContextMenu3
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
    private const string TextOpen = "打开压缩包";
    private const string TextExtractHereSingle = "原地解压包";
    private const string TextExtractHereMulti = "原地解压{0}个压缩包";
    private const string TextSmartExtractSingle = "智能原地解压";
    private const string TextSmartExtractMulti = "智能原地解压{0}个压缩包";
    private const string TextExtractToNamed = "解压到";
    private const string TextExtractTo = "解压到……";
    private const string TextCompress = "压缩……";

    // Runtime text fields (populated from registry; fall back to defaults above)
    private string _textOpen = TextOpen;
    private string _textExtractHereSingle = TextExtractHereSingle;
    private string _textExtractHereMulti = TextExtractHereMulti;
    private string _textSmartExtractSingle = TextSmartExtractSingle;
    private string _textSmartExtractMulti = TextSmartExtractMulti;
    private string _textExtractToNamed = TextExtractToNamed;
    private string _textExtractTo = TextExtractTo;
    private string _textCompress = TextCompress;

    // ─── Instance tracking ───
    private static volatile int _nextInstanceId;
    private readonly int _instanceId = Interlocked.Increment(ref _nextInstanceId);

    // ─── Cross-instance file list sharing ───
    // Explorer truncates IShellExtInit.Initialize to 16 files for the first COM
    // instance, then creates a second instance with the full list. This second
    // instance often never receives InvokeCommand (known Explorer routing issue
    // with CreatePopupMenu submenus). We store the full list here so the first
    // instance (which DOES receive InvokeCommand) can use it.
    private static List<string>? _fullFileList;
    private static readonly object _fileListLock = new();

    // ─── State ───
    private List<string> _selectedFiles = new();
    private string? _targetFolder; // Directory\Background mode
    private bool _isBackgroundMode;
    private readonly List<int> _cmdIdOrder = new();

    // ─── Constructor / finalizer ───
    public ContextMenuHandler()
    {
        ShellExtLog.Info($"ContextMenuHandler #{_instanceId}: constructor");
    }

    ~ContextMenuHandler()
    {
        ShellExtLog.Info($"ContextMenuHandler #{_instanceId}: finalizer");
    }

    // ─── Settings cache (read from registry) ───
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
        ShellExtLog.Info($"IShellExtInit.Initialize #{_instanceId} entered, pidlFolder=0x{pidlFolder:x16}, hKeyProgId=0x{hKeyProgId:x16}");
        try
        {
            if (pDataObj == IntPtr.Zero)
            {
                ShellExtLog.Warn("IShellExtInit.Initialize: pDataObj is null, returning E_FAIL");
                return NativeMethods.E_FAIL;
            }

            ShellExtLog.Info("IShellExtInit.Initialize: pDataObj is valid, reading settings from registry");

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
            var dataObj = (IDataObjectCom)Marshal.GetObjectForIUnknown(pDataObj);
            int hr = dataObj.GetData(ref formatEtc, out medium);
            ShellExtLog.Info($"IShellExtInit.Initialize: GetData returned hr=0x{hr:x8}");
            if (hr != NativeMethods.S_OK)
            {
                ShellExtLog.Warn($"IShellExtInit.Initialize: GetData failed with hr=0x{hr:x8}, returning E_FAIL");
                return NativeMethods.E_FAIL;
            }

            try
            {
                IntPtr hDrop = medium.hBitmap;
                int fileCount = NativeMethods.DragQueryFile(hDrop, -1, null, 0);
                ShellExtLog.Info($"IShellExtInit.Initialize: DragQueryFile count={fileCount}");

                _selectedFiles = new List<string>(fileCount > 0 ? fileCount : 0);

                var sb = new StringBuilder(260);
                for (int i = 0; i < fileCount; i++)
                {
                    int len = NativeMethods.DragQueryFile(hDrop, i, sb, sb.Capacity);
                    if (len > 0)
                    {
                        string path = sb.ToString();
                        _selectedFiles.Add(path);
                        ShellExtLog.Info($"IShellExtInit.Initialize: File[{i}] = \"{path}\"");
                    }
                    else
                    {
                        ShellExtLog.Warn($"IShellExtInit.Initialize: File[{i}] DragQueryFile returned 0");
                    }
                }

                // Directory\Background: pidlFolder gives us the target directory
                if (fileCount == 0 && pidlFolder != IntPtr.Zero)
                {
                    _targetFolder = NativeMethods.GetPathFromPidl(pidlFolder);
                    _isBackgroundMode = !string.IsNullOrEmpty(_targetFolder);
                    ShellExtLog.Info($"IShellExtInit.Initialize: Background mode, _targetFolder=\"{_targetFolder}\", _isBackgroundMode={_isBackgroundMode}");
                }
                else
                {
                    var ext0 = _selectedFiles.Count > 0 ? Path.GetExtension(_selectedFiles[0]) : null;
                    ShellExtLog.Info($"IShellExtInit.Initialize: File mode, selected {_selectedFiles.Count} files, isArchive={_selectedFiles.Count > 0 && ext0 != null && ArchiveExtensions.Contains(ext0)}");
                }

                ShellExtLog.Info($"IShellExtInit.Initialize returning S_OK with {_selectedFiles.Count} files selected");

                // Store the file list in the shared static field (keep largest).
                // Explorer creates multiple instances for >16 files: the first gets
                // truncated to 16, a later instance has the full list, and more
                // instances may have only 1 file. We keep the largest list.
                if (_selectedFiles.Count > 0)
                {
                    lock (_fileListLock)
                    {
                        if (_fullFileList == null || _selectedFiles.Count > _fullFileList.Count)
                        {
                            _fullFileList = new List<string>(_selectedFiles);
                            ShellExtLog.Info($"IShellExtInit.Initialize: stored {_fullFileList.Count} files in static _fullFileList (was {(int?)_fullFileList?.Count ?? 0})");
                        }
                        else
                        {
                            ShellExtLog.Info($"IShellExtInit.Initialize: skipped _fullFileList update (current={_fullFileList.Count} >= new={_selectedFiles.Count})");
                        }
                    }
                }

                return NativeMethods.S_OK;
            }
            finally
            {
                NativeMethods.ReleaseStgMedium(ref medium);
                ShellExtLog.Info("IShellExtInit.Initialize: Released STGMEDIUM");
            }
        }
        catch (Exception ex)
        {
            ShellExtLog.Error("IShellExtInit.Initialize exception", ex);
            // Never throw to Explorer
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region IContextMenu.QueryContextMenu

    public int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags)
    {
        ShellExtLog.Info($"IContextMenu.QueryContextMenu #{_instanceId} entered, idCmdFirst={idCmdFirst}, idCmdLast={idCmdLast}, uFlags=0x{uFlags:x8}, _selectedFiles.Count={_selectedFiles.Count}");
        try
        {
            // Refresh settings each time (user may have changed them)
            LoadSettingsFromRegistry();

            // Clean up icon HBITMAPs from the previous right-click.
            // We do this at the start (not end) of QueryContextMenu because Explorer
            // renders the menu asynchronously — deleting before draw = invisible icons.
            CleanupIconCache();

            _cmdIdOrder.Clear();
            uint idCmd = idCmdFirst;
            string? firstExt = _selectedFiles.Count > 0 ? Path.GetExtension(_selectedFiles[0]) : null;
            bool isArchive = firstExt != null && ArchiveExtensions.Contains(firstExt);
            ShellExtLog.Info($"QueryContextMenu: firstExt=\"{firstExt}\", isArchive={isArchive}");

            // Build dynamic file name snippets
            string fileName = _selectedFiles.Count > 0
                ? Path.GetFileNameWithoutExtension(_selectedFiles[0])
                : _targetFolder != null
                    ? Path.GetFileName(_targetFolder.TrimEnd('\\'))
                    : "";

            // Common parent directory name for CompressCombined display
            string parentDirName = ComputeCommonParentName(_selectedFiles);
            ShellExtLog.Info($"QueryContextMenu: fileName=\"{fileName}\", parentDirName=\"{parentDirName}\", multiFile={_selectedFiles.Count > 1}");

            // ─── Top separator ───
            NativeMethods.InsertMenu(hMenu, indexMenu++, NativeMethods.MF_SEPARATOR | NativeMethods.MF_BYPOSITION,
                idCmd, null);
            ShellExtLog.Info("QueryContextMenu: added top separator");

            // ─── Extract group (archive only): Open + extract items ───
            bool hasExtractOrOpen = _enableOpen || _enableExtractHere || _enableSmartExtract || _enableExtractToNamed || _enableExtractTo;
            ShellExtLog.Info($"QueryContextMenu: hasExtractOrOpen={hasExtractOrOpen}, isArchive={isArchive}");
            if (hasExtractOrOpen && isArchive)
            {
                ShellExtLog.Info("QueryContextMenu: building extract submenu");
                IntPtr hSubMenu = NativeMethods.CreatePopupMenu();
                uint subId = 0;

                // Open first, then extract items
                bool multipleFiles = _selectedFiles.Count > 1;
                if (_enableOpen)
                {
                    string openText = multipleFiles
                        ? $"{_textOpen} 等 {_selectedFiles.Count} 个文件"
                        : _textOpen;
                    ShellExtLog.Info($"QueryContextMenu: extract submenu: TextOpen");
                    InsertMenuItem(hSubMenu, subId++, idCmd++, openText, CmdIdOpen);
                    _cmdIdOrder.Add(CmdIdOpen);
                    ShellExtLog.Info($"  -> Open added at subId={subId - 1}");
                }
                if (_enableExtractHere)
                {
                    string hereText = multipleFiles
                        ? string.Format(_textExtractHereMulti, _selectedFiles.Count)
                        : _textExtractHereSingle;
                    ShellExtLog.Info($"QueryContextMenu: extract submenu: TextExtractHere = \"{hereText}\"");
                    InsertMenuItem(hSubMenu, subId++, idCmd++, hereText, CmdIdExtractHere);
                    _cmdIdOrder.Add(CmdIdExtractHere);
                }
                if (_enableSmartExtract)
                {
                    string smartText = multipleFiles
                        ? string.Format(_textSmartExtractMulti, _selectedFiles.Count)
                        : _textSmartExtractSingle;
                    ShellExtLog.Info($"QueryContextMenu: extract submenu: TextSmartExtract = \"{smartText}\"");
                    InsertMenuItem(hSubMenu, subId++, idCmd++, smartText, CmdIdSmartExtract);
                    _cmdIdOrder.Add(CmdIdSmartExtract);
                }
                if (_enableExtractToNamed)
                { ShellExtLog.Info($"QueryContextMenu: extract submenu: TextExtractToNamed \"{fileName}\""); InsertMenuItem(hSubMenu, subId++, idCmd++, $"{_textExtractToNamed} {fileName}", CmdIdExtractToNamed); _cmdIdOrder.Add(CmdIdExtractToNamed); }
                if (_enableExtractTo)
                { ShellExtLog.Info($"QueryContextMenu: extract submenu: TextExtractTo"); InsertMenuItem(hSubMenu, subId++, idCmd++, _textExtractTo, CmdIdExtractTo); _cmdIdOrder.Add(CmdIdExtractTo); }

                ShellExtLog.Info("QueryContextMenu: inserting extract submenu into main menu");
                InsertMenuItem(hMenu, indexMenu++, idCmd, "打开/解压", -1, hSubMenu: hSubMenu, hbmpOverride: GetParentIcon(isExtract: true));
            }
            else
            {
                ShellExtLog.Info($"QueryContextMenu: skipping extract group (hasExtractOrOpen={hasExtractOrOpen}, isArchive={isArchive})");
            }

            // ─── Compress group ───
            bool hasCompress = _enableCompressSeparate || _enableCompressCombined || _enableCompress;
            ShellExtLog.Info($"QueryContextMenu: hasCompress={hasCompress}");
            if (hasCompress)
            {
                ShellExtLog.Info("QueryContextMenu: building compress submenu");
                IntPtr hSubMenu = NativeMethods.CreatePopupMenu();
                uint subId = 0;

                if (_enableCompressSeparate)
                {
                    string separateText = _selectedFiles.Count > 1
                        ? $"压缩到{_selectedFiles.Count}个独立的压缩文件.zip"
                        : $"压缩到 {fileName}.zip";
                    ShellExtLog.Info($"QueryContextMenu: compress submenu: \"{separateText}\"");
                    InsertMenuItem(hSubMenu, subId++, idCmd++, separateText, CmdIdCompressSeparate);
                    _cmdIdOrder.Add(CmdIdCompressSeparate);
                }
                if (_enableCompressCombined)
                {
                    string combinedText = parentDirName.Length > 0
                        ? $"压缩到 {parentDirName}.zip"
                        : "压缩到";
                    ShellExtLog.Info($"QueryContextMenu: compress submenu: \"{combinedText}\"");
                    InsertMenuItem(hSubMenu, subId++, idCmd++, combinedText, CmdIdCompressCombined);
                    _cmdIdOrder.Add(CmdIdCompressCombined);
                }
                if (_enableCompress)
                { ShellExtLog.Info($"QueryContextMenu: compress submenu: TextCompress"); InsertMenuItem(hSubMenu, subId++, idCmd++, _textCompress, CmdIdCompress); _cmdIdOrder.Add(CmdIdCompress); }

                ShellExtLog.Info("QueryContextMenu: inserting compress submenu into main menu");
                InsertMenuItem(hMenu, indexMenu++, idCmd, "压缩", -1, hSubMenu: hSubMenu, hbmpOverride: GetParentIcon(isExtract: false));
            }
            else
            {
                ShellExtLog.Info("QueryContextMenu: skipping compress group (all toggles off)");
            }

            // ─── Bottom separator ───
            NativeMethods.InsertMenu(hMenu, indexMenu++, NativeMethods.MF_SEPARATOR | NativeMethods.MF_BYPOSITION,
                idCmd, null);
            ShellExtLog.Info("QueryContextMenu: added bottom separator");

            // Note: icon HBITMAPs are NOT deleted here because Explorer draws the menu
            // asynchronously after QueryContextMenu returns. Deleting them would cause
            // the icons to not render. They will be cleaned up when the COM object is
            // released by Explorer (next Initialize call clears them).
            // CleanupIconCache();

            int itemsAdded = (int)(idCmd - idCmdFirst);
            string orderSnapshot = string.Join(", ", _cmdIdOrder);
            ShellExtLog.Info($"QueryContextMenu #{_instanceId}: returning {itemsAdded} items, _cmdIdOrder=[{orderSnapshot}]");
            // Return the number of menu items added (offset from idCmdFirst)
            return itemsAdded;
        }
        catch (Exception ex)
        {
            ShellExtLog.Error("IContextMenu.QueryContextMenu exception", ex);
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region IContextMenu.InvokeCommand

    public int InvokeCommand(IntPtr pici)
    {
        ShellExtLog.Info($"IContextMenu.InvokeCommand #{_instanceId} entered, pici=0x{pici:x16}");
        try
        {
            var ici = Marshal.PtrToStructure<CmInvokeCommandInfo>(pici);
            ShellExtLog.Info($"InvokeCommand: cbSize={ici.cbSize}, fMask=0x{ici.fMask:x}, lpVerb=0x{ici.lpVerb:x16}, nShow={ici.nShow}");

            // Determine command ID from lpVerb (could be string or int)
            uint hiword = ici.lpVerb.HIWORD();
            if (hiword != 0)
            {
                // String verb — not expected in our implementation
                ShellExtLog.Warn($"InvokeCommand: lpVerb HIWORD=0x{hiword:x4} != 0, got string verb, returning E_FAIL");
                return NativeMethods.E_FAIL;
            }

            int offset = (int)ici.lpVerb.LOWORD();
            ShellExtLog.Info($"InvokeCommand: offset={offset}, _cmdIdOrder.Count={_cmdIdOrder.Count}");
            if (offset < 0 || offset >= _cmdIdOrder.Count)
            {
                ShellExtLog.Warn($"InvokeCommand: offset {offset} out of range [0, {_cmdIdOrder.Count}), returning E_FAIL");
                return NativeMethods.E_FAIL;
            }
            int cmdId = _cmdIdOrder[offset];
            if (cmdId < 0)
            {
                ShellExtLog.Warn($"InvokeCommand: cmdId={cmdId} is not a valid command (separator placeholder at offset {offset}), returning E_FAIL");
                return NativeMethods.E_FAIL;
            }

            string[] cmdNames = { "Open", "ExtractHere", "SmartExtract", "ExtractToNamed", "ExtractTo", "CompressSeparate", "CompressCombined", "Compress" };
            string cmdName = cmdId >= 0 && cmdId < cmdNames.Length ? cmdNames[cmdId] : $"UNKNOWN({cmdId})";
            ShellExtLog.Info($"InvokeCommand: mapped to cmdId={cmdId} ({cmdName})");

            // Build the executable path (same directory as our assembly = MantisZip install dir)
            string asmDir = Path.GetDirectoryName(typeof(ContextMenuHandler).Assembly.Location) ?? ".";
            string exePath = Path.Combine(asmDir, "MantisZip.UI.exe");
            bool exeExists = File.Exists(exePath);
            ShellExtLog.Info($"InvokeCommand: assemblyDir=\"{asmDir}\", exePath=\"{exePath}\", exists={exeExists}");

            if (!exeExists)
            {
                ShellExtLog.Error($"InvokeCommand: MantisZip.UI.exe not found at \"{exePath}\"");
                return NativeMethods.E_FAIL;
            }

            // Resolve the effective file list — Explorer truncates the first instance's
            // Initialize to 16 files, but a later instance may have the full list in the
            // shared static field.
            var files = GetEffectiveFiles(out int originalCount);
            bool usingSharedList = files.Count > originalCount;
            if (usingSharedList)
            {
                ShellExtLog.Info($"InvokeCommand: using shared full file list ({files.Count} files, original instance had {originalCount})");
            }

            string cmdLine;
            string paths = string.Join(" ", files.Select(p => $@"""{p}"""));
            string bgTarget = _isBackgroundMode && _targetFolder != null ? $@"""{_targetFolder}""" : "";
            string singlePath = files.Count > 0 ? $@"""{files[0]}""" : bgTarget;

            // Use _isBackgroundMode to decide single vs multi path
            if (_isBackgroundMode && cmdId >= CmdIdCompressSeparate && cmdId <= CmdIdCompress)
            {
                // Background mode: use _targetFolder as the single argument
                singlePath = bgTarget;
                paths = bgTarget;
                ShellExtLog.Info($"InvokeCommand: background mode, using targetFolder as path");
            }

            ShellExtLog.Info($"InvokeCommand: usingSharedList={usingSharedList}, files.Count={files.Count}, _isBackgroundMode={_isBackgroundMode}, singlePath=\"{singlePath}\", paths.Length={paths.Length}");

            switch (cmdId)
            {
                case CmdIdOpen:
                    cmdLine = $"--open {singlePath}";
                    break;
                case CmdIdExtractHere:
                    cmdLine = $"--extract-here {paths}";
                    break;
                case CmdIdSmartExtract:
                    cmdLine = $"--extract-smart {paths}";
                    break;
                case CmdIdExtractToNamed:
                    cmdLine = $"--extract-to-name {paths}";
                    break;
                case CmdIdExtractTo:
                    cmdLine = $"--extract {paths}";
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
                    ShellExtLog.Warn($"InvokeCommand: unknown cmdId={cmdId}, returning E_FAIL");
                    return NativeMethods.E_FAIL;
            }

            ShellExtLog.Info($"InvokeCommand: starting process: \"{exePath}\" {cmdLine}");
            ShellExtLog.Info($"InvokeCommand: cmdLine length={cmdLine.Length}, pathCount={files.Count}, firstPath={(files.Count > 0 ? files[0] : "(none)")}");
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = cmdLine,
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            Process.Start(psi);
            ShellExtLog.Info("InvokeCommand: Process.Start succeeded, returning S_OK");
            return NativeMethods.S_OK;
        }
        catch (Exception ex)
        {
            ShellExtLog.Error("IContextMenu.InvokeCommand exception", ex);
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region IContextMenu.GetCommandString

    public int GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved, StringBuilder commandString, int cch)
    {
        int offset = (int)idCmd;
        string flagName = uFlags switch
        {
            NativeMethods.GCS_HELPTEXTA or NativeMethods.GCS_HELPTEXTW => "GCS_HELPTEXT",
            NativeMethods.GCS_VERBA or NativeMethods.GCS_VERBW => "GCS_VERB",
            _ => $"0x{uFlags:x8}"
        };
        ShellExtLog.Info($"IContextMenu.GetCommandString #{_instanceId} entered, offset={offset}, uFlags={flagName}, cch={cch}");
        try
        {
            if (uFlags != NativeMethods.GCS_HELPTEXTA && uFlags != NativeMethods.GCS_HELPTEXTW &&
                uFlags != NativeMethods.GCS_VERBA && uFlags != NativeMethods.GCS_VERBW)
            {
                ShellExtLog.Warn($"GetCommandString: unsupported uFlags=0x{uFlags:x8}, returning E_NOTIMPL");
                return NativeMethods.E_NOTIMPL;
            }

            if (offset < 0 || offset >= _cmdIdOrder.Count)
            {
                ShellExtLog.Warn($"GetCommandString: offset {offset} out of range [0, {_cmdIdOrder.Count}), returning E_FAIL");
                return NativeMethods.E_FAIL;
            }
            int cmdId = _cmdIdOrder[offset];
            if (cmdId < 0)
            {
                ShellExtLog.Warn($"GetCommandString: cmdId={cmdId} is not a valid command (separator placeholder at offset {offset}), returning E_FAIL");
                return NativeMethods.E_FAIL;
            }

            string text;

            if (uFlags == NativeMethods.GCS_HELPTEXTA || uFlags == NativeMethods.GCS_HELPTEXTW)
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
            {
                ShellExtLog.Warn($"GetCommandString: no text for cmdId={cmdId}, returning E_FAIL");
                return NativeMethods.E_FAIL;
            }

            if (commandString != null && cch > 0)
            {
                commandString.Clear();
                commandString.Append(text);
                ShellExtLog.Info($"GetCommandString: returning text=\"{text}\" (length={text.Length}, cch={cch})");
            }
            else
            {
                ShellExtLog.Warn($"GetCommandString: commandString is null or cch={cch} <= 0");
            }

            return NativeMethods.S_OK;
        }
        catch (Exception ex)
        {
            ShellExtLog.Error("IContextMenu.GetCommandString exception", ex);
            return NativeMethods.E_FAIL;
        }
    }

    #endregion

    #region IContextMenu2 / IContextMenu3

    /// <summary>
    /// HandleMenuMsg — forwards menu window messages (WM_INITMENUPOPUP, WM_DRAWITEM,
    /// WM_MEASUREITEM, WM_MENUSELECT) from Explorer. By implementing this interface,
    /// Explorer properly routes InvokeCommand for submenu items.
    ///
    /// Without IContextMenu2/3, Explorer's submenu InvokeCommand routing breaks when
    /// >16 files are selected. GetCommandString(offset=0) for the first submenu item
    /// kills all subsequent InvokeCommand dispatch. Implementing these interfaces
    /// tells Explorer to use the proper message-based routing instead.
    /// </summary>
    public int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        ShellExtLog.Info($"IContextMenu2.HandleMenuMsg #{_instanceId}: uMsg=0x{uMsg:x8}");
        // We don't use owner-draw, so WM_DRAWITEM/WM_MEASUREITEM are irrelevant.
        // WM_INITMENUPOPUP is also a no-op since our submenu items are always
        // enabled. The mere presence of this interface is sufficient to fix the
        // submenu routing issue.
        return NativeMethods.S_OK;
    }

    /// <summary>
    /// HandleMenuMsg2 — extended version of HandleMenuMsg with result pointer.
    /// </summary>
    public int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult)
    {
        ShellExtLog.Info($"IContextMenu3.HandleMenuMsg2 #{_instanceId}: uMsg=0x{uMsg:x8}");
        plResult = IntPtr.Zero;
        return NativeMethods.S_OK;
    }

    #endregion

    #region Private helpers

    // Per-command icon cache: one HBITMAP per icon, loaded on first use.
    private static IntPtr _cachedIconOpen = IntPtr.Zero;
    private static IntPtr _cachedIconExtract = IntPtr.Zero;            // parent submenu: 打开/解压
    private static IntPtr _cachedIconCompress = IntPtr.Zero;           // parent submenu: 压缩
    private static IntPtr _cachedIconExtractHere = IntPtr.Zero;
    private static IntPtr _cachedIconSmartExtract = IntPtr.Zero;
    private static IntPtr _cachedIconExtractToNamed = IntPtr.Zero;
    private static IntPtr _cachedIconExtractTo = IntPtr.Zero;
    private static IntPtr _cachedIconCompressSeparate = IntPtr.Zero;
    private static IntPtr _cachedIconCompressCombined = IntPtr.Zero;
    private static IntPtr _cachedIconCompressDialog = IntPtr.Zero;

    /// <summary>
    /// Returns the effective file list for command execution.
    /// When Explorer creates a second COM instance with the full file list
    /// (because Initialize was truncated to 16 files), we borrow that list
    /// via the shared static field.
    /// </summary>
    private List<string> GetEffectiveFiles(out int originalCount)
    {
        originalCount = _selectedFiles.Count;
        if (originalCount <= 0)
            return _selectedFiles;

        // If our list seems truncated and a fuller list is available, use it
        List<string>? shared = null;
        lock (_fileListLock)
        {
            shared = _fullFileList;
        }

        if (shared != null && shared.Count > originalCount && shared.Take(originalCount).SequenceEqual(_selectedFiles, StringComparer.OrdinalIgnoreCase))
        {
            ShellExtLog.Info($"GetEffectiveFiles: instance #{_instanceId} resolved {shared.Count} files from shared list (original had {originalCount})");
            return shared;
        }

        return _selectedFiles;
    }

    private void InsertMenuItem(IntPtr hMenu, uint position, uint id, string text, int commandId, bool showIcon = true, IntPtr hSubMenu = default, IntPtr hbmpOverride = default)
    {
        string subInfo = hSubMenu != default ? $" (submenu)" : "";
        ShellExtLog.Info($"InsertMenuItem: text=\"{text}\", position={position}, id={id}, commandId={commandId}, showIcons={_showIcons}, showIcon={showIcon}, hbmpOverride=0x{hbmpOverride:x16}{subInfo}");
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

        // If this is a popup submenu item, set the submenu handle
        if (hSubMenu != default)
        {
            mii.fMask |= NativeMethods.MIIM_SUBMENU;
            mii.hSubMenu = hSubMenu;
        }

        // Add icon based on command type if enabled
        if (showIcon && _showIcons)
        {
            IntPtr hbmp = hbmpOverride != default ? hbmpOverride : (commandId >= 0 ? GetIconForCommand(commandId) : IntPtr.Zero);
            if (hbmp != IntPtr.Zero)
            {
                mii.fMask |= NativeMethods.MIIM_BITMAP;
                mii.hbmpItem = hbmp;
                ShellExtLog.Info($"InsertMenuItem: set MIIM_BITMAP, hbmpItem=0x{hbmp:x16}");
            }
        }
        else
        {
            ShellExtLog.Info("InsertMenuItem: icons disabled or showIcon=false, skipping MIIM_BITMAP");
        }

        bool result = NativeMethods.InsertMenuItem(hMenu, position, true, ref mii);
        if (!result)
            ShellExtLog.Warn($"InsertMenuItem: InsertMenuItem returned false (failed) for text=\"{text}\"");

        // Free the string memory we allocated
        Marshal.FreeCoTaskMem(mii.dwTypeData);
    }

    /// <summary>
    /// Return the cached HBITMAP for a given command ID, loading from embedded resource if needed.
    /// Each command has its own icon (per-command, not shared).
    /// </summary>
    private IntPtr GetIconForCommand(int commandId)
    {
        return commandId switch
        {
            CmdIdOpen => GetOrLoadIcon("Open.ico", ref _cachedIconOpen),
            CmdIdExtractHere => GetOrLoadIcon("ExtractHere.ico", ref _cachedIconExtractHere),
            CmdIdSmartExtract => GetOrLoadIcon("ExtractSmart.ico", ref _cachedIconSmartExtract),
            CmdIdExtractToNamed => GetOrLoadIcon("ExtractToNamed.ico", ref _cachedIconExtractToNamed),
            CmdIdExtractTo => GetOrLoadIcon("ExtractTo.ico", ref _cachedIconExtractTo),
            CmdIdCompressSeparate => GetOrLoadIcon("CompressSeparate.ico", ref _cachedIconCompressSeparate),
            CmdIdCompressCombined => GetOrLoadIcon("CompressCombined.ico", ref _cachedIconCompressCombined),
            CmdIdCompress => GetOrLoadIcon("CompressDialog.ico", ref _cachedIconCompressDialog),
            _ => IntPtr.Zero
        };
    }

    /// <summary>Return the cached HBITMAP for a parent submenu icon (Extract.ico / Compress.ico).</summary>
    private IntPtr GetParentIcon(bool isExtract)
    {
        return isExtract
            ? GetOrLoadIcon("Extract.ico", ref _cachedIconExtract)
            : GetOrLoadIcon("Compress.ico", ref _cachedIconCompress);
    }

    /// <summary>Load icon from embedded resource or return cached HBITMAP.</summary>
    private static IntPtr GetOrLoadIcon(string resourceName, ref IntPtr cache)
    {
        if (cache != IntPtr.Zero)
            return cache;
        cache = LoadIconFromResource(resourceName);
        if (cache != IntPtr.Zero)
            ShellExtLog.Info($"LoadIcon: loaded \"{resourceName}\" as hbmp=0x{cache:x16}");
        else
            ShellExtLog.Warn($"LoadIcon: failed to load \"{resourceName}\"");
        return cache;
    }

    /// <summary>
    /// Load a 16x16 .ico from embedded resource and convert to HBITMAP.
    /// Uses Win32 APIs (no System.Drawing dependency).
    /// </summary>
    private static IntPtr LoadIconFromResource(string resourceName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string? fullName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceName));
            if (fullName == null)
                return IntPtr.Zero;

            using var stream = assembly.GetManifestResourceStream(fullName);
            if (stream == null)
                return IntPtr.Zero;

            // Read the entire .ico file into memory
            byte[] icoBytes = new byte[stream.Length];
            stream.ReadExactly(icoBytes, 0, icoBytes.Length);

            // Parse .ico header to find the 16x16 image data.
            // .ico format: 6-byte header, then N 16-byte directory entries, then raw image data.
            // ICONDIR: reserved(2) + type(2) + count(2)
            int count = icoBytes[4] | (icoBytes[5] << 8);
            int bestIdx = -1;
            int bestWidth = int.MaxValue;

            for (int i = 0; i < count; i++)
            {
                int entryOff = 6 + i * 16;
                int w = icoBytes[entryOff];
                int h = icoBytes[entryOff + 1];
                // In .ico, a width/height of 0 means 256 pixels
                int iw = w == 0 ? 256 : w;
                int ih = h == 0 ? 256 : h;
                // Find closest to 16x16 (prefer exact match)
                int dist = Math.Abs(iw - 16) + Math.Abs(ih - 16);
                if (dist < bestWidth)
                {
                    bestWidth = dist;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
                return IntPtr.Zero;

            int bestEntryOff = 6 + bestIdx * 16;
            int imgSize = BitConverter.ToInt32(icoBytes, bestEntryOff + 8);
            int imgOff = BitConverter.ToInt32(icoBytes, bestEntryOff + 12);

            // Extract raw image data for the chosen entry
            byte[] imgData = new byte[imgSize];
            Array.Copy(icoBytes, imgOff, imgData, 0, imgSize);

            // Create HICON from the raw image data
            IntPtr hIcon = NativeMethods.CreateIconFromResourceEx(
                imgData, (uint)imgSize, true, 0x00030000,
                16, 16, 0);
            if (hIcon == IntPtr.Zero)
            {
                ShellExtLog.Warn($"LoadIconFromResource: CreateIconFromResourceEx returned null for \"{resourceName}\"");
                return IntPtr.Zero;
            }

            // Convert HICON → HBITMAP via GDI
            IntPtr hbmp = ConvertIconToBitmap(hIcon);
            NativeMethods.DestroyIcon(hIcon);
            return hbmp;
        }
        catch (Exception ex)
        {
            ShellExtLog.Warn($"LoadIconFromResource: exception loading \"{resourceName}\": {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Convert an HICON to an HBITMAP with alpha channel using a 32-bit DIB section.
    /// </summary>
    private static IntPtr ConvertIconToBitmap(IntPtr hIcon)
    {
        var bmi = new NativeMethods.BitmapInfo
        {
            bmiHeader = new NativeMethods.BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                biWidth = 16,
                biHeight = -16, // top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            }
        };

        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr hbmp = NativeMethods.CreateDIBSection(hdcScreen, ref bmi, NativeMethods.DIB_RGB_COLORS, out _, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            return IntPtr.Zero;
        }

        IntPtr hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
        IntPtr hOld = NativeMethods.SelectObject(hdcMem, hbmp);
        NativeMethods.DrawIconEx(hdcMem, 0, 0, hIcon, 16, 16, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
        NativeMethods.SelectObject(hdcMem, hOld);
        NativeMethods.DeleteDC(hdcMem);
        NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
        return hbmp;
    }

    /// <summary>Clean up all cached icon bitmaps. Called at the start of each QueryContextMenu.</summary>
    private void CleanupIconCache()
    {
        foreach (var kv in new[] {
            new { Name = "Open",             Hbmp = _cachedIconOpen },
            new { Name = "Extract (parent)", Hbmp = _cachedIconExtract },
            new { Name = "Compress (parent)",Hbmp = _cachedIconCompress },
            new { Name = "ExtractHere",      Hbmp = _cachedIconExtractHere },
            new { Name = "SmartExtract",     Hbmp = _cachedIconSmartExtract },
            new { Name = "ExtractToNamed",   Hbmp = _cachedIconExtractToNamed },
            new { Name = "ExtractTo",        Hbmp = _cachedIconExtractTo },
            new { Name = "CompressSeparate", Hbmp = _cachedIconCompressSeparate },
            new { Name = "CompressCombined", Hbmp = _cachedIconCompressCombined },
            new { Name = "CompressDialog",   Hbmp = _cachedIconCompressDialog },
        })
        {
            if (kv.Hbmp != IntPtr.Zero)
            {
                ShellExtLog.Info($"CleanupIconCache: deleting {kv.Name} hbmp=0x{kv.Hbmp:x16}");
                bool deleted = NativeMethods.DeleteObject(kv.Hbmp);
                ShellExtLog.Info($"CleanupIconCache: DeleteObject returned {deleted}");
            }
        }
        _cachedIconOpen = IntPtr.Zero;
        _cachedIconExtract = IntPtr.Zero;
        _cachedIconCompress = IntPtr.Zero;
        _cachedIconExtractHere = IntPtr.Zero;
        _cachedIconSmartExtract = IntPtr.Zero;
        _cachedIconExtractToNamed = IntPtr.Zero;
        _cachedIconExtractTo = IntPtr.Zero;
        _cachedIconCompressSeparate = IntPtr.Zero;
        _cachedIconCompressCombined = IntPtr.Zero;
        _cachedIconCompressDialog = IntPtr.Zero;
    }

    /// <summary>Read settings from HKCU\Software\MantisZip\ContextMenu (set by AppSettings sync).</summary>
    private void LoadSettingsFromRegistry()
    {
        ShellExtLog.Info("LoadSettingsFromRegistry: opening HKCU\\Software\\MantisZip\\ContextMenu");
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\MantisZip\ContextMenu", writable: false);
            if (key == null)
            {
                ShellExtLog.Warn("LoadSettingsFromRegistry: registry key not found, using defaults (all enabled)");
                return; // use defaults
            }

            _showIcons = ReadDword(key, "ShowMenuIcons", 1) != 0;
            _enableOpen = ReadDword(key, "EnableOpenMenu", 1) != 0;
            _enableExtractHere = ReadDword(key, "EnableExtractHereMenu", 1) != 0;
            _enableSmartExtract = ReadDword(key, "EnableSmartExtractMenu", 1) != 0;
            _enableExtractToNamed = ReadDword(key, "EnableExtractToNamedMenu", 1) != 0;
            _enableExtractTo = ReadDword(key, "EnableExtractToMenu", 1) != 0;
            _enableCompressSeparate = ReadDword(key, "EnableCompressSeparate", 1) != 0;
            _enableCompressCombined = ReadDword(key, "EnableCompressCombined", 1) != 0;
            _enableCompress = ReadDword(key, "EnableCompressMenu", 1) != 0;

            // Read localized menu text (fall back to hardcoded defaults)
            _textOpen = ReadString(key, "TextOpen", TextOpen);
            _textExtractHereSingle = ReadString(key, "TextExtractHereSingle", TextExtractHereSingle);
            _textExtractHereMulti = ReadString(key, "TextExtractHereMulti", TextExtractHereMulti);
            _textSmartExtractSingle = ReadString(key, "TextSmartExtractSingle", TextSmartExtractSingle);
            _textSmartExtractMulti = ReadString(key, "TextSmartExtractMulti", TextSmartExtractMulti);
            _textExtractToNamed = ReadString(key, "TextExtractToNamed", TextExtractToNamed);
            _textExtractTo = ReadString(key, "TextExtractTo", TextExtractTo);
            _textCompress = ReadString(key, "TextCompress", TextCompress);

            ShellExtLog.Info(
                $"LoadSettingsFromRegistry: showIcons={_showIcons}, " +
                $"open={_enableOpen}, extHere={_enableExtractHere}, smartExt={_enableSmartExtract}, " +
                $"extNamed={_enableExtractToNamed}, ext={_enableExtractTo}, " +
                $"compSep={_enableCompressSeparate}, compComb={_enableCompressCombined}, comp={_enableCompress}");
        }
        catch (Exception ex)
        {
            ShellExtLog.Error("LoadSettingsFromRegistry exception, using defaults", ex);
            // Use defaults
        }
    }

    private static int ReadDword(Microsoft.Win32.RegistryKey key, string name, int defaultValue)
    {
        var val = key.GetValue(name);
        if (val is int i) return i;
        return defaultValue;
    }

    private static string ReadString(Microsoft.Win32.RegistryKey key, string name, string defaultValue)
    {
        var val = key.GetValue(name);
        if (val is string s && !string.IsNullOrEmpty(s)) return s;
        return defaultValue;
    }

    /// <summary>
    /// Compute the common parent directory name from a list of file paths.
    /// Used for CompressCombined display text (e.g., "压缩到 2024.zip").
    /// Mirrors App.FindCommonParent from the UI project.
    /// Returns empty string if no common parent can be determined.
    /// </summary>
    private static string ComputeCommonParentName(List<string> paths)
    {
        if (paths.Count == 0) return "";

        try
        {
            var parents = paths.Select(p =>
            {
                var trimmed = p.TrimEnd('\\', '/');
                return Path.GetDirectoryName(trimmed) ?? "";
            }).ToList();

            if (parents.Any(string.IsNullOrEmpty)) return "";

            var common = parents[0];
            for (int i = 1; i < parents.Count; i++)
            {
                while (!parents[i].StartsWith(common, StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Path.GetDirectoryName(common);
                    if (parent == null) return "";
                    common = parent;
                }
            }

            // Check if result is a drive root (e.g., "C:\")
            if (common.Length == 3 && common[1] == ':') return "";

            return Path.GetFileName(common.TrimEnd('\\', '/'));
        }
        catch
        {
            return "";
        }
    }

    #endregion
}
