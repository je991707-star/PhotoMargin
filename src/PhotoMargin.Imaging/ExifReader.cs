using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class ExifReader
{
    public PhotoMetadata Read(string path)
    {
        var directories = ImageMetadataReader.ReadMetadata(path).ToArray();
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var sub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        var (capturedAt, source) = ReadDate(sub, ifd0);
        return new PhotoMetadata
        {
            Make = Clean(ifd0?.GetString(ExifDirectoryBase.TagMake)),
            Model = Clean(ifd0?.GetString(ExifDirectoryBase.TagModel)),
            LensModel = Clean(sub?.GetString(ExifDirectoryBase.TagLensModel)),
            FocalLengthMm = ReadRational(sub, ExifDirectoryBase.TagFocalLength),
            FNumber = ReadRational(sub, ExifDirectoryBase.TagFNumber),
            ExposureSeconds = ReadRational(sub, ExifDirectoryBase.TagExposureTime),
            Iso = ReadInt(sub, ExifDirectoryBase.TagIsoEquivalent),
            CapturedAt = capturedAt,
            DateSource = source
        };
    }

    private static double? ReadRational(MetadataExtractor.Directory? directory, int tag)
    {
        if (directory is null)
        {
            return null;
        }

        try
        {
            var value = directory.GetRational(tag).ToDouble();
            return value > 0 && double.IsFinite(value) ? value : null;
        }
        catch (MetadataException)
        {
            return null;
        }
    }

    private static int? ReadInt(MetadataExtractor.Directory? directory, int tag)
    {
        if (directory is null)
        {
            return null;
        }

        try
        {
            var value = directory.GetInt32(tag);
            return value > 0 ? value : null;
        }
        catch (MetadataException)
        {
            return null;
        }
    }

    private static (DateTimeOffset? Value, string? Source) ReadDate(ExifSubIfdDirectory? sub, ExifIfd0Directory? ifd0)
    {
        var candidates = new (MetadataExtractor.Directory? Directory, int Tag, string Source)[]
        {
            (sub, ExifDirectoryBase.TagDateTimeOriginal, "DateTimeOriginal"),
            (sub, ExifDirectoryBase.TagDateTimeDigitized, "DateTimeDigitized"),
            (ifd0, ExifDirectoryBase.TagDateTime, "DateTime")
        };

        foreach (var candidate in candidates)
        {
            var text = candidate.Directory?.GetString(candidate.Tag);
            if (DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var parsed))
            {
                return (new DateTimeOffset(parsed), candidate.Source);
            }
        }

        return (null, null);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
