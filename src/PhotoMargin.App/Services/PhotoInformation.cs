using System.Globalization;
using System.Collections.Frozen;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

public enum InformationField { Camera, Lens, Date, Time, FocalLength, Aperture, Shutter, Iso }
public enum InformationOrigin { Missing, PhotoExif, Raw, Manual, FileCreation }
public enum RawMergeMode { FillMissing, OverwriteProvided }

// A confirmed per-field decision, independent from mutable dialog controls.
public sealed class RawInformationSelection(IEnumerable<InformationField> rawFields)
{
    public IReadOnlySet<InformationField> RawFields { get; } = rawFields.ToFrozenSet();
    public static RawInformationSelection FromMode(RawInformationReview review, RawMergeMode mode) => new(
        review.Differences.Where(row => !string.IsNullOrWhiteSpace(row.Incoming) &&
            (mode == RawMergeMode.OverwriteProvided || row.FillMissing)).Select(row => row.Field));
}

public sealed record PhotoInformationRow(InformationField Field, string Label, string Value, string Source);

// Typed date/time and separate field values: no orientation, color, dimensions,
// image bytes or whole EXIF/GPS payload can enter the RAW merge path.
public sealed record InformationValues
{
    public string Camera { get; init; } = "";
    public string Lens { get; init; } = "";
    public string FocalLength { get; init; } = "";
    public string Aperture { get; init; } = "";
    public string Shutter { get; init; } = "";
    public string Iso { get; init; } = "";
    public DateTimeOffset? Date { get; init; }
    public TimeSpan? Time { get; init; }
    public static InformationValues From(PhotoMetadata value) => new()
    {
        Camera = value.CameraDisplay, Lens = value.LensModel ?? "", FocalLength = value.FocalLengthDisplay,
        Aperture = value.ApertureDisplay, Shutter = value.ShutterDisplay, Iso = value.IsoDisplay,
        Date = value.CapturedAt, Time = value.CapturedAt?.TimeOfDay
    };
    public string Display(InformationField field) => field switch
    {
        InformationField.Camera => Camera, InformationField.Lens => Lens, InformationField.FocalLength => FocalLength,
        InformationField.Aperture => Aperture, InformationField.Shutter => Shutter, InformationField.Iso => Iso,
        InformationField.Date => Date?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "",
        InformationField.Time => Time is { } time ? $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}" : "", _ => ""
    };
    public InformationValues CopyField(InformationField field, InformationValues source) => field switch
    {
        InformationField.Camera => this with { Camera = source.Camera }, InformationField.Lens => this with { Lens = source.Lens },
        InformationField.FocalLength => this with { FocalLength = source.FocalLength }, InformationField.Aperture => this with { Aperture = source.Aperture },
        InformationField.Shutter => this with { Shutter = source.Shutter }, InformationField.Iso => this with { Iso = source.Iso },
        InformationField.Date => this with { Date = source.Date }, InformationField.Time => this with { Time = source.Time }, _ => this
    };
}

public sealed record RawInformationSnapshot(string SourcePath, PhotoMetadata Metadata,
    InformationValues AppliedValues, InformationField[] AppliedFields);
public sealed record WorkInformationSnapshot(string SourcePath, long Length, long LastWriteUtcTicks,
    PhotoMetadata PhotoExif, RawInformationSnapshot? Raw, InformationValues Current,
    Dictionary<InformationField, InformationOrigin> Origins);

public sealed record RawInformationDifference(InformationField Field, string Name, string Current, string Incoming, string Source, bool FillMissing);
public sealed class RawInformationReview
{
    internal RawInformationReview(string path, PhotoMetadata metadata, InformationValues current,
        IReadOnlyDictionary<InformationField, InformationOrigin> origins, PhotoMetadata photo, string photoFormat = "JPG")
    {
        SourcePath = path; Metadata = metadata; Incoming = InformationValues.From(metadata);
        Differences = Enum.GetValues<InformationField>().Select(field => new RawInformationDifference(field, Name(field),
            current.Display(field), Incoming.Display(field), origins.GetValueOrDefault(field) switch
            {
                InformationOrigin.PhotoExif or InformationOrigin.Missing => photoFormat,
                InformationOrigin.Raw => "RAW", InformationOrigin.FileCreation => photoFormat + " 建立日期（非 EXIF）",
                _ => OriginLabel(origins.GetValueOrDefault(field))
            },
            origins.GetValueOrDefault(field) != InformationOrigin.Manual &&
                (string.IsNullOrWhiteSpace(current.Display(field)) || origins.GetValueOrDefault(field) == InformationOrigin.FileCreation))).ToArray();
        MatchWarning = photo.CapturedAt is { } captured && metadata.CapturedAt is { } raw
            ? Math.Abs((captured.DateTime - raw.DateTime).TotalSeconds) > 120
                ? "注意：兩者的 EXIF 拍攝時間相差超過兩分鐘，可能不是同張照片。請自行核對後再套用。"
                : "拍攝時間接近仍不能證明是同張照片，請核對照片與 RAW。"
            : "缺少可比對的 EXIF 拍攝時間，無法確認是否為同張照片。請自行核對，不以檔名判定。";
    }
    public string SourcePath { get; }
    public PhotoMetadata Metadata { get; }
    public InformationValues Incoming { get; }
    public IReadOnlyList<RawInformationDifference> Differences { get; }
    public string MatchWarning { get; }
    public static string Name(InformationField field) => field switch
    {
        InformationField.Camera => "機身", InformationField.Lens => "鏡頭", InformationField.Date => "拍攝日期", InformationField.Time => "拍攝時間",
        InformationField.FocalLength => "焦距", InformationField.Aperture => "光圈", InformationField.Shutter => "快門", _ => "ISO"
    };
    public static string OriginLabel(InformationOrigin value) => value switch
    {
        InformationOrigin.PhotoExif => "照片 EXIF", InformationOrigin.Raw => "RAW 補入", InformationOrigin.Manual => "手動修改",
        InformationOrigin.FileCreation => "檔案建立日期（非 EXIF）", _ => "未提供"
    };
}
