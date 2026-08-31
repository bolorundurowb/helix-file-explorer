using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Session;

/// <summary>
/// Atomic save: write to a sibling temp file, then move, so a crash mid-write cannot corrupt session.json.
/// </summary>
public sealed class JsonSessionStore(string path) : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();

    public JsonSessionStore() : this(AppPaths.SessionFile)
    {
    }

    public SessionDocument Load()
    {
        if (!File.Exists(path))
            return new SessionDocument();

        // Deliberately broad: same "corrupt/unreadable file degrades to defaults" pattern used by
        // JsonSettingsStore.Load. A malformed or unreadable session.json should just start a fresh
        // session, not crash the app on launch.
#pragma warning disable CA1031
        try
        {
            string json;
            lock (_gate)
                json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SessionDocument>(json, Options) ?? new SessionDocument();
        }
        catch
        {
            return new SessionDocument();
        }
#pragma warning restore CA1031
    }

    public void Save(SessionDocument document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(document, Options);
        // Unique temp name avoids cross-call clobber of a shared *.tmp; lock serializes replace.
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        lock (_gate)
        {
            // Deliberately broad: cleanup-then-rethrow. The write/move sequence can fail in more
            // ways than one exception type, and every one of them should still trigger the same
            // temp-file cleanup before the original failure propagates to the caller.
#pragma warning disable CA1031
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { /* best-effort */ }
                throw;
            }
#pragma warning restore CA1031
        }
    }
}
