namespace WayType.Libraries.Core.Settings;

public sealed class PostProcessingSettings
{
    public bool Enabled { get; set; }

    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string? ModelId { get; set; }

    public Guid? SelectedPromptId { get; set; }

    public TextGenerationOptions Options { get; set; } = new();

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ModelId);
}
