namespace TopBar;

internal static class SysInfo
{
    private static long s_lastIdle;
    private static long s_lastKernel;
    private static long s_lastUser;
    private static bool s_hasPrev;

    public static string Text { get; private set; } = "";

    public static void Poll()
    {
        Native.GetSystemTimes(out long idle, out long kernel, out long user);

        if (s_hasPrev)
        {
            long idleD = idle - s_lastIdle;
            long kernelD = kernel - s_lastKernel;
            long userD = user - s_lastUser;
            long total = kernelD + userD;
            int cpu = total > 0 ? (int)Math.Round(100.0 * (total - idleD) / total) : 0;
            cpu = Math.Clamp(cpu, 0, 100);

            var ms = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            int ram = Native.GlobalMemoryStatusEx(ref ms) ? (int)ms.dwMemoryLoad : 0;

            Text = $"CPU {cpu}% \u00b7 RAM {ram}%";
        }

        s_lastIdle = idle;
        s_lastKernel = kernel;
        s_lastUser = user;
        s_hasPrev = true;
    }

    public static void Reset()
    {
        Text = "";
        s_hasPrev = false;
    }
}
