using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace YGOProSharp.Logging;

public static class AppLog
{
    private static readonly object SyncRoot = new();
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    public static void Configure(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        lock (SyncRoot)
            _loggerFactory = loggerFactory;
    }

    public static ILogger<T> CreateLogger<T>()
    {
        lock (SyncRoot)
            return _loggerFactory.CreateLogger<T>();
    }

    public static ILogger<T> For<T>()
    {
        return CreateLogger<T>();
    }

    public static ILogger CreateLogger(string categoryName)
    {
        lock (SyncRoot)
            return _loggerFactory.CreateLogger(categoryName);
    }

    public static LogLevel ParseLevel(string? value, LogLevel defaultLevel = LogLevel.Information)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultLevel;

        return Enum.TryParse(value, ignoreCase: true, out LogLevel level)
            ? level
            : defaultLevel;
    }
}
