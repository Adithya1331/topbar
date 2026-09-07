namespace TopBar;

internal static class BatteryInfo
{
    private const byte NoSystemBattery = 128;

    public static string Text { get; private set; } = "";
    public static int Percent { get; private set; }
    public static bool IsCharging { get; private set; }

    public static void Poll()
    {
        if (!Native.GetSystemPowerStatus(out Native.SYSTEM_POWER_STATUS status)
            || status.BatteryLifePercent == byte.MaxValue
            || (status.BatteryFlag & NoSystemBattery) != 0)
        {
            Text = "";
            Percent = 0;
            IsCharging = false;
            return;
        }

        Percent = status.BatteryLifePercent;
        IsCharging = status.ACLineStatus == 1;
        Text = $"{Percent}%";
    }

    public static void Reset()
    {
        Text = "";
        Percent = 0;
        IsCharging = false;
    }
}
