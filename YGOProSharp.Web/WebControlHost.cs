using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.AgentBridge;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Mcp;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;
using YGOProSharp.Web.Logging;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;
using YGOProSharp.Web.Services;

namespace YGOProSharp.Web;

/// <summary>
/// ASP.NET Core 控制面宿主：所有业务端点均通过声明式特性由 Roslyn 增量源生成器（<c>WebControlHost.Endpoints.g.cs</c>）在编译期完成零反射映射。
/// </summary>
public sealed partial class WebControlHost : IAsyncDisposable, IDisposable
{
    private readonly ILogger<WebControlHost> _logger = AppLog.CreateLogger<WebControlHost>();
    private readonly YGOProSharpServerHost _serverHost;
    private readonly OperationalStore _store;
    private readonly OperationalServerCoordinator _coordinator;
    private readonly WebControlOptions _options;
    private readonly LogRingBuffer _logBuffer;
    private readonly WebEventHub _eventHub;
    private readonly DiyPackageManager _diyManager;
    private readonly McpServerEngine _mcpEngine;
    private readonly UserPortalHost? _embeddedPortal;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    private WebApplication? _app;
    private bool _disposed;

    public WebControlHost(
        YGOProSharpServerHost serverHost,
        OperationalStore store,
        OperationalServerCoordinator? coordinator = null,
        WebControlOptions? options = null,
        LogRingBuffer? logBuffer = null)
    {
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? new OperationalServerCoordinator(store);
        _options = options ?? new WebControlOptions();
        _logBuffer = logBuffer ?? new LogRingBuffer(_options.LogRingBufferCapacity);
        _eventHub = new WebEventHub();

        string diyRoot = Path.Combine(Path.GetFullPath(_serverHost.Server.Options.RootPath), "diy");
        _diyManager = new DiyPackageManager(diyRoot, new SqliteCardDatabaseManager());
        AgentSessionManager sessionManager = _coordinator.GetOrCreateAgentSessionManager(_serverHost, _options.NamedCardRepositoryProvider);
        _mcpEngine = new McpServerEngine(new McpRuntimeDependencies
        {
            ServerHost = _serverHost,
            Store = _store,
            SessionManager = sessionManager,
            DiyManager = _diyManager,
            NamedCardRepositoryProvider = () => _options.NamedCardRepositoryProvider?.Invoke() ?? EmptyNamedCardRepository.Instance,
            LuaRuntimeProvider = _options.LuaRuntimeProvider,
            SpawnWindBotCallback = _options.WindBotSpawner
        });

        if (_options.EnableUnifiedPortal)
        {
            _embeddedPortal = new UserPortalHost(
                _serverHost,
                _store,
                _coordinator,
                new UserPortalOptions
                {
                    Host = _options.Host,
                    Port = _options.Port,
                    PublicGameHost = _options.PublicGameHost,
                    RequireHttpsCookie = _options.RequireHttpsCookie,
                    McpPortHint = _options.Port,
                    StaticFilesRoot = _options.StaticFilesRoot,
                    NamedCardRepositoryProvider = _options.NamedCardRepositoryProvider,
                    WindBotSpawner = _options.WindBotSpawner,
                    AvailableBotDecksProvider = _options.AvailableBotDecksProvider
                });
        }

        _coordinator.OnPublicEventPublished += HandleCoordinatorEvent;
        _logBuffer.OnEntryAdded += HandleBufferedLogAdded;
    }

