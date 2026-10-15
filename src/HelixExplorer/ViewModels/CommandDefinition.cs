namespace HelixExplorer.ViewModels;

/// <summary>
/// One command as the palette and generated menus see it. <paramref name="Icon"/> is a geometry resource key.
/// <paramref name="IsChecked"/> marks a radio option whose state is read from the target each time a menu opens.
/// </summary>
public sealed record CommandDefinition<TTarget>(
    string Id,
    string Label,
    string Category,
    string? Icon,
    string Gesture,
    Action<TTarget> Execute,
    Func<TTarget, bool>? CanExecute = null,
    Func<TTarget, bool>? IsChecked = null);
