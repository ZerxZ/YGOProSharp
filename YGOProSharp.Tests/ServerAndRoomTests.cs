namespace YGOProSharp.Tests;

public sealed class ServerAndRoomTests
{
    [Fact]
    public async Task ServerBroadcastsChatToSenderAndOpponentAsync()
    {
        ServerOptions serverOptions = new()
        {
            EnableNetworkListener = false,
            ClientVersion = 0x1362,
            Game = new GameOptions { NoCheckDeck = true }
        };

        await using YGOProSharpServerHost host = new(duelFactory: null, cardRepository: TestRepository(TestCard(1)), options: serverOptions);
        host.StartBackgroundLoop(tickDelayMilliseconds: 2, embedded: true);

        YGOClient playerA = host.ConnectLocalClient();
        YGOClient playerB = host.ConnectLocalClient();

        List<(short Sender, string Text)> receivedByA = new();
        List<(short Sender, string Text)> receivedByB = new();
        playerA.PacketReceivedRaw += p =>
        {
            if (p.Length > 3 && p.Span[0] == (byte)StocMessage.Chat)
            {
                short sender = BitConverter.ToInt16(p.Span.Slice(1, 2));
                string text = Encoding.Unicode.GetString(p.Span.Slice(3)).TrimEnd('\0');
                receivedByA.Add((sender, text));
            }
        };
        playerB.PacketReceivedRaw += p =>
        {
            if (p.Length > 3 && p.Span[0] == (byte)StocMessage.Chat)
            {
                short sender = BitConverter.ToInt16(p.Span.Slice(1, 2));
                string text = Encoding.Unicode.GetString(p.Span.Slice(3)).TrimEnd('\0');
                receivedByB.Add((sender, text));
            }
        };

        void JoinPlayer(YGOClient client, string name)
        {
            using (MemoryStream info = new())
            using (BinaryWriter w = new(info, Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)CtosMessage.PlayerInfo);
                w.WriteUnicode(name, 20);
                client.Send(info.ToArray());
            }
            using (MemoryStream join = new())
            using (BinaryWriter w = new(join, Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)CtosMessage.JoinGame);
                w.Write((short)0x1362);
                w.Write(0);
                w.Write((short)0);
                w.WriteUnicode("", 20);
                client.Send(join.ToArray());
            }
        }

        JoinPlayer(playerA, "PlayerA");
        JoinPlayer(playerB, "PlayerB");
        await Task.Delay(40);
        playerA.Update();
        playerB.Update();

        using (MemoryStream chat = new())
        using (BinaryWriter w = new(chat, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)CtosMessage.Chat);
            w.WriteUnicodeAutoLength("你好 MDPro3!", 255);
            playerA.Send(chat.ToArray());
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && (receivedByA.Count == 0 || receivedByB.Count == 0))
        {
            playerA.Update();
            playerB.Update();
            await Task.Delay(10);
        }

        AssertTrue(receivedByA.Any(x => x.Sender == 0 && x.Text == "你好 MDPro3!"));
        AssertTrue(receivedByB.Any(x => x.Sender == 0 && x.Text == "你好 MDPro3!"));

        playerA.Close();
        playerB.Close();
        await host.StopAsync();
    }

    [Fact]
    public void HostInfoParsesCreateGamePayload()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(0x12345678U);
        writer.Write((byte)2);
        writer.Write((byte)5);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write(new byte[3]);
        writer.Write(4000);
        writer.Write((byte)4);
        writer.Write((byte)2);
        writer.Write((short)120);

        stream.Position = 0;
        using BinaryReader reader = new(stream);
        HostInfo info = HostInfo.ReadFrom(reader);

        AssertEqual(0x12345678U, info.LfList);
        AssertEqual(2, info.Region);
        AssertEqual(5, info.MasterRule);
        AssertEqual(1, info.Mode);
        AssertTrue(info.EnablePriority);
        AssertFalse(info.NoCheckDeck);
        AssertTrue(info.NoShuffleDeck);
        AssertEqual(4000, info.StartLp);
        AssertEqual(4, info.StartHand);
        AssertEqual(2, info.DrawCount);
        AssertEqual(120, info.TimeLimit);
    }

    [Fact]
    public void GamePacketFactoryCreatesDeckCountAndFieldFinish()
    {
        using BinaryWriter deckCount = GamePacketFactory.CreateDeckCount(40, 15, 3, 41, 14, 2);
        byte[] deckCountBytes = ((MemoryStream)deckCount.BaseStream).ToArray();

        AssertEqual(13, deckCountBytes.Length);
        AssertEqual((byte)StocMessage.DeckCount, deckCountBytes[0]);
        AssertEqual(40, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(1, 2)));
        AssertEqual(15, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(3, 2)));
        AssertEqual(3, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(5, 2)));
        AssertEqual(41, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(7, 2)));
        AssertEqual(14, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(9, 2)));
        AssertEqual(2, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(11, 2)));

        using BinaryWriter fieldFinish = GamePacketFactory.CreateFieldFinish();
        AssertSequenceEqual(new[] { (byte)StocMessage.FieldFinish }, ((MemoryStream)fieldFinish.BaseStream).ToArray());
    }

    [Fact]
    public void PlayerHandlesRequestFieldSafely()
    {
        TestLog.Provider.Clear();
        Game game = new(new CoreServer());
        Player player = new(game, new YGOClient());

        using MemoryStream infoStream = new();
        using (BinaryWriter writer = new(infoStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.PlayerInfo);
            writer.WriteUnicode("FieldTester", 20);
        }
        player.Parse(infoStream.ToArray());

        using MemoryStream joinStream = new();
        using (BinaryWriter writer = new(joinStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.JoinGame);
            writer.Write((short)0x1362);
            writer.Write(0);
            writer.Write((short)0);
        }
        player.Parse(joinStream.ToArray());
        player.Parse(new[] { (byte)CtosMessage.RequestField });

        AssertTrue(TestLog.Provider.Contains(LogLevel.Debug, "Ignoring field request"));
    }

    [Fact]
    public void PlayerParsesPlayerInfoFromSpans()
    {
        TestLog.Provider.Clear();
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write((byte)CtosMessage.PlayerInfo);
        writer.WriteUnicode("Tester", 20);

        Player player = new(new Game(new CoreServer()), new YGOClient());

        player.Parse(stream.ToArray());

        AssertEqual("Tester", player.Name);
        AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Player identified as Tester."));
    }

    [Fact]
    public void PlayerParsesVariableLengthChatPacket()
    {
        TestLog.Provider.Clear();
        Game game = new(new CoreServer());
        Player player = new(game, new YGOClient());

        using (MemoryStream infoStream = new())
        using (BinaryWriter writer = new(infoStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.PlayerInfo);
            writer.WriteUnicode("Chatter", 20);
            player.Parse(infoStream.ToArray());
        }

        using (MemoryStream joinStream = new())
        using (BinaryWriter writer = new(joinStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.JoinGame);
            writer.Write((short)0x1362);
            writer.Write(0);
            writer.Write((short)0);
            player.Parse(joinStream.ToArray());
        }

        using MemoryStream chatStream = new();
        using (BinaryWriter writer = new(chatStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.Chat);
            writer.WriteUnicodeAutoLength("你好，决斗者！", 255);
        }

        byte[] chatPacket = chatStream.ToArray();
        PacketReader reader = new(chatPacket.AsSpan(1));
        AssertEqual("你好，决斗者！", reader.ReadUnicodeAutoLength(256));

        player.Parse(chatPacket);
        AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Player Chatter sent chat message."));
    }

    [Fact]
    public void GameAnalyserParsesSelectChainAndSelectEffectYnPayloads()
    {
        Game game = new(new CoreServer());
        GameAnalyser analyser = new(game);
        Player p0 = new(game, new YGOClient());
        Player p1 = new(game, new YGOClient());
        game.CurPlayers[0] = p0;
        game.CurPlayers[1] = p1;

        // SelectEffectYn: player(1) + code(4) + info_location(4) + description(4) = 13 字节
        byte[] effectYnPayload = new byte[13];
        effectYnPayload[0] = 0;
        SequenceReader<byte> effectYnReader = new(new ReadOnlySequence<byte>(effectYnPayload));
        AssertEqual(1, analyser.Analyse(GameMessage.SelectEffectYn, ref effectYnReader, effectYnPayload));
        AssertEqual(0, (int)effectYnReader.Remaining);

        // SelectChain (count=1): player(1) + count(1) + spe_count(1) + hint1(4) + hint2(4) + item(14) = 25 字节
        byte[] selectChainPayload = new byte[25];
        selectChainPayload[0] = 1;
        selectChainPayload[1] = 1;
        SequenceReader<byte> selectChainReader = new(new ReadOnlySequence<byte>(selectChainPayload));
        AssertEqual(1, analyser.Analyse(GameMessage.SelectChain, ref selectChainReader, selectChainPayload));
        AssertEqual(0, (int)selectChainReader.Remaining);

        // ConfirmCards (count=1): player(1) + skip_panel(1) + count(1) + card(code:4 + controller:1 + location:1 + seq:1) = 10 字节
        byte[] confirmCardsPayload = new byte[10];
        confirmCardsPayload[0] = 0;
        confirmCardsPayload[1] = 0;
        confirmCardsPayload[2] = 1;
        confirmCardsPayload[8] = (byte)CardLocation.Hand;
        SequenceReader<byte> confirmCardsReader = new(new ReadOnlySequence<byte>(confirmCardsPayload));
        AssertEqual(0, analyser.Analyse(GameMessage.ConfirmCards, ref confirmCardsReader, confirmCardsPayload));
        AssertEqual(0, (int)confirmCardsReader.Remaining);
    }

    [Fact]
    public void GameLogsNativeErrors()
    {
        TestLog.Provider.Clear();
        string previousCurrentDirectory = Directory.GetCurrentDirectory();
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Directory.SetCurrentDirectory(temporaryDirectory);
            Game game = new(new CoreServer());
            System.Reflection.MethodInfo handleError = typeof(Game).GetMethod(
                "HandleError",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(Game), "HandleError");

            handleError.Invoke(game, ["synthetic lua error"]);

            AssertTrue(Directory.EnumerateFiles(temporaryDirectory, "lua_*.txt").Any());
            AssertTrue(TestLog.Provider.Contains(LogLevel.Error, "Native/Lua error"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task EmbeddedServerHostConnectsLocalWindBotSessionAndClientInMemoryAsync()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpInMemDuel_" + Guid.NewGuid().ToString("N"));
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

            ICardRepository serverCards = TestRepository(TestCard(1), TestCard(2));
            INamedCardRepository botCards = TestNamedRepository(TestNamedCard(1), TestNamedCard(2));

            ServerOptions serverOptions = new()
            {
                EnableNetworkListener = false,
                ClientVersion = 0x1362,
                Game = new GameOptions
                {
                    MasterRule = 5,
                    NoCheckDeck = true
                }
            };

            await using YGOProSharpServerHost serverHost = new(duelFactory: null, cardRepository: serverCards, options: serverOptions);
            serverHost.StartBackgroundLoop(tickDelayMilliseconds: 2, embedded: true);

            AssertTrue(serverHost.IsRunning);
            AssertFalse(serverHost.IsListening);

            // 先接入一个纯内存本地 C# 客户端，观察房间状态与聊天广播
            YGOClient observerClient = serverHost.ConnectLocalClient();
            List<byte[]> observerPackets = new();
            observerClient.PacketReceivedRaw += p => observerPackets.Add(p.ToArray());

            using (MemoryStream infoStream = new())
            using (BinaryWriter writer = new(infoStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)CtosMessage.PlayerInfo);
                writer.WriteUnicode("LocalHost", 20);
                observerClient.Send(infoStream.ToArray());
            }
            using (MemoryStream joinStream = new())
            using (BinaryWriter writer = new(joinStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)CtosMessage.JoinGame);
                writer.Write((short)0x1362);
                writer.Write(0);
                writer.Write((short)0);
                writer.WriteUnicode("", 20);
                observerClient.Send(joinStream.ToArray());
            }

            // 启动进程内托管 WindBot 会话并直接连入嵌入式服务端
            await using WindBot.WindBotService windBotService = new(new WindBot.WindBotRuntimeOptions
            {
                CardRepository = botCards,
                TickDelayMilliseconds = 5
            });

            AssertTrue(windBotService.GetAvailableDecks().Count > 0);

            bool sessionStarted = false;
            bool sessionStopped = false;

            using WindBot.WindBotSession botSession = windBotService.StartBot(
                new WindBot.WindBotInfo
                {
                    Name = "InMemBot",
                    Deck = "AI_Yubel",
                    Chat = false,
                    Version = 0x1362
                },
                serverHost.CreateLocalTransport());

            botSession.Started += _ => sessionStarted = true;
            botSession.Stopped += (_, _) => sessionStopped = true;

            AssertTrue(windBotService.TryGetSession(botSession.InstanceId, out WindBot.WindBotSession found));
            AssertTrue(ReferenceEquals(botSession, found));
            AssertEqual(2, serverHost.ClientCount);

            // 等待本地客户端收到 Bot 进入房间及准备状态变更（玩家 1 进入 + Ready）
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            bool sawBotReady = false;
            while (DateTime.UtcNow < deadline && !sawBotReady)
            {
                observerClient.Update();
                sawBotReady = observerPackets.Any(p =>
                    p.Length >= 2 &&
                    p[0] == (byte)StocMessage.HsPlayerChange &&
                    p[1] == ((1 << 4) | (byte)PlayerChange.Ready));
                if (!sawBotReady)
                    await Task.Delay(10);
            }

            AssertTrue(sawBotReady);
            AssertTrue(sessionStarted || botSession.IsConnected);

            // 通过 C# 会话句柄直接发送聊天，验证本地客户端收到 StocMessage.Chat
            botSession.Chat("hello in-memory");
            bool sawChat = false;
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && !sawChat)
            {
                observerClient.Update();
                sawChat = observerPackets.Any(p => p.Length > 3 && p[0] == (byte)StocMessage.Chat);
                if (!sawChat)
                    await Task.Delay(10);
            }
            AssertTrue(sawChat);

            // 停止所有 Bot 会话并确认清理干净
            await windBotService.StopAllAsync();
            AssertTrue(sessionStopped);
            AssertFalse(botSession.IsRunning);
            AssertTrue(botSession.LastError is null);
            AssertEqual(0, windBotService.GetActiveSessions().Count);

            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && serverHost.ClientCount > 1)
                await Task.Delay(10);
            AssertEqual(1, serverHost.ClientCount);

            observerClient.Close();
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void RoomPasswordParserParsesBuiltInAndCustomDiyTokens()
    {
        RoomPasswordParser parser = new();
        parser.RegisterToken(["BOSS"], static (o, _) =>
        {
            o.StartLp = 16000;
            o.Region = 3;
            o.ExtraScripts.Add("./script/boss_mode.lua");
            o.CustomTags["Mode"] = "Boss";
        });
        parser.RegisterPrefixToken("SKILL:", static (o, arg) => o.CustomTags["Skill"] = arg);

        ParsedRoomPassword complex = parser.Parse("M,DIY,LP4000,MR5,LF2,ST6,DR2,TM180,NC,NS,PR#DiyArena");
        AssertTrue(complex.HasRulePrefix);
        AssertEqual("DiyArena", complex.RoomName);
        GameOptions applied = complex.ApplyTo(new GameOptions());
        AssertEqual(1, applied.Mode);
        AssertEqual(3, applied.Region);
        AssertEqual(4000, applied.StartLp);
        AssertEqual(5, applied.MasterRule);
        AssertEqual(1, applied.Banlist);
        AssertEqual(6, applied.StartHand);
        AssertEqual(2, applied.DrawCount);
        AssertEqual(180, applied.GameTimer);
        AssertTrue(applied.NoCheckDeck);
        AssertTrue(applied.NoShuffleDeck);
        AssertTrue(applied.EnablePriority);

        ParsedRoomPassword tag = parser.Parse("T#TagRoom");
        GameOptions tagOpts = tag.ApplyTo(new GameOptions());
        AssertEqual(2, tagOpts.Mode);
        AssertEqual(16000, tagOpts.StartLp);

        ParsedRoomPassword bo5 = parser.Parse("BO5,NF#Championship");
        GameOptions bo5Opts = bo5.ApplyTo(new GameOptions());
        AssertEqual(1, bo5Opts.Mode);
        AssertEqual(3, bo5Opts.MatchWins);
        AssertEqual(-1, bo5Opts.Banlist);

        ParsedRoomPassword custom = parser.Parse("BOSS,SKILL:DestinyDraw#BossRoom");
        GameOptions customOpts = custom.ApplyTo(new GameOptions());
        AssertEqual(16000, customOpts.StartLp);
        AssertEqual(3, customOpts.Region);
        AssertTrue(customOpts.ExtraScripts.Contains("./script/boss_mode.lua"));
        AssertEqual("Boss", customOpts.CustomTags!["Mode"]);
        AssertEqual("DestinyDraw", customOpts.CustomTags["Skill"]);

        ParsedRoomPassword plain = parser.Parse("PlainRoom");
        AssertFalse(plain.HasRulePrefix);
        AssertEqual("PlainRoom", plain.RoomName);
    }

    [Fact]
    public async Task ServerExtensionsSupportAddonsDeckValidatorChatCommandsAndDuelPreloadAsync()
    {
        Card c1 = new(1001, 4, 0, 0, (int)CardType.Monster, 4, 1, 1, 1800, 1000, 0, 0, 0);
        InMemoryCardRepository repo = new([c1]);
        ServerTestFakeDuelFactory duelFactory = new();
        bool addonInitialized = false;
        bool addonDisposed = false;
        bool beforeDuelStartFired = false;
        bool allowDeck = false;

        ServerOptions options = new()
        {
            EnableNetworkListener = false,
            EnableChatCommands = true,
            ChatCommands =
            [
                new ChatCommandDefinition("ping", "测试命令", ctx => ctx.Reply($"PONG:{ctx.RawArguments}"))
            ],
            AddonFactories =
            [
                game => new ServerTestRecordingAddon(
                    game,
                    () => addonInitialized = true,
                    () => addonDisposed = true,
                    ctx =>
                    {
                        beforeDuelStartFired = true;
                        ctx.Session.AddCard(9990001, 0, CardLocation.Extra);
                    })
            ],
            Game = new GameOptions
            {
                Region = 3,
                DeckRules = new DeckRules(MainDeckMinSize: 1),
                ExtraScripts = ["./script/diy_rule.lua"],
                DeckValidator = new ServerTestDelegateDeckValidator(_ => allowDeck ? 0 : 1001)
            }
        };

        CoreServer server = new(duelFactory, repo, options);
        server.StartEmbedded();
        AssertTrue(addonInitialized);

        YGOClient p1 = server.ConnectLocalClient();
        YGOClient p2 = server.ConnectLocalClient();
        List<(short Sender, string Text)> p1Chats = new();
        List<(short Sender, string Text)> p2Chats = new();
        List<int> p1DeckErrors = new();

        void HookChat(YGOClient client, List<(short, string)> sink, List<int>? deckErrors)
        {
            client.PacketReceivedRaw += pkt =>
            {
                if (pkt.Length == 0)
                    return;
                PacketReader r = new(pkt.Span);
                StocMessage msg = (StocMessage)r.ReadByte();
                if (msg == StocMessage.Chat)
                {
                    short sender = r.ReadInt16();
                    string text = r.ReadUnicodeAutoLength(256);
                    sink.Add((sender, text));
                }
                else if (msg == StocMessage.ErrorMsg && deckErrors is not null)
                {
                    byte errType = r.ReadByte();
                    r.ReadSpan(3);
                    int code = r.ReadInt32();
                    if (errType == 2)
                        deckErrors.Add(code);
                }
            };
        }

        HookChat(p1, p1Chats, p1DeckErrors);
        HookChat(p2, p2Chats, null);

        SendPlayerJoin(p1, "Alice", "DIY,LP6000#");
        SendPlayerJoin(p2, "Bob", "DIY,LP6000#");
        PumpServerAndClients(server, p1, p2);

        AssertEqual(6000, server.Game!.StartLp);
        AssertEqual(3, server.Game.Region);

        // 1. 聊天命令应被拦截，只回执给发送者，不广播给对手
        SendChat(p1, "/ping hello");
        SendChat(p1, "/rules");
        PumpServerAndClients(server, p1, p2);
        AssertTrue(p1Chats.Any(c => c.Text == "PONG:hello"));
        AssertTrue(p1Chats.Any(c => c.Text.Contains("LP=6000") && c.Text.Contains("diy_rule.lua")));
        AssertEqual(0, p2Chats.Count);

        // 普通聊天正常广播
        SendChat(p1, "normal hello");
        PumpServerAndClients(server, p1, p2);
        AssertTrue(p2Chats.Any(c => c.Text == "normal hello"));

        // 2. 自定义 IDeckValidator 拦截不合法卡组并放行合法卡组
        SendSingleCardDeck(p1, 1001);
        SendSingleCardDeck(p2, 1001);
        SendSimpleCtos(p1, CtosMessage.HsReady);
        PumpServerAndClients(server, p1, p2);
        AssertTrue(p1DeckErrors.Contains(1001));
        AssertFalse(server.Game.IsReady[0]);

        allowDeck = true;
        SendSimpleCtos(p1, CtosMessage.HsReady);
        SendSimpleCtos(p2, CtosMessage.HsReady);
        PumpServerAndClients(server, p1, p2);
        SendSimpleCtos(p1, CtosMessage.HsStart);
        PumpServerAndClients(server, p1, p2);

        // 3. 进入猜拳（1=剪刀, 2=石头, 3=布）-> p1 胜出并选先攻 -> 触发 ExtraScripts.PreloadScript 与 OnBeforeDuelStart
        SendByteCtos(p1, CtosMessage.HandResult, 2); // Rock
        SendByteCtos(p2, CtosMessage.HandResult, 1); // Scissors -> p1 wins
        PumpServerAndClients(server, p1, p2);
        SendByteCtos(p1, CtosMessage.TpResult, 1);
        PumpServerAndClients(server, p1, p2);

        AssertTrue(beforeDuelStartFired);
        AssertTrue(duelFactory.LastSession is not null);
        AssertTrue(duelFactory.LastSession!.PreloadedScripts.Contains("./script/diy_rule.lua"));
        AssertTrue(duelFactory.LastSession.AddedCards.Any(c => c.CardId == 9990001 && c.Location == CardLocation.Extra));
        AssertTrue(duelFactory.LastSession.Started);

        server.Stop();
        AssertTrue(addonDisposed);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CoreServerMultiRoomRoutesConcurrentRoomsAndCleansUpAsync()
    {
        ServerOptions options = new()
        {
            EnableNetworkListener = false,
            EnableMultiRoom = true,
            EnableChatCommands = true
        };

        CoreServer server = new(options: options);
        server.StartEmbedded();

        List<string> createdRooms = new();
        List<string> removedRooms = new();
        server.OnRoomCreated += g => createdRooms.Add(g.RoomName);
        server.OnRoomRemoved += g => removedRooms.Add(g.RoomName);

        YGOClient p1 = server.ConnectLocalClient();
        YGOClient p2 = server.ConnectLocalClient();
        YGOClient p3 = server.ConnectLocalClient();

        List<string> p1Messages = new();
        p1.PacketReceivedRaw += pkt =>
        {
            PacketReader r = new(pkt.Span);
            if (r.Remaining > 0 && (StocMessage)r.ReadByte() == StocMessage.Chat)
            {
                r.ReadInt16();
                p1Messages.Add(r.ReadUnicodeAutoLength(256));
            }
        };

        SendPlayerJoin(p1, "Alice", "DIY,LP4000#Alpha");
        SendPlayerJoin(p2, "Bob", "Alpha");
        SendPlayerJoin(p3, "Charlie", "T#Beta");
        PumpServerAndClients(server, p1, p2, p3);

        AssertEqual(2, server.RoomCount);
        AssertTrue(createdRooms.Contains("Alpha"));
        AssertTrue(createdRooms.Contains("Beta"));
        AssertTrue(server.TryGetRoom("Alpha", out Game? alpha));
        AssertTrue(server.TryGetRoom("Beta", out Game? beta));
        AssertEqual(4000, alpha!.StartLp);
        AssertEqual(3, alpha.Region);
        AssertEqual(2, alpha.CreateSnapshot().DuelistCount);
        AssertEqual(2, beta!.Mode);
        AssertEqual(16000, beta.StartLp);
        AssertEqual(1, beta.CreateSnapshot().DuelistCount);

        SendChat(p1, "/rooms");
        PumpServerAndClients(server, p1, p2, p3);
        AssertTrue(p1Messages.Any(m => m.Contains("Alpha") && m.Contains("Beta")));

        // 房主 Charlie 离开 Beta，只关闭 Beta，不关闭整个服务器也不影响 Alpha
        SendSimpleCtos(p3, CtosMessage.LeaveGame);
        PumpServerAndClients(server, p1, p2, p3);

        AssertTrue(server.IsRunning);
        AssertEqual(1, server.RoomCount);
        AssertTrue(removedRooms.Contains("Beta"));
        AssertTrue(server.TryGetRoom("Alpha", out _));
        AssertFalse(server.TryGetRoom("Beta", out _));

        server.Stop();
        AssertEqual(0, server.RoomCount);
        await Task.CompletedTask;
    }

    [Fact]
    public void ServerAutoDiscoversExpansionsAndCliMapsOptions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpAutoExp_" + Guid.NewGuid().ToString("N"));
        string baseScriptDir = Path.Combine(tempDir, "script");
        string expDir = Path.Combine(tempDir, "expansions");
        string diyDir = Path.Combine(tempDir, "diy");
        string diyScriptDir = Path.Combine(diyDir, "script");
        Directory.CreateDirectory(baseScriptDir);
        Directory.CreateDirectory(expDir);
        Directory.CreateDirectory(diyScriptDir);

        try
        {
            CreateTestCardDb(Path.Combine(tempDir, "cards.cdb"),
                [(1001, 1, 0, 0, (int)CardType.Monster, 4, 1, 1, 1500, 1000, "Base", "d")]);
            CreateTestCardDb(Path.Combine(expDir, "exp.cdb"),
                [(1001, 3, 0, 0, (int)CardType.Monster, 4, 1, 1, 2000, 1000, "Exp", "d")]);
            CreateTestCardDb(Path.Combine(diyDir, "custom.cdb"),
                [(9990010, 4, 0, 0, (int)CardType.Monster, 4, 1, 1, 3000, 2000, "Diy", "d")]);

            File.WriteAllText(Path.Combine(tempDir, "lflist.conf"), "!BaseLF\n1001 1\n");
            File.WriteAllText(Path.Combine(diyDir, "lflist.conf"), "!DiyLF\n9990010 3\n");
            File.WriteAllText(Path.Combine(baseScriptDir, "c1001.lua"), "-- base");
            File.WriteAllText(Path.Combine(diyScriptDir, "c1001.lua"), "-- diy");

            ServerOptions options = new()
            {
                RootPath = tempDir,
                EnableNetworkListener = false,
                AutoLoadExpansions = true
            };

            IReadOnlyList<string> dbPaths = YGOProSharpServer.ResolveDatabasePaths(options);
            AssertEqual(3, dbPaths.Count);

            using ServerTestFakeOcgRuntime fakeRuntime = new();
            using YGOProSharpServerHost host = YGOProSharpServer.CreateHost(options, fakeRuntime);
            AssertTrue(fakeRuntime.LastOptions is not null);
            AssertTrue(fakeRuntime.LastOptions!.CardDataProvider.TryGetCardData(1001, out OcgCardData overridden));
            AssertEqual(2000, overridden.Attack);
            AssertTrue(fakeRuntime.LastOptions.CardDataProvider.TryGetCardData(9990010, out OcgCardData customCard));
            AssertEqual(3000, customCard.Attack);
            AssertTrue(fakeRuntime.LastOptions.ScriptProvider.TryGetScript("./script/c1001.lua", out byte[] scriptBytes));
            AssertEqual("-- diy", Encoding.UTF8.GetString(scriptBytes));
            AssertEqual("DiyLF", BanlistManager.Banlists[0].Name);

            // CLI 配置映射验证
            CliConfiguration config = CliConfiguration.Load(
            [
                "EnableMultiRoom=true",
                "MaxRooms=64",
                "AutoLoadExpansions=false",
                "ExpansionDirs=exp1;exp2",
                "ExtraDatabases=diy1.cdb,diy2.cdb",
                "ExtraScriptDirs=diy/script",
                "ExtraScripts=./script/global_diy.lua",
                "EnableChatCommands=false",
                "MatchWins=3"
            ]);
            ServerOptions cliOptions = CliOptionsFactory.CreateServerOptions(config);
            AssertTrue(cliOptions.EnableMultiRoom);
            AssertEqual(64, cliOptions.MaxRooms);
            AssertFalse(cliOptions.AutoLoadExpansions);
            AssertEqual(2, cliOptions.ExpansionDirectories.Count);
            AssertEqual(2, cliOptions.ExtraDatabases.Count);
            AssertEqual(1, cliOptions.ExtraScriptDirectories.Count);
            AssertEqual(1, cliOptions.Game.ExtraScripts.Count);
            AssertFalse(cliOptions.EnableChatCommands);
            AssertEqual(3, cliOptions.Game.MatchWins);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ChatCommandAddonSupportsLiveRuleEditsAndPermissionsAsync()
    {
        BanlistManager.Init([]);
        Banlist b1 = new("TestLF1");
        b1.Add(1001, 1);
        BanlistManager.AddBanlist(b1);

        CoreServer server = new(options: new ServerOptions
        {
            EnableNetworkListener = false,
            EnableChatCommands = true
        });
        server.StartEmbedded();

        YGOClient hostClient = server.ConnectLocalClient();
        YGOClient guestClient = server.ConnectLocalClient();

        List<string> hostChats = new();
        List<string> guestChats = new();
        int hostJoinGamePackets = 0;

        hostClient.PacketReceivedRaw += pkt =>
        {
            PacketReader r = new(pkt.Span);
            if (r.Remaining == 0) return;
            StocMessage msg = (StocMessage)r.ReadByte();
            if (msg == StocMessage.Chat)
            {
                r.ReadInt16();
                hostChats.Add(r.ReadUnicodeAutoLength(256));
            }
            else if (msg == StocMessage.JoinGame)
            {
                hostJoinGamePackets++;
            }
        };
        guestClient.PacketReceivedRaw += pkt =>
        {
            PacketReader r = new(pkt.Span);
            if (r.Remaining == 0) return;
            if ((StocMessage)r.ReadByte() == StocMessage.Chat)
            {
                r.ReadInt16();
                guestChats.Add(r.ReadUnicodeAutoLength(256));
            }
        };

        SendPlayerJoin(hostClient, "Host", "");
        SendPlayerJoin(guestClient, "Guest", "");
        PumpServerAndClients(server, hostClient, guestClient);
        int initialJoinCount = hostJoinGamePackets;

        // 非房主尝试修改规则应被拒绝
        SendChat(guestClient, "/lp 4000");
        PumpServerAndClients(server, hostClient, guestClient);
        AssertEqual(Game.DEFAULT_LIFEPOINTS, server.Game!.StartLp);
        AssertTrue(guestChats.Any(m => m.Contains("仅房主")));

        // 房主实时修改各项规则与 DIY 脚本，并同步 StocMessage.JoinGame 到客户端
        SendChat(hostClient, "/lp 12000");
        SendChat(hostClient, "/mr 4");
        SendChat(hostClient, "/region diy");
        SendChat(hostClient, "/nocheck on");
        SendChat(hostClient, "/noshuffle on");
        SendChat(hostClient, "/hand 6");
        SendChat(hostClient, "/draw 2");
        SendChat(hostClient, "/timer 180");
        SendChat(hostClient, "/banlist none");
        SendChat(hostClient, "/script add ./script/custom_boss.lua");
        SendChat(hostClient, "/roll 20");
        SendChat(hostClient, "/coin");
        SendChat(hostClient, "/help lp");
        PumpServerAndClients(server, hostClient, guestClient);

        AssertEqual(12000, server.Game.StartLp);
        AssertEqual(4, server.Game.MasterRule);
        AssertEqual(3, server.Game.Region);
        AssertTrue(server.Game.NoCheckDeck);
        AssertTrue(server.Game.NoShuffleDeck);
        AssertEqual(6, server.Game.StartHand);
        AssertEqual(2, server.Game.DrawCount);
        AssertEqual(180, server.Game.Timer);
        AssertTrue(server.Game.Banlist is null);
        AssertTrue(server.Game.ExtraScripts.Contains("./script/custom_boss.lua"));
        AssertTrue(hostJoinGamePackets > initialJoinCount);
        AssertTrue(guestChats.Any(m => m.Contains("[Roll]") && m.Contains("1~20")));
        AssertTrue(guestChats.Any(m => m.Contains("[Coin]")));
        AssertTrue(hostChats.Any(m => m.Contains("[Help]") && m.Contains("/lp")));

        // 房主一键 /preset 套用规则 + 切回禁限表 + 踢人
        SendChat(hostClient, "/preset M,OT,LP8000,MR5");
        SendChat(hostClient, "/banlist TestLF1");
        SendChat(hostClient, "/kick Guest");
        PumpServerAndClients(server, hostClient, guestClient);

        AssertEqual(1, server.Game.Mode);
        AssertEqual(2, server.Game.Region);
        AssertEqual(8000, server.Game.StartLp);
        AssertEqual(5, server.Game.MasterRule);
        AssertTrue(server.Game.Banlist is not null);
        AssertEqual("TestLF1", server.Game.Banlist!.Name);
        AssertEqual(1, server.Game.CreateSnapshot().DuelistCount);

        server.Stop();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ServerRandomDeckMatchSidingSuddenDeathAndMaintenanceModeWorkAsync()
    {
        List<Card> cards = new();
        for (int i = 1; i <= 20; i++)
            cards.Add(TestCard(1000 + i, (int)CardType.Monster | (int)CardType.Normal, level: 4, race: 1, attribute: 1, attack: 1900, defense: 1000));
        ICardRepository repo = TestRepository(cards.ToArray());

        InMemoryScriptProvider stubScripts = new();
        stubScripts.RegisterText("constant.lua", "-- stub");
        stubScripts.RegisterText("utility.lua", "-- stub");
        stubScripts.RegisterText("procedure.lua", "-- stub");

        using NativeOcgRuntime runtime = new();
        runtime.Initialize(new OcgRuntimeOptions(".", "script", "cards.cdb", new RepositoryCardDataProvider(repo), stubScripts));

        CoreServer server = new(runtime.DuelFactory, repo, new ServerOptions
        {
            EnableNetworkListener = false,
            EnableMultiRoom = true,
            EnableChatCommands = true,
            Game = new GameOptions
            {
                Banlist = -1,
                DeckRules = new DeckRules(MainDeckMinSize: 10, MainDeckMaxSize: 40)
            }
        });
        server.StartEmbedded();

        // 1. 控制面预创建 BO3 + 镜像混沌随机 + 额外回合猝死（ED）房间
        ParsedRoomPassword parsed = server.PasswordParser.Parse("M,RDM,ED#RandomMatch01");
        AssertEqual((int)RandomDeckMode.MirrorRandom, (int)parsed.Overrides.RandomDeck!.Value);
        AssertTrue(parsed.Overrides.SuddenDeath == true);

        Game room = server.CreateRoom("RandomMatch01", parsed.ApplyTo(server.Options.Game) with { ChaosMainDeckSize = 15 });
        AssertEqual((int)RandomDeckMode.MirrorRandom, (int)room.RandomDeckMode);
        AssertTrue(room.IsSuddenDeath);

        YGOClient p0 = server.ConnectLocalClient();
        YGOClient p1 = server.ConnectLocalClient();
        List<string> p0Chats = new();
        int suddenDeathWins = 0;

        p0.PacketReceivedRaw += pkt =>
        {
            if (pkt.Length == 0)
                return;
            PacketReader r = new(pkt.Span);
            StocMessage stoc = (StocMessage)r.ReadByte();
            if (stoc == StocMessage.Chat)
            {
                r.ReadInt16();
                p0Chats.Add(r.ReadUnicodeAutoLength(256));
            }
            else if (stoc == StocMessage.GameMsg && r.Remaining >= 3)
            {
                GameMessage gm = (GameMessage)r.ReadByte();
                if (gm == GameMessage.Win)
                {
                    r.ReadByte();
                    int reason = r.ReadByte();
                    if (reason == Game.WIN_REASON_SUDDEN_DEATH)
                        suddenDeathWins++;
                }
            }
        };

        SendPlayerJoin(p0, "PlayerA", "RandomMatch01");
        SendPlayerJoin(p1, "PlayerB", "RandomMatch01");
        PumpServerAndClients(server, p0, p1);

        // 2. 双方不上传任何本地卡组直接点准备并开局（服务端随机卡组自动放行并注入对称镜像构筑）
        SendSimpleCtos(p0, CtosMessage.HsReady);
        SendSimpleCtos(p1, CtosMessage.HsReady);
        PumpServerAndClients(server, p0, p1);
        AssertTrue(room.IsReady[0] && room.IsReady[1]);

        SendSimpleCtos(p0, CtosMessage.HsStart);
        PumpServerAndClients(server, p0, p1);
        SendByteCtos(p0, CtosMessage.HandResult, 2);
        SendByteCtos(p1, CtosMessage.HandResult, 1);
        PumpServerAndClients(server, p0, p1);
        SendByteCtos(p0, CtosMessage.TpResult, 1);
        PumpServerAndClients(server, p0, p1);

        AssertEqual((int)GameState.Duel, (int)room.State);
        AssertEqual(15, room.Players[0].Deck.Main.Count);
        AssertTrue(room.Players[0].Deck.Check(room.Players[1].Deck));
        AssertTrue(p0Chats.Any(c => c.Contains("[RandomDeck] 本局镜像随机卡组")));

        // 3. Game 1 投降进入 Match 换副阶段（GameState.Side），客户端提交任意本地 1 张卡包不报 ErrorMsg(3)，无缝进入 Game 2
        SendSimpleCtos(p0, CtosMessage.Surrender);
        PumpServerAndClients(server, p0, p1);
        AssertEqual((int)GameState.Side, (int)room.State);

        SendSingleCardDeck(p0, 1001);
        SendSingleCardDeck(p1, 1001);
        PumpServerAndClients(server, p0, p1);
        AssertEqual((int)GameState.Starting, (int)room.State);

        SendByteCtos(p0, CtosMessage.TpResult, 1);
        PumpServerAndClients(server, p0, p1);
        AssertEqual((int)GameState.Duel, (int)room.State);

        // 4. 在对局中触发 /death 额外回合猝死规则，制造 LP 差并推进回合，验证自动按 LP 高低判胜 (reason = 0x11)
        SendChat(p0, "/death on 1");
        PumpServerAndClients(server, p0, p1);
        room.LifePoints[0] = 6000;
        room.LifePoints[1] = 8000;

        using (MemoryStream endTurn = new())
        using (BinaryWriter w = new(endTurn))
        {
            w.Write((byte)CtosMessage.Response);
            w.Write(7); // End Turn -> 进入下一回合触发 SuddenDeath 结算
            p0.Send(endTurn.ToArray());
        }
        PumpServerAndClients(server, p0, p1);

        AssertEqual(1, suddenDeathWins);
        AssertEqual((int)GameState.End, (int)room.State);
        AssertEqual(1, room.Winner);

        // 5. 开启停服维护开关（stop），新玩家入房收到维护黄字提示并被安全拦截
        server.SetMaintenanceMode(true, "例行维护");
        YGOClient lateClient = server.ConnectLocalClient();
        List<string> lateChats = new();
        lateClient.PacketReceivedRaw += pkt =>
        {
            PacketReader r = new(pkt.Span);
            if (r.Remaining > 0 && (StocMessage)r.ReadByte() == StocMessage.Chat)
            {
                r.ReadInt16();
                lateChats.Add(r.ReadUnicodeAutoLength(256));
            }
        };
        SendPlayerJoin(lateClient, "LateUser", "NewRoomAfterStop");
        PumpServerAndClients(server, lateClient);
        AssertTrue(lateChats.Any(m => m.Contains("维护模式") && m.Contains("例行维护")));
        AssertFalse(server.TryGetRoom("NewRoomAfterStop", out _));

        server.Stop();
        await Task.CompletedTask;
    }

}
