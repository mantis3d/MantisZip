using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// COM interface for OLE drag-drop IDropSource.
/// IID: 00000121-0000-0000-C000-000000000046
/// </summary>
[ComImport]
[Guid("00000121-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropSourceCom
{
    [PreserveSig]
    int QueryContinueDrag(int fEscapePressed, int grfKeyState);

    [PreserveSig]
    int GiveFeedback(int dwEffect);
}

/// <summary>
/// OLE IDropSource implementation for custom drag-drop operations.
/// Handles ESC to cancel, all mouse buttons released to drop,
/// and delegates cursor feedback to OLE defaults.
/// </summary>
[ComVisible(true)]
internal sealed class DropSource : IDropSourceCom
{
    // HRESULT constants
    private const int S_OK = 0;
    private const int DRAGDROP_S_CANCEL = 0x00040101;
    private const int DRAGDROP_S_DROP = 0x00040100;
    private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;

    // Mouse button key state masks
    private const int MK_LBUTTON = 0x0001;
    private const int MK_MBUTTON = 0x0010;
    private const int MK_RBUTTON = 0x0002;

    /// <summary>
    /// Called by OLE to determine whether to continue, cancel, or complete the drag operation.
    /// </summary>
    /// <param name="fEscapePressed">Non-zero if the ESC key has been pressed.</param>
    /// <param name="grfKeyState">Current state of the modifier keys and mouse buttons.</param>
    /// <returns>DRAGDROP_S_CANCEL if ESC pressed, DRAGDROP_S_DROP if all mouse buttons released, S_OK to continue.</returns>
    public int QueryContinueDrag(int fEscapePressed, int grfKeyState)
    {
        Debug.WriteLine($"[DropSource] QueryContinueDrag: esc={fEscapePressed}, keys=0x{grfKeyState:X}");

        // ESC pressed → cancel
        if (fEscapePressed != 0)
        {
            Debug.WriteLine("[DropSource] QueryContinueDrag -> CANCEL (ESC)");
            return DRAGDROP_S_CANCEL;
        }

        // No mouse buttons pressed → drop
        if ((grfKeyState & (MK_LBUTTON | MK_MBUTTON | MK_RBUTTON)) == 0)
        {
            Debug.WriteLine("[DropSource] QueryContinueDrag -> DROP (all buttons released)");
            return DRAGDROP_S_DROP;
        }

        // Continue dragging
        return S_OK;
    }

    /// <summary>
    /// Called by OLE to request cursor feedback during the drag operation.
    /// Always uses default OLE cursors.
    /// </summary>
    /// <param name="dwEffect">The drop effect value.</param>
    /// <returns>DRAGDROP_S_USEDEFAULTCURSORS to let OLE manage cursor display.</returns>
    public int GiveFeedback(int dwEffect)
    {
        Debug.WriteLine($"[DropSource] GiveFeedback: effect={dwEffect}");
        return DRAGDROP_S_USEDEFAULTCURSORS;
    }
}
