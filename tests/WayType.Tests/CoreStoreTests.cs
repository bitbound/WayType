using WayType.Libraries.Core.History;
using WayType.Libraries.Core.Prompts;
using WayType.Libraries.Core.Settings;

namespace WayType.Tests;

public class SettingsServiceTests
{
    [Fact]
    public async Task SaveAsyncThenReload_RoundTripsAllSettings()
    {
        var fileStore = new InMemoryFileStore();
        var settings = TestSettings.Create(fileStore);
        var ct = TestContext.Current.CancellationToken;

        settings.Current.Theme = ThemeMode.Dark;
        settings.Current.Hotkey = "CTRL+ALT+F5";
        settings.Current.HistoryItemsToKeep = 7;
        settings.Current.SpeechToText.Endpoint = "http://localhost:8000/v1";
        settings.Current.SpeechToText.ApiKey = "secret-key";
        settings.Current.PostProcessing.Enabled = true;
        settings.Current.PostProcessing.Options.Temperature = 0.2;
        await settings.SaveAsync(ct);

        var reloaded = TestSettings.Create(fileStore);

        Assert.Equal(ThemeMode.Dark, reloaded.Current.Theme);
        Assert.Equal("CTRL+ALT+F5", reloaded.Current.Hotkey);
        Assert.Equal(7, reloaded.Current.HistoryItemsToKeep);
        Assert.Equal("http://localhost:8000/v1", reloaded.Current.SpeechToText.Endpoint);
        Assert.Equal("secret-key", reloaded.Current.SpeechToText.ApiKey);
        Assert.True(reloaded.Current.PostProcessing.Enabled);
        Assert.Equal(0.2, reloaded.Current.PostProcessing.Options.Temperature);
    }

    [Fact]
    public async Task SaveAsync_WritesSettingsFileWithOwnerOnlyPermissions()
    {
        var fileStore = new InMemoryFileStore();
        var settings = TestSettings.Create(fileStore);
        var paths = new TestPlatformPaths();

        await settings.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Contains(paths.SettingsFilePath, fileStore.RestrictedPaths);
    }

    [Fact]
    public async Task Reload_WithCorruptSettingsFile_FallsBackToDefaults()
    {
        var fileStore = new InMemoryFileStore();
        var paths = new TestPlatformPaths();

        await fileStore.WriteAllTextAsync(paths.SettingsFilePath, "{ not json", TestContext.Current.CancellationToken);

        var settings = TestSettings.Create(fileStore);

        Assert.Equal(ThemeMode.System, settings.Current.Theme);
        Assert.Equal(AppSettings.DefaultHotkey, settings.Current.Hotkey);
    }
}

public class HistoryServiceTests
{
    [Fact]
    public async Task AddAsync_TrimmsToConfiguredHistoryItemsToKeepKeepingNewest()
    {
        var settings = TestSettings.Create(new InMemoryFileStore(), new AppSettings { HistoryItemsToKeep = 2 });
        var history = new HistoryService(new TestPlatformPaths(), new InMemoryFileStore(), settings);
        var ct = TestContext.Current.CancellationToken;

        for (var index = 0; index < 5; index++)
        {
            await history.AddAsync(new HistoryEntry
            {
                Text = $"entry {index}",
                TimestampUtc = DateTimeOffset.UnixEpoch.AddMinutes(index),
            }, ct);
        }

        var entries = history.GetAll();

        Assert.Equal(2, entries.Count);
        Assert.Equal("entry 4", entries[0].Text);
        Assert.Equal("entry 3", entries[1].Text);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheSelectedEntry()
    {
        var fileStore = new InMemoryFileStore();
        var history = new HistoryService(new TestPlatformPaths(), fileStore, TestSettings.Create(fileStore));
        var keep = new HistoryEntry { Text = "keep" };
        var drop = new HistoryEntry { Text = "drop" };
        var ct = TestContext.Current.CancellationToken;

        await history.AddAsync(keep, ct);
        await history.AddAsync(drop, ct);
        var deleted = await history.DeleteAsync(drop.Id, ct);

        Assert.True(deleted);
        Assert.Single(history.GetAll());
        Assert.Equal("keep", history.GetAll()[0].Text);
    }

    [Fact]
    public async Task DeleteAsync_WithAnUnknownId_ReturnsFalse()
    {
        var fileStore = new InMemoryFileStore();
        var history = new HistoryService(new TestPlatformPaths(), fileStore, TestSettings.Create(fileStore));
        var ct = TestContext.Current.CancellationToken;

        await history.AddAsync(new HistoryEntry { Text = "one" }, ct);

        Assert.False(await history.DeleteAsync(Guid.NewGuid(), ct));
        Assert.Single(history.GetAll());
    }

    [Fact]
    public async Task ClearAsync_LeavesNoEntriesAndPersists()
    {
        var fileStore = new InMemoryFileStore();
        var paths = new TestPlatformPaths();
        var history = new HistoryService(paths, fileStore, TestSettings.Create(fileStore));
        var ct = TestContext.Current.CancellationToken;

        await history.AddAsync(new HistoryEntry { Text = "one" }, ct);
        await history.ClearAsync(ct);

        Assert.Empty(history.GetAll());

        var reloaded = new HistoryService(paths, fileStore, TestSettings.Create(fileStore));

        Assert.Empty(reloaded.GetAll());
    }
}

public class PromptServiceTests
{
    [Fact]
    public void GetAll_AlwaysStartsWithTheBuiltInPrompt()
    {
        var prompts = new PromptService(new TestPlatformPaths(), new InMemoryFileStore(), TestSettings.Create(new InMemoryFileStore()));

        var all = prompts.GetAll();

        Assert.Equal(TranscriptionPrompt.BuiltInId, all[0].Id);
        Assert.True(all[0].IsBuiltIn);
        Assert.Contains(PromptRenderer.OutputPlaceholder, all[0].Instructions);
    }

