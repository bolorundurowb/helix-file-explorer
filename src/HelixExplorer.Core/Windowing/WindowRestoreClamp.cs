namespace HelixExplorer.Core.Windowing;

/// <summary>
/// Working area of one screen. Coordinates are physical pixels, the same space as a window position.
/// <see cref="Scaling"/> converts DIPs to those pixels (1 = 96 DPI).
/// </summary>
public readonly record struct ScreenWorkArea(
    double X,
    double Y,
    double Width,
    double Height,
    double Scaling,
    bool IsPrimary);

/// <summary>
/// Restored top-left in physical pixels, plus the size to apply in DIPs.
/// A null origin means there was nothing to place against.
/// </summary>
public readonly record struct RestoredWindowPlacement(int? X, int? Y, double WidthDip, double HeightDip);

/// <summary>
/// Keeps a restored window reachable. Position is physical pixels; width and height stay DIPs.
/// </summary>
public static class WindowRestoreClamp
{
    public const double MinimumOverlapDip = 48;

    public static RestoredWindowPlacement Resolve(
        int? x,
        int? y,
        double widthDip,
        double heightDip,
        IReadOnlyList<ScreenWorkArea> workAreas)
    {
        var hasPosition = x.HasValue && y.HasValue;
        if (!TryGetPrimary(workAreas, out var primary))
        {
            return hasPosition
                ? new RestoredWindowPlacement(x, y, widthDip, heightDip)
                : new RestoredWindowPlacement(null, null, widthDip, heightDip);
        }

        if (!hasPosition)
            return Centre(widthDip, heightDip, primary);

        var savedX = x.GetValueOrDefault();
        var savedY = y.GetValueOrDefault();
        if (OverlapsEnough(savedX, savedY, widthDip, heightDip, workAreas))
            return new RestoredWindowPlacement(savedX, savedY, widthDip, heightDip);

        var target = Nearest(savedX, savedY, widthDip, heightDip, workAreas);
        return ShiftOnto(savedX, savedY, widthDip, heightDip, target);
    }

    private static RestoredWindowPlacement Centre(double widthDip, double heightDip, ScreenWorkArea area)
    {
        var size = ToPixels(widthDip, heightDip, ScaleOf(area));
        var x = area.X + ((area.Width - size.Width) / 2);
        var y = area.Y + ((area.Height - size.Height) / 2);
        return new RestoredWindowPlacement(RoundToInt(x), RoundToInt(y), widthDip, heightDip);
    }

    private static bool OverlapsEnough(
        int x,
        int y,
        double widthDip,
        double heightDip,
        IReadOnlyList<ScreenWorkArea> workAreas)
    {
        for (var i = 0; i < workAreas.Count; i++)
        {
            var area = workAreas[i];
            if (!IsUsable(area))
                continue;

            var scale = ScaleOf(area);
            var size = ToPixels(widthDip, heightDip, scale);
            var overlap = Intersection(x, y, size.Width, size.Height, area);
            var minimum = MinimumOverlapDip * scale;
            if (overlap.Width >= minimum && overlap.Height >= minimum)
                return true;
        }

        return false;
    }

    private static ScreenWorkArea Nearest(int x, int y, double widthDip, double heightDip, IReadOnlyList<ScreenWorkArea> workAreas)
    {
        var best = workAreas[0];
        var bestOverlap = -1d;
        var bestGap = double.PositiveInfinity;
        var found = false;

        for (var i = 0; i < workAreas.Count; i++)
        {
            var area = workAreas[i];
            if (!IsUsable(area))
                continue;

            var size = ToPixels(widthDip, heightDip, ScaleOf(area));
            var overlap = Intersection(x, y, size.Width, size.Height, area);
            var overlapArea = overlap.Width * overlap.Height;
            var gap = Gap(x, y, size.Width, size.Height, area);
            if (!found || overlapArea > bestOverlap || (overlapArea == bestOverlap && gap < bestGap))
            {
                best = area;
                bestOverlap = overlapArea;
                bestGap = gap;
                found = true;
            }
        }

        return best;
    }

    private static RestoredWindowPlacement ShiftOnto(int x, int y, double widthDip, double heightDip, ScreenWorkArea area)
    {
        var scale = ScaleOf(area);
        var size = ToPixels(widthDip, heightDip, scale);
        // Cap the grab margin so a tiny window or a tiny work area is not asked to show 48 DIP it does not have.
        var minW = Math.Min(MinimumOverlapDip * scale, Math.Min(area.Width, size.Width));
        var minH = Math.Min(MinimumOverlapDip * scale, Math.Min(area.Height, size.Height));

        var left = (double)x;
        var top = (double)y;
        if (left + size.Width < area.X + minW)
            left = area.X + minW - size.Width;
        if (left > area.X + area.Width - minW)
            left = area.X + area.Width - minW;

        if (top + size.Height < area.Y + minH)
            top = area.Y + minH - size.Height;
        if (top > area.Y + area.Height - minH)
            top = area.Y + area.Height - minH;

        return new RestoredWindowPlacement(RoundToInt(left), RoundToInt(top), widthDip, heightDip);
    }

    private static bool TryGetPrimary(IReadOnlyList<ScreenWorkArea> workAreas, out ScreenWorkArea area)
    {
        var found = false;
        area = default;
        for (var i = 0; i < workAreas.Count; i++)
        {
            var candidate = workAreas[i];
            if (!IsUsable(candidate))
                continue;

            if (candidate.IsPrimary)
            {
                area = candidate;
                return true;
            }

            if (!found)
            {
                area = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool IsUsable(ScreenWorkArea area)
        => double.IsFinite(area.X)
           && double.IsFinite(area.Y)
           && double.IsFinite(area.Width)
           && double.IsFinite(area.Height)
           && area.Width > 0
           && area.Height > 0;

    private static double ScaleOf(ScreenWorkArea area)
        => area.Scaling > 0 && double.IsFinite(area.Scaling) ? area.Scaling : 1;

    private static (double Width, double Height) ToPixels(double widthDip, double heightDip, double scale)
    {
        var width = double.IsFinite(widthDip) ? widthDip * scale : 0;
        var height = double.IsFinite(heightDip) ? heightDip * scale : 0;
        if (width < 0)
            width = 0;
        if (height < 0)
            height = 0;
        return (width, height);
    }

    private static (double Width, double Height) Intersection(
        double x,
        double y,
        double width,
        double height,
        ScreenWorkArea area)
    {
        var left = Math.Max(x, area.X);
        var top = Math.Max(y, area.Y);
        var right = Math.Min(x + width, area.X + area.Width);
        var bottom = Math.Min(y + height, area.Y + area.Height);
        return (Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static double Gap(double x, double y, double width, double height, ScreenWorkArea area)
    {
        var dx = 0d;
        if (x + width < area.X)
            dx = area.X - (x + width);
        else if (x > area.X + area.Width)
            dx = x - (area.X + area.Width);

        var dy = 0d;
        if (y + height < area.Y)
            dy = area.Y - (y + height);
        else if (y > area.Y + area.Height)
            dy = y - (area.Y + area.Height);

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static int RoundToInt(double value)
    {
        if (!double.IsFinite(value))
            return 0;
        if (value >= int.MaxValue)
            return int.MaxValue;
        if (value <= int.MinValue)
            return int.MinValue;
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
