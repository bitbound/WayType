namespace WayType.Libraries.Core.Settings;

public sealed class PostProcessingSettings
{
    public const int DefaultTimeoutSeconds = 10;

    public bool Enabled { get; set; }

    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string? ModelId { get; set; }

    /// <summary>
    /// How long to wait for the text model. A reasoning model can take far longer than a plain one,
    /// so this is a setting rather than a constant.
    /// </summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public Guid? SelectedPromptId { get; set; }

    public TextGenerationOptions Options { get; set; } = new();

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 3600));

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ModelId);
}
