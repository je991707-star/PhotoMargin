namespace PhotoMargin.Core.Models;

public enum ExportFormat { Jpg, Png, WebP }
public enum FileConflictPolicy { AutoNumber, Ask, Overwrite }

public sealed record ExportPreferences
{
    public ExportFormat Format { get; init; }
    public int JpegQuality { get; init; } = 95;
    public int WebpQuality { get; init; } = 95;
    public bool WebpLossless { get; init; }
    public string? Directory { get; init; }
    public bool CustomFileName { get; init; }
    public string FileNamePattern { get; init; } = "{name}_framed";
    public FileConflictPolicy ConflictPolicy { get; init; }
    public bool IncludeMetadata { get; init; }
    public bool OpenFolder { get; init; }
    public bool RememberLocation { get; init; } = true;
    public string Extension => Format switch { ExportFormat.Png => ".png", ExportFormat.WebP => ".webp", _ => ".jpg" };
    public bool Lossless => Format == ExportFormat.Png || Format == ExportFormat.WebP && WebpLossless;
    public ExportPreferences Validate() => this with
    {
        Format = Enum.IsDefined(Format) ? Format : ExportFormat.Jpg,
        ConflictPolicy = Enum.IsDefined(ConflictPolicy) ? ConflictPolicy : FileConflictPolicy.AutoNumber,
        JpegQuality = Math.Clamp(JpegQuality, 1, 100), WebpQuality = Math.Clamp(WebpQuality, 1, 100),
        FileNamePattern = string.IsNullOrWhiteSpace(FileNamePattern) ? "{name}_framed" : FileNamePattern
    };
    public string GetFileName(string? source, DateTimeOffset date)
    {
        var name = Path.GetFileNameWithoutExtension(source ?? "PhotoMargin");
        var pattern = CustomFileName ? FileNamePattern : "{name}_framed";
        var result = pattern.Replace("{name}", name).Replace("{date}", date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
        result = new string(result.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        while (new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(result), StringComparer.OrdinalIgnoreCase))
            result = Path.GetFileNameWithoutExtension(result).TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(result)) result = "PhotoMargin_framed";
        var first = result.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(first, StringComparer.OrdinalIgnoreCase)) result = "_" + result;
        if (result.Length > 180) result = result[..180].TrimEnd('.', ' ');
        if (char.IsHighSurrogate(result[^1])) result = result[..^1];
        return result + Extension;
    }
}
