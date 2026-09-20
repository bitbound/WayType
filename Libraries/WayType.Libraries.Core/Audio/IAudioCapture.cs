namespace WayType.Libraries.Core.Audio;

public interface IAudioCaptureDeviceEnumerator
{
    Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
}

public interface IAudioRecorder
{
    /// <summary>
    /// Captures from the given device until endOfRecording is signalled, then returns the captured audio.
    /// A null deviceId uses the system default.
    /// </summary>
    Task<PcmAudio> RecordAsync(string? deviceId, CancellationToken endOfRecording, CancellationToken cancellationToken = default);
}
