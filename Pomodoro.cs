using System.Text.Json;

namespace TopBar;

internal static class Pomodoro
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string TodayText { get; private set; } = "";

    public static async Task RefreshAsync(Settings s)
    {
        if (s.RoundPieToken.Length == 0)
        {
            TodayText = "";
            return;
        }

        try
        {
            var now = DateTime.Now;
            long from = new DateTimeOffset(now.Date).ToUnixTimeSeconds();
            long to = new DateTimeOffset(now).ToUnixTimeSeconds() + 60;

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{s.RoundPieUrl.TrimEnd('/')}/api/v3/log?projects=&sources=&buckets=&tags=&from={from}&to={to}&build_number=my&workspaces=");
            req.Headers.UserAgent.ParseAdd("MonkeyBar-Windows");
            req.Headers.TryAddWithoutValidation("authorization", s.RoundPieToken);

            using var resp = await s_http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();

            long sum = 0;
            using (var doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.TryGetProperty("timeline", out var tl) && tl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in tl.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.Object) continue;
                        if (e.TryGetProperty("type", out var ty) && ty.GetString() != "pomodoro") continue;
                        if (e.TryGetProperty("timeSpent", out var ts) && ts.ValueKind == JsonValueKind.Number)
                            sum += ts.GetInt64();
                    }
                }
            }

            TodayText = $"Today {FormatDuration((int)Math.Min(sum, int.MaxValue))}";
        }
        catch
        {
        }
    }

    public static string FormatDuration(int seconds)
    {
        int h = seconds / 3600;
        int m = (seconds % 3600) / 60;
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }
}
