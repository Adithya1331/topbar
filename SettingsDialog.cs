using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TopBar;

internal static class SettingsDialog
{
    private const int CLIENT_W = 500;
    private const int CLIENT_H = 812;

    private const uint WM_SETTEXT = 0x000C;
    private const uint BN_CLICKED = 0;

    private const int IDC_SAVE = 1;
    private const int IDC_CANCEL = 2;
    private const int IDC_GETKEY = 118;
    private const int IDC_HIGHLIGHT = 105;
    private const int IDC_WEEKONLY = 106;
    private const int IDC_AUTOSTART = 112;
    private const int IDC_CPURAM = 113;
    private const int IDC_POMO = 114;
    private const int IDC_BATTERY = 116;
    private const int IDC_VOLUME = 117;
    private const int IDC_FOCUS = 119;
    private const int IDC_GUARDIAN = 121;
    private const int CTL_WARN_HOURS = 122;
    private const int CTL_UPDATES = 123;

    private const int CTL_USER = 101;
    private const int CTL_KEY = 102;
    private const int CTL_INTERVAL = 103;
    private const int CTL_DAYS = 104;
    private const int CTL_HEIGHT = 111;
    private const int CTL_WEEKSTART = 107;
    private const int CTL_TOKEN = 115;
    private const int CTL_MODE = 108;
    private const int CTL_THEME = 109;
    private const int CTL_ACTION = 110;
    private const int CTL_FOCUS_DURATION = 120;

    private static nint s_dlg;
    private static nint s_owner;
    private static Action? s_onSaved;
    private static Settings s_settings = new();
    private static readonly Dictionary<int, nint> s_ctl = [];

