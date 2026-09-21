using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Core.History;
using WayType.Views;

namespace WayType.ViewModels;

public sealed partial class HistoryViewModel : ViewModelBase<HistoryView>
{
    private readonly IAudioPlayer _player;
    private readonly IHistoryService _history;
    private readonly ILogger<HistoryViewModel> _logger;
    private readonly IRecordingStore _recordings;

    private CancellationTokenSource? _playback;
    private Guid? _playingId;

    [ObservableProperty]
    private ObservableCollection<HistoryItemViewModel> _entries = [];

    [ObservableProperty]
    private HistoryItemViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _hasEntries;

    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private string? _playbackError;

    public HistoryViewModel(
        IHistoryService history,
        IRecordingStore recordings,
        IAudioPlayer player,
        ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _recordings = recordings;
        _player = player;
        _logger = logger;

        _history.HistoryChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
    }

    protected override Task OnInitializeAsync()
    {
        Refresh();

        return Task.CompletedTask;
    }

    partial void OnSelectedEntryChanged(HistoryItemViewModel? value)
    {
        CanDelete = value is not null;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedEntry is not null)
        {
            await _history.DeleteAsync(SelectedEntry.Id);
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        await _history.ClearAsync();
    }

    [RelayCommand]
    private async Task PlayAsync(HistoryItemViewModel? item)
    {
        if (item is null || !item.HasAudio)
        {
            return;
        }

        // Activating the row that is already playing stops it. Any other row takes playback over.
        var stopRequested = _playingId == item.Id;

        StopPlayback();

        if (stopRequested)
        {
            return;
        }

        if (_recordings.Read(item.AudioFileName) is not { } wav)
        {
            PlaybackError = "The audio for this entry is no longer on disk.";
            return;
        }

        var playback = new CancellationTokenSource();
        _playback = playback;
        _playingId = item.Id;
        PlaybackError = null;
        item.IsPlaying = true;

        try
        {
            await _player.PlayAsync(wav, playback.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playing a recording failed.");
            PlaybackError = ex.Message;
        }
        finally
        {
            item.IsPlaying = false;
            _playingId = null;

            Interlocked.CompareExchange(ref _playback, null, playback);
            playback.Dispose();
        }
    }

    private void StopPlayback()
    {
        Interlocked.Exchange(ref _playback, null)?.Cancel();
    }

    private void Refresh()
    {
        // Rebuilding the rows drops the playing flag, so playback has to end with them.
        StopPlayback();

        var selectedId = SelectedEntry?.Id;
        var items = _history.GetAll().Select(entry => HistoryItemViewModel.Create(entry, _recordings.Exists(entry.AudioFileName)));

        Entries = new ObservableCollection<HistoryItemViewModel>(items);
        HasEntries = Entries.Count > 0;

        SelectedEntry = Entries.FirstOrDefault(item => item.Id == selectedId) ?? Entries.FirstOrDefault();
    }
}

public sealed partial class HistoryItemViewModel : ObservableObject
{
    private HistoryItemViewModel(HistoryEntry entry, bool hasAudio)
    {
        Id = entry.Id;
        AudioFileName = entry.AudioFileName;
        HasAudio = hasAudio;
        Timestamp = entry.TimestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Text = entry.Text;
        Transcription = entry.Transcription;

        var details = new[]
        {
            entry.ModelId,
            entry.PromptTitle,
            entry.DurationMs > 0 ? $"{entry.DurationMs} ms" : null,
        };

        Details = string.Join("  ·  ", details.Where(detail => !string.IsNullOrWhiteSpace(detail)));
    }

    public Guid Id { get; }

    public string? AudioFileName { get; }

    public bool HasAudio { get; }

    public string Timestamp { get; }

    public string Text { get; }

    public string Details { get; }

    public string? Transcription { get; }

    [ObservableProperty]
    private bool _isPlaying;

    public static HistoryItemViewModel Create(HistoryEntry entry, bool hasAudio)
    {
        return new HistoryItemViewModel(entry, hasAudio);
    }
}
