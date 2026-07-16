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

    public JsonSessionStore() : this(AppPaths.SessionFile)
    {
    }

    public SessionDocument Load()
    {
        if (!File.Exists(path))
            return new SessionDocument();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SessionDocument>(json, Options) ?? new SessionDocument();
        }
        catch
        {
            return new SessionDocument();
        }
    }

    public void Save(SessionDocument document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(document, Options);
        var tempPath = path + ".tmp";

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }
}
