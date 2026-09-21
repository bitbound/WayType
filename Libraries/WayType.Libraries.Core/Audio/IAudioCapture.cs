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
    /// <param name="timeout">
    /// Upper bound on the whole capture. The stop signal cannot interrupt a native read that the audio
    /// server never answers, so this is what keeps a wedged server from hanging the take forever.
    /// </param>
    Task<PcmAudio> RecordAsync(string? deviceId, CancellationToken endOfRecording, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public interface IAudioPlayer
{
    /// <summary>
    /// Plays a WAV clip on the default output and returns when it finishes or cancellation is requested.
    /// </summary>
    Task PlayAsync(byte[] wavBytes, CancellationToken cancellationToken = default);
}
