namespace WayType.Libraries.Core.Updates;

public interface IUpdateService
{
    UpdateInfo? AvailableUpdate { get; }

    event EventHandler<UpdateInfo>? UpdateAvailable;

    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the update to temp and hands off to it, which replaces this executable.
    /// </summary>
    Task ApplyAsync(CancellationToken cancellationToken = default);
}
