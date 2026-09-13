using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TopBar;

internal static class Program
{
    private const int STATUS_GAP = 6;
    private const int BOX_SIZE = 14;
    private const int BOX_GAP = 6;
    private const int BOX_RADIUS = 3;
    private const uint BAR_BG = 0x001C1818;

    private const uint WM_APPBAR_CALLBACK = 0x8000 | 0x0001;
    private const uint WM_APP_REFRESH = 0x8000 | 0x0002;
    internal const uint WM_APP_VOLUME = 0x8000 | 0x0003;
    private const uint WM_APP_UPDATE = 0x8000 | 0x0004;

    private const nint TIMER_REFRESH = 1;
    private const nint TIMER_CLOCK = 2;
    private const nint TIMER_SYSINFO = 3;
    private const nint TIMER_POMO = 4;
    private const nint TIMER_RESUME_REFRESH = 5;
    private const nint TIMER_FOCUS = 6;
    private const nint TIMER_UPDATE = 7;

    private const uint UPDATE_FIRST_CHECK_MS = 90_000;
    private const uint UPDATE_CHECK_INTERVAL_MS = 24 * 3600 * 1000;

    private const int CMD_REFRESH = 10;
    private const int CMD_SETTINGS = 11;
    private const int CMD_OPEN = 12;
    private const int CMD_QUIT = 13;
    private const int CMD_UPDATE = 14;

    private const int HK_REFRESH = 1;
    private const int HK_OPEN = 2;
    private const int HK_PROFILE = 3;
    private const int HK_TOGGLE_BAR = 4;
    private const int CMD_HIDE = 16;

    /// <summary>True while the bar is hidden via the toggle hotkey; the AppBar reservation is released meanwhile.</summary>
    private static bool s_hidden;

    private static nint s_hwnd;
    private static uint s_taskbarCreated;
    private static ActivityState s_state = new() { HasData = false };
    private static Settings s_settings = new();
    private static int s_lastMinute = -1;
    private static readonly Dictionary<uint, nint> s_brushCache = [];
    private static nint s_borderPen;
    private static int s_boxLeft;
    private static int s_boxRight;
    private static string s_lastSysText = "";
    private static int s_pomoLeft;
    private static int s_pomoRight;
    private static string s_lastPomoText = "";
    private static int s_focusLeft;
    private static int s_focusRight;
    private static int s_batteryLeft;
    private static int s_batteryRight;
    private static int s_volumeLeft;
    private static int s_volumeRight;
    private static int s_statusLeftLimit;
    private static int s_clockLeft;
    private static int s_clockRight;
    private static int s_refreshing;
    private static int s_resumeRefreshAttempts;
    private static readonly List<nint> s_powerNotificationHandles = [];
    private static double s_scale = 1.0;
    private static nint s_font;
    private static StreakInfo? s_streak;
    private static GuardianStatus s_guardian = GuardianStatus.None;
    private static long s_guardianAlertedDay = long.MinValue;
    private static int s_guardianLeft;
    private static int s_guardianRight;
    private static int s_updateLeft;
    private static int s_updateRight;

    /// <summary>Scales a 96-DPI design pixel value to the current system DPI.</summary>
    internal static int S(int px) => (int)Math.Round(px * s_scale);
    internal static float Sf(float px) => (float)(px * s_scale);

    private static unsafe int Main()
    {
        // Single instance. After a self-update the new exe is started while the old one is still
        // shutting down, so in that case wait for the mutex instead of giving up immediately.
        bool relaunched = Environment.GetCommandLineArgs().Contains(Updater.UpdatedArg, StringComparer.Ordinal);
        using var mutex = new Mutex(false, @"Local\MonkeyBar-TopBar");
        bool owned;
        try { owned = mutex.WaitOne(relaunched ? 15_000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) return 0;

        s_settings = Settings.Load();
        _ = Updater.CleanupOldBinary();
        Native.SetProcessDPIAware();
        InitScaling();
        s_taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");

        nint hInstance = Native.GetModuleHandleW(null);

        var wc = new Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
            lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WndProc,
            hInstance = hInstance,
            hCursor = Native.LoadCursorW(default, Native.IDC_ARROW),
            lpszClassName = "TopBar",
        };
        _ = Native.RegisterClassExW(ref wc);

        SettingsDialog.RegisterClass(hInstance);
        CalendarPopup.RegisterClass(hInstance);

        s_hwnd = Native.CreateWindowExW(
            Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            "TopBar",
            "MonkeyBar",
            Native.WS_POPUP | Native.WS_VISIBLE,
            0, 0, 400, s_settings.BarHeight,
            default, default, hInstance, default);

        if (s_hwnd == default)
        {
            return Marshal.GetLastWin32Error();
        }

        RegisterAppBar(s_hwnd);
        MoveAppBar(s_hwnd);
        ApplyHotkeys();
        RestartTimer();
        ArmClockTimer(s_hwnd);
        if (s_settings.ShowCpuRam)
        {
            Native.SetTimer(s_hwnd, TIMER_SYSINFO, 5000, default);
            SysInfo.Poll();
        }
        if (s_settings.ShowPomodoro)
        {
            Native.SetTimer(s_hwnd, TIMER_POMO, 300000, default);
            _ = RefreshPomodoroAsync();
        }
        if (s_settings.ShowBattery)
        {
            RegisterPowerNotifications(s_hwnd);
            BatteryInfo.Poll();
        }
        if (s_settings.ShowVolume) VolumeInfo.Start(s_hwnd);
        if (s_settings.ShowFocusTimer) FocusTimer.Reset(s_settings.FocusDurationMinutes);
        _ = RefreshAsync();
        Native.SetTimer(s_hwnd, TIMER_UPDATE, UPDATE_FIRST_CHECK_MS, default);

        while (Native.GetMessageW(out Native.MSG msg, default, 0, 0) > 0)
        {
            _ = Native.TranslateMessage(ref msg);
            _ = Native.DispatchMessageW(ref msg);
        }

        return 0;
    }

    /// <summary>
    /// Reads the system DPI and creates a DPI-scaled UI font. DEFAULT_GUI_FONT does not
    /// scale, which is why fixed-pixel layouts overlapped on 125%/150% displays.
    /// </summary>
    private static void InitScaling()
    {
        nint screenDc = Native.GetDC(default);
        if (screenDc != default)
        {
            int dpi = Native.GetDeviceCaps(screenDc, Native.LOGPIXELSX);
            if (dpi > 0) s_scale = dpi / 96.0;
            _ = Native.ReleaseDC(default, screenDc);
        }

        s_font = Native.CreateFontW(
            -S(12), 0, 0, 0, Native.FW_NORMAL, 0, 0, 0,
            Native.DEFAULT_CHARSET, Native.OUT_DEFAULT_PRECIS, Native.CLIP_DEFAULT_PRECIS,
            Native.CLEARTYPE_QUALITY, Native.DEFAULT_PITCH, "Segoe UI");
    }

    private static nint UiFont() => s_font != default ? s_font : Native.GetStockObject(Native.DEFAULT_GUI_FONT);

