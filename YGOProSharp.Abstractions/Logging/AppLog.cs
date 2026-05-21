using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace YGOProSharp.Abstractions.Logging;

/// <summary>
/// 进程级日志入口（process-wide logging entry point），由 CLI 或测试配置 logger factory 后供库代码使用。
/// 默认 factory 是 null logger，因此核心代码可直接请求 logger，而不强制要求 composition root。
/// </summary>
public static class AppLog
{
    private static readonly object SyncRoot = new();
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    /// <summary>
    /// 替换全局 logger factory。CLI 启动和测试会在运行核心代码前调用一次。
    /// </summary>
    public static void Configure(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        lock (SyncRoot)
            _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 从已配置的全局 factory 创建类型 category logger。
    /// </summary>
    public static ILogger<T> CreateLogger<T>()
    {
        lock (SyncRoot)
            return _loggerFactory.CreateLogger<T>();
    }

    /// <summary>
    /// <see cref="CreateLogger{T}"/> 的别名（alias），用于读起来更像“为某类型创建 logger”的调用点。
    /// </summary>
    public static ILogger<T> For<T>()
    {
        return CreateLogger<T>();
    }

    /// <summary>
    /// 为 CLI、server entrypoint 等非类型 category 创建命名 logger。
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        lock (SyncRoot)
            return _loggerFactory.CreateLogger(categoryName);
    }

    /// <summary>
    /// 解析命令行 log level，并让非法或缺失值保持非致命（non-fatal）。
    /// </summary>
    public static LogLevel ParseLevel(string? value, LogLevel defaultLevel = LogLevel.Information)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultLevel;

        return Enum.TryParse(value, ignoreCase: true, out LogLevel level)
            ? level
            : defaultLevel;
    }
}
