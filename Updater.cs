using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TopBar;

internal enum UpdateState
{
    Idle,
    Checking,
    Available,
    Installing,
    Restarting,
    Failed,
}

internal sealed record UpdateInfo(string Tag, Version Version, string AssetName, string DownloadUrl, string? ChecksumUrl, string ReleaseUrl);

/// <summary>
/// Self-updater against GitHub Releases. Checks the latest release tag, downloads the matching
/// asset (AOT or framework build) next to the running exe, verifies its SHA-256 against the
/// release's SHA256SUMS.txt, then swaps it in using the rename trick: Windows locks a running
/// exe against overwrite but allows renaming it, so TopBar.exe -> TopBar.old.exe, TopBar.new.exe
/// -> TopBar.exe, relaunch, and the new instance deletes TopBar.old.exe once the old one exits.
/// </summary>
internal static class Updater
{
    private const string Repo = "Adithya1331/topbar";
    private const string LatestApi = $"https://api.github.com/repos/{Repo}/releases/latest";
    internal const string ReleasesPage = $"https://github.com/{Repo}/releases";

    private const string NewSuffix = ".new.exe";
    private const string OldSuffix = ".old.exe";

    /// <summary>Passed to the relaunched exe so it waits for the previous instance to release the mutex.</summary>
    internal const string UpdatedArg = "--updated";

    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static int s_busy;

    public static Version Current { get; } = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Dev builds carry version 0.0.0 and would otherwise always "update" to the latest release.</summary>
    public static bool IsDevBuild => Current.Major == 0 && Current.Minor == 0 && Current.Build <= 0;

    /// <summary>AOT builds cannot JIT; framework-dependent single-file builds can.</summary>
    private static bool IsFrameworkBuild => RuntimeFeature.IsDynamicCodeSupported;

    public static UpdateState State { get; private set; }
    public static UpdateInfo? Available { get; private set; }
    public static string? Error { get; private set; }

    public static string VersionText => IsDevBuild ? "dev build" : $"v{Current.ToString(3)}";

    private static void Log(string message) => Diag.Log("update.log", message);

