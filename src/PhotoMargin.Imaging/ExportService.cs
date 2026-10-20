using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class ExportService(ImageRenderer renderer)
{
    public async Task<string> ExportAsync(LoadedPhoto photo, LayoutResult layout, string destinationPath,
        ExportPreferences preferences, PhotoMetadata? metadata, string? rawPath,
        CancellationToken cancellationToken = default, FileStream? approvedOverwrite = null)
    {
        preferences = preferences.Validate();
        var destination = Path.GetFullPath(destinationPath);
        if (!string.Equals(Path.GetExtension(destination), preferences.Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("副檔名與輸出格式不一致。");
        if (layout.CanvasSize is < 320 or > 8192 || !layout.IsValid || PhotoMargin.Core.LayoutValidator.Validate(layout).Count != 0)
            throw new InvalidOperationException("輸出尺寸或文字安全區無效。");
        var protections = new List<FileStream>();
        string? temporary = null;
        try
        {
            foreach (var path in new[] { photo.SourcePath, rawPath }.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                if (string.Equals(Path.GetFullPath(path!), destination, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("禁止覆寫來源照片或 RAW。");
                try { protections.Add(new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.Read)); }
                catch (FileNotFoundException) when (path == rawPath) { }
                catch (DirectoryNotFoundException) when (path == rawPath) { }
            }
            foreach (var protection in protections) SourceFileProtection.ThrowIfSameFile(protection, destination);
            var bytes = await Task.Run(() => renderer.RenderExport(photo, layout, preferences, metadata, cancellationToken), cancellationToken).ConfigureAwait(false);
            var directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, ".photomargin-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            for (var number = 0; ; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = number == 0 ? destination : Path.Combine(directory, Path.GetFileNameWithoutExtension(destination) + $" ({number})" + preferences.Extension);
                if (new[] { photo.SourcePath, rawPath }.Any(path => !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetFullPath(path), candidate, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("禁止覆寫來源照片或 RAW。");
                foreach (var protection in protections) SourceFileProtection.ThrowIfSameFile(protection, candidate);
                try
                {
                    if (approvedOverwrite is not null)
                    {
                        SourceFileProtection.RequireSameFile(approvedOverwrite, candidate);
                        approvedOverwrite.Dispose();
                    }
                    File.Move(temporary, candidate, approvedOverwrite is not null || preferences.ConflictPolicy == FileConflictPolicy.Overwrite);
                    return candidate;
                }
                catch (IOException) when (preferences.ConflictPolicy == FileConflictPolicy.AutoNumber && File.Exists(candidate)) { }
            }
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            foreach (var protection in protections) protection.Dispose();
        }
    }

    public async Task ExportJpegAsync(
        LoadedPhoto photo,
        LayoutResult layout,
        string destinationPath,
        int quality,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var source = Path.GetFullPath(photo.SourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("禁止覆寫來源照片，請選擇其他檔名。");
        }

        // Hold the source without write/delete sharing through the final rename. Even if a
        // junction is changed after identity checking, Windows refuses to replace this file.
        using var sourceProtection = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        SourceFileProtection.ThrowIfSameFile(sourceProtection, destination);

        var bytes = await Task.Run(
            () => renderer.RenderJpeg(photo, layout, quality, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("輸出路徑無效。");
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(directory, ".photomargin-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            SourceFileProtection.ThrowIfSameFile(sourceProtection, destination);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
