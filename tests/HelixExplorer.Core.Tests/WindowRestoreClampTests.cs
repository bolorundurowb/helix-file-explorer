using HelixExplorer.Core.Windowing;

namespace HelixExplorer.Core.Tests;

public class WindowRestoreClampTests
{
    [Fact]
    public void Resolve_KeepsAPositionThatAlreadyOverlapsAndLeavesSizeInDips()
    {
        var placed = WindowRestoreClamp.Resolve(
            200,
            100,
            800,
            500,
            [WorkArea(0, 0, 3840, 2160, scaling: 2, isPrimary: true)]);

        placed.Must().Be(new RestoredWindowPlacement(200, 100, 800, 500));
    }

    [Fact]
    public void Resolve_ShiftsUntilFortyEightDipOverlapsTheWorkingArea()
    {
        var placed = WindowRestoreClamp.Resolve(
            9000,
            4000,
            800,
            600,
            [WorkArea(100, 40, 1800, 1000, isPrimary: true)]);

        placed.Must().Be(new RestoredWindowPlacement(1852, 992, 800, 600));
    }

    [Fact]
    public void Resolve_MeasuresTheOverlapInDipsAtTheScreenScale()
    {
        var placed = WindowRestoreClamp.Resolve(
            5000,
            80,
            800,
            500,
            [WorkArea(0, 0, 3840, 2160, scaling: 2, isPrimary: true)]);

        placed.Must().Be(new RestoredWindowPlacement(3744, 80, 800, 500));
    }

    [Fact]
    public void Resolve_PullsAFortySevenDipSliverUpToTheMinimum()
    {
        var shortfall = WindowRestoreClamp.Resolve(
            1873,
            100,
            800,
            600,
            [WorkArea(0, 0, 1920, 1080, isPrimary: true)]);
        var exact = WindowRestoreClamp.Resolve(
            1872,
            100,
            800,
            600,
            [WorkArea(0, 0, 1920, 1080, isPrimary: true)]);

        shortfall.Must().Be(new RestoredWindowPlacement(1872, 100, 800, 600));
        exact.Must().Be(new RestoredWindowPlacement(1872, 100, 800, 600));
    }

    [Fact]
    public void Resolve_KeepsAWindowOnItsMonitorAndClampsAMissingOneOntoTheNearest()
    {
        ScreenWorkArea[] both =
        [
            WorkArea(0, 0, 1920, 1080, isPrimary: true),
            WorkArea(1920, 0, 1920, 1080)
        ];
        var onSecondary = WindowRestoreClamp.Resolve(2100, 120, 800, 600, both);
        var unplugged = WindowRestoreClamp.Resolve(2100, 120, 800, 600, [both[0]]);

        onSecondary.Must().Be(new RestoredWindowPlacement(2100, 120, 800, 600));
        unplugged.Must().Be(new RestoredWindowPlacement(1872, 120, 800, 600));
    }

    [Fact]
    public void Resolve_CentresOnThePrimaryWhenPositionIsMissing()
    {
        ScreenWorkArea[] screens =
        [
            WorkArea(1920, 0, 1920, 1080),
            WorkArea(80, 40, 4000, 2000, scaling: 2, isPrimary: true)
        ];

        var placed = WindowRestoreClamp.Resolve(null, null, 800, 500, screens);
        var onlyX = WindowRestoreClamp.Resolve(12, null, 800, 500, screens);

        placed.Must().Be(new RestoredWindowPlacement(1280, 540, 800, 500));
        onlyX.Must().Be(placed);
    }

    [Fact]
    public void Resolve_BringsAWindowSmallerThanTheMarginFullyInside()
    {
        var placed = WindowRestoreClamp.Resolve(
            -100,
            10,
            20,
            30,
            [WorkArea(0, 0, 1920, 1080, isPrimary: true)]);

        placed.Must().Be(new RestoredWindowPlacement(0, 10, 20, 30));
    }

    [Fact]
    public void Resolve_ClampsOntoAMonitorLeftOfTheOrigin()
    {
        var placed = WindowRestoreClamp.Resolve(
            -4000,
            40,
            800,
            600,
            [
                WorkArea(0, 0, 1920, 1080, isPrimary: true),
                WorkArea(-1920, 0, 1920, 1080)
            ]);

        placed.Must().Be(new RestoredWindowPlacement(-2672, 40, 800, 600));
    }

    [Fact]
    public void Resolve_WithoutScreensKeepsASavedPositionAndDoesNotInventOne()
    {
        var saved = WindowRestoreClamp.Resolve(40, 50, 800, 500, []);
        var missing = WindowRestoreClamp.Resolve(null, null, 800, 500, []);

        saved.Must().Be(new RestoredWindowPlacement(40, 50, 800, 500));
        missing.X.Must().BeNull();
        missing.Y.Must().BeNull();
        missing.WidthDip.Must().Be(800);
        missing.HeightDip.Must().Be(500);
    }

    private static ScreenWorkArea WorkArea(
        double x,
        double y,
        double width,
        double height,
        double scaling = 1,
        bool isPrimary = false)
        => new(x, y, width, height, scaling, isPrimary);
}
