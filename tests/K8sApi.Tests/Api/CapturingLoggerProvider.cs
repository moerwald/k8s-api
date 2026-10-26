using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace K8sApi.Tests.Api;

/// <summary>
/// Logger provider for endpoint tests. It keeps every log line of the whole app,
/// with its log scopes (ASP.NET puts the TraceId into a scope).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    public sealed record Entry(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyList<string> Scopes)
    {
        /// <summary>Message, exception and all scopes as one text, to search in.</summary>
        public string AllText => string.Join(" | ", new[] { Message, Exception?.ToString() ?? "" }.Concat(Scopes));
    }

    private readonly ConcurrentQueue<Entry> _entries = new();
    private IExternalScopeProvider? _scopes;

    public IReadOnlyList<Entry> Entries => _entries.ToList();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            owner._scopes?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            owner._scopes?.ForEachScope((scope, list) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    list.AddRange(pairs.Select(p => $"{p.Key}:{p.Value}"));
                else if (scope is not null)
                    list.Add(scope.ToString() ?? "");
            }, scopes);

            owner._entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception, scopes));
        }
    }
}
