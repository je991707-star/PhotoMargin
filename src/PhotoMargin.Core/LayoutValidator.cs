using PhotoMargin.Core.Models;

namespace PhotoMargin.Core;

public static class LayoutValidator
{
    public static IReadOnlyList<string> Validate(LayoutResult layout)
    {
        var errors = new List<string>();
        var canvas = new RectD(0, 0, layout.CanvasSize, layout.CanvasSize);

        if (!canvas.Contains(layout.PhotoBounds))
        {
            errors.Add("照片超出正方形畫布。");
        }

        foreach (var run in layout.TextRuns)
        {
            if (!layout.TextSafeBounds.Contains(run.Bounds))
            {
                errors.Add($"文字「{run.Text}」超出裁切安全區。");
            }

            if (run.Bounds.Intersects(layout.PhotoBounds))
            {
                errors.Add($"文字「{run.Text}」與照片重疊。");
            }
        }

        for (var index = 0; index < layout.TextRuns.Count; index++)
        {
            for (var other = index + 1; other < layout.TextRuns.Count; other++)
            {
                if (layout.TextRuns[index].Bounds.Intersects(layout.TextRuns[other].Bounds))
                {
                    errors.Add("資訊文字互相重疊。");
                }
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
}
