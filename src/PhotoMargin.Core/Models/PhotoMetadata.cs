using System.Globalization;

namespace PhotoMargin.Core.Models;

public sealed record PhotoMetadata
{
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? LensModel { get; init; }
    public double? FocalLengthMm { get; init; }
    public double? FNumber { get; init; }
    public double? ExposureSeconds { get; init; }
    public int? Iso { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public string? DateSource { get; init; }

    public string CameraDisplay => JoinNonEmpty(Make, Model);
    public string FocalLengthDisplay => FocalLengthMm is > 0 ? $"{FocalLengthMm:0.#}mm" : string.Empty;
    public string ApertureDisplay => FNumber is > 0 ? $"f/{FNumber:0.#}" : string.Empty;
    public string ShutterDisplay => FormatShutter(ExposureSeconds);
    public string IsoDisplay => Iso is > 0 ? $"ISO {Iso}" : string.Empty;

    private static string JoinNonEmpty(params string?[] values) =>
        string.Join(' ', values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string FormatShutter(double? seconds)
    {
        if (seconds is null or <= 0 || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
        {
            return string.Empty;
        }

        if (seconds.Value >= 1)
        {
            return $"{seconds.Value:0.#}s";
        }

        var reciprocal = 1d / seconds.Value;
        if (reciprocal < int.MaxValue)
        {
            var denominator = (int)Math.Round(reciprocal);
            if (denominator >= 2 && Math.Abs(1d / denominator - seconds.Value) <= seconds.Value * 0.01)
                return $"1/{denominator}s";
        }
        return seconds.Value.ToString("0.########", CultureInfo.InvariantCulture) + "s";
    }
}

public sealed class EditableMetadata
{
    public PhotoMetadata Original { get; private set; } = new();
    public string? Camera { get; set; }
    public string? Lens { get; set; }
    public string? FocalLength { get; set; }
    public string? Aperture { get; set; }
    public string? Shutter { get; set; }
    public string? Iso { get; set; }
    public string? Date { get; set; }

    public void LoadOriginal(PhotoMetadata metadata)
    {
        Original = metadata;
        Camera = Original.CameraDisplay;
        Lens = Original.LensModel?.Trim() ?? string.Empty;
        FocalLength = Original.FocalLengthDisplay;
        Aperture = Original.ApertureDisplay;
        Shutter = Original.ShutterDisplay;
        Iso = Original.IsoDisplay;
        Date = Original.CapturedAt?.ToString("yyyy.MM.dd") ?? string.Empty;
    }

    public DisplayMetadata ToDisplay() => new(
        Clean(Camera), Clean(Lens), Clean(FocalLength), Clean(Aperture),
        Clean(Shutter), Clean(Iso), Clean(Date));

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}

public sealed record DisplayMetadata(
    string Camera,
    string Lens,
    string FocalLength,
    string Aperture,
    string Shutter,
    string Iso,
    string Date);
