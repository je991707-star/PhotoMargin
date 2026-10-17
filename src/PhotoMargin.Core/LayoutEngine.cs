using PhotoMargin.Core.Models;

namespace PhotoMargin.Core;

public sealed class LayoutEngine(ITextMeasurer textMeasurer)
{
    public const string ColumnsFallbackWarning = "安全檢查失敗，無法使用左右分欄；已改為置中堆疊。";
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
        if (safe.IsEmpty || !double.IsFinite(safe.Width) || !double.IsFinite(safe.Height))
            return InvalidResult(canvasSize, safe, ["文字安全區無效，請調整文字與左右邊界及下方邊距。"]);
        var sideMargin = settings.PhotoSideMargin * scale;
        var topMargin = settings.PhotoTopMargin * scale;
        var gap = settings.PhotoInfoGap * scale;

        var content = BuildInformation(metadata, settings, safe);
        var hasInfo = content.Runs.Count > 0;
        // Use the union of measured ink bounds (including only the renderer's
        // one-pixel anti-clipping guard), not line/container/trailing height.
        var infoTop = hasInfo ? content.Runs.Min(run => run.Bounds.Top) : 0;
        var infoHeight = hasInfo ? content.Runs.Max(run => run.Bounds.Bottom) - infoTop : 0;
        var actualGap = hasInfo ? gap : 0;

        var photoAvailableWidth = canvasSize - sideMargin * 2;
        var photoAvailableHeight = hasInfo
            ? safe.Bottom - topMargin - actualGap - infoHeight
            : canvasSize - topMargin * 2;

        var errors = new List<string>();
        if (!content.Fits)
        {
            errors.Add("目前的資訊無法安全容納，請調整文字、焦距、曝光三要素間隔或文字與左右邊界。");
        }
        if (photoAvailableWidth <= 0 || photoAvailableHeight <= 0)
        {
            errors.Add("目前的設定沒有足夠空間放置完整照片，請調整上方邊距、下方邊距或照片與文字間隔。");
            return InvalidResult(canvasSize, safe, errors) with { FellBackFromColumns = content.FellBackFromColumns };
        }

