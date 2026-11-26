using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
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
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;
using YGOProSharp.Web.Services;

namespace YGOProSharp.Web;

/// <summary>
/// 前台玩家与社区用户面板宿主：所有业务端点均通过声明式特性由 Roslyn 增量源生成器（<c>UserPortalHost.Endpoints.g.cs</c>）在编译期完成零反射映射。
/// </summary>
public sealed partial class UserPortalHost : IAsyncDisposable, IDisposable
{
    private readonly ILogger<UserPortalHost> _logger = AppLog.CreateLogger<UserPortalHost>();
    private readonly YGOProSharpServerHost _serverHost;
    private readonly OperationalStore _store;
    private readonly OperationalServerCoordinator _coordinator;
    private readonly UserPortalOptions _options;
    private readonly DiyPackageManager _diyManager;
    private readonly McpServerEngine _mcpEngine;
    private readonly ConcurrentDictionary<Guid, WebSocket> _publicSubscribers = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    private WebApplication? _app;
    private bool _disposed;

    public UserPortalHost(
        YGOProSharpServerHost serverHost,
        OperationalStore store,
        OperationalServerCoordinator? coordinator = null,
        UserPortalOptions? options = null)
    {
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? new OperationalServerCoordinator(store);
        _options = options ?? new UserPortalOptions();

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
            SpawnWindBotCallback = _options.WindBotSpawner
        });

        _coordinator.OnPublicEventPublished += HandlePublicEvent;
    }

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
            if (IPAddress.TryParse(bindHost, out IPAddress? ip))
                kestrel.Listen(ip, _options.Port);
            else
                kestrel.ListenAnyIP(_options.Port);
        });

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        builder.AddYgoWebSecurityAndRateLimiting(_store, "ygp_panel_sid", _options.SessionCookieName);

        WebApplication app = builder.Build();
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

        // 编译期源生成路由挂载（独立门户端口同时挂载独立探针与 PortalCore 分组）
        MapGeneratedEndpoints(app);
        MapGeneratedPortalCoreEndpoints(app);
        app.MapMcpStreamableHttp(_mcpEngine, _store, allowedSurfaceRoles: [McpRoleCodes.Player, McpRoleCodes.Spectator]);

        if (staticRoot is not null)
        {
            string indexHtml = Path.Combine(staticRoot, "index.html");
            app.MapFallback(async (HttpContext http) =>
            {
                if (http.Request.Path.StartsWithSegments("/api") ||
                    http.Request.Path.StartsWithSegments("/ws") ||
                    http.Request.Path.StartsWithSegments("/mcp"))
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    await http.Response.WriteAsJsonAsync(new { error = "not_found", message = "前台用户面板不存在该接口。" });
                    return;
                }

                http.Response.ContentType = "text/html; charset=utf-8";
                await http.Response.SendFileAsync(indexHtml);
            });
        }

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;

        IServerAddressesFeature? addrFeature = app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>();
        BoundAddresses = addrFeature?.Addresses.ToArray() ?? [$"http://{bindHost}:{_options.Port}"];
        _logger.LogInformation("User portal listening on {Addresses} (GameServerPort={GamePort}).", string.Join(", ", BoundAddresses), _serverHost.Server.Options.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        foreach ((Guid id, WebSocket ws) in _publicSubscribers.ToArray())
        {
            _publicSubscribers.TryRemove(id, out _);
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Portal shutting down", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                ws.Dispose();
            }
        }

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
    private IResult GetPortalSurface() =>
        Results.Ok(new
        {
            surface = "portal",
            gamePort = _serverHost.Server.Options.Port,
            publicGameHost = _options.PublicGameHost,
            portalPort = _options.Port,
            mcpPortHint = _options.McpPortHint ?? _options.Port
        });

    [HttpGet("/healthz")]
    private async Task<IResult> GetPortalHealthzAsync(CancellationToken ct)
    {
        DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
        bool ok = _serverHost.IsRunning && dbHealth.IsHealthy;
        return Results.Json(new
        {
            surface = "portal",
            status = ok ? "healthy" : "degraded",
            gameServerRunning = _serverHost.IsRunning,
            maintenanceMode = _serverHost.Server.IsMaintenanceMode
        }, statusCode: ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    [MapEndpoint("/ws/public", Group = "PortalCore")]
    private async Task HandlePortalPublicWebSocketAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket ws = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        Guid subId = Guid.NewGuid();
        _publicSubscribers[subId] = ws;

        try
        {
            byte[] welcome = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "connected", surface = "portal" }));
            await ws.SendAsync(welcome, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

            byte[] recvBuf = new byte[1024];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult res = await ws.ReceiveAsync(recvBuf, ct).ConfigureAwait(false);
                if (res.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _publicSubscribers.TryRemove(subId, out _);
        }
    }

    private void HandlePublicEvent(string eventType, object payload)
    {
        _ = _store.Cache.FlushNamespaceAsync("portal");
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type = eventType,
            timestampUtc = DateTime.UtcNow,
            data = payload
        }));

        foreach ((Guid id, WebSocket ws) in _publicSubscribers.ToArray())
        {
            if (ws.State != WebSocketState.Open)
            {
                _publicSubscribers.TryRemove(id, out _);
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    _publicSubscribers.TryRemove(id, out _);
                }
            });
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _coordinator.OnPublicEventPublished -= HandlePublicEvent;
        await StopAsync().ConfigureAwait(false);
    }
}
