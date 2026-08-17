using Microsoft.Data.Sqlite;

namespace HelixExplorer.Core.Persistence;

/// <summary>
/// Owns the shared SQLite connection and schema for the app database
/// (<see cref="HelixExplorer.Core.Infrastructure.AppPaths.AppDatabaseFile"/>). Stores borrow the connection
/// and synchronize on <see cref="ConnectionGate"/>.
/// </summary>
public interface IAppDatabase : IDisposable
{
    /// <summary>
    /// Opens the connection, applies pragmas, creates the schema, and runs the
    /// legacy JSON migration. Must be called once before any store access and
    /// before <see cref="Core.Settings.ISettingsStore"/> load/save so chrome
    /// flushes cannot drop the legacy maps before they are migrated.
    /// </summary>
    void Initialize();

    /// <summary>
    /// The shared, already-open connection. Store commands are created on this
    /// connection while holding <see cref="ConnectionGate"/>.
    /// </summary>
    SqliteConnection Connection { get; }

    /// <summary>
    /// Lock that serializes all access to <see cref="Connection"/>. Stores must
    /// hold this lock for the duration of each command execution.
    /// </summary>
    object ConnectionGate { get; }
}