    [Fact]
    public void GetSelected_WithNoSelection_FallsBackToTheBuiltInPrompt()
    {
        var fileStore = new InMemoryFileStore();
        var settings = TestSettings.Create(fileStore);
        var prompts = new PromptService(new TestPlatformPaths(), fileStore, settings);

        Assert.Equal(TranscriptionPrompt.BuiltInId, prompts.GetSelected().Id);
    }

    [Fact]
    public async Task DeleteAsync_ForBuiltInPrompt_KeepsItAndReturnsFalse()
    {
        var prompts = new PromptService(new TestPlatformPaths(), new InMemoryFileStore(), TestSettings.Create(new InMemoryFileStore()));

        var deleted = await prompts.DeleteAsync(TranscriptionPrompt.BuiltInId, TestContext.Current.CancellationToken);

        Assert.False(deleted);
        Assert.NotEmpty(prompts.GetAll());
    }

    [Fact]
    public async Task UpdateAsync_RoundTripsThroughStorage()
    {
        var fileStore = new InMemoryFileStore();
        var prompts = new PromptService(new TestPlatformPaths(), fileStore, TestSettings.Create(fileStore));
        var ct = TestContext.Current.CancellationToken;

        var created = await prompts.CreateAsync("My prompt", ct);
        created.Instructions = "Fix this: ${sst_output}";
        await prompts.UpdateAsync(created, ct);

        var reloaded = new PromptService(new TestPlatformPaths(), fileStore, TestSettings.Create(fileStore));
        var stored = reloaded.Get(created.Id);

        Assert.NotNull(stored);
        Assert.Equal("My prompt", stored.Title);
        Assert.Equal("Fix this: ${sst_output}", stored.Instructions);
        Assert.DoesNotContain(reloaded.GetAll(), prompt => prompt.IsBuiltIn && prompt.Id != TranscriptionPrompt.BuiltInId);
    }

    [Fact]
    public async Task UpdateAsync_ForBuiltInPrompt_Throws()
    {
        var prompts = new PromptService(new TestPlatformPaths(), new InMemoryFileStore(), TestSettings.Create(new InMemoryFileStore()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => prompts.UpdateAsync(TranscriptionPrompt.CreateBuiltIn(), TestContext.Current.CancellationToken));
    }
}

public class PromptRendererTests
{
    [Fact]
    public void Render_ReplacesPlaceholderWithTranscription()
    {
        var rendered = PromptRenderer.Render("Clean up: ${sst_output}", "hello  there");

        Assert.Equal("Clean up: hello  there", rendered);
    }

    [Fact]
    public void Render_WithoutPlaceholder_AppendsTranscription()
    {
        var rendered = PromptRenderer.Render("Fix the grammar.", "raw text");

        Assert.Contains("raw text", rendered);
        Assert.StartsWith("Fix the grammar.", rendered);
    }
}
