using System.Runtime.InteropServices;

namespace PhotoMargin.App.Services;

public sealed record DataLocation(string DirectoryPath, string? LegacySettingsPath = null)
{
    private static readonly Lazy<DataLocation> Effective = new(() => Resolve(AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoMargin"), IsPackaged()));
    public static DataLocation Current => Effective.Value;
    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    public string PresetsPath => Path.Combine(DirectoryPath, "presets.json");
    public string BackupsPath => Path.Combine(DirectoryPath, "Backups");
    internal static DataLocation Resolve(string executableDirectory, string userDirectory, bool packaged,
        Func<string, bool>? canWrite = null)
    {
        var portable = Path.GetFullPath(Path.Combine(executableDirectory, "Data"));
        var fallback = Path.GetFullPath(Path.Combine(userDirectory, "Data"));
        // Choose once at startup, never in response to an ordinary save failure.
        var root = !packaged && (canWrite ?? ProbeWritable)(portable) ? portable : fallback;
        return new(root, Path.Combine(Path.GetFullPath(userDirectory), "settings.json"));
    }
    private static bool ProbeWritable(string directory)
    {
        string? probe = null;
        try
        {
            Directory.CreateDirectory(directory);
            probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0); stream.Flush(true); return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException error) when ((error.HResult & 0xffff) is 5 or 19) { return false; }
    }
    private static bool IsPackaged()
    {
        var length = 0u;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != 15700; // APPMODEL_ERROR_NO_PACKAGE
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
