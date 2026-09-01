using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.Core.Session;

/// <summary>
/// Atomic save: write to a sibling temp file, then move, so a crash mid-write cannot corrupt session.json.
/// </summary>
public sealed class JsonSessionStore(string path, ILogger<JsonSessionStore>? logger = null) : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly ILogger _logger = logger ?? NullLogger<JsonSessionStore>.Instance;

    public JsonSessionStore() : this(AppPaths.SessionFile)
    {
    }

    public JsonSessionStore(ILogger<JsonSessionStore> logger) : this(AppPaths.SessionFile, logger)
    {
    }

    public SessionDocument Load()
    {
        if (!File.Exists(path))
            return new SessionDocument();

#pragma warning disable CA1031
        try
        {
            string json;
            lock (_gate)
                json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SessionDocument>(json, Options) ?? new SessionDocument();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session file '{Path}' is unreadable; quarantining it and starting a fresh session.", path);
            QuarantineCorruptFile();
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
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

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

    private void QuarantineCorruptFile()
    {
        try
        {
            var quarantinePath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            lock (_gate)
            {
                if (File.Exists(path))
                    File.Move(path, quarantinePath, overwrite: true);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to quarantine corrupt session file '{Path}'.", path);
        }
#pragma warning restore CA1031
    }
}