    private static readonly int[] Intervals = [900, 1800, 3600, 7200, 14400, 21600, 43200, 86400];
    private static readonly string[] IntervalLabels = ["15 minutes", "30 minutes", "1 hour", "2 hours", "4 hours", "6 hours", "12 hours", "24 hours"];
    private static readonly int[] Heights = [24, 28, 32, 36, 40, 48, 56, 64];
    private static readonly string[] WeekDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] ModeLabels = ["Opacity Mode", "Grade Mode"];
    private static readonly string[] ActionLabels = ["Open Homepage", "Open Profile"];
    private static readonly int[] FocusDurations = [15, 25, 45, 50, 60];
    private static readonly int[] WarnHours = [1, 2, 3, 4, 6, 8];
    private static readonly string[] UpdateLabels = ["Off", "Notify me in the bar", "Install automatically"];

    public static unsafe void RegisterClass(nint hInstance)
    {
        var wc = new Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
            lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&DlgProc,
            hInstance = hInstance,
            hCursor = Native.LoadCursorW(default, Native.IDC_ARROW),
            hbrBackground = Native.COLOR_WINDOW + 1,
            lpszClassName = "MonkeyBarSettings",
        };
        _ = Native.RegisterClassExW(ref wc);
    }

    public static void Show(nint owner, Settings settings, Action onSaved)
    {
        if (s_dlg != default)
        {
            _ = Native.SetForegroundWindow(s_dlg);
            return;
        }

        s_owner = owner;
        s_settings = settings;
        s_onSaved = onSaved;

        var style = Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX;
        var frame = new Native.RECT { Right = CLIENT_W, Bottom = CLIENT_H };
        _ = Native.AdjustWindowRect(ref frame, style, false);

        Native.EnableWindow(owner, false);
        s_dlg = Native.CreateWindowExW(
            0,
            "MonkeyBarSettings",
            "MonkeyBar Settings",
            style | Native.WS_VISIBLE,
            100, 100,
            frame.Right - frame.Left,
            frame.Bottom - frame.Top,
            owner, default,
            Native.GetModuleHandleW(null), default);

        if (s_dlg == default)
        {
            Native.EnableWindow(owner, true);
        }
    }

    private static nint AddControl(nint parent, string cls, string text, uint style, uint ex, int x, int y, int w, int h, int id, nint font)
    {
        nint c = Native.CreateWindowExW(
            ex, cls, text,
            Native.WS_CHILD | Native.WS_VISIBLE | style,
            x, y, w, h,
            parent, id, Native.GetModuleHandleW(null), default);
        _ = Native.SendMessageW(c, Native.WM_SETFONT, font, 1);
        s_ctl[id] = c;
        return c;
    }

    private static void Label(nint parent, string text, int x, int y, nint font, int id)
    {
        AddControl(parent, "STATIC", text, 0, 0, x, y, 140, 18, id, font);
    }

    private static nint Combo(nint parent, int id, int x, int y, nint font)
    {
        return AddControl(parent, "COMBOBOX", "", Native.CBS_DROPDOWNLIST, 0, x, y, 320, 200, id, font);
    }

    private static void FillCombo(nint cb, string[] items, int selected)
    {
        foreach (var item in items) _ = Native.SendMessageW(cb, Native.CB_ADDSTRING, 0, item);
        _ = Native.SendMessageW(cb, Native.CB_SETCURSEL, Math.Max(0, selected), 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint DlgProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_CREATE:
                CreateControls(hwnd);
                return default;

            case Native.WM_COMMAND:
                int id = (int)(wParam & 0xFFFF);
                int note = (int)((wParam >> 16) & 0xFFFF);
                if (note == BN_CLICKED)
                {
                    switch (id)
                    {
                        case IDC_SAVE:
                            SaveAndClose(hwnd);
                            return default;
                        case IDC_CANCEL:
                            Native.DestroyWindow(hwnd);
                            return default;
                        case IDC_GETKEY:
                            _ = Native.ShellExecuteW(hwnd, "open", "https://monkeytype.com/account-settings?tab=apeKeys", null, null, Native.SW_SHOWNORMAL);
                            return default;
                        case IDC_WEEKONLY:
                            Native.EnableWindow(s_ctl.GetValueOrDefault(CTL_WEEKSTART), IsChecked(IDC_WEEKONLY));
                            return default;
                    }
                }
                return default;

            case Native.WM_CLOSE:
                Native.DestroyWindow(hwnd);
                return default;

            case Native.WM_DESTROY:
                s_dlg = default;
                s_ctl.Clear();
                Native.EnableWindow(s_owner, true);
                _ = Native.SetForegroundWindow(s_owner);
                return default;

            default:
                return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private static void CreateControls(nint hwnd)
    {
        s_ctl.Clear();
        nint font = Native.GetStockObject(Native.DEFAULT_GUI_FONT);
        const int lx = 16;
        const int cx = 160;
        const int cw = 320;

        Label(hwnd, "Monkeytype Username", lx, 24, font, 200);
        AddControl(hwnd, "EDIT", "", Native.ES_AUTOHSCROLL, Native.WS_EX_CLIENTEDGE, cx, 20, cw, 24, CTL_USER, font);

        Label(hwnd, "Monkeytype ApeKey", lx, 56, font, 201);
        AddControl(hwnd, "EDIT", "", Native.ES_AUTOHSCROLL | Native.ES_PASSWORD, Native.WS_EX_CLIENTEDGE, cx, 52, cw, 24, CTL_KEY, font);
        AddControl(hwnd, "BUTTON", "Get ApeKey (opens monkeytype.com)", Native.BS_PUSHBUTTON, 0, cx, 84, cw, 26, IDC_GETKEY, font);

        Label(hwnd, "Refresh Interval", lx, 126, font, 202);
        Combo(hwnd, CTL_INTERVAL, cx, 122, font);

        Label(hwnd, "Days to Show (1-7)", lx, 158, font, 203);
        Combo(hwnd, CTL_DAYS, cx, 154, font);

        Label(hwnd, "Bar Height (px)", lx, 190, font, 209);
        Combo(hwnd, CTL_HEIGHT, cx, 186, font);

        AddControl(hwnd, "BUTTON", "Highlight current day", Native.BS_AUTOCHECKBOX, 0, cx, 224, cw, 20, IDC_HIGHLIGHT, font);
        AddControl(hwnd, "BUTTON", "Show current week only", Native.BS_AUTOCHECKBOX, 0, cx, 250, cw, 20, IDC_WEEKONLY, font);
        AddControl(hwnd, "BUTTON", "Start with Windows (launch at login)", Native.BS_AUTOCHECKBOX, 0, cx, 276, cw, 20, IDC_AUTOSTART, font);
        AddControl(hwnd, "BUTTON", "Show CPU / RAM usage", Native.BS_AUTOCHECKBOX, 0, cx, 302, cw, 20, IDC_CPURAM, font);
        AddControl(hwnd, "BUTTON", "Show Pomodoro (today time)", Native.BS_AUTOCHECKBOX, 0, cx, 328, cw, 20, IDC_POMO, font);
        AddControl(hwnd, "BUTTON", "Show battery / charging status", Native.BS_AUTOCHECKBOX, 0, cx, 354, cw, 20, IDC_BATTERY, font);
        AddControl(hwnd, "BUTTON", "Show volume (click mute, scroll adjust)", Native.BS_AUTOCHECKBOX, 0, cx, 380, cw, 20, IDC_VOLUME, font);
        AddControl(hwnd, "BUTTON", "Show local focus timer (click start/pause, right-click reset)", Native.BS_AUTOCHECKBOX, 0, cx, 406, cw, 20, IDC_FOCUS, font);
        AddControl(hwnd, "BUTTON", "Streak guardian (warn before the streak day ends, needs ApeKey)", Native.BS_AUTOCHECKBOX, 0, cx, 432, cw, 20, IDC_GUARDIAN, font);
        Label(hwnd, "Focus duration", lx, 464, font, 210);
        Combo(hwnd, CTL_FOCUS_DURATION, cx, 460, font);

        Label(hwnd, "Warn before reset", lx, 496, font, 211);
        Combo(hwnd, CTL_WARN_HOURS, cx, 492, font);

        Label(hwnd, "RoundPie Token (JWT)", lx, 528, font, 209);
        AddControl(hwnd, "EDIT", "", Native.ES_AUTOHSCROLL, Native.WS_EX_CLIENTEDGE, cx, 524, cw, 24, CTL_TOKEN, font);

        Label(hwnd, "Week Starts On", lx, 560, font, 204);
        Combo(hwnd, CTL_WEEKSTART, cx, 556, font);

        Label(hwnd, "Color Mode", lx, 592, font, 205);
        Combo(hwnd, CTL_MODE, cx, 588, font);

        Label(hwnd, "Theme", lx, 624, font, 206);
        Combo(hwnd, CTL_THEME, cx, 620, font);

        Label(hwnd, "Left-click Action", lx, 656, font, 207);
        Combo(hwnd, CTL_ACTION, cx, 652, font);

        Label(hwnd, $"Updates ({Updater.VersionText})", lx, 688, font, 212);
        Combo(hwnd, CTL_UPDATES, cx, 684, font);

        AddControl(hwnd, "BUTTON", "Save", Native.BS_DEFPUSHBUTTON, 0, cx, 722, 100, 30, IDC_SAVE, font);
        AddControl(hwnd, "BUTTON", "Cancel", Native.BS_PUSHBUTTON, 0, cx + 110, 722, 100, 30, IDC_CANCEL, font);

        AddControl(hwnd, "STATIC",
            "Hotkeys: Win+Shift+R refresh · Win+Shift+M Monkeytype · Win+Shift+P profile · Win+Shift+B hide/show bar · click the clock for the calendar",
            0, 0, lx, 764, 468, 32, 208, font);

        FillCombo(s_ctl[CTL_INTERVAL], IntervalLabels, Array.IndexOf(Intervals, s_settings.RefreshInterval));
        FillCombo(s_ctl[CTL_DAYS], ["1", "2", "3", "4", "5", "6", "7"], s_settings.DaysToShow - 1);
        FillCombo(s_ctl[CTL_HEIGHT], Heights.Select(h => h.ToString()).ToArray(), Array.IndexOf(Heights, s_settings.BarHeight));
        FillCombo(s_ctl[CTL_WEEKSTART], WeekDays, s_settings.WeekStartDay);
        FillCombo(s_ctl[CTL_MODE], ModeLabels, s_settings.ColorMode);
        FillCombo(s_ctl[CTL_THEME], Themes.List.Select(t => t.Label).ToArray(), s_settings.ThemeName);
        FillCombo(s_ctl[CTL_ACTION], ActionLabels, s_settings.RightClickAction == "profile" ? 1 : 0);
        FillCombo(s_ctl[CTL_FOCUS_DURATION], FocusDurations.Select(m => $"{m} minutes").ToArray(), Array.IndexOf(FocusDurations, s_settings.FocusDurationMinutes));
        FillCombo(s_ctl[CTL_WARN_HOURS], WarnHours.Select(h => h == 1 ? "1 hour" : $"{h} hours").ToArray(), Array.IndexOf(WarnHours, s_settings.StreakWarnHours));
        FillCombo(s_ctl[CTL_UPDATES], UpdateLabels, s_settings.UpdateMode);

        _ = Native.SendMessageW(s_ctl[CTL_USER], WM_SETTEXT, 0, s_settings.Username);
        _ = Native.SendMessageW(s_ctl[CTL_KEY], WM_SETTEXT, 0, s_settings.ApeKey);
        _ = Native.SendMessageW(s_ctl[CTL_TOKEN], WM_SETTEXT, 0, s_settings.RoundPieToken);
        _ = Native.SendMessageW(s_ctl[IDC_HIGHLIGHT], Native.BM_SETCHECK, s_settings.HighlightCurrentDay ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_WEEKONLY], Native.BM_SETCHECK, s_settings.ShowCurrentWeekOnly ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_AUTOSTART], Native.BM_SETCHECK, s_settings.StartWithWindows ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_CPURAM], Native.BM_SETCHECK, s_settings.ShowCpuRam ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_POMO], Native.BM_SETCHECK, s_settings.ShowPomodoro ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_BATTERY], Native.BM_SETCHECK, s_settings.ShowBattery ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_VOLUME], Native.BM_SETCHECK, s_settings.ShowVolume ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_FOCUS], Native.BM_SETCHECK, s_settings.ShowFocusTimer ? Native.BST_CHECKED : 0, 0);
        _ = Native.SendMessageW(s_ctl[IDC_GUARDIAN], Native.BM_SETCHECK, s_settings.StreakGuardianEnabled ? Native.BST_CHECKED : 0, 0);
        Native.EnableWindow(s_ctl[CTL_WEEKSTART], s_settings.ShowCurrentWeekOnly);
    }

    private static void SaveAndClose(nint hwnd)
    {
        int intervalIdx = (int)Native.SendMessageW(s_ctl[CTL_INTERVAL], Native.CB_GETCURSEL, 0, 0);
        int daysIdx = (int)Native.SendMessageW(s_ctl[CTL_DAYS], Native.CB_GETCURSEL, 0, 0);
        int weekIdx = (int)Native.SendMessageW(s_ctl[CTL_WEEKSTART], Native.CB_GETCURSEL, 0, 0);
        int modeIdx = (int)Native.SendMessageW(s_ctl[CTL_MODE], Native.CB_GETCURSEL, 0, 0);
        int themeIdx = (int)Native.SendMessageW(s_ctl[CTL_THEME], Native.CB_GETCURSEL, 0, 0);
        int actionIdx = (int)Native.SendMessageW(s_ctl[CTL_ACTION], Native.CB_GETCURSEL, 0, 0);
        int focusDurationIdx = (int)Native.SendMessageW(s_ctl[CTL_FOCUS_DURATION], Native.CB_GETCURSEL, 0, 0);

        s_settings.Username = GetText(CTL_USER);
        s_settings.ApeKey = GetText(CTL_KEY);
        s_settings.RoundPieToken = GetText(CTL_TOKEN);
        if (intervalIdx >= 0) s_settings.RefreshInterval = Intervals[intervalIdx];
        if (daysIdx >= 0) s_settings.DaysToShow = daysIdx + 1;
        int heightIdx = (int)Native.SendMessageW(s_ctl[CTL_HEIGHT], Native.CB_GETCURSEL, 0, 0);
        if (heightIdx >= 0) s_settings.BarHeight = Heights[heightIdx];
        if (weekIdx >= 0) s_settings.WeekStartDay = weekIdx;
        if (modeIdx >= 0) s_settings.ColorMode = modeIdx;
        if (themeIdx >= 0) s_settings.ThemeName = themeIdx;
        if (actionIdx >= 0) s_settings.RightClickAction = actionIdx == 1 ? "profile" : "homepage";
        if (focusDurationIdx >= 0) s_settings.FocusDurationMinutes = FocusDurations[focusDurationIdx];
        int warnIdx = (int)Native.SendMessageW(s_ctl[CTL_WARN_HOURS], Native.CB_GETCURSEL, 0, 0);
        if (warnIdx >= 0) s_settings.StreakWarnHours = WarnHours[warnIdx];
        int updateIdx = (int)Native.SendMessageW(s_ctl[CTL_UPDATES], Native.CB_GETCURSEL, 0, 0);
        if (updateIdx >= 0) s_settings.UpdateMode = updateIdx;
        s_settings.StreakGuardianEnabled = IsChecked(IDC_GUARDIAN);
        s_settings.HighlightCurrentDay = IsChecked(IDC_HIGHLIGHT);
        s_settings.ShowCurrentWeekOnly = IsChecked(IDC_WEEKONLY);
        s_settings.StartWithWindows = IsChecked(IDC_AUTOSTART);
        s_settings.ShowCpuRam = IsChecked(IDC_CPURAM);
        s_settings.ShowPomodoro = IsChecked(IDC_POMO);
        s_settings.ShowBattery = IsChecked(IDC_BATTERY);
        s_settings.ShowVolume = IsChecked(IDC_VOLUME);
        s_settings.ShowFocusTimer = IsChecked(IDC_FOCUS);

        s_settings.Save();
        s_onSaved?.Invoke();
        Native.DestroyWindow(hwnd);
    }

    private static bool IsChecked(int id)
    {
        return !s_ctl.TryGetValue(id, out nint c) || Native.SendMessageW(c, Native.BM_GETCHECK, 0, 0) != 0;
    }

    private static string GetText(int id)
    {
        if (!s_ctl.TryGetValue(id, out nint c)) return "";
        var buf = new char[4096];
        int len = Native.GetWindowTextW(c, buf, buf.Length);
        return len > 0 ? new string(buf, 0, len).Trim() : "";
    }
}
