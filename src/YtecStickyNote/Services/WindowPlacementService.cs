using System.Windows;
using YtecStickyNote.Models;

namespace YtecStickyNote.Services;

public static class WindowPlacementService
{
    private const double MinimumVisibleWidth = 90;
    private const double MinimumVisibleHeight = 48;

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

    private static double ClampFinite(double value, double min, double max, double fallback)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
