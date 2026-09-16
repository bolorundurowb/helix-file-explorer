namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// In-memory filesystem tree used to exercise filesystem consumers deterministically without touching
/// the real disk. Entries are keyed by a normalized absolute path and support permission-error
/// simulation (<see cref="SetAccessDenied"/>), hidden flags, and file content for content search.
/// </summary>
public sealed class VirtualFileSystem
{
    private static readonly DateTime DefaultModifiedUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Dictionary<string, VirtualEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    public VirtualFileSystem(string root = @"C:\virtual")
    {
        _root = Normalize(root);
        _entries[_root] = new VirtualEntry
        {
            FullPath = _root,
            Name = Path.GetFileName(_root),
            IsDirectory = true,
            ModifiedUtc = DefaultModifiedUtc,
        };
    }

    public string Root => _root;

    public void AddFile(string path, string content = "")
    {
        var full = ToAbsolute(path);
        EnsureParent(full);
        _entries[full] = new VirtualEntry
        {
            FullPath = full,
            Name = Path.GetFileName(full),
            IsDirectory = false,
            SizeBytes = content.Length,
            Content = content,
            ModifiedUtc = DefaultModifiedUtc,
        };
    }

    public void AddDirectory(string path)
    {
        var full = ToAbsolute(path);
        EnsureDirectory(full);
    }

    public void Delete(string path)
    {
        var full = ToAbsolute(path);
        var doomed = _entries.Values
            .Where(e => string.Equals(e.FullPath, full, StringComparison.OrdinalIgnoreCase)
                        || e.FullPath.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullPath)
            .ToList();

        foreach (var key in doomed)
            _entries.Remove(key);
    }

    public void SetHidden(string path, bool hidden = true)
        => Find(ToAbsolute(path))!.IsHidden = hidden;

    public void SetAccessDenied(string path, bool denied = true)
        => Find(ToAbsolute(path))!.AccessDenied = denied;

    internal string Resolve(string path)
        => string.IsNullOrWhiteSpace(path) ? path : Normalize(path);

    internal VirtualEntry? Find(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return _entries.TryGetValue(Normalize(path), out var entry) ? entry : null;
    }

    internal IReadOnlyList<VirtualEntry> GetChildren(string directoryPath)
    {
        var children = new List<VirtualEntry>();
        foreach (var entry in _entries.Values)
        {
            if (string.Equals(entry.FullPath, directoryPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var parent = Path.GetDirectoryName(entry.FullPath);
            if (string.Equals(parent, directoryPath, StringComparison.OrdinalIgnoreCase))
                children.Add(entry);
        }

        return children;
    }

    private string ToAbsolute(string path)
        => Path.IsPathRooted(path) ? Normalize(path) : Normalize(Path.Combine(_root, path));

    private void EnsureParent(string full)
    {
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
            EnsureDirectory(parent);
    }

    private void EnsureDirectory(string full)
    {
        if (_entries.ContainsKey(full))
            return;

        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent) && !_entries.ContainsKey(parent))
            EnsureDirectory(parent);

        _entries[full] = new VirtualEntry
        {
            FullPath = full,
            Name = Path.GetFileName(full),
            IsDirectory = true,
            ModifiedUtc = DefaultModifiedUtc,
        };
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 3)
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return full;
    }

    internal sealed class VirtualEntry
    {
        public required string FullPath { get; init; }
        public required string Name { get; init; }
        public required bool IsDirectory { get; init; }
        public long SizeBytes { get; init; }
        public DateTime ModifiedUtc { get; init; }
        public string Content { get; init; } = string.Empty;
        public bool IsHidden { get; set; }
        public bool AccessDenied { get; set; }
    }
}
