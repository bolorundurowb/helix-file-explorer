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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient access problem, not corrupt data: do not quarantine a file that may be
            // perfectly fine once whatever is locking it lets go.
            _logger.LogError(ex, "Failed to read settings file '{Path}'; using defaults for this session.", path);
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Previously: silently returned defaults with zero trace of what happened. Losing every
            // setting is bad enough without also erasing the evidence needed to find out why.
            _logger.LogError(ex, "Settings file '{Path}' is corrupt; quarantining it and starting from defaults.", path);
            QuarantineCorruptFile();
            return new AppSettings();
        }
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
            catch (Exception ex)
            {
                try { File.Delete(tempPath); } catch { /* best-effort */ }
                throw new IOException($"Failed to save settings to {path}", ex);
            }
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
        catch (Exception ex)
        {
            // Best-effort: still return defaults even if quarantining itself fails (e.g. the file
            // is locked). The next successful Save() will overwrite the corrupt file anyway.
            _logger.LogWarning(ex, "Failed to quarantine corrupt settings file '{Path}'.", path);
        }
    }
}