        var photoScale = Math.Min(photoAvailableWidth / sourceSize.Width, photoAvailableHeight / sourceSize.Height);
        var photoWidth = sourceSize.Width * photoScale;
        var photoHeight = sourceSize.Height * photoScale;
        // Anchor the actual text bottom, then place the contained photo directly
        // above the requested gap. Do not vertically center the whole group.
        var verticalTop = hasInfo
            ? safe.Bottom - infoHeight - actualGap - photoHeight
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
                Bounds = new RectD(run.Bounds.Left, run.Bounds.Top - infoTop + textTop, run.Bounds.Right, run.Bounds.Bottom - infoTop + textTop)
            })
            .ToArray();

        var result = new LayoutResult(canvasSize, photoBounds, safe, shiftedRuns, content.Stacked, errors)
        { ProtectFourByFive = settings.ProtectFourByFive, ProtectFiveByFour = settings.ProtectFiveByFour };
        errors.AddRange(LayoutValidator.Validate(result));
        return result with { Errors = errors.Distinct(StringComparer.Ordinal).ToArray(), FellBackFromColumns = content.FellBackFromColumns };
    }

    private InformationLayout BuildInformation(DisplayMetadata metadata, LayoutSettings settings, RectD safe)
    {
        var scale = settings.Scale;
        var font = settings.FontFamily;
        var primary = Math.Max(settings.MinimumFontSize, settings.PrimaryFontSize) * scale;
        var secondary = Math.Max(settings.MinimumFontSize, settings.SecondaryFontSize) * scale;
        var left = new List<InformationItem>();
        var right = new List<InformationItem>();

        if (settings.Visibility.Camera && !string.IsNullOrWhiteSpace(metadata.Camera))
        {
            left.Add(new([metadata.Camera], primary, true));
        }

        if (settings.Visibility.Lens && !string.IsNullOrWhiteSpace(metadata.Lens))
        {
            // Lens size has its own 8px lower bound. A valid user value must not
            // be raised by the other information fields' shared minimum size.
            left.Add(new([metadata.Lens], settings.LensFontSize * scale, false));
        }

        if (settings.Visibility.Exposure)
        {
            var exposure = new[] { metadata.FocalLength, metadata.Aperture, metadata.Shutter, metadata.Iso }
                .Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
            if (exposure.Length > 0)
            {
                right.Add(new(exposure, secondary, false, true));
            }
        }

        if (settings.Visibility.Date && !string.IsNullOrWhiteSpace(metadata.Date))
        {
            right.Add(new([metadata.Date], Math.Max(settings.MinimumFontSize, settings.DateFontSize) * scale, false));
        }

        if (left.Count == 0 && right.Count == 0)
        {
            return InformationLayout.Empty;
        }

        if (settings.Arrangement != InformationArrangement.Stacked)
        {
            var gutter = 48 * scale;
            var columnWidth = (safe.Width - gutter) / 2d;
            var leftMeasured = MeasureColumn(left, font, columnWidth, TextAlignment.Left, safe.Left, settings.ExposureItemGap * scale);
            var rightMeasured = MeasureColumn(right, font, columnWidth, TextAlignment.Right, safe.Right - columnWidth, settings.ExposureItemGap * scale);
            var fits = leftMeasured.Fits && rightMeasured.Fits;
            if (fits)
            {
                var height = Math.Max(leftMeasured.Height, rightMeasured.Height);
                var runs = leftMeasured.Runs.Concat(rightMeasured.Runs).ToArray();
                return new InformationLayout(runs, height, false, fits);
            }
        }

        var all = left.Concat(right).ToList();
        var stacked = MeasureColumn(all, font, safe.Width, TextAlignment.Center, safe.Left, settings.ExposureItemGap * scale, allowWrap: true);
        return new InformationLayout(stacked.Runs, stacked.Height, true, stacked.Fits,
            FellBackFromColumns: settings.Arrangement == InformationArrangement.TwoColumns);
    }

    private ColumnLayout MeasureColumn(
        IReadOnlyList<InformationItem> items,
        string font,
        double width,
        TextAlignment alignment,
        double left,
        double itemGap,
        bool allowWrap = false)
    {
        var runs = new List<TextRunLayout>();
        var top = 0d;
        var fits = true;

        foreach (var item in items)
        {
            var lines = item.Exposure
                ? ExposureLines(item.Texts, font, item.Size, width, itemGap, allowWrap)
                : (allowWrap ? Wrap(item.Texts[0], font, item.Size, width) : [item.Texts[0]])
                    .Select(text => (IReadOnlyList<string>)new[] { text }).ToArray();
            foreach (var line in lines)
            {
                var measurements = line.Select(text => textMeasurer.Measure(text, font, item.Size)).ToArray();
                var baselines = line.Select(text => textMeasurer.MeasureBaseline(text, font, item.Size)).ToArray();
                var rowWidth = measurements.Sum(size => size.Width) + itemGap * (line.Count - 1);
                var baseline = baselines.Max();
                var rowHeight = baseline + measurements.Select((size, index) => size.Height - baselines[index]).Max();
                fits &= rowWidth <= width + 0.5;
                var x = alignment switch
                {
                    TextAlignment.Right => left + width - rowWidth,
                    TextAlignment.Center => left + (width - rowWidth) / 2d,
                    _ => left
                };
                for (var index = 0; index < line.Count; index++)
                {
                    var measured = measurements[index];
                    var itemTop = top + baseline - baselines[index];
                    var runBounds = new RectD(x, itemTop, x + measured.Width, itemTop + measured.Height);
                    runs.Add(new TextRunLayout(line[index], runBounds, item.Size, font,
                        item.Primary ? "#202020" : "#626262", alignment, item.Primary));
                    x += measured.Width + itemGap;
                }
                top += rowHeight + item.Size * LineSpacingRatio;
            }
        }

        if (runs.Count > 0)
        {
            top -= runs[^1].FontSize * LineSpacingRatio;
        }

        return new ColumnLayout(runs, top, fits);
    }

    private IReadOnlyList<IReadOnlyList<string>> ExposureLines(IReadOnlyList<string> texts, string font,
        double size, double width, double gap, bool allowWrap)
    {
        if (!allowWrap) return [texts];
        var lines = new List<IReadOnlyList<string>>();
        var row = new List<string>(); var used = 0d;
        foreach (var text in texts)
        {
            var measured = textMeasurer.Measure(text, font, size).Width;
            if (row.Count > 0 && used + gap + measured > width + 0.5)
            { lines.Add(row); row = []; used = 0; }
            used += (row.Count > 0 ? gap : 0) + measured;
            row.Add(text);
        }
        if (row.Count > 0) lines.Add(row);
        return lines;
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
    private sealed record InformationItem(IReadOnlyList<string> Texts, double Size, bool Primary, bool Exposure = false);
    private sealed record InformationLayout(IReadOnlyList<TextRunLayout> Runs, double Height, bool Stacked, bool Fits = true, bool FellBackFromColumns = false)
    {
        public static InformationLayout Empty { get; } = new([], 0, false);
    }
}
