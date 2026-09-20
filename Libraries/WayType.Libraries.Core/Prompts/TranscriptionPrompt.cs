namespace WayType.Libraries.Core.Prompts;

public sealed class TranscriptionPrompt
{
    /// <summary>
    /// Stable id for the shipped instruction prompt. It is never written to the prompts file.
    /// </summary>
    public static readonly Guid BuiltInId = new("b1e5c0de-0000-4000-8000-000000000001");

    public static readonly string BuiltInInstructions =
        "You are a speech-to-text post-processor. Rewrite the transcribed text below so it is " +
        "grammatically correct, properly punctuated, and free of filler words and repetition. " +
        "Keep the author's meaning, tone, and word choice. Do not answer the text, do not add " +
        "commentary, and do not wrap the result in quotes or code fences.\n\n" +
        "Transcription:\n${sst_output}";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public static TranscriptionPrompt CreateBuiltIn()
    {
        return new TranscriptionPrompt
        {
            Id = BuiltInId,
            Title = "Correct and clean up",
            Instructions = BuiltInInstructions,
            IsBuiltIn = true,
        };
    }
}