    public LogRingBuffer LogBuffer => _logBuffer;
    public WebEventHub EventHub => _eventHub;
    public OperationalServerCoordinator Coordinator => _coordinator;
    public IReadOnlyCollection<string> BoundAddresses { get; private set; } = [];

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_app is not null)
            return;

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        string bindHost = string.IsNullOrWhiteSpace(_options.Host) ? "127.0.0.1" : _options.Host.Trim();
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = 256L * 1024 * 1024;
            if (IPAddress.TryParse(bindHost, out IPAddress? ip))
                kestrel.Listen(ip, _options.Port);
            else
                kestrel.ListenAnyIP(_options.Port);
        });

        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(forms =>
        {
            forms.MultipartBodyLengthLimit = 256L * 1024 * 1024;
            forms.ValueLengthLimit = 32 * 1024 * 1024;
        });

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        builder.AddYgoWebSecurityAndRateLimiting(_store, _options.SessionCookieName, "ygp_portal_sid");

        WebApplication app = builder.Build();
        app.UseYgoAntiBypassHardening(bindHost);
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(25)
        });

        string? staticRoot = WebSecurityExtensions.ResolveStaticFilesDirectory(_options.StaticFilesRoot);
        PhysicalFileProvider? fileProvider = staticRoot is not null ? new PhysicalFileProvider(staticRoot) : null;
        if (fileProvider is not null)
        {
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        }

        app.UseYgoWebSecurityAndRateLimiting();
        app.Use(async (ctx, next) =>
        {
            try
            {
                await next(ctx).ConfigureAwait(false);
            }
            catch (BadHttpRequestException badReq) when (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = badReq.StatusCode;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    error = "bad_http_request",
                    message = badReq.StatusCode == StatusCodes.Status413PayloadTooLarge
                        ? "上传文件超过服务器单次 256MB 体积上限。"
                        : badReq.Message
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ctx.Response.HasStarted && !ctx.WebSockets.IsWebSocketRequest)
            {
                _logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    error = "internal_error",
                    message = $"服务器处理请求时发生异常：{ex.Message}"
                }).ConfigureAwait(false);
            }
        });

        // 编译期源生成路由挂载（零反射）
        MapGeneratedEndpoints(app);
        _embeddedPortal?.MapGeneratedPortalCoreEndpoints(app);
        app.MapMcpStreamableHttp(_mcpEngine, _store);

        if (staticRoot is not null)
        {
            string indexHtmlPath = Path.Combine(staticRoot, "index.html");
            app.MapFallback(async (HttpContext http) =>
            {
                PathString path = http.Request.Path;
                if (path.StartsWithSegments("/api") ||
                    path.StartsWithSegments("/ws") ||
                    path.StartsWithSegments("/mcp") ||
                    path.StartsWithSegments("/healthz"))
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                if (File.Exists(indexHtmlPath))
                {
                    http.Response.ContentType = "text/html; charset=utf-8";
                    await http.Response.SendFileAsync(indexHtmlPath, http.RequestAborted).ConfigureAwait(false);
                }
                else
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            });
        }

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;

        IServerAddressesFeature? addrFeature = app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>();
        BoundAddresses = addrFeature?.Addresses.ToArray() ?? [$"http://{bindHost}:{_options.Port}"];
        _logger.LogInformation(
            "Web control plane listening on {Addresses} (PlayerPortal='/', AdminPanel='/admin', Mcp='/mcp', UnifiedPortal={UnifiedPortal}, StaticRoot={StaticRoot}).",
            string.Join(", ", BoundAddresses),
            _options.EnableUnifiedPortal,
            staticRoot ?? "<none>");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        WebApplication? app = _app;
        _app = null;
        if (app is not null)
        {
            try
            {
                await app.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [HttpGet("/api/surface")]
    private IResult GetAdminSurface() =>
        Results.Ok(new
        {
            surface = "admin",
            unifiedPortal = _options.EnableUnifiedPortal,
            defaultPage = _options.EnableUnifiedPortal ? "portal" : "admin",
            adminPath = "/admin",
            gamePort = _serverHost.Server.Options.Port,
            publicGameHost = _options.PublicGameHost,
            adminPort = _options.Port,
            portalPort = _options.PortalPort ?? (_options.EnableUnifiedPortal ? _options.Port : null)
        });

    [HttpGet("/healthz")]
    private async Task<IResult> GetAdminHealthzAsync(CancellationToken ct)
    {
        DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
        CacheStatsSnapshot cacheStats = _store.Cache.GetStats();
        bool serverOk = _serverHost.IsRunning;
        bool healthy = serverOk && dbHealth.IsHealthy;

        return Results.Json(new
        {
            status = healthy ? "healthy" : "degraded",
            uptimeSeconds = (long)(DateTime.UtcNow - _startedAtUtc).TotalSeconds,
            gameServerRunning = serverOk,
            gameServerListening = _serverHost.IsListening,
            maintenanceMode = _serverHost.Server.IsMaintenanceMode,
            database = new
            {
                provider = dbHealth.Provider,
                healthy = dbHealth.IsHealthy,
                isHealthy = dbHealth.IsHealthy,
                latencyMs = dbHealth.LatencyMs,
                journalMode = dbHealth.JournalMode,
                activeUserCount = dbHealth.ActiveUserCount,
                activeBanCount = dbHealth.ActiveBanCount,
                totalDuelRecords = dbHealth.TotalDuelRecords,
                error = dbHealth.ErrorMessage
            },
            cache = new
            {
                redisConfigured = cacheStats.RedisConfigured,
                redisConnected = cacheStats.RedisConnected,
                hitRatioPercent = cacheStats.HitRatioPercent
            }
        }, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    [MapEndpoint("/ws/events")]
    private async Task<IResult> HandleAdminEventsWebSocketAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.WebSockets.IsWebSocketRequest)
            return Results.BadRequest(new { message = "Expected WebSocket upgrade request." });

        AuthenticatedPrincipal? principal = null;
        if (http.Request.Query.TryGetValue("ticket", out var ticketVals) && !string.IsNullOrWhiteSpace(ticketVals.ToString()))
        {
            string? rawSessionFromTicket = await _store.Cache.ConsumeOneTimeTicketAsync("ws", ticketVals.ToString(), ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(rawSessionFromTicket))
                principal = await _store.ValidateSessionAsync(rawSessionFromTicket, ct).ConfigureAwait(false);
        }
        else
        {
            (string? rawToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
            principal = await _store.ValidateSessionAsync(rawToken, ct).ConfigureAwait(false);
        }

        if (principal is null || principal.MustChangePassword || principal.IsPortalSessionOnly)
            return Results.Unauthorized();

        using WebSocket ws = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await _eventHub.HandleConnectionAsync(ws, principal, ct).ConfigureAwait(false);
        return Results.Empty;
    }

    private void HandleCoordinatorEvent(string eventType, object payload)
    {
        _ = _store.Cache.FlushNamespaceAsync("rooms");
        _eventHub.BroadcastEvent(eventType, payload, RbacPermissions.RoomView);
    }

    private void HandleBufferedLogAdded(BufferedLogEntry entry) => _eventHub.BroadcastLog(entry);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _coordinator.OnPublicEventPublished -= HandleCoordinatorEvent;
        _logBuffer.OnEntryAdded -= HandleBufferedLogAdded;
        _eventHub.Dispose();
        if (_embeddedPortal is not null)
            await _embeddedPortal.DisposeAsync().ConfigureAwait(false);
        await StopAsync().ConfigureAwait(false);
    }
}
