namespace HelixExplorer.Core.Session;

/// <summary>Loads and persists the workspace session snapshot.</summary>
public interface ISessionStore
{
    /// <summary>Loads the last session, or an empty document when none exists / is corrupt.</summary>
    SessionDocument Load();

    /// <summary>Atomically writes the session snapshot to disk.</summary>
    void Save(SessionDocument document);
}
