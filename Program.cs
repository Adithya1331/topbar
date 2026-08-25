using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TopBar;

internal static class Program
{
    private const int BOX_SIZE = 14;
    private const int BOX_GAP = 6;
    private const int BOX_RADIUS = 3;
    private const uint BAR_BG = 0x001C1818;

    private const uint WM_APPBAR_CALLBACK = 0x8000 | 0x0001;
    private const uint WM_APP_REFRESH = 0x8000 | 0x0002;

    private const nint TIMER_REFRESH = 1;
    private const nint TIMER_CLOCK = 2;
    private const nint TIMER_SYSINFO = 3;

    private const int CMD_REFRESH = 10;
    private const int CMD_SETTINGS = 11;
    private const int CMD_OPEN = 12;
    private const int CMD_QUIT = 13;

    private const int HK_REFRESH = 1;
    private const int HK_OPEN = 2;
    private const int HK_PROFILE = 3;

    private static readonly Native.WndProc s_wndProc = WndProc;
    private static nint s_hwnd;
    private static uint s_taskbarCreated;
    private static ActivityState s_state = new();
    private static Settings s_settings = new();
    private static int s_lastMinute = -1;
    private static readonly Dictionary<uint, nint> s_brushCache = [];
    private static nint s_borderPen;
    private static bool s_trimmed;
    private static int s_boxLeft;
    private static int s_boxRight;
    private static string s_lastSysText = "";

    private static int Main()
    {
        using var mutex = new Mutex(true, @"Local\MonkeyBar-TopBar", out bool createdNew);
        if (!createdNew) return 0;

        s_settings = Settings.Load();
        Native.SetProcessDPIAware();
        s_taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");

        nint hInstance = Native.GetModuleHandleW(null);

        var wc = new Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
            lpfnWndProc = s_wndProc,
            hInstance = hInstance,
            hCursor = Native.LoadCursorW(default, Native.IDC_ARROW),
            lpszClassName = "TopBar",
        };
        _ = Native.RegisterClassExW(ref wc);

        SettingsDialog.RegisterClass(hInstance);

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
        Native.SetTimer(s_hwnd, TIMER_CLOCK, 5000, default);
        if (s_settings.ShowCpuRam)
        {
            Native.SetTimer(s_hwnd, TIMER_SYSINFO, 5000, default);
            SysInfo.Poll();
        }
        _ = RefreshAsync();

        while (Native.GetMessageW(out Native.MSG msg, default, 0, 0) > 0)
        {
            _ = Native.TranslateMessage(ref msg);
            _ = Native.DispatchMessageW(ref msg);
        }

