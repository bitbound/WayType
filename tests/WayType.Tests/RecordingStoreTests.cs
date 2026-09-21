using WayType.Libraries.Core.Audio;
using WayType.Libraries.Core.History;

namespace WayType.Tests;

public class RecordingStoreTests
{
    private readonly InMemoryFileStore _fileStore = new();
    private readonly TestPlatformPaths _paths = new();
    private readonly RecordingStore _recordings;

    public RecordingStoreTests()
    {
        _recordings = new RecordingStore(_paths, _fileStore);
    }

    [Fact]
    public async Task SaveAsync_WritesTheWavUnderTheAudioDirectoryWithOwnerOnlyPermissions()
    {
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var fileName = await _recordings.SaveAsync(id, [1, 2, 3], ct);
        var path = Path.Combine(_paths.AudioDirectory, fileName);

        Assert.Equal($"{id:N}.wav", fileName);
        Assert.Equal([1, 2, 3], _fileStore.ReadAllBytes(path));
        Assert.Contains(path, _fileStore.RestrictedPaths);
    }

    [Fact]
    public async Task SaveAsyncThenRead_RoundTripsBinaryAudio()
    {
        var wav = PcmProcessor.ToWav(new PcmAudio([0.5f, -0.5f, 1f, -1f], 16_000, 1));

        var fileName = await _recordings.SaveAsync(Guid.NewGuid(), wav, TestContext.Current.CancellationToken);

        Assert.Equal(wav, _recordings.Read(fileName));
    }

    [Fact]
    public void Exists_WithAnUnknownName_IsFalse()
    {
        Assert.False(_recordings.Exists("missing.wav"));
        Assert.False(_recordings.Exists(null));
        Assert.False(_recordings.Exists("  "));
    }

    [Fact]
    public void Read_WithAPathOutsideTheAudioDirectory_ReturnsNull()
    {
        // history.json is editable by hand, so a name that escapes the audio directory is ignored.
        Assert.Null(_recordings.Read("../../history.json"));
        Assert.False(_recordings.Exists("/etc/passwd"));
    }

    [Fact]
    public async Task Delete_WithAPathOutsideTheAudioDirectory_LeavesTheTargetAlone()
    {
        var ct = TestContext.Current.CancellationToken;

        await _fileStore.WriteAllTextAsync(Path.Combine(_paths.DataDirectory, "history.json"), "[]", ct);

        _recordings.Delete("../history.json");

        Assert.True(_fileStore.FileExists(Path.Combine(_paths.DataDirectory, "history.json")));
    }

    [Fact]
    public async Task Delete_RemovesTheSavedFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var fileName = await _recordings.SaveAsync(Guid.NewGuid(), [1, 2, 3], ct);

        _recordings.Delete(fileName);

        Assert.False(_recordings.Exists(fileName));
        Assert.Null(_recordings.Read(fileName));
    }

    [Fact]
    public void Delete_WithANullName_DoesNothing()
    {
        _recordings.Delete(null);
    }
}

public class WavFileTests
{
    [Fact]
    public void TryRead_RoundTripsAWavWrittenByPcmProcessor()
    {
        var wav = PcmProcessor.ToWav(new PcmAudio([0.5f, -0.5f, 1f], 16_000, 1));

        var clip = WavFile.TryRead(wav);

        Assert.NotNull(clip);
        Assert.Equal(16_000, clip.SampleRate);
        Assert.Equal(1, clip.ChannelCount);
        Assert.Equal(16, clip.BitsPerSample);
        Assert.Equal(6, clip.PcmBytes.Length);
    }

    [Fact]
    public void TryRead_WithAnEmptyOrMissingBuffer_ReturnsNull()
    {
        Assert.Null(WavFile.TryRead(null));
        Assert.Null(WavFile.TryRead([]));
    }

    [Fact]
    public void TryRead_WithSomethingOtherThanWav_ReturnsNull()
    {
        Assert.Null(WavFile.TryRead([.. "RIFFxxxxNOPE"u8, .. new byte[32]]));
    }

    [Fact]
    public void TryRead_WithoutADataChunk_ReturnsNull()
    {
        // A header-only file has nothing to play.
        Assert.Null(WavFile.TryRead(PcmProcessor.ToWav(PcmAudio.Empty)));
    }
}
