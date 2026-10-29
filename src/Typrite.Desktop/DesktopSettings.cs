using System.Text.Json;

namespace Typrite.Desktop;

/// <summary>Installer-written settings next to the executable, plus the per-user data folder.</summary>
public sealed record DesktopSettings(Uri ApiBaseUrl)
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Typrite");

    public static DesktopSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "typrite.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return new DesktopSettings(new Uri(doc.RootElement.GetProperty("apiBaseUrl").GetString()!, UriKind.Absolute));
    }
}
