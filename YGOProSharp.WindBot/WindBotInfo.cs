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
        public string Host { get; set; }
        public int Port { get; set; }
        public string HostInfo { get; set; }
        public int Version { get; set; }
        public int Hand { get; set; }
        public bool Debug { get; set; }
        public bool Chat { get; set; }

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