    /// <summary>Plays the Windows alarm sound; falls back to the system beep if the file is missing.</summary>
    private static void PlayFocusDoneSound() => PlaySound("Alarm01.wav");

    private static void PlaySound(string mediaFile)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", mediaFile);
        if (!File.Exists(path) || !Native.PlaySoundW(path, default, Native.SND_FILENAME | Native.SND_ASYNC | Native.SND_NODEFAULT))
            _ = Native.MessageBeep(0x00000040);
    }

    private static void RegisterAppBar(nint hwnd)
    {
        var abd = NewAppBarData(hwnd);
        _ = Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
    }

    private static void UnregisterAppBar(nint hwnd)
    {
        var abd = NewAppBarData(hwnd);
        _ = Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
    }

    private static void MoveAppBar(nint hwnd)
    {
        var abd = NewAppBarData(hwnd);
        abd.uEdge = Native.ABE_TOP;
        _ = Native.SystemParametersInfoW(Native.SPI_GETWORKAREA, 0, ref abd.rc, 0);
        abd.rc.Right = abd.rc.Left + Native.GetSystemMetrics(Native.SM_CXSCREEN);
        abd.rc.Bottom = abd.rc.Top + s_settings.BarHeight;

        _ = Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        abd.rc.Bottom = abd.rc.Top + s_settings.BarHeight;
        _ = Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);

        _ = Native.SetWindowPos(
            hwnd,
            default,
            abd.rc.Left, abd.rc.Top,
            abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private static Native.APPBARDATA NewAppBarData(nint hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(),
        hWnd = hwnd,
        uCallbackMessage = WM_APPBAR_CALLBACK,
    };

    private static async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref s_refreshing, 1) != 0) return;
        Settings snap = s_settings.Clone();
        try
        {
            var st = await MonkeytypeService.FetchTypingActivityAsync(snap);
            if (st.HasData || !s_state.HasData) s_state = st;

            // Sequential, never parallel: keeps us well inside the shared 30 req/min budget.
            var streak = await MonkeytypeService.FetchStreakAsync(snap);
            if (streak is not null) s_streak = streak;
        }
        catch
        {
            if (!s_state.HasData)
                s_state = new ActivityState { HasData = false, Dates = s_state.Dates, Counts = s_state.Counts };
        }
        finally
        {
            Volatile.Write(ref s_refreshing, 0);
            _ = Native.PostMessageW(s_hwnd, WM_APP_REFRESH, 0, 0);
        }
    }

    /// <summary>
    /// Runs on the UI thread after each refresh: caches the streak hour offset in settings the
    /// first time we see it (the API allows changing it only once, so it's stable), then
    /// re-evaluates the guardian and pushes fresh data to the calendar popup.
    /// </summary>
    private static void OnDataRefreshed(nint hwnd)
    {
        if (s_streak is { } streak && (!s_settings.StreakHourOffsetKnown || s_settings.StreakHourOffset != streak.HourOffset))
        {
            s_settings.StreakHourOffset = streak.HourOffset;
            s_settings.StreakHourOffsetKnown = true;
            try { s_settings.Save(); } catch { }
        }
        EvaluateGuardian(hwnd, playSound: true);
        CalendarPopup.Update(s_state, s_streak, s_guardian, s_settings);
    }

    /// <summary>Cheap local computation; called every minute and after each refresh.</summary>
    private static void EvaluateGuardian(nint hwnd, bool playSound)
    {
        GuardianStatus prev = s_guardian;
        s_guardian = StreakGuardian.Evaluate(s_streak, s_settings, DateTimeOffset.Now);

        if (playSound && s_guardian.IsWarning && s_guardianAlertedDay != s_guardian.StreakDay)
        {
            s_guardianAlertedDay = s_guardian.StreakDay;
            PlaySound("Windows Notify System Generic.wav");
        }

        if (prev.IsWarning != s_guardian.IsWarning || (s_guardian.IsWarning && prev.TimeLeftText != s_guardian.TimeLeftText))
        {
            InvalidateStatusArea(hwnd);
            var boxes = new Native.RECT { Left = s_boxLeft - 4, Top = 0, Right = s_boxRight + 4, Bottom = s_settings.BarHeight };
            _ = Native.InvalidateRect(hwnd, ref boxes, false);
        }
    }

    // ---- Self-update -------------------------------------------------------------------------------

    private static void CheckForUpdates(bool manual)
    {
        if (!manual && (s_settings.UpdateMode == 0 || Updater.IsDevBuild)) return;
        _ = Updater.CheckAsync(s_hwnd, WM_APP_UPDATE, manual);
    }

    private static void InstallUpdate()
    {
        if (Updater.State != UpdateState.Available) return;
        _ = Updater.InstallAsync(s_hwnd, WM_APP_UPDATE);
    }

    /// <summary>Runs on the UI thread whenever the updater changes state.</summary>
    private static void OnUpdateStateChanged(nint hwnd, nuint wParam)
    {
        if (wParam == 2)
        {
            // Exe already swapped on disk: start the new one and leave. The new instance waits
            // for our mutex, then removes TopBar.old.exe.
            Updater.Relaunch();
            Native.DestroyWindow(hwnd);
            return;
        }

        if (Updater.State == UpdateState.Available && s_settings.UpdateMode == 2 && !FocusTimer.IsRunning)
            InstallUpdate();

        InvalidateStatusArea(hwnd);
    }

    /// <summary>Text and colour of the bar indicator; null when nothing should be shown.</summary>
    private static (string Text, uint Color)? UpdateIndicator()
    {
        const uint green = 0x0070C088;
        const uint amber = 0x003CA0E8;
        const uint dim = 0x00999999;
        return Updater.State switch
        {
            UpdateState.Available when Updater.Available is { } u => ($"Update {u.Tag}", green),
            UpdateState.Installing => ("Updating…", dim),
            UpdateState.Restarting => ("Restarting…", dim),
            UpdateState.Failed => ("Update failed", amber),
            _ => null,
        };
    }

    private static void OnUpdateIndicatorClicked()
    {
        switch (Updater.State)
        {
            case UpdateState.Available:
                InstallUpdate();
                break;
            case UpdateState.Failed:
                // Manual fallback: let the user grab the exe from the release page.
                _ = Native.ShellExecuteW(s_hwnd, "open", Updater.Available?.ReleaseUrl ?? Updater.ReleasesPage, null, null, Native.SW_SHOWNORMAL);
                Updater.Dismiss();
                InvalidateStatusArea(s_hwnd);
                break;
        }
    }

    private static void RegisterPowerNotifications(nint hwnd)
    {
        if (s_powerNotificationHandles.Count != 0) return;
        RegisterPowerNotification(hwnd, Native.GUID_ACDC_POWER_SOURCE);
        RegisterPowerNotification(hwnd, Native.GUID_BATTERY_PERCENTAGE_REMAINING);
        RegisterPowerNotification(hwnd, Native.GUID_POWER_SAVING_STATUS);
    }

    private static void RegisterPowerNotification(nint hwnd, Guid setting)
    {
        nint handle = Native.RegisterPowerSettingNotification(hwnd, ref setting, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (handle != default) s_powerNotificationHandles.Add(handle);
    }

    private static void UnregisterPowerNotifications()
    {
        foreach (nint handle in s_powerNotificationHandles) _ = Native.UnregisterPowerSettingNotification(handle);
        s_powerNotificationHandles.Clear();
    }

    internal static nint GetBrush(uint color)
    {
        if (!s_brushCache.TryGetValue(color, out nint b))
        {
            b = Native.CreateSolidBrush(color);
            s_brushCache[color] = b;
        }
        return b;
    }

    /// <summary>
    /// Arms a one-shot timer that fires just after the next minute boundary,
    /// so the clock wakes up once per minute instead of polling every 5 s.
    /// </summary>
    private static void ArmClockTimer(nint hwnd)
    {
        var now = DateTime.Now;
        int msIntoMinute = now.Second * 1000 + now.Millisecond;
        uint delay = (uint)(60_000 - msIntoMinute + 50);
        Native.SetTimer(hwnd, TIMER_CLOCK, delay, default);
    }

    private static void RestartTimer()
    {
        Native.KillTimer(s_hwnd, TIMER_REFRESH);
        Native.SetTimer(s_hwnd, TIMER_REFRESH, (uint)s_settings.RefreshInterval * 1000, default);
    }

    private static void ApplyHotkeys()
    {
        Native.UnregisterHotKey(s_hwnd, HK_REFRESH);
        Native.UnregisterHotKey(s_hwnd, HK_OPEN);
        Native.UnregisterHotKey(s_hwnd, HK_PROFILE);
        Native.UnregisterHotKey(s_hwnd, HK_TOGGLE_BAR);
        TryRegisterHotKey(HK_REFRESH, s_settings.HotkeyRefresh, 'R');
        TryRegisterHotKey(HK_OPEN, s_settings.HotkeyOpenMonkeytype, 'M');
        TryRegisterHotKey(HK_PROFILE, s_settings.HotkeyOpenProfile, 'P');
        TryRegisterHotKey(HK_TOGGLE_BAR, s_settings.HotkeyToggleBar, 'H');
    }

    private static void TryRegisterHotKey(int id, string? spec, char fallbackKey)
    {
        uint mod = Native.MOD_WIN | Native.MOD_SHIFT;
        uint key = fallbackKey;
        if (Hotkey.TryParse(spec, out uint m, out uint k))
        {
            mod = m;
            key = k;
        }
        Native.RegisterHotKey(s_hwnd, id, mod, key);
    }

    private static void OpenMonkeytype()
    {
        string url = "https://monkeytype.com";
        if (s_settings.RightClickAction == "profile" && s_settings.Username.Length > 0)
            url = $"https://monkeytype.com/profile/{Uri.EscapeDataString(s_settings.Username)}";
        _ = Native.ShellExecuteW(s_hwnd, "open", url, null, null, Native.SW_SHOWNORMAL);
    }

    private static void OpenProfile()
    {
        if (s_settings.Username.Length == 0)
        {
            SettingsDialog.Show(s_hwnd, s_settings, OnSettingsSaved);
            return;
        }
        _ = Native.ShellExecuteW(s_hwnd, "open", $"https://monkeytype.com/profile/{Uri.EscapeDataString(s_settings.Username)}", null, null, Native.SW_SHOWNORMAL);
    }

    /// <summary>
    /// Hides or restores the bar. Hiding also removes the AppBar work-area reservation so
    /// maximized windows reclaim the strip; restoring re-registers and re-docks it.
    /// </summary>
    private static void ToggleBarVisibility(nint hwnd)
    {
        if (!s_hidden)
        {
            s_hidden = true;
            CalendarPopup.Hide();
            UnregisterAppBar(hwnd);
            _ = Native.ShowWindow(hwnd, Native.SW_HIDE);
        }
        else
        {
            s_hidden = false;
            RegisterAppBar(hwnd);
            MoveAppBar(hwnd); // SWP_SHOWWINDOW inside makes it visible again without activating
            _ = Native.InvalidateRect(hwnd, default, true);
        }
    }

    private static void OpenRoundPie()
    {
        _ = Native.ShellExecuteW(s_hwnd, "open", "https://my.rpie.me/", null, null, Native.SW_SHOWNORMAL);
    }

    private static void OnSettingsSaved()
    {
        ApplyHotkeys();
        ApplyAutostart();
        RestartTimer();
        MoveAppBar(s_hwnd);
        if (s_settings.ShowCpuRam)
        {
            if (SysInfo.Text.Length == 0) SysInfo.Poll();
            Native.SetTimer(s_hwnd, TIMER_SYSINFO, 5000, default);
        }
        else
        {
            Native.KillTimer(s_hwnd, TIMER_SYSINFO);
            SysInfo.Reset();
        }
        if (s_settings.ShowPomodoro)
        {
            Native.SetTimer(s_hwnd, TIMER_POMO, 300000, default);
            _ = RefreshPomodoroAsync();
        }
        else
        {
            Native.KillTimer(s_hwnd, TIMER_POMO);
        }
        if (s_settings.ShowBattery)
        {
            RegisterPowerNotifications(s_hwnd);
            BatteryInfo.Poll();
        }
        else
        {
            UnregisterPowerNotifications();
            BatteryInfo.Reset();
        }
        if (s_settings.ShowVolume)
        {
            VolumeInfo.Start(s_hwnd);
            VolumeInfo.Refresh();
        }
        else
        {
            VolumeInfo.Stop();
        }
        if (!s_settings.ShowFocusTimer)
        {
            Native.KillTimer(s_hwnd, TIMER_FOCUS);
        }
        else if (!FocusTimer.IsRunning)
        {
            FocusTimer.Reset(s_settings.FocusDurationMinutes);
        }
        CalendarPopup.Hide();
        EvaluateGuardian(s_hwnd, playSound: false);
        if (s_settings.UpdateMode == 0) Updater.Dismiss();
        else if (Updater.State == UpdateState.Idle) CheckForUpdates(manual: false);
        _ = RefreshAsync();
        _ = Native.InvalidateRect(s_hwnd, default, true);
    }

    private static async Task RefreshPomodoroAsync()
    {
        await Pomodoro.RefreshAsync(s_settings);
        if (Pomodoro.TodayText != s_lastPomoText)
        {
            s_lastPomoText = Pomodoro.TodayText;
            var region = new Native.RECT { Left = 0, Top = 0, Right = Math.Max(s_pomoRight, 200) + 16, Bottom = s_settings.BarHeight };
            _ = Native.InvalidateRect(s_hwnd, ref region, false);
        }
    }

    private static void ApplyAutostart()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (s_settings.StartWithWindows && Environment.ProcessPath is { Length: > 0 } path)
                key.SetValue("MonkeyBar", $"\"{path}\"");
            else
                key.DeleteValue("MonkeyBar", false);
        }
        catch
        {
        }
    }

    private static void ShowMenu()
    {
        nint menu = Native.CreatePopupMenu();
        ActivityState st = s_state;

        if (!st.HasData)
        {
            AppendDisabled(menu, "No test data available");
        }
        else if (st.IsStreakOnly)
        {
            AppendDisabled(menu, $"Current Streak: {st.Streak} day{(st.Streak == 1 ? "" : "s")}");
            AppendDisabled(menu, $"Max Streak: {st.MaxStreak} day{(st.MaxStreak == 1 ? "" : "s")}");
            AppendDisabled(menu, "Add an ApeKey in Settings for daily activity");
        }
        else
        {
            for (int i = 0; i < st.Dates.Length; i++)
            {
                int c = st.Counts[i];
                string day = st.Dates[i] == DateTime.Today ? "Today" : st.Dates[i].ToString("MMM d");
                AppendDisabled(menu, $"{day}: {c} test{(c == 1 ? "" : "s")}");
            }
        }

        _ = Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
        _ = Native.AppendMenuW(menu, Native.MF_STRING, CMD_REFRESH, "Refresh Now");
        _ = Native.AppendMenuW(menu, Native.MF_STRING, CMD_SETTINGS, "Settings");
        _ = Native.AppendMenuW(menu, Native.MF_STRING, CMD_OPEN, "Open Monkeytype");
        _ = Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
        string updateItem = Updater.State switch
        {
            UpdateState.Available when Updater.Available is { } u => $"Install update {u.Tag}",
            UpdateState.Checking => "Checking for updates…",
            UpdateState.Installing or UpdateState.Restarting => "Installing update…",
            _ => "Check for updates",
        };
        bool updateBusy = Updater.State is UpdateState.Checking or UpdateState.Installing or UpdateState.Restarting;
        _ = Native.AppendMenuW(menu, Native.MF_STRING | (updateBusy ? Native.MF_GRAYED : 0), CMD_UPDATE, updateItem);
        AppendDisabled(menu, $"MonkeyBar {Updater.VersionText}");
        _ = Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
        _ = Native.AppendMenuW(menu, Native.MF_STRING, CMD_HIDE, $"Hide bar\t{s_settings.HotkeyToggleBar}");
        _ = Native.AppendMenuW(menu, Native.MF_STRING, CMD_QUIT, "Quit");

        _ = Native.GetCursorPos(out Native.POINT pt);
        _ = Native.SetForegroundWindow(s_hwnd);
        int cmd = Native.TrackPopupMenuEx(menu, Native.TPM_LEFTALIGN | Native.TPM_NONOTIFY | Native.TPM_RETURNCMD, pt.X, pt.Y, s_hwnd, default);
        _ = Native.DestroyMenu(menu);

        switch (cmd)
        {
            case CMD_REFRESH:
                _ = RefreshAsync();
                break;
            case CMD_SETTINGS:
                SettingsDialog.Show(s_hwnd, s_settings, OnSettingsSaved);
                break;
            case CMD_OPEN:
                OpenMonkeytype();
                break;
            case CMD_UPDATE:
                if (Updater.State == UpdateState.Available) InstallUpdate();
                else CheckForUpdates(manual: true);
                break;
            case CMD_HIDE:
                ToggleBarVisibility(s_hwnd);
                break;
            case CMD_QUIT:
                Native.DestroyWindow(s_hwnd);
                break;
        }
    }

    private static bool IsOverBoxes(nint lParam)
    {
        int x = (short)(lParam & 0xFFFF);
        return s_boxRight > s_boxLeft && x >= s_boxLeft - 2 && x <= s_boxRight + 2;
    }

    private static bool IsOverPomo(nint lParam)
    {
        int x = (short)(lParam & 0xFFFF);
        return s_pomoRight > s_pomoLeft && x >= s_pomoLeft - 2 && x <= s_pomoRight + 4;
    }

    private static bool IsOverVolume(nint lParam)
    {
        int x = (short)(lParam & 0xFFFF);
        return IsOverVolumeX(x);
    }

    private static bool IsOverVolumeX(int x)
    {
        return s_volumeRight > s_volumeLeft && x >= s_volumeLeft - 3 && x <= s_volumeRight + 3;
    }

    private static bool IsOverFocusTimer(nint lParam)
    {
        int x = (short)(lParam & 0xFFFF);
        return s_focusRight > s_focusLeft && x >= s_focusLeft - 3 && x <= s_focusRight + 3;
    }

    private static bool IsOverUpdate(nint lParam)
    {
        int x = (short)(lParam & 0xFFFF);
        return s_updateRight > s_updateLeft && x >= s_updateLeft - 3 && x <= s_updateRight + 3;
    }

    private static bool IsOverClock(nint lParam)
    {
        int x = (short)(lParam & 0xFFFF);
        return s_clockRight > s_clockLeft && x >= s_clockLeft && x <= s_clockRight;
    }

    private static void ToggleCalendar(nint hwnd)
    {
        _ = Native.GetWindowRect(hwnd, out Native.RECT bar);
        int centerX = bar.Left + (s_clockLeft + s_clockRight) / 2;
        CalendarPopup.Update(s_state, s_streak, s_guardian, s_settings);
        CalendarPopup.Toggle(hwnd, centerX, bar.Bottom);
    }

    private static void ToggleFocusTimer(nint hwnd)
    {
        if (FocusTimer.Toggle(s_settings.FocusDurationMinutes))
            Native.SetTimer(hwnd, TIMER_FOCUS, 1000, default);
        else
            Native.KillTimer(hwnd, TIMER_FOCUS);
        InvalidateFocusArea(hwnd);
    }

    private static void ResetFocusTimer(nint hwnd)
    {
        FocusTimer.Reset(s_settings.FocusDurationMinutes);
        Native.KillTimer(hwnd, TIMER_FOCUS);
        InvalidateFocusArea(hwnd);
    }

    private static void InvalidateFocusArea(nint hwnd)
    {
        if (s_focusRight <= s_focusLeft)
        {
            _ = Native.InvalidateRect(hwnd, default, false);
            return;
        }

        var region = new Native.RECT { Left = s_focusLeft - 4, Top = 0, Right = s_focusRight + 4, Bottom = s_settings.BarHeight };
        _ = Native.InvalidateRect(hwnd, ref region, false);
    }

    private static void InvalidateClockArea(nint hwnd)
    {
        if (s_clockRight <= s_clockLeft)
        {
            _ = Native.InvalidateRect(hwnd, default, false);
            return;
        }

        var region = new Native.RECT { Left = s_clockLeft, Top = 0, Right = s_clockRight, Bottom = s_settings.BarHeight };
        _ = Native.InvalidateRect(hwnd, ref region, false);
    }

    private static void InvalidateStatusArea(nint hwnd)
    {
        if (s_boxLeft <= s_statusLeftLimit)
        {
            _ = Native.InvalidateRect(hwnd, default, false);
            return;
        }

        var region = new Native.RECT { Left = s_statusLeftLimit - 8, Top = 0, Right = s_boxLeft, Bottom = s_settings.BarHeight };
        _ = Native.InvalidateRect(hwnd, ref region, false);
    }

    private static void AppendDisabled(nint menu, string text)
    {
        _ = Native.AppendMenuW(menu, Native.MF_STRING | Native.MF_GRAYED, 0, text);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_ERASEBKGND:
                return new nint(1);

            case Native.WM_PAINT:
                Paint(hwnd);
                return default;

            case Native.WM_LBUTTONUP:
                if (IsOverPomo(lParam)) OpenRoundPie();
                else if (IsOverFocusTimer(lParam)) ToggleFocusTimer(hwnd);
                else if (IsOverVolume(lParam)) VolumeInfo.ToggleMute();
                else if (IsOverUpdate(lParam)) OnUpdateIndicatorClicked();
                else if (IsOverBoxes(lParam)) OpenMonkeytype();
                else if (IsOverClock(lParam)) ToggleCalendar(hwnd);
                return default;

            case Native.WM_RBUTTONUP:
                if (IsOverBoxes(lParam)) ShowMenu();
                else if (IsOverFocusTimer(lParam)) ResetFocusTimer(hwnd);
                else if (IsOverUpdate(lParam))
                {
                    // Hide the indicator until the next daily check.
                    Updater.Dismiss();
                    InvalidateStatusArea(hwnd);
                }
                return default;

            case Native.WM_MOUSEWHEEL:
                var wheelPoint = new Native.POINT { X = (short)(lParam & 0xFFFF), Y = (short)((lParam >> 16) & 0xFFFF) };
                _ = Native.ScreenToClient(hwnd, ref wheelPoint);
                if (IsOverVolumeX(wheelPoint.X))
                {
                    VolumeInfo.Adjust((short)((wParam >> 16) & 0xFFFF));
                    InvalidateStatusArea(hwnd);
                }
                return default;

            case Native.WM_TIMER:
                if (wParam == (nuint)TIMER_REFRESH) _ = RefreshAsync();
                else if (wParam == (nuint)TIMER_CLOCK)
                {
                    int m = DateTime.Now.Minute;
                    if (m != s_lastMinute)
                    {
                        s_lastMinute = m;
                        InvalidateClockArea(hwnd);
                    }
                    EvaluateGuardian(hwnd, playSound: true);
                    if (CalendarPopup.IsVisible) CalendarPopup.Update(s_state, s_streak, s_guardian, s_settings);
                    ArmClockTimer(hwnd);
                }
                else if (wParam == (nuint)TIMER_SYSINFO)
                {
                    SysInfo.Poll();
                    if (SysInfo.Text != s_lastSysText)
                    {
                        s_lastSysText = SysInfo.Text;
                        InvalidateStatusArea(hwnd);
                    }
                }
                else if (wParam == (nuint)TIMER_POMO)
                {
                    _ = RefreshPomodoroAsync();
                }
                else if (wParam == (nuint)TIMER_RESUME_REFRESH)
                {
                    _ = RefreshAsync();
                    _ = RefreshPomodoroAsync();
                    if (--s_resumeRefreshAttempts > 0)
                        Native.SetTimer(hwnd, TIMER_RESUME_REFRESH, 20000, default);
                    else
                        Native.KillTimer(hwnd, TIMER_RESUME_REFRESH);
                }
                else if (wParam == (nuint)TIMER_FOCUS)
                {
                    if (FocusTimer.Tick(s_settings.FocusDurationMinutes))
                    {
                        Native.KillTimer(hwnd, TIMER_FOCUS);
                        PlayFocusDoneSound();
                        // A deferred auto-install (we don't restart mid-focus) can go ahead now.
                        if (Updater.State == UpdateState.Available && s_settings.UpdateMode == 2) InstallUpdate();
                    }
                    InvalidateFocusArea(hwnd);
                }
                else if (wParam == (nuint)TIMER_UPDATE)
                {
                    // First fire is 90 s after launch, then daily. One unauthenticated GitHub
                    // API call per check; the limit is 60/hour per IP.
                    Native.SetTimer(hwnd, TIMER_UPDATE, UPDATE_CHECK_INTERVAL_MS, default);
                    _ = Updater.CleanupOldBinary();
                    CheckForUpdates(manual: false);
                }
                return default;

            case WM_APP_REFRESH:
                OnDataRefreshed(hwnd);
                _ = Native.InvalidateRect(hwnd, default, true);
                return default;

            case WM_APP_UPDATE:
                OnUpdateStateChanged(hwnd, wParam);
                return default;

            case WM_APP_VOLUME:
                VolumeInfo.Refresh();
                InvalidateStatusArea(hwnd);
                return default;

            case Native.WM_POWERBROADCAST:
                if (wParam == (nuint)Native.PBT_POWERSETTINGCHANGE || wParam == (nuint)Native.PBT_APMPOWERSTATUSCHANGE)
                {
                    if (s_settings.ShowBattery)
                    {
                        BatteryInfo.Poll();
                        InvalidateStatusArea(hwnd);
                    }
                }
                else if (wParam == (nuint)Native.PBT_APMRESUMESUSPEND
                    || wParam == (nuint)Native.PBT_APMRESUMEAUTOMATIC
                    || wParam == (nuint)Native.PBT_APMRESUMECRITICAL)
                {
                    s_resumeRefreshAttempts = 2;
                    Native.KillTimer(hwnd, TIMER_RESUME_REFRESH);
                    Native.SetTimer(hwnd, TIMER_RESUME_REFRESH, 8000, default);
                    if (s_settings.ShowBattery) BatteryInfo.Poll();
                    if (s_settings.ShowVolume) VolumeInfo.Refresh();
                    if (s_settings.ShowFocusTimer && FocusTimer.Tick(s_settings.FocusDurationMinutes))
                    {
                        Native.KillTimer(hwnd, TIMER_FOCUS);
                        PlayFocusDoneSound();
                    }
                    InvalidateStatusArea(hwnd);
                    InvalidateFocusArea(hwnd);
                }
                return new nint(1);

            case Native.WM_HOTKEY:
                switch ((int)wParam)
                {
                    case HK_REFRESH:
                        _ = RefreshAsync();
                        break;
                    case HK_OPEN:
                        OpenMonkeytype();
                        break;
                    case HK_PROFILE:
                        OpenProfile();
                        break;
                    case HK_TOGGLE_BAR:
                        ToggleBarVisibility(hwnd);
                        break;
                }
                return default;

            case Native.WM_DISPLAYCHANGE:
                if (!s_hidden) MoveAppBar(hwnd);
                return default;

            case Native.WM_DESTROY:
                CalendarPopup.Destroy();
                UnregisterAppBar(hwnd);
                Native.UnregisterHotKey(hwnd, HK_REFRESH);
                Native.UnregisterHotKey(hwnd, HK_OPEN);
                Native.UnregisterHotKey(hwnd, HK_PROFILE);
                Native.UnregisterHotKey(hwnd, HK_TOGGLE_BAR);
                Native.KillTimer(hwnd, TIMER_REFRESH);
                Native.KillTimer(hwnd, TIMER_CLOCK);
                Native.KillTimer(hwnd, TIMER_SYSINFO);
                Native.KillTimer(hwnd, TIMER_POMO);
                Native.KillTimer(hwnd, TIMER_RESUME_REFRESH);
                Native.KillTimer(hwnd, TIMER_FOCUS);
                Native.KillTimer(hwnd, TIMER_UPDATE);
                UnregisterPowerNotifications();
                VolumeInfo.Stop();
                foreach (nint b in s_brushCache.Values) Native.DeleteObject(b);
                s_brushCache.Clear();
                if (s_borderPen != default)
                {
                    Native.DeleteObject(s_borderPen);
                    s_borderPen = default;
                }
                if (s_font != default)
                {
                    Native.DeleteObject(s_font);
                    s_font = default;
                }
                Native.PostQuitMessage(0);
                return default;

            default:
                if (msg == s_taskbarCreated && s_taskbarCreated != 0)
                {
                    // Explorer restarted: re-dock, unless the user has the bar hidden right now.
                    if (!s_hidden)
                    {
                        RegisterAppBar(hwnd);
                        MoveAppBar(hwnd);
                    }
                    return default;
                }
                return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    internal static uint Blend(uint fg, uint bg, double alpha)
    {
        int fr = (int)(fg & 0xFF), fgc = (int)((fg >> 8) & 0xFF), fb = (int)((fg >> 16) & 0xFF);
        int br = (int)(bg & 0xFF), bgc = (int)((bg >> 8) & 0xFF), bb = (int)((bg >> 16) & 0xFF);
        int r = (int)Math.Round(fr * alpha + br * (1.0 - alpha));
        int g = (int)Math.Round(fgc * alpha + bgc * (1.0 - alpha));
        int b = (int)Math.Round(fb * alpha + bb * (1.0 - alpha));
        return (uint)(r | (g << 8) | (b << 16));
    }

    private static void Paint(nint hwnd)
    {
        nint hdc = Native.BeginPaint(hwnd, out Native.PAINTSTRUCT ps);
        if (hdc == default) return;
        try
        {
            _ = Native.GetClientRect(hwnd, out Native.RECT rc);
            // Only the invalidated strip needs clearing; GDI clips everything else anyway,
            // but skipping the calls saves the work of issuing them.
            Native.RECT dirty = ps.rcPaint;
            _ = Native.FillRect(hdc, ref dirty, GetBrush(BAR_BG));

            Settings s = s_settings;
            ActivityState st = s_state;

            var theme = Themes.Get(s.ThemeName);
            int days = Math.Clamp(s.DaysToShow, 1, 7);
            int box = S(BOX_SIZE);
            int boxGap = S(BOX_GAP);
            int margin = S(12);
            int y = ((rc.Bottom - rc.Top) - box) / 2;
            int cy = y + box / 2;
            int totalW = days * box + (days - 1) * boxGap;
            int x = rc.Right - margin - totalW;
            s_boxLeft = x;
            s_boxRight = x + totalW;

            _ = Native.SelectObject(hdc, UiFont());
            Native.SetBkMode(hdc, Native.TRANSPARENT);

            string pomoText = s.ShowPomodoro ? Pomodoro.TodayText : "";
            if (pomoText.Length > 0)
            {
                // Geometry is always computed (hit-testing depends on it); drawing is conditional.
                Native.SIZE sz = default;
                _ = Native.GetTextExtentPoint32W(hdc, pomoText, pomoText.Length, ref sz);
                int dot = S(9);
                int textLeft = margin + dot + S(6);
                s_pomoLeft = margin;
                s_pomoRight = textLeft + sz.cx + S(6);

                if (Hits(dirty, s_pomoLeft, s_pomoRight))
                {
                    _ = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
                    _ = Native.SelectObject(hdc, GetBrush(0x003C54E8));
                    _ = Native.Ellipse(hdc, margin, cy - dot / 2, margin + dot, cy - dot / 2 + dot);

                    Native.SetTextColor(hdc, 0x00DDDDDD);
                    var pomoRc = new Native.RECT { Left = textLeft, Top = rc.Top, Right = textLeft + sz.cx + 2, Bottom = rc.Bottom };
                    _ = Native.DrawTextW(hdc, pomoText, -1, ref pomoRc, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE);
                }
            }
            else
            {
                s_pomoLeft = 0;
                s_pomoRight = 0;
            }

            s_focusLeft = s_focusRight = 0;
            if (s.ShowFocusTimer)
            {
                FocusTimerSnapshot focus = FocusTimer.GetSnapshot(s.FocusDurationMinutes);
                int focusX = pomoText.Length > 0 ? s_pomoRight + S(16) : margin;
                int ringSize = S(18);
                int ringInset = S(4);
                int ringTop = cy - ringSize / 2;
                int ringRight = focusX + ringSize;

                string focusText = focus.Text;
                Native.SIZE focusSize = default;
                _ = Native.GetTextExtentPoint32W(hdc, focusText, focusText.Length, ref focusSize);
                var focusRc = new Native.RECT { Left = ringRight + S(7), Top = rc.Top, Right = ringRight + S(9) + focusSize.cx, Bottom = rc.Bottom };
                s_focusLeft = focusX;
                s_focusRight = focusRc.Right + S(4);

                if (Hits(dirty, s_focusLeft, s_focusRight))
                {
                    _ = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
                    _ = Native.SelectObject(hdc, GetBrush(0x00444A4A));
                    _ = Native.Ellipse(hdc, focusX, ringTop, ringRight, ringTop + ringSize);

                    if (focus.Progress >= 0.999)
                    {
                        _ = Native.SelectObject(hdc, GetBrush(0x003C54E8));
                        _ = Native.Ellipse(hdc, focusX, ringTop, ringRight, ringTop + ringSize);
                    }
                    else if (focus.Progress > 0.001)
                    {
                        double end = -Math.PI / 2 + Math.PI * 2 * focus.Progress;
                        int cxRing = focusX + ringSize / 2;
                        int cyRing = ringTop + ringSize / 2;
                        int radius = ringSize / 2;
                        int sx = cxRing;
                        int sy = ringTop;
                        int ex = cxRing + (int)Math.Round(Math.Cos(end) * radius);
                        int ey = cyRing + (int)Math.Round(Math.Sin(end) * radius);
                        _ = Native.SelectObject(hdc, GetBrush(0x003C54E8));
                        _ = Native.Pie(hdc, focusX, ringTop, ringRight, ringTop + ringSize, sx, sy, ex, ey);
                    }

                    _ = Native.SelectObject(hdc, GetBrush(BAR_BG));
                    _ = Native.Ellipse(hdc, focusX + ringInset, ringTop + ringInset, ringRight - ringInset, ringTop + ringSize - ringInset);

                    Native.SetTextColor(hdc, focus.IsRunning ? 0x00DDDDDDu : 0x00999999u);
                    _ = Native.DrawTextW(hdc, focusText, -1, ref focusRc, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE);
                }
            }

            if (Hits(dirty, s_boxLeft - 2, s_boxRight + 2))
            {
                if (s_borderPen == default)
                    s_borderPen = Native.CreatePen(Native.PS_SOLID, 1, Blend(0x00FFFFFF, BAR_BG, 0.08));
                _ = Native.SelectObject(hdc, s_borderPen);
                DrawActivityBoxes(hdc, s, st, theme, days, x, y, box, boxGap);
            }

            // Clock is centered; status widgets may extend leftwards no further than the clock's right edge.
            int clientW = rc.Right - rc.Left;
            Native.SIZE clockSize = default;
            _ = Native.GetTextExtentPoint32W(hdc, "00:00", 5, ref clockSize);
            int clockHalf = clockSize.cx / 2 + S(8);
            s_clockLeft = clientW / 2 - clockHalf;
            s_clockRight = clientW / 2 + clockHalf;
            s_statusLeftLimit = s_clockRight + S(16);

            int statusRight = s_boxLeft - S(10);
            string? sys = s.ShowCpuRam && SysInfo.Text.Length > 0 ? SysInfo.Text : null;
            bool statusFits = true;

            s_updateLeft = s_updateRight = 0;
            if (UpdateIndicator() is { } upd)
            {
                statusFits = DrawDotStatus(hdc, upd.Text, upd.Color, ref statusRight, s_statusLeftLimit, rc, dirty, out s_updateLeft, out s_updateRight);
            }

            s_guardianLeft = s_guardianRight = 0;
            if (statusFits && s_guardian.IsWarning)
            {
                statusFits = DrawDotStatus(hdc, s_guardian.TimeLeftText + " left", 0x003CA0E8, ref statusRight, s_statusLeftLimit, rc, dirty, out s_guardianLeft, out s_guardianRight);
            }

            if (statusFits && s.ShowCpuRam)
            {
                s_lastSysText = sys ?? "";
                statusFits = DrawCpuStatus(hdc, sys, ref statusRight, s_statusLeftLimit, rc, dirty);
            }

            s_volumeLeft = s_volumeRight = 0;
            if (statusFits && s.ShowVolume && VolumeInfo.Text.Length > 0)
            {
                statusFits = DrawVolumeStatus(hdc, VolumeInfo.Text, ref statusRight, s_statusLeftLimit, rc, dirty, out s_volumeLeft, out s_volumeRight);
            }

            s_batteryLeft = s_batteryRight = 0;
            if (statusFits && s.ShowBattery && BatteryInfo.Text.Length > 0)
            {
                _ = DrawBatteryStatus(hdc, BatteryInfo.Text, ref statusRight, s_statusLeftLimit, rc, dirty, out s_batteryLeft, out s_batteryRight);
            }

            var now = DateTime.Now;
            s_lastMinute = now.Minute;
            if (Hits(dirty, s_clockLeft, s_clockRight))
            {
                Native.SetTextColor(hdc, 0x00DDDDDD);
                var clockRc = new Native.RECT { Left = s_clockLeft, Top = rc.Top, Right = s_clockRight, Bottom = rc.Bottom };
                _ = Native.DrawTextW(
                    hdc,
                    now.ToString("HH:mm"),
                    -1,
                    ref clockRc,
                    Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE);
            }
        }
        finally
        {
            _ = Native.EndPaint(hwnd, ref ps);
        }
    }

    /// <summary>True when the horizontal span [left, right) overlaps the invalidated rect.</summary>
    private static bool Hits(in Native.RECT dirty, int left, int right) => right > dirty.Left && left < dirty.Right;

    /// <summary>Coloured dot + short text (streak guardian warning, update notice).</summary>
    private static bool DrawDotStatus(nint hdc, string text, uint color, ref int right, int leftLimit, Native.RECT bounds, in Native.RECT dirty, out int left, out int itemRight)
    {
        Native.SIZE size = default;
        _ = Native.GetTextExtentPoint32W(hdc, text, text.Length, ref size);
        int dot = S(7);
        int gap = S(5);
        itemRight = right;
        left = right - (dot + gap + size.cx);
        if (left < leftLimit)
        {
            left = itemRight = 0;
            return false;
        }

        if (Hits(dirty, left, right))
        {
            int cy = (bounds.Bottom - bounds.Top) / 2;
            _ = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
            _ = Native.SelectObject(hdc, GetBrush(color));
            _ = Native.Ellipse(hdc, left, cy - dot / 2, left + dot + 1, cy - dot / 2 + dot + 1);
            Native.SetTextColor(hdc, color);
            var textRc = new Native.RECT { Left = left + dot + gap, Top = bounds.Top, Right = right, Bottom = bounds.Bottom };
            _ = Native.DrawTextW(hdc, text, -1, ref textRc, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE);
        }
        right = left - S(STATUS_GAP);
        return true;
    }

    private static void DrawActivityBoxes(nint hdc, Settings s, ActivityState st, Theme theme, int days, int x, int y, int box, int boxGap)
    {
        int radius = S(BOX_RADIUS) * 2;
        bool warn = s_guardian.IsWarning;
        for (int i = 0; i < days; i++)
        {
            int count = st.HasData && !st.IsStreakOnly && i < st.Counts.Length ? st.Counts[i] : 0;

            uint fill;
            double alpha;
            if (count == 0)
            {
                fill = 0x00FFFFFF;
                alpha = 0.12;
            }
            else if (s.ColorMode == 1)
            {
                fill = Themes.GradeColor(theme, count);
                alpha = 1.0;
            }
            else
            {
                fill = theme.Grade3;
                alpha = Math.Min(50 + count * 20, 255) / 255.0;
            }

            _ = Native.SelectObject(hdc, GetBrush(Blend(fill, BAR_BG, alpha)));
            _ = Native.RoundRect(hdc, x, y, x + box - 1, y + box - 1, radius, radius);

            bool isToday = st.HasData && i < st.Dates.Length && st.Dates[i] == DateTime.Today;
            if (isToday && (warn || (s.HighlightCurrentDay && !st.IsStreakOnly)))
            {
                // Streak at risk overrides the normal highlight with an amber ring.
                nint hlBrush = GetBrush(warn ? 0x003CA0E8u : Blend(0x00FFFFFF, BAR_BG, 0.6));
                var outer = new Native.RECT { Left = x - 2, Top = y - 2, Right = x + box + 1, Bottom = y + box + 1 };
                var inner = new Native.RECT { Left = x - 1, Top = y - 1, Right = x + box, Bottom = y + box };
                _ = Native.FrameRect(hdc, ref outer, hlBrush);
                _ = Native.FrameRect(hdc, ref inner, hlBrush);
            }

            x += box + boxGap;
        }
    }

    // Typical widest string for the CPU/RAM widget (two-digit values). Reserving at least
    // this width keeps neighbours from shifting as digits change; the rare 100% case simply
    // grows the slot for a moment. Measuring with the real font avoids the DPI overlap bug.
    private const string CPU_STATUS_TYPICAL = "CPU 00% \u00b7 RAM 00%";

    private static bool DrawCpuStatus(nint hdc, string? text, ref int right, int leftLimit, Native.RECT bounds, in Native.RECT dirty)
    {
        Native.SIZE size = default;
        _ = Native.GetTextExtentPoint32W(hdc, CPU_STATUS_TYPICAL, CPU_STATUS_TYPICAL.Length, ref size);
        int width = size.cx;
        if (!string.IsNullOrEmpty(text))
        {
            Native.SIZE actual = default;
            _ = Native.GetTextExtentPoint32W(hdc, text, text.Length, ref actual);
            width = Math.Max(width, actual.cx);
        }
        int left = right - (width + S(2));
        if (left < leftLimit)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(text) && Hits(dirty, left, right))
        {
            Native.SetTextColor(hdc, 0x00888888);
            var textRc = new Native.RECT { Left = left, Top = bounds.Top, Right = right, Bottom = bounds.Bottom };
            _ = Native.DrawTextW(hdc, text, -1, ref textRc, Native.DT_RIGHT | Native.DT_VCENTER | Native.DT_SINGLELINE);
        }
        right = left - S(STATUS_GAP);
        return true;
    }

    private static bool DrawVolumeStatus(nint hdc, string text, ref int right, int leftLimit, Native.RECT bounds, in Native.RECT dirty, out int left, out int itemRight)
    {
        Native.SIZE size = default;
        _ = Native.GetTextExtentPoint32W(hdc, text, text.Length, ref size);
        int iconW = S(14);
        int textGap = S(5);
        itemRight = right;
        left = right - (iconW + textGap + size.cx);
        if (left < leftLimit)
        {
            left = itemRight = 0;
            return false;
        }

        if (!Hits(dirty, left, right))
        {
            right = left - S(STATUS_GAP);
            return true;
        }

        int cy = (bounds.Bottom - bounds.Top) / 2;
        uint iconColor = VolumeInfo.IsMuted ? 0x00666666u : 0x00C8C8C8u;
        _ = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
        _ = Native.SelectObject(hdc, GetBrush(iconColor));
        Native.POINT[] speaker =
        [
            new() { X = left, Y = cy - S(3) }, new() { X = left + S(4), Y = cy - S(3) },
            new() { X = left + S(9), Y = cy - S(7) }, new() { X = left + S(9), Y = cy + S(7) },
            new() { X = left + S(4), Y = cy + S(3) }, new() { X = left, Y = cy + S(3) },
        ];
        _ = Native.Polygon(hdc, speaker, speaker.Length);
        if (VolumeInfo.IsMuted)
        {
            _ = Native.SelectObject(hdc, GetBrush(0x004040D8));
            _ = Native.Ellipse(hdc, left + S(10), cy - S(2), left + S(14), cy + S(2));
        }

        Native.SetTextColor(hdc, 0x00C8C8C8);
        var textRc = new Native.RECT { Left = left + iconW + textGap, Top = bounds.Top, Right = right, Bottom = bounds.Bottom };
        _ = Native.DrawTextW(hdc, text, -1, ref textRc, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE);
        right = left - S(STATUS_GAP);
        return true;
    }

    private static bool DrawBatteryStatus(nint hdc, string text, ref int right, int leftLimit, Native.RECT bounds, in Native.RECT dirty, out int left, out int itemRight)
    {
        Native.SIZE size = default;
        _ = Native.GetTextExtentPoint32W(hdc, text, text.Length, ref size);
        int bodyW = S(14);
        int capW = S(2);
        int textGap = S(4);
        bool charging = BatteryInfo.IsCharging;
        // Bolt sits to the left of the body (percentage is on the right), so it only takes space when plugged in.
        int boltW = charging ? S(6) : 0;
        int boltGap = charging ? S(3) : 0;
        itemRight = right;
        left = right - (boltW + boltGap + bodyW + capW + textGap + size.cx);
        if (left < leftLimit)
        {
            left = itemRight = 0;
            return false;
        }

        if (!Hits(dirty, left, right))
        {
            right = left - S(STATUS_GAP);
            return true;
        }

        int cy = (bounds.Bottom - bounds.Top) / 2;
        int halfH = S(5);
        uint outline = 0x00C8C8C8;
        uint fill = BatteryInfo.Percent <= 20 && !charging ? 0x004040D8u : 0x0088C070u;

        if (charging)
        {
            int bx = left + boltW / 2;
            _ = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
            _ = Native.SelectObject(hdc, GetBrush(0x0088C070));
            Native.POINT[] bolt =
            [
                new() { X = bx + S(1), Y = cy - halfH },
                new() { X = bx - S(3), Y = cy + S(1) },
                new() { X = bx,        Y = cy + S(1) },
                new() { X = bx - S(1), Y = cy + halfH },
                new() { X = bx + S(3), Y = cy - S(1) },
                new() { X = bx,        Y = cy - S(1) },
            ];
            _ = Native.Polygon(hdc, bolt, bolt.Length);
        }

        int bodyLeft = left + boltW + boltGap;
        var body = new Native.RECT { Left = bodyLeft, Top = cy - halfH, Right = bodyLeft + bodyW, Bottom = cy + halfH };
        _ = Native.FrameRect(hdc, ref body, GetBrush(outline));
        var cap = new Native.RECT { Left = bodyLeft + bodyW, Top = cy - S(2), Right = bodyLeft + bodyW + capW, Bottom = cy + S(2) };
        _ = Native.FillRect(hdc, ref cap, GetBrush(outline));

        int innerW = bodyW - S(4);
        int chargeWidth = Math.Clamp((BatteryInfo.Percent * innerW) / 100, 1, innerW);
        var charge = new Native.RECT { Left = bodyLeft + S(2), Top = cy - (halfH - S(2)), Right = bodyLeft + S(2) + chargeWidth, Bottom = cy + (halfH - S(2)) };
        _ = Native.FillRect(hdc, ref charge, GetBrush(fill));

        Native.SetTextColor(hdc, 0x00C8C8C8);
        var textRc = new Native.RECT { Left = bodyLeft + bodyW + capW + textGap, Top = bounds.Top, Right = right, Bottom = bounds.Bottom };
        _ = Native.DrawTextW(hdc, text, -1, ref textRc, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE);
        right = left - S(STATUS_GAP);
        return true;
    }
}

internal static class Hotkey
{
    public static bool TryParse(string? spec, out uint mod, out uint key)
    {
        mod = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "WIN" or "SUPER": mod |= Native.MOD_WIN; break;
                case "CTRL" or "CONTROL": mod |= Native.MOD_CONTROL; break;
                case "ALT": mod |= Native.MOD_ALT; break;
                case "SHIFT": mod |= Native.MOD_SHIFT; break;
                default: return false;
            }
        }

        string k = parts[^1].ToUpperInvariant();
        if (k.Length == 1 && (char.IsAsciiLetterOrDigit(k[0]))) key = k[0];
        else if (k.Length == 2 && k[0] == 'F' && char.IsAsciiDigit(k[1]) && int.TryParse(k.AsSpan(1), out int f) && f is >= 1 and <= 12) key = (uint)(0x70 + f - 1);
        else return false;

        return mod != 0 && key != 0;
    }
}
