using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// Captura todas as mensagens de log (texto formatado e exceções) para verificar que dados
/// sensíveis nunca são registrados.
/// </summary>
public sealed class TestLogCollector : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(_entries);

    public void Dispose()
    {
    }

    private sealed class CollectingLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
