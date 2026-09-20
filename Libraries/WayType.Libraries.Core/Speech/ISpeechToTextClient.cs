namespace WayType.Libraries.Core.Speech;

public interface ISpeechToTextClient
{
    Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default);
}
