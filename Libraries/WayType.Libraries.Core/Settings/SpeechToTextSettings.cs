namespace WayType.Libraries.Core.Settings;

public sealed class SpeechToTextSettings
{
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string? ModelId { get; set; }

    public string? Language { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ModelId);
}
