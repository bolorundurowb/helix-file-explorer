namespace HelixExplorer.Core.Session;

public interface ISessionStore
{
    /// <summary>Missing or corrupt session yields an empty document so callers need not special-case load failures.</summary>
    SessionDocument Load();

    /// <summary>Atomic write so a crash mid-save cannot corrupt session.json.</summary>
    void Save(SessionDocument document);
}
