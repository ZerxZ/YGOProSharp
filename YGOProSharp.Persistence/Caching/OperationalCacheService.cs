using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence.Caching;

public sealed record CacheStatsSnapshot(
    bool RedisConfigured,
    bool RedisConnected,
    long L1Hits,
    long L2RedisHits,
    long Misses,
    double HitRatioPercent,
    double AverageLatencyMs,
    int ActiveTickets,
    long Generation,
    IReadOnlyDictionary<string, long> NamespaceGenerations);

public sealed record RateLimitDecision(
    bool Allowed,
    int Limit,
    int Remaining,
    int RetryAfterSeconds);

/// <summary>
/// 基于 .NET <see cref="HybridCache"/>（L1 内存 + 可选 L2 Redis + Pub/Sub 跨节点失效广播）的多级缓存、一次性原子票据与滑动窗口限流门面。
/// 当 <c>RedisUrl</c> 未配置或不可达时，100% 自动退化为单机 L1 内存模式。
/// </summary>
public sealed class OperationalCacheService : IAsyncDisposable, IDisposable
{
    private const string PubSubInvalidationChannel = "ygp:pubsub:invalidate";

    private readonly ILogger<OperationalCacheService> _logger = AppLog.CreateLogger<OperationalCacheService>();
    private readonly string _prefix;
    private readonly ServiceProvider _serviceProvider;
    private readonly HybridCache _hybridCache;
    private readonly IConnectionMultiplexer? _redis;
    private readonly bool _redisConfigured;
    private readonly string _nodeId = Guid.NewGuid().ToString("N")[..12];

    private long _l1Hits;
    private long _l2Hits;
    private long _misses;
    private long _totalRequests;
    private long _totalElapsedTicks;
    private long _globalGeneration = 1;

    private readonly ConcurrentDictionary<string, long> _namespaceGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MemoryTicketEntry> _memoryTickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SlidingWindowBucket> _memoryRateBuckets = new(StringComparer.Ordinal);
    private bool _disposed;

    private static readonly AsyncLocal<CallTraceState?> CurrentCallTrace = new();

    public OperationalCacheService(PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _prefix = string.IsNullOrWhiteSpace(options.CacheKeyPrefix) ? "ygp:" : options.CacheKeyPrefix;
        string? redisUrl = ResolveRedisUrl(options.RedisUrl);
        _redisConfigured = !string.IsNullOrWhiteSpace(redisUrl);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddMemoryCache();

        if (_redisConfigured)
        {
            try
            {
                ConfigurationOptions redisConfig = ParseRedisConfiguration(redisUrl!);
                _redis = ConnectionMultiplexer.Connect(redisConfig);

                services.AddSingleton<IConnectionMultiplexer>(_redis);
                services.AddStackExchangeRedisCache(redisCacheOptions =>
                {
                    redisCacheOptions.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(_redis);
                });

                // 装饰 IDistributedCache 以精确统计 L2 Redis 命中并容错降级
                ServiceDescriptor? originalDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDistributedCache));
                if (originalDescriptor is not null)
                {
                    services.Remove(originalDescriptor);
                    services.AddSingleton<IDistributedCache>(sp =>
                    {
                        RedisCache inner = ActivatorUtilities.CreateInstance<RedisCache>(sp);
                        return new InstrumentedDistributedCache(inner, _logger);
                    });
                }

                SubscribePubSubInvalidation(_redis);
                _logger.LogInformation("Connected to Redis L2 cache and subscribed to invalidation channel '{Channel}'.", PubSubInvalidationChannel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Redis at '{RedisUrl}'; gracefully degrading to L1 in-memory HybridCache.", redisUrl);
                _redis = null;
            }
        }

#pragma warning disable EXTEXP0018
        services.AddHybridCache(hybridOptions =>
        {
            hybridOptions.MaximumPayloadBytes = 4 * 1024 * 1024;
            hybridOptions.MaximumKeyLength = 512;
            hybridOptions.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });
#pragma warning restore EXTEXP0018

