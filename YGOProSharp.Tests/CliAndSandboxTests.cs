namespace YGOProSharp.Tests;

public sealed class CliAndSandboxTests
{
    [Fact]
    public void CliMapsServerOptions()
    {
        CliConfiguration configuration = CliConfiguration.Load([
            "RootPath=.",
            "ScriptDirectory=scripts",
            "DatabaseFile=cards-ja.cdb",
            "BanlistFile=custom-lflist.conf",
            "Port=7922",
            "ClientVersion=0x1362",
            "StandardStreamProtocol=true",
            "Rule=1",
            "MasterRule=5",
            "Mode=2",
            "StartLp=4000",
            "StartHand=4",
            "DrawCount=2",
            "GameTimer=120",
            "NoCheckDeck=true",
            "NoShuffleDeck=true",
            "MainDeckMinSize=1",
            "MainDeckMaxSize=80",
            "ExtraDeckMaxSize=20",
            "SideDeckMaxSize=20"
        ]);

        ServerOptions options = CliOptionsFactory.CreateServerOptions(configuration);

        AssertEqual(".", options.RootPath);
        AssertEqual("scripts", options.ScriptDirectory);
        AssertEqual("cards-ja.cdb", options.DatabaseFile);
        AssertEqual("custom-lflist.conf", options.BanlistFile);
        AssertEqual(7922, options.Port);
        AssertEqual(0x1362U, options.ClientVersion);
        AssertTrue(options.StandardStreamProtocol);
        AssertEqual(1, options.Game.Region);
        AssertEqual(5, options.Game.MasterRule);
        AssertEqual(2, options.Game.Mode);
        AssertEqual(4000, options.Game.StartLp);
        AssertEqual(4, options.Game.StartHand);
        AssertEqual(2, options.Game.DrawCount);
        AssertEqual(120, options.Game.GameTimer);
        AssertTrue(options.Game.NoCheckDeck);
        AssertTrue(options.Game.NoShuffleDeck);
        AssertEqual(1, options.Game.DeckRules.MainDeckMinSize);
        AssertEqual(80, options.Game.DeckRules.MainDeckMaxSize);
        AssertEqual(20, options.Game.DeckRules.ExtraDeckMaxSize);
        AssertEqual(20, options.Game.DeckRules.SideDeckMaxSize);
    }

    [Fact]
    public void CliMapsWindBotOptions()
    {
        INamedCardRepository repository = TestNamedRepository(TestNamedCard(1));
        CliConfiguration configuration = CliConfiguration.Load([
            "Name=Bot",
            "Deck=AI",
            "DeckFile=Smoke.ydk",
            "Dialog=default",
            "Host=127.0.0.1",
            "Port=7911",
            "HostInfo=abc",
            "Version=4962",
            "Hand=1",
            "Debug=true",
            "Chat=false",
            "TickDelayMilliseconds=5",
            "ServerPort=2400",
            "BotListPath=bots.json"
        ]);

        WindBot.WindBotInfo info = CliOptionsFactory.CreateWindBotInfo(configuration);
        WindBot.WindBotRuntimeOptions runtimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(configuration, repository);
        WindBot.WindBotServerModeOptions serverModeOptions = CliOptionsFactory.CreateWindBotServerModeOptions(configuration, runtimeOptions);

        AssertEqual("Bot", info.Name);
        AssertEqual("AI", info.Deck);
        AssertEqual("Smoke.ydk", info.DeckFile);
        AssertEqual("default", info.Dialog);
        AssertEqual("127.0.0.1", info.Host);
        AssertEqual(7911, info.Port);
        AssertEqual("abc", info.HostInfo);
        AssertEqual(4962, info.Version);
        AssertEqual(1, info.Hand);
        AssertTrue(info.Debug);
        AssertFalse(info.Chat);
        AssertEqual(5, runtimeOptions.TickDelayMilliseconds);
        AssertEqual(2400, serverModeOptions.ServerPort);
        AssertEqual("bots.json", serverModeOptions.BotListPath);
        if (!ReferenceEquals(repository, runtimeOptions.CardRepository))
            throw new InvalidOperationException("WindBot runtime options should keep the repository supplied by CLI.");
    }

