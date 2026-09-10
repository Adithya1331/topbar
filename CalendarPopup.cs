using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TopBar;

/// <summary>
/// Month-view flyout under the clock, styled after the Windows 11 clock flyout: date header,
/// month/year with â–²â–¼ navigation, weekday row, 6-week grid. Days with tests get a tinted circle in
/// the theme colour, consecutive active days are joined into a pill so streaks read at a glance,
/// and today is a filled accent circle. Pure GDI, double-buffered, DWM rounded corners.
/// </summary>
internal static class CalendarPopup
{
    private const string ClassName = "MonkeyBarCalendar";
    private const uint BG = 0x001C1818;
    private const uint TEXT = 0x00E6E6E6;
    private const uint DIM = 0x00858585;
    private const uint FAINT = 0x00505050;
    private const uint AMBER = 0x003CA0E8;
    private const uint GREEN = 0x0070C088;

    private static nint s_hwnd;
    private static nint s_owner;
    private static nint s_fontRegular;
    private static nint s_fontSmall;
    private static nint s_fontTitle;
    private static nint s_borderPen;
    private static nint s_tooltipPen;

    private static ActivityState s_state = new() { HasData = false };
    private static StreakInfo? s_streak;
    private static GuardianStatus s_guardian = GuardianStatus.None;
    private static Settings s_settings = new();

    private static DateTime s_viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private static int s_hoverCell = -1;      // 0..41, or -1
    private static int s_hoverButton = 0;     // 0 none, 1 prev, 2 next
    private static bool s_tracking;
    private static Native.RECT s_workArea;

    public static bool IsVisible => s_hwnd != default && Native.IsWindowVisible(s_hwnd);

