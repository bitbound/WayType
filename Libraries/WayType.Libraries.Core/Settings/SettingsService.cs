using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Serialization;

namespace WayType.Libraries.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? SettingsChanged;

    Task SaveAsync(CancellationToken cancellationToken = default);

    void Reload();
}

public sealed class SettingsService : ISettingsService
{
    private readonly IFileStore _fileStore;
    private readonly ILogger<SettingsService> _logger;
    private readonly IPlatformPaths _paths;
    private readonly Lock _sync = new();

    public SettingsService(IPlatformPaths paths, IFileStore fileStore, ILogger<SettingsService> logger)
    {
        _paths = paths;
        _fileStore = fileStore;
        _logger = logger;

        Current = Read();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? SettingsChanged;

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        AppSettings snapshot;

        lock (_sync)
        {
            snapshot = Current;
        }

        _paths.EnsureDirectories();

        var contents = JsonSerializer.Serialize(snapshot, WayTypeJson.Options);
        await _fileStore.WriteAllTextAsync(_paths.SettingsFilePath, contents, cancellationToken);
        _fileStore.RestrictToOwner(_paths.SettingsFilePath);

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
        lock (_sync)
        {
            Current = Read();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Read()
    {
        var contents = _fileStore.ReadAllTextOrNull(_paths.SettingsFilePath);

        if (string.IsNullOrWhiteSpace(contents))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(contents, WayTypeJson.Options)
                ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Settings file {Path} is unreadable, falling back to defaults.", _paths.SettingsFilePath);
            return new AppSettings();
        }
    }
}
