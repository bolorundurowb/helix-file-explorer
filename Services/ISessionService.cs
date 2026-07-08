using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>Persists and restores the workspace session (open tabs, paths, layout).</summary>
public interface ISessionService
{
    /// <summary>Reads the last session, or an empty session when none exists / on error.</summary>
    SessionState Load();

    /// <summary>Writes the current session to disk (best-effort; never throws).</summary>
    void Save(SessionState state);
}
