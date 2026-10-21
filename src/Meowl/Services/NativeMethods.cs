using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Meowl.Services;

static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_APPWINDOW = 0x00040000;

    /// <summary>Mauszeiger in WPF-Einheiten (DIP).</summary>
    public static Point GetCursorPosDip(Visual relativeTo)
    {
        GetCursorPos(out var p);
        var source = PresentationSource.FromVisual(relativeTo);
        if (source?.CompositionTarget is { } target)
            return target.TransformFromDevice.Transform(new Point(p.X, p.Y));
        var dpi = VisualTreeHelper.GetDpi(relativeTo);
        return new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
    }

    /// <summary>Fenster nicht in Alt+Tab anzeigen.</summary>
    public static void HideFromAltTab(IntPtr hwnd)
    {
        var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)style);
    }

    public static MEMORYSTATUSEX? GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status : null;
    }
}
