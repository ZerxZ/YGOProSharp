using System;
using System.IO;
using System.Threading;
using System.Net;
using WindBot.Game;
using WindBot.Game.AI;
using YGOProSharp.Core.Cards;
using System.Collections.Specialized;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace WindBot
{
    public class Program
    {
        internal static Random Rand;

        internal static void Main(string[] args)
        {
            Config.Load(args);
            ConfigureLogging();

            Logger.WriteLine("WindBot starting...");

            string databasePath = Config.GetString("DbPath", "cards.cdb");

            InitDatas(databasePath);

            bool serverMode = Config.GetBool("ServerMode", false);

            if (serverMode)
            {
                // Run in server mode, provide a http interface to create bot.
                int serverPort = Config.GetInt("ServerPort", 2399);
                RunAsServer(serverPort);
            }
            else
            {
                // Join the host specified on the command line.
                if (args.Length == 0)
                {
                    Logger.WriteErrorLine("=== WARN ===");
                    Logger.WriteLine("No input found, tring to connect to localhost YGOPro host.");
                    Logger.WriteLine("If it fail, the program will quit sliently.");
                }
                RunFromArgs();
            }
        }

        public static void InitDatas(string databasePath)
        {
            Rand = new Random();
            DecksManager.Init();
            List<string> databasePaths = ResolveDatabasePaths(databasePath);
            if (databasePaths.Count == 0)
            {
                Logger.WriteErrorLine("Can't find cards database file.");
                Logger.WriteErrorLine("Please configure DbPath/DbPaths or place cards.cdb next to WindBot.");
                System.Environment.Exit(1);
            }

            SqliteCardDatabaseManager databaseManager = new SqliteCardDatabaseManager();
            CardDatabase.Initialize(databaseManager.LoadNamedCards(databasePaths));
            Logger.WriteLine("Card databases loaded: " + databasePaths.Count);
        }

        private static void ConfigureLogging()
        {
            LogLevel logLevel = AppLog.ParseLevel(Config.GetString("LogLevel"), LogLevel.Information);
            AppLog.Configure(LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(logLevel);
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
            }));
        }

        private static List<string> ResolveDatabasePaths(string databasePath)
        {
            List<string> paths = new List<string>();
            AddDatabasePathCandidates(paths, databasePath);
            AddDatabasePathCandidates(paths, Config.GetString("DbPaths"));
            AddDatabasePathCandidates(paths, Config.GetString("Databases"));
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddDatabasePathCandidates(List<string> paths, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string[] parts = value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPath in parts)
            {
                string trimmedPath = rawPath.Trim();
                foreach (string candidate in ExpandDatabasePath(trimmedPath))
                {
                    if (File.Exists(candidate))
                    {
                        paths.Add(candidate);
                        break;
                    }
                }
            }
        }

        private static IEnumerable<string> ExpandDatabasePath(string databasePath)
        {
            if (Path.IsPathRooted(databasePath))
            {
                yield return Path.GetFullPath(databasePath);
                yield break;
            }

            yield return Path.GetFullPath(databasePath);
            yield return Path.GetFullPath(Path.Combine("Data", databasePath));
            yield return Path.GetFullPath(Path.Combine("cdb", databasePath));

            string locale = Config.GetString("Locale");
            if (!string.IsNullOrWhiteSpace(locale))
                yield return Path.GetFullPath(Path.Combine("Data", "locales", locale, databasePath));

            yield return Path.GetFullPath(Path.Combine("..", databasePath));
            yield return Path.GetFullPath(Path.Combine("..", "cdb", databasePath));
        }

        private static void RunFromArgs()
        {
            WindBotInfo Info = new WindBotInfo();
            Info.Name = Config.GetString("Name", Info.Name);
            Info.Deck = Config.GetString("Deck", Info.Deck);
            Info.DeckFile = Config.GetString("DeckFile", Info.DeckFile);
            Info.Dialog = Config.GetString("Dialog", Info.Dialog);
            Info.Host = Config.GetString("Host", Info.Host);
            Info.Port = Config.GetInt("Port", Info.Port);
            Info.HostInfo = Config.GetString("HostInfo", Info.HostInfo);
            Info.Version = Config.GetInt("Version", Info.Version);
            Info.Hand = Config.GetInt("Hand", Info.Hand);
            Info.Debug = Config.GetBool("Debug", Info.Debug);
            Info.Chat = Config.GetBool("Chat", Info.Chat);
            Run(Info);
        }

        private static void RunAsServer(int ServerPort)
        {
            using (HttpListener MainServer = new HttpListener())
            {
                MainServer.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                MainServer.Prefixes.Add("http://+:" + ServerPort + "/");
                MainServer.Start();
                Logger.WriteLine("WindBot server start successed.");
                Logger.WriteLine("HTTP GET http://127.0.0.1:" + ServerPort + "/?name=WindBot&host=127.0.0.1&port=7911 to call the bot.");
                while (true)
                {
#if !DEBUG
    try
    {
#endif
                    HttpListenerContext ctx = MainServer.GetContext();

                    WindBotInfo Info = new WindBotInfo();
                    string RawUrl = Path.GetFileName(ctx.Request.RawUrl);
                    var queryStringParser = QueryStringParser.ParseQueryString(RawUrl);
                    Info.Name = queryStringParser["name"];
                    Info.Deck = queryStringParser["deck"];
                    Info.Host = queryStringParser["host"];
                    string port = queryStringParser["port"];
                    if (port != null)
                        Info.Port = Int32.Parse(port);
                    string deckfile = queryStringParser["deckfile"];
                    if (deckfile != null)
                        Info.DeckFile = deckfile;
                    string dialog = queryStringParser["dialog"];
                    if (dialog != null)
                        Info.Dialog = dialog;
                    string version = queryStringParser["version"];
                    if (version != null)
                        Info.Version = Int16.Parse(version);
                    string password = queryStringParser["password"];
                    if (password != null)
                        Info.HostInfo = password;
                    string hand = queryStringParser["hand"];
                    if (hand != null)
                        Info.Hand = Int32.Parse(hand);
                    string debug = queryStringParser["debug"];
                    if (debug != null)
                        Info.Debug= bool.Parse(debug);
                    string chat = queryStringParser["chat"];
                    if (chat != null)
                        Info.Chat = bool.Parse(chat);

                    if (Info.Name == null || Info.Host == null || port == null)
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                    }
                    else
                    {
#if !DEBUG
        try
        {
#endif
                        Thread workThread = new Thread(new ParameterizedThreadStart(Run));
                        workThread.Start(Info);
#if !DEBUG
        }
        catch (Exception ex)
        {
            Logger.WriteErrorLine("Start Thread Error: " + ex);
        }
#endif
                        ctx.Response.StatusCode = 200;
                        ctx.Response.Close();
                    }
#if !DEBUG
    }
    catch (Exception ex)
    {
        Logger.WriteErrorLine("Parse Http Request Error: " + ex);
    }
#endif
                }
            }
        }

        private static void Run(object o)
        {
#if !DEBUG
    try
    {
    //all errors will be catched instead of causing the program to crash.
#endif
            WindBotInfo Info = (WindBotInfo)o;
            GameClient client = new GameClient(Info);
            client.Start();
            Logger.DebugWriteLine(client.Username + " started.");
            while (client.Connection.IsConnected)
            {
#if !DEBUG
        try
        {
#endif
                client.Tick();
                Thread.Sleep(30);
#if !DEBUG
        }
        catch (Exception ex)
        {
            Logger.WriteErrorLine("Tick Error: " + ex);
        }
#endif
            }
            Logger.DebugWriteLine(client.Username + " end.");
#if !DEBUG
    }
    catch (Exception ex)
    {
        Logger.WriteErrorLine("Run Error: " + ex);
    }
#endif
        }

        public static FileStream ReadFile(string directory, string filename, string extension)
        {
            string tryfilename = filename + "." + extension;
            string fullpath = Path.Combine(directory, tryfilename);
            if (!File.Exists(fullpath))
                fullpath = filename;
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../", filename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../deck/", filename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../", tryfilename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../deck/", tryfilename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("Data/WindBot/" + directory, tryfilename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("Deck/", tryfilename);
            return new FileStream(fullpath, FileMode.Open, FileAccess.Read);
        }
    }

    public static class QueryStringParser
    {
        public static NameValueCollection ParseQueryString(string query)
        {
            var result = new NameValueCollection();
            if (!string.IsNullOrEmpty(query))
            {
                var pairs = query.Split('&');
                foreach (var pair in pairs)
                {
                    if (pair.Contains("="))
                    {
                        var parts = pair.Split('=');
                        var key = Uri.UnescapeDataString(parts[0]);
                        var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                        result[key] = value;
                    }
                }
            }
            return result;
        }
    }
}

