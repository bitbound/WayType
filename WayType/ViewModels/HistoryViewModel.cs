using System.Collections.ObjectModel;
using Avalonia.Threading;
using WayType.Libraries.Core.History;
using WayType.Views;

namespace WayType.ViewModels;

public sealed partial class HistoryViewModel : ViewModelBase<HistoryView>
{
    private readonly IHistoryService _history;

    [ObservableProperty]
    private ObservableCollection<HistoryItemViewModel> _entries = [];

    [ObservableProperty]
    private HistoryItemViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _hasEntries;

    [ObservableProperty]
    private bool _canDelete;

    public HistoryViewModel(IHistoryService history)
    {
        _history = history;

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

    private void Refresh()
    {
        var selectedId = SelectedEntry?.Id;
        var items = _history.GetAll().Select(HistoryItemViewModel.Create);

        Entries = new ObservableCollection<HistoryItemViewModel>(items);
        HasEntries = Entries.Count > 0;

        SelectedEntry = Entries.FirstOrDefault(item => item.Id == selectedId) ?? Entries.FirstOrDefault();
    }
}

public sealed record HistoryItemViewModel(
    Guid Id,
    string Timestamp,
    string Text,
    string Details,
    string? Transcription)
{
    public static HistoryItemViewModel Create(HistoryEntry entry)
    {
        var details = new[]
        {
            entry.ModelId,
            entry.PromptTitle,
            entry.DurationMs > 0 ? $"{entry.DurationMs} ms" : null,
        };

        return new HistoryItemViewModel(
            entry.Id,
            entry.TimestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            entry.Text,
            string.Join("  ·  ", details.Where(detail => !string.IsNullOrWhiteSpace(detail))),
            entry.Transcription);
    }
}
