namespace Wrapper;

internal sealed class AppLoggerFactory : IDisposable
{
    private readonly DailyFileLoggerProvider _provider;
    private readonly ILoggerFactory _factory;

    private AppLoggerFactory(DailyFileLoggerProvider provider)
    {
        _provider = provider;
        _factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(provider);
        });
    }

    public string FilePath => _provider.FilePath;

    public static AppLoggerFactory Create() => CreateInDirectory(AppBrand.DefaultLogDirectory);

    public static AppLoggerFactory CreateInDirectory(string directory) =>
        new(DailyFileLoggerProvider.Create(directory));

    public void UseDirectory(string directory) => _provider.UseDirectory(directory);

    public ILogger<T> CreateLogger<T>() => _factory.CreateLogger<T>();

    public ILogger CreateLogger(string categoryName) => _factory.CreateLogger(categoryName);

    public void Dispose() => _factory.Dispose();
}

internal sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private string _filePath;

    private DailyFileLoggerProvider(string filePath)
    {
        _filePath = filePath;
    }

    public string FilePath
    {
        get
        {
            lock (_sync)
            {
                return _filePath;
            }
        }
    }

    public static DailyFileLoggerProvider Create(string directory)
        => new(ResolveFilePath(directory));

    public void UseDirectory(string directory)
    {
        var filePath = ResolveFilePath(directory);

        lock (_sync)
        {
            _filePath = filePath;
        }
    }

    public ILogger CreateLogger(string categoryName) => new ProviderLogger(categoryName, Write);

    public void Dispose()
    {
    }

    private static string ResolveFilePath(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return GetDatedPath(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var fallbackDirectory = GetFallbackDirectory();
            Directory.CreateDirectory(fallbackDirectory);
            return GetDatedPath(fallbackDirectory);
        }
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        lock (_sync)
        {
            var exceptionSuffix = exception is null ? "" : $": {exception}";
            var line = $"{DateTimeOffset.UtcNow:O} [{level.ToString().ToLowerInvariant()}] {category}: {message}{exceptionSuffix}{Environment.NewLine}";
            try
            {
                Append(_filePath, line);
            }
            catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
            {
                var fallback = GetFallbackDirectory();
                Directory.CreateDirectory(fallback);
                _filePath = GetDatedPath(fallback);
                Append(_filePath, line);
            }
        }
    }

    private static string GetDatedPath(string directory) =>
        Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMdd}.log");

    private static string GetFallbackDirectory() =>
        Path.Combine(Path.GetTempPath(), AppBrand.StorageDirectoryName, "logs");

    private static void Append(string path, string line)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(line);
    }

    private sealed class ProviderLogger(
        string category,
        Action<string, LogLevel, string, Exception?> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                write(category, logLevel, formatter(state, exception), exception);
            }
        }
    }
}
