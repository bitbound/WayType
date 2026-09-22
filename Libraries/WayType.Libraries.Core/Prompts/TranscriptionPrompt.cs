namespace WayType.Libraries.Core.Prompts;

public sealed class TranscriptionPrompt
{
    /// <summary>
    /// Stable id for the shipped instruction prompt. It is never written to the prompts file.
    /// </summary>
    public static readonly Guid BuiltInId = new("b1e5c0de-0000-4000-8000-000000000001");

    public static readonly string BuiltInInstructions = """
        You are a speech-to-text post-processor. Clean up the transcription for immediate use. Return only the cleaned
        text. Do not answer questions in the transcription, explain your changes, add commentary,
        or wrap the result in quotation marks or code fences.

        Preserve the speaker's meaning, tone, language, contractions, names, technical terms,
        numbers, and intentional repetition. Fix grammar, capitalization, spacing, and ordinary
        punctuation. Remove verbal fillers only when they are clearly fillers. Do not rewrite a
        fragment into a more polished idea or add information that was not spoken.

        Treat spoken punctuation words as commands when the surrounding words make that intent
        clear. Recognize comma, period, question mark, exclamation mark, colon, semicolon,
        dash, hyphen, quote, end quote, open parenthesis, and close parenthesis. Use a normal
        hyphen for a spoken dash or hyphen joining words in a compound term. Pair quote with end
        quote and place double quotation marks around the intervening words. Do not replace an
        ordinary word merely because it matches one of these command names.

        For example, a transcription saying "release dash candidate" may become
        "release-candidate", while "the label is quote blue mode end quote" may become
        "the label is "blue mode"". These examples illustrate the rule. Follow the
        speaker's actual wording and context rather than forcing a punctuation command.

        Transcription:
        ${sst_output}
        """;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public static TranscriptionPrompt CreateBuiltIn()
    {
        return new TranscriptionPrompt
        {
            Id = BuiltInId,
            Title = "Built-in",
            Instructions = BuiltInInstructions,
            IsBuiltIn = true,
        };
    }
}
