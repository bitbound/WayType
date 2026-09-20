namespace WayType.Libraries.Core.Speech;

public interface ITextGenerationClient
{
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default);
}
