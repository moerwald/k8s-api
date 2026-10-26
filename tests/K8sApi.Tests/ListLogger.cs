using Microsoft.Extensions.Logging;

namespace K8sApi.Tests;

/// <summary>Logger for tests. It keeps every log line in a list.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message);

    public List<Entry> Entries { get; } = new();

    public IEnumerable<Entry> Warnings => Entries.Where(e => e.Level == LogLevel.Warning);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception)));
}
