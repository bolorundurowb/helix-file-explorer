namespace HelixExplorer.Core.Models;

public sealed record VolumeInfo(
    string RootPath,
    string Label,
    string DisplayName,
    DriveType DriveType,
    bool IsReady);
