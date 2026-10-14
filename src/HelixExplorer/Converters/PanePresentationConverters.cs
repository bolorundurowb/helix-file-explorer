using System.Globalization;
using Avalonia.Data.Converters;
using HelixExplorer.Core.Git;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.Converters;

public sealed class ModifiedDateConverter : IValueConverter
{
    public static ModifiedDateConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime modified)
            return string.Empty;

        return PanePresentation.FormatModified(modified, DateTime.UtcNow, TimeZoneInfo.Local, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ModifiedDateTooltipConverter : IValueConverter
{
    public static ModifiedDateTooltipConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime modified)
            return string.Empty;

        return PanePresentation.FormatModifiedTooltip(modified, TimeZoneInfo.Local, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class GitStatusLetterConverter : IValueConverter
{
    public static GitStatusLetterConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GitFileStatus status ? PanePresentation.GitLetter(status) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class GitStatusBadgeVisibleConverter : IValueConverter
{
    public static GitStatusBadgeVisibleConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GitFileStatus status && PanePresentation.GitLetter(status).Length > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class GitStatusTooltipConverter : IValueConverter
{
    public static GitStatusTooltipConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GitFileStatus status ? PanePresentation.GitTooltip(status) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class EntryAutomationNameConverter : IMultiValueConverter
{
    public static EntryAutomationNameConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = values.Count > 0 ? values[0] as string : null;
        var type = values.Count > 1 ? values[1] as string : null;
        return PanePresentation.FormatAutomationName(name, type);
    }
}
