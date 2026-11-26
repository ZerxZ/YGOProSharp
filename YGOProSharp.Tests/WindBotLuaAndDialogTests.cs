namespace YGOProSharp.Tests;

public sealed class WindBotLuaAndDialogTests
{
    [Fact]
    public void WindBotLuaDeckExecutorBindsGlobalsRegistersRulesAndDisposes()
    {
        WindBot.WindBotInfo info = new() { Deck = "TestLua" };
        WindBot.Game.GameClient client = new(info, TestNamedRepository());
        WindBot.Game.Duel duel = new();
        WindBot.Game.GameAI ai = new(client, duel);

        int disposeHookCalls = 0;
        bool customRuleExecuted = false;
        bool rawWrappedRuleExecuted = false;

        FakeLuaRuntime runtime = new(context =>
        {
            context.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnInit] = new FakeLuaFunction(args =>
            {
                WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)args[0]!;
                exec.AddLuaExecutor("Activate", 1001, "MyCardRule");
                exec.AddLuaExecutor(WindBot.Game.AI.ExecutorType.Summon, 1002, (Func<bool>)(() => true));
                exec.AddLuaExecutor(WindBot.Game.AI.ExecutorType.SpellSet, 1003, new FakeRawLuaClosure(_ =>
                {
                    rawWrappedRuleExecuted = true;
                    return true;
                }));
                exec.AddDefaultExecutor("Activate", 5318639, "DefaultTrap");
                return null;
            });

            context.Functions["MyCardRule"] = new FakeLuaFunction(args =>
            {
                WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)args[0]!;
                customRuleExecuted = exec.CurrentCard != null && exec.CurrentCard.Id == 1001;
                return true;
            });

