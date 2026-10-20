using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class ExifReader
{
    public PhotoMetadata Read(string path)
    {
        return ReadDirectories(ImageMetadataReader.ReadMetadata(path));
    }

    internal static PhotoMetadata ReadDirectories(IReadOnlyList<MetadataExtractor.Directory> directories, bool allowDateTimeFallback = true)
    {
        var roots = directories.Where(directory => directory is ExifIfd0Directory or PanasonicRawIfd0Directory).ToArray();
        var subs = directories.OfType<ExifSubIfdDirectory>().Cast<MetadataExtractor.Directory>().ToArray();
        var (capturedAt, source) = ReadDate(subs, allowDateTimeFallback ? roots : []);
        return new PhotoMetadata
        {
            Make = roots.Select(directory => Clean(directory.GetString(ExifDirectoryBase.TagMake))).FirstOrDefault(value => value is not null),
            Model = roots.Select(directory => Clean(directory.GetString(ExifDirectoryBase.TagModel))).FirstOrDefault(value => value is not null),
            LensModel = subs.Select(directory => Clean(directory.GetString(ExifDirectoryBase.TagLensModel))).FirstOrDefault(value => value is not null)
                ?? directories.OfType<PanasonicMakernoteDirectory>().Select(directory => directory.Tags.FirstOrDefault(tag => tag.Name == "Lens Type")?.Description)
                    .Select(Clean).FirstOrDefault(value => value is not null),
            FocalLengthMm = subs.Select(directory => ReadRational(directory, ExifDirectoryBase.TagFocalLength)).FirstOrDefault(value => value is not null),
            FNumber = subs.Select(directory => ReadRational(directory, ExifDirectoryBase.TagFNumber)).FirstOrDefault(value => value is not null),
            ExposureSeconds = subs.Select(directory => ReadRational(directory, ExifDirectoryBase.TagExposureTime)).FirstOrDefault(value => value is not null),
            Iso = subs.Select(directory => ReadInt(directory, ExifDirectoryBase.TagIsoEquivalent)).FirstOrDefault(value => value is not null),
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

    private static (DateTimeOffset? Value, string? Source) ReadDate(MetadataExtractor.Directory[] subs, MetadataExtractor.Directory[] roots)
    {
        var candidates = new (MetadataExtractor.Directory[] Directories, int Tag, string Source)[]
        {
            (subs, ExifDirectoryBase.TagDateTimeOriginal, "DateTimeOriginal"),
            (subs, ExifDirectoryBase.TagDateTimeDigitized, "DateTimeDigitized"),
            (roots, ExifDirectoryBase.TagDateTime, "DateTime")
        };

        foreach (var candidate in candidates)
        foreach (var directory in candidate.Directories)
        {
            var text = directory.GetString(candidate.Tag);
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
