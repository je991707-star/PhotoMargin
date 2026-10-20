using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PhotoMargin.App.Services;

// Listen on this desktop window only, without CoreWindow-only accessibility events.
internal sealed class WindowThemeListener : IDisposable
{
    private readonly IntPtr window;
    private readonly SubclassProc procedure;
    private const nuint Id = 0x504D5448;
    internal WindowThemeListener(IntPtr window, Action changed)
    {
        this.window = window;
        procedure = (hwnd, message, wparam, lparam, id, data) =>
        {
            if (message is 0x001A or 0x0015 or 0x031A) changed(); // settings/system-colors/theme
            return DefSubclassProc(hwnd, message, wparam, lparam);
        };
        if (!SetWindowSubclass(window, procedure, Id, UIntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    internal static bool IsHighContrast
    {
        get
        {
            var contrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
            return SystemParametersInfo(0x0042, contrast.Size, ref contrast, 0) && (contrast.Flags & 1) != 0;
        }
    }
    public void Dispose() { RemoveWindowSubclass(window, procedure, Id); GC.KeepAlive(procedure); }
    [StructLayout(LayoutKind.Sequential)] private struct HighContrast { public uint Size, Flags; public IntPtr DefaultScheme; }
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam, nuint id, UIntPtr data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrast contrast, uint flags);
}
