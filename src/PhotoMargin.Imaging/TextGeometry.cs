using System.Collections.Concurrent;
using ImageMagick;
using ImageMagick.Drawing;

namespace PhotoMargin.Imaging;

// Resolve UI family names to actual font files and measure the same ink that is drawn.
// This affects typography only, not contain geometry or crop/safety rules.
internal sealed record TextGeometry(string Font, double Width, double Height, double OriginX, double Baseline)
{
    private static readonly ConcurrentDictionary<(string, string, double), TextGeometry> Cache = new();
    private static readonly IReadOnlyDictionary<string, string> FontFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft JhengHei UI"] = "msjh.ttc", ["Microsoft JhengHei"] = "msjh.ttc", ["Segoe UI"] = "segoeui.ttf",
        ["Arial"] = "arial.ttf", ["Calibri"] = "calibri.ttf", ["Consolas"] = "consola.ttf", ["Times New Roman"] = "times.ttf"
    };
    public static TextGeometry Measure(string text, string family, double size)
    {
        var font = ResolveFont(family, text);
        if (Cache.Count > 512) Cache.Clear();
        return Cache.GetOrAdd((text, font, size), key => Create(key.Item1, key.Item2, key.Item3));
    }
    private static string ResolveFont(string family, string text)
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (text.Any(ch => ch is >= '\u2E80' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF'))
        {
            var fallback = Path.Combine(fonts, "msjh.ttc");
            if (File.Exists(fallback)) return fallback;
        }
        if (FontFiles.TryGetValue(family, out var file) && File.Exists(Path.Combine(fonts, file))) return Path.Combine(fonts, file);
        return family;
    }
    private static TextGeometry Create(string text, string font, double size)
    {
        var metrics = new Drawables().Font(font).FontPointSize(size).FontTypeMetrics(text)
            ?? throw new InvalidOperationException($"無法量測字體：{font}");
        var padding = (int)Math.Ceiling(size * 2 + 4);
        var baseline = padding + metrics.Ascent;
        using var mask = new MagickImage(MagickColors.White,
            (uint)Math.Max(1, Math.Ceiling(metrics.TextWidth + padding * 2)),
            (uint)Math.Max(1, Math.Ceiling(metrics.TextHeight + padding * 2)));
        new Drawables().Font(font).FontPointSize(size).FillColor(MagickColors.Black).StrokeColor(MagickColors.Transparent)
            .Text(padding, baseline, text).Draw(mask);
        var ink = mask.BoundingBox;
        if (ink is null)
        {
            if (string.IsNullOrWhiteSpace(text)) return new TextGeometry(font, metrics.TextWidth, size, 0, metrics.Ascent);
            throw new InvalidOperationException("所選字體無法繪製這段文字，請選擇其他照片字體。");
        }
        // One pixel of guard on every side also covers fractional-position rasterization.
        return new TextGeometry(font, ink.Width + 2, ink.Height + 2, 1 + padding - ink.X, 1 + baseline - ink.Y);
    }
}
