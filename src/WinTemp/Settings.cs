using System.Text.Json;

namespace WinTemp;

/// <summary>Impostazioni salvate in %APPDATA%\WinTemp\settings.json.</summary>
public sealed class Settings
{
    public bool Pinned { get; set; }
    public int? PinnedX { get; set; }
    public int? PinnedY { get; set; }
    public bool AlwaysVisibleInTray { get; set; } = true;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTemp", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
