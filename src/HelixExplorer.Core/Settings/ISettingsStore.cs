namespace HelixExplorer.Core.Settings;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);

    /// <summary>
    /// Read-modify-write: loads the latest persisted settings, applies <paramref name="mutate"/>,
    /// then saves. Only the fields touched by <paramref name="mutate"/> change, so concurrent
    /// windows/processes interleave their edits instead of overwriting the whole document.
    /// </summary>
    void Update(Action<AppSettings> mutate);
}
