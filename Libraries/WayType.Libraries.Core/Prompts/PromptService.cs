using System.Text.Json;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Serialization;
using WayType.Libraries.Core.Settings;

namespace WayType.Libraries.Core.Prompts;

public interface IPromptService
{
    TranscriptionPrompt BuiltIn { get; }

    IReadOnlyList<TranscriptionPrompt> GetAll();

    TranscriptionPrompt? Get(Guid id);

    /// <summary>
    /// The prompt selected in settings, falling back to the built-in prompt.
    /// </summary>
    TranscriptionPrompt GetSelected();

    Task<TranscriptionPrompt> CreateAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies an existing prompt under a "- Copy" title. Returns null when the source is unknown.
    /// </summary>
    Task<TranscriptionPrompt?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpdateAsync(TranscriptionPrompt prompt, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class PromptService : IPromptService
{
    private const string NewPromptTitle = "New prompt";
    private const string CopySuffix = " - Copy";

    private readonly IFileStore _fileStore;
    private readonly IPlatformPaths _paths;
    private readonly ISettingsService _settings;

    public PromptService(IPlatformPaths paths, IFileStore fileStore, ISettingsService settings)
    {
        _paths = paths;
        _fileStore = fileStore;
        _settings = settings;
    }

    public TranscriptionPrompt BuiltIn => TranscriptionPrompt.CreateBuiltIn();

    public IReadOnlyList<TranscriptionPrompt> GetAll()
    {
        return [BuiltIn, .. ReadUserPrompts()];
    }

    public TranscriptionPrompt? Get(Guid id)
    {
        return GetAll().FirstOrDefault(prompt => prompt.Id == id);
    }

    public TranscriptionPrompt GetSelected()
    {
        var selectedId = _settings.Current.PostProcessing.SelectedPromptId;

        if (selectedId is null)
        {
            return BuiltIn;
        }

        return Get(selectedId.Value) ?? BuiltIn;
    }

    public async Task<TranscriptionPrompt> CreateAsync(string title, CancellationToken cancellationToken = default)
    {
        var prompt = new TranscriptionPrompt
        {
            Title = string.IsNullOrWhiteSpace(title) ? NewPromptTitle : title.Trim(),
            Instructions = "Instructions for the text model.\n\nTranscription:\n${sst_output}",
        };

        var prompts = ReadUserPrompts();
        prompts.Add(prompt);

        await WriteAsync(prompts, cancellationToken);

        return prompt;
    }

    public async Task<TranscriptionPrompt?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // The built-in prompt is never stored, but it is the one most people start from, so copying
        // it has to work too.
        if (Get(id) is not { } source)
        {
            return null;
        }

        var copy = new TranscriptionPrompt
        {
            Title = $"{source.Title}{CopySuffix}",
            Instructions = source.Instructions,
        };

        var prompts = ReadUserPrompts();
        prompts.Add(copy);

        await WriteAsync(prompts, cancellationToken);

        return copy;
    }

    public async Task UpdateAsync(TranscriptionPrompt prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (prompt.IsBuiltIn)
        {
            throw new InvalidOperationException("The built-in prompt cannot be modified.");
        }

        var prompts = ReadUserPrompts();
        var index = prompts.FindIndex(stored => stored.Id == prompt.Id);

        if (index < 0)
        {
            throw new InvalidOperationException($"Prompt {prompt.Id} does not exist.");
        }

        prompts[index] = prompt;

        await WriteAsync(prompts, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == TranscriptionPrompt.BuiltInId)
        {
            return false;
        }

        var prompts = ReadUserPrompts();
        var removed = prompts.RemoveAll(stored => stored.Id == id) > 0;

        if (!removed)
        {
            return false;
        }

        if (_settings.Current.PostProcessing.SelectedPromptId == id)
        {
            _settings.Current.PostProcessing.SelectedPromptId = null;
            await _settings.SaveAsync(cancellationToken);
        }

        await WriteAsync(prompts, cancellationToken);

        return true;
    }

    private List<TranscriptionPrompt> ReadUserPrompts()
    {
        var contents = _fileStore.ReadAllTextOrNull(_paths.PromptsFilePath);

        if (string.IsNullOrWhiteSpace(contents))
        {
            return [];
        }

        try
        {
            var prompts = JsonSerializer.Deserialize<List<TranscriptionPrompt>>(contents, WayTypeJson.Options) ?? [];

            return [.. prompts.Where(prompt => !prompt.IsBuiltIn)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAsync(List<TranscriptionPrompt> prompts, CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();

        var contents = JsonSerializer.Serialize(prompts, WayTypeJson.Options);
        await _fileStore.WriteAllTextAsync(_paths.PromptsFilePath, contents, cancellationToken);
        _fileStore.RestrictToOwner(_paths.PromptsFilePath);
    }
}
