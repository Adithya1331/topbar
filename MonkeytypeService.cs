using System.Text.Json;

namespace TopBar;

internal sealed class ActivityState
{
    public bool HasData { get; set; } = true;
    public bool IsStreakOnly { get; set; }
    public int Streak { get; set; }
    public int MaxStreak { get; set; }
    public int TotalTests { get; set; }
    public long TimeTypingSeconds { get; set; }
    public DateTime[] Dates { get; set; } = [];
    public int[] Counts { get; set; } = [];
    /// <summary>Every day returned by the API (up to 372), keyed by local calendar date.</summary>
    public Dictionary<DateTime, int> ByDay { get; set; } = [];
}

/// <summary>Snapshot of /users/streak. Timestamps are Unix milliseconds; HourOffset is in half-hour steps.</summary>
internal sealed record StreakInfo(int Length, int MaxLength, long LastResultTimestampMs, double HourOffset);

internal static class MonkeytypeService
{
    private const string BaseUrl = "https://api.monkeytype.com";

    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Fetches streak data (needs an ApeKey). Returns null on any failure so callers keep
    /// their previous value; a 429/479 should never wipe good cached state.
    /// </summary>
    public static async Task<StreakInfo?> FetchStreakAsync(Settings s)
    {
        if (string.IsNullOrWhiteSpace(s.ApeKey)) return null;
        string body = "";
        try
        {
            using var resp = await SendAsync($"{BaseUrl}/users/streak", s.ApeKey);
            body = await resp.Content.ReadAsStringAsync();
            // Raw response goes to api.log so odd account setups (half-hour offsets, null data)
            // can be diagnosed from a user's machine without a debugger.
            Diag.Log("api.log", $"GET /users/streak -> {(int)resp.StatusCode} {Trim(body)}");
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Object) return null;

            // hourOffset is a number in 0.5 steps (-11..12); the others are integers but are
            // read leniently too, since GetInt32() throws on "5.0".
            double hourOffset = GetDouble(data, "hourOffset");
            long last = (long)GetDouble(data, "lastResultTimestamp");
            return new StreakInfo(GetInt(data, "length"), GetInt(data, "maxLength"), last, hourOffset);
        }
        catch (Exception ex)
        {
            Diag.Log("api.log", $"/users/streak parse failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string Trim(string s) => s.Length <= 600 ? s : s[..600] + "…";

    public static DateTime[] GetDates(Settings s)
    {
        int days = Math.Clamp(s.DaysToShow, 1, 7);
        var today = DateTime.Today;
        var dates = new DateTime[days];

        if (s.ShowCurrentWeekOnly)
        {
            int daysToSubtract = ((int)today.DayOfWeek - s.WeekStartDay + 7) % 7;
            var weekStart = today.AddDays(-daysToSubtract);
            for (int i = 0; i < days; i++) dates[i] = weekStart.AddDays(i);
        }
        else
        {
            for (int i = 0; i < days; i++) dates[i] = today.AddDays(-(days - 1 - i));
        }

        return dates;
    }

    public static async Task<ActivityState> FetchTypingActivityAsync(Settings s)
    {
        var dates = GetDates(s);
        var iso = dates.Select(d => d.ToString("yyyy-MM-dd")).ToArray();

        if (string.IsNullOrWhiteSpace(s.Username)) return Zeros(dates);

        try
        {
            if (string.IsNullOrWhiteSpace(s.ApeKey))
                return await FetchPublicProfileAsync(s.Username, dates, iso);

            using (var resp = await SendAsync($"{BaseUrl}/users/{Uri.EscapeDataString(s.Username)}/profile", s.ApeKey))
            using (var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()))
            {
                var data = doc.RootElement.GetProperty("data");
                if (TryGetActivityElement(data, out var ta)) return FillProfileMeta(ParseTestActivity(ta, dates, iso), data);
            }

            using (var resp = await SendAsync($"{BaseUrl}/users/currentTestActivity", s.ApeKey))
            using (var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()))
            {
                var data = doc.RootElement.GetProperty("data");
                if (TryGetActivityElement(data, out var ta)) return ParseTestActivity(ta, dates, iso);
            }

            return Zeros(dates);
        }
        catch
        {
            return Unavailable(dates);
        }
    }

