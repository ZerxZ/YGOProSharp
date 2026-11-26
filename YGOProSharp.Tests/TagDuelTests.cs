namespace YGOProSharp.Tests;

public sealed class TagDuelTests
{
    [Fact]
    public async Task TagDuelLobbyModeSwitchSeatSwapAndCliMappingWorksAsync()
    {
        RoomPasswordParser parser = new();
        GameOptions tag2v2 = parser.Parse("2V2#Arena").ApplyTo(new GameOptions());
        AssertEqual(2, tag2v2.Mode);
        AssertEqual(16000, tag2v2.StartLp);

        GameOptions tag2vs2 = parser.Parse("2VS2#Arena").ApplyTo(new GameOptions());
        AssertEqual(2, tag2vs2.Mode);
        AssertEqual(16000, tag2vs2.StartLp);

        GameOptions backToSingle = parser.Parse("1V1#Arena").ApplyTo(tag2v2);
        AssertEqual(0, backToSingle.Mode);
        AssertEqual(8000, backToSingle.StartLp);

        ServerOptions cli2v2 = CliOptionsFactory.CreateServerOptions(CliConfiguration.Load(["Mode=2v2"]));
        AssertEqual(2, cli2v2.Game.Mode);
        AssertEqual(16000, cli2v2.Game.StartLp);

        ServerOptions cli2v2CustomLp = CliOptionsFactory.CreateServerOptions(CliConfiguration.Load(["Mode=tag", "StartLp=12000"]));
        AssertEqual(2, cli2v2CustomLp.Game.Mode);
        AssertEqual(12000, cli2v2CustomLp.Game.StartLp);

        CoreServer server = new(options: new ServerOptions
        {
            EnableNetworkListener = false,
            EnableChatCommands = true
        });
        server.StartEmbedded();

        YGOClient p0 = server.ConnectLocalClient();
        YGOClient p1 = server.ConnectLocalClient();
        List<string> p0Chats = new();
        p0.PacketReceivedRaw += pkt =>
        {
            PacketReader r = new(pkt.Span);
            if (r.Remaining > 0 && (StocMessage)r.ReadByte() == StocMessage.Chat)
            {
                r.ReadInt16();
                p0Chats.Add(r.ReadUnicodeAutoLength(256));
            }
        };

        SendPlayerJoin(p0, "HostA", "");
        SendPlayerJoin(p1, "GuestB", "");
        PumpServerAndClients(server, p0, p1);

        AssertEqual(0, server.Game!.Mode);
        AssertEqual(8000, server.Game.StartLp);
        AssertEqual(2, server.Game.Players.Length);

        // 房主在大厅切换为 2v2 双打，席位自动扩展为 4，默认 LP 升至 16000
        SendChat(p0, "/mode 2v2");
        PumpServerAndClients(server, p0, p1);
        AssertEqual(2, server.Game.Mode);
        AssertTrue(server.Game.IsTag);
        AssertEqual(16000, server.Game.StartLp);
        AssertEqual(4, server.Game.Players.Length);

        // 玩家使用 /swap 在空余 2v2 席位间切换队伍（槽位 1 -> 槽位 2 [B队]）
        SendChat(p1, "/swap");
        SendChat(p0, "/room");
        PumpServerAndClients(server, p0, p1);
        AssertTrue(server.Game.Players[1] is null);
        AssertTrue(server.Game.Players[2] is not null && server.Game.Players[2].Name == "GuestB");
        AssertTrue(p0Chats.Any(m => m.Contains("[A队]") && m.Contains("[B队]")));

        // B 队槽位 2 有人时禁止直接缩回 1v1
        SendChat(p0, "/mode single");
        PumpServerAndClients(server, p0, p1);
        AssertEqual(2, server.Game.Mode);

        // GuestB 用 /swap 循环回到槽位 1（2 -> 3 -> 1）后可切回 1v1，LP 自动恢复 8000
        SendChat(p1, "/swap");
        SendChat(p1, "/swap");
        PumpServerAndClients(server, p0, p1);
        AssertTrue(server.Game.Players[1] is not null && server.Game.Players[1].Name == "GuestB");

        SendChat(p0, "/mode 1v1");
        PumpServerAndClients(server, p0, p1);
        AssertEqual(0, server.Game.Mode);
        AssertFalse(server.Game.IsTag);
        AssertEqual(8000, server.Game.StartLp);
        AssertEqual(2, server.Game.Players.Length);

        server.Stop();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TagDuelSwappedTurnOrderFogOfWarAndTeammateSurrenderAsync()
    {
        Card[] cards =
        [
            TestCard(1001, (int)CardType.Monster | (int)CardType.Normal, level: 4, race: 1, attribute: 1, attack: 1800, defense: 1000),
            TestCard(1002, (int)CardType.Monster | (int)CardType.Normal, level: 4, race: 1, attribute: 1, attack: 1800, defense: 1000),
            TestCard(1003, (int)CardType.Monster | (int)CardType.Normal, level: 4, race: 1, attribute: 1, attack: 1800, defense: 1000),
            TestCard(1004, (int)CardType.Monster | (int)CardType.Normal, level: 4, race: 1, attribute: 1, attack: 1800, defense: 1000),
            TestCard(2001, (int)CardType.Monster | (int)CardType.Fusion, level: 6, race: 1, attribute: 1, attack: 2400, defense: 1500),
            TestCard(2002, (int)CardType.Monster | (int)CardType.Fusion, level: 6, race: 1, attribute: 1, attack: 2400, defense: 1500),
            TestCard(2003, (int)CardType.Monster | (int)CardType.Fusion, level: 6, race: 1, attribute: 1, attack: 2400, defense: 1500),
            TestCard(2004, (int)CardType.Monster | (int)CardType.Fusion, level: 6, race: 1, attribute: 1, attack: 2400, defense: 1500)
        ];
        ICardRepository repo = TestRepository(cards);
        InMemoryScriptProvider stubScripts = new();
        stubScripts.RegisterText("constant.lua", "-- stub");
        stubScripts.RegisterText("utility.lua", "-- stub");
        stubScripts.RegisterText("procedure.lua", "-- stub");

        using NativeOcgRuntime runtime = new();
        runtime.Initialize(new OcgRuntimeOptions(".", "script", "cards.cdb", new RepositoryCardDataProvider(repo), stubScripts));

        ServerOptions options = new()
        {
            EnableNetworkListener = false,
            Game = new GameOptions
            {
                Mode = 2,
                StartLp = 16000,
                StartHand = 2,
                DrawCount = 1,
                NoCheckDeck = true
            }
        };

        CoreServer server = new(runtime.DuelFactory, repo, options);
        server.StartEmbedded();

        YGOClient[] clients =
        [
            server.ConnectLocalClient(), // P0 (Lobby Team 0, mainCard=1001, extra=2001)
            server.ConnectLocalClient(), // P1 (Lobby Team 0, mainCard=1002, extra=2002)
            server.ConnectLocalClient(), // P2 (Lobby Team 1, mainCard=1003, extra=2003)
            server.ConnectLocalClient()  // P3 (Lobby Team 1, mainCard=1004, extra=2004)
        ];
        YGOClient observer = server.ConnectLocalClient();

        List<int>[] drawnCodes = [new(), new(), new(), new()];
        List<int>[] tagSwapHandCodes = [new(), new(), new(), new()];
        List<int> idlePromptOrder = new();
        int[] teammateSurrenderCount = new int[4];
        int observerStartType = -1;
        byte[]? capturedReplay = null;
        int winSideSeenByP0 = -1;

        for (int idx = 0; idx < 4; idx++)
        {
            int clientIdx = idx;
            clients[clientIdx].PacketReceivedRaw += pkt =>
            {
                if (pkt.Length == 0)
                    return;
                PacketReader r = new(pkt.Span);
                StocMessage stoc = (StocMessage)r.ReadByte();
                if (stoc == StocMessage.TeammateSurrender)
                {
                    teammateSurrenderCount[clientIdx]++;
                }
                else if (stoc == StocMessage.Replay && clientIdx == 0)
                {
                    capturedReplay = r.ReadRemainingBytes().ToArray();
                }
                else if (stoc == StocMessage.GameMsg && r.Remaining > 0)
                {
                    GameMessage gm = (GameMessage)r.ReadByte();
                    if (gm == GameMessage.Draw)
                    {
                        r.ReadByte(); // player
                        int count = r.ReadByte();
                        for (int k = 0; k < count; k++)
                            drawnCodes[clientIdx].Add(r.ReadInt32());
                    }
                    else if (gm == GameMessage.TagSwap)
                    {
                        r.ReadByte(); // player
                        r.ReadByte(); // mcount
                        int ecount = r.ReadByte();
                        r.ReadByte(); // pcount
                        int hcount = r.ReadByte();
                        r.ReadInt32(); // topcode
                        for (int k = 0; k < hcount; k++)
                            tagSwapHandCodes[clientIdx].Add(r.ReadInt32());
                        for (int k = 0; k < ecount; k++)
                            r.ReadInt32();
                    }
                    else if (gm == GameMessage.SelectIdleCmd)
                    {
                        idlePromptOrder.Add(clientIdx);
                    }
                    else if (gm == GameMessage.Win && clientIdx == 0)
                    {
                        winSideSeenByP0 = r.ReadByte();
                    }
                }
            };
        }

        observer.PacketReceivedRaw += pkt =>
        {
            if (pkt.Length >= 3 && pkt.Span[0] == (byte)StocMessage.GameMsg && pkt.Span[1] == (byte)GameMessage.Start)
                observerStartType = pkt.Span[2];
        };

        string[] names = ["Alice", "Bob", "Carol", "Dave"];
        int[] mainCards = [1001, 1002, 1003, 1004];
        int[] extraCards = [2001, 2002, 2003, 2004];
        for (int i = 0; i < 4; i++)
            SendPlayerJoin(clients[i], names[i], "");
        SendPlayerJoin(observer, "Watcher", "");
        PumpServerAndClients(server, [.. clients, observer]);

        AssertEqual(4, server.Game!.CreateSnapshot().DuelistCount);
        AssertEqual(1, server.Game.Observers.Count);

        for (int i = 0; i < 4; i++)
        {
            using MemoryStream ms = new();
            using BinaryWriter w = new(ms);
            w.Write((byte)CtosMessage.UpdateDeck);
            w.Write(11); // 10 main + 1 extra
            w.Write(0);
            for (int k = 0; k < 10; k++)
                w.Write(mainCards[i]);
            w.Write(extraCards[i]);
            clients[i].Send(ms.ToArray());
            SendSimpleCtos(clients[i], CtosMessage.HsReady);
        }
        PumpServerAndClients(server, [.. clients, observer]);

        SendSimpleCtos(clients[0], CtosMessage.HsStart);
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual((int)GameState.Hand, (int)server.Game.State);

        // 2v2 仅每队首位（P0 与 P2）猜拳；令 B 队（P2 Carol）获胜并选择先攻（触发 IsDuelSwapped = true）
        SendByteCtos(clients[0], CtosMessage.HandResult, 1); // 剪刀
        SendByteCtos(clients[2], CtosMessage.HandResult, 2); // 石头 -> P2 胜出
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual((int)GameState.Starting, (int)server.Game.State);

        SendByteCtos(clients[2], CtosMessage.TpResult, 1); // B 队选先攻
        PumpServerAndClients(server, [.. clients, observer]);

        AssertEqual((int)GameState.Duel, (int)server.Game.State);
        AssertTrue(server.Game.IsDuelSwapped);
        AssertEqual(0x11, observerStartType);

        // Turn 1：B 队先攻，P2 (Carol, 1003) 为 Side 0 首发，A 队二号位 P1 (Bob, 1002) 为 Side 1 Turn 1 待命执子；
        // 非执子队友 P3 (Dave) 与 P0 (Alice) 收到的全部 MSG_DRAW 卡密均必须被遮蔽为 0！
        AssertEqual(1, idlePromptOrder.Count);
        AssertEqual(2, idlePromptOrder[0]);
        AssertTrue(drawnCodes[2].Contains(1003));
        AssertFalse(drawnCodes[2].Contains(1002));
        AssertTrue(drawnCodes[1].Contains(1002));
        AssertFalse(drawnCodes[1].Contains(1003));
        AssertTrue(drawnCodes[0].All(c => c == 0));
        AssertTrue(drawnCodes[3].All(c => c == 0));

        static void SendEndTurnResponse(YGOClient c)
        {
            using MemoryStream ms = new();
            using BinaryWriter w = new(ms);
            w.Write((byte)CtosMessage.Response);
            w.Write(7); // IDLECMD: To End Phase
            c.Send(ms.ToArray());
        }

        // 推进 Turn 1 (P2) -> Turn 2 (P0) -> Turn 3 (P3) -> Turn 4 (P1)，验证标准 0->2->1->3 换位顺序与 MSG_TAG_SWAP 队友手牌遮蔽
        SendEndTurnResponse(clients[2]);
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual(2, idlePromptOrder.Count);
        AssertEqual(0, idlePromptOrder[1]); // Turn 2 轮到 A 队首发 P0 (Alice)
        AssertTrue(tagSwapHandCodes[0].All(c => c == 1001));
        AssertTrue(tagSwapHandCodes[1].All(c => c == 0)); // 队友 Bob 收到的 MSG_TAG_SWAP 手牌必须全为 0

        SendEndTurnResponse(clients[0]);
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual(3, idlePromptOrder.Count);
        AssertEqual(3, idlePromptOrder[2]); // Turn 3 轮到 B 队二号位 P3 (Dave)
        AssertTrue(tagSwapHandCodes[3].Contains(1004));
        AssertFalse(tagSwapHandCodes[3].Contains(1003));
        AssertTrue(tagSwapHandCodes[2].All(c => c == 0)); // 队友 Carol 在 Turn 2 与 Turn 3 收到的 MSG_TAG_SWAP 手牌均被遮蔽为 0

        SendEndTurnResponse(clients[3]);
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual(4, idlePromptOrder.Count);
        AssertEqual(1, idlePromptOrder[3]); // Turn 4 轮到 A 队二号位 P1 (Bob)
        AssertTrue(tagSwapHandCodes[1].Contains(1002));

        // 验证 2v2 双打投降确认机制：单人点击投降先发 STOC_TEAMMATE_SURRENDER，队友确认后才正式判负
        SendSimpleCtos(clients[0], CtosMessage.Surrender);
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual((int)GameState.Duel, (int)server.Game.State);
        AssertEqual(1, teammateSurrenderCount[0]);
        AssertEqual(1, teammateSurrenderCount[1]);
        AssertEqual(0, teammateSurrenderCount[2]);

        // 同一人重复点击投降不直接结束
        SendSimpleCtos(clients[0], CtosMessage.Surrender);
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual((int)GameState.Duel, (int)server.Game.State);

        // 队友 P1 确认投降 -> B 队（duelSide 0 / Lobby Team 1）获胜，生成带 FlagTag 的录像
        SendSimpleCtos(clients[1], CtosMessage.Surrender);
        PumpServerAndClients(server, [.. clients, observer]);
        AssertEqual((int)GameState.End, (int)server.Game.State);
        AssertEqual(0, winSideSeenByP0); // B 队在本局是先攻方 duelSide 0
        AssertEqual(1, server.Game.Winner); // B 队在大厅是 Team 1
        AssertTrue(capturedReplay is not null && capturedReplay.Length > 32);
        uint replayFlag = BinaryPrimitives.ReadUInt32LittleEndian(capturedReplay.AsSpan(8, 4));
        AssertTrue((replayFlag & Replay.FlagTag) != 0);

        server.Stop();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FourWindBotsPlayTwoVsTwoTagDuelInMemoryAsync()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpTagWindBot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Decks"));

        try
        {
            string[] deckLines = Enumerable.Repeat("1001", 40).ToArray();
            File.WriteAllLines(Path.Combine(tempDir, "Decks", "AI_Yubel.ydk"), deckLines);
            Environment.CurrentDirectory = tempDir;

            Card beatstick = TestCard(1001, (int)CardType.Monster | (int)CardType.Normal, level: 4, race: 1, attribute: 1, attack: 2000, defense: 1000);
            NamedCard namedBeatstick = TestNamedCard(
                1001,
                type: (int)CardType.Monster | (int)CardType.Normal,
                level: 4,
                attack: 2000,
                defense: 1000,
                race: 1,
                attribute: 1,
                name: "TagBeatstick");

            ICardRepository serverRepo = TestRepository(beatstick);
            INamedCardRepository botRepo = TestNamedRepository(namedBeatstick);
            InMemoryScriptProvider stubScripts = new();
            stubScripts.RegisterText("constant.lua", "-- stub");
            stubScripts.RegisterText("utility.lua", "-- stub");
            stubScripts.RegisterText("procedure.lua", "-- stub");

            using NativeOcgRuntime runtime = new();
            runtime.Initialize(new OcgRuntimeOptions(".", "script", "cards.cdb", new RepositoryCardDataProvider(serverRepo), stubScripts));

            ServerOptions serverOptions = new()
            {
                EnableNetworkListener = false,
                ClientVersion = 0x1362,
                Game = new GameOptions
                {
                    Mode = 2,
                    MasterRule = 5,
                    StartLp = 2000,
                    NoCheckDeck = true
                }
            };

            await using YGOProSharpServerHost serverHost = new(runtime.DuelFactory, serverRepo, serverOptions);
            serverHost.StartBackgroundLoop(tickDelayMilliseconds: 1, embedded: true);

            await using WindBot.WindBotService windBotService = new(new WindBot.WindBotRuntimeOptions
            {
                CardRepository = botRepo,
                TickDelayMilliseconds = 2
            });

            List<WindBot.WindBotSession> sessions = new(4);
            for (int i = 0; i < 4; i++)
            {
                WindBot.WindBotSession session = windBotService.StartBot(
                    new WindBot.WindBotInfo
                    {
                        Name = $"TagBot{i + 1}",
                        Deck = "AI_Yubel",
                        Chat = false,
                        Hand = 0, // 使用 AI 动态随机猜拳，避免并发入座顺序变化时固定出拳死循环平局
                        Version = 0x1362
                    },
                    serverHost.CreateLocalTransport());
                sessions.Add(session);
            }

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline && serverHost.Server.Game!.State != GameState.End)
            {
                foreach (WindBot.WindBotSession s in sessions)
                {
                    if (s.LastError is not null)
                        throw new InvalidOperationException($"WindBot faulted during 2v2 Tag duel: {s.LastError}", s.LastError);
                }
                await Task.Delay(15);
            }

            foreach (WindBot.WindBotSession s in sessions)
                AssertTrue(s.LastError is null);

            AssertEqual((int)GameState.End, (int)serverHost.Server.Game!.State);
            AssertTrue(serverHost.Server.Game.TurnCount >= 2);
            AssertTrue(serverHost.Server.Game.Winner == 0 || serverHost.Server.Game.Winner == 1);

            foreach (WindBot.WindBotSession s in sessions)
                s.Dispose();
            await windBotService.StopAllAsync();
            await serverHost.StopAsync();
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

}
