namespace HelixExplorer.Input;

/// <summary>
/// Layout slot of one tab chip along the strip, in the items panel's coordinates.
/// </summary>
public readonly record struct TabSlot(double Left, double Width)
{
    public double Right => Left + Width;

    public double Centre => Left + Width / 2;
}

/// <summary>
/// Pure maths behind dragging a tab along the strip. The strip lays chips out once and the drag only applies
/// render transforms, so every decision is taken against the slots captured when the drag began.
/// </summary>
public static class TabReorderGeometry
{
    /// <summary>
    /// Keeps the dragged chip inside the strip: its left edge cannot pass the first slot and its right edge
    /// cannot pass the last.
    /// </summary>
    public static double ClampOffset(ReadOnlySpan<TabSlot> slots, int source, double delta)
    {
        var dragged = slots[source];
        return Math.Clamp(delta, slots[0].Left - dragged.Left, Math.Max(0, slots[^1].Right - dragged.Right));
    }

    /// <summary>
    /// The index the dragged tab would land on: it passes a neighbour once its centre reaches that
    /// neighbour's centre, as browser tab strips do. Reaching (not strictly crossing) matters at the ends:
    /// the clamp stops the chip with its centre exactly on the last slot's centre.
    /// </summary>
    public static int TargetIndex(ReadOnlySpan<TabSlot> slots, int source, double offset)
    {
        var centre = slots[source].Centre + offset;
        var target = source;

        for (var i = source + 1; i < slots.Length && centre >= slots[i].Centre; i++)
            target = i;

        if (target != source)
            return target;

        for (var i = source - 1; i >= 0 && centre <= slots[i].Centre; i--)
            target = i;

        return target;
    }

    /// <summary>
    /// How far one slot moves: the dragged chip's width plus the gap after it (or before it, for the last chip).
    /// </summary>
    public static double Step(ReadOnlySpan<TabSlot> slots, int source)
    {
        var dragged = slots[source];
        var gap = 0d;

        if (source + 1 < slots.Length)
            gap = slots[source + 1].Left - dragged.Right;
        else if (source > 0)
            gap = dragged.Left - slots[source - 1].Right;

        return dragged.Width + Math.Max(0, gap);
    }

    /// <summary>
    /// The horizontal shift for a neighbour so the gap opens where the dragged tab would land.
    /// </summary>
    public static double NeighbourShift(int index, int source, int target, double step)
    {
        if (source < target && index > source && index <= target)
            return -step;

        if (target < source && index >= target && index < source)
            return step;

        return 0;
    }

    /// <summary>
    /// Offset from the dropped chip's final slot to where it was released, so it can glide into place instead
    /// of snapping.
    /// </summary>
    public static double SettleOffset(ReadOnlySpan<TabSlot> slots, int source, int target, double offset)
    {
        var dragged = slots[source];
        var slotLeft = target == source
            ? dragged.Left
            : target > source ? slots[target].Right - dragged.Width : slots[target].Left;

        return dragged.Left + offset - slotLeft;
    }
}
