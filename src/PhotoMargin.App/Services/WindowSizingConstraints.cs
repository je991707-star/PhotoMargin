using System.Runtime.InteropServices;

namespace PhotoMargin.App.Services;

// Native minimum tracking size; WinUI and Windows retain all normal caption/resize behavior.
internal sealed class WindowSizingConstraints : IDisposable
{
    private readonly IntPtr hwnd;
    private readonly SubclassProcedure callback;
    private static readonly UIntPtr Id = new(0x504D);
    public WindowSizingConstraints(IntPtr hwnd)
    {
        this.hwnd = hwnd; callback = WindowProcedure;
        if (!SetWindowSubclass(hwnd, callback, Id, UIntPtr.Zero)) throw new InvalidOperationException("無法設定視窗最小尺寸。");
    }
    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0024)
        {
            var constraints = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var scale = GetDpiForWindow(window) / 96d;
            constraints.MinTrack.X = Math.Max(constraints.MinTrack.X, (int)Math.Ceiling(576 * scale));
            constraints.MinTrack.Y = Math.Max(constraints.MinTrack.Y, (int)Math.Ceiling(420 * scale));
            Marshal.StructureToPtr(constraints, lParam, false);
        }
        return result;
    }
    public void Dispose() => RemoveWindowSubclass(hwnd, callback, Id);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr SubclassProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProcedure callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProcedure callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
