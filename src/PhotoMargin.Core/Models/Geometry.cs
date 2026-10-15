namespace PhotoMargin.Core.Models;

public readonly record struct SizeD(double Width, double Height)
{
    public bool IsPositive => Width > 0 && Height > 0;
}

public readonly record struct RectD(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(RectD other, double tolerance = 0.01) =>
        other.Left >= Left - tolerance && other.Top >= Top - tolerance &&
        other.Right <= Right + tolerance && other.Bottom <= Bottom + tolerance;

    public bool Intersects(RectD other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public RectD Inset(double value) =>
        new(Left + value, Top + value, Right - value, Bottom - value);

    public static RectD Intersect(RectD left, RectD right) =>
        new(Math.Max(left.Left, right.Left), Math.Max(left.Top, right.Top),
            Math.Min(left.Right, right.Right), Math.Min(left.Bottom, right.Bottom));
}
