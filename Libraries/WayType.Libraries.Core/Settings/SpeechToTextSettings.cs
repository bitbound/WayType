namespace WayType.Libraries.Core.Settings;

public sealed class SpeechToTextSettings
{
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string? ModelId { get; set; }

    public string? Language { get; set; }

    /// <summary>
    /// False for single-model servers (e.g. parakeet.cpp) that serve one transcription model chosen
    /// at launch and expose no /v1/models listing. No model is sent or required in that case.
    /// </summary>
    public bool ModelSelectionEnabled { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && (!ModelSelectionEnabled || !string.IsNullOrWhiteSpace(ModelId));
}