    [Fact]
    public void CliMapsEmbeddedServerBotOptions()
    {
        CliConfiguration defaultConfig = CliConfiguration.Load(["Port=7911"]);
        AssertFalse(CliOptionsFactory.ShouldStartServerBot(defaultConfig));

        CliConfiguration botDeckConfig = CliConfiguration.Load([
            "Port=7922",
            "ClientVersion=0x1362",
            "BotName=LuaBot",
            "BotDeck=ExampleOldSchool",
            "BotChat=false",
            "BotDebug=true"
        ]);

        ServerOptions serverOptions = CliOptionsFactory.CreateServerOptions(botDeckConfig);
        AssertTrue(CliOptionsFactory.ShouldStartServerBot(botDeckConfig));

        WindBot.WindBotInfo botInfo = CliOptionsFactory.CreateServerBotInfo(botDeckConfig, serverOptions);
        AssertEqual("LuaBot", botInfo.Name);
        AssertEqual("ExampleOldSchool", botInfo.Deck);
        AssertEqual(7922, botInfo.Port);
        AssertEqual(0x1362, botInfo.Version);
        AssertFalse(botInfo.Chat);
        AssertTrue(botInfo.Debug);
    }

    [Fact]
    public void CliNLuaRuntimeExecutesExampleScriptAndLua54Features()
    {
        string root = FindRepositoryRoot();
        string luaDecksDir = Path.Combine(root, "YGOProSharp.WindBot", "Decks", "Lua");

        // 1. 验证 CLI 默认自动装配 NLuaRuntime 并解析自定义 LuaDeckDir 与开关
        INamedCardRepository cardRepo = TestNamedRepository(
            TestNamedCard(1000, attack: 1000, defense: 1000),
            TestNamedCard(1001, attack: 500, defense: 500),
            TestNamedCard(1002, attack: 2500, defense: 2000));

        CliConfiguration cliConfig = CliConfiguration.Load([
            $"LuaDeckDir={luaDecksDir}",
            "PreferLuaDecks=true",
            "ThrowOnLuaScriptError=true"
        ]);
        WindBot.WindBotRuntimeOptions cliRuntimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(cliConfig, cardRepo);
        AssertTrue(cliRuntimeOptions.LuaRuntime is NLuaRuntime);
        AssertTrue(cliRuntimeOptions.LuaScriptProvider is WindBot.Game.AI.Lua.FileSystemLuaDeckScriptProvider);
        AssertTrue(cliRuntimeOptions.PreferLuaDecks);
        AssertTrue(cliRuntimeOptions.ThrowOnLuaScriptError);

        CliConfiguration disabledConfig = CliConfiguration.Load(["EnableLuaDecks=false"]);
        WindBot.WindBotRuntimeOptions disabledOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(disabledConfig, cardRepo);
        AssertTrue(disabledOptions.LuaRuntime is null);

        // 2. 使用真实 NLua 5.4 运行时加载并执行仓库自带的 ExampleOldSchool.lua
        WindBot.Game.Duel duel = new();
        duel.Fields[0].Init(40, 15, 0);
        duel.Fields[1].Init(40, 15, 1);
        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo { Deck = "ExampleOldSchool" }, cardRepo);
        WindBot.Game.GameAI ai = new(client, duel);

