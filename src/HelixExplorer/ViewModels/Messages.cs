namespace HelixExplorer.ViewModels;

/// <summary>Requests navigation of the active pane to a filesystem path (home, folder, or shell location).</summary>
public sealed record GlobalNavigationRequestMessage(string Path);

/// <summary>Requests opening a URL in the system default browser.</summary>
public sealed record OpenUrlRequestMessage(string Url);
