using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.Core.Settings;

/// <summary>Atomic save: write to a sibling temp file, then move.</summary>
public sealed class JsonSettingsStore(string path, ILogger<JsonSettingsStore>? logger = null) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger _logger = logger ?? NullLogger<JsonSettingsStore>.Instance;
    private readonly object _gate = new();

    public JsonSettingsStore() : this(AppPaths.SettingsFile)
    {
    }

    /// <summary>DI entry point: same default path, but with a real logger instead of NullLogger.</summary>
    public JsonSettingsStore(ILogger<JsonSettingsStore> logger) : this(AppPaths.SettingsFile, logger)
    {
    }

    public AppSettings Load()
    {
        if (!File.Exists(path))
            return new AppSettings();

        string json;
        try
        {
            lock (_gate)
                json = File.ReadAllText(path);
        }
        // CA1031 flags the catch clause's declared type (Exception), not the `when` filter, so this
        // multi-type catch - the only way to catch two unrelated exception types in C# without a
        // shared base - reads as "general" to the analyzer despite already being narrowed to exactly
        // IOException/UnauthorizedAccessException.
#pragma warning disable CA1031
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient access problem, not corrupt data: do not quarantine a file that may be
            // perfectly fine once whatever is locking it lets go.
            _logger.LogError(ex, "Failed to read settings file '{Path}'; using defaults for this session.", path);
            return new AppSettings();
        }
#pragma warning restore CA1031

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        // Deliberately broad: JsonSerializer.Deserialize can fail in more ways than JsonException
        // (e.g. a custom converter throwing something else), and every one of them means the same
        // thing here - the file is unusable and must be quarantined rather than left to corrupt the
        // next save. Narrowing this to specific exception types risks letting an unanticipated
        // failure mode propagate and crash startup instead of degrading to defaults.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            // Previously: silently returned defaults with zero trace of what happened. Losing every
            // setting is bad enough without also erasing the evidence needed to find out why.
            _logger.LogError(ex, "Settings file '{Path}' is corrupt; quarantining it and starting from defaults.", path);
            QuarantineCorruptFile();
            return new AppSettings();
        }
#pragma warning restore CA1031
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, Options);
        // Unique temp name avoids cross-call clobber of a shared *.tmp; lock serializes replace.
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        lock (_gate)
        {
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    // Force the temp file's bytes to physical disk before the atomic rename below,
                    // not just to the OS page cache: otherwise an unclean shutdown right after save
                    // could rename in data that a crash then loses, leaving settings.json truncated.
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, path, overwrite: true);
            }
            // Deliberately broad: the goal is to translate ANY failure from the write/flush/rename
            // sequence above into a single wrapped IOException with the destination path attached,
            // regardless of which BCL exception type actually caused it. Narrowing this would mean
            // guessing the complete exception surface of FileStream/StreamWriter/File.Move up front;
            // getting that list wrong would let a real save failure escape unwrapped and without the
            // temp-file cleanup below. The inner bare `catch` is the same best-effort cleanup pattern
            // used elsewhere in this codebase (e.g. JsonSessionStore.Save) - deletion failing here
            // must never mask the original save failure being thrown below.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                try { File.Delete(tempPath); } catch { /* best-effort */ }
                throw new IOException($"Failed to save settings to {path}", ex);
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
        // Deliberately broad: this is already a best-effort cleanup step reached only after Load()
        // has decided the file is corrupt. Any failure here (locked file, permissions, ...) must be
        // logged and swallowed rather than propagate, since a caller mid-startup-recovery has no
        // better response to a quarantine failure than continuing with defaults anyway.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            // Best-effort: still return defaults even if quarantining itself fails (e.g. the file
            // is locked). The next successful Save() will overwrite the corrupt file anyway.
            _logger.LogWarning(ex, "Failed to quarantine corrupt settings file '{Path}'.", path);
        }
#pragma warning restore CA1031
    }
}