        try
        {
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.WindBotRuntime.Configure(cliRuntimeOptions);

            using WindBot.Game.AI.Lua.LuaDeckExecutor exec =
                (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(ai, duel);

            AssertEqual("AI_OldSchool", exec.Deck);
            AssertEqual("ExampleOldSchool", exec.Dsl.DeclaredDeckName);
            AssertEqual("Easy", exec.Dsl.DeclaredLevel);
            AssertEqual("YGOProSharp", exec.Dsl.DeclaredAuthor);

            // 验证 add_executors 批量注册了 6 只怪兽
            AssertEqual(6, exec.Executors.Count(e => e.Type == WindBot.Game.AI.ExecutorType.SummonOrSet));

            // 触发自定义 Lua 规则 should_activate_swords（敌方存在里侧怪兽 -> true）
            WindBot.Game.AI.CardExecutor swordsExec = exec.Executors.First(e =>
                e.Type == WindBot.Game.AI.ExecutorType.Activate && e.CardId == 72302403);
            WindBot.Game.ClientCard facedownEnemy = new(1000, CardLocation.MonsterZone, 0, (int)CardPosition.FaceDownDefence);
            duel.Fields[1].MonsterZone[0] = facedownEnemy;
            AssertTrue(swordsExec.Func());

            // 敌方仅剩低攻表侧怪兽且己方有高攻怪兽 -> false
            WindBot.Game.ClientCard weakEnemy = new(1001, CardLocation.MonsterZone, 0, (int)CardPosition.FaceUpAttack);
            duel.Fields[1].MonsterZone[0] = weakEnemy;
            WindBot.Game.ClientCard strongBot = new(1002, CardLocation.MonsterZone, 0, (int)CardPosition.FaceUpAttack);
            duel.Fields[0].MonsterZone[0] = strongBot;
            AssertFalse(swordsExec.Func());

            // 3. 验证真实 NLua 5.4 位运算与 Lua table 返回卡牌列表（TryConvertList<ClientCard>）
            const string lua54Script = """
                deck("Lua54FeatureCheck")
                    on_select_disfield(function(ctx, hint, count, available)
                        return (1 << 2) & available
                    end)
                    on_select_card(function(ctx, cards, min, max, hint, cancelable)
                        return { cards[1], cards[0] }
                    end)
                    on_select_position(function(ctx, cardId, positions)
                        return CardPosition.FaceUpDefence
                    end)
                deck_end()
                """;

            using WindBot.Game.AI.Lua.LuaDeckExecutor featureExec = new(
                ai,
                duel,
                NLuaRuntime.Instance,
                lua54Script,
                "Lua54FeatureCheck.lua",
                throwOnScriptError: true,
                targetDeckName: "Lua54FeatureCheck");

            AssertEqual(0x04U, featureExec.OnSelectDisfield(0, 1, 0x1FU));
            AssertEqual((int)CardPosition.FaceUpDefence, (int)featureExec.OnSelectPosition(1, [CardPosition.FaceUpAttack, CardPosition.FaceUpDefence]));

            WindBot.Game.ClientCard first = new(2001, CardLocation.Hand, 0);
            WindBot.Game.ClientCard second = new(2002, CardLocation.Hand, 1);
            IList<WindBot.Game.ClientCard> selected = featureExec.OnSelectCard([first, second], 2, 2, 0, false);
            AssertEqual(2, selected.Count);
            AssertEqual(2002, selected[0].Id);
            AssertEqual(2001, selected[1].Id);

            // 4. 验证仓库自带的 ExampleBlueEyes.lua 可被真实 NLua 5.4 加载并执行青眼规则
            WindBot.Game.GameClient blueEyesClient = new(new WindBot.WindBotInfo { Deck = "ExampleBlueEyes", Chat = false }, cardRepo);
            WindBot.Game.GameAI blueEyesAi = new(blueEyesClient, duel);
            using WindBot.Game.AI.Lua.LuaDeckExecutor blueEyesExec =
                (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(blueEyesAi, duel);
            AssertEqual("AI_BlueEyes", blueEyesExec.Deck);
            AssertEqual("ExampleBlueEyes", blueEyesExec.Dsl.DeclaredDeckName);
            AssertTrue(blueEyesExec.Executors.Any(e => e.CardId == 38517737 && e.Type == WindBot.Game.AI.ExecutorType.SpSummon));
            AssertTrue(blueEyesExec.Executors.Any(e => e.CardId == 41620959 && e.Type == WindBot.Game.AI.ExecutorType.Activate));
        }
        finally
        {
            WindBot.Game.AI.DecksManager.ResetLua();
        }
    }

    [Fact]
    public void CliNLuaRuntimeExecutesExampleBlueEyesDialogEndToEnd()
    {
        string root = FindRepositoryRoot();
        string luaDialogsDir = Path.Combine(root, "YGOProSharp.WindBot", "Dialogs", "Lua");
        INamedCardRepository cardRepo = TestNamedRepository(TestNamedCard(89631139));

        // 1. 验证 CLI 选项装配 LuaDialogDir / PreferLuaDialogs / EnableLuaDialogs / Locale
        CliConfiguration cliConfig = CliConfiguration.Load([
            $"LuaDialogDir={luaDialogsDir}",
            "Locale=zh-CN",
            "PreferLuaDialogs=true",
            "ThrowOnLuaScriptError=true"
        ]);
        WindBot.WindBotRuntimeOptions runtimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(cliConfig, cardRepo);
        AssertTrue(runtimeOptions.LuaRuntime is NLuaRuntime);
        AssertTrue(runtimeOptions.LuaDialogScriptProvider is WindBot.Game.AI.Lua.FileSystemLuaDialogScriptProvider);
        AssertTrue(runtimeOptions.PreferLuaDialogs);
        AssertEqual("zh-CN", runtimeOptions.Locale);

        CliConfiguration disabledDialogs = CliConfiguration.Load(["EnableLuaDecks=false", "EnableLuaDialogs=false"]);
        WindBot.WindBotRuntimeOptions disabledOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(disabledDialogs, cardRepo);
        AssertTrue(disabledOptions.LuaRuntime is null);
        AssertTrue(disabledOptions.LuaDialogScriptProvider is null);

        try
        {
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.DialogsManager.ResetLua();
            WindBot.WindBotRuntime.Configure(runtimeOptions);

            // 2. 简体中文（zh-CN）：验证 locale("zh-CN", "zh") 块 + set_fallback("auto") 命中 zh-CN.json + 动态青眼台词
            (InMemoryNetworkTransport clientTransportZh, InMemoryNetworkTransport serverTransportZh) =
                InMemoryNetworkTransport.CreatePair();
            using WindBot.Game.GameClient clientZh = new(
                new WindBot.WindBotInfo
                {
                    Name = "BlueEyesBotZh",
                    Deck = "AI_OldSchool",
                    Dialog = "ExampleBlueEyes",
                    Locale = "zh-CN",
                    Chat = true,
                    ChatIntervalMilliseconds = 0,
                    DuelStartChatDelayMilliseconds = 0
                },
                cardRepo,
                clientTransportZh);

            List<string> chatsZh = CaptureDialogChatMessages(clientZh, clientTransportZh, serverTransportZh, c =>
            {
                WindBot.Game.Duel duel = new();
                duel.Turn = 1;
                using WindBot.Game.AI.Dialogs dialogs = new(c, duel);

                AssertTrue(dialogs.LuaScript != null);
                AssertEqual("ExampleBlueEyes.zh-CN.lua", dialogs.ResolvedDialogName);
                AssertEqual("ExampleBlueEyes", dialogs.LuaScript!.Dsl.DeclaredDialogName);
                AssertEqual("zh-CN", dialogs.LuaScript.Dsl.DeclaredLocale);
                AssertEqual("YGOProSharp", dialogs.LuaScript.Dsl.DeclaredAuthor); // 来自 includes("ExampleBlueEyes.common.lua")
                AssertEqual("zh-CN", dialogs.Locale);
                AssertEqual("里侧怪兽", dialogs.FacedownMonsterName);
                AssertTrue(dialogs.GetLines("deckerror").Any(line => line.Contains("卡组", StringComparison.Ordinal)));

                dialogs.SendNewTurn();
                dialogs.SendSummon("青眼白龙");
                dialogs.SendSummon("太古的白石");
                dialogs.SendDirectAttack("青眼白龙");
                dialogs.SendAttack("青眼亚白龙", "monster");
            });

            AssertEqual(5, chatsZh.Count);
            AssertEqual("第一回合，抽卡！让青眼的传说就此开始！", chatsZh[0]);
            AssertEqual("出现吧，最强之龙——青眼白龙！", chatsZh[1]);
            AssertTrue(chatsZh[2].Contains("太古的白石", StringComparison.Ordinal));
            AssertEqual("青眼白龙，直接攻击！毁灭的爆裂疾风弹！！", chatsZh[3]);
            AssertTrue(chatsZh[4].Contains("青眼亚白龙", StringComparison.Ordinal) && chatsZh[4].Contains("里侧怪兽", StringComparison.Ordinal));

            // 3. 繁体中文（zh-TW）：验证自动命中拆分文件 ExampleBlueEyes.zh-TW.lua
            (InMemoryNetworkTransport clientTransportTw, InMemoryNetworkTransport serverTransportTw) =
                InMemoryNetworkTransport.CreatePair();
            using WindBot.Game.GameClient clientTw = new(
                new WindBot.WindBotInfo
                {
                    Name = "BlueEyesBotTw",
                    Deck = "AI_OldSchool",
                    Dialog = "ExampleBlueEyes",
                    Locale = "zh-TW",
                    Chat = true,
                    ChatIntervalMilliseconds = 0,
                    DuelStartChatDelayMilliseconds = 0
                },
                cardRepo,
                clientTransportTw);

            List<string> chatsTw = CaptureDialogChatMessages(clientTw, clientTransportTw, serverTransportTw, c =>
            {
                WindBot.Game.Duel duel = new();
                duel.Turn = 1;
                using WindBot.Game.AI.Dialogs dialogs = new(c, duel);

                AssertEqual("ExampleBlueEyes.zh-TW.lua", dialogs.ResolvedDialogName);
                AssertEqual("zh-TW", dialogs.LuaScript!.Dsl.DeclaredLocale);
                AssertEqual("YGOProSharp", dialogs.LuaScript.Dsl.DeclaredAuthor);
                AssertEqual("裡側怪獸", dialogs.FacedownMonsterName);
                dialogs.SendNewTurn();
                dialogs.SendSummon("青眼白龍");
            });

            AssertEqual(2, chatsTw.Count);
            AssertEqual("第一回合，抽牌！讓青眼的傳說就此開始！", chatsTw[0]);
            AssertEqual("出現吧，最強之龍——青眼白龍！", chatsTw[1]);

            // 4. 英文（en-US）：验证自动命中英文基线 ExampleBlueEyes.lua + default.json 回退
            (InMemoryNetworkTransport clientTransportEn, InMemoryNetworkTransport serverTransportEn) =
                InMemoryNetworkTransport.CreatePair();
            using WindBot.Game.GameClient clientEn = new(
                new WindBot.WindBotInfo
                {
                    Name = "BlueEyesBotEn",
                    Deck = "AI_OldSchool",
                    Dialog = "ExampleBlueEyes",
                    Locale = "en-US",
                    Chat = true,
                    ChatIntervalMilliseconds = 0,
                    DuelStartChatDelayMilliseconds = 0
                },
                cardRepo,
                clientTransportEn);

            List<string> chatsEn = CaptureDialogChatMessages(clientEn, clientTransportEn, serverTransportEn, c =>
            {
                WindBot.Game.Duel duel = new();
                duel.Turn = 1;
                using WindBot.Game.AI.Dialogs dialogs = new(c, duel);

                AssertEqual("ExampleBlueEyes.lua", dialogs.ResolvedDialogName);
                AssertEqual("en-US", dialogs.LuaScript!.Dsl.DeclaredLocale);
                AssertEqual("YGOProSharp", dialogs.LuaScript.Dsl.DeclaredAuthor);
                AssertEqual("monster", dialogs.FacedownMonsterName);
                AssertTrue(dialogs.GetLines("deckerror").Any(line => line.Contains("deck", StringComparison.OrdinalIgnoreCase)));
                dialogs.SendNewTurn();
                dialogs.SendSummon("Blue-Eyes White Dragon");
                dialogs.SendDirectAttack("Blue-Eyes White Dragon");
            });

            AssertEqual(3, chatsEn.Count);
            AssertEqual("Turn 1, draw! Let the legend of Blue-Eyes begin!", chatsEn[0]);
            AssertEqual("Come forth, the ultimate engine of destruction — Blue-Eyes White Dragon!", chatsEn[1]);
            AssertEqual("Blue-Eyes White Dragon, direct attack! Burst Stream of Destruction!!", chatsEn[2]);
        }
        finally
        {
            WindBot.WindBotRuntime.Locale = null;
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.DialogsManager.ResetLua();
        }
    }

    [Fact]
    public void CliMapsPersistenceAndWebControlOptions()
    {
        CliConfiguration defaultCfg = CliConfiguration.Load(["Port=7911"]);
        AssertFalse(CliOptionsFactory.ShouldEnableWeb(defaultCfg));
        AssertFalse(CliOptionsFactory.ShouldEnablePersistence(defaultCfg));

        // 默认开启 EnableWeb=true 时采用单端口前后台融合模式（仅启动 WebControlHost，不额外占用第二端口）
        CliConfiguration singlePortWebCfg = CliConfiguration.Load(["EnableWeb=true"]);
        AssertTrue(CliOptionsFactory.ShouldEnableWeb(singlePortWebCfg));
        AssertTrue(CliOptionsFactory.ShouldEnableAdminPanel(singlePortWebCfg));
        AssertFalse(CliOptionsFactory.ShouldEnableUserPortal(singlePortWebCfg));
        WebControlOptions singlePortWebOpts = CliOptionsFactory.CreateWebControlOptions(singlePortWebCfg);
        AssertTrue(singlePortWebOpts.EnableUnifiedPortal);
        AssertTrue(singlePortWebOpts.PortalPort is null);

        CliConfiguration webCfg = CliConfiguration.Load([
            "RootPath=./srvroot",
            "EnableWeb=true",
            "AdminHost=127.0.0.1",
            "AdminPort=8090",
            "PortalHost=0.0.0.0",
            "PortalPort=8091",
            "PublicGameHost=play.example.com",
            "RequireHttpsCookie=true",
            "DbProvider=Postgres",
            "DbConnectionString=Host=pg;Database=ygoprosharp",
            "RedisUrl=redis:6379",
            "AdminUser=ops_root",
            "AdminPassword=123456",
            "SessionTtlHours=12"
        ]);

        AssertTrue(CliOptionsFactory.ShouldEnableWeb(webCfg));
        AssertTrue(CliOptionsFactory.ShouldEnableAdminPanel(webCfg));
        AssertTrue(CliOptionsFactory.ShouldEnableUserPortal(webCfg));
        AssertTrue(CliOptionsFactory.ShouldEnablePersistence(webCfg));

        ServerOptions srvOpts = CliOptionsFactory.CreateServerOptions(webCfg);
        PersistenceOptions pOpts = CliOptionsFactory.CreatePersistenceOptions(webCfg, srvOpts);
        WebControlOptions wOpts = CliOptionsFactory.CreateWebControlOptions(webCfg);
        UserPortalOptions uOpts = CliOptionsFactory.CreateUserPortalOptions(webCfg);

        AssertEqual((int)OperationalDbProvider.Postgres, (int)pOpts.DbProvider);
        AssertEqual("Host=pg;Database=ygoprosharp", pOpts.ConnectionString);
        AssertEqual("redis:6379", pOpts.RedisUrl!);
        AssertEqual("ops_root", pOpts.BootstrapAdminUser!);
        AssertEqual("123456", pOpts.BootstrapAdminPassword!);
        AssertEqual(12, pOpts.SessionTtlHours);

        AssertEqual("127.0.0.1", wOpts.Host);
        AssertEqual(8090, wOpts.Port);
        AssertEqual(8091, wOpts.PortalPort!.Value);
        AssertTrue(wOpts.RequireHttpsCookie);

        AssertEqual("0.0.0.0", uOpts.Host);
        AssertEqual(8091, uOpts.Port);
        AssertEqual("play.example.com", uOpts.PublicGameHost!);
    }

    [Fact]
    public void NLuaSandboxBlocksDangerousGlobalsInfiniteLoopsAndSupportsDualBrainHooks()
    {
        NLuaRuntime tightRuntime = new(maxInstructionBatches: 10, maxCallDurationMs: 250);
        using (ILuaScriptContext ctx = tightRuntime.CreateContext("sandbox_check"))
        {
            object? checkRes = ctx.DoString(
                "return (os == nil) and (io == nil) and (package == nil) and (debug == nil) and (dofile == nil) and (loadfile == nil) and (require == nil) and (luanet == nil)",
                "check_globals.lua");
            AssertTrue(checkRes is true);

            bool loopAborted = false;
            try
            {
                ctx.DoString("while true do end", "infinite_loop.lua");
            }
            catch (Exception ex) when (ex is TimeoutException || ex.InnerException is TimeoutException || ex.Message.Contains("沙箱上限", StringComparison.Ordinal))
            {
                loopAborted = true;
            }
            AssertTrue(loopAborted);
        }

        WindBot.Game.Duel duel = new();
        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo { Name = "DualBrainBot" }, EmptyNamedCardRepository.Instance);
        WindBot.Game.GameAI ai = new(client, duel);
        const string luaScript = """
            deck("DualBrainTest")
                set_deckfile("AI_Yubel")
                on_rock_paper_scissors(function(ctx)
                    emit_thought("准备猜拳策略")
                    local ext = ask_external_agent("猜拳选择", {"剪刀", "石头", "布"})
                    if ext ~= nil then
                        return ext
                    end
                    return 2
                end)
            deck_end()
            """;

        using WindBot.Game.AI.Lua.LuaDeckExecutor exec = new(ai, duel, NLuaRuntime.Instance, luaScript, "DualBrainTest.lua", throwOnScriptError: true);
        exec.ExternalAgentHandler = req =>
        {
            AssertEqual("猜拳选择", req.Reason);
            AssertEqual(3, req.Candidates.Count);
            return 3;
        };

        int rps = exec.OnRockPaperScissors();
        AssertEqual(3, rps);
        AssertEqual(1, exec.ThoughtHistory.Count);
        AssertEqual("准备猜拳策略", exec.ThoughtHistory[0].Summary);
    }

