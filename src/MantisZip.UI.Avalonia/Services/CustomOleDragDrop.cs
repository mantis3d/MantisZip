using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;

namespace MantisZip.UI.Avalonia.Services;

// ─────────────────────────────────────────────────────────────────────────────
// 自实现 OLE 拖拽（方案 C 第一阶段，路线 2）
//
// 背景：Avalonia 的 OleDragSource.GiveFeedback 固定返回 DRAGDROP_S_USEDEFAULTCURSORS，
// 系统按 DROPEFFECT 用 LoadCursor(OCR_NO) 设置禁止光标 —— 而替换系统 OCR_NO 资源表
// 在本机无效（SetSystemCursor 返回成功但 LoadCursor 仍返回旧句柄，已实证）。
// 因此自实现 IDropSource：GiveFeedback 返回 S_OK 并直接 SetCursor 自定义光标，
// 光标完全由本应用控制，无全局副作用。Esc/鼠标按钮由 QueryContinueDrag 处理。
//
// 数据对象（IDataObject）仅提供自定义字符串格式（压缩包路径），Explorer 不识别
// 仍返回 DROPEFFECT_NONE —— 与原有行为一致（释放由全屏 overlay 拦截），变的只是光标。
// 未来完整方案 C：在此 IDataObject 上增加 CF_FILEDESCRIPTOR/CF_FILECONTENTS
// 即可让 Explorer 接受拖放并显示"复制"光标。
// ─────────────────────────────────────────────────────────────────────────────

internal static class OleDragConstants
{
    // HRESULT
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
    public const int DV_E_FORMATETC = unchecked((int)0x80040064);
    public const int DV_E_DVASPECT = unchecked((int)0x8004006B);
    public const int DV_E_TYMED = unchecked((int)0x80040069);
    public const int DRAGDROP_S_DROP = 0x00040100;
    public const int DRAGDROP_S_CANCEL = 0x00040101;

    // TYMED / DVASPECT / DATADIR / GMEM
    public const int TYMED_HGLOBAL = 1;
    public const int DVASPECT_CONTENT = 1;
    public const int DATADIR_GET = 1;
    public const uint GMEM_MOVEABLE = 0x0002;

    // DROPEFFECT
    public const int DROPEFFECT_NONE = 0;
    public const int DROPEFFECT_COPY = 1;
    public const int DROPEFFECT_MOVE = 2;
    public const int DROPEFFECT_LINK = 4;

    // grfKeyState 鼠标按钮掩码
    public const int MK_LBUTTON = 0x0001;
    public const int MK_RBUTTON = 0x0002;
    public const int MK_MBUTTON = 0x0010;
}

/// <summary>标准 OLE FORMATETC（lpformatetc）</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OleFormatEtc
{
    public ushort cfFormat;
    public nint ptd;
    public int dwAspect;
    public int lindex;
    public int tymed;
}

/// <summary>标准 OLE STGMEDIUM</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OleStgMedium
{
    public int tymed;
    public nint unionmember;
    public nint pUnkForRelease;
}