        return 0;
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
        Settings snap = s_settings.Clone();
        try
        {
            var st = await MonkeytypeService.FetchTypingActivityAsync(snap);
            s_state = st;
        }
        catch
        {
            s_state = new ActivityState { HasData = false, Dates = s_state.Dates, Counts = s_state.Counts };
        }
        TrimWorkingSet();
        Native.PostMessageW(s_hwnd, WM_APP_REFRESH, 0, 0);
    }

    private static void TrimWorkingSet()
    {
        if (s_trimmed) return;
        s_trimmed = true;
        _ = Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), -1, -1);
    }

    private static nint GetBrush(uint color)
    {
        if (!s_brushCache.TryGetValue(color, out nint b))
        {
            b = Native.CreateSolidBrush(color);
            s_brushCache[color] = b;
        }
        return b;
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
        TryRegisterHotKey(HK_REFRESH, s_settings.HotkeyRefresh, 'R');
        TryRegisterHotKey(HK_OPEN, s_settings.HotkeyOpenMonkeytype, 'M');
        TryRegisterHotKey(HK_PROFILE, s_settings.HotkeyOpenProfile, 'P');
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
        _ = RefreshAsync();
        _ = Native.InvalidateRect(s_hwnd, default, true);
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

    private static void AppendDisabled(nint menu, string text)
    {
        _ = Native.AppendMenuW(menu, Native.MF_STRING | Native.MF_GRAYED, 0, text);
    }

    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_PAINT:
                Paint(hwnd);
                return default;

            case Native.WM_LBUTTONUP:
                if (IsOverBoxes(lParam)) OpenMonkeytype();
                return default;

            case Native.WM_RBUTTONUP:
                if (IsOverBoxes(lParam)) ShowMenu();
                return default;

            case Native.WM_TIMER:
                if (wParam == (nuint)TIMER_REFRESH) _ = RefreshAsync();
                else if (wParam == (nuint)TIMER_CLOCK)
                {
                    int m = DateTime.Now.Minute;
                    if (m != s_lastMinute)
                    {
                        s_lastMinute = m;
                        _ = Native.InvalidateRect(hwnd, default, false);
                    }
                }
                else if (wParam == (nuint)TIMER_SYSINFO)
                {
                    SysInfo.Poll();
                    if (SysInfo.Text != s_lastSysText)
                    {
                        s_lastSysText = SysInfo.Text;
                        _ = Native.InvalidateRect(hwnd, default, false);
                    }
                }
                return default;

            case WM_APP_REFRESH:
                _ = Native.InvalidateRect(hwnd, default, true);
                return default;

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
                }
                return default;

            case Native.WM_DISPLAYCHANGE:
                MoveAppBar(hwnd);
                return default;

            case Native.WM_DESTROY:
                UnregisterAppBar(hwnd);
                Native.UnregisterHotKey(hwnd, HK_REFRESH);
                Native.UnregisterHotKey(hwnd, HK_OPEN);
                Native.UnregisterHotKey(hwnd, HK_PROFILE);
                Native.KillTimer(hwnd, TIMER_REFRESH);
                Native.KillTimer(hwnd, TIMER_CLOCK);
                Native.KillTimer(hwnd, TIMER_SYSINFO);
                foreach (nint b in s_brushCache.Values) Native.DeleteObject(b);
                s_brushCache.Clear();
                if (s_borderPen != default)
                {
                    Native.DeleteObject(s_borderPen);
                    s_borderPen = default;
                }
                Native.PostQuitMessage(0);
                return default;

            default:
                if (msg == s_taskbarCreated && s_taskbarCreated != 0)
                {
                    RegisterAppBar(hwnd);
                    MoveAppBar(hwnd);
                    return default;
                }
                return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private static uint Blend(uint fg, uint bg, double alpha)
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
            _ = Native.FillRect(hdc, ref rc, GetBrush(BAR_BG));

            Settings s = s_settings;
            ActivityState st = s_state;

            var theme = Themes.Get(s.ThemeName);
            int days = Math.Clamp(s.DaysToShow, 1, 7);
            int y = ((rc.Bottom - rc.Top) - BOX_SIZE) / 2;
            int totalW = days * BOX_SIZE + (days - 1) * BOX_GAP;
            int x = rc.Right - 12 - totalW;
            s_boxLeft = x;
            s_boxRight = x + totalW;

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

                if (s_borderPen == default)
                    s_borderPen = Native.CreatePen(Native.PS_SOLID, 1, Blend(0x00FFFFFF, BAR_BG, 0.08));
                _ = Native.SelectObject(hdc, s_borderPen);
                _ = Native.SelectObject(hdc, GetBrush(Blend(fill, BAR_BG, alpha)));
                _ = Native.RoundRect(hdc, x, y, x + BOX_SIZE - 1, y + BOX_SIZE - 1, BOX_RADIUS * 2, BOX_RADIUS * 2);

                if (s.HighlightCurrentDay
                    && st.HasData && !st.IsStreakOnly
                    && i < st.Dates.Length && st.Dates[i] == DateTime.Today)
                {
                    nint hlBrush = GetBrush(Blend(0x00FFFFFF, BAR_BG, 0.6));
                    var outer = new Native.RECT { Left = x - 2, Top = y - 2, Right = x + BOX_SIZE + 1, Bottom = y + BOX_SIZE + 1 };
                    var inner = new Native.RECT { Left = x - 1, Top = y - 1, Right = x + BOX_SIZE, Bottom = y + BOX_SIZE };
                    _ = Native.FrameRect(hdc, ref outer, hlBrush);
                    _ = Native.FrameRect(hdc, ref inner, hlBrush);
                }

                x += BOX_SIZE + BOX_GAP;
            }

            Native.SetBkMode(hdc, Native.TRANSPARENT);
            _ = Native.SelectObject(hdc, Native.GetStockObject(Native.DEFAULT_GUI_FONT));

            int clientW = rc.Right - rc.Left;

            string? sys = s.ShowCpuRam && SysInfo.Text.Length > 0 ? SysInfo.Text : null;
            if (sys is not null)
            {
                s_lastSysText = sys;
                Native.SetTextColor(hdc, 0x00888888);
                var textRc = new Native.RECT { Left = clientW / 2 + 160, Top = rc.Top, Right = s_boxLeft - 10, Bottom = rc.Bottom };
                _ = Native.DrawTextW(
                    hdc,
                    sys,
                    -1,
                    ref textRc,
                    Native.DT_RIGHT | Native.DT_VCENTER | Native.DT_SINGLELINE);
            }

            Native.SetTextColor(hdc, 0x00DDDDDD);
            var now = DateTime.Now;
            s_lastMinute = now.Minute;
            var clockRc = new Native.RECT { Left = clientW / 2 - 150, Top = rc.Top, Right = clientW / 2 + 150, Bottom = rc.Bottom };
            _ = Native.DrawTextW(
                hdc,
                now.ToString("HH:mm"),
                -1,
                ref clockRc,
                Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE);
        }
        finally
        {
            _ = Native.EndPaint(hwnd, ref ps);
        }
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