    [Fact]
    public void CliMapsDockerEnvironmentVariablesAndContainerArtifactsEnforceSecurityInvariants()
    {
        string root = FindRepositoryRoot();
        string dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        string compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        string dockerignore = File.ReadAllText(Path.Combine(root, ".dockerignore"));
        string envExample = File.ReadAllText(Path.Combine(root, ".env.example"));

        // 1. 四阶段净化 Dockerfile 与非 root 用户约束
        AssertTrue(dockerfile.Contains("AS native-builder", StringComparison.Ordinal));
        AssertTrue(dockerfile.Contains("AS web-builder", StringComparison.Ordinal));
        AssertTrue(dockerfile.Contains("AS dotnet-builder", StringComparison.Ordinal));
        AssertTrue(dockerfile.Contains("AS runtime", StringComparison.Ordinal));
        AssertTrue(dockerfile.Contains("USER ygopro", StringComparison.Ordinal));
        AssertTrue(dockerfile.Contains("/healthz", StringComparison.Ordinal));
        AssertDoesNotContainAny(dockerfile, ["OPENAI_API_KEY", "ANTHROPIC_API_KEY", "GEMINI_API_KEY", "DEEPSEEK_API_KEY"], "Dockerfile zero LLM keys");

        // 2. docker-compose.yml 单机默认 + prod Profile (Postgres + Redis 内网隔离，不映射 5432/6379 到公网)
        AssertTrue(compose.Contains("profiles: [\"prod\"]", StringComparison.Ordinal));
        AssertTrue(compose.Contains("postgres:17-alpine", StringComparison.Ordinal));
        AssertTrue(compose.Contains("redis:7-alpine", StringComparison.Ordinal));
        AssertTrue(compose.Contains("--appendonly", StringComparison.Ordinal));
        AssertTrue(compose.Contains("pg_isready", StringComparison.Ordinal));
        AssertTrue(compose.Contains("redis-cli", StringComparison.Ordinal));
        AssertDoesNotContainAny(compose, ["\"5432:5432\"", "\"6379:6379\"", "0.0.0.0:5432", "0.0.0.0:6379"], "Compose DB/Redis internal isolation");

        // 3. .dockerignore 排除构建产物、参考仓、测试工程与敏感配置
        foreach (string requiredIgnore in new[] { ".external/", "YGOProSharp.Tests/", "**/bin/", "**/obj/", ".env", "*.db", "!YGOProSharp.Native/lib/**" })
            AssertTrue(dockerignore.Contains(requiredIgnore, StringComparison.Ordinal));

        AssertTrue(envExample.Contains("YGOPRO_DB_PROVIDER=Sqlite", StringComparison.Ordinal));
        AssertDoesNotContainAny(envExample, ["OPENAI_API_KEY", "ANTHROPIC_API_KEY", "GEMINI_API_KEY"], ".env.example zero LLM keys");

        // 4. 验证容器纯环境变量启动映射
        (string Key, string Value)[] testEnv =
        [
            ("YGOPRO_ROOT_PATH", "/app/data"),
            ("YGOPRO_GAME_PORT", "7921"),
            ("YGOPRO_MULTI_ROOM", "true"),
            ("YGOPRO_ENABLE_WEB", "true"),
            ("YGOPRO_ADMIN_HOST", "0.0.0.0"),
            ("YGOPRO_ADMIN_PORT", "7922"),
            ("YGOPRO_PORTAL_HOST", "0.0.0.0"),
            ("YGOPRO_PORTAL_PORT", "7923"),
            ("YGOPRO_PUBLIC_GAME_HOST", "duel.example.org"),
            ("YGOPRO_REQUIRE_HTTPS_COOKIE", "true"),
            ("YGOPRO_DB_PROVIDER", "Postgres"),
            ("YGOPRO_DB_CONNECTION_STRING", "Host=postgres;Database=ygoprosharp"),
            ("YGOPRO_REDIS_URL", "redis:6379"),
            ("YGOPRO_REPLAY_DIR", "/app/data/replays"),
            ("YGOPRO_BOOTSTRAP_ADMIN_USER", "docker_admin"),
            ("YGOPRO_BOOTSTRAP_ADMIN_PASSWORD", "DockerPass123"),
            ("YGOPRO_SESSION_TTL_HOURS", "36")
        ];

        Dictionary<string, string?> prevEnv = new(StringComparer.Ordinal);
        try
        {
            foreach ((string k, string v) in testEnv)
            {
                prevEnv[k] = Environment.GetEnvironmentVariable(k);
                Environment.SetEnvironmentVariable(k, v);
            }

            CliConfiguration emptyArgs = CliConfiguration.Load([]);
            AssertTrue(CliOptionsFactory.ShouldEnableWeb(emptyArgs));
            AssertTrue(CliOptionsFactory.ShouldEnableAdminPanel(emptyArgs));
            AssertTrue(CliOptionsFactory.ShouldEnableUserPortal(emptyArgs));
            AssertTrue(CliOptionsFactory.ShouldEnablePersistence(emptyArgs));

            ServerOptions srv = CliOptionsFactory.CreateServerOptions(emptyArgs);
            PersistenceOptions per = CliOptionsFactory.CreatePersistenceOptions(emptyArgs, srv);
            WebControlOptions web = CliOptionsFactory.CreateWebControlOptions(emptyArgs);
            UserPortalOptions portal = CliOptionsFactory.CreateUserPortalOptions(emptyArgs);

            AssertEqual("/app/data", srv.RootPath);
            AssertEqual(7921, srv.Port);
            AssertTrue(srv.EnableMultiRoom);

            AssertEqual((int)OperationalDbProvider.Postgres, (int)per.DbProvider);
            AssertEqual("Host=postgres;Database=ygoprosharp", per.ConnectionString);
            AssertEqual("redis:6379", per.RedisUrl!);
            AssertEqual("/app/data/replays", per.ReplayDirectory);
            AssertEqual("docker_admin", per.BootstrapAdminUser!);
            AssertEqual("DockerPass123", per.BootstrapAdminPassword!);
            AssertEqual(36, per.SessionTtlHours);

            AssertEqual("0.0.0.0", web.Host);
            AssertEqual(7922, web.Port);
            AssertEqual(7923, web.PortalPort!.Value);
            AssertTrue(web.RequireHttpsCookie);

            AssertEqual("0.0.0.0", portal.Host);
            AssertEqual(7923, portal.Port);
            AssertEqual(7922, portal.McpPortHint!.Value);
            AssertEqual("duel.example.org", portal.PublicGameHost!);
            AssertTrue(portal.RequireHttpsCookie);
        }
        finally
        {
            foreach ((string k, string? oldVal) in prevEnv)
                Environment.SetEnvironmentVariable(k, oldVal);
        }
    }

}
