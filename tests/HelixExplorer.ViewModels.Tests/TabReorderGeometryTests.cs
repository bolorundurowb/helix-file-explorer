using HelixExplorer.Input;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Slots used throughout: four 100px chips with a 4px gap, at 0, 104, 208 and 312.
/// </summary>
public class TabReorderGeometryTests
{
    private const double Width = 100;
    private const double Gap = 4;
    private const double Step = Width + Gap;

    private static readonly TabSlot[] Slots =
    [
        new(0, Width),
        new(Step, Width),
        new(Step * 2, Width),
        new(Step * 3, Width),
    ];

    [Fact]
    public void Slot_ExposesRightAndCentre()
    {
        var slot = new TabSlot(10, 100);

        slot.Right.Must().Be(110);
        slot.Centre.Must().Be(60);
    }

    [Theory]
    [InlineData(0, -50, 0)]
    [InlineData(0, 500, Step * 3)]
    [InlineData(3, 50, 0)]
    [InlineData(3, -500, -Step * 3)]
    [InlineData(1, 30, 30)]
    [InlineData(1, -30, -30)]
    public void ClampOffset_KeepsTheChipInsideTheStrip(int source, double delta, double expected)
    {
        TabReorderGeometry.ClampOffset(Slots, source, delta).Must().Be(expected);
    }

    [Fact]
    public void ClampOffset_SingleTab_CannotMove()
    {
        TabReorderGeometry.ClampOffset([new TabSlot(0, Width)], 0, 80).Must().Be(0);
        TabReorderGeometry.ClampOffset([new TabSlot(0, Width)], 0, -80).Must().Be(0);
    }

    [Theory]
    // Swaps once the dragged centre (50 + offset) reaches the neighbour's centre (154).
    [InlineData(0, 103, 0)]
    [InlineData(0, 104, 1)]
    [InlineData(0, 209, 2)]
    [InlineData(0, 312, 3)]
    [InlineData(3, -103, 3)]
    [InlineData(3, -104, 2)]
    [InlineData(3, -312, 0)]
    [InlineData(2, 0, 2)]
    [InlineData(1, 105, 2)]
    [InlineData(1, -105, 0)]
    public void TargetIndex_MovesPastANeighbourOnceItsCentreIsReached(int source, double offset, int expected)
    {
        TabReorderGeometry.TargetIndex(Slots, source, offset).Must().Be(expected);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    public void DraggingToTheClampLimit_ReachesTheEndSlot(int source, int end)
    {
        // The clamp stops the chip with its centre exactly on the end slot's centre, so that must count as a move,
        // otherwise a tab could never be dropped first or last.
        var offset = TabReorderGeometry.ClampOffset(Slots, source, source < end ? 10_000 : -10_000);

        TabReorderGeometry.TargetIndex(Slots, source, offset).Must().Be(end);
    }

    [Fact]
    public void Step_IsWidthPlusTheFollowingGap()
    {
        TabReorderGeometry.Step(Slots, 0).Must().Be(Step);
    }

    [Fact]
    public void Step_ForLastTab_UsesThePrecedingGap()
    {
        TabReorderGeometry.Step(Slots, 3).Must().Be(Step);
    }

    [Fact]
    public void Step_UsesTheDraggedChipsOwnWidth()
    {
        TabSlot[] mixed = [new(0, 150), new(154, 100)];

        TabReorderGeometry.Step(mixed, 0).Must().Be(154);
    }

    [Fact]
    public void Step_SingleTab_IsJustItsWidth()
    {
        TabReorderGeometry.Step([new TabSlot(0, Width)], 0).Must().Be(Width);
    }

    [Fact]
    public void Step_OverlappingSlots_NeverShrinksBelowWidth()
    {
        TabSlot[] overlapping = [new(0, 100), new(90, 100)];

        TabReorderGeometry.Step(overlapping, 0).Must().Be(100);
    }

    [Fact]
    public void NeighbourShift_DraggingRight_SlidesTheTabsItPassedLeft()
    {
        double[] shifts = [.. Enumerable.Range(0, 4).Select(i => TabReorderGeometry.NeighbourShift(i, 0, 2, Step))];

        shifts.SequenceEqual(new double[] { 0, -Step, -Step, 0 }).Must().BeTrue();
    }

    [Fact]
    public void NeighbourShift_DraggingLeft_SlidesTheTabsItPassedRight()
    {
        double[] shifts = [.. Enumerable.Range(0, 4).Select(i => TabReorderGeometry.NeighbourShift(i, 3, 1, Step))];

        shifts.SequenceEqual(new double[] { 0, Step, Step, 0 }).Must().BeTrue();
    }

    [Fact]
    public void NeighbourShift_NoMovement_LeavesEveryTabPut()
    {
        for (var i = 0; i < Slots.Length; i++)
            TabReorderGeometry.NeighbourShift(i, 1, 1, Step).Must().Be(0);
    }

    [Theory]
    [InlineData(0, 220, 2, 220 - Step * 2)]
    [InlineData(3, -220, 1, -220 + Step * 2)]
    [InlineData(1, 30, 1, 30)]
    [InlineData(0, Step * 3, 3, 0)]
    public void SettleOffset_IsTheDistanceFromTheFinalSlotToTheReleasePoint(
        int source, double offset, int target, double expected)
    {
        TabReorderGeometry.SettleOffset(Slots, source, target, offset).Must().Be(expected);
    }

    [Fact]
    public void SettleOffset_DraggingAWideTabRight_AlignsItsRightEdgeWithTheTargetSlot()
    {
        TabSlot[] mixed = [new(0, 150), new(154, 100), new(258, 100)];

        // Landing at index 2 puts the wide chip's right edge on slot 2's right edge (358), so its left is 208.
        TabReorderGeometry.SettleOffset(mixed, 0, 2, 200).Must().Be(200 - 208);
    }
}
