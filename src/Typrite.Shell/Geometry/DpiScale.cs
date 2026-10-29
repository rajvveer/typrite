namespace Typrite.Shell.Geometry;

/// <summary>Conversions between device-independent pixels (96 DPI) and physical pixels.</summary>
public static class DpiScale
{
    public const uint BaseDpi = 96;

    public static int DipToPhysical(double dip, uint dpi) =>
        (int)Math.Round(dip * dpi / BaseDpi, MidpointRounding.AwayFromZero);
}
