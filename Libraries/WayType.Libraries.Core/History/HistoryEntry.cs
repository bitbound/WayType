namespace WayType.Libraries.Core.History;

public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The text that was typed into the focused application.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The model output before post-processing, when post-processing ran.
    /// </summary>
    public string? Transcription { get; set; }

    public string? ModelId { get; set; }

    public string? PromptTitle { get; set; }

    public long DurationMs { get; set; }
}
