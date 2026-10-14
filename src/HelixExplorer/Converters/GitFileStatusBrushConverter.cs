using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using HelixExplorer.Core.Git;

namespace HelixExplorer.Converters;

/// <summary>
/// Resolves the foreground brush for an entry from its git status.
/// </summary>
/// <remarks>
/// Use the multi-value form with the target's <c>ActualThemeVariant</c> as the second binding. A
/// converter only runs when one of its inputs changes, so a single <c>GitStatus</c> binding resolves
/// the brush once and keeps the old theme's colour after a light/dark switch until the row is
/// recycled (e.g. on navigation). Feeding the theme variant in makes the switch itself re-evaluate.
/// </remarks>
public sealed class GitFileStatusBrushConverter : IValueConverter, IMultiValueConverter
{
    public static GitFileStatusBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Resolve(value, Application.Current?.ActualThemeVariant);

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = values.Count > 0 ? values[0] : null;
        var variant = values.Count > 1 ? values[1] as ThemeVariant : null;
        return Resolve(status, variant ?? Application.Current?.ActualThemeVariant);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object Resolve(object? value, ThemeVariant? variant)
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

        return GetBrush(key, variant) ?? Brushes.Gray;
    }

    private static object? GetBrush(string key, ThemeVariant? variant)
    {
        if (Application.Current?.TryGetResource(key, variant, out var brush) == true)
            return brush;
        return null;
    }
}