    private static async Task<ActivityState> FetchPublicProfileAsync(string username, DateTime[] dates, string[] iso)
    {
        try
        {
            using var resp = await SendAsync($"{BaseUrl}/users/{Uri.EscapeDataString(username)}/profile", null);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");

            if (TryGetActivityElement(data, out var ta)) return FillProfileMeta(ParseTestActivity(ta, dates, iso), data);

            return FillProfileMeta(new ActivityState
            {
                IsStreakOnly = true,
                Dates = dates,
                Counts = new int[dates.Length],
            }, data);
        }
        catch
        {
            return Unavailable(dates);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(string url, string? apeKey)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("MonkeyBar-Windows");
        if (apeKey is not null) req.Headers.TryAddWithoutValidation("Authorization", $"ApeKey {apeKey}");
        return await s_http.SendAsync(req);
    }

    private static bool TryGetActivityElement(JsonElement element, out JsonElement activity)
    {
        activity = default;
        if (element.ValueKind != JsonValueKind.Object) return false;

        if (element.TryGetProperty("testActivity", out var nested))
        {
            if (nested.ValueKind == JsonValueKind.Object
                && nested.TryGetProperty("testsByDays", out var t)
                && t.ValueKind == JsonValueKind.Array)
            {
                activity = nested;
                return true;
            }
            return false;
        }

        if (element.TryGetProperty("testsByDays", out var tbd) && tbd.ValueKind == JsonValueKind.Array)
        {
            activity = element;
            return true;
        }

        return false;
    }

    /// <summary>Copies streak and lifetime totals from a /profile payload onto the state.</summary>
    private static ActivityState FillProfileMeta(ActivityState st, JsonElement data)
    {
        st.Streak = GetInt(data, "streak");
        st.MaxStreak = GetInt(data, "maxStreak");
        if (data.TryGetProperty("typingStats", out var ts) && ts.ValueKind == JsonValueKind.Object)
        {
            st.TotalTests = GetInt(ts, "completedTests");
            if (ts.TryGetProperty("timeTyping", out var tt) && tt.ValueKind == JsonValueKind.Number)
                st.TimeTypingSeconds = (long)tt.GetDouble();
        }
        return st;
    }

    private static ActivityState ParseTestActivity(JsonElement testActivity, DateTime[] dates, string[] iso)
    {
        var map = new Dictionary<string, int>();
        var byDay = new Dictionary<DateTime, int>();
        try
        {
            var tbd = testActivity.GetProperty("testsByDays");
            int n = tbd.GetArrayLength();
            long lastDayMs = 0;
            if (testActivity.TryGetProperty("lastDay", out var ld))
            {
                if (ld.ValueKind == JsonValueKind.Number) lastDayMs = ld.GetInt64();
                else if (ld.ValueKind == JsonValueKind.String && DateTime.TryParse(ld.GetString(), out var parsed))
                    lastDayMs = new DateTimeOffset(parsed.ToUniversalTime()).ToUnixTimeMilliseconds();
            }

            if (n > 0)
            {
                var lastDay = lastDayMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(lastDayMs).LocalDateTime.Date
                    : DateTime.Today;
                for (int i = n - 1; i >= 0; i--)
                {
                    var d = lastDay.AddDays(-(n - 1 - i));
                    int c = 0;
                    if (tbd[i].ValueKind == JsonValueKind.Number && tbd[i].TryGetInt32(out int v))
                        c = Math.Max(0, v);
                    map[d.ToString("yyyy-MM-dd")] = c;
                    byDay[d] = c;
                }
            }
        }
        catch
        {
        }

        var counts = new int[dates.Length];
        for (int i = 0; i < dates.Length; i++) counts[i] = map.TryGetValue(iso[i], out int c) ? c : 0;
        return new ActivityState { Dates = dates, Counts = counts, ByDay = byDay };
    }

    private static ActivityState Zeros(DateTime[] dates) => new() { Dates = dates, Counts = new int[dates.Length] };

    private static ActivityState Unavailable(DateTime[] dates) => new() { HasData = false, Dates = dates, Counts = new int[dates.Length] };

    private static int GetInt(JsonElement e, string name) => (int)Math.Round(GetDouble(e, name));

    private static double GetDouble(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        return 0;
    }
}
