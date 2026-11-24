using System.IO;
using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace YGOProSharp.Protocol;

/// <summary>
/// 基于 .NET 内置 <see cref="ClientWebSocket"/> 的零依赖 WebSocket 传输实现，支持 <c>ws://</c> 与 <c>wss://</c>。
/// </summary>
public sealed class WebSocketNetworkClient : INetworkTransport
{
    public event Action? Connected;
    public event Action<Exception?>? Disconnected;
    public event Action<byte[]>? DataReceived;

    private const int ReceiveChunkSize = 4096;
    private const int DefaultMaxMessageLength = 1024 * 1024;

    private readonly WebSocketFramingMode _framingMode;
    private readonly Action<ClientWebSocketOptions>? _configureOptions;
    private readonly int _maxMessageLength;
    private readonly byte[] _receiveBuffer = new byte[ReceiveChunkSize];
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ILogger<WebSocketNetworkClient> _logger = AppLog.CreateLogger<WebSocketNetworkClient>();

    private ClientWebSocket? _webSocket;
    private Uri? _remoteUri;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;
    private bool _isClosed;

    public WebSocketNetworkClient(
        WebSocketFramingMode framingMode = WebSocketFramingMode.LengthPrefixed,
        Action<ClientWebSocketOptions>? configureOptions = null,
        int maxMessageLength = DefaultMaxMessageLength)
    {
        if (maxMessageLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessageLength), maxMessageLength, "Max message length must be positive.");

        _framingMode = framingMode;
        _configureOptions = configureOptions;
        _maxMessageLength = maxMessageLength;
    }

    public bool IsConnected { get; private set; }

    public IPAddress RemoteIPAddress { get; private set; } = IPAddress.None;

    public Uri? RemoteUri => _remoteUri;

    public WebSocketFramingMode FramingMode => _framingMode;

    public bool IsMessageFramed => _framingMode == WebSocketFramingMode.MessagePerPacket;

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!string.Equals(uri.Scheme, Uri.UriSchemeWs, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeWss, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"WebSocket URI scheme must be 'ws' or 'wss', got '{uri.Scheme}'.", nameof(uri));
        }

        if (IsConnected || _isClosed)
            return;

        try
        {
            _remoteUri = uri;
            RemoteIPAddress = IPAddress.TryParse(uri.Host, out IPAddress? parsedAddress) ? parsedAddress : IPAddress.None;

            ClientWebSocket webSocket = new();
            _configureOptions?.Invoke(webSocket.Options);
            _webSocket = webSocket;

            await webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            IsConnected = true;
            _logger.LogInformation("WebSocket client connected to {RemoteUri} ({FramingMode}).", uri, _framingMode);
            Connected?.Invoke();
            BeginReceive(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to connect WebSocket client to {RemoteUri}.", uri);
            Close(ex);
        }
    }

    public void BeginConnect(Uri uri)
    {
        _ = ConnectAsync(uri);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ClientWebSocket webSocket = _webSocket ?? throw new InvalidOperationException("WebSocket client is not connected.");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Sending {ByteCount} bytes over WebSocket to {RemoteUri}.", data.Length, _remoteUri);
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("WebSocket send payload preview: {PayloadPreview}.", CreatePayloadPreview(data.Span));

            await webSocket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WebSocket send failed for {RemoteUri}.", _remoteUri);
            Close(ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void BeginSend(byte[] data)
    {
        _ = SendAsync(data).ContinueWith(
            task => Close(task.Exception?.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public void BeginReceive()
    {
        BeginReceive(CancellationToken.None);
    }

    public void BeginReceive(CancellationToken cancellationToken)
    {
        if (_webSocket is null || _isClosed)
            return;

        if (_receiveTask is { IsCompleted: false })
            return;

        _receiveCancellation?.Dispose();
        _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCancellation.Token), CancellationToken.None);
    }

    public void Close(Exception? error = null)
    {
        if (_isClosed)
            return;

        _isClosed = true;
        IsConnected = false;

        if (error is null)
            _logger.LogInformation("WebSocket client disconnected from {RemoteUri}.", _remoteUri);
        else
            _logger.LogWarning(error, "WebSocket client disconnected with error from {RemoteUri}.", _remoteUri);

        try
        {
            _receiveCancellation?.Cancel();
            if (_webSocket is { State: WebSocketState.Open or WebSocketState.CloseReceived })
            {
                _webSocket.Abort();
            }
        }
        catch (Exception ex)
        {
            error = error is null ? ex : new AggregateException(error, ex);
        }
        finally
        {
            _webSocket?.Dispose();
            _webSocket = null;
            Disconnected?.Invoke(error);
        }
    }

    public void Dispose()
    {
        Close();
        _receiveCancellation?.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        ClientWebSocket webSocket = _webSocket ?? throw new InvalidOperationException("WebSocket client is not connected.");
        MemoryStream? messageBuffer = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ValueWebSocketReceiveResult result = await webSocket.ReceiveAsync(_receiveBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Remote WebSocket endpoint {RemoteUri} closed the connection.", _remoteUri);
                    Close();
                    return;
                }

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Ignoring non-binary WebSocket frame ({MessageType}) from {RemoteUri}.", result.MessageType, _remoteUri);
                    continue;
                }

                if (_framingMode == WebSocketFramingMode.LengthPrefixed)
                {
                    if (result.Count == 0)
                        continue;

                    DispatchBytes(_receiveBuffer.AsSpan(0, result.Count));
                    continue;
                }

                // MessagePerPacket 模式：单分片完整消息直接零额外缓冲分发，多分片则拼齐至 EndOfMessage。
                if (result.EndOfMessage && messageBuffer is null)
                {
                    DispatchBytes(_receiveBuffer.AsSpan(0, result.Count));
                    continue;
                }

                messageBuffer ??= new MemoryStream(Math.Max(result.Count * 2, ReceiveChunkSize));
                if (messageBuffer.Length + result.Count > _maxMessageLength)
                    throw new InvalidDataException($"Incoming WebSocket message exceeded maximum allowed length {_maxMessageLength}.");

                if (result.Count > 0)
                    messageBuffer.Write(_receiveBuffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    byte[] payload = messageBuffer.ToArray();
                    messageBuffer.SetLength(0);
                    DispatchBytes(payload);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_isClosed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket receive failed for {RemoteUri}.", _remoteUri);
            Close(ex);
        }
        finally
        {
            messageBuffer?.Dispose();
        }
    }

    private void DispatchBytes(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return;

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Received {ByteCount} bytes over WebSocket from {RemoteUri}.", payload.Length, _remoteUri);
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("WebSocket receive payload preview: {PayloadPreview}.", CreatePayloadPreview(payload));

        DataReceived?.Invoke(payload.ToArray());
    }

    private static string CreatePayloadPreview(ReadOnlySpan<byte> payload)
    {
        const int maxPreviewLength = 32;
        ReadOnlySpan<byte> preview = payload[..Math.Min(payload.Length, maxPreviewLength)];
        string suffix = payload.Length > maxPreviewLength ? "..." : string.Empty;
        return Convert.ToHexString(preview) + suffix;
    }
}
