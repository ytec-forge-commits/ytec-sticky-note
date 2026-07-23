using System.Runtime.InteropServices;
using System.Windows;

namespace YtecStickyNote.Services;

public sealed record MonitorGeometry(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight,
    int ScaleMilli);

public static class MonitorLayoutService
{
    private const int MonitorDefaultScaleMilli = 1000;
    private const int EffectiveDpi = 0;
    private const uint DefaultDpi = 96;

    public static string GetCurrentLayoutId() => CreateLayoutId(GetCurrentMonitorGeometries());

    public static string CreateLayoutId(IEnumerable<MonitorGeometry> monitors)
    {
        var sorted = monitors
            .OrderBy(monitor => monitor.X)
            .ThenBy(monitor => monitor.Y)
            .ThenBy(monitor => monitor.Width)
            .ThenBy(monitor => monitor.Height)
            .ThenBy(monitor => monitor.WorkX)
            .ThenBy(monitor => monitor.WorkY)
            .ThenBy(monitor => monitor.WorkWidth)
            .ThenBy(monitor => monitor.WorkHeight)
            .ThenBy(monitor => monitor.ScaleMilli)
            .ThenBy(monitor => monitor.Name, StringComparer.Ordinal)
            .ToArray();

        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var monitor in sorted)
        {
            var value = FormattableString.Invariant(
                $"{monitor.Name.Length}:{monitor.Name}:{monitor.X}:{monitor.Y}:{monitor.Width}:{monitor.Height}:{monitor.WorkX}:{monitor.WorkY}:{monitor.WorkWidth}:{monitor.WorkHeight}:{monitor.ScaleMilli};");
            foreach (var byteValue in System.Text.Encoding.UTF8.GetBytes(value))
            {
                hash ^= byteValue;
                hash *= prime;
            }
        }

        return $"layout-{sorted.Length}-{hash:x16}";
    }

    public static IReadOnlyList<MonitorGeometry> GetCurrentMonitorGeometries()
    {
        var monitors = new List<MonitorGeometry>();
        MonitorEnumProcedure callback = (
            IntPtr monitorHandle,
            IntPtr _,
            ref NativeRect _,
            IntPtr _) =>
        {
            var info = new MonitorInfoEx();
            if (!GetMonitorInfo(monitorHandle, ref info))
            {
                return true;
            }

            var scaleMilli = GetScaleMilli(monitorHandle);
            monitors.Add(new MonitorGeometry(
                info.DeviceName ?? string.Empty,
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Width,
                info.Monitor.Height,
                info.WorkArea.Left,
                info.WorkArea.Top,
                info.WorkArea.Width,
                info.WorkArea.Height,
                scaleMilli));
            return true;
        };

        if (EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) && monitors.Count > 0)
        {
            return monitors;
        }

        var workArea = SystemParameters.WorkArea;
        return
        [
            new MonitorGeometry(
                "virtual-desktop",
                (int)Math.Round(SystemParameters.VirtualScreenLeft),
                (int)Math.Round(SystemParameters.VirtualScreenTop),
                (int)Math.Round(SystemParameters.VirtualScreenWidth),
                (int)Math.Round(SystemParameters.VirtualScreenHeight),
                (int)Math.Round(workArea.Left),
                (int)Math.Round(workArea.Top),
                (int)Math.Round(workArea.Width),
                (int)Math.Round(workArea.Height),
                MonitorDefaultScaleMilli)
        ];
    }

    private static int GetScaleMilli(IntPtr monitorHandle)
    {
        try
        {
            if (GetDpiForMonitor(monitorHandle, EffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return (int)Math.Round(dpiX / (double)DefaultDpi * MonitorDefaultScaleMilli);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return MonitorDefaultScaleMilli;
    }

    private delegate bool MonitorEnumProcedure(
        IntPtr monitorHandle,
        IntPtr deviceContext,
        ref NativeRect monitorRectangle,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRectangle,
        MonitorEnumProcedure callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfoEx monitorInfo);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitorHandle,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;

        public MonitorInfoEx()
        {
            Size = Marshal.SizeOf<MonitorInfoEx>();
            DeviceName = string.Empty;
        }
    }
}
