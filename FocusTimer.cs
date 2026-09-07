namespace TopBar;

internal readonly record struct FocusTimerSnapshot(int RemainingSeconds, bool IsRunning, double Progress)
{
    public string Text => $"{RemainingSeconds / 60}:{RemainingSeconds % 60:00}";
}

internal static class FocusTimer
{
    private static DateTimeOffset s_end;
    private static int s_remainingSeconds;
    private static bool s_initialized;

    public static bool IsRunning => s_end != default;

    public static FocusTimerSnapshot GetSnapshot(int durationMinutes)
    {
        EnsureInitialized(durationMinutes);
        int remaining = GetRemainingSeconds();
        int total = Math.Max(1, durationMinutes * 60);
        return new FocusTimerSnapshot(remaining, IsRunning, Math.Clamp(1.0 - (double)remaining / total, 0.0, 1.0));
    }

    public static bool Toggle(int durationMinutes)
    {
        EnsureInitialized(durationMinutes);
        if (IsRunning)
        {
            s_remainingSeconds = GetRemainingSeconds();
            s_end = default;
            return false;
        }

        if (s_remainingSeconds <= 0) s_remainingSeconds = durationMinutes * 60;
        s_end = DateTimeOffset.UtcNow.AddSeconds(s_remainingSeconds);
        return true;
    }

    public static void Reset(int durationMinutes)
    {
        s_initialized = true;
        s_end = default;
        s_remainingSeconds = durationMinutes * 60;
    }

    public static bool Tick(int durationMinutes)
    {
        EnsureInitialized(durationMinutes);
        if (!IsRunning) return false;

        int remaining = GetRemainingSeconds();
        if (remaining > 0) return false;

        s_remainingSeconds = 0;
        s_end = default;
        return true;
    }

    private static void EnsureInitialized(int durationMinutes)
    {
        if (s_initialized) return;
        s_initialized = true;
        s_remainingSeconds = durationMinutes * 60;
    }

    private static int GetRemainingSeconds()
    {
        if (!IsRunning) return s_remainingSeconds;
        return Math.Max(0, (int)Math.Ceiling((s_end - DateTimeOffset.UtcNow).TotalSeconds));
    }
}
