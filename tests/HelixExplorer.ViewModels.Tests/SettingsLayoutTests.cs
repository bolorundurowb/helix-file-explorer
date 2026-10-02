using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HelixExplorer.Controls;
using HelixExplorer.Views;

namespace HelixExplorer.ViewModels.Tests;

public sealed class SettingsLayoutTests
{
    [Fact]
    public void SectionFrames_UseWidestContent_NotTheVisibleSection()
    {
        const string shortLabel = "Show";
        const string longLabel = "Choose folder sort order";

        HeadlessSession.RunOnUiThread(() =>
        {
            var shortWidth = MeasureAlone(shortLabel);
            var longWidth = MeasureAlone(longLabel);

            var host = new WidestChildPanel();
            host.Children.Add(Section(shortLabel));
            host.Children.Add(Section(longLabel));

            var window = Show(Wrap(host));
            try
            {
                host.ActiveIndex = 0;
                window.UpdateLayout();
                var narrowPage = FrameWidth(host);

                host.ActiveIndex = 1;
                window.UpdateLayout();
                var widePage = FrameWidth(host);

                var sameWidth = Math.Abs(narrowPage - widePage) < 1;
                sameWidth.Must().BeTrue();
                (Math.Abs(narrowPage - longWidth) < 1).Must().BeTrue();
                narrowPage.Must().BeGreaterThan(shortWidth + 80);
                narrowPage.Must().BeLessThan(720);
                host.Bounds.Height.Must().BeGreaterThan(1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SettingsPage_FramesMatchAcrossSections_HeightFollowsActiveSection()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var page = new SettingsPage();
            var window = new Window { Content = page, Width = 1200, Height = 900 };
            window.Show();
            try
            {
                window.UpdateLayout();
                var host = page.GetVisualDescendants().OfType<WidestChildPanel>().Single();
                var widths = new double[host.Children.Count];
                var heights = new double[host.Children.Count];

                for (var i = 0; i < host.Children.Count; i++)
                {
                    host.ActiveIndex = i;
                    window.UpdateLayout();
                    widths[i] = FrameWidth(host);
                    heights[i] = host.Bounds.Height;
                }

                host.Children.Count.Must().Be(5);
                foreach (var width in widths)
                {
                    (Math.Abs(width - widths[0]) < 1).Must().BeTrue();
                    width.Must().BeGreaterThan(400);
                    width.Must().BeLessThanOrEqualTo(720);
                }

                // General is several groups; Layout is two rows. Shared width must not
                // also pin every section to the tallest page.
                heights[0].Must().BeGreaterThan(heights[2] + 40);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static double MeasureAlone(string label)
    {
        var section = Section(label);
        var window = Show(Wrap(section));
        try
        {
            window.UpdateLayout();
            return section.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("settingsRow")).Bounds.Width;
        }
        finally
        {
            window.Close();
        }
    }

    private static StackPanel Wrap(Control child)
    {
        var outer = new StackPanel
        {
            Margin = new Avalonia.Thickness(24, 20),
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 8,
        };
        outer.Children.Add(child);
        return outer;
    }

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 1200, Height = 800 };
        window.Show();
        return window;
    }

    private static StackPanel Section(string label)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var toggle = new ToggleSwitch { IsChecked = true };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);

        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new Border { Classes = { "settingsRow" }, Child = grid });
        return section;
    }

    private static double FrameWidth(WidestChildPanel host)
    {
        var border = host.GetVisualDescendants()
            .OfType<Border>()
            .Where(candidate => candidate.Classes.Contains("settingsRow") && candidate.Bounds.Width > 1)
            .ToList();
        border.Count.Must().BeGreaterThan(0);
        return border.Max(candidate => candidate.Bounds.Width);
    }
}
