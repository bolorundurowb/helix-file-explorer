using Microsoft.Data.Sqlite;

namespace HelixExplorer.Core.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IFolderColorStore"/>.
/// All access is serialized on <see cref="IAppDatabase.ConnectionGate"/>.
/// </summary>
public sealed class FolderColorStore : IFolderColorStore
{
    private readonly IAppDatabase _db;

    public FolderColorStore(IAppDatabase db) => _db = db;

    public IReadOnlyDictionary<string, uint> LoadAll()
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        lock (_db.ConnectionGate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT path, color_argb FROM folder_colors;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                var color = (uint)reader.GetInt64(1);
                result[path] = color;
            }
        }
        return result;
    }

    public void Upsert(string normalizedPath, uint argb)
    {
        lock (_db.ConnectionGate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO folder_colors (path, color_argb)
                VALUES (@path, @color)
                ON CONFLICT(path) DO UPDATE SET color_argb = excluded.color_argb;
                """;
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            cmd.Parameters.AddWithValue("@color", (long)argb);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(string normalizedPath)
    {
        lock (_db.ConnectionGate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM folder_colors WHERE path = @path;";
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            cmd.ExecuteNonQuery();
        }
    }
}
