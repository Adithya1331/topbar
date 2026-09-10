namespace TopBar;

/// <summary>
/// Result of evaluating the streak for "now".
/// </summary>
/// <param name="Available">False when we have no ApeKey-backed streak data yet.</param>
/// <param name="TypedToday">A test exists in the current streak day.</param>
/// <param name="TimeLeft">Time until the streak day rolls over.</param>
/// <param name="IsWarning">Guardian enabled, nothing typed, and inside the warning window.</param>
/// <param name="StreakDay">Index of the current streak day (for once-per-day alerts).</param>
internal readonly record struct GuardianStatus(bool Available, bool TypedToday, TimeSpan TimeLeft, bool IsWarning, long StreakDay)
{
    public static GuardianStatus None => new(false, false, TimeSpan.Zero, false, 0);

    public string TimeLeftText
    {
        get
        {
            int h = (int)TimeLeft.TotalHours;
            int m = TimeLeft.Minutes;
            return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
        }
    }
}

/// <summary>
/// Monkeytype counts streaks in "streak days": UTC days shifted by the account's hour offset.
/// A day index is floor((unixSeconds - hourOffset * 3600) / 86400). A test keeps the streak
/// alive if its timestamp falls in the current index; the streak resets once a full index
/// passes without one.
/// </summary>
internal static class StreakGuardian
{
    private const long DaySeconds = 86400;

    public static long DayIndex(DateTimeOffset t, int hourOffset)
        => FloorDiv(t.ToUnixTimeSeconds() - hourOffset * 3600L, DaySeconds);

    public static DateTimeOffset DayEnd(DateTimeOffset now, int hourOffset)
    {
        long next = (DayIndex(now, hourOffset) + 1) * DaySeconds + hourOffset * 3600L;
        return DateTimeOffset.FromUnixTimeSeconds(next);
    }

    public static GuardianStatus Evaluate(StreakInfo? streak, Settings s, DateTimeOffset now)
    {
        if (streak is null) return GuardianStatus.None;

        long today = DayIndex(now, streak.HourOffset);
        bool typed = streak.LastResultTimestampMs > 0
            && DayIndex(DateTimeOffset.FromUnixTimeMilliseconds(streak.LastResultTimestampMs), streak.HourOffset) == today;
        TimeSpan left = DayEnd(now, streak.HourOffset) - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;

        bool warn = s.StreakGuardianEnabled && !typed && left <= TimeSpan.FromHours(s.StreakWarnHours);
        return new GuardianStatus(true, typed, left, warn, today);
    }

    private static long FloorDiv(long a, long b)
    {
        long q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }
}
