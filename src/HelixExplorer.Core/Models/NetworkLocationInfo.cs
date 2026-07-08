namespace HelixExplorer.Core.Models;

public sealed record NetworkLocationInfo(
    string Path,
    string DisplayName,
    string? Comment = null);
