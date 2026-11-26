namespace YGOProSharp.Tests;

public sealed class WindBotClientTests
{
    [Fact]
    public void WindBotPacketFactoryWritesCtosMessageByte()
    {
        using BinaryWriter writer = WindBot.Game.GamePacketFactory.Create(CtosMessage.Chat);
        MemoryStream stream = (MemoryStream)writer.BaseStream;

        AssertSequenceEqual(new[] { (byte)CtosMessage.Chat }, stream.ToArray());
    }

    [Fact]
    public void WindBotDeckLoadsThroughNamedCardRepository()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpWindBotDeckTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

        try
        {
            File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "Smoke.ydk"),
            [
                "#created by test",
                "1",
                "2",
                "!side",
                "3",
                "404"
            ]);

            Environment.CurrentDirectory = temporaryDirectory;
            WindBot.Game.Deck deck = WindBot.Game.Deck.Load("Smoke", TestNamedRepository(
                TestNamedCard(1),
                TestNamedCard(2, (int)CardType.Monster | (int)CardType.Fusion),
                TestNamedCard(3)));

            AssertEqual(1, deck.Cards.Count);
            AssertEqual(1, deck.ExtraCards.Count);
            AssertEqual(1, deck.SideCards.Count);
            AssertEqual(1, deck.Cards[0].Id);
            AssertEqual(2, deck.ExtraCards[0].Id);
            AssertEqual(3, deck.SideCards[0].Id);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void WindBotServerModeParsesQueryInfo()
    {
        NameValueCollection query = WindBot.QueryStringParser.ParseQueryString(
            "name=Bot&deck=AI&host=127.0.0.1&port=7911&deckfile=Smoke.ydk&dialog=default&version=4962&password=abc&hand=1&debug=true&chat=false");

        WindBot.WindBotInfo info = WindBot.WindBotServerModeHost.CreateInfoFromQuery(query);
        if (info == null)
            throw new InvalidOperationException("Expected query to create WindBotInfo.");

        AssertEqual("Bot", info.Name);
        AssertEqual("AI", info.Deck);
        AssertEqual("127.0.0.1", info.Host);
        AssertEqual(7911, info.Port);
        AssertEqual("Smoke.ydk", info.DeckFile);
        AssertEqual("default", info.Dialog);
        AssertEqual(4962, info.Version);
        AssertEqual("abc", info.HostInfo);
        AssertEqual(1, info.Hand);
        AssertTrue(info.Debug);
        AssertFalse(info.Chat);
    }

    [Fact]
    public void WindBotServerModeRejectsMalformedQueryParameters()
    {
        AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
            WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=notanumber")) is null);
        AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
            WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=70000")) is null);
        AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
            WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=7911&debug=invalid")) is null);
        AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
            WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=7911&wsframing=invalid")) is null);
    }

    [Fact]
    public void WindBotServerModeAndCliParseWebSocketOptions()
    {
        WindBot.WindBotInfo? wsUriInfo = WindBot.WindBotServerModeHost.CreateInfoFromQuery(
            WindBot.QueryStringParser.ParseQueryString("name=WsBot&host=wss://example.com:8443/ygopro&wsframing=MessagePerPacket"));
        if (wsUriInfo is null)
            throw new InvalidOperationException("Expected ws:// or wss:// host without explicit port param to succeed.");

        AssertTrue(wsUriInfo.UseWebSocket);
        AssertEqual(8443, wsUriInfo.Port);
        AssertEqual((int)WebSocketFramingMode.MessagePerPacket, (int)wsUriInfo.WebSocketFramingMode);

        CliConfiguration cliConfig = CliConfiguration.Load([
            "Name=CliWsBot",
            "Host=127.0.0.1",
            "Port=7911",
            "UseWebSocket=true",
            "WebSocketUseWss=true",
            "WebSocketPath=/ygo",
            "WebSocketFramingMode=MessagePerPacket"
        ]);
        WindBot.WindBotInfo cliInfo = CliOptionsFactory.CreateWindBotInfo(cliConfig);
        AssertTrue(cliInfo.UseWebSocket);
        AssertTrue(cliInfo.WebSocketUseWss);
        AssertEqual("/ygo", cliInfo.WebSocketPath);
        AssertEqual((int)WebSocketFramingMode.MessagePerPacket, (int)cliInfo.WebSocketFramingMode);
    }

    [Fact]
    public void WindBotDecksManagerInitializesLazilyAndResolvesByNameAndFile()
    {
        WindBot.WindBotInfo byNameInfo = new() { Deck = "Yubel" };
        WindBot.Game.GameClient byNameClient = new(byNameInfo, TestNamedRepository());
        WindBot.Game.Duel duel = new();
        WindBot.Game.GameAI aiByName = new(byNameClient, duel);

        WindBot.Game.AI.Executor execByName = WindBot.Game.AI.DecksManager.Instantiate(aiByName, duel);
        AssertEqual("AI_Yubel", execByName.Deck);

        WindBot.WindBotInfo byFileInfo = new() { Deck = "AI_Yubel" };
        WindBot.Game.GameClient byFileClient = new(byFileInfo, TestNamedRepository());
        WindBot.Game.GameAI aiByFile = new(byFileClient, duel);

        WindBot.Game.AI.Executor execByFile = WindBot.Game.AI.DecksManager.Instantiate(aiByFile, duel);
        AssertEqual("AI_Yubel", execByFile.Deck);
    }

    [Fact]
    public void WindBotUpstreamSyncSupportsAltartNewDecksAndHooks()
    {
        // 1. 验证 NamedCard.IsAltartAlias 异画判定（含 5405695 特例）
        AssertTrue(NamedCard.IsAltartAlias(89631140, 89631139));
        AssertFalse(NamedCard.IsAltartAlias(89631160, 89631139));
        AssertFalse(NamedCard.IsAltartAlias(5405695, 5405694));

        // 2. 验证从上游同步的全新卡组执行器均可正常注册与实例化
        WindBot.Game.Duel duel = new();
        foreach (string deckName in new[] { "Elfnote", "ChaosRitual", "RadiantTyphoon", "Voiceless", "SacredBeast", "Monarch506" })
        {
            WindBot.Game.GameClient client = new(new WindBot.WindBotInfo { Deck = deckName }, TestNamedRepository());
            WindBot.Game.GameAI ai = new(client, duel);
            WindBot.Game.AI.Executor exec = WindBot.Game.AI.DecksManager.Instantiate(ai, duel);
            AssertTrue(exec is not null && !string.IsNullOrWhiteSpace(exec.Deck));
        }

        // 3. 验证上游新增的 Executor 钩子已同步接入 LuaDeckExecutor 与 xmake 风格 DSL
        int posChangeCount = 0;
        FakeLuaRuntime runtime = new(context =>
        {
            var onPosChange = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_pos_change"]!;
            var onSelectDisfield = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_select_disfield"]!;
            var onSelectDirect = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_select_battle_direct_attack"]!;

            onPosChange(new FakeRawLuaClosure(_ => { posChangeCount++; return null; }));
            onSelectDisfield(new FakeRawLuaClosure(_ => (double)0x04));
            onSelectDirect(new FakeRawLuaClosure(_ => false));
        });

        WindBot.Game.GameClient luaClient = new(new WindBot.WindBotInfo { Name = "CtxBot", Deck = "HookTest" }, TestNamedRepository());
        WindBot.Game.GameAI luaAi = new(luaClient, duel);
        using WindBot.Game.AI.Lua.LuaDeckExecutor luaExec = new(luaAi, duel, runtime, "-- hooks", "HookTest.lua");

        luaExec.OnPosChange(new WindBot.Game.ClientCard(1, CardLocation.MonsterZone, 0), 1, 4);
        AssertEqual(1, posChangeCount);
        AssertEqual(0x04U, luaExec.OnSelectDisfield(0, 1, 0x1FU));
        AssertFalse(luaExec.OnSelectBattleDirectAttack(new WindBot.Game.ClientCard(1, CardLocation.MonsterZone, 0), true));
        AssertTrue(luaClient.GetLogContext().Contains("Bot=CtxBot", StringComparison.Ordinal));
    }

    [Fact]
    public void WindBotGameBehaviorThrowsClearErrorWhenDeckIsMissing()
    {
        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
        {
            Deck = "Yubel",
            DeckFile = "missing_deck_" + Guid.NewGuid().ToString("N")
        }, TestNamedRepository());

        AssertThrows<InvalidOperationException>(() => _ = new WindBot.Game.GameBehavior(client, TestNamedRepository()));
    }

    [Fact]
    public void WindBotBotListPathLogsMissingFile()
    {
        TestLog.Provider.Clear();
        string missingPath = Path.Combine(Path.GetTempPath(), "missing_windbot_list_" + Guid.NewGuid().ToString("N") + ".json");

        IReadOnlyList<WindBot.WindBotInfo> bots = WindBot.WindBotServerModeHost.LoadBotList(missingPath);

        AssertEqual(0, bots.Count);
        AssertTrue(TestLog.Provider.Contains(LogLevel.Error, "BotListPath file not found"));
    }

    [Fact]
    public async Task WindBotClientConnectsToLoopbackServerAndSendsDeckAsync()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpWindBotSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

        try
        {
            File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "AI_Yubel.ydk"),
            [
                "1",
                "!side",
                "2"
            ]);

            Environment.CurrentDirectory = temporaryDirectory;

            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            INamedCardRepository repository = TestNamedRepository(
                TestNamedCard(1),
                TestNamedCard(2));

            WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
            {
                CardRepository = repository,
                TickDelayMilliseconds = 5
            });

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            Task<Socket> acceptTask = listener.AcceptSocketAsync(cts.Token).AsTask();
            Task botTask = service.RunBotAsync(new WindBot.WindBotInfo
            {
                Name = "SmokeBot",
                Host = "127.0.0.1",
                Port = port,
                Deck = "AI_Yubel",
                Chat = false
            }, cts.Token);

            using Socket serverSocket = await WaitAsync(acceptTask);
            using NetworkClient serverClient = new(serverSocket);
            PacketFramer framer = new();
            List<byte[]> receivedPackets = new();
            TaskCompletionSource<bool> handshakeDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> readyReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

            serverClient.DataReceived += data =>
            {
                framer.Append(data);
                while (framer.TryReadPacket(out byte[] packet))
                {
                    lock (receivedPackets)
                    {
                        receivedPackets.Add(packet);
                        if (receivedPackets.Count >= 3)
                            handshakeDone.TrySetResult(true);
                        if (packet.Length > 0 && packet[0] == (byte)CtosMessage.HsReady)
                            readyReceived.TrySetResult(true);
                    }
                }
            };
            serverClient.BeginReceive(cts.Token);

            await WaitAsync(handshakeDone.Task);

            // 发送 JoinGame 和 TypeChange 后，确认 WindBot 会提交卡组并准备就绪。
            using (MemoryStream joinPayload = new())
            using (BinaryWriter writer = new(joinPayload, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)StocMessage.JoinGame);
                writer.Write(0U);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)5);
                await serverClient.SendAsync(framer.Frame(joinPayload.ToArray()), cts.Token);
            }

            await serverClient.SendAsync(framer.Frame([(byte)StocMessage.TypeChange, (byte)PlayerType.Host]), cts.Token);
            await WaitAsync(readyReceived.Task);

            lock (receivedPackets)
            {
                AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.PlayerInfo));
                AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.JoinGame));
                AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.UpdateDeck));
                AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.HsReady));
            }

            cts.Cancel();
            await botTask;
            serverClient.Close();
            listener.Stop();
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task WindBotClientConnectsOverWebSocketAndSendsDeckAsync()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpWindBotWsSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

        try
        {
            File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "AI_Yubel.ydk"),
            [
                "1",
                "!side",
                "2"
            ]);

            Environment.CurrentDirectory = temporaryDirectory;

            int port = GetAvailableTcpPort();
            using HttpListener listener = new();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            INamedCardRepository repository = TestNamedRepository(
                TestNamedCard(1),
                TestNamedCard(2));

            WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
            {
                CardRepository = repository,
                TickDelayMilliseconds = 5
            });

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            Task<HttpListenerContext> acceptTask = listener.GetContextAsync();
            Task botTask = service.RunBotAsync(new WindBot.WindBotInfo
            {
                Name = "WsSmokeBot",
                Host = $"ws://127.0.0.1:{port}/ygopro",
                Deck = "AI_Yubel",
                Chat = false
            }, cts.Token);

            HttpListenerContext httpContext = await WaitAsync(acceptTask);
            AssertTrue(httpContext.Request.IsWebSocketRequest);
            HttpListenerWebSocketContext wsContext = await WaitAsync(httpContext.AcceptWebSocketAsync(subProtocol: null));
            using WebSocket serverWs = wsContext.WebSocket;

            PacketFramer framer = new();
            List<byte[]> receivedPackets = new();
            byte[] recvBuf = new byte[2048];

            while (receivedPackets.Count < 3)
            {
                WebSocketReceiveResult res = await WaitAsync(serverWs.ReceiveAsync(recvBuf, cts.Token));
                framer.Append(recvBuf.AsSpan(0, res.Count));
                while (framer.TryReadPacket(out byte[] pkt))
                    receivedPackets.Add(pkt);
            }

            // 发送 JoinGame 与 TypeChange，确认 WindBot 通过 WebSocket 提交卡组并准备就绪
            using (MemoryStream joinPayload = new())
            using (BinaryWriter writer = new(joinPayload, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)StocMessage.JoinGame);
                writer.Write(0U);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)5);
                await serverWs.SendAsync(framer.Frame(joinPayload.ToArray()), WebSocketMessageType.Binary, true, cts.Token);
            }

            await serverWs.SendAsync(framer.Frame([(byte)StocMessage.TypeChange, (byte)PlayerType.Host]), WebSocketMessageType.Binary, true, cts.Token);

            while (!receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.HsReady))
            {
                WebSocketReceiveResult res = await WaitAsync(serverWs.ReceiveAsync(recvBuf, cts.Token));
                framer.Append(recvBuf.AsSpan(0, res.Count));
                while (framer.TryReadPacket(out byte[] pkt))
                    receivedPackets.Add(pkt);
            }

            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.PlayerInfo));
            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.JoinGame));
            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.UpdateDeck));
            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.HsReady));

            cts.Cancel();
            await botTask;
            listener.Stop();
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void WindBotDirectClientProducesCtosPacketsSynchronously()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpDirectBot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

        try
        {
            File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "AI_Yubel.ydk"),
            [
                "1",
                "!side",
                "2"
            ]);
            Environment.CurrentDirectory = temporaryDirectory;

            INamedCardRepository botCards = TestNamedRepository(TestNamedCard(1), TestNamedCard(2));
            WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
            {
                CardRepository = botCards
            });

            List<CtosMessage> producedEvents = new();
            using WindBot.WindBotDirectClient directClient = service.CreateDirectClient(new WindBot.WindBotInfo
            {
                Name = "DirectBot",
                Deck = "AI_Yubel",
                Chat = false
            });
            directClient.CtosPacketProduced += pkt => producedEvents.Add((CtosMessage)pkt.Span[0]);

            directClient.Start(pumpInitialHandshake: true);
            AssertTrue(directClient.IsStarted);
            AssertTrue(directClient.IsConnected);

            IReadOnlyList<byte[]> handshakePackets = directClient.DrainCtosPackets();
            AssertEqual(3, handshakePackets.Count);
            AssertEqual((byte)CtosMessage.ExternalAddress, handshakePackets[0][0]);
            AssertEqual((byte)CtosMessage.PlayerInfo, handshakePackets[1][0]);
            AssertEqual((byte)CtosMessage.JoinGame, handshakePackets[2][0]);
            AssertEqual(3, producedEvents.Count);

            // 同步喂入 JoinGame + TypeChange（Host），无需后台线程即可立刻拿到 UpdateDeck + HsReady
            using (MemoryStream joinPayload = new())
            using (BinaryWriter writer = new(joinPayload, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(0U);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)5);
                directClient.FeedStocPacket(StocMessage.JoinGame, joinPayload.ToArray());
            }
            directClient.FeedStocPacket(StocMessage.TypeChange, [(byte)PlayerType.Host]);

            IReadOnlyList<byte[]> readyPackets = directClient.DrainCtosPackets();
            AssertTrue(readyPackets.Any(p => p[0] == (byte)CtosMessage.UpdateDeck));
            AssertTrue(readyPackets.Any(p => p[0] == (byte)CtosMessage.HsReady));

            // 喂入 SelectHand，验证同步产出 HandResult
            directClient.FeedStocPacket(StocMessage.SelectHand);
            AssertTrue(directClient.TryDequeueCtosPacket(out byte[] handPacket));
            AssertEqual((byte)CtosMessage.HandResult, handPacket[0]);

            // 直接发起聊天与投降
            directClient.Chat("gg");
            directClient.Surrender();
            IReadOnlyList<byte[]> controlPackets = directClient.DrainCtosPackets();
            AssertEqual(2, controlPackets.Count);
            AssertEqual((byte)CtosMessage.Chat, controlPackets[0][0]);
            AssertEqual((byte)CtosMessage.Surrender, controlPackets[1][0]);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task WindBotServiceStartBotCapturesStartupErrorAsync()
    {
        await using WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
        {
            CardRepository = TestNamedRepository(),
            TickDelayMilliseconds = 5
        });

        (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) = InMemoryNetworkTransport.CreatePair();
        using WindBot.WindBotSession session = service.StartBot(
            new WindBot.WindBotInfo
            {
                Name = "BrokenDeckBot",
                Deck = "Yubel",
                DeckFile = "missing_deck_" + Guid.NewGuid().ToString("N")
            },
            clientTransport);

        await WaitAsync(session.Completion.ContinueWith(_ => true));
        AssertFalse(session.IsRunning);
        AssertTrue(session.LastError is InvalidOperationException);
        AssertEqual(0, service.GetActiveSessions().Count);
        AssertFalse(serverTransport.IsConnected);

        bool threw = false;
        try
        {
            await session.WaitForCompletionAsync(throwOnError: true);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        AssertTrue(threw);
    }

}
