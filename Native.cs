using System.Runtime.InteropServices;

namespace TopBar;

internal static class Native
{
    public delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    public const uint WS_POPUP = 0x80000000u;
    public const uint WS_VISIBLE = 0x10000000u;
    public const uint WS_CHILD = 0x40000000u;
    public const uint WS_CAPTION = 0x00C00000u;
    public const uint WS_SYSMENU = 0x00080000u;
    public const uint WS_MINIMIZEBOX = 0x00020000u;
    public const uint WS_EX_TOPMOST = 0x00000008u;
    public const uint WS_EX_TOOLWINDOW = 0x00000080u;
    public const uint WS_EX_NOACTIVATE = 0x08000000u;
    public const uint WS_EX_CLIENTEDGE = 0x00000200u;

    public const uint BS_PUSHBUTTON = 0x00000000u;
    public const uint BS_DEFPUSHBUTTON = 0x00000001u;
    public const uint BS_AUTOCHECKBOX = 0x00000003u;
    public const uint ES_AUTOHSCROLL = 0x00000080u;
    public const uint ES_PASSWORD = 0x00000020u;
    public const uint CBS_DROPDOWNLIST = 0x00000003u;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_HOTKEY = 0x0312;

    public const uint CB_ADDSTRING = 0x0143;
    public const uint CB_GETCURSEL = 0x0147;
    public const uint CB_SETCURSEL = 0x014E;
    public const uint BM_GETCHECK = 0x00F0;
    public const uint BM_SETCHECK = 0x00F1;
    public const int BST_CHECKED = 0x0001;

    public const int TRANSPARENT = 1;
    public const int PS_SOLID = 0;
    public const int DEFAULT_GUI_FONT = 17;
    public const uint DT_LEFT = 0x00000000;
    public const uint DT_CENTER = 0x00000001;
    public const uint DT_RIGHT = 0x00000002;
    public const uint DT_VCENTER = 0x00000004;
    public const uint DT_SINGLELINE = 0x00000020;

    public const nint IDC_ARROW = 32512;
    public const nint COLOR_WINDOW = 5;

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

    public const uint MF_STRING = 0x00000000;
    public const uint MF_GRAYED = 0x00000001;
    public const uint MF_DISABLED = 0x00000002;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
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
    [DllImport("gdi32.dll")] public static extern nint CreatePen(int fnStyle, int cWidth, uint color);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint hObject);
    [DllImport("gdi32.dll")] public static extern nint GetStockObject(int i);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint hObject);
    [DllImport("gdi32.dll")] public static extern bool RoundRect(nint hdc, int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] public static extern int FrameRect(nint hdc, ref RECT lprc, nint hbr);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DrawTextW(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);
    [DllImport("user32.dll")] public static extern nint LoadCursorW(nint hInstance, nint lpCursorName);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(nint hMenu, uint uFlags, nint uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint hwnd, ref POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool EnableWindow(nint hWnd, bool bEnable);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);
    [DllImport("user32.dll")] public static extern nint SetTimer(nint hWnd, nint nIDEvent, uint uElapse, nint lpTimerFunc);
    [DllImport("user32.dll")] public static extern bool KillTimer(nint hWnd, nint uIDEvent);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool AdjustWindowRect(ref RECT lpRect, uint dwStyle, bool bMenu);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern nint ShellExecuteW(nint hwnd, string lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);
}
