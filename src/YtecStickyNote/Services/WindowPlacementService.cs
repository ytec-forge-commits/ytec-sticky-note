using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using YtecStickyNote.Models;

namespace YtecStickyNote.Services;

public static class WindowPlacementService
{
    private const double MinimumVisibleWidth = 90;
    private const double MinimumVisibleHeight = 48;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static Rect GetRestoredBounds(WindowStateData state, Rect virtualDesktop, Rect primaryWorkArea)
    {
        var width = ClampFinite(state.Width, 360, Math.Max(360, virtualDesktop.Width), 520);
        var height = ClampFinite(state.Height, 400, Math.Max(400, virtualDesktop.Height), 620);

        var defaultLeft = primaryWorkArea.Right - width - 36;
        var defaultTop = primaryWorkArea.Top + 72;
        var left = state.Left is double savedLeft && double.IsFinite(savedLeft) ? savedLeft : defaultLeft;
        var top = state.Top is double savedTop && double.IsFinite(savedTop) ? savedTop : defaultTop;

        var maxLeft = virtualDesktop.Right - MinimumVisibleWidth;
        var minLeft = virtualDesktop.Left - width + MinimumVisibleWidth;
        var maxTop = virtualDesktop.Bottom - MinimumVisibleHeight;
        var minTop = virtualDesktop.Top;

        left = Math.Clamp(left, minLeft, maxLeft);
        top = Math.Clamp(top, minTop, maxTop);
        return new Rect(left, top, width, height);
    }

    public static Rect GetRestoredBounds(WindowStateData state) => GetRestoredBounds(
        state,
        new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight),
        SystemParameters.WorkArea);

    public static bool EnsureVisible(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var nativeBounds))
        {
            return false;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var currentBounds = nativeBounds.ToRect();
        var workArea = monitorInfo.WorkArea.ToRect();
        var correctedBounds = ConstrainToWorkArea(currentBounds, workArea);
        if (correctedBounds == currentBounds)
        {
            return false;
        }

        return SetWindowPos(
            handle,
            IntPtr.Zero,
            (int)Math.Round(correctedBounds.Left),
            (int)Math.Round(correctedBounds.Top),
            (int)Math.Round(correctedBounds.Width),
            (int)Math.Round(correctedBounds.Height),
            SwpNoZOrder | SwpNoActivate);
    }

    public static Rect ConstrainToWorkArea(Rect windowBounds, Rect workArea)
    {
        if (!IsFinitePositive(workArea.Width) || !IsFinitePositive(workArea.Height))
        {
            return windowBounds;
        }

        var width = IsFinitePositive(windowBounds.Width)
            ? Math.Min(windowBounds.Width, workArea.Width)
            : Math.Min(520, workArea.Width);
        var height = IsFinitePositive(windowBounds.Height)
            ? Math.Min(windowBounds.Height, workArea.Height)
            : Math.Min(620, workArea.Height);

        var maxLeft = workArea.Right - width;
        var maxTop = workArea.Bottom - height;
        var left = double.IsFinite(windowBounds.Left)
            ? Math.Clamp(windowBounds.Left, workArea.Left, maxLeft)
            : workArea.Left;
        var top = double.IsFinite(windowBounds.Top)
            ? Math.Clamp(windowBounds.Top, workArea.Top, maxTop)
            : workArea.Top;

        return new Rect(left, top, width, height);
    }

    private static double ClampFinite(double value, double min, double max, double fallback)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        public MonitorInfo()
        {
            Size = Marshal.SizeOf<MonitorInfo>();
        }
    }
}
