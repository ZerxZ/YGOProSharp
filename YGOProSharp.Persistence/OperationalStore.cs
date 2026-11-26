using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence;

/// <summary>
/// 运营数据与多级缓存统一门面（内部按单一职责拆分为 Auth / UsersAndRoles / McpTokens / Moderation / DuelReplays / DiyAndAudit / AiEvaluations 分部模块）。
/// </summary>
public sealed partial class OperationalStore : IAsyncDisposable, IDisposable
{
    private readonly ILogger<OperationalStore> _logger = AppLog.CreateLogger<OperationalStore>();
    private readonly PersistenceOptions _options;
    private readonly Argon2PasswordHasher _passwordHasher;
    private readonly OperationalDatabaseManager _dbManager;
    private readonly OperationalCacheService _cache;
    private bool _disposed;

    public OperationalStore(PersistenceOptions? options = null)
    {
        _options = options ?? new PersistenceOptions();
        _passwordHasher = new Argon2PasswordHasher(
            _options.Argon2MemoryKb,
            _options.Argon2Iterations,
            _options.Argon2Parallelism);
        _dbManager = new OperationalDatabaseManager(_options);
        _cache = new OperationalCacheService(_options);
    }

    public PersistenceOptions Options => _options;
    public OperationalDatabaseManager Database => _dbManager;
    public OperationalCacheService Cache => _cache;
    public Argon2PasswordHasher PasswordHasher => _passwordHasher;
    public BootstrapAdminResult? LastBootstrapResult { get; private set; }

    public async Task<BootstrapAdminResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetFullPath(_options.ReplayDirectory));
        BootstrapAdminResult result = await _dbManager.InitializeAndSeedAsync(_passwordHasher, cancellationToken).ConfigureAwait(false);
        LastBootstrapResult = result;
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cache.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        return _cache.DisposeAsync();
    }
}