        _serviceProvider = services.BuildServiceProvider();
        _hybridCache = _serviceProvider.GetRequiredService<HybridCache>();
    }

    public bool IsRedisConnected => _redis is { IsConnected: true };

    /// <summary>
    /// 读取或生成多级缓存项（自动附带命名空间代数，支持 O(1) 按域清理与热重载平滑切代）。
    /// </summary>
    public async ValueTask<T> GetOrCreateAsync<T>(
        string @namespace,
        string rawKey,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan ttl,
        TimeSpan? l1Ttl = null,
        CancellationToken cancellationToken = default)
    {
        string scopedKey = BuildScopedKey(@namespace, rawKey);
        CallTraceState trace = new();
        CallTraceState? previous = CurrentCallTrace.Value;
        CurrentCallTrace.Value = trace;

        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            HybridCacheEntryOptions entryOptions = new()
            {
                Expiration = ttl,
                LocalCacheExpiration = l1Ttl ?? TimeSpan.FromTicks(Math.Min(ttl.Ticks, TimeSpan.FromMinutes(5).Ticks))
            };

            T result = await _hybridCache.GetOrCreateAsync(
                scopedKey,
                async ct =>
                {
                    trace.FactoryInvoked = true;
                    return await factory(ct).ConfigureAwait(false);
                },
                entryOptions,
                tags: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            RecordTelemetry(trace, Stopwatch.GetTimestamp() - startTimestamp);
            return result;
        }
        finally
        {
            CurrentCallTrace.Value = previous;
        }
    }

    public async ValueTask RemoveAsync(string @namespace, string rawKey, CancellationToken cancellationToken = default)
    {
        string scopedKey = BuildScopedKey(@namespace, rawKey);
        await _hybridCache.RemoveAsync(scopedKey, cancellationToken).ConfigureAwait(false);
        await PublishInvalidationAsync($"key:{scopedKey}").ConfigureAwait(false);
    }

    /// <summary>
    /// 按命名空间（如 <c>auth</c>、<c>ban</c>、<c>cards</c>、<c>rooms</c>）原子切代清理缓存，并通过 Redis Pub/Sub 同步其他节点。
    /// </summary>
    public async Task<long> FlushNamespaceAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string ns = NormalizeSegment(@namespace);
        long nextGen = _namespaceGenerations.AddOrUpdate(ns, 2, static (_, current) => current + 1);
        await PublishInvalidationAsync($"ns:{ns}:{nextGen}").ConfigureAwait(false);
        _logger.LogInformation("Flushed cache namespace '{Namespace}' to generation {Generation}.", ns, nextGen);
        return nextGen;
    }

    /// <summary>
    /// 全局清理所有缓存命名空间。
    /// </summary>
    public async Task<long> FlushAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long nextGlobal = Interlocked.Increment(ref _globalGeneration);
        await PublishInvalidationAsync($"all:{nextGlobal}").ConfigureAwait(false);
        _logger.LogInformation("Flushed all cache namespaces to global generation {GlobalGeneration}.", nextGlobal);
        return nextGlobal;
    }

    /// <summary>
    /// 签发一次性票据（如 <c>ws</c> 实时事件连接票据、<c>bot</c> 内置机器人入房票据、<c>reconnect</c> 重连票据）。
    /// </summary>
    public async Task<string> IssueOneTimeTicketAsync(
        string category,
        string payload,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string nonce = TokenCrypto.GenerateNonce(24);
        string redisKey = $"{_prefix}ticket:{NormalizeSegment(category)}:{nonce}";

        if (_redis is { IsConnected: true })
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                await db.StringSetAsync(redisKey, payload, ttl).ConfigureAwait(false);
                return nonce;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis ticket issue failed; falling back to local memory ticket.");
            }
        }

        PruneExpiredMemoryTickets();
        _memoryTickets[redisKey] = new MemoryTicketEntry(payload, DateTime.UtcNow.Add(ttl));
        return nonce;
    }

    /// <summary>
    /// 原子消费一次性票据（Redis <c>GETDEL</c> / 内存 <c>TryRemove</c> 阅后即焚，防重放）。
    /// </summary>
    public async Task<string?> ConsumeOneTimeTicketAsync(
        string category,
        string nonce,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(nonce))
            return null;

        string redisKey = $"{_prefix}ticket:{NormalizeSegment(category)}:{nonce.Trim()}";

        if (_redis is { IsConnected: true })
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                RedisValue val = await db.StringGetDeleteAsync(redisKey).ConfigureAwait(false);
                if (val.HasValue)
                    return val.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis ticket GETDEL failed; checking local memory ticket store.");
            }
        }

        if (_memoryTickets.TryRemove(redisKey, out MemoryTicketEntry? entry) && entry.ExpiresAtUtc > DateTime.UtcNow)
            return entry.Payload;

        return null;
    }

    /// <summary>
    /// 滑动窗口限流检查（如登录防爆破 <c>ygp:rl:login:{ip}</c>、房间猜密码限流 <c>ygp:rl:room_pw:{ip}:{room}</c>）。
    /// </summary>
    public async Task<RateLimitDecision> CheckSlidingWindowAsync(
        string bucket,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int safeLimit = Math.Max(1, limit);
        long windowMs = Math.Max(1000L, (long)window.TotalMilliseconds);
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string fullKey = $"{_prefix}rl:{bucket}";

        if (_redis is { IsConnected: true })
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                const string luaScript = """
                    local key = KEYS[1]
                    local now = tonumber(ARGV[1])
                    local window = tonumber(ARGV[2])
                    local limit = tonumber(ARGV[3])
                    local member = ARGV[4]
                    redis.call('ZREMRANGEBYSCORE', key, '-inf', now - window)
                    local count = redis.call('ZCARD', key)
                    if count >= limit then
                        local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
                        local retryMs = window
                        if oldest[2] then
                            retryMs = math.max(1000, window - (now - tonumber(oldest[2])))
                        end
                        return {0, 0, math.ceil(retryMs / 1000)}
                    end
                    redis.call('ZADD', key, now, member)
                    redis.call('PEXPIRE', key, window)
                    return {1, limit - count - 1, 0}
                    """;

                RedisResult[]? res = (RedisResult[]?)await db.ScriptEvaluateAsync(
                    luaScript,
                    [fullKey],
                    [nowMs, windowMs, safeLimit, $"{nowMs}:{Guid.NewGuid():N}"]).ConfigureAwait(false);

                if (res is { Length: 3 })
                {
                    bool allowed = (int)res[0] == 1;
                    int remaining = (int)res[1];
                    int retryAfter = (int)res[2];
                    return new RateLimitDecision(allowed, safeLimit, remaining, retryAfter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis sliding window check failed; falling back to local memory rate limiter.");
            }
        }

        SlidingWindowBucket localBucket = _memoryRateBuckets.GetOrAdd(fullKey, static _ => new SlidingWindowBucket());
        return localBucket.CheckAndRecord(nowMs, windowMs, safeLimit);
    }

    public CacheStatsSnapshot GetStats()
    {
        PruneExpiredMemoryTickets();

        long l1 = Interlocked.Read(ref _l1Hits);
        long l2 = Interlocked.Read(ref _l2Hits);
        long misses = Interlocked.Read(ref _misses);
        long total = Interlocked.Read(ref _totalRequests);
        long ticks = Interlocked.Read(ref _totalElapsedTicks);

        long hits = l1 + l2;
        double hitRatio = total > 0 ? Math.Round((double)hits * 100.0 / total, 2) : 100.0;
        double avgMs = total > 0
            ? Math.Round(TimeSpan.FromTicks((long)((double)ticks / total * TimeSpan.TicksPerSecond / Stopwatch.Frequency)).TotalMilliseconds, 3)
            : 0.0;

        Dictionary<string, long> nsSnapshot = new(_namespaceGenerations, StringComparer.OrdinalIgnoreCase);
        return new CacheStatsSnapshot(
            _redisConfigured,
            IsRedisConnected,
            l1,
            l2,
            misses,
            hitRatio,
            avgMs,
            _memoryTickets.Count,
            Interlocked.Read(ref _globalGeneration),
            nsSnapshot);
    }

    private string BuildScopedKey(string @namespace, string rawKey)
    {
        string ns = NormalizeSegment(@namespace);
        long globalGen = Interlocked.Read(ref _globalGeneration);
        long nsGen = _namespaceGenerations.GetOrAdd(ns, 1);
        return $"{_prefix}{ns}:g{globalGen}.{nsGen}:{rawKey}";
    }

    private void RecordTelemetry(CallTraceState trace, long elapsedStopwatchTicks)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Add(ref _totalElapsedTicks, elapsedStopwatchTicks);

        if (trace.FactoryInvoked)
            Interlocked.Increment(ref _misses);
        else if (trace.L2Hit)
            Interlocked.Increment(ref _l2Hits);
        else
            Interlocked.Increment(ref _l1Hits);
    }

    private void SubscribePubSubInvalidation(IConnectionMultiplexer multiplexer)
    {
        ISubscriber sub = multiplexer.GetSubscriber();
        sub.Subscribe(RedisChannel.Literal(PubSubInvalidationChannel), (_, message) =>
        {
            string? text = message.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return;

            int sep = text.IndexOf('|');
            if (sep <= 0)
                return;

            string senderNode = text[..sep];
            if (string.Equals(senderNode, _nodeId, StringComparison.Ordinal))
                return;

            string command = text[(sep + 1)..];
            if (command.StartsWith("key:", StringComparison.Ordinal))
            {
                string key = command[4..];
                _ = _hybridCache.RemoveAsync(key).AsTask();
            }
            else if (command.StartsWith("ns:", StringComparison.Ordinal))
            {
                string[] parts = command.Split(':');
                if (parts.Length == 3 && long.TryParse(parts[2], out long gen))
                    _namespaceGenerations.AddOrUpdate(parts[1], gen, (_, current) => Math.Max(current, gen));
            }
            else if (command.StartsWith("all:", StringComparison.Ordinal) && long.TryParse(command.AsSpan(4), out long allGen))
            {
                long current = Interlocked.Read(ref _globalGeneration);
                if (allGen > current)
                    Interlocked.Exchange(ref _globalGeneration, allGen);
            }
        });
    }

    private async Task PublishInvalidationAsync(string command)
    {
        if (_redis is not { IsConnected: true })
            return;

        try
        {
            ISubscriber sub = _redis.GetSubscriber();
            await sub.PublishAsync(RedisChannel.Literal(PubSubInvalidationChannel), $"{_nodeId}|{command}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish cache invalidation '{Command}'.", command);
        }
    }

    private void PruneExpiredMemoryTickets()
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, MemoryTicketEntry> pair in _memoryTickets)
        {
            if (pair.Value.ExpiresAtUtc <= now)
                _memoryTickets.TryRemove(pair.Key, out _);
        }
    }

    private static string? ResolveRedisUrl(string? explicitUrl)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl))
            return explicitUrl.Trim();

        string? env = Environment.GetEnvironmentVariable("YGOPRO_REDIS_URL");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    private static ConfigurationOptions ParseRedisConfiguration(string rawUrl)
    {
        string normalized = rawUrl.Trim();
        if (normalized.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            Uri uri = new(normalized);
            ConfigurationOptions opts = new()
            {
                Ssl = string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase),
                AbortOnConnectFail = false,
                ConnectTimeout = 3000,
                SyncTimeout = 2000
            };
            opts.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : 6379);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                string[] userParts = uri.UserInfo.Split(':', 2);
                if (userParts.Length == 2)
                {
                    if (!string.IsNullOrEmpty(userParts[0]))
                        opts.User = Uri.UnescapeDataString(userParts[0]);
                    opts.Password = Uri.UnescapeDataString(userParts[1]);
                }
                else
                {
                    opts.Password = Uri.UnescapeDataString(userParts[0]);
                }
            }
            return opts;
        }

        ConfigurationOptions parsed = ConfigurationOptions.Parse(normalized);
        parsed.AbortOnConnectFail = false;
        return parsed;
    }

    private static string NormalizeSegment(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "default" : value.Trim().ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _redis?.Dispose();
        _serviceProvider.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_redis is not null)
            await _redis.CloseAsync().ConfigureAwait(false);
        _redis?.Dispose();
        await _serviceProvider.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class CallTraceState
    {
        public bool L2Hit { get; set; }
        public bool FactoryInvoked { get; set; }
    }

    private sealed record MemoryTicketEntry(string Payload, DateTime ExpiresAtUtc);

    private sealed class SlidingWindowBucket
    {
        private readonly object _gate = new();
        private readonly Queue<long> _timestamps = new();

        public RateLimitDecision CheckAndRecord(long nowMs, long windowMs, int limit)
        {
            lock (_gate)
            {
                long cutoff = nowMs - windowMs;
                while (_timestamps.Count > 0 && _timestamps.Peek() <= cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= limit)
                {
                    long oldest = _timestamps.Peek();
                    int retryAfterSec = (int)Math.Max(1L, (windowMs - (nowMs - oldest) + 999L) / 1000L);
                    return new RateLimitDecision(false, limit, 0, retryAfterSec);
                }

                _timestamps.Enqueue(nowMs);
                return new RateLimitDecision(true, limit, limit - _timestamps.Count, 0);
            }
        }
    }

    private sealed class InstrumentedDistributedCache : IDistributedCache
    {
        private readonly IDistributedCache _inner;
        private readonly ILogger _logger;

        public InstrumentedDistributedCache(IDistributedCache inner, ILogger logger)
        {
            _inner = inner;
            _logger = logger;
        }

        public byte[]? Get(string key)
        {
            try
            {
                byte[]? bytes = _inner.Get(key);
                if (bytes is { Length: > 0 } && CurrentCallTrace.Value is { } trace)
                    trace.L2Hit = true;
                return bytes;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis L2 Get failed for key {Key}; degrading to miss.", key);
                return null;
            }
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            try
            {
                byte[]? bytes = await _inner.GetAsync(key, token).ConfigureAwait(false);
                if (bytes is { Length: > 0 } && CurrentCallTrace.Value is { } trace)
                    trace.L2Hit = true;
                return bytes;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis L2 GetAsync failed for key {Key}; degrading to miss.", key);
                return null;
            }
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            try
            {
                _inner.Set(key, value, options);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis L2 Set failed for key {Key}.", key);
            }
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            try
            {
                await _inner.SetAsync(key, value, options, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis L2 SetAsync failed for key {Key}.", key);
            }
        }

        public void Refresh(string key)
        {
            try { _inner.Refresh(key); } catch { }
        }

        public async Task RefreshAsync(string key, CancellationToken token = default)
        {
            try { await _inner.RefreshAsync(key, token).ConfigureAwait(false); } catch { }
        }

        public void Remove(string key)
        {
            try { _inner.Remove(key); } catch { }
        }

        public async Task RemoveAsync(string key, CancellationToken token = default)
        {
            try { await _inner.RemoveAsync(key, token).ConfigureAwait(false); } catch { }
        }
    }
}
