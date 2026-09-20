namespace WayType.Libraries.Core.Settings;

/// <summary>
/// Request overrides for the post-processing model. A null value is left out of the request body.
/// </summary>
public sealed class TextGenerationOptions
{
    public bool? ThinkingEnabled { get; set; }

    public string? ReasoningEffort { get; set; }

    public double? Temperature { get; set; }

    public double? TopP { get; set; }

    public int? MaxCompletionTokens { get; set; }

    public int? MaxTokens { get; set; }

    public double? FrequencyPenalty { get; set; }

    public double? PresencePenalty { get; set; }

    public double? RepetitionPenalty { get; set; }

    public int? Seed { get; set; }

    public int? N { get; set; }

    public string? Stop { get; set; }

    public string? ResponseFormat { get; set; }

    /// <summary>
    /// Raw JSON object merged into the request body last, for vendor-specific parameters.
    /// </summary>
    public string? AdvancedOverridesJson { get; set; }
}
