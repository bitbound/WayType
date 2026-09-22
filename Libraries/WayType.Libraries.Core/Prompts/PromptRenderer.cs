namespace WayType.Libraries.Core.Prompts;

public static class PromptRenderer
{
    public const string OutputPlaceholder = "${stt_output}";

    /// <summary>
    /// Injects the transcription at the placeholder. Prompts without the placeholder get the
    /// transcription appended so a user-authored prompt cannot silently discard the audio result.
    /// </summary>
    public static string Render(string instructions, string transcription)
    {
        if (string.IsNullOrEmpty(instructions))
        {
            return transcription;
        }

        if (instructions.Contains(OutputPlaceholder, StringComparison.Ordinal))
        {
            return instructions.Replace(OutputPlaceholder, transcription, StringComparison.Ordinal);
        }

        return $"{instructions}\n\nTranscription:\n{transcription}";
    }
}
