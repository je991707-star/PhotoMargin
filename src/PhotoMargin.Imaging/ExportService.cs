using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class ExportService(ImageRenderer renderer)
{
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
