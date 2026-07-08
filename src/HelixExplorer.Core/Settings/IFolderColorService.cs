namespace HelixExplorer.Core.Settings;

public interface IFolderColorService
{
    event EventHandler? ColorsChanged;

    bool TryGetColor(string path, out uint argb);

    void SetColor(string path, uint argb);

    void RemoveColor(string path);

    IReadOnlyDictionary<string, uint> GetAll();
}