            context.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnDispose] = new FakeLuaFunction(_ =>
            {
                disposeHookCalls++;
                return null;
            });
        });

        WindBot.Game.AI.Lua.LuaDeckExecutor executor = new(ai, duel, runtime, "-- test lua deck", "TestLua.lua");
        FakeLuaContext createdContext = runtime.CreatedContexts[0];

        AssertTrue(ReferenceEquals(executor, createdContext.Globals["Self"]));
        AssertTrue(ReferenceEquals(executor, createdContext.Globals["Executor"]));
        AssertTrue(ReferenceEquals(duel, createdContext.Globals["Duel"]));
        AssertTrue(ReferenceEquals(ai, createdContext.Globals["AI"]));
        AssertTrue(ReferenceEquals(executor.Util, createdContext.Globals["Util"]));
        AssertTrue(ReferenceEquals(duel.Fields[0], createdContext.Globals["Bot"]));
        AssertTrue(ReferenceEquals(duel.Fields[1], createdContext.Globals["Enemy"]));

        WindBot.Game.ClientCard sampleCard = new(1001, CardLocation.Hand, 0);
        executor.SetCard(WindBot.Game.AI.ExecutorType.Activate, sampleCard, 0);
        AssertTrue(executor.Executors.Any(e => e.CardId == 1001 && e.Type == WindBot.Game.AI.ExecutorType.Activate && e.Func()));
        AssertTrue(customRuleExecuted);

        AssertTrue(executor.Executors.Any(e => e.CardId == 1002 && e.Type == WindBot.Game.AI.ExecutorType.Summon && e.Func()));
        AssertTrue(executor.Executors.Any(e => e.CardId == 1003 && e.Type == WindBot.Game.AI.ExecutorType.SpellSet && e.Func()));
        AssertTrue(rawWrappedRuleExecuted);
        AssertTrue(executor.Executors.Any(e => e.CardId == 5318639 && e.Type == WindBot.Game.AI.ExecutorType.Activate));

        executor.Dispose();
        executor.Dispose(); // 验证幂等释放

        AssertTrue(executor.IsDisposed);
        AssertEqual(1, disposeHookCalls);
        AssertTrue(createdContext.Disposed);
        AssertTrue(createdContext.TrackedFunctions.All(f => f.Disposed));
    }

    [Fact]
    public void WindBotLuaDeckExecutorSupportsModuleTableHooksAndFallback()
    {
        WindBot.WindBotInfo info = new() { Deck = "ModuleLua" };
        WindBot.Game.GameClient client = new(info, TestNamedRepository());
        WindBot.Game.Duel duel = new();
        WindBot.Game.GameAI ai = new(client, duel);

        int newTurnCalls = 0;
        FakeLuaTable? createdModule = null;

        FakeLuaRuntime runtime = new(context =>
        {
            FakeLuaTable module = new();
            createdModule = module;

            // Lua number 常见为 double，验证安全转 int 与枚举
            module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnRockPaperScissors] = new FakeLuaFunction(_ => 2.0d);
            module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectHand] = new FakeLuaFunction(_ => false);
            module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectOption] = new FakeLuaFunction(_ => 1L);
            module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectPosition] = new FakeLuaFunction(_ => (double)CardPosition.FaceUpDefence);
            module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectYesNo] = new FakeLuaFunction(args =>
            {
                int desc = (int)args[1]!;
                return desc == 42 ? false : null; // 返回 null 时应回退基类 DefaultExecutor（返回 true）
            });
            module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnNewTurn] = new FakeLuaFunction(_ =>
            {
                newTurnCalls++;
                return null;
            });
            // 模拟 XLua 返回自定义 LuaTable 列表对象，经由 ILuaScriptContext.TryConvertList<ClientCard> 转换
            module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectCard] = new FakeLuaFunction(args =>
            {
                IList<WindBot.Game.ClientCard> cards = (IList<WindBot.Game.ClientCard>)args[1]!;
                return new FakeLuaCardArray([cards[^1]]);
            });

            context.ModuleResult = module;
        });

        using WindBot.Game.AI.Lua.LuaDeckExecutor executor = new(ai, duel, runtime, "return M", "ModuleLua.lua");

        AssertEqual(2, executor.OnRockPaperScissors());
        AssertFalse(executor.OnSelectHand());
        AssertEqual(1, executor.OnSelectOption([10, 20]));
        AssertEqual((int)CardPosition.FaceUpDefence, (int)executor.OnSelectPosition(1, [CardPosition.FaceUpAttack, CardPosition.FaceUpDefence]));
        AssertFalse(executor.OnSelectYesNo(42));
        AssertTrue(executor.OnSelectYesNo(99)); // 回退到 Executor.OnSelectYesNo 默认 true

        executor.OnNewTurn();
        AssertEqual(1, newTurnCalls);

        WindBot.Game.ClientCard c1 = new(101, CardLocation.Hand, 0);
        WindBot.Game.ClientCard c2 = new(102, CardLocation.Hand, 1);
        IList<WindBot.Game.ClientCard> picked = executor.OnSelectCard([c1, c2], 1, 1, 0, false);
        AssertEqual(1, picked.Count);
        AssertEqual(102, picked[0].Id);

        executor.Dispose();
        AssertTrue(createdModule is not null && createdModule.Disposed);
    }

    [Fact]
    public void WindBotLuaDeckExecutorHandlesScriptErrors()
    {
        WindBot.WindBotInfo info = new() { Deck = "ErrorLua" };
        WindBot.Game.GameClient client = new(info, TestNamedRepository());
        WindBot.Game.Duel duel = new();
        WindBot.Game.GameAI ai = new(client, duel);

        FakeLuaRuntime runtime = new(context =>
        {
            context.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectYesNo] = new FakeLuaFunction(_ =>
                throw new InvalidOperationException("boom in lua"));
        });

        using WindBot.Game.AI.Lua.LuaDeckExecutor resilient = new(ai, duel, runtime, "-- err", "Err.lua", throwOnScriptError: false);
        AssertTrue(resilient.OnSelectYesNo(1)); // 记录错误后回退默认实现

        using WindBot.Game.AI.Lua.LuaDeckExecutor strict = new(ai, duel, runtime, "-- err", "ErrStrict.lua", throwOnScriptError: true);
        AssertThrows<InvalidOperationException>(() => _ = strict.OnSelectYesNo(1));
    }

    [Fact]
    public void WindBotLuaXmakeStyleDslSupportsScopesRulesAndHooks()
    {
        WindBot.Game.Duel duel = new();
        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo { Deck = "DeckBeta" }, TestNamedRepository());
        WindBot.Game.GameAI ai = new(client, duel);

        int initCalls = 0;
        int newTurnCalls = 0;
        int disposeCalls = 0;
        bool betaDeferredRuleCalled = false;

        FakeLuaRuntime runtime = new(context =>
        {
            // 模拟类似 xmake.lua 的声明式脚本：
            // 顶层公共规则 + deck("DeckAlpha") ... deck_end() + deck("DeckBeta") ... deck_end()
            var deckFn = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["deck"]!;
            var deckEndFn = (WindBot.Game.AI.Lua.LuaDslAction0)context.Globals["deck_end"]!;
            var setDeckFile = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_deckfile"]!;
            var setLevel = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_level"]!;
            var setDescription = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_description"]!;
            var setAuthor = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_author"]!;
            var addDefault = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_default"]!;
            var addExecutors = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_executors"]!;
            var onActivate = (WindBot.Game.AI.Lua.LuaDslAction2)context.Globals["on_activate"]!;
            var onSummon = (WindBot.Game.AI.Lua.LuaDslAction2)context.Globals["on_summon"]!;
            var onInit = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_init"]!;
            var onNewTurn = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_new_turn"]!;
            var onSelectYesNo = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_select_yesno"]!;
            var onDispose = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_dispose"]!;

            // 顶层全局规则（对所有 deck 生效，仿照 xmake 顶层作用域）
            addDefault("Repos", "DefaultMonsterRepos", null);

            deckFn("DeckAlpha");
                setDeckFile("AI_Alpha");
                onActivate(1111, new FakeRawLuaClosure(_ => true));
            deckEndFn();

            deckFn("DeckBeta");
                setDeckFile("AI_Beta_Custom");
                setLevel("Hard");
                setDescription("xmake-style Lua deck");
                setAuthor("tester");
                // 先声明规则，引用脚本底部才定义的函数名
                onSummon(2222, "can_summon_beta");
                addExecutors("SummonOrSet", new object[] { 3333.0, 4444L }, null);
                onInit(new FakeRawLuaClosure(_ => { initCalls++; return null; }));
                onNewTurn((Action<WindBot.Game.AI.Lua.LuaDeckExecutor>)(_ => newTurnCalls++));
                onSelectYesNo(new FakeRawLuaClosure(args => (int)args[1]! == 7 ? false : null));
                onDispose(new FakeRawLuaClosure(_ => { disposeCalls++; return null; }));
            deckEndFn();

            // 在脚本尾部才定义函数，验证延迟解析
            context.Functions["can_summon_beta"] = new FakeLuaFunction(args =>
            {
                WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)args[0]!;
                betaDeferredRuleCalled = exec.CurrentCard != null && exec.CurrentCard.Id == 2222;
                return true;
            });
        });

        WindBot.Game.AI.Lua.InMemoryLuaDeckScriptProvider provider = new();
        provider.Add("DeckBeta", "-- xmake style multi-deck");

        try
        {
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.DecksManager.ConfigureLua(runtime, provider);

            WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(ai, duel);

            // 验证仅激活 DeckBeta，且 DecksManager 尊重脚本内的 set_deckfile("AI_Beta_Custom")
            AssertEqual("AI_Beta_Custom", exec.Deck);
            AssertEqual("DeckBeta", exec.Dsl.DeclaredDeckName);
            AssertEqual("Hard", exec.Dsl.DeclaredLevel);
            AssertEqual("xmake-style Lua deck", exec.Dsl.DeclaredDescription);
            AssertEqual("tester", exec.Dsl.DeclaredAuthor);
            AssertEqual(1, initCalls);

            // DeckAlpha 的 1111 规则不应生效，顶层 Repos + DeckBeta 的 2222/3333/4444 应按顺序生效
            AssertFalse(exec.Executors.Any(e => e.CardId == 1111));
            int reposIndex = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.Repos && e.CardId == -1);
            int summon2222Index = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.Summon && e.CardId == 2222);
            int sos3333Index = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.SummonOrSet && e.CardId == 3333);
            int sos4444Index = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.SummonOrSet && e.CardId == 4444);
            AssertTrue(reposIndex >= 0 && summon2222Index > reposIndex && sos3333Index > summon2222Index && sos4444Index > sos3333Index);

            exec.SetCard(WindBot.Game.AI.ExecutorType.Summon, new WindBot.Game.ClientCard(2222, CardLocation.Hand, 0), 0);
            AssertTrue(exec.Executors[summon2222Index].Func());
            AssertTrue(betaDeferredRuleCalled);

            exec.OnNewTurn();
            AssertEqual(1, newTurnCalls);
            AssertFalse(exec.OnSelectYesNo(7));
            AssertTrue(exec.OnSelectYesNo(8)); // nil 回退默认 true

            exec.Dispose();
            AssertEqual(1, disposeCalls);
            AssertTrue(runtime.CreatedContexts[0].Disposed);
            AssertTrue(runtime.CreatedContexts[0].TrackedFunctions.All(f => f.Disposed));
        }
        finally
        {
            WindBot.Game.AI.DecksManager.ResetLua();
        }
    }

    [Fact]
    public void WindBotDecksManagerResolvesLuaDecksAndFileSystemProvider()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpLuaDeckTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "DynamicBlueEyes.lua"), "-- dynamic");
            File.WriteAllText(Path.Combine(tempDir, "RegisteredHero.lua"), "-- registered");
            File.WriteAllText(Path.Combine(tempDir, "Yubel.lua"), "-- override");

            WindBot.Game.AI.Lua.FileSystemLuaDeckScriptProvider fsProvider = new(tempDir);
            IReadOnlyList<WindBot.Game.AI.Lua.LuaDeckDescriptor> discovered = fsProvider.DiscoverDecks();
            AssertEqual(3, discovered.Count);

            FakeLuaRuntime runtime = new(_ => { });
            WindBot.Game.Duel duel = new();

            // 1. 注册自定义 Lua 卡组（含自定义 .ydk 文件名）+ 动态按名发现
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.DecksManager.ConfigureLua(runtime, fsProvider, preferLuaDecks: false);
            WindBot.Game.AI.DecksManager.RegisterLuaDeck(new WindBot.Game.AI.Lua.LuaDeckDescriptor(
                name: "HeroLua",
                file: "AI_HeroLua",
                scriptName: "RegisteredHero"));

            WindBot.Game.GameAI heroAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "HeroLua" }, TestNamedRepository()), duel);
            using WindBot.Game.AI.Lua.LuaDeckExecutor heroExec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(heroAi, duel);
            AssertEqual("AI_HeroLua", heroExec.Deck);

            WindBot.Game.GameAI dynamicAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "DynamicBlueEyes" }, TestNamedRepository()), duel);
            using WindBot.Game.AI.Lua.LuaDeckExecutor dynamicExec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(dynamicAi, duel);
            AssertEqual("DynamicBlueEyes", dynamicExec.Deck);

            // 2. 默认不覆盖内置 C# Yubel；设置 AllowOverride=true 后可覆盖内置 C# 卡组
            WindBot.Game.GameAI yubelManagedAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "Yubel" }, TestNamedRepository()), duel);
            WindBot.Game.AI.Executor yubelManaged = WindBot.Game.AI.DecksManager.Instantiate(yubelManagedAi, duel);
            AssertFalse(yubelManaged is WindBot.Game.AI.Lua.LuaDeckExecutor);

            WindBot.Game.AI.DecksManager.RegisterLuaDeck(new WindBot.Game.AI.Lua.LuaDeckDescriptor(
                name: "Yubel",
                file: "AI_Yubel_Lua",
                scriptName: "Yubel",
                allowOverride: true));
            WindBot.Game.GameAI yubelOverrideAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "Yubel" }, TestNamedRepository()), duel);
            using WindBot.Game.AI.Lua.LuaDeckExecutor yubelLua = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(yubelOverrideAi, duel);
            AssertEqual("AI_Yubel_Lua", yubelLua.Deck);

            // 3. 验证通过 WindBotRuntime.Configure 注入 LuaRuntime + InMemoryLuaDeckScriptProvider
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.Lua.InMemoryLuaDeckScriptProvider memoryProvider = new();
            memoryProvider.Add("MemoryDeck", "-- memory lua");
            WindBot.WindBotRuntime.Configure(new WindBot.WindBotRuntimeOptions
            {
                CardRepository = TestNamedRepository(),
                LuaRuntime = runtime,
                LuaScriptProvider = memoryProvider,
                LuaDecks = [new WindBot.Game.AI.Lua.LuaDeckDescriptor("MemoryDeck", "AI_MemoryDeck")]
            });

            WindBot.Game.GameAI memAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "MemoryDeck" }, TestNamedRepository()), duel);
            using WindBot.Game.AI.Lua.LuaDeckExecutor memExec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(memAi, duel);
            AssertEqual("AI_MemoryDeck", memExec.Deck);
        }
        finally
        {
            WindBot.Game.AI.DecksManager.ResetLua();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void WindBotGameBehaviorDisposesLuaExecutorOnFailure()
    {
        FakeLuaRuntime runtime = new(_ => { });
        WindBot.Game.AI.Lua.InMemoryLuaDeckScriptProvider memoryProvider = new();
        memoryProvider.Add("LeakCheckDeck", "-- leak check");

        try
        {
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.DecksManager.ConfigureLua(runtime, memoryProvider);

            WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
            {
                Deck = "LeakCheckDeck",
                DeckFile = "missing_ydk_" + Guid.NewGuid().ToString("N")
            }, TestNamedRepository());

            AssertThrows<InvalidOperationException>(() => _ = new WindBot.Game.GameBehavior(client, TestNamedRepository()));
            AssertEqual(1, runtime.CreatedContexts.Count);
            AssertTrue(runtime.CreatedContexts[0].Disposed);
        }
        finally
        {
            WindBot.Game.AI.DecksManager.ResetLua();
        }
    }

    [Fact]
    public void WindBotDialogsDegradesGracefullyWhenDialogFileIsMissing()
    {
        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
        {
            Dialog = "non_existent_dialog_" + Guid.NewGuid().ToString("N"),
            Chat = true
        }, TestNamedRepository());

        WindBot.Game.AI.Dialogs dialogs = new(client);
        dialogs.SendWelcome();
        dialogs.SendSorry();
        dialogs.SendDeckSorry("DECK");
        dialogs.SendCustomChat(0);
    }

    [Fact]
    public void WindBotLuaDialogXmakeStyleDslSupportsScopesFallbackAndDynamicHooks()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpLuaDialogDsl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Dialogs"));
        File.WriteAllText(
            Path.Combine(tempDir, "Dialogs", "base-fallback.json"),
            """
            {
              "welcome": ["FALLBACK_WELCOME"],
              "deckerror": ["FALLBACK_DECK_ERROR:{0}"],
              "endturn": ["FALLBACK_END_TURN"],
              "surrender": ["FALLBACK_SURRENDER"],
              "facedownmonstername": "默认里侧怪兽"
            }
            """);

        int initCalls = 0;
        int disposeCalls = 0;
        bool initSawMergedFallback = false;

        FakeLuaRuntime runtime = new(context =>
        {
            var dialogFn = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["dialog"]!;
            var dialogEndFn = (WindBot.Game.AI.Lua.LuaDslAction0)context.Globals["dialog_end"]!;
            var setLocale = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_locale"]!;
            var setDescription = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_description"]!;
            var setAuthor = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_author"]!;
            var setFallback = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_fallback"]!;
            var setFacedown = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_facedown_monster_name"]!;
            var addWelcome = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_welcome"]!;
            var setSummon = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["set_summon"]!;
            var addAttack = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_attack"]!;
            var clearLines = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["clear_lines"]!;
            var onInit = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_init"]!;
            var onSummon = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_summon"]!;
            var onDirectAttack = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_directattack"]!;
            var onCustom = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_custom"]!;
            var onMessage = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_message"]!;
            var onDispose = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_dispose"]!;

            // 顶层共享设置
            setFacedown("顶层里侧怪兽");

            dialogFn("PersonaAlpha");
                addWelcome("ALPHA_ONLY", null, null);
            dialogEndFn();

            dialogFn("PersonaBeta");
                setLocale("zh-CN");
                setDescription("xmake-style persona");
                setAuthor("tester");
                setFallback("base-fallback");
                addWelcome("BETA_WELCOME", null, null);
                setSummon("STATIC_SUMMON:{0}", null, null);
                addAttack("{0}->ATK->{1}", null, null);
                // 清空 endturn，验证显式 clear_lines 阻止回退继承
                clearLines("endturn");
                // 延迟解析函数名
                onSummon("format_beta_summon");
                onDirectAttack(new FakeRawLuaClosure(args =>
                {
                    string attacker = (string)args[1]!;
                    return attacker == "Silent" ? "" : null;
                }));
                onCustom(new FakeRawLuaClosure(args => $"CUSTOM#{args[1]}:{args[2]}"));
                onMessage(new FakeRawLuaClosure(args =>
                {
                    string eventName = (string)args[1]!;
                    string text = (string)args[2]!;
                    return eventName == "welcome" ? "[PREFIX]" + text : null;
                }));
                onInit(new FakeRawLuaClosure(args =>
                {
                    initCalls++;
                    var ctx = (WindBot.Game.AI.Lua.LuaDialogContext)args[0]!;
                    initSawMergedFallback = ctx.GetLines("surrender").Contains("FALLBACK_SURRENDER");
                    return null;
                }));
                onDispose(new FakeRawLuaClosure(_ =>
                {
                    disposeCalls++;
                    return null;
                }));
            dialogEndFn();

            // 在脚本尾部定义具名回调
            context.Functions["format_beta_summon"] = new FakeLuaFunction(args =>
            {
                string monster = (string)args[1]!;
                return monster == "BlueEyes" ? "SPECIAL_BLUE_EYES" : null;
            });
        });

        WindBot.Game.AI.Lua.InMemoryLuaDialogScriptProvider provider = new();
        provider.Add("PersonaBeta", "-- xmake dialog dsl");

        try
        {
            Environment.CurrentDirectory = tempDir;
            WindBot.Game.AI.DialogsManager.ResetLua();
            WindBot.Game.AI.DialogsManager.ConfigureLua(runtime, provider);

            (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
                InMemoryNetworkTransport.CreatePair();
            using WindBot.Game.GameClient client = new(
                new WindBot.WindBotInfo { Name = "BetaBot", Deck = "AI_OldSchool", Dialog = "PersonaBeta", Chat = true, ChatIntervalMilliseconds = 0, DuelStartChatDelayMilliseconds = 0 },
                TestNamedRepository(),
                clientTransport);

            WindBot.Game.AI.Dialogs? dialogs = null;
            List<string> chats = CaptureDialogChatMessages(client, clientTransport, serverTransport, c =>
            {
                WindBot.Game.Duel duel = new();
                duel.Turn = 3;
                dialogs = new WindBot.Game.AI.Dialogs(c, duel);

                AssertTrue(dialogs.LuaScript != null);
                AssertEqual("PersonaBeta", dialogs.LuaScript!.Dsl.DeclaredDialogName);
                AssertEqual("zh-CN", dialogs.LuaScript.Dsl.DeclaredLocale);
                AssertEqual("xmake-style persona", dialogs.LuaScript.Dsl.DeclaredDescription);
                AssertEqual("tester", dialogs.LuaScript.Dsl.DeclaredAuthor);
                AssertEqual("顶层里侧怪兽", dialogs.FacedownMonsterName);
                AssertEqual(2, initCalls); // 1 次来自 GameBehavior 内 GameAI，1 次来自显式 dialogs
                AssertTrue(initSawMergedFallback);

                dialogs.SendWelcome();
                dialogs.SendDeckSorry("BadCard");
                dialogs.SendEndTurn();
                dialogs.SendSummon("BlueEyes");
                dialogs.SendSummon("OtherMonster");
                dialogs.SendAttack("Dragon", "monster");
                dialogs.SendDirectAttack("Silent");
                dialogs.SendCustomChat(2, "OK");
            });

            AssertEqual(6, chats.Count);
            AssertEqual("[PREFIX]BETA_WELCOME", chats[0]);
            AssertEqual("FALLBACK_DECK_ERROR:BadCard", chats[1]);
            AssertEqual("SPECIAL_BLUE_EYES", chats[2]);
            AssertEqual("STATIC_SUMMON:OtherMonster", chats[3]);
            AssertEqual("Dragon->ATK->顶层里侧怪兽", chats[4]);
            AssertEqual("CUSTOM#2:OK", chats[5]);

            dialogs!.Dispose();
            client.Dispose();
            AssertEqual(2, disposeCalls);
            AssertTrue(dialogs.LuaScript == null);
            // 确认 XLua 释放安全：全部上下文与 ILuaFunction 均已释放
            AssertTrue(runtime.CreatedContexts.All(c => c.Disposed));
            AssertTrue(runtime.CreatedContexts.All(c => c.TrackedFunctions.All(f => f.Disposed)));
        }
        finally
        {
            WindBot.Game.AI.DialogsManager.ResetLua();
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void WindBotLuaDialogSupportsModuleTablePriorityAndErrorIsolation()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpLuaDialogMod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Dialogs"));
        File.WriteAllText(
            Path.Combine(tempDir, "Dialogs", "shared.json"),
            """
            {
              "welcome": ["JSON_WELCOME"],
              "summon": ["JSON_SUMMON:{0}"]
            }
            """);

        List<FakeLuaTable> createdTables = new();
        FakeLuaRuntime runtime = new(context =>
        {
            FakeLuaTable table = new();
            table.Fields["welcome"] = new[] { "LUA_TABLE_WELCOME" };
            table.Fields["summon"] = new[] { "LUA_TABLE_SUMMON:{0}" };
            table.Functions["OnSummon"] = new FakeLuaFunction(_ => throw new InvalidOperationException("boom in dialog hook"));
            createdTables.Add(table);
            context.ModuleResult = table;
        });

        WindBot.Game.AI.Lua.InMemoryLuaDialogScriptProvider provider = new();
        provider.Add("shared", "-- return module table");

        try
        {
            Environment.CurrentDirectory = tempDir;
            WindBot.Game.AI.DialogsManager.ResetLua();

            // 1. 默认 PreferLuaDialogs=false 且无 AllowOverride 时，同名优先使用 JSON
            WindBot.Game.AI.DialogsManager.ConfigureLua(runtime, provider, preferLuaDialogs: false, throwOnScriptError: false);
            WindBot.Game.GameClient jsonClient = new(new WindBot.WindBotInfo { Dialog = "shared", Chat = true }, TestNamedRepository());
            using (WindBot.Game.AI.Dialogs jsonDialogs = new(jsonClient))
            {
                AssertTrue(jsonDialogs.LuaScript == null);
                AssertEqual("JSON_WELCOME", jsonDialogs.GetLines("welcome")[0]);
            }

            // 2. 注册 AllowOverride=true 描述符后，优先走 Lua 模块表，且异常在 throwOnScriptError=false 时隔离并回退静态台词
            WindBot.Game.AI.DialogsManager.RegisterLuaDialog(
                new WindBot.Game.AI.Lua.LuaDialogDescriptor("shared", "shared", allowOverride: true));
            (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
                InMemoryNetworkTransport.CreatePair();
            using WindBot.Game.GameClient luaClient = new(
                new WindBot.WindBotInfo { Deck = "AI_OldSchool", Dialog = "shared", Chat = true, ChatIntervalMilliseconds = 0, DuelStartChatDelayMilliseconds = 0 },
                TestNamedRepository(),
                clientTransport);

            List<string> chats = CaptureDialogChatMessages(luaClient, clientTransport, serverTransport, c =>
            {
                using WindBot.Game.AI.Dialogs luaDialogs = new(c);
                AssertTrue(luaDialogs.LuaScript != null);
                luaDialogs.SendWelcome();
                luaDialogs.SendSummon("DarkMagician");
            });

            AssertEqual(2, chats.Count);
            AssertEqual("LUA_TABLE_WELCOME", chats[0]);
            AssertEqual("LUA_TABLE_SUMMON:DarkMagician", chats[1]);

            luaClient.Dispose();
            AssertTrue(createdTables.Count > 0 && createdTables.All(t => t.Disposed));

            // 3. throwOnScriptError=true 时向上抛出异常
            FakeLuaTable throwingTable = new();
            throwingTable.Functions["OnWelcome"] = new FakeLuaFunction(_ => throw new InvalidOperationException("strict error"));
            FakeLuaRuntime strictRuntime = new(ctx => ctx.ModuleResult = throwingTable);
            WindBot.Game.AI.DialogsManager.ResetLua();
            WindBot.Game.AI.DialogsManager.ConfigureLua(strictRuntime, provider, preferLuaDialogs: true, throwOnScriptError: true);

            using (WindBot.Game.AI.Dialogs strictDialogs = new(jsonClient))
            {
                bool threw = false;
                try
                {
                    strictDialogs.SendWelcome();
                }
                catch (InvalidOperationException ex)
                {
                    threw = ex.Message.Contains("strict error", StringComparison.Ordinal);
                }

                AssertTrue(threw);
            }

            // 4. 验证 GameBehavior 在加载卡组失败时也会释放 Lua Dialogs 资源
            FakeLuaTable failDeckDialogTable = new();
            failDeckDialogTable.Functions["OnWelcome"] = new FakeLuaFunction(_ => "hi");
            FakeLuaRuntime failDeckRuntime = new(ctx => ctx.ModuleResult = failDeckDialogTable);
            WindBot.Game.AI.DialogsManager.ResetLua();
            WindBot.Game.AI.DialogsManager.ConfigureLua(failDeckRuntime, provider, preferLuaDialogs: true);

            WindBot.Game.GameClient missingDeckClient = new(
                new WindBot.WindBotInfo { Deck = "AI_OldSchool", DeckFile = "NonExistentDeckForDialogDispose", Dialog = "shared" },
                TestNamedRepository());
            bool deckThrew = false;
            try
            {
                _ = new WindBot.Game.GameBehavior(missingDeckClient, TestNamedRepository());
            }
            catch (InvalidOperationException)
            {
                deckThrew = true;
            }

            AssertTrue(deckThrew);
            AssertTrue(failDeckDialogTable.Disposed);
            AssertTrue(failDeckRuntime.CreatedContexts.All(c => c.Disposed));
        }
        finally
        {
            WindBot.Game.AI.DialogsManager.ResetLua();
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void WindBotDialogsSupportsLocalizationAndConsolidatedFolders()
    {
        INamedCardRepository cardRepo = TestNamedRepository(TestNamedCard(89631139));

        try
        {
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.DialogsManager.ResetLua();
            WindBot.WindBotRuntime.Configure(new WindBot.WindBotRuntimeOptions
            {
                CardRepository = cardRepo,
                LuaRuntime = NLuaRuntime.Instance
            });

            // 1. 验证默认无参数配置即可从输出目录集中位置自动发现 Decks/Lua 与 Dialogs/Lua 脚本
            WindBot.Game.AI.Lua.FileSystemLuaDeckScriptProvider defaultDeckProvider = new();
            AssertTrue(defaultDeckProvider.TryGetScriptSource("ExampleBlueEyes", out string? deckSrc, out _));
            AssertTrue(!string.IsNullOrWhiteSpace(deckSrc));

            WindBot.Game.AI.Lua.FileSystemLuaDialogScriptProvider defaultDialogProvider = new();
            AssertTrue(defaultDialogProvider.TryGetScriptSource("ExampleBlueEyes", out string? dialogSrc, out _));
            AssertTrue(!string.IsNullOrWhiteSpace(dialogSrc));

            // 2. JSON 自动本地化：Dialog="cirno" + Locale="zh-CN" -> 自动命中 cirno.zh-CN.json
            WindBot.Game.GameClient cirnoClient = new(
                new WindBot.WindBotInfo { Dialog = "cirno", Locale = "zh-CN", Chat = true },
                cardRepo);
            using (WindBot.Game.AI.Dialogs cirnoDialogs = new(cirnoClient))
            {
                AssertEqual("cirno.zh-CN", cirnoDialogs.ResolvedDialogName);
                AssertTrue(cirnoDialogs.GetLines("welcome").Any(line => line.Contains("最强", StringComparison.Ordinal)));
            }

            // 3. JSON 默认本地化：Dialog="default" + Locale="zh-CN" -> 自动命中 zh-CN.json
            WindBot.Game.GameClient defaultZhClient = new(
                new WindBot.WindBotInfo { Dialog = "default", Locale = "zh-CN", Chat = true },
                cardRepo);
            using (WindBot.Game.AI.Dialogs defaultZhDialogs = new(defaultZhClient))
            {
                AssertEqual("zh-CN", defaultZhDialogs.ResolvedDialogName);
                AssertTrue(defaultZhDialogs.GetLines("welcome").Any(line => line.Contains("机器人", StringComparison.Ordinal)));
            }

            // 4. 拆分文件本地化：优先命中 <name>.<locale>.lua，未命中时回退 <name>.lua + is_locale(...)
            WindBot.Game.AI.Lua.InMemoryLuaDialogScriptProvider splitProvider = new();
            splitProvider.Add("SplitHero", """
                dialog("SplitHero")
                    if is_locale("ja") then
                        add_welcome("こんにちは、ヒーローだ！")
                    else
                        add_welcome("Hello from base SplitHero!")
                    end
                dialog_end()
                """);
            splitProvider.Add("SplitHero.zh-CN", """
                dialog("SplitHero.zh-CN")
                    add_welcome("你好，来自拆分中文文件 SplitHero.zh-CN.lua！")
                dialog_end()
                """);

            WindBot.Game.AI.DialogsManager.ConfigureLua(NLuaRuntime.Instance, splitProvider, preferLuaDialogs: true, throwOnScriptError: true);

            using (WindBot.Game.AI.Dialogs zhSplit = new(new WindBot.Game.GameClient(
                new WindBot.WindBotInfo { Dialog = "SplitHero", Locale = "zh-CN" }, cardRepo)))
            {
                AssertEqual("SplitHero.zh-CN.lua", zhSplit.ResolvedDialogName);
                AssertEqual("你好，来自拆分中文文件 SplitHero.zh-CN.lua！", zhSplit.GetLines("welcome")[0]);
            }

            using (WindBot.Game.AI.Dialogs jaFallback = new(new WindBot.Game.GameClient(
                new WindBot.WindBotInfo { Dialog = "SplitHero", Locale = "ja-JP" }, cardRepo)))
            {
                AssertEqual("SplitHero.lua", jaFallback.ResolvedDialogName);
                AssertEqual("こんにちは、ヒーローだ！", jaFallback.GetLines("welcome")[0]);
            }

            // 5. 跨 .lua 的 set_fallback 与子目录 Dialogs/Lua/<name>/<locale>.lua 解析
            string nestedTempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpNestedLuaDialogs_" + Guid.NewGuid().ToString("N"));
            try
            {
                string charDir = Path.Combine(nestedTempDir, "MyChar");
                Directory.CreateDirectory(charDir);
                File.WriteAllText(Path.Combine(nestedTempDir, "BasePersona.lua"), """
                    dialog("BasePersona")
                        add_surrender("BASE_LUA_SURRENDER")
                    dialog_end()
                    """);
                File.WriteAllText(Path.Combine(charDir, "zh-CN.lua"), """
                    dialog("MyChar")
                        set_fallback("BasePersona")
                        add_welcome("NESTED_ZH_WELCOME")
                    dialog_end()
                    """);

                WindBot.Game.AI.DialogsManager.ConfigureLua(
                    NLuaRuntime.Instance,
                    new WindBot.Game.AI.Lua.FileSystemLuaDialogScriptProvider(nestedTempDir),
                    preferLuaDialogs: true,
                    throwOnScriptError: true);

                using WindBot.Game.AI.Dialogs nestedDialog = new(new WindBot.Game.GameClient(
                    new WindBot.WindBotInfo { Dialog = "MyChar", Locale = "zh-CN" }, cardRepo));
                AssertEqual("NESTED_ZH_WELCOME", nestedDialog.GetLines("welcome")[0]);
                AssertEqual("BASE_LUA_SURRENDER", nestedDialog.GetLines("surrender")[0]);
            }
            finally
            {
                Directory.Delete(nestedTempDir, recursive: true);
            }
        }
        finally
        {
            WindBot.WindBotRuntime.Locale = null;
            WindBot.Game.AI.DecksManager.ResetLua();
            WindBot.Game.AI.DialogsManager.ResetLua();
        }
    }

    [Fact]
    public void WindBotChatPacerPacesBurstsAndRespectsDelay()
    {
        (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
            InMemoryNetworkTransport.CreatePair();
        List<string> received = new();
        YGOClient serverPeer = new(serverTransport);
        serverPeer.PacketReceivedRaw += packet =>
        {
            if (packet.Length > 1 && packet.Span[0] == (byte)CtosMessage.Chat)
                received.Add(Encoding.Unicode.GetString(packet.Span.Slice(1)).TrimEnd('\0'));
        };

        using WindBot.Game.GameClient client = new(
            new WindBot.WindBotInfo
            {
                Name = "PacedBot",
                Deck = "AI_OldSchool",
                Dialog = "default",
                Chat = true,
                ChatIntervalMilliseconds = 60,
                DuelStartChatDelayMilliseconds = 80,
                MaxQueuedChatMessages = 3
            },
            TestNamedRepository(),
            clientTransport);

        client.Start(clientTransport);
        serverPeer.Update();
        received.Clear();

        // 1. 首条立即发送，后续密集 5 条按 MaxQueuedChatMessages=3 丢弃最旧并排队（保留 msg4, msg5, msg6）
        client.Chat("msg1");
        client.Chat("msg2");
        client.Chat("msg3");
        client.Chat("msg4");
        client.Chat("msg5");
        client.Chat("msg6");

        serverPeer.Update();
        AssertEqual(1, received.Count);
        AssertEqual("msg1", received[0]);
        AssertEqual(3, client.PendingChatCount);

        // 2. 逐次等待节流间隔并 Tick，按顺序逐条发出（不一次性倾倒）
        for (int i = 0; i < 3; i++)
        {
            Thread.Sleep(75);
            client.Tick();
            serverPeer.Update();
            AssertEqual(i + 2, received.Count);
        }

        AssertEqual(4, received.Count);
        AssertEqual("msg1", received[0]);
        AssertEqual("msg4", received[1]);
        AssertEqual("msg5", received[2]);
        AssertEqual("msg6", received[3]);
        AssertEqual(0, client.PendingChatCount);

        // 3. DelayChat 延后首条聊天，未到期不发，到期后发出
        received.Clear();
        client.DelayChat(90);
        client.Chat("after-delay");
        client.Tick();
        serverPeer.Update();
        AssertEqual(0, received.Count);

        Thread.Sleep(110);
        client.Tick();
        serverPeer.Update();
        AssertEqual(1, received.Count);
        AssertEqual("after-delay", received[0]);
        serverPeer.Close();
    }

    [Fact]
    public void WindBotBundledDecksAndDialogsAssetsLoadFromOutputDirectory()
    {
        string decksDir = Path.Combine(AppContext.BaseDirectory, "Decks");
        string dialogsDir = Path.Combine(AppContext.BaseDirectory, "Dialogs");

        AssertTrue(Directory.Exists(decksDir));
        AssertTrue(Directory.Exists(dialogsDir));

        string[] deckFiles = Directory.GetFiles(decksDir, "*.ydk");
        string[] dialogFiles = Directory.GetFiles(dialogsDir, "*.json");
        AssertTrue(deckFiles.Length >= 71);
        AssertTrue(dialogFiles.Length >= 20);

        // 验证通过 WindBotRuntime.ReadFile 直接解析内置 .ydk 文件
        using (FileStream oldSchool = WindBot.WindBotRuntime.ReadFile("Decks", "AI_OldSchool", "ydk"))
        {
            AssertTrue(oldSchool.Length > 0);
        }

        // 验证全部内置 Dialogs JSON 均可正常反序列化
        foreach (string dialogFile in dialogFiles)
        {
            string dialogName = Path.GetFileNameWithoutExtension(dialogFile);
            WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
            {
                Dialog = dialogName,
                Chat = false
            }, TestNamedRepository());

            WindBot.Game.AI.Dialogs dialogs = new(client);
            dialogs.SendWelcome();
        }
    }

}
