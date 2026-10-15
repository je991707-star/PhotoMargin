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

        var bytes = await Task.Run(
            () => renderer.RenderJpeg(photo, layout, quality, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("輸出路徑無效。");
        Directory.CreateDirectory(directory);

        var temporary = destination + ".photomargin.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
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
