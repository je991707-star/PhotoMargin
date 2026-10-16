using PhotoMargin.Core.Models;

namespace PhotoMargin.Core;

public sealed class LayoutEngine(ITextMeasurer textMeasurer)
{
    private const double LineSpacingRatio = 0.22;

    public LayoutResult Calculate(SizeD sourceSize, DisplayMetadata metadata, LayoutSettings rawSettings)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!sourceSize.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSize));
        }

        var settings = rawSettings.Validate();
        var scale = settings.Scale;
        var canvasSize = settings.OutputSize;
        var safe = CropGeometry.TextSafeRect(settings);
        var sideMargin = settings.PhotoSideMargin * scale;
        var topMargin = settings.PhotoTopMargin * scale;
        var gap = settings.PhotoInfoGap * scale;

        var content = BuildInformation(metadata, settings, safe);
        var infoHeight = content.Height;
        var hasInfo = content.Runs.Count > 0;
        var actualGap = hasInfo ? gap : 0;

        var photoAvailableWidth = canvasSize - sideMargin * 2;
        var photoAvailableHeight = hasInfo
            ? safe.Bottom - topMargin - actualGap - infoHeight
            : canvasSize - topMargin * 2;

        var errors = new List<string>();
        if (!content.Fits)
        {
            errors.Add(settings.Arrangement == InformationArrangement.TwoColumns
                ? "左右分欄無法容納目前的資訊，請改用自動或置中堆疊排列，或調整文字設定。"
                : "目前的資訊無法安全容納，請調整文字或版面設定。");
        }
        if (photoAvailableWidth <= 0 || photoAvailableHeight <= 0)
        {
            errors.Add("目前的邊距與文字設定沒有足夠空間放置照片。");
            return InvalidResult(canvasSize, safe, errors);
        }

        var photoScale = Math.Min(photoAvailableWidth / sourceSize.Width, photoAvailableHeight / sourceSize.Height);
        var photoWidth = sourceSize.Width * photoScale;
        var photoHeight = sourceSize.Height * photoScale;
        var groupHeight = photoHeight + actualGap + infoHeight;

        var verticalTop = hasInfo
            ? topMargin + Math.Max(0, (safe.Bottom - topMargin - groupHeight) / 2d)
            : (canvasSize - photoHeight) / 2d;

        var photoBounds = new RectD(
            (canvasSize - photoWidth) / 2d,
            verticalTop,
            (canvasSize + photoWidth) / 2d,
            verticalTop + photoHeight);

        var textTop = photoBounds.Bottom + actualGap;
        var shiftedRuns = content.Runs
            .Select(run => run with
            {
                Bounds = new RectD(run.Bounds.Left, run.Bounds.Top + textTop, run.Bounds.Right, run.Bounds.Bottom + textTop)
            })
            .ToArray();

        var result = new LayoutResult(canvasSize, photoBounds, safe, shiftedRuns, content.Stacked, errors);
        errors.AddRange(LayoutValidator.Validate(result));
        return result with { Errors = errors.Distinct(StringComparer.Ordinal).ToArray() };
    }

    private InformationLayout BuildInformation(DisplayMetadata metadata, LayoutSettings settings, RectD safe)
    {
        var scale = settings.Scale;
        var font = settings.FontFamily;
        var primary = Math.Max(settings.MinimumFontSize, settings.PrimaryFontSize) * scale;
        var secondary = Math.Max(settings.MinimumFontSize, settings.SecondaryFontSize) * scale;
        var left = new List<(string Text, double Size, bool Primary)>();
        var right = new List<(string Text, double Size, bool Primary)>();

        if (settings.Visibility.Camera && !string.IsNullOrWhiteSpace(metadata.Camera))
        {
            left.Add((metadata.Camera, primary, true));
        }

        if (settings.Visibility.Lens && !string.IsNullOrWhiteSpace(metadata.Lens))
        {
            left.Add((metadata.Lens, Math.Max(settings.MinimumFontSize, settings.LensFontSize) * scale, false));
        }

        if (settings.Visibility.Exposure)
        {
            var exposure = string.Join("   ", new[] { metadata.FocalLength, metadata.Aperture, metadata.Shutter, metadata.Iso }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
            if (exposure.Length > 0)
            {
                right.Add((exposure, secondary, false));
            }
        }

        if (settings.Visibility.Date && !string.IsNullOrWhiteSpace(metadata.Date))
        {
            right.Add((metadata.Date, Math.Max(settings.MinimumFontSize, settings.DateFontSize) * scale, false));
        }

        if (left.Count == 0 && right.Count == 0)
        {
            return InformationLayout.Empty;
        }

        if (settings.Arrangement != InformationArrangement.Stacked)
        {
            var gutter = 48 * scale;
            var columnWidth = (safe.Width - gutter) / 2d;
            var leftMeasured = MeasureColumn(left, font, columnWidth, TextAlignment.Left, safe.Left);
            var rightMeasured = MeasureColumn(right, font, columnWidth, TextAlignment.Right, safe.Right - columnWidth);
            var fits = leftMeasured.Fits && rightMeasured.Fits;
            if (fits || settings.Arrangement == InformationArrangement.TwoColumns)
            {
                var height = Math.Max(leftMeasured.Height, rightMeasured.Height);
                var runs = leftMeasured.Runs.Concat(rightMeasured.Runs).ToArray();
                return new InformationLayout(runs, height, false, fits);
            }
        }

        var all = left.Concat(right).ToList();
        var stacked = MeasureColumn(all, font, safe.Width, TextAlignment.Center, safe.Left, allowWrap: true);
        return new InformationLayout(stacked.Runs, stacked.Height, true, stacked.Fits);
    }

    private ColumnLayout MeasureColumn(
        IReadOnlyList<(string Text, double Size, bool Primary)> items,
        string font,
        double width,
        TextAlignment alignment,
        double left,
        bool allowWrap = false)
    {
        var runs = new List<TextRunLayout>();
        var top = 0d;
        var fits = true;

        foreach (var item in items)
        {
            var lines = allowWrap ? Wrap(item.Text, font, item.Size, width) : [item.Text];
            foreach (var line in lines)
            {
                var measured = textMeasurer.Measure(line, font, item.Size);
                fits &= measured.Width <= width + 0.5;
                var x = alignment switch
                {
                    TextAlignment.Right => left + width - measured.Width,
                    TextAlignment.Center => left + (width - measured.Width) / 2d,
                    _ => left
                };
                var runBounds = new RectD(x, top, x + measured.Width, top + measured.Height);
                runs.Add(new TextRunLayout(line, runBounds, item.Size, font,
                    item.Primary ? "#202020" : "#626262", alignment, item.Primary));
                top += measured.Height + item.Size * LineSpacingRatio;
            }
        }

        if (runs.Count > 0)
        {
            top -= runs[^1].FontSize * LineSpacingRatio;
        }

        return new ColumnLayout(runs, top, fits);
    }

    private IReadOnlyList<string> Wrap(string text, string font, double fontSize, double maxWidth)
    {
        if (textMeasurer.Measure(text, font, fontSize).Width <= maxWidth)
        {
            return [text];
        }

        var lines = new List<string>();
        var remaining = text.Trim();
        while (remaining.Length > 0)
        {
            var low = 1;
            var high = remaining.Length;
            var take = 0;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                var candidate = remaining[..middle].TrimEnd();
                if (textMeasurer.Measure(candidate, font, fontSize).Width <= maxWidth)
                {
                    take = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (take == 0)
            {
                lines.Add(remaining);
                break;
            }

            if (take < remaining.Length)
            {
                var boundary = remaining.LastIndexOfAny([' ', '\t', '/', '-'], take - 1, take);
                if (boundary > 0)
                {
                    take = boundary + 1;
                }
            }

            lines.Add(remaining[..take].Trim());
            remaining = remaining[take..].TrimStart();
        }

        return lines;
    }

    private static LayoutResult InvalidResult(int canvasSize, RectD safe, IReadOnlyList<string> errors) =>
        new(canvasSize, default, safe, [], false, errors);

    private sealed record ColumnLayout(IReadOnlyList<TextRunLayout> Runs, double Height, bool Fits);
    private sealed record InformationLayout(IReadOnlyList<TextRunLayout> Runs, double Height, bool Stacked, bool Fits = true)
    {
        public static InformationLayout Empty { get; } = new([], 0, false);
    }
}
