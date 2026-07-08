using System.IO;
using System.Text.Json;
using HelixExplorer.Infrastructure;

namespace HelixExplorer.Services;

/// <summary>JSON-backed user settings, persisted to <c>settings.json</c>.</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly string s_path = AppPaths.File("settings.json");
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private SizeDisplayMode _fileSizeDisplayMode = SizeDisplayMode.Binary;
    private double _thumbnailSize = 96;
    private int _defaultViewMode; // LayoutMode.Details
    private bool _dualPaneVertical;
    private bool _loading;

    public SettingsService() => Load();

    public SizeDisplayMode FileSizeDisplayMode
    {
        get => _fileSizeDisplayMode;
        set => Set(ref _fileSizeDisplayMode, value);
    }

    public double ThumbnailSize
    {
        get => _thumbnailSize;
        set => Set(ref _thumbnailSize, Math.Clamp(value, 32, 256));
    }

    public int DefaultViewMode
    {
        get => _defaultViewMode;
        set => Set(ref _defaultViewMode, value);
    }

    public bool DualPaneVertical
    {
        get => _dualPaneVertical;
        set => Set(ref _dualPaneVertical, value);
    }

    public event EventHandler? SettingsChanged;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        if (_loading) return;
        Persist();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SettingsDto
    {
        public int FileSizeDisplayMode { get; set; }
        public double ThumbnailSize { get; set; } = 96;
        public int DefaultViewMode { get; set; }
        public bool DualPaneVertical { get; set; }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(s_path)) return;
            var dto = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(s_path), s_json);
            if (dto is null) return;
            _loading = true;
            FileSizeDisplayMode = (SizeDisplayMode)dto.FileSizeDisplayMode;
            ThumbnailSize = dto.ThumbnailSize;
            DefaultViewMode = dto.DefaultViewMode;
            DualPaneVertical = dto.DualPaneVertical;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsService.Load failed: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    private void Persist()
    {
        try
        {
            var dto = new SettingsDto
            {
                FileSizeDisplayMode = (int)_fileSizeDisplayMode,
                ThumbnailSize = _thumbnailSize,
                DefaultViewMode = _defaultViewMode,
                DualPaneVertical = _dualPaneVertical,
            };
            File.WriteAllText(s_path, JsonSerializer.Serialize(dto, s_json));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsService.Persist failed: {ex.Message}");
        }
    }
}
