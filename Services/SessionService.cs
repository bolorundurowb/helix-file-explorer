using System.IO;
using System.Text.Json;
using HelixExplorer.Infrastructure;
using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>
/// JSON-backed session store. The document is small (a handful of paths), so we read
/// and write it synchronously on demand rather than keeping a watcher open.
/// </summary>
public sealed class SessionService : ISessionService
{
    private static readonly string s_path = AppPaths.File("session.json");
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public SessionState Load()
    {
        try
        {
            if (!File.Exists(s_path)) return new SessionState();
            var json = File.ReadAllText(s_path);
            if (string.IsNullOrWhiteSpace(json)) return new SessionState();
            return JsonSerializer.Deserialize<SessionState>(json, s_json) ?? new SessionState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SessionService.Load failed: {ex.Message}");
            return new SessionState();
        }
    }

    public void Save(SessionState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, s_json);
            // Write to a temp file then move, so a crash mid-write can't corrupt the session.
            var tmp = s_path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, s_path, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SessionService.Save failed: {ex.Message}");
        }
    }
}
