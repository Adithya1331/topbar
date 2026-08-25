using System.Runtime.InteropServices;

namespace TopBar;

internal static class Program
{
    private const int BAR_HEIGHT = 44;
    private const int BOX_SIZE = 14;
    private const int BOX_GAP = 6;
    private const uint WM_APPBAR_CALLBACK = 0x8000 | 0x0001;

    private static uint s_taskbarCreated;

    private static readonly Native.WndProc s_wndProc = WndProc;

    private static int Main()
    {
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

        nint hwnd = Native.CreateWindowExW(
            Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            "TopBar",
            "TopBar",
            Native.WS_POPUP | Native.WS_VISIBLE,
            0, 0, 400, BAR_HEIGHT,
            default, default, hInstance, default);

        if (hwnd == default)
        {
            return Marshal.GetLastWin32Error();
        }

        RegisterAppBar(hwnd);
        MoveAppBar(hwnd);

        while (Native.GetMessageW(out Native.MSG msg, default, 0, 0) > 0)
        {
            _ = Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
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
        abd.rc.Bottom = abd.rc.Top + BAR_HEIGHT;

        _ = Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        abd.rc.Bottom = abd.rc.Top + BAR_HEIGHT;
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

    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_PAINT:
                Paint(hwnd);
                return default;

            case Native.WM_RBUTTONUP:
                Native.DestroyWindow(hwnd);
                return default;

            case Native.WM_DISPLAYCHANGE:
                MoveAppBar(hwnd);
                return default;

            case Native.WM_DESTROY:
                UnregisterAppBar(hwnd);
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

    private static void Paint(nint hwnd)
    {
        nint hdc = Native.BeginPaint(hwnd, out Native.PAINTSTRUCT ps);
        if (hdc == default)
        {
            return;
        }
        try
        {
            _ = Native.GetClientRect(hwnd, out Native.RECT rc);

            nint bgBrush = Native.CreateSolidBrush(0x001C1818);
            _ = Native.FillRect(hdc, ref rc, bgBrush);
            _ = Native.DeleteObject(bgBrush);

            int y = ((rc.Bottom - rc.Top) - BOX_SIZE) / 2;
            int x = 12;
            for (int i = 0; i < 7; i++)
            {
                var box = new Native.RECT { Left = x, Top = y, Right = x + BOX_SIZE, Bottom = y + BOX_SIZE };
                uint color = i == 6 ? 0x0014B7E2u : 0x003E3E3Eu;
                nint boxBrush = Native.CreateSolidBrush(color);
                _ = Native.FillRect(hdc, ref box, boxBrush);
                _ = Native.DeleteObject(boxBrush);
                x += BOX_SIZE + BOX_GAP;
            }

            Native.SetBkMode(hdc, Native.TRANSPARENT);
            Native.SetTextColor(hdc, 0x00888888);
            _ = Native.SelectObject(hdc, Native.GetStockObject(Native.DEFAULT_GUI_FONT));

            var textRc = new Native.RECT { Left = x + 10, Top = rc.Top, Right = rc.Right, Bottom = rc.Bottom };
            _ = Native.DrawTextW(
                hdc,
                "monkeybar \u00b7 right-click to quit",
                -1,
                ref textRc,
                Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE);
        }
        finally
        {
            _ = Native.EndPaint(hwnd, ref ps);
        }
    }
}

internal static class Native
{
    public delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    public const uint WS_POPUP = 0x80000000u;
    public const uint WS_VISIBLE = 0x10000000u;
    public const uint WS_EX_TOPMOST = 0x00000008u;
    public const uint WS_EX_TOOLWINDOW = 0x00000080u;
    public const uint WS_EX_NOACTIVATE = 0x08000000u;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_RBUTTONUP = 0x0205;

    public const int TRANSPARENT = 1;
    public const uint DT_LEFT = 0x00000000;
    public const uint DT_CENTER = 0x00000001;
    public const uint DT_VCENTER = 0x00000004;
    public const uint DT_SINGLELINE = 0x00000020;

    public const int DEFAULT_GUI_FONT = 17;
    public const nint IDC_ARROW = 32512;

    public const uint ABM_NEW = 0x00000000;
    public const uint ABM_REMOVE = 0x00000001;
    public const uint ABM_QUERYPOS = 0x00000002;
    public const uint ABM_SETPOS = 0x00000003;
    public const uint ABE_TOP = 0x00000001;

    public const uint SPI_GETWORKAREA = 0x004F;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int SM_CXSCREEN = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public ulong pad0;
        public ulong pad1;
        public ulong pad2;
        public ulong pad3;
    }

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string lpString);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? lpModuleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int w, int h, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int nExitCode);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG lpMsg);
    [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("shell32.dll")] public static extern nint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")] public static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint hwnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern int FillRect(nint hdc, ref RECT lprc, nint hbr);
    [DllImport("gdi32.dll")] public static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint hObject);
    [DllImport("gdi32.dll")] public static extern nint GetStockObject(int i);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint hObject);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DrawTextW(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);
    [DllImport("user32.dll")] public static extern nint LoadCursorW(nint hInstance, nint lpCursorName);
}
