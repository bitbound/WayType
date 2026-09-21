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
    private readonly IRecordingStore _recordings;
    private readonly ISettingsService _settings;
    private readonly Lock _sync = new();

    private List<HistoryEntry>? _cached;

    public HistoryService(IPlatformPaths paths, IFileStore fileStore, IRecordingStore recordings, ISettingsService settings)
    {
        _paths = paths;
        _fileStore = fileStore;
        _recordings = recordings;
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
        List<HistoryEntry> trimmed = [];

        lock (_sync)
        {
            next = Trim(Load(), trimmed);

            next.Add(entry);
            next = Trim(next, trimmed);

            _cached = next;
        }

        RemoveRecordings(trimmed);

        await WriteAsync(next, cancellationToken);

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        List<HistoryEntry> next;
        HistoryEntry? removed;

        lock (_sync)
        {
            next = Load();

            var index = next.FindIndex(entry => entry.Id == id);

            if (index < 0)
            {
                return false;
            }

            removed = next[index];
            next.RemoveAt(index);

            _cached = next;
        }

        _recordings.Delete(removed.AudioFileName);

        await WriteAsync(next, cancellationToken);

        HistoryChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        List<HistoryEntry> removed;

        lock (_sync)
        {
            removed = Load();
            _cached = [];
        }

        RemoveRecordings(removed);

        await WriteAsync([], cancellationToken);

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    // Once a row is gone its audio is unreachable, so it goes with the row.
    private void RemoveRecordings(IEnumerable<HistoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            _recordings.Delete(entry.AudioFileName);
        }
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

    private List<HistoryEntry> Trim(List<HistoryEntry> entries, List<HistoryEntry> dropped)
    {
        var keep = Math.Clamp(_settings.Current.HistoryItemsToKeep, 0, HardCap);

        if (entries.Count <= keep)
        {
            return entries;
        }

        var kept = NewestFirst(entries).Take(keep).ToList();

        dropped.AddRange(entries.Except(kept));

        return kept;
    }

    private static IEnumerable<HistoryEntry> NewestFirst(List<HistoryEntry> entries)
    {
        return entries.OrderByDescending(entry => entry.TimestampUtc);
    }
}
