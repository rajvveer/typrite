using Typrite.Shell.Geometry;

namespace Typrite.Shell.Displays;

/// <summary>One attached display: stable key, full monitor bounds (not work area) and effective DPI.</summary>
public sealed record DisplayInfo(DisplayKey Key, PhysicalRect Bounds, uint Dpi);
