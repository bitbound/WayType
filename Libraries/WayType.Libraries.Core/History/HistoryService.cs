using System.Text.Json;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Serialization;
using WayType.Libraries.Core.Settings;

namespace WayType.Libraries.Core.History;

public interface IHistoryService
{
    event EventHandler? HistoryChanged;

    IReadOnlyList<HistoryEntry> GetAll();

    Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class HistoryService : IHistoryService
{
    private const int HardCap = 5000;

    private readonly IFileStore _fileStore;
    private readonly IPlatformPaths _paths;
    private readonly ISettingsService _settings;
    private readonly Lock _sync = new();

    private List<HistoryEntry>? _cached;

    public HistoryService(IPlatformPaths paths, IFileStore fileStore, ISettingsService settings)
    {
        _paths = paths;
        _fileStore = fileStore;
        _settings = settings;
    }

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<HistoryEntry> GetAll()
    {
        lock (_sync)
        {
            return [.. NewestFirst(Load())];
        }
    }

    public async Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        List<HistoryEntry> next;

        lock (_sync)
        {
            next = Trim(Load());

            next.Add(entry);
            next = Trim(next);

            _cached = next;
        }

        await WriteAsync(next, cancellationToken);

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        List<HistoryEntry> next;

        lock (_sync)
        {
            next = Load();

            if (next.RemoveAll(entry => entry.Id == id) == 0)
            {
                return false;
            }

            _cached = next;
        }

        await WriteAsync(next, cancellationToken);

        HistoryChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _cached = [];
        }

        await WriteAsync([], cancellationToken);

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<HistoryEntry> Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var contents = _fileStore.ReadAllTextOrNull(_paths.HistoryFilePath);

        if (string.IsNullOrWhiteSpace(contents))
        {
            _cached = [];
            return _cached;
        }

        try
        {
            _cached = JsonSerializer.Deserialize<List<HistoryEntry>>(contents, WayTypeJson.Options) ?? [];
        }
        catch (JsonException)
        {
            _cached = [];
        }

        return _cached;
    }

    private async Task WriteAsync(List<HistoryEntry> entries, CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();

        var contents = JsonSerializer.Serialize(entries, WayTypeJson.Options);
        await _fileStore.WriteAllTextAsync(_paths.HistoryFilePath, contents, cancellationToken);
        _fileStore.RestrictToOwner(_paths.HistoryFilePath);
    }

    private List<HistoryEntry> Trim(List<HistoryEntry> entries)
    {
        var keep = Math.Clamp(_settings.Current.HistoryItemsToKeep, 0, HardCap);

        if (entries.Count <= keep)
        {
            return entries;
        }

        return [.. NewestFirst(entries).Take(keep)];
    }

    private static IEnumerable<HistoryEntry> NewestFirst(List<HistoryEntry> entries)
    {
        return entries.OrderByDescending(entry => entry.TimestampUtc);
    }
}
