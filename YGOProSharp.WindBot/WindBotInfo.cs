using System;
using YGOProSharp.Protocol;

namespace WindBot
{
    public class WindBotInfo
    {
        public string Name { get; set; }
        public string Deck { get; set; }
        public string DeckFile { get; set; }
        public string Dialog { get; set; }

        /// <summary>
        /// 可选的 Bot 级本地化区域标识（如 <c>zh-CN</c>、<c>zh-TW</c>、<c>en-US</c>）。
        /// 未指定时自动回退到 <see cref="WindBotRuntime.Locale"/>。
        /// </summary>
        public string Locale { get; set; }

        public string Host { get; set; }
        public int Port { get; set; }
        public string HostInfo { get; set; }
        public int Version { get; set; }
        public int Hand { get; set; }
        public bool Debug { get; set; }
        public bool Chat { get; set; }

        /// <summary>
        /// 可选的 Bot 级连续聊天最小间隔（毫秒）。未指定（<c>null</c>）时跟随 <see cref="WindBotRuntime.ChatIntervalMilliseconds"/>；设为 <c>0</c> 表示立即发送不节流。
        /// </summary>
        public int? ChatIntervalMilliseconds { get; set; }

        /// <summary>
        /// 可选的开局（<c>GameMessage.Start</c>）首条聊天延迟（毫秒），用于等待客户端完成对局场景加载后再推送首回合台词。未指定时跟随 <see cref="WindBotRuntime.DuelStartChatDelayMilliseconds"/>。
        /// </summary>
        public int? DuelStartChatDelayMilliseconds { get; set; }

        /// <summary>
        /// 可选的待发送聊天队列上限（超出时丢弃最旧消息，防止长连招积压过久）。未指定时跟随 <see cref="WindBotRuntime.MaxQueuedChatMessages"/>。
        /// </summary>
        public int? MaxQueuedChatMessages { get; set; }

        /// <summary>
        /// 是否启用 WebSocket 传输。当 <see cref="Host"/> 本身以 <c>ws://</c> 或 <c>wss://</c> 开头时会自动启用。
        /// </summary>
        public bool UseWebSocket { get; set; }

        /// <summary>
        /// 配合 <see cref="UseWebSocket"/> 使用：true 表示使用 <c>wss://</c>，false 表示 <c>ws://</c>。
        /// </summary>
        public bool WebSocketUseWss { get; set; }

        /// <summary>
        /// 配合 <see cref="UseWebSocket"/> 使用的可选 WebSocket 路径（例如 <c>/ws</c>）。
        /// </summary>
        public string WebSocketPath { get; set; }

        /// <summary>
        /// WebSocket 分帧模式：默认 <see cref="WebSocketFramingMode.LengthPrefixed"/>（2 字节长度头），
        /// 也支持 <see cref="WebSocketFramingMode.MessagePerPacket"/>（单条 WS Binary 消息即单包）。
        /// </summary>
        public WebSocketFramingMode WebSocketFramingMode { get; set; }

        public WindBotInfo()
        {
            Name = "WindBot";
            Deck = null;
            DeckFile = null;
            Dialog = "default";
            Locale = null;
            Host = "127.0.0.1";
            Port = 7911;
            HostInfo = "";
            Version = 0x1362;
            Hand = 0;
            Debug = false;
            Chat = true;
            UseWebSocket = false;
            WebSocketUseWss = false;
            WebSocketPath = null;
            WebSocketFramingMode = WebSocketFramingMode.LengthPrefixed;
        }
    }
}

