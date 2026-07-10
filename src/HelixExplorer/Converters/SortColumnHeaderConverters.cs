using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Converters;

public sealed class SortColumnIsActiveConverter : IMultiValueConverter
{
    public static SortColumnIsActiveConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 1 || values[0] is not SortColumn active)
            return false;

        var column = ResolveColumn(values, parameter);
        return column.HasValue && active == column.Value;
    }

    internal static SortColumn? ResolveColumn(IList<object?> values, object? parameter)
    {
        if (values.Count > 2 && values[2] is SortColumn boundColumn)
            return boundColumn;

        if (parameter is SortColumn paramColumn)
            return paramColumn;

        return null;
    }
}

public sealed class SortColumnHeaderWeightConverter : IMultiValueConverter
{
    public static SortColumnHeaderWeightConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isActive = SortColumnIsActiveConverter.Instance.Convert(values, targetType, parameter, culture);
        return isActive is true ? FontWeight.SemiBold : FontWeight.Normal;
    }
}

public sealed class SortColumnHeaderForegroundConverter : IMultiValueConverter
{
    public static SortColumnHeaderForegroundConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isActive = SortColumnIsActiveConverter.Instance.Convert(values, targetType, parameter, culture);
        if (isActive is true
            && Application.Current?.TryGetResource("HelixAccentBrush", Application.Current.ActualThemeVariant, out var accent) == true
            && accent is IBrush accentBrush)
            return accentBrush;

        if (Application.Current?.TryGetResource("HelixSecondaryTextBrush", Application.Current.ActualThemeVariant, out var muted) == true
            && muted is IBrush mutedBrush)
            return mutedBrush;

        return Brushes.Gray;
    }
}

public sealed class SortColumnIndicatorGeometryConverter : IMultiValueConverter
{
    public static SortColumnIndicatorGeometryConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (SortColumnIsActiveConverter.Instance.Convert(values, targetType, parameter, culture) is not true)
            return null;

        var descending = values.Count > 1 && values[1] is true;
        var key = descending ? "HelixSortDescendingGeometry" : "HelixSortAscendingGeometry";
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource) == true
            && resource is Geometry geometry)
            return geometry;

        return null;
    }
}
