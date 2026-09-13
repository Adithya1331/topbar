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
    public bool ShowCpuRam { get; set; } = true;
    public bool ShowPomodoro { get; set; } = true;
    public bool ShowFocusTimer { get; set; } = true;
    public int FocusDurationMinutes { get; set; } = 25;
    public bool ShowBattery { get; set; } = true;
    public bool ShowVolume { get; set; } = true;
    public string RoundPieToken { get; set; } = "";
    public string RoundPieUrl { get; set; } = "https://api.rpie.me";
    public bool ShowCurrentWeekOnly { get; set; }
    public int WeekStartDay { get; set; } = 1;
    public bool HighlightCurrentDay { get; set; }
    public int ThemeName { get; set; }
    public int ColorMode { get; set; }
    public string RightClickAction { get; set; } = "homepage";
    public string HotkeyRefresh { get; set; } = "Win+Shift+R";
    public string HotkeyOpenMonkeytype { get; set; } = "Win+Shift+M";
    public string HotkeyOpenProfile { get; set; } = "Win+Shift+P";
    /// <summary>Hides/shows the whole bar (e.g. for fullscreen video). Works while hidden too.</summary>
    public string HotkeyToggleBar { get; set; } = "Win+Shift+H";

    /// <summary>
    /// Monkeytype streak-day boundary, in hours relative to UTC midnight (the account's
    /// "streak hour offset", -11..12 in 0.5 steps). Read from /users/streak and cached here
    /// because the API only lets a user change it once, so it is effectively immutable.
    /// </summary>
    public double StreakHourOffset { get; set; }
    public bool StreakHourOffsetKnown { get; set; }
    public bool StreakGuardianEnabled { get; set; } = true;
    /// <summary>Warn when no test has been done and fewer than this many hours remain in the streak day.</summary>
    public int StreakWarnHours { get; set; } = 3;

    /// <summary>Self-updater behaviour: 0 = off, 1 = notify in the bar, 2 = install automatically.</summary>
    public int UpdateMode { get; set; } = 1;

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
        FocusDurationMinutes = Math.Clamp(FocusDurationMinutes, 5, 120);
        StreakHourOffset = double.IsFinite(StreakHourOffset) ? Math.Clamp(StreakHourOffset, -11, 12) : 0;
        StreakWarnHours = Math.Clamp(StreakWarnHours, 1, 12);
        UpdateMode = Math.Clamp(UpdateMode, 0, 2);
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
