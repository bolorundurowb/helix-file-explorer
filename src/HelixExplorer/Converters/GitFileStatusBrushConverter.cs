using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Git;

namespace HelixExplorer.Converters;

/// <summary>Maps <see cref="GitFileStatus"/> to theme brushes for row/name coloring.</summary>
public sealed class GitFileStatusBrushConverter : IValueConverter
{
    public static GitFileStatusBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not GitFileStatus status || status == GitFileStatus.None)
            return GetBrush("HelixEntryForegroundBrush") ?? Brushes.Gray;

        var key = status switch
        {
            GitFileStatus.AddedOrStaged => "HelixGitStagedBrush",
            GitFileStatus.Modified => "HelixGitModifiedBrush",
            GitFileStatus.Conflict => "HelixGitConflictBrush",
            GitFileStatus.Untracked => "HelixGitUntrackedBrush",
            _ => "HelixEntryForegroundBrush"
        };

        return GetBrush(key) ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object? GetBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var brush) == true)
            return brush;
        return null;
    }
}
