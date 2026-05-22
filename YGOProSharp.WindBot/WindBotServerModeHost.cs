using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text.Json;

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
                    string queryText = context.Request.Url == null
                        ? string.Empty
                        : context.Request.Url.Query.TrimStart('?');
                    WindBotInfo info = CreateInfoFromQuery(QueryStringParser.ParseQueryString(queryText));
                    if (info == null)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
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
                    context.Response.Close();
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
            WindBotInfo info = new WindBotInfo
            {
                Name = query["name"],
                Deck = query["deck"],
                Host = query["host"]
            };

            string port = query["port"];
            if (port != null)
                info.Port = int.Parse(port);

            string deckFile = query["deckfile"];
            if (deckFile != null)
                info.DeckFile = deckFile;

            string dialog = query["dialog"];
            if (dialog != null)
                info.Dialog = dialog;

            string version = query["version"];
            if (version != null)
                info.Version = short.Parse(version);

            string password = query["password"];
            if (password != null)
                info.HostInfo = password;

            string hand = query["hand"];
            if (hand != null)
                info.Hand = int.Parse(hand);

            string debug = query["debug"];
            if (debug != null)
                info.Debug = bool.Parse(debug);

            string chat = query["chat"];
            if (chat != null)
                info.Chat = bool.Parse(chat);

            if (info.Name == null || info.Host == null || port == null)
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
