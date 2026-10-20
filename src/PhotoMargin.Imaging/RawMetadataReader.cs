using MetadataExtractor;
using MetadataExtractor.Util;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public interface IRawMetadataReader
{
    Task<PhotoMetadata> ReadAsync(string path, CancellationToken cancellationToken);
}

// Metadata-only parsing through the existing managed library. Never pass RAW to
// MagickImage/ImageLoader, and never copy its image geometry or color profile.
public sealed class RawMetadataReader : IRawMetadataReader
{
    public static IReadOnlyList<string> SupportedExtensions { get; } = Array.AsReadOnly(new[] { ".rw2" });
    public Task<PhotoMetadata> ReadAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("目前 RAW 資訊匯入僅開放 Panasonic RW2。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var registration = cancellationToken.Register(stream.Dispose);
        try
        {
            if (FileTypeDetector.DetectFileType(stream, path) != FileType.Rw2)
                throw new InvalidDataException("內容不是 Panasonic RW2；不能只更改副檔名。");
            stream.Position = 0;
            var metadata = ExifReader.ReadDirectories(ImageMetadataReader.ReadMetadata(stream, path), allowDateTimeFallback: false);
            cancellationToken.ThrowIfCancellationRequested();
            if (metadata == new PhotoMetadata()) throw new InvalidDataException("RAW 沒有可補入的拍攝資訊。");
            return metadata;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
    }, cancellationToken);
}
