using System.Security.Cryptography;
using System.Text;
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

    // Serializes read-modify-write across processes sharing the same settings file, so a stale
    // process cannot drop another process's edit. _gate already serializes in-process callers.
    private readonly Mutex _mutex = CreateSettingsMutex(path);

    public JsonSettingsStore() : this(AppPaths.SettingsFile)
    {
    }

    public AppSettings Load()
    {
        lock (_gate)
            return LoadCore();
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
            WithMutex(() => SaveCore(settings));
    }

    public void Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_gate)
            WithMutex(() =>
            {
                var settings = LoadCore();
                mutate(settings);
                SaveCore(settings);
            });
    }

    private AppSettings LoadCore()
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private void SaveCore(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, Options);
        // Unique temp name avoids cross-call clobber of a shared *.tmp; lock serializes replace.
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

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

    private void WithMutex(Action action)
    {
        var acquired = false;
        try
        {
            try
            {
                acquired = _mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // A previous owner crashed without releasing; we now own the mutex.
                acquired = true;
            }

            action();
        }
        finally
        {
            if (acquired)
                _mutex.ReleaseMutex();
        }
    }

    private static Mutex CreateSettingsMutex(string path)
    {
        var normalized = path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new Mutex(initiallyOwned: false, $"Local\\HelixExplorer.Settings.{hash}");
    }
}
