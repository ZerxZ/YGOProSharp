using System.Net;

namespace YGOProSharp.Protocol;

/// <summary>
/// WebSocket 二进制分帧模式。
/// </summary>
public enum WebSocketFramingMode
{
    /// <summary>
    /// 流式长度前缀模式（默认）：WebSocket 二进制负载中携带与原生 TCP 一致的 2 字节小端长度头，
    /// 兼容 websockify、srvpro/koishipro WS 网关及常见 TCP-over-WS 代理。
    /// </summary>
    LengthPrefixed = 0,

    /// <summary>
    /// 单帧即单包模式：每条完整 WebSocket Binary 消息直接对应一个 CTOS/STOC payload，无额外长度头。
    /// </summary>
    MessagePerPacket = 1
}

/// <summary>
/// 底层网络传输抽象，统一原生 TCP（<see cref="NetworkClient"/>）与 WebSocket（<see cref="WebSocketNetworkClient"/>）。
/// </summary>
public interface INetworkTransport : IDisposable
{
    event Action? Connected;
    event Action<Exception?>? Disconnected;
    event Action<byte[]>? DataReceived;

    bool IsConnected { get; }

    IPAddress RemoteIPAddress { get; }

    /// <summary>
    /// 指示传输层本身是否已完成消息分帧。
    /// 为 <c>true</c> 时，<see cref="BinaryClient"/> 直接把每次收发字节块视为完整业务包，不额外加/拆长度头。
    /// </summary>
    bool IsMessageFramed { get; }

    void BeginSend(byte[] data);

    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    void BeginReceive();

    void Close(Exception? error = null);
}