/// <summary>标准 OLE IDropSource（GUID 00000121-0000-0000-C000-000000000046）</summary>
[ComImport]
[Guid("00000121-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleDropSource
{
    [PreserveSig]
    int QueryContinueDrag(int fEscapePressed, int grfKeyState);

    [PreserveSig]
    int GiveFeedback(int dwEffect);
}

/// <summary>标准 OLE IDataObject（GUID 0000010E-0000-0000-C000-000000000046）</summary>
[ComImport]
[Guid("0000010E-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleDataObject
{
    [PreserveSig]
    int GetData(ref OleFormatEtc pformatetcIn, out OleStgMedium pmedium);

    [PreserveSig]
    int GetDataHere(ref OleFormatEtc pformatetc, ref OleStgMedium pmedium);

    [PreserveSig]
    int QueryGetData(ref OleFormatEtc pformatetc);

    [PreserveSig]
    int GetCanonicalFormatEtc(ref OleFormatEtc pformatetcIn, out OleFormatEtc pformatetcOut);

    [PreserveSig]
    int SetData(ref OleFormatEtc pformatetc, ref OleStgMedium pmedium, bool fRelease);

    [PreserveSig]
    int EnumFormatEtc(int dwDirection, out IOleEnumFormatEtc ppenumFormatetc);

    [PreserveSig]
    int DAdvise(ref OleFormatEtc pformatetc, int advf, nint pAdvSink, out int pdwConnection);

    [PreserveSig]
    int DUnadvise(int dwConnection);

    [PreserveSig]
    int EnumDAdvise(out nint ppenumAdvise);
}

/// <summary>标准 OLE IEnumFORMATETC（GUID 00000103-0000-0000-C000-000000000046）</summary>
[ComImport]
[Guid("00000103-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleEnumFormatEtc
{
    [PreserveSig]
    int Next(int celt,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] OleFormatEtc[] rgelt,
        out int pceltFetched);

    [PreserveSig]
    int Skip(int celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IOleEnumFormatEtc ppenum);
}

/// <summary>
/// 自定义 IDropSource：GiveFeedback 返回 S_OK 并自设光标（系统不再用默认光标覆盖），
/// QueryContinueDrag 处理 Esc / 右键取消 / 鼠标按钮释放（与 Avalonia OleDragSource 行为一致）。
/// 光标由调用方提供的 <see cref="Func{nint}"/> 在每次 GiveFeedback 时按当前状态动态获取，
/// 从而支持"状态 → 不同图标"（对应 overlay 的不同颜色）。
/// </summary>
internal sealed class CustomDropSource : IOleDropSource
{
    private readonly Func<nint> _cursorProvider;
    private readonly Action? _onCancelled;

    public CustomDropSource(Func<nint> cursorProvider, Action? onCancelled = null)
    {
        _cursorProvider = cursorProvider;
        _onCancelled = onCancelled;
    }

    public int QueryContinueDrag(int fEscapePressed, int grfKeyState)
    {
        if (fEscapePressed != 0)
        {
            _onCancelled?.Invoke();
            return OleDragConstants.DRAGDROP_S_CANCEL;
        }

        // 统计当前按下的鼠标按钮数
        int pressed = 0;
        if ((grfKeyState & OleDragConstants.MK_LBUTTON) != 0) pressed++;
        if ((grfKeyState & OleDragConstants.MK_RBUTTON) != 0) pressed++;
        if ((grfKeyState & OleDragConstants.MK_MBUTTON) != 0) pressed++;
        if (pressed >= 2)
        {
            // 左键拖拽中按下右键/中键 = 标准 OLE 取消手势 → 同样触发取消回调，
            // 与 Esc 分支一致，否则 MainWindow 会误以为拖拽成功而继续解压。
            _onCancelled?.Invoke();
            return OleDragConstants.DRAGDROP_S_CANCEL;
        }
        if (pressed == 0)
            return OleDragConstants.DRAGDROP_S_DROP;
        return OleDragConstants.S_OK;
    }

    public int GiveFeedback(int dwEffect)
    {
        var cursor = _cursorProvider();
        if (cursor != nint.Zero)
            NativeMethods.SetCursor(cursor);
        return OleDragConstants.S_OK; // S_OK = 禁止系统设置默认光标
    }
}

/// <summary>IEnumFORMATETC 实现：枚举单个自定义格式</summary>
internal sealed class CustomEnumFormatEtc : IOleEnumFormatEtc
{
    private readonly OleFormatEtc[] _formats;
    private int _index;

    public CustomEnumFormatEtc(OleFormatEtc[] formats)
    {
        _formats = formats;
    }

    public int Next(int celt, OleFormatEtc[] rgelt, out int pceltFetched)
    {
        pceltFetched = 0;
        if (rgelt == null || rgelt.Length == 0)
            return OleDragConstants.S_FALSE;
        while (pceltFetched < celt && _index < _formats.Length)
        {
            rgelt[pceltFetched] = _formats[_index];
            _index++;
            pceltFetched++;
        }
        return pceltFetched == celt ? OleDragConstants.S_OK : OleDragConstants.S_FALSE;
    }

    public int Skip(int celt)
    {
        _index = Math.Min(_formats.Length, _index + celt);
        return _index >= _formats.Length ? OleDragConstants.S_FALSE : OleDragConstants.S_OK;
    }

    public int Reset()
    {
        _index = 0;
        return OleDragConstants.S_OK;
    }

    public int Clone(out IOleEnumFormatEtc ppenum)
    {
        ppenum = new CustomEnumFormatEtc(_formats) { _index = _index };
        return OleDragConstants.S_OK;
    }
}

/// <summary>
/// 自定义 IDataObject：仅支持单个自定义字符串格式（HGLOBAL / UTF-16，含 NUL 结尾）。
/// 其余方法返回 E_NOTIMPL（OLE 允许）。GetData 延迟渲染，HGLOBAL 由 OLE 通过
/// ReleaseStgMedium 释放（GlobalFree），本类不持有。
/// </summary>
internal sealed class CustomDataObject : IOleDataObject
{
    private readonly ushort _cfFormat;
    private readonly string _value;

    public CustomDataObject(ushort cfFormat, string value)
    {
        _cfFormat = cfFormat;
        _value = value;
    }

    public int GetData(ref OleFormatEtc pformatetcIn, out OleStgMedium pmedium)
    {
        pmedium = default;
        if ((pformatetcIn.dwAspect & OleDragConstants.DVASPECT_CONTENT) == 0)
            return OleDragConstants.DV_E_DVASPECT;
        if ((pformatetcIn.tymed & OleDragConstants.TYMED_HGLOBAL) == 0)
            return OleDragConstants.DV_E_TYMED;
        if (pformatetcIn.cfFormat != _cfFormat)
            return OleDragConstants.DV_E_FORMATETC;

        var bytes = Encoding.Unicode.GetBytes(_value + "\0");
        var hGlobal = NativeMethods.GlobalAlloc(OleDragConstants.GMEM_MOVEABLE, (uint)bytes.Length);
        if (hGlobal == nint.Zero)
            return OleDragConstants.E_OUTOFMEMORY;

        var ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == nint.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return OleDragConstants.E_OUTOFMEMORY;
        }
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }

        pmedium.tymed = OleDragConstants.TYMED_HGLOBAL;
        pmedium.unionmember = hGlobal;
        pmedium.pUnkForRelease = nint.Zero; // OLE 按 HGLOBAL 用 GlobalFree 释放
        return OleDragConstants.S_OK;
    }

    public int GetDataHere(ref OleFormatEtc pformatetc, ref OleStgMedium pmedium)
        => OleDragConstants.E_NOTIMPL;

    public int QueryGetData(ref OleFormatEtc pformatetc)
    {
        if ((pformatetc.dwAspect & OleDragConstants.DVASPECT_CONTENT) == 0)
            return OleDragConstants.DV_E_DVASPECT;
        if ((pformatetc.tymed & OleDragConstants.TYMED_HGLOBAL) == 0)
            return OleDragConstants.DV_E_TYMED;
        if (pformatetc.cfFormat != _cfFormat)
            return OleDragConstants.DV_E_FORMATETC;
        return OleDragConstants.S_OK;
    }

    public int GetCanonicalFormatEtc(ref OleFormatEtc pformatetcIn, out OleFormatEtc pformatetcOut)
    {
        pformatetcOut = default;
        return OleDragConstants.E_NOTIMPL;
    }

    public int SetData(ref OleFormatEtc pformatetc, ref OleStgMedium pmedium, bool fRelease)
        => OleDragConstants.E_NOTIMPL;

    public int EnumFormatEtc(int dwDirection, out IOleEnumFormatEtc ppenumFormatetc)
    {
        ppenumFormatetc = null!;
        if (dwDirection != OleDragConstants.DATADIR_GET)
            return OleDragConstants.E_NOTIMPL;
        ppenumFormatetc = new CustomEnumFormatEtc(new[]
        {
            new OleFormatEtc
            {
                cfFormat = _cfFormat,
                ptd = nint.Zero,
                dwAspect = OleDragConstants.DVASPECT_CONTENT,
                lindex = -1,
                tymed = OleDragConstants.TYMED_HGLOBAL
            }
        });
        return OleDragConstants.S_OK;
    }

    public int DAdvise(ref OleFormatEtc pformatetc, int advf, nint pAdvSink, out int pdwConnection)
    {
        pdwConnection = 0;
        return OleDragConstants.E_NOTIMPL;
    }

    public int DUnadvise(int dwConnection) => OleDragConstants.E_NOTIMPL;

    public int EnumDAdvise(out nint ppenumAdvise)
    {
        ppenumAdvise = nint.Zero;
        return OleDragConstants.E_NOTIMPL;
    }
}

/// <summary>
/// 自实现 OLE 拖拽入口。替代 Avalonia 的 DragDrop.DoDragDropAsync（其内部 OleDragSource
/// 固定返回 USEDEFAULTCURSORS，导致禁止光标无法替换）。拖拽期间光标由 GiveFeedback 控制。
/// </summary>
internal static class CustomOleDragDrop
{
    /// <param name="triggerEvent">触发拖拽的鼠标按下事件（用于释放 Pointer 捕获）</param>
    /// <param name="formatName">自定义剪贴板格式名</param>
    /// <param name="value">格式载荷（压缩包路径，UTF-16）</param>
    /// <param name="cursorProvider">拖拽期间按当前状态返回光标句柄的函数（由调用方加载/销毁）。每次 GiveFeedback 调用一次</param>
    /// <param name="onCancelled">拖拽被取消（按 Esc 或左键拖拽中按下右键/中键）时回调（QueryContinueDrag 同步触发，早于返回）</param>
    public static DragDropEffects PerformDragDrop(
        PointerPressedEventArgs triggerEvent,
        string formatName,
        string value,
        Func<nint> cursorProvider,
        DragDropEffects allowedEffects,
        Action? onCancelled)
    {
        triggerEvent.Pointer.Capture(null);

        var cf = NativeMethods.RegisterClipboardFormatW(formatName);
        if (cf == 0)
            return DragDropEffects.None;

        var dataObj = new CustomDataObject(cf, value);
        var dropSource = new CustomDropSource(cursorProvider, onCancelled);

        var objPtr = Marshal.GetComInterfaceForObject(dataObj, typeof(IOleDataObject));
        var srcPtr = Marshal.GetComInterfaceForObject(dropSource, typeof(IOleDropSource));
        try
        {
            int allowed = allowedEffects switch
            {
                DragDropEffects.Copy => OleDragConstants.DROPEFFECT_COPY,
                DragDropEffects.Move => OleDragConstants.DROPEFFECT_MOVE,
                DragDropEffects.Link => OleDragConstants.DROPEFFECT_LINK,
                _ => OleDragConstants.DROPEFFECT_COPY
            };

            // 阻塞模态循环（OLE 内部派发消息），与 Avalonia DoDragDropAsync 行为一致
            var hr = NativeMethods.DoDragDrop(objPtr, srcPtr, allowed, out int effect);
            if (hr < 0)
                return DragDropEffects.None;

            return effect switch
            {
                OleDragConstants.DROPEFFECT_COPY => DragDropEffects.Copy,
                OleDragConstants.DROPEFFECT_MOVE => DragDropEffects.Move,
                OleDragConstants.DROPEFFECT_LINK => DragDropEffects.Link,
                _ => DragDropEffects.None
            };
        }
        finally
        {
            Marshal.Release(objPtr);
            Marshal.Release(srcPtr);
        }
    }
}
