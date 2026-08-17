using System.Text.Json;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using Microsoft.Data.Sqlite;

namespace HelixExplorer.Core.Persistence;

/// <summary>
/// SQLite implementation of <see cref="IFolderViewPreferencesStore"/>.
/// All access is serialized on <see cref="IAppDatabase.ConnectionGate"/>.
/// </summary>
public sealed class FolderViewPreferencesStore : IFolderViewPreferencesStore
{
    private readonly IAppDatabase _db;
    private static readonly JsonSerializerOptions CollapsedKeysOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FolderViewPreferencesStore(IAppDatabase db) => _db = db;

    public bool TryGet(string normalizedPath, out FolderViewPreferences preferences)
    {
        preferences = new FolderViewPreferences();
        lock (_db.ConnectionGate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT view_mode, sort_column, sort_descending, directory_sort,
                       thumbnail_size, group_by, collapsed_group_keys
                FROM folder_view_preferences
                WHERE path = @path;
                """;
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return false;

            preferences.ViewMode = (LayoutMode)reader.GetInt64(0);
            preferences.SortColumn = (SortColumn)reader.GetInt64(1);
            preferences.SortDescending = reader.GetInt64(2) != 0;
            preferences.DirectorySort = (DirectorySortMode)reader.GetInt64(3);
            preferences.ThumbnailSize = reader.GetDouble(4);
            preferences.GroupBy = (GroupByMode)reader.GetInt64(5);
            var keysJson = reader.GetString(6);
            preferences.CollapsedGroupKeys =
                JsonSerializer.Deserialize<List<string>>(keysJson, CollapsedKeysOptions) ?? [];
            return true;
        }
    }

    public void Upsert(string normalizedPath, FolderViewPreferences preferences)
    {
        lock (_db.ConnectionGate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO folder_view_preferences
                    (path, view_mode, sort_column, sort_descending, directory_sort,
                     thumbnail_size, group_by, collapsed_group_keys)
                VALUES
                    (@path, @viewMode, @sortColumn, @sortDescending, @directorySort,
                     @thumbnailSize, @groupBy, @collapsedGroupKeys)
                ON CONFLICT(path) DO UPDATE SET
                    view_mode = excluded.view_mode,
                    sort_column = excluded.sort_column,
                    sort_descending = excluded.sort_descending,
                    directory_sort = excluded.directory_sort,
                    thumbnail_size = excluded.thumbnail_size,
                    group_by = excluded.group_by,
                    collapsed_group_keys = excluded.collapsed_group_keys;
                """;
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            cmd.Parameters.AddWithValue("@viewMode", (int)preferences.ViewMode);
            cmd.Parameters.AddWithValue("@sortColumn", (int)preferences.SortColumn);
            cmd.Parameters.AddWithValue("@sortDescending", preferences.SortDescending ? 1 : 0);
            cmd.Parameters.AddWithValue("@directorySort", (int)preferences.DirectorySort);
            cmd.Parameters.AddWithValue("@thumbnailSize", preferences.ThumbnailSize);
            cmd.Parameters.AddWithValue("@groupBy", (int)preferences.GroupBy);
            cmd.Parameters.AddWithValue("@collapsedGroupKeys",
                JsonSerializer.Serialize(preferences.CollapsedGroupKeys ?? new List<string>(), CollapsedKeysOptions));
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(string normalizedPath)
    {
        lock (_db.ConnectionGate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM folder_view_preferences WHERE path = @path;";
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            cmd.ExecuteNonQuery();
        }
    }
}
