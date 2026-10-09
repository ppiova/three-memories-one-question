using Microsoft.Extensions.Logging;

namespace ThreeMemories.Memories;

/// <summary>
/// Sends the library's warnings to a file and counts what it skipped. AgentMemory does not throw
/// when it cannot write or recall something: it logs a warning and carries on, so the log is where
/// a lost memory shows up.
/// </summary>
internal sealed class FileLog(string path) : ILoggerProvider
{
    private readonly StreamWriter _file = new(path, append: false) { AutoFlush = true };
    private int _skipped;

    public string Path { get; } = path;

    /// <summary>Entities, facts or relationships the library extracted and then did not store.</summary>
    public int Skipped => _skipped;

    public ILogger CreateLogger(string categoryName) => new Category(this, categoryName);

    public void Dispose() => _file.Dispose();

    private sealed class Category(FileLog log, string name) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (message.StartsWith("Skipping", StringComparison.Ordinal)) Interlocked.Increment(ref log._skipped);

            var line = $"{logLevel} {name}: {message}";
            lock (log._file)
            {
                log._file.WriteLine(exception is null ? line : $"{line} | {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
