namespace HelixExplorer.Core.Theming;

public interface IAccentBrushService
{
    uint? CustomAccentArgb { get; }

    void ApplyCustomAccent(uint? argb);

    event Action? AccentChanged;
}
