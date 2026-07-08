using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Session;

/// <summary>
/// JSON-backed session store. Saves are atomic: the document is written to a sibling
/// temp file and then moved over the target so a crash mid-write cannot corrupt session.json.
/// </summary>
public sealed class JsonSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public JsonSessionStore() : this(AppPaths.SessionFile)
    {
    }

    public JsonSessionStore(string path)
    {
        _path = path;
    }

    public SessionDocument Load()
    {
        if (!File.Exists(_path))
            return new SessionDocument();

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<SessionDocument>(json, Options) ?? new SessionDocument();
        }
        catch
        {
            return new SessionDocument();
        }
    }

    public void Save(SessionDocument document)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(document, Options);
        var tempPath = _path + ".tmp";

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }
}
