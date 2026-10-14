using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Git;
using HelixExplorer.Services;

namespace HelixExplorer.Converters;

/// <summary>
/// Resolves the foreground brush for an entry from its git status.
/// </summary>
/// <remarks>
/// Use the multi-value form with <see cref="AvaloniaThemeService.ThemeRevisionResourceKey"/> as a
/// second binding. The converter only runs when an input changes. <c>ActualThemeVariant</c> stays
/// <c>Default</c> when a System-mode app is re-applied after Windows switches light/dark, so the
/// revision is what makes the lookup run again. Names with no git status resolve
/// <c>HelixEntryForegroundBrush</c> on that pass rather than keeping the brush from the first one.
/// </remarks>
public sealed class GitFileStatusBrushConverter : IValueConverter, IMultiValueConverter
{
    public static GitFileStatusBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Resolve(value);

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[1..] (theme revision) only force this method to run again.
        var status = values.Count > 0 ? values[0] : null;
        return Resolve(status);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object Resolve(object? value)
    {
        var key = value is GitFileStatus status
            ? status switch
            {
                GitFileStatus.AddedOrStaged => "HelixGitStagedBrush",
                GitFileStatus.Modified => "HelixGitModifiedBrush",
                GitFileStatus.Conflict => "HelixGitConflictBrush",
                GitFileStatus.Untracked => "HelixGitUntrackedBrush",
                _ => "HelixEntryForegroundBrush"
            }
            : "HelixEntryForegroundBrush";

        return GetBrush(key) ?? Brushes.Gray;
    }

    private static object? GetBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, AvaloniaThemeService.EffectiveVariant, out var brush) == true)
            return brush;
        return null;
    }
}
