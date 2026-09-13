namespace TopBar;

/// <summary>
/// Tiny append-only diagnostic logs in %APPDATA%\MonkeyBar (next to settings.json).
/// Each file is truncated once it passes 64 KB, so they never grow unbounded.
/// </summary>
internal static class Diag
{
    private const long MaxBytes = 64 * 1024;
    private static readonly object s_lock = new();

    public static void Log(string fileName, string message)
    {
        try
        {
            string dir = Path.GetDirectoryName(Settings.FilePath)!;
            string path = Path.Combine(dir, fileName);
            lock (s_lock)
            {
                Directory.CreateDirectory(dir);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
