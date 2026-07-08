namespace HelixExplorer.Core.FileSystem;

public enum ClipboardOperation
{
    Copy,
    Cut
}

public sealed record ClipboardPayload(
    ClipboardOperation Operation,
    IReadOnlyList<string> Paths,
    string SourceDirectory);