    /// <summary>
    /// Queries the latest release. On completion posts <paramref name="notifyMsg"/> to
    /// <paramref name="hwnd"/>; wParam 1 = state changed. Never throws.
    /// </summary>
    public static async Task CheckAsync(nint hwnd, uint notifyMsg, bool manual)
    {
        if (Interlocked.Exchange(ref s_busy, 1) != 0) return;
        try
        {
            State = UpdateState.Checking;
            Error = null;
            _ = Native.PostMessageW(hwnd, notifyMsg, 1, 0);

            Log($"check ({(manual ? "manual" : "scheduled")}), running {VersionText}, {(IsFrameworkBuild ? "framework" : "aot")} build");
            UpdateInfo? info = await FetchLatestAsync();
            if (info is null)
            {
                State = UpdateState.Failed;
                Error = "Could not reach GitHub";
                Log("check failed: no usable release/asset");
            }
            else if (info.Version > Current && !IsDevBuild)
            {
                Available = info;
                State = UpdateState.Available;
                Log($"update available: {info.Tag} ({info.AssetName}), checksums {(info.ChecksumUrl is null ? "absent" : "present")}");
            }
            else
            {
                Available = null;
                State = UpdateState.Idle;
                Log(IsDevBuild ? $"dev build, not updating (latest {info.Tag})" : $"up to date (latest {info.Tag})");
            }
        }
        catch (Exception ex)
        {
            State = UpdateState.Failed;
            Error = ex.Message;
            Log($"check failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref s_busy, 0);
            _ = Native.PostMessageW(hwnd, notifyMsg, 1, 0);
        }
    }

    /// <summary>
    /// Downloads, verifies and swaps in <see cref="Available"/>. Posts <paramref name="notifyMsg"/>
    /// with wParam 2 when the UI thread should relaunch and exit, or 1 on failure.
    /// </summary>
    public static async Task InstallAsync(nint hwnd, uint notifyMsg)
    {
        UpdateInfo? info = Available;
        if (info is null || Interlocked.Exchange(ref s_busy, 1) != 0) return;

        string exePath = Environment.ProcessPath ?? "";
        string newPath = Path.ChangeExtension(exePath, null) + NewSuffix;
        string oldPath = Path.ChangeExtension(exePath, null) + OldSuffix;
        try
        {
            if (exePath.Length == 0 || !File.Exists(exePath)) throw new InvalidOperationException("Cannot locate the running exe");

            State = UpdateState.Installing;
            Error = null;
            _ = Native.PostMessageW(hwnd, notifyMsg, 1, 0);

            TryDelete(newPath);
            Log($"downloading {info.DownloadUrl}");
            await DownloadAsync(info.DownloadUrl, newPath);
            Log($"downloaded {new FileInfo(newPath).Length:N0} bytes");

            if (info.ChecksumUrl is not null)
            {
                string? expected = await FetchExpectedHashAsync(info.ChecksumUrl, info.AssetName);
                if (expected is null) throw new InvalidOperationException("Checksum for the asset is missing");
                string actual = HashFile(newPath);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Checksum mismatch, download discarded");
                Log("checksum verified");
            }

            // Swap. Renaming a running exe is allowed; overwriting it is not.
            TryDelete(oldPath);
            File.Move(exePath, oldPath);
            try
            {
                File.Move(newPath, exePath);
            }
            catch
            {
                File.Move(oldPath, exePath); // roll back so the app still starts next time
                throw;
            }

            Log($"swapped in {info.Tag}, restarting");
            State = UpdateState.Restarting;
            _ = Native.PostMessageW(hwnd, notifyMsg, 2, 0);
        }
        catch (Exception ex)
        {
            TryDelete(newPath);
            State = UpdateState.Failed;
            Error = ex.Message;
            Log($"install failed: {ex.GetType().Name}: {ex.Message}");
            _ = Native.PostMessageW(hwnd, notifyMsg, 1, 0);
        }
        finally
        {
            Volatile.Write(ref s_busy, 0);
        }
    }

    /// <summary>Starts the freshly swapped exe. The caller must exit right after.</summary>
    public static void Relaunch()
    {
        string exePath = Environment.ProcessPath ?? "";
        if (exePath.Length == 0) return;
        _ = Native.ShellExecuteW(default, "open", exePath, UpdatedArg, Path.GetDirectoryName(exePath), Native.SW_SHOWNORMAL);
    }

    /// <summary>Removes the previous version left behind by a swap. Fails silently while it is still running.</summary>
    public static bool CleanupOldBinary()
    {
        string exePath = Environment.ProcessPath ?? "";
        if (exePath.Length == 0) return true;
        string oldPath = Path.ChangeExtension(exePath, null) + OldSuffix;
        if (!File.Exists(oldPath)) return true;
        return TryDelete(oldPath);
    }

    public static void Dismiss()
    {
        if (State is UpdateState.Failed or UpdateState.Available)
        {
            State = UpdateState.Idle;
            Error = null;
        }
    }

    private static async Task<UpdateInfo?> FetchLatestAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        req.Headers.UserAgent.ParseAdd("MonkeyBar-Windows");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var resp = await s_http.SendAsync(req, cts.Token);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;

        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? version)) return null;
        version = new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));
        string releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;

        string wanted = IsFrameworkBuild ? $"TopBar-{tag}-win-x64-framework.exe" : $"TopBar-{tag}-win-x64.exe";
        string? downloadUrl = null, checksumUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (url.Length == 0) continue;
                if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) downloadUrl = url;
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) checksumUrl = url;
            }
        }
        if (downloadUrl is null) return null;
        return new UpdateInfo(tag, version, wanted, downloadUrl, checksumUrl, releaseUrl);
    }

    private static async Task DownloadAsync(string url, string destination)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("MonkeyBar-Windows");
        using var resp = await s_http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        await src.CopyToAsync(dst);
    }

    private static async Task<string?> FetchExpectedHashAsync(string url, string assetName)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("MonkeyBar-Windows");
        using var resp = await s_http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        string text = await resp.Content.ReadAsStringAsync();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int sp = line.IndexOf(' ');
            if (sp <= 0) continue;
            string hash = line[..sp];
            string name = line[sp..].Trim().TrimStart('*');
            if (name.Equals(assetName, StringComparison.OrdinalIgnoreCase) && hash.Length == 64) return hash;
        }
        return null;
    }

    private static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
