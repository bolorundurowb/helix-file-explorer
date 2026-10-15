namespace HelixExplorer.Core.Theming;

/// <summary>
/// Reads the operating-system contrast theme. Null means no contrast theme is active.
/// </summary>
public interface IHighContrastSource
{
    HighContrastPalette? TryReadActivePalette();
}
