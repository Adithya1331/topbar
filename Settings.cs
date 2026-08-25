using System.Text.Json;
using System.Text.Json.Serialization;

namespace TopBar;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}

internal sealed class Settings
{
    public string Username { get; set; } = "";
    public string ApeKey { get; set; } = "";
    public int RefreshInterval { get; set; } = 21600;
    public int DaysToShow { get; set; } = 7;
    public int BarHeight { get; set; } = 32;
    public bool StartWithWindows { get; set; }
    public bool ShowCurrentWeekOnly { get; set; }
    public int WeekStartDay { get; set; } = 1;
    public bool HighlightCurrentDay { get; set; }
    public int ThemeName { get; set; }
    public int ColorMode { get; set; }
    public string RightClickAction { get; set; } = "homepage";
    public string HotkeyRefresh { get; set; } = "Win+Shift+R";
    public string HotkeyOpenMonkeytype { get; set; } = "Win+Shift+M";
    public string HotkeyOpenProfile { get; set; } = "Win+Shift+P";

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonkeyBar", "settings.json");

    public Settings Clone() => (Settings)MemberwiseClone();

    public void Normalize()
    {
        Username = Username.Trim();
        ApeKey = ApeKey.Trim();
        DaysToShow = Math.Clamp(DaysToShow, 1, 7);
        BarHeight = Math.Clamp(BarHeight, 24, 80);
        WeekStartDay = Math.Clamp(WeekStartDay, 0, 6);
        ThemeName = Math.Clamp(ThemeName, 0, Themes.List.Length - 1);
        ColorMode = Math.Clamp(ColorMode, 0, 1);
        RefreshInterval = Math.Clamp(RefreshInterval, 60, 7 * 86400);
        if (RightClickAction != "profile") RightClickAction = "homepage";
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppJsonContext.Default.Settings);
                if (s != null)
                {
                    s.Normalize();
                    return s;
                }
            }
        }
        catch
        {
        }
        return new Settings();
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, AppJsonContext.Default.Settings));
    }
}
