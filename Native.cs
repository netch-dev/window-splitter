using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowSplitter;

static class Native
{
    public const int SW_RESTORE = 9;
    public const int SW_SHOWNORMAL = 1;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cx, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

    public static string Title(IntPtr h)
    {
        int len = GetWindowTextLength(h);
        if (len == 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static Process? ProcessOf(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        try { return Process.GetProcessById((int)pid); } catch { return null; }
    }

    /// <summary>Visible rectangle of the window (excludes the invisible resize borders).</summary>
    public static RECT VisibleRect(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0) return r;
        GetWindowRect(h, out r);
        return r;
    }

    /// <summary>Move/resize so the *visible* frame lands exactly on the target rectangle.</summary>
    public static void PlaceVisible(IntPtr h, int x, int y, int w, int hgt)
    {
        if (IsIconic(h) || IsZoomed(h)) ShowWindow(h, SW_RESTORE);
        GetWindowRect(h, out RECT outer);
        RECT vis = VisibleRect(h);
        int dl = vis.Left - outer.Left, dt = vis.Top - outer.Top;
        int dr = outer.Right - vis.Right, db = outer.Bottom - vis.Bottom;
        SetWindowPos(h, IntPtr.Zero, x - dl, y - dt, w + dl + dr, hgt + dt + db, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public static bool IsAppWindow(IntPtr h)
    {
        if (!IsWindowVisible(h)) return false;
        if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return false;
        if (((long)GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;
        var t = Title(h);
        return t.Length > 0 && t != "Program Manager";
    }

    public static List<IntPtr> AppWindows()
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) => { if (IsAppWindow(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static void Activate(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        // Raise in z-order (works without foreground rights), then try to take focus.
        SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint me = GetCurrentThreadId();
        AttachThreadInput(me, fgThread, true);
        SetForegroundWindow(h);
        AttachThreadInput(me, fgThread, false);
    }
}