    public static unsafe void RegisterClass(nint hInstance)
    {
        var wc = new Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
            style = Native.CS_DROPSHADOW,
            lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WndProc,
            hInstance = hInstance,
            hCursor = Native.LoadCursorW(default, Native.IDC_ARROW),
            lpszClassName = ClassName,
        };
        _ = Native.RegisterClassExW(ref wc);
    }

    public static void Update(ActivityState state, StreakInfo? streak, GuardianStatus guardian, Settings settings)
    {
        s_state = state;
        s_streak = streak;
        s_guardian = guardian;
        s_settings = settings;
        if (IsVisible) _ = Native.InvalidateRect(s_hwnd, default, false);
    }

    /// <summary>Show centred under (anchorCenterX, anchorBottomY) in screen coordinates, or hide if already shown.</summary>
    public static void Toggle(nint owner, int anchorCenterX, int anchorBottomY)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        s_owner = owner;
        EnsureCreated();

        var today = DateTime.Today;
        s_viewMonth = new DateTime(today.Year, today.Month, 1);
        s_hoverCell = -1;
        s_hoverButton = 0;

        var m = Metrics.Compute();
        s_workArea = default;
        bool haveWorkArea = Native.SystemParametersInfoW(Native.SPI_GETWORKAREA, 0, ref s_workArea, 0)
            && s_workArea.Right > s_workArea.Left;
        if (!haveWorkArea)
        {
            s_workArea = new Native.RECT { Left = 0, Top = 0, Right = Native.GetSystemMetrics(Native.SM_CXSCREEN), Bottom = Native.GetSystemMetrics(Native.SM_CYSCREEN) };
        }

        int margin = Program.S(8);
        int x = anchorCenterX - m.Width / 2;
        int y = anchorBottomY + margin;
        int minX = s_workArea.Left + margin;
        int maxX = s_workArea.Right - margin - m.Width;
        if (maxX >= minX) x = Math.Clamp(x, minX, maxX);

        _ = Native.SetWindowPos(s_hwnd, default, x, y, m.Width, m.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        _ = Native.ShowWindow(s_hwnd, Native.SW_SHOW);
        _ = Native.SetForegroundWindow(s_hwnd);
        _ = Native.InvalidateRect(s_hwnd, default, false);
    }

    public static void Hide()
    {
        if (s_hwnd != default) _ = Native.ShowWindow(s_hwnd, Native.SW_HIDE);
    }

    public static void Destroy()
    {
        if (s_hwnd != default) Native.DestroyWindow(s_hwnd);
    }

    private static void EnsureCreated()
    {
        if (s_hwnd != default) return;

        s_hwnd = Native.CreateWindowExW(
            Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST,
            ClassName, "",
            Native.WS_POPUP,
            0, 0, 10, 10,
            s_owner, default, Native.GetModuleHandleW(null), default);

        int round = Native.DWMWCP_ROUND;
        _ = Native.DwmSetWindowAttribute(s_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        s_fontRegular = MakeFont(13, Native.FW_NORMAL);
        s_fontSmall = MakeFont(11, Native.FW_NORMAL);
        s_fontTitle = MakeFont(14, Native.FW_SEMIBOLD);
        s_borderPen = Native.CreatePen(Native.PS_SOLID, 1, Program.Blend(0x00FFFFFF, BG, 0.10));
        s_tooltipPen = Native.CreatePen(Native.PS_SOLID, 1, Program.Blend(0x00FFFFFF, BG, 0.18));
    }

    private static nint MakeFont(int px, int weight) => Native.CreateFontW(
        -Program.S(px), 0, 0, 0, weight, 0, 0, 0,
        Native.DEFAULT_CHARSET, Native.OUT_DEFAULT_PRECIS, Native.CLIP_DEFAULT_PRECIS,
        Native.CLEARTYPE_QUALITY, Native.DEFAULT_PITCH, "Segoe UI");

    /// <summary>All layout numbers in one place so paint and hit-testing agree.</summary>
    private readonly struct Metrics
    {
        public readonly int Pad, CellW, CellH, Circle, HeaderY, DividerY, MonthY, ButtonSize, WeekdayY, GridY, FooterY, Width, Height;

        private Metrics(int pad, int cellW, int cellH, int circle)
        {
            Pad = pad; CellW = cellW; CellH = cellH; Circle = circle;
            HeaderY = pad;
            DividerY = HeaderY + Program.S(22) + Program.S(14);
            MonthY = DividerY + Program.S(14);
            ButtonSize = Program.S(26);
            WeekdayY = MonthY + Program.S(34);
            GridY = WeekdayY + Program.S(24);
            FooterY = GridY + 6 * cellH + Program.S(10);
            Width = pad * 2 + 7 * cellW;
            // Two footer lines: guardian status, then month totals.
            Height = FooterY + Program.S(18) * 2 + Program.S(2) + pad;
        }

        public static Metrics Compute() => new(Program.S(16), Program.S(44), Program.S(40), Program.S(30));

        public Native.RECT PrevButton(int width) => new() { Left = width - Pad - ButtonSize * 2 - Program.S(6), Top = MonthY - Program.S(3), Right = width - Pad - ButtonSize - Program.S(6), Bottom = MonthY - Program.S(3) + ButtonSize };
        public Native.RECT NextButton(int width) => new() { Left = width - Pad - ButtonSize, Top = MonthY - Program.S(3), Right = width - Pad, Bottom = MonthY - Program.S(3) + ButtonSize };
    }

    /// <summary>First date shown in the 6x7 grid for the month being viewed.</summary>
    private static DateTime GridStart(DateTime month, int weekStart)
    {
        int offset = ((int)month.DayOfWeek - weekStart + 7) % 7;
        return month.AddDays(-offset);
    }

    private static DateTime MinMonth()
    {
        DateTime earliest = s_state.ByDay.Count > 0 ? s_state.ByDay.Keys.Min() : DateTime.Today.AddMonths(-12);
        return new DateTime(earliest.Year, earliest.Month, 1);
    }

    private static DateTime MaxMonth() => new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private static bool IsActive(DateTime d)
        => d <= DateTime.Today && d.Month == s_viewMonth.Month && s_state.ByDay.TryGetValue(d, out int c) && c > 0;

    private static uint ActiveFill(int count, Settings s, Theme theme)
        => s.ColorMode == 1 ? Themes.GradeColor(theme, count) : Program.Blend(theme.Grade3, BG, Math.Min(90 + count * 18, 255) / 255.0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_ERASEBKGND:
                return 1;

            case Native.WM_PAINT:
                Paint(hwnd);
                return 0;

            case Native.WM_MOUSEMOVE:
                OnMouseMove(hwnd, (short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                return 0;

            case Native.WM_LBUTTONUP:
                OnClick(hwnd, (short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                return 0;

            case Native.WM_MOUSEWHEEL:
                Navigate(hwnd, (short)((wParam >> 16) & 0xFFFF) > 0 ? -1 : 1);
                return 0;

            case Native.WM_MOUSELEAVE:
                s_tracking = false;
                if (s_hoverCell >= 0 || s_hoverButton != 0)
                {
                    s_hoverCell = -1;
                    s_hoverButton = 0;
                    _ = Native.InvalidateRect(hwnd, default, false);
                }
                return 0;

            case Native.WM_KEYDOWN:
                switch ((uint)wParam)
                {
                    case Native.VK_ESCAPE: Hide(); break;
                    case 0x25 or 0x26: Navigate(hwnd, -1); break; // Left / Up
                    case 0x27 or 0x28: Navigate(hwnd, 1); break;  // Right / Down
                }
                return 0;

            case Native.WM_ACTIVATE:
                if ((wParam & 0xFFFF) == Native.WA_INACTIVE) Hide();
                return 0;

            case Native.WM_DESTROY:
                foreach (nint h in new[] { s_fontRegular, s_fontSmall, s_fontTitle, s_borderPen, s_tooltipPen })
                    if (h != default) Native.DeleteObject(h);
                s_fontRegular = s_fontSmall = s_fontTitle = s_borderPen = s_tooltipPen = default;
                s_hwnd = default;
                return 0;

            default:
                return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private static void Navigate(nint hwnd, int deltaMonths)
    {
        DateTime target = s_viewMonth.AddMonths(deltaMonths);
        if (target < MinMonth() || target > MaxMonth()) return;
        s_viewMonth = target;
        s_hoverCell = -1;
        _ = Native.InvalidateRect(hwnd, default, false);
    }

    private static void OnClick(nint hwnd, int x, int y)
    {
        var m = Metrics.Compute();
        _ = Native.GetClientRect(hwnd, out Native.RECT rc);
        if (Contains(m.PrevButton(rc.Right), x, y)) Navigate(hwnd, -1);
        else if (Contains(m.NextButton(rc.Right), x, y)) Navigate(hwnd, 1);
    }

    private static bool Contains(in Native.RECT r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    private static void OnMouseMove(nint hwnd, int x, int y)
    {
        if (!s_tracking)
        {
            var tme = new Native.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<Native.TRACKMOUSEEVENT>(), dwFlags = Native.TME_LEAVE, hwndTrack = hwnd };
            s_tracking = Native.TrackMouseEvent(ref tme);
        }

        var m = Metrics.Compute();
        _ = Native.GetClientRect(hwnd, out Native.RECT rc);

        int button = Contains(m.PrevButton(rc.Right), x, y) ? 1 : Contains(m.NextButton(rc.Right), x, y) ? 2 : 0;
        int cell = -1;
        if (x >= m.Pad && x < m.Pad + 7 * m.CellW && y >= m.GridY && y < m.GridY + 6 * m.CellH)
        {
            int col = (x - m.Pad) / m.CellW;
            int row = (y - m.GridY) / m.CellH;
            DateTime d = GridStart(s_viewMonth, s_settings.WeekStartDay).AddDays(row * 7 + col);
            if (d <= DateTime.Today && d.Month == s_viewMonth.Month) cell = row * 7 + col;
        }

        if (cell != s_hoverCell || button != s_hoverButton)
        {
            s_hoverCell = cell;
            s_hoverButton = button;
            _ = Native.InvalidateRect(hwnd, default, false);
        }
    }

    private static void Paint(nint hwnd)
    {
        nint hdc = Native.BeginPaint(hwnd, out Native.PAINTSTRUCT ps);
        if (hdc == default) return;
        _ = Native.GetClientRect(hwnd, out Native.RECT rc);
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;

        nint mem = Native.CreateCompatibleDC(hdc);
        nint bmp = Native.CreateCompatibleBitmap(hdc, w, h);
        nint oldBmp = Native.SelectObject(mem, bmp);
        try
        {
            Draw(mem, w, h);
            _ = Native.BitBlt(hdc, 0, 0, w, h, mem, 0, 0, Native.SRCCOPY);
        }
        finally
        {
            _ = Native.SelectObject(mem, oldBmp);
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
            _ = Native.EndPaint(hwnd, ref ps);
        }
    }

    private static void Draw(nint dc, int w, int h)
    {
        var m = Metrics.Compute();
        Settings s = s_settings;
        ActivityState st = s_state;
        Theme theme = Themes.Get(s.ThemeName);
        DateTime today = DateTime.Today;
        DateTime start = GridStart(s_viewMonth, s.WeekStartDay);
        uint accent = s.ColorMode == 1 ? theme.Grade4 : theme.Grade3;

        var full = new Native.RECT { Left = 0, Top = 0, Right = w, Bottom = h };
        _ = Native.FillRect(dc, ref full, Program.GetBrush(BG));
        var divider = new Native.RECT { Left = 0, Top = m.DividerY, Right = w, Bottom = m.DividerY + 1 };
        _ = Native.FillRect(dc, ref divider, Program.GetBrush(Program.Blend(0x00FFFFFF, BG, 0.08)));
        Native.SetBkMode(dc, Native.TRANSPARENT);

        // Text metrics needed to place shapes that sit beside text.
        _ = Native.SelectObject(dc, s_fontRegular);
        int streakDays = s_streak?.Length ?? st.Streak;
        bool showStreak = st.HasData || s_streak is not null;
        string streakLabel = $"{streakDays} day streak";
        Native.SIZE streakSize = Measure(dc, streakLabel);
        _ = Native.SelectObject(dc, s_fontSmall);
        (string status, uint statusColor) = GuardianLine(s);
        Native.SIZE statusSize = Measure(dc, status);

        float r = m.Circle / 2f;
        uint pill = Program.Blend(accent, BG, 0.22);

        // ---- Shapes pass (anti-aliased) ------------------------------------------------------------
        using (var g = new Gdip.Surface(dc))
        {
            if (showStreak)
            {
                float dotR = Program.S(4);
                float dotX = w - m.Pad - streakSize.cx - Program.S(7) - dotR;
                float dotY = m.HeaderY + Program.S(2) + streakSize.cy / 2f;
                g.FillCircle(accent, dotX, dotY, dotR);
            }

            DrawNavButton(g, m.PrevButton(w), up: true, enabled: s_viewMonth > MinMonth(), hovered: s_hoverButton == 1);
            DrawNavButton(g, m.NextButton(w), up: false, enabled: s_viewMonth < MaxMonth(), hovered: s_hoverButton == 2);

            // Streak pills: consecutive active days in a row share one connected background.
            for (int row = 0; row < 6; row++)
            {
                int runStart = -1;
                for (int col = 0; col <= 7; col++)
                {
                    bool active = col < 7 && IsActive(start.AddDays(row * 7 + col));
                    if (active && runStart < 0) runStart = col;
                    if (!active && runStart >= 0)
                    {
                        int runEnd = col - 1;
                        if (runEnd > runStart)
                        {
                            float cy = m.GridY + row * m.CellH + m.CellH / 2f;
                            float x1 = m.Pad + runStart * m.CellW + m.CellW / 2f;
                            float x2 = m.Pad + runEnd * m.CellW + m.CellW / 2f;
                            g.FillPill(pill, x1, x2, cy, r);
                        }
                        runStart = -1;
                    }
                }
            }

            // Day circles.
            for (int i = 0; i < 42; i++)
            {
                DateTime d = start.AddDays(i);
                bool isToday = d == today;
                bool active = IsActive(d);
                int count = st.ByDay.TryGetValue(d, out int c) ? c : 0;
                float cx = m.Pad + (i % 7) * m.CellW + m.CellW / 2f;
                float cy = m.GridY + (i / 7) * m.CellH + m.CellH / 2f;

                if (s_hoverCell == i && !isToday)
                    g.FillCircle(Program.Blend(0x00FFFFFF, BG, 0.10), cx, cy, r + Program.S(2));

                if (active) g.FillCircle(isToday ? accent : ActiveFill(count, s, theme), cx, cy, r);

                if (isToday && !active)
                    g.DrawRing(s_guardian.IsWarning ? AMBER : accent, cx, cy, r, Math.Max(1.5f, Program.S(2)));
            }

            // Guardian status dot (footer line 1).
            float gdR = Program.Sf(3.5f);
            g.FillCircle(statusColor, m.Pad + gdR, m.FooterY + statusSize.cy / 2f, gdR);
        }

        // ---- Text pass (GDI) ------------------------------------------------------------------------
        _ = Native.SelectObject(dc, s_fontRegular);
        Native.SetTextColor(dc, TEXT);
        DrawText(dc, today.ToString("dddd, d MMMM"), m.Pad, m.HeaderY + Program.S(2), Native.DT_LEFT);
        if (showStreak)
        {
            Native.SetTextColor(dc, accent);
            DrawText(dc, streakLabel, w - m.Pad, m.HeaderY + Program.S(2), Native.DT_RIGHT);
        }

        _ = Native.SelectObject(dc, s_fontTitle);
        Native.SetTextColor(dc, TEXT);
        DrawText(dc, s_viewMonth.ToString("MMMM yyyy"), m.Pad, m.MonthY, Native.DT_LEFT);

        _ = Native.SelectObject(dc, s_fontSmall);
        Native.SetTextColor(dc, DIM);
        for (int col = 0; col < 7; col++)
        {
            var dow = (DayOfWeek)((s.WeekStartDay + col) % 7);
            DrawTextCentered(dc, dow.ToString()[..2], m.Pad + col * m.CellW, m.WeekdayY, m.CellW, Program.S(18));
        }

        _ = Native.SelectObject(dc, s_fontRegular);
        for (int i = 0; i < 42; i++)
        {
            DateTime d = start.AddDays(i);
            bool inMonth = d.Month == s_viewMonth.Month;
            bool future = d > today;
            bool isToday = d == today;
            bool active = IsActive(d);

            uint textColor = !inMonth || future ? FAINT : (isToday && active) ? BG : TEXT;
            Native.SetTextColor(dc, textColor);
            int x = m.Pad + (i % 7) * m.CellW;
            int y = m.GridY + (i / 7) * m.CellH;
            DrawTextCentered(dc, d.Day.ToString(), x, y, m.CellW, m.CellH);
        }

        // Footer line 1: guardian; line 2: month totals.
        _ = Native.SelectObject(dc, s_fontSmall);
        Native.SetTextColor(dc, statusColor);
        DrawText(dc, status, m.Pad + Program.S(7) * 2, m.FooterY, Native.DT_LEFT);

        int monthTests = 0, monthDays = 0;
        foreach (var kv in st.ByDay)
        {
            if (kv.Key.Year == s_viewMonth.Year && kv.Key.Month == s_viewMonth.Month && kv.Value > 0) { monthTests += kv.Value; monthDays++; }
        }
        if (st.HasData && !st.IsStreakOnly)
        {
            Native.SetTextColor(dc, DIM);
            string totals = monthTests == 0
                ? $"{s_viewMonth:MMMM}: no tests"
                : $"{s_viewMonth:MMMM}: {monthTests:N0} test{(monthTests == 1 ? "" : "s")} on {monthDays} active day{(monthDays == 1 ? "" : "s")}";
            DrawText(dc, totals, m.Pad, m.FooterY + Program.S(18) + Program.S(2), Native.DT_LEFT);
        }

        // ---- Border ------------------------------------------------------------------------------------
        _ = Native.SelectObject(dc, s_borderPen);
        _ = Native.SelectObject(dc, Native.GetStockObject(Native.NULL_BRUSH));
        _ = Native.Rectangle(dc, 0, 0, w, h);

        // ---- Hover tooltip -------------------------------------------------------------------------------
        if (s_hoverCell >= 0)
        {
            DateTime d = start.AddDays(s_hoverCell);
            int count = st.ByDay.TryGetValue(d, out int c) ? c : 0;
            string tip = $"{d:ddd, MMM d}: {count} test{(count == 1 ? "" : "s")}";
            _ = Native.SelectObject(dc, s_fontSmall);
            Native.SIZE ts = Measure(dc, tip);
            int padX = Program.S(9), padY = Program.S(5);
            int tw = ts.cx + padX * 2, th = ts.cy + padY * 2;
            int cx = m.Pad + (s_hoverCell % 7) * m.CellW + m.CellW / 2;
            int cellTop = m.GridY + (s_hoverCell / 7) * m.CellH;
            int tx = Math.Clamp(cx - tw / 2, m.Pad, w - m.Pad - tw);
            int ty = cellTop - th - Program.S(2);
            if (ty < m.WeekdayY) ty = cellTop + m.CellH + Program.S(2);

            using (var g = new Gdip.Surface(dc))
            {
                g.FillRoundRect(Program.Blend(0x00FFFFFF, BG, 0.16), tx, ty, tw, th, Program.S(6));
            }
            Native.SetTextColor(dc, TEXT);
            DrawText(dc, tip, tx + padX, ty + padY, Native.DT_LEFT);
        }
    }

    private static void DrawNavButton(Gdip.Surface g, Native.RECT r, bool up, bool enabled, bool hovered)
    {
        if (hovered && enabled)
            g.FillRoundRect(Program.Blend(0x00FFFFFF, BG, 0.10), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Program.S(6));

        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        int hw = Program.S(5), hh = Program.S(3);
        Native.POINT[] tri = up
            ? [new() { X = cx - hw, Y = cy + hh }, new() { X = cx + hw, Y = cy + hh }, new() { X = cx, Y = cy - hh }]
            : [new() { X = cx - hw, Y = cy - hh }, new() { X = cx + hw, Y = cy - hh }, new() { X = cx, Y = cy + hh }];
        g.FillPolygon(enabled ? TEXT : FAINT, tri);
    }

    private static (string, uint) GuardianLine(Settings s)
    {
        GuardianStatus g = s_guardian;
        if (!g.Available)
            return (s.ApeKey.Length == 0 ? "Add an ApeKey for streak alerts" : "Streak status unavailable", DIM);
        if (g.TypedToday) return ($"Today: streak safe, next day starts in {g.TimeLeftText}", GREEN);
        if (g.IsWarning) return ($"Today: no test yet, {g.TimeLeftText} to keep the streak", AMBER);
        return ($"Today: no test yet, {g.TimeLeftText} to keep the streak", DIM);
    }

    private static Native.SIZE Measure(nint dc, string text)
    {
        Native.SIZE sz = default;
        _ = Native.GetTextExtentPoint32W(dc, text, text.Length, ref sz);
        return sz;
    }

    /// <summary>Draws single-line text anchored at x (left or right edge) and returns its width.</summary>
    private static int DrawText(nint dc, string text, int x, int y, uint align)
    {
        Native.SIZE sz = Measure(dc, text);
        var rc = align == Native.DT_RIGHT
            ? new Native.RECT { Left = x - sz.cx, Top = y, Right = x, Bottom = y + sz.cy }
            : new Native.RECT { Left = x, Top = y, Right = x + sz.cx, Bottom = y + sz.cy };
        _ = Native.DrawTextW(dc, text, -1, ref rc, Native.DT_LEFT | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
        return sz.cx;
    }

    private static void DrawTextCentered(nint dc, string text, int x, int y, int w, int h)
    {
        var rc = new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        _ = Native.DrawTextW(dc, text, -1, ref rc, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
    }
}
