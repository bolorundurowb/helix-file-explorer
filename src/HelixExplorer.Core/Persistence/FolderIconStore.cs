namespace HelixExplorer.Core.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IFolderIconStore"/>.
/// All access is serialized on <see cref="IAppDatabase.ConnectionGate"/>.
/// </summary>
public sealed class FolderIconStore(IAppDatabase db) : IFolderIconStore
{
    public IReadOnlyDictionary<string, string> LoadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        lock (db.ConnectionGate)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT path, icon_file FROM folder_icons;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                var iconFile = reader.GetString(1);
                result[path] = iconFile;
            }
        }
        return result;
    }

    public void Upsert(string normalizedPath, string iconFilePath)
    {
        lock (db.ConnectionGate)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO folder_icons (path, icon_file)
                VALUES (@path, @iconFile)
                ON CONFLICT(path) DO UPDATE SET icon_file = excluded.icon_file;
                """;
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            cmd.Parameters.AddWithValue("@iconFile", iconFilePath);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(string normalizedPath)
    {
        lock (db.ConnectionGate)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM folder_icons WHERE path = @path;";
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            cmd.ExecuteNonQuery();
        }
    }
}
