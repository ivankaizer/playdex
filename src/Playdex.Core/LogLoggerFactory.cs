using Microsoft.Extensions.Logging;

namespace Playdex.Core;

/// <summary>
/// <c>ILogger</c>s that write to <see cref="Log"/>, for the classes ported from the Wrapper (they log through
/// <c>ILogger</c> and <c>LoggerMessage</c>). Information and above; a line reads "warn LauncherManager: message".
/// </summary>
public sealed class LogLoggerFactory : ILoggerFactory
{
    public static LogLoggerFactory Instance { get; } = new();

    public ILogger CreateLogger(string categoryName) => new LogLogger(categoryName[(categoryName.LastIndexOf('.') + 1)..]);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class LogLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var level = logLevel switch
            {
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "error",
                _ => "critical",
            };
            Core.Log.Info($"{level} {category}: {formatter(state, exception)}{(exception is null ? "" : $": {exception}")}");
        }
    }
}
