using Microsoft.Extensions.Logging;

namespace WayType.Services;

/// <summary>
/// Holds the live minimum level so the settings screen can turn verbose logging on and off without
/// a restart. Every filter reads the current value on each write.
/// </summary>
public sealed class LogLevelSwitch(LogLevel initial)
{
    private int _level = (int)initial;

    public LogLevel Current => (LogLevel)Volatile.Read(ref _level);

    public bool IsEnabled(LogLevel level)
    {
        return level != LogLevel.None && level >= Current;
    }

    public void Set(LogLevel level)
    {
        Volatile.Write(ref _level, (int)level);
    }
}

/// <summary>
/// Appends log lines to a file under the config directory. The app ships as a windowed executable
/// with no console attached, so stdout goes nowhere and a file is the only way to see diagnostics
/// after the fact.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 1024 * 1024;

    private readonly string _directory;
    private readonly LogLevelSwitch _levelSwitch;
    private readonly Lock _writeLock = new();

    public FileLoggerProvider(string configDirectory, LogLevelSwitch levelSwitch)
    {
        _directory = Path.Combine(configDirectory, "logs");
        _levelSwitch = levelSwitch;
    }

    public string LogFilePath => Path.Combine(_directory, "waytype.log");

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private void Write(string categoryName, LogLevel level, string message, Exception? exception)
    {
        if (!_levelSwitch.IsEnabled(level))
        {
            return;
        }

        var line = FormattableString.Invariant(
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Short(categoryName)} {message}");

        lock (_writeLock)
        {
            try
            {
                Directory.CreateDirectory(_directory);

                RollIfNeeded();

                File.AppendAllText(LogFilePath, line + Environment.NewLine + (exception is null ? string.Empty : exception + Environment.NewLine));
            }
            catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
            {
                // Logging must never be the reason a dictation fails.
            }
        }
    }

    private void RollIfNeeded()
    {
        var info = new FileInfo(LogFilePath);

        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        var previous = LogFilePath + ".1";

        File.Delete(previous);
        File.Move(LogFilePath, previous);
    }

    private static string Short(string categoryName)
    {
        var lastDot = categoryName.LastIndexOf('.');

        return lastDot < 0 ? categoryName : categoryName[(lastDot + 1)..];
    }

    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return provider._levelSwitch.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
