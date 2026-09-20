namespace WayType.Libraries.Core.Dictation;

public interface IDictationCoordinator
{
    DictationState State { get; }

    string? LastError { get; }

    bool IsListening { get; }

    event EventHandler? StateChanged;

    Task ToggleAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
