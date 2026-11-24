using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text.Json;
using YGOProSharp.Protocol;

namespace WindBot
{
    public sealed class WindBotServerModeHost
    {
        private readonly int _serverPort;
        private readonly Func<WindBotInfo, CancellationToken, Task> _startBot;
        private readonly string _botListPath;

        public WindBotServerModeHost(int serverPort, Func<WindBotInfo, CancellationToken, Task> startBot, string botListPath = null)
        {
            _serverPort = serverPort;
            _startBot = startBot;
            _botListPath = botListPath;
        }

        public WindBotServerModeHost(int serverPort, Action<WindBotInfo> startBot, string botListPath = null)
            : this(serverPort, (info, _) =>
            {
                startBot(info);
                return Task.CompletedTask;
            }, botListPath)
        {
        }

        public void Run()
        {
            RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            LoadBotList(_botListPath);

            using HttpListener mainServer = new HttpListener();
            mainServer.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
            mainServer.Prefixes.Add("http://+:" + _serverPort + "/");
            mainServer.Start();
            using CancellationTokenRegistration registration = cancellationToken.Register(mainServer.Stop);
            Logger.WriteLine("WindBot server start successed.");
            Logger.WriteLine("HTTP GET http://127.0.0.1:" + _serverPort + "/?name=WindBot&host=127.0.0.1&port=7911 to call the bot.");

            while (!cancellationToken.IsCancellationRequested)
            {
#if !DEBUG
                try
                {
#endif
                    HttpListenerContext context = await mainServer.GetContextAsync().ConfigureAwait(false);
                    try
                    {
                        string queryText = context.Request.Url == null
                            ? string.Empty
                            : context.Request.Url.Query.TrimStart('?');
                        WindBotInfo info = CreateInfoFromQuery(QueryStringParser.ParseQueryString(queryText));
                        if (info == null)
                        {
                            context.Response.StatusCode = 400;
                            continue;
                        }

#if !DEBUG
                        try
                        {
#endif
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _startBot(info, cancellationToken).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Logger.WriteErrorLine("Start Thread Error: " + ex);
                                }
                            }, cancellationToken);
#if !DEBUG
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteErrorLine("Start Thread Error: " + ex);
                        }
#endif
                        context.Response.StatusCode = 200;
                    }
                    finally
                    {
                        context.Response.Close();
                    }
#if !DEBUG
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    Logger.WriteErrorLine("Parse Http Request Error: " + ex);
                }
#endif
            }
        }

        public static WindBotInfo CreateInfoFromQuery(NameValueCollection query)
        {
            if (query == null)
                return null;

            WindBotInfo info = new WindBotInfo
            {
                Name = query["name"],
                Deck = query["deck"],
                Host = query["host"]
            };

            bool isExplicitWsUri = !string.IsNullOrWhiteSpace(info.Host) &&
                (info.Host.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                 info.Host.StartsWith("wss://", StringComparison.OrdinalIgnoreCase));

            string rawPort = query["port"];
            if (rawPort != null)
            {
                if (!int.TryParse(rawPort, out int parsedPort) || parsedPort <= 0 || parsedPort > 65535)
                    return null;
                info.Port = parsedPort;
            }
            else if (isExplicitWsUri && Uri.TryCreate(info.Host, UriKind.Absolute, out Uri parsedWsUri))
            {
                info.Port = parsedWsUri.Port;
                info.UseWebSocket = true;
            }
            else
            {
                return null;
            }

            string rawWs = query["ws"] ?? query["usewebsocket"];
            if (rawWs != null)
            {
                if (!bool.TryParse(rawWs, out bool parsedWs))
                    return null;
                info.UseWebSocket = parsedWs;
            }

            string rawWss = query["wss"] ?? query["websocketusewss"];
            if (rawWss != null)
            {
                if (!bool.TryParse(rawWss, out bool parsedWss))
                    return null;
                info.WebSocketUseWss = parsedWss;
            }

            string wsPath = query["wspath"] ?? query["websocketpath"];
            if (wsPath != null)
                info.WebSocketPath = wsPath;

            string rawWsFraming = query["wsframing"] ?? query["websocketframingmode"];
            if (rawWsFraming != null)
            {
                if (!Enum.TryParse(rawWsFraming, ignoreCase: true, out WebSocketFramingMode parsedFraming))
                    return null;
                info.WebSocketFramingMode = parsedFraming;
            }

            string deckFile = query["deckfile"];
            if (deckFile != null)
                info.DeckFile = deckFile;

            string dialog = query["dialog"];
            if (dialog != null)
                info.Dialog = dialog;

            string rawVersion = query["version"];
            if (rawVersion != null)
            {
                if (!short.TryParse(rawVersion, out short parsedVersion))
                    return null;
                info.Version = parsedVersion;
            }

            string password = query["password"];
            if (password != null)
                info.HostInfo = password;

            string rawHand = query["hand"];
            if (rawHand != null)
            {
                if (!int.TryParse(rawHand, out int parsedHand))
                    return null;
                info.Hand = parsedHand;
            }

            string rawDebug = query["debug"];
            if (rawDebug != null)
            {
                if (!bool.TryParse(rawDebug, out bool parsedDebug))
                    return null;
                info.Debug = parsedDebug;
            }

            string rawChat = query["chat"];
            if (rawChat != null)
            {
                if (!bool.TryParse(rawChat, out bool parsedChat))
                    return null;
                info.Chat = parsedChat;
            }

            if (string.IsNullOrWhiteSpace(info.Name) || string.IsNullOrWhiteSpace(info.Host))
                return null;

            return info;
        }

        public static IReadOnlyList<WindBotInfo> LoadBotList(string botListPath)
        {
            if (string.IsNullOrWhiteSpace(botListPath))
                return Array.Empty<WindBotInfo>();

            if (!File.Exists(botListPath))
            {
                Logger.WriteErrorLine("BotListPath file not found: " + botListPath);
                return Array.Empty<WindBotInfo>();
            }

            try
            {
                string json = File.ReadAllText(botListPath);
                return JsonSerializer.Deserialize<List<WindBotInfo>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<WindBotInfo>();
            }
            catch (Exception ex)
            {
                Logger.WriteErrorLine("Failed to read BotListPath: " + ex);
                return Array.Empty<WindBotInfo>();
            }
        }
    }
}
