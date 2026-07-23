using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Settings;

/// <summary>Atomic save: write to a sibling temp file, then move.</summary>
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();

    public JsonSettingsStore() : this(AppPaths.SettingsFile)
    {
    }

    public AppSettings Load()
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            string json;
            lock (_gate)
                json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
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
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                try { File.Delete(tempPath); } catch { /* best-effort */ }
                throw new IOException($"Failed to save settings to {path}", ex);
            }
        }
    }
}
