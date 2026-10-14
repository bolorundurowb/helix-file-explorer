namespace HelixExplorer.Core.Theming;

/// <summary>
/// Reads the operating-system accent. Null means the OS did not provide one.
/// </summary>
public interface IAccentColorSource
{
    uint? TryReadAccentColor();
}
