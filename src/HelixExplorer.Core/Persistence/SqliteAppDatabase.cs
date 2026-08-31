using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Settings;
using Microsoft.Data.Sqlite;

namespace HelixExplorer.Core.Persistence;

/// <summary>
/// SQLite-backed <see cref="IAppDatabase"/>. Opens a single connection in WAL mode,
/// applies schema v1, and runs a one-time legacy JSON migration before any chrome
/// settings load/save can drop the old maps.
/// </summary>
public sealed class SqliteAppDatabase : IAppDatabase
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions CollapsedKeysOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISettingsStore _settingsStore;
    private readonly string _settingsFile;
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private bool _disposed;
    private bool _initialized;

    public SqliteAppDatabase(ISettingsStore? settingsStore = null, string? databasePath = null, string? settingsFile = null)
    {
        var path = databasePath ?? AppPaths.AppDatabaseFile;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        _connection = new SqliteConnection(csb.ToString());
        _settingsStore = settingsStore ?? new JsonSettingsStore();
        _settingsFile = settingsFile ?? AppPaths.SettingsFile;
    }

    public SqliteConnection Connection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_initialized)
                throw new InvalidOperationException("Call Initialize() before accessing the connection.");
            return _connection;
        }
    }

    public object ConnectionGate => _gate;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;

        lock (_gate)
        {
            if (_initialized)
                return;

            _connection.Open();
            ApplyPragmas();
            ApplySchema();
            MigrateLegacyJson();
            _initialized = true;
        }
    }

    private void ApplyPragmas()
    {
        using (var wal = _connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
        }
        using (var sync = _connection.CreateCommand())
        {
            sync.CommandText = "PRAGMA synchronous = NORMAL;";
            sync.ExecuteNonQuery();
        }
        using (var busyTimeout = _connection.CreateCommand())
        {
            // Nothing stops a user from launching a second Helix Explorer process (no
            // single-instance guard), and both processes write to the same helix.db. WAL lets
            // readers proceed during a writer, but writer-vs-writer contention still exists, and
            // without busy_timeout SQLite returns SQLITE_BUSY immediately instead of waiting a
            // moment for the other process's (millisecond-scale) transaction to commit. None of
            // the store call sites catch SqliteException today, so an immediate SQLITE_BUSY would
            // surface as an unhandled exception on whatever thread is saving/loading preferences.
            busyTimeout.CommandText = "PRAGMA busy_timeout = 3000;";
            busyTimeout.ExecuteNonQuery();
        }
    }

    private void ApplySchema()
    {
        using var versionCmd = _connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        var currentVersion = Convert.ToInt32(versionCmd.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);

        if (currentVersion >= SchemaVersion)
            return;

        using var batch = _connection.CreateCommand();
        batch.CommandText = """
            CREATE TABLE IF NOT EXISTS folder_view_preferences (
                path TEXT PRIMARY KEY COLLATE NOCASE,
                view_mode INTEGER NOT NULL,
                sort_column INTEGER NOT NULL,
                sort_descending INTEGER NOT NULL,
                directory_sort INTEGER NOT NULL,
                thumbnail_size REAL NOT NULL,
                group_by INTEGER NOT NULL,
                collapsed_group_keys TEXT NOT NULL DEFAULT '[]'
            );

            CREATE TABLE IF NOT EXISTS folder_colors (
                path TEXT PRIMARY KEY COLLATE NOCASE,
                color_argb INTEGER NOT NULL
            );

            PRAGMA user_version = 1;
            """;
        batch.ExecuteNonQuery();
    }

    private void MigrateLegacyJson()
    {
        if (!File.Exists(_settingsFile))
            return;

        LegacySettingsDto? legacy;
        // Deliberately broad: this is a one-time, best-effort legacy-data migration. Any failure
        // reading or deserializing the old settings.json (missing, corrupt, locked, unexpected
        // shape) should just skip the migration - the two maps being migrated are non-critical view
        // preferences and folder colors, not something worth crashing startup over.
#pragma warning disable CA1031
        try
        {
            var json = File.ReadAllText(_settingsFile);
            legacy = JsonSerializer.Deserialize<LegacySettingsDto>(json, LegacyJsonOptions);
        }
        catch
        {
            return;
        }
#pragma warning restore CA1031

        if (legacy is null)
            return;

        var prefs = legacy.FolderViewPreferences;
        var colors = legacy.FolderColors;
        var hasPrefs = prefs is { Count: > 0 };
        var hasColors = colors is { Count: > 0 };

        if (!hasPrefs && !hasColors)
            return;

        var migrated = false;

        if (hasPrefs && TableIsEmpty("folder_view_preferences") && prefs is not null)
        {
            foreach (var (path, p) in prefs)
            {
                var normalized = NormalizePath(path);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                using var cmd = _connection.CreateCommand();
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
                cmd.Parameters.AddWithValue("@path", normalized);
                cmd.Parameters.AddWithValue("@viewMode", (int)p.ViewMode);
                cmd.Parameters.AddWithValue("@sortColumn", (int)p.SortColumn);
                cmd.Parameters.AddWithValue("@sortDescending", p.SortDescending ? 1 : 0);
                cmd.Parameters.AddWithValue("@directorySort", (int)p.DirectorySort);
                cmd.Parameters.AddWithValue("@thumbnailSize", p.ThumbnailSize);
                cmd.Parameters.AddWithValue("@groupBy", (int)p.GroupBy);
                cmd.Parameters.AddWithValue("@collapsedGroupKeys",
                    JsonSerializer.Serialize(p.CollapsedGroupKeys ?? new List<string>(), CollapsedKeysOptions));
                cmd.ExecuteNonQuery();
            }
            migrated = true;
        }

        if (hasColors && TableIsEmpty("folder_colors") && colors is not null)
        {
            foreach (var (path, color) in colors)
            {
                var normalized = NormalizePath(path);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO folder_colors (path, color_argb)
                    VALUES (@path, @color)
                    ON CONFLICT(path) DO UPDATE SET color_argb = excluded.color_argb;
                    """;
                cmd.Parameters.AddWithValue("@path", normalized);
                cmd.Parameters.AddWithValue("@color", (long)color);
                cmd.ExecuteNonQuery();
            }
            migrated = true;
        }

        if (migrated)
        {
            // Rewrite settings.json via the store so the legacy map keys are dropped from
            // the file immediately. AppSettings no longer declares these properties, so
            // System.Text.Json ignores them on load and omits them on save.
            var settings = _settingsStore.Load();
            _settingsStore.Save(settings);
        }
    }

    private bool TableIsEmpty(string table)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM " + table + ";";
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, System.Globalization.CultureInfo.InvariantCulture) == 0L;
    }

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return path.TrimEnd('\\', '/');
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_gate)
        {
            if (_initialized)
            {
                // Deliberately broad: this is a best-effort WAL checkpoint on Dispose. A failure here
                // (connection already in a bad state, disk issue) must not prevent the connection
                // from still being disposed immediately below.
#pragma warning disable CA1031
                try
                {
                    using var checkpoint = _connection.CreateCommand();
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }
                catch
                {
                    // Best-effort checkpoint on shutdown.
                }
#pragma warning restore CA1031
            }
            _connection.Dispose();
        }
    }

    /// <summary>
    /// Legacy DTO used only for migration. Only the two map properties that have
    /// moved to SQLite are read; everything else is ignored.
    /// </summary>
    private sealed class LegacySettingsDto
    {
        public Dictionary<string, FolderViewPreferences>? FolderViewPreferences { get; set; }
        public Dictionary<string, uint>? FolderColors { get; set; }
    }
}
