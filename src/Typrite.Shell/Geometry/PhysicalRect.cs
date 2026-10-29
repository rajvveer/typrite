namespace Typrite.Shell.Geometry;

/// <summary>A rectangle in physical screen pixels (Win32 RECT semantics: right/bottom exclusive).</summary>
public readonly record struct PhysicalRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>True when this rect covers <paramref name="other"/> entirely, allowing each edge to fall short by up to <paramref name="tolerancePx"/>.</summary>
    public bool Covers(PhysicalRect other, int tolerancePx) =>
        Left <= other.Left + tolerancePx
        && Top <= other.Top + tolerancePx
        && Right >= other.Right - tolerancePx
        && Bottom >= other.Bottom - tolerancePx;
}
