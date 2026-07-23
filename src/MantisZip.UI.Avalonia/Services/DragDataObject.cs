using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using MantisZip.Core.Abstractions;

using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.Tar;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Custom COM IDataObject implementation for delayed-rendering drag-drop from archives.
/// Supports CFSTR_FILEDESCRIPTORW (FileGroupDescriptorW) and CFSTR_FILECONTENTSW (FileContents)
/// clipboard formats, enabling direct drag-drop from archive browsing into Explorer or other targets.
/// Implements the eager-extraction-on-demand pattern: content is extracted to memory only when
/// the drop target requests it via GetData/GetDataHere.
/// </summary>
[ComVisible(true)]
internal sealed class DragDataObject : IDataObject, IDisposable
{
    // ── Clipboard format registration ──

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClipboardFormatW(string lpszFormat);

    private static readonly short _cfFileDescriptor = (short)RegisterClipboardFormatW("FileGroupDescriptorW");
    private static readonly short _cfFileContents = (short)RegisterClipboardFormatW("FileContents");
    // CF_HDROP = 15 (predefined clipboard format, no registration needed)
    private const short CF_HDROP = 15;

    // ── HRESULT constants ──

    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_FAIL = unchecked((int)0x80004005);
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);
    private const int DV_E_TYMED = unchecked((int)0x80040069);
    private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
    private const int STG_E_MEDIUMFULL = unchecked((int)0x80030070);

    // ── FD_FLAGS (FILEDESCRIPTORW dwFlags) ──

    private const uint FD_UNICODE = 0x80000000;
    private const uint FD_ATTRIBUTES = 0x00000004;
    private const uint FD_FILESIZE = 0x00000040;
    private const uint FD_WRITESTIME = 0x00000020;

    // ── Win32 constants ──

    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    /// <summary>Size of one FILEDESCRIPTORW struct in bytes.</summary>
    private const int FileDescriptorSize = 592;

    // ── Fields ──

    private readonly IReadOnlyList<ArchiveItem> _items;
    private readonly string _archivePath;
    private readonly ArchiveFormat _format;
    private readonly string? _password;
    private bool _disposed;
    private string? _tempDir; // Created on first CF_HDROP GetData call, cleaned up in Dispose

    // ── Constructor ──

    /// <summary>
    /// Initializes a new instance of the <see cref="DragDataObject"/> class.
    /// </summary>
    /// <param name="items">Flat list of archive items (no directories, already expanded).</param>
    /// <param name="archivePath">Full path to the archive file.</param>
    /// <param name="format">Archive format enum.</param>
    /// <param name="password">Optional archive password.</param>
    public DragDataObject(
        IReadOnlyList<ArchiveItem> items,
        string archivePath,
        ArchiveFormat format,
        string? password)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
        _format = format;
        _password = password;
        App.DebugLog($"[DragDataObject] Created: {items.Count} items, format={format}, archive={archivePath}");
        App.DebugLog($"[DragDataObject] _cfFileDescriptor={_cfFileDescriptor}, _cfFileContents={_cfFileContents}");
        int cbsize = Marshal.SizeOf<FILEDESCRIPTORW>();
        App.DebugLog($"[DragDataObject] FILEDESCRIPTORW struct size: {cbsize} (expected 592)");
    }

    // ── IDisposable ──

    public void Dispose()
    {
        _disposed = true;
        // Clean up temp directory created for CF_HDROP
        if (_tempDir != null)
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
                App.DebugLog($"[DragDataObject] Dispose: cleaned up temp dir {_tempDir}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DragDataObject] Dispose: failed to clean up temp dir: {ex.Message}");
            }
            _tempDir = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  IDataObject implementation
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by OLE to retrieve data in a specified format.
    /// Allocates HGLOBAL for the requested format and returns it in the STGMEDIUM.
    /// </summary>
    public void GetData(ref FORMATETC formatEtc, out STGMEDIUM medium)
    {
        medium = new STGMEDIUM();

        if (_disposed)
            throw new COMException("Data object has been disposed.", DV_E_FORMATETC);

        int tymedInt = (int)formatEtc.tymed;

        App.DebugLog($"[DragDataObject] GetData: cfFormat={formatEtc.cfFormat}, tymed={tymedInt}, lindex={formatEtc.lindex}, aspect={formatEtc.dwAspect}");

        // ── CF_HDROP (predefined format 15) — Explorer expects this for file drags ──
        if (formatEtc.cfFormat == CF_HDROP)
        {
            App.DebugLog("[DragDataObject] GetData: returning CF_HDROP");
            if ((tymedInt & (int)TYMED.TYMED_HGLOBAL) == 0)
            {
                App.DebugLog($"[DragDataObject] GetData: invalid tymed for HDROP: {tymedInt}");
                throw new COMException("Invalid TYMED.", DV_E_TYMED);
            }

            var hGlobal = BuildHdrop();
            if (hGlobal == IntPtr.Zero)
            {
                App.DebugLog("[DragDataObject] GetData: BuildHdrop failed");
                throw new COMException("Failed to build HDROP.", E_FAIL);
            }

            medium.unionmember = hGlobal;
            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.pUnkForRelease = null;
            App.DebugLog($"[DragDataObject] GetData: CF_HDROP OK, tempDir={_tempDir}");
            return;
        }

        // ── FileGroupDescriptorW ──
        if (formatEtc.cfFormat == _cfFileDescriptor)
        {
            App.DebugLog("[DragDataObject] GetData: returning FileGroupDescriptorW");
            if ((tymedInt & (int)TYMED.TYMED_HGLOBAL) == 0)
            {
                App.DebugLog($"[DragDataObject] GetData: invalid tymed for FGD: {tymedInt}");
                throw new COMException("Invalid TYMED.", DV_E_TYMED);
            }

            var hGlobal = BuildFileGroupDescriptor();
            if (hGlobal == IntPtr.Zero)
            {
                App.DebugLog("[DragDataObject] GetData: BuildFileGroupDescriptor failed");
                throw new COMException("Failed to build file group descriptor.", E_FAIL);
            }

            medium.unionmember = hGlobal;
            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.pUnkForRelease = null;
            App.DebugLog("[DragDataObject] GetData: FileGroupDescriptorW OK");
            return;
        }

        // ── FileContents ──
        if (formatEtc.cfFormat == _cfFileContents)
        {
            App.DebugLog($"[DragDataObject] GetData: returning FileContents for lindex={formatEtc.lindex}");
            if ((tymedInt & (int)TYMED.TYMED_HGLOBAL) == 0)
            {
                App.DebugLog($"[DragDataObject] GetData: invalid tymed for FC: {tymedInt}");
                throw new COMException("Invalid TYMED.", DV_E_TYMED);
            }

            int index = formatEtc.lindex;
            if (index < 0 || index >= _items.Count)
            {
                App.DebugLog($"[DragDataObject] GetData: invalid lindex={index}, items.Count={_items.Count}");
                throw new COMException("Invalid item index.", DV_E_FORMATETC);
            }

            var hGlobal = ExtractFileContentToHGlobal(index);
            if (hGlobal == IntPtr.Zero)
            {
                App.DebugLog($"[DragDataObject] GetData: ExtractFileContentToHGlobal failed for index={index}");
                throw new COMException("Failed to extract file content.", E_FAIL);
            }

            medium.unionmember = hGlobal;
            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.pUnkForRelease = null;
            App.DebugLog($"[DragDataObject] GetData: FileContents OK for index={index}");
            return;
        }

        App.DebugLog($"[DragDataObject] GetData: unsupported format {formatEtc.cfFormat}");
        throw new COMException("Format not supported.", DV_E_FORMATETC);
    }

    /// <summary>
    /// Called by OLE to retrieve data into a pre-allocated HGLOBAL.
    /// Same as GetData but writes into the caller's buffer.
    /// </summary>
    public void GetDataHere(ref FORMATETC formatEtc, ref STGMEDIUM medium)
    {
        if (_disposed)
            throw new COMException("Data object has been disposed.", DV_E_FORMATETC);

        // ── FileGroupDescriptorW ──
        if (formatEtc.cfFormat == _cfFileDescriptor)
        {
            if (medium.unionmember == IntPtr.Zero)
                throw new COMException("Invalid STGMEDIUM.", DV_E_FORMATETC);

            ThrowOnFailure(WriteFileGroupDescriptorToHGlobal(medium.unionmember));
            return;
        }

        // ── FileContents ──
        if (formatEtc.cfFormat == _cfFileContents)
        {
            int index = formatEtc.lindex;
            if (index < 0 || index >= _items.Count)
                throw new COMException("Invalid item index.", DV_E_FORMATETC);

            if (medium.unionmember == IntPtr.Zero)
                throw new COMException("Invalid STGMEDIUM.", DV_E_FORMATETC);

            ThrowOnFailure(WriteFileContentToHGlobal(index, medium.unionmember));
            return;
        }

        throw new COMException("Format not supported.", DV_E_FORMATETC);
    }

    /// <summary>Convert HRESULT to exception if not S_OK.</summary>
    private static void ThrowOnFailure(int hresult)
    {
        if (hresult < 0)
            throw new COMException("Operation failed.", hresult);
    }

    /// <summary>
    /// Checks whether the data object supports the specified format.
    /// </summary>
    public int QueryGetData(ref FORMATETC formatEtc)
    {
        if (_disposed)
            return DV_E_FORMATETC;

        // Validate aspect
        if (formatEtc.dwAspect != DVASPECT.DVASPECT_CONTENT)
            return DV_E_FORMATETC;

        int tymedInt = (int)formatEtc.tymed;

        // Check for our formats
        if (formatEtc.cfFormat == CF_HDROP)
        {
            int result = (tymedInt & (int)TYMED.TYMED_HGLOBAL) == 0 ? DV_E_TYMED : S_OK;
            App.DebugLog($"[DragDataObject] QueryGetData CF_HDROP: result=0x{result:X8}");
            return result;
        }

        if (formatEtc.cfFormat == _cfFileDescriptor)
        {
            int result = (tymedInt & (int)TYMED.TYMED_HGLOBAL) == 0 ? DV_E_TYMED : S_OK;
            App.DebugLog($"[DragDataObject] QueryGetData FGD: result=0x{result:X8}");
            return result;
        }

        if (formatEtc.cfFormat == _cfFileContents)
        {
            int result = (tymedInt & (int)TYMED.TYMED_HGLOBAL) == 0 ? DV_E_TYMED : S_OK;
            App.DebugLog($"[DragDataObject] QueryGetData FC: lindex={formatEtc.lindex}, result=0x{result:X8}");
            return result;
        }

        App.DebugLog($"[DragDataObject] QueryGetData: unsupported cfFormat={formatEtc.cfFormat}");
        return DV_E_FORMATETC;
    }

    /// <summary>
    /// Returns a canonical FORMATETC. Not implemented — copy input format to output.
    /// </summary>
    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        return E_NOTIMPL;
    }

    /// <summary>
    /// Sets data into the object. Not supported (read-only data source).
    /// Releases the storage medium if ownership is transferred.
    /// </summary>
    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
    {
        if (release)
        {
            // Release the storage medium (OLE transferred ownership to us)
            if (medium.pUnkForRelease != null)
            {
                Marshal.ReleaseComObject(medium.pUnkForRelease);
            }
            else if (medium.tymed == TYMED.TYMED_HGLOBAL && medium.unionmember != IntPtr.Zero)
            {
                NativeMethods.GlobalFree(medium.unionmember);
            }
        }

        throw new NotSupportedException("SetData is not supported on this data object.");
    }

    /// <summary>
    /// Enumerates the formats supported by this data object.
    /// Returns the two clipboard formats (FileGroupDescriptorW and FileContents).
    /// </summary>
    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        App.DebugLog($"[DragDataObject] EnumFormatEtc called: direction={direction}");

        if (direction != DATADIR.DATADIR_GET)
        {
            App.DebugLog("[DragDataObject] EnumFormatEtc: DATADIR_SET not supported");
            throw new COMException("Direction not supported.", E_NOTIMPL);
        }

        var formats = new[]
        {
            new FORMATETC
            {
                cfFormat = CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            },
            new FORMATETC
            {
                cfFormat = _cfFileDescriptor,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            },
            new FORMATETC
            {
                cfFormat = _cfFileContents,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            }
        };

        return new EnumFORMATETC(formats);
    }

    // ── DAdvise / DUnadvise / EnumDAdvise (stubs) ──

    public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
    {
        connection = 0;
        return OLE_E_ADVISENOTSUPPORTED;
    }

    public void DUnadvise(int connection)
    {
        throw new NotSupportedException("Advise connections are not supported.");
    }

    public int EnumDAdvise(out IEnumSTATDATA? enumAdvise)
    {
        enumAdvise = null;
        return E_NOTIMPL;
    }

    // ═══════════════════════════════════════════════════════════════
    //  FILEGROUPDESCRIPTOR serialization
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FILEDESCRIPTORW struct for manual marshaling.
    /// Layout matches the Win32 FILEDESCRIPTORW exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct FILEDESCRIPTORW
    {
        public uint dwFlags;
        public Guid clsid;
        public long sizel;       // SIZE struct: cx (int) | ((long)cy << 32)
        public long pointl;      // POINT struct: x (int) | ((long)y << 32)
        public uint dwFileAttributes;

        // FILETIME (3 × 8 bytes)
        public int ftCreationTimeLow;
        public int ftCreationTimeHigh;
        public int ftLastAccessTimeLow;
        public int ftLastAccessTimeHigh;
        public int ftLastWriteTimeLow;
        public int ftLastWriteTimeHigh;

        public uint nFileSizeHigh;
        public uint nFileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
    }

    /// <summary>
    /// Build a FILEGROUPDESCRIPTOR from the archive items and allocate it as HGLOBAL.
    /// </summary>
    private IntPtr BuildFileGroupDescriptor()
    {
        int count = _items.Count;
        int totalSize = 4 + count * FileDescriptorSize;
        App.DebugLog($"[DragDataObject] BuildFileGroupDescriptor: {count} items, totalSize={totalSize}");

        var hGlobal = NativeMethods.GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)totalSize);
        if (hGlobal == IntPtr.Zero)
            return IntPtr.Zero;

        var ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return IntPtr.Zero;
        }

        try
        {
            // Write cItems (DWORD) at offset 0
            Marshal.WriteInt32(ptr, 0, count);

            // Write each FILEDESCRIPTORW
            for (int i = 0; i < count; i++)
            {
                var item = _items[i];
                long fileTime = item.LastModified.ToFileTime();

                var desc = new FILEDESCRIPTORW
                {
                    dwFlags = FD_UNICODE | FD_ATTRIBUTES | FD_FILESIZE | FD_WRITESTIME,
                    clsid = Guid.Empty,
                    sizel = 0,
                    pointl = 0,
                    dwFileAttributes = FILE_ATTRIBUTE_NORMAL,
                    ftCreationTimeLow = 0,
                    ftCreationTimeHigh = 0,
                    ftLastAccessTimeLow = 0,
                    ftLastAccessTimeHigh = 0,
                    ftLastWriteTimeLow = (int)(fileTime & 0xFFFFFFFF),
                    ftLastWriteTimeHigh = (int)(fileTime >> 32),
                    nFileSizeHigh = (uint)((ulong)item.Size >> 32),
                    nFileSizeLow = (uint)((ulong)item.Size & 0xFFFFFFFF),
                    cFileName = item.Name
                };

                Marshal.StructureToPtr(desc, ptr + 4 + i * FileDescriptorSize, false);
            }
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }

    /// <summary>
    /// Write FILEGROUPDESCRIPTOR data into a pre-allocated HGLOBAL (for GetDataHere).
    /// </summary>
    private int WriteFileGroupDescriptorToHGlobal(IntPtr hGlobal)
    {
        var ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
            return E_FAIL;

        try
        {
            int count = _items.Count;
            nuint bufferSize = NativeMethods.GlobalSize(hGlobal);
            int requiredSize = 4 + count * FileDescriptorSize;

            if ((long)bufferSize < requiredSize)
                return STG_E_MEDIUMFULL;

            Marshal.WriteInt32(ptr, 0, count);

            for (int i = 0; i < count; i++)
            {
                var item = _items[i];
                long fileTime = item.LastModified.ToFileTime();

                var desc = new FILEDESCRIPTORW
                {
                    dwFlags = FD_UNICODE | FD_ATTRIBUTES | FD_FILESIZE | FD_WRITESTIME,
                    clsid = Guid.Empty,
                    sizel = 0,
                    pointl = 0,
                    dwFileAttributes = FILE_ATTRIBUTE_NORMAL,
                    ftCreationTimeLow = 0,
                    ftCreationTimeHigh = 0,
                    ftLastAccessTimeLow = 0,
                    ftLastAccessTimeHigh = 0,
                    ftLastWriteTimeLow = (int)(fileTime & 0xFFFFFFFF),
                    ftLastWriteTimeHigh = (int)(fileTime >> 32),
                    nFileSizeHigh = (uint)((ulong)item.Size >> 32),
                    nFileSizeLow = (uint)((ulong)item.Size & 0xFFFFFFFF),
                    cFileName = item.Name
                };

                Marshal.StructureToPtr(desc, ptr + 4 + i * FileDescriptorSize, false);
            }

            return S_OK;
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Content extraction
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Extract a single file entry's content to a byte array.
    /// Synchronous — called from OLE's GetData callback on the UI thread's message pump.
    /// </summary>
    private byte[] ExtractFileContent(int index)
    {
        var item = _items[index];
        App.DebugLog($"[DragDataObject] ExtractFileContent: index={index}, name={item.Name}, size={item.Size}, format={_format}");

        switch (_format)
        {
            case ArchiveFormat.Zip:
                return ExtractZipContent(item);

            case ArchiveFormat.SevenZip:
            case ArchiveFormat.Rar:
                return ExtractSevenZipContent(item);

            case ArchiveFormat.Tar:
            case ArchiveFormat.GZip:
                return ExtractTarGzContent(item);

            default:
                throw new NotSupportedException($"Format {_format} is not supported for drag-drop content extraction.");
        }
    }

    /// <summary>
    /// Extract content and return it as an HGLOBAL (for GetData).
    /// </summary>
    private IntPtr ExtractFileContentToHGlobal(int index)
    {
        try
        {
            byte[] data = ExtractFileContent(index);
            return AllocHGlobalAndCopy(data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DragDataObject] ExtractFileContent failed: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Write file content into a pre-allocated HGLOBAL (for GetDataHere).
    /// </summary>
    private int WriteFileContentToHGlobal(int index, IntPtr hGlobal)
    {
        try
        {
            byte[] data = ExtractFileContent(index);
            nuint bufferSize = NativeMethods.GlobalSize(hGlobal);

            if ((ulong)bufferSize < (ulong)data.Length)
                return STG_E_MEDIUMFULL;

            var ptr = NativeMethods.GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero)
                return E_FAIL;

            try
            {
                Marshal.Copy(data, 0, ptr, data.Length);
                return S_OK;
            }
            finally
            {
                NativeMethods.GlobalUnlock(hGlobal);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DragDataObject] WriteFileContent failed: {ex.Message}");
            return E_FAIL;
        }
    }

    // ── ZIP extraction ──

    private byte[] ExtractZipContent(ArchiveItem item)
    {
        using var fs = File.Open(_archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var archive = ArchiveFactory.OpenArchive(fs, new ReaderOptions
        {
            Password = _password ?? string.Empty
        });

        var entry = archive.Entries.FindEntry(item.FullPath);
        if (entry == null)
            throw new FileNotFoundException($"Entry not found in archive: {item.FullPath}");

        using var ms = new MemoryStream((int)(entry.Size > 0 ? entry.Size : 4096));
        entry.WriteTo(ms);
        return ms.ToArray();
    }

    // ── 7z / RAR extraction ──

    private byte[] ExtractSevenZipContent(ArchiveItem item)
    {
        using var extractor = string.IsNullOrEmpty(_password)
            ? new SharpSevenZip.SharpSevenZipExtractor(_archivePath)
            : new SharpSevenZip.SharpSevenZipExtractor(_archivePath, _password);

        var szEntry = extractor.ArchiveFileData.FirstOrDefault(
            e => ArchivePath.Normalize(e.FileName) == item.FullPath);

        if (szEntry.FileName == null)
            throw new FileNotFoundException($"Entry not found in archive: {item.FullPath}");

        using var ms = new MemoryStream((int)(szEntry.Size > 0 ? szEntry.Size : 4096));
        extractor.ExtractFile(szEntry.Index, ms);
        return ms.ToArray();
    }

    // ── Tar / Gz extraction ──

    private byte[] ExtractTarGzContent(ArchiveItem item)
    {
        using var fs = File.OpenRead(_archivePath);
        using var reader = TarReader.OpenReader(fs, new ReaderOptions
        {
            LookForHeader = true,
            Password = _password ?? string.Empty
        });

        while (reader.MoveToNextEntry())
        {
            if (reader.Entry == null)
                continue;

            if (reader.Entry.Key == item.FullPath)
            {
                using var ms = new MemoryStream((int)(reader.Entry.Size > 0 ? reader.Entry.Size : 4096));
                using var entryStream = reader.OpenEntryStream();
                entryStream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        throw new FileNotFoundException($"Entry not found in archive: {item.FullPath}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  CF_HDROP building (for compatibility with Explorer drop targets)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// DROPFILES structure for CF_HDROP format.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public int pFiles;  // Offset to file list from beginning of structure
        public int ptX;     // POINT x
        public int ptY;     // POINT y
        public int fNC;     // Is non-client area (BOOL)
        public int fWide;   // TRUE for Unicode paths (BOOL)
    }

    /// <summary>
    /// Build a CF_HDROP data block: extracts all items to a temp directory,
    /// then constructs a DROPFILES struct + null-terminated Unicode file paths.
    /// </summary>
    private IntPtr BuildHdrop()
    {
        try
        {
            // Create temp directory on first call
            if (_tempDir == null)
            {
                _tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DragDrop", Guid.NewGuid().ToString());
                Directory.CreateDirectory(_tempDir);
                App.DebugLog($"[DragDataObject] BuildHdrop: created temp dir {_tempDir}");
            }

            // Extract all items to temp directory
            var extractedPaths = new List<string>();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var outputPath = Path.Combine(_tempDir, SanitizeFileName(item.Name));
                try
                {
                    byte[] data = ExtractFileContent(i);
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(outputPath, data);
                    extractedPaths.Add(outputPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DragDataObject] BuildHdrop: failed to extract {item.Name}: {ex.Message}");
                }
            }

            if (extractedPaths.Count == 0)
            {
                App.DebugLog("[DragDataObject] BuildHdrop: no files extracted");
                return IntPtr.Zero;
            }

            // Build the DROPFILES data
            int dropfilesSize = Marshal.SizeOf<DROPFILES>();
            // Calculate total size: DROPFILES + null-terminated Unicode paths + final null terminator
            int pathsLength = 0;
            foreach (var path in extractedPaths)
                pathsLength += (path.Length + 1) * 2; // char count + null terminator * 2 bytes
            pathsLength += 2; // Final double null terminator

            int totalSize = dropfilesSize + pathsLength;
            var hGlobal = NativeMethods.GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)totalSize);
            if (hGlobal == IntPtr.Zero)
            {
                App.DebugLog("[DragDataObject] BuildHdrop: GlobalAlloc failed");
                return IntPtr.Zero;
            }

            var ptr = NativeMethods.GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(hGlobal);
                return IntPtr.Zero;
            }

            try
            {
                // Write DROPFILES structure
                var df = new DROPFILES
                {
                    pFiles = dropfilesSize, // Offset to file list
                    ptX = 0,
                    ptY = 0,
                    fNC = 0,
                    fWide = 1 // Unicode
                };
                Marshal.StructureToPtr(df, ptr, false);

                // Write null-terminated Unicode paths
                int offset = dropfilesSize;
                foreach (var path in extractedPaths)
                {
                    foreach (char c in path)
                    {
                        Marshal.WriteInt16(ptr, offset, (short)c);
                        offset += 2;
                    }
                    Marshal.WriteInt16(ptr, offset, 0); // null terminator
                    offset += 2;
                }
                Marshal.WriteInt16(ptr, offset, 0); // Final null terminator
            }
            finally
            {
                NativeMethods.GlobalUnlock(hGlobal);
            }

            App.DebugLog($"[DragDataObject] BuildHdrop: {extractedPaths.Count} files, totalSize={totalSize}");
            return hGlobal;
        }
        catch (Exception ex)
        {
            App.DebugLog($"[DragDataObject] BuildHdrop: exception: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    //  HGLOBAL helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Allocate an HGLOBAL (GMEM_MOVEABLE | GMEM_ZEROINIT), copy data into it,
    /// unlock, and return the handle.
    /// </summary>
    private static IntPtr AllocHGlobalAndCopy(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            // Return a zero-initialized block for empty files
            var hGlobal = NativeMethods.GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, 1);
            return hGlobal;
        }

        var hMem = NativeMethods.GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)data.Length);
        if (hMem == IntPtr.Zero)
            return IntPtr.Zero;

        var ptr = NativeMethods.GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hMem);
            return IntPtr.Zero;
        }

        try
        {
            Marshal.Copy(data, 0, ptr, data.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(hMem);
        }

        return hMem;
    }

    // ═══════════════════════════════════════════════════════════════
    //  IEnumFORMATETC implementation
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal IEnumFORMATETC implementation enumerating the supported clipboard formats.
    /// </summary>
    [ComVisible(true)]
    private sealed class EnumFORMATETC : IEnumFORMATETC
    {
        private readonly FORMATETC[] _formats;
        private int _current;

        public EnumFORMATETC(FORMATETC[] formats)
        {
            _formats = formats;
            _current = 0;
        }

        public void Clone(out IEnumFORMATETC enumerator)
        {
            enumerator = new EnumFORMATETC(_formats) { _current = _current };
        }

        public int Next(int celt, FORMATETC[] rgelt, int[]? pceltFetched)
        {
            int fetched = 0;

            for (int i = 0; i < celt && _current < _formats.Length; i++)
            {
                rgelt[i] = _formats[_current++];
                fetched++;
            }

            if (pceltFetched != null && pceltFetched.Length > 0)
                pceltFetched[0] = fetched;

            return fetched == celt ? S_OK : S_FALSE;
        }

        public int Reset()
        {
            _current = 0;
            return S_OK;
        }

        public int Skip(int celt)
        {
            _current = Math.Min(_current + celt, _formats.Length);
            return _current >= _formats.Length ? S_FALSE : S_OK;
        }
    }
}
