using Microsoft.Extensions.Logging;

namespace YGOProSharp.Web.Logging;

public sealed record BufferedLogEntry(
    long Sequence,
    DateTime TimestampUtc,
    string Level,
    string Category,
    string Message);

/// <summary>
/// 线程安全的内存结构化日志环形缓冲区，供管理面板仪表盘与 WebSocket 实时日志流订阅。
/// </summary>
public sealed class LogRingBuffer
{
    private readonly object _gate = new();
    private readonly BufferedLogEntry[] _buffer;
    private readonly int _capacity;
    private int _writeIndex;
    private int _count;
    private long _sequence;

    public event Action<BufferedLogEntry>? OnEntryAdded;

    public LogRingBuffer(int capacity = 500)
    {
        _capacity = Math.Clamp(capacity, 50, 5000);
        _buffer = new BufferedLogEntry[_capacity];
    }

    public void Append(LogLevel level, string category, string message)
    {
        BufferedLogEntry entry;
        lock (_gate)
        {
            long seq = ++_sequence;
            entry = new BufferedLogEntry(
                seq,
                DateTime.UtcNow,
                level.ToString(),
                category,
                message);

            _buffer[_writeIndex] = entry;
            _writeIndex = (_writeIndex + 1) % _capacity;
            if (_count < _capacity)
                _count++;
        }

        OnEntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<BufferedLogEntry> GetRecent(int limit = 200, string? minLevel = null, string? search = null)
    {
        int clamped = Math.Clamp(limit, 1, _capacity);
        LogLevel? parsedMin = null;
        if (!string.IsNullOrWhiteSpace(minLevel) && Enum.TryParse(minLevel.Trim(), ignoreCase: true, out LogLevel lvl))
            parsedMin = lvl;

        string? keyword = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        List<BufferedLogEntry> snapshot = new(_count);
        lock (_gate)
        {
            int start = _count == _capacity ? _writeIndex : 0;
            for (int i = 0; i < _count; i++)
            {
                int idx = (start + i) % _capacity;
                snapshot.Add(_buffer[idx]);
            }
        }

        IEnumerable<BufferedLogEntry> query = snapshot;
        if (parsedMin.HasValue)
        {
            query = query.Where(e =>
                Enum.TryParse(e.Level, ignoreCase: true, out LogLevel itemLevel) && itemLevel >= parsedMin.Value);
        }

        if (keyword is not null)
        {
            query = query.Where(e =>
                e.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                e.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        return query.TakeLast(clamped).ToArray();
    }
}

/// <summary>
/// 接入 <see cref="ILoggerFactory"/> 的环形内存日志 Provider。
/// </summary>
public sealed class RingBufferLoggerProvider : ILoggerProvider
{
    private readonly LogRingBuffer _ringBuffer;

    public RingBufferLoggerProvider(LogRingBuffer ringBuffer)
    {
        _ringBuffer = ringBuffer ?? throw new ArgumentNullException(nameof(ringBuffer));
    }

    public ILogger CreateLogger(string categoryName) => new RingBufferLogger(categoryName, _ringBuffer);

    public void Dispose()
    {
    }

    private sealed class RingBufferLogger(string category, LogRingBuffer ringBuffer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None)
                return;

            string text = formatter(state, exception);
            if (exception is not null)
                text = $"{text} [{exception.GetType().Name}: {exception.Message}]";

            ringBuffer.Append(logLevel, category, text);
        }
    }
}
