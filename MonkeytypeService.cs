using System.Text.Json;

namespace TopBar;

internal sealed class ActivityState
{
    public bool HasData { get; set; } = true;
    public bool IsStreakOnly { get; set; }
    public int Streak { get; set; }
    public int MaxStreak { get; set; }
    public DateTime[] Dates { get; set; } = [];
    public int[] Counts { get; set; } = [];
}

internal static class MonkeytypeService
{
    private const string BaseUrl = "https://api.monkeytype.com";

    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };

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
                if (TryGetActivityElement(data, out var ta)) return ParseTestActivity(ta, dates, iso);
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

            if (TryGetActivityElement(data, out var ta)) return ParseTestActivity(ta, dates, iso);

            return new ActivityState
            {
                IsStreakOnly = true,
                Streak = GetInt(data, "streak"),
                MaxStreak = GetInt(data, "maxStreak"),
                Dates = dates,
                Counts = new int[dates.Length],
            };
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

    private static ActivityState ParseTestActivity(JsonElement testActivity, DateTime[] dates, string[] iso)
    {
        var map = new Dictionary<string, int>();
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
                }
            }
        }
        catch
        {
        }

        var counts = new int[dates.Length];
        for (int i = 0; i < dates.Length; i++) counts[i] = map.TryGetValue(iso[i], out int c) ? c : 0;
        return new ActivityState { Dates = dates, Counts = counts };
    }

    private static ActivityState Zeros(DateTime[] dates) => new() { Dates = dates, Counts = new int[dates.Length] };

    private static ActivityState Unavailable(DateTime[] dates) => new() { HasData = false, Dates = dates, Counts = new int[dates.Length] };

    private static int GetInt(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        return 0;
    }
}
