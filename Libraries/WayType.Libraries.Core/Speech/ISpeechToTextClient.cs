namespace WayType.Libraries.Core.Speech;

public interface ISpeechToTextClient
{
    Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default);

    // Endpoint and key are arguments rather than settings lookups so the caller can list models from a
    // value the user typed but has not saved yet.
    Task<IReadOnlyList<AiModel>> ListModelsAsync(string? endpoint, string? apiKey, CancellationToken cancellationToken = default);
}
