using DesktopFamiliar.Models;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace DesktopFamiliar.Services.AutoClick;

/// <summary>Generates bounded auto-click targets and delays without WPF or timer dependencies.</summary>
public static class AutoClickRandomizationPolicy
{
    public static DrawingRectangle NormalizeTargetArea(DrawingRectangle area) => new(
        area.Left,
        area.Top,
        Math.Max(1, area.Width),
        Math.Max(1, area.Height));

    public static DrawingPoint GetRandomTarget(DrawingRectangle targetArea, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        targetArea = NormalizeTargetArea(targetArea);
        return new DrawingPoint(
            targetArea.Left + random.Next(targetArea.Width),
            targetArea.Top + random.Next(targetArea.Height));
    }

    public static int GetRandomInterval(
        int minimumMilliseconds,
        int maximumMilliseconds,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        minimumMilliseconds = Math.Clamp(
            minimumMilliseconds,
            AppSettings.MinimumAutoClickIntervalMilliseconds,
            AppSettings.MaximumAutoClickIntervalMilliseconds);
        maximumMilliseconds = Math.Clamp(
            maximumMilliseconds,
            minimumMilliseconds,
            AppSettings.MaximumAutoClickIntervalMilliseconds);
        return random.Next(minimumMilliseconds, maximumMilliseconds + 1);
    }
}
