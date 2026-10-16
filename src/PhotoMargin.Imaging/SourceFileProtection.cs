using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhotoMargin.Imaging;

internal static class SourceFileProtection
{
    public static void ThrowIfSameFile(FileStream source, string destination)
    {
        FileStream target;
        try { target = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        using (target)
        {
            var first = Identity(source.SafeFileHandle);
            var second = Identity(target.SafeFileHandle);
            if (first == second) throw new InvalidOperationException("禁止覆寫來源照片，請選擇其他檔名。");
        }
    }

    private static FileIdentity Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 18 /* FileIdInfo */, out var info, (uint)Marshal.SizeOf<FileIdentity>()))
            throw new IOException("無法確認來源與輸出檔案身分，已停止匯出以保護原照片。", new Win32Exception(Marshal.GetLastWin32Error()));
        return info;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileIdentity(ulong Volume, ulong IdLow, ulong IdHigh);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out FileIdentity info, uint size);
}
