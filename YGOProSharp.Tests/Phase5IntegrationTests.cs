namespace YGOProSharp.Tests;

public sealed class Phase5IntegrationTests
{
    [Fact]
    public async Task Phase5TagForceDeckSixDimEvaluationPostDuelReviewAndCareerMemoryWorkEndToEndAsync()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "YGOProSharpPhase5_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "script"));

        try
        {
            // 准备覆盖怪兽（调整/效果检索+无效）、魔法、陷阱、同调额外与 DIY 卡的测试卡库
            string cdbPath = Path.Combine(tempRoot, "cards.cdb");
            using (SqliteConnection conn = new($"Data Source={cdbPath};Pooling=False"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE datas (id INTEGER PRIMARY KEY, ot INTEGER, alias INTEGER, setcode INTEGER, type INTEGER, atk INTEGER, def INTEGER, level INTEGER, race INTEGER, attribute INTEGER, category INTEGER);
                    CREATE TABLE texts (id INTEGER PRIMARY KEY, name TEXT, desc TEXT, str1 TEXT, str2 TEXT, str3 TEXT, str4 TEXT, str5 TEXT, str6 TEXT, str7 TEXT, str8 TEXT, str9 TEXT, str10 TEXT, str11 TEXT, str12 TEXT, str13 TEXT, str14 TEXT, str15 TEXT, str16 TEXT);
                    -- 4星调整效果怪兽（含“从卡组...加入手卡”与“特殊召唤”词频）
                    INSERT INTO datas VALUES (91000001, 3, 0, 4119, 4129, 1300, 500, 3, 2, 8, 0);
                    INSERT INTO texts VALUES (91000001, '废品同调士·改', '①：这张卡召唤成功时才能发动。从卡组把1只2星怪兽加入手卡，那之后可从墓地特殊召唤1只怪兽。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    -- 8星同调怪兽（含“发动无效并破坏”词频）
                    INSERT INTO datas VALUES (91000002, 3, 0, 163, 8225, 2500, 2000, 8, 8192, 8, 0);
                    INSERT INTO texts VALUES (91000002, '星尘龙·极', '调整＋调整以外的怪兽1只以上。①：对方把场上卡破坏的效果发动时，把这张卡解放才能发动。那个发动无效并破坏。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    -- 通常魔法（抽卡）
                    INSERT INTO datas VALUES (91000003, 3, 0, 0, 2, 0, 0, 0, 0, 0, 0);
                    INSERT INTO texts VALUES (91000003, '调律星芒', '①：自己抽1张。那之后从卡组上面把1张卡除外。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    -- 反击陷阱（DIY ot=4，含“不能特殊召唤”封锁词频）
                    INSERT INTO datas VALUES (91000004, 4, 0, 163, 1048580, 0, 0, 0, 0, 0, 0);
                    INSERT INTO texts VALUES (91000004, '星尘闪光结界', '①：怪兽的效果发动时才能发动。那个发动无效，这个回合对方不能特殊召唤怪兽。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    """;
                cmd.ExecuteNonQuery();
                for (int filler = 91001001; filler <= 91001036; filler++)
                {
                    using SqliteCommand fillCmd = conn.CreateCommand();
                    fillCmd.CommandText = $"""
                        INSERT INTO datas VALUES ({filler}, 3, 0, 163, 17, 1800, 1200, 4, 1, 16, 0);
                        INSERT INTO texts VALUES ({filler}, '星光战士{filler - 91001000}', '并肩作战的同调搭档战士。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                        """;
                    fillCmd.ExecuteNonQuery();
                }
            }

            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "constant.lua"), "-- constants\n");
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "utility.lua"), "function GetID() return self_table, self_code, 1 end\n");
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "procedure.lua"), "-- procedure\n");
            for (int cid = 91000001; cid <= 91000004; cid++)
                await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", $"c{cid}.lua"), "local s,id=GetID()\nfunction s.initial_effect(c)\nend\n");

            SqliteCardDatabaseManager cdbMgr = new();
            INamedCardRepository namedRepo = cdbMgr.LoadNamedCards([cdbPath]);

            // 1. 单元测试 DeckStaticProfileBuilder 标准顺序无关 Hash
            List<int> mainForward = [91000001, 91000001, 91000003, 91000004, .. Enumerable.Range(91001001, 36)];
            List<int> mainReversed = mainForward.AsEnumerable().Reverse().ToList();
            string ydkForward = "#created by test\n#main\n" + string.Join("\n", mainForward) + "\n#extra\n91000002\n91000002\n!side\n91000004\n";
            string ydkReversed = "#created by shuffled\n#main\n" + string.Join("\n", mainReversed) + "\n#extra\n91000002\n91000002\n!side\n91000004\n";
            YdkDeckData parsedFwd = YdkSerializer.Parse(ydkForward, "StardustFwd");
            YdkDeckData parsedRev = YdkSerializer.Parse(ydkReversed, "StardustRev");
            string canonicalHash = DeckStaticProfileBuilder.ComputeCanonicalDeckHash(parsedFwd);
            AssertEqual(canonicalHash, DeckStaticProfileBuilder.ComputeCanonicalDeckHash(parsedRev));

            PersistenceOptions pOpts = new()
            {
                DbProvider = OperationalDbProvider.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(tempRoot, "phase5_ops.db")}",
                ReplayDirectory = Path.Combine(tempRoot, "replays"),
                BootstrapAdminUser = "admin",
                BootstrapAdminPassword = "AdminPass123",
                Argon2MemoryKb = 4096,
                Argon2Iterations = 1,
                Argon2Parallelism = 1
            };

            await using OperationalStore store = new(pOpts);
            await store.InitializeAsync();
            using OperationalServerCoordinator coordinator = new(store);

            ServerOptions srvOpts = new()
            {
                RootPath = tempRoot,
                DatabaseFile = "cards.cdb",
                ScriptDirectory = "script",
                EnableNetworkListener = false,
                EnableMultiRoom = true,
                AddonFactories = [coordinator.CreateRoomAddonFactory()],
                JoinAccessValidator = coordinator.CreateJoinAccessValidator(),
                Game = new GameOptions
                {
                    MasterRule = 5,
                    NoCheckDeck = true
                }
            };

            using NativeOcgRuntime ocgRuntime = new();
            await using YGOProSharpServerHost serverHost = YGOProSharpServerHost.Create(srvOpts, ocgRuntime);
            serverHost.StartBackgroundLoop(tickDelayMilliseconds: 1, embedded: true);

            await using UserPortalHost portalHost = new(
                serverHost,
                store,
                coordinator,
                new UserPortalOptions
                {
                    Host = "127.0.0.1",
                    Port = 0,
                    NamedCardRepositoryProvider = () => namedRepo
                });
            await portalHost.StartAsync();
            Uri portalBase = new(portalHost.BoundAddresses.First());

            using HttpClient http = new();

            // 注册玩家门户账号并签发 4 个 2v2 对战 player Token + 1 个只读 spectator 解说复盘 Token
            using HttpResponseMessage regResp = await http.PostAsync(
                new Uri(portalBase, "/api/portal/auth/register"),
                new StringContent("""{"username":"tag_captain","password":"CaptainPassword9!","displayName":"武藤游戏"}""", Encoding.UTF8, "application/json"));
            AssertEqual((int)HttpStatusCode.OK, (int)regResp.StatusCode);
            string portalBearer;
            using (JsonDocument regDoc = JsonDocument.Parse(await regResp.Content.ReadAsStringAsync()))
            {
                portalBearer = regDoc.RootElement.GetProperty("accessToken").GetString()!;
            }

            async Task<string> IssuePortalTokenAsync(string nick, string role, string? persona = null)
            {
                using HttpRequestMessage req = new(HttpMethod.Post, new Uri(portalBase, "/api/portal/mcp/tokens"));
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", portalBearer);
                req.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    name = nick,
                    preferredPlayerName = role == "player" ? nick : null,
                    personaStyle = persona,
                    mcpRole = role,
                    maxSessions = 2,
                    expiresInHours = 24
                }), Encoding.UTF8, "application/json");
                using HttpResponseMessage resp = await http.SendAsync(req);
                AssertEqual((int)HttpStatusCode.OK, (int)resp.StatusCode);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("rawToken").GetString()!;
            }

            string tokA1 = await IssuePortalTokenAsync("不动游星", "player", "沉稳同调加速流");
            string tokA2 = await IssuePortalTokenAsync("杰克亚特兰斯", "player", "孤高红莲王者");
            string tokB1 = await IssuePortalTokenAsync("海马濑人", "player", "傲岸青眼社长");
            string tokB2 = await IssuePortalTokenAsync("城之内克也", "player", "热血赌狗魂系");
            string tokSpec = await IssuePortalTokenAsync("场外解说员", "spectator");

            async Task<JsonDocument> SendMcpRpcAsync(string rawTok, string method, object @params)
            {
                string rpc = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params });
                using HttpResponseMessage resp = await http.PostAsync(
                    new Uri(portalBase, $"/mcp?key={Uri.EscapeDataString(rawTok)}"),
                    new StringContent(rpc, Encoding.UTF8, "application/json"));
                AssertEqual((int)HttpStatusCode.OK, (int)resp.StatusCode);
                return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            }

            async Task<(bool IsError, JsonDocument Body)> CallMcpToolAsync(string rawTok, string toolName, object arguments)
            {
                using JsonDocument env = await SendMcpRpcAsync(rawTok, "tools/call", new { name = toolName, arguments });
                JsonElement res = env.RootElement.GetProperty("result");
                bool isErr = res.TryGetProperty("isError", out JsonElement ie) && ie.GetBoolean();
                string text = res.GetProperty("content")[0].GetProperty("text").GetString()!;
                return (isErr, JsonDocument.Parse(text));
            }

            // 2. 验证 Phase 5A: initialize 携带 personaStyle 指引、Phase 5 三大 Prompt 可读、生涯记忆有界 50 条裁剪
            using (JsonDocument initDoc = await SendMcpRpcAsync(tokA1, "initialize", new { protocolVersion = "2025-03-26" }))
            {
                string instr = initDoc.RootElement.GetProperty("result").GetProperty("instructions").GetString()!;
                AssertTrue(instr.Contains("沉稳同调加速流", StringComparison.Ordinal));
            }

            using (JsonDocument pListDoc = await SendMcpRpcAsync(tokA1, "prompts/list", new { }))
            {
                string[] pNames = pListDoc.RootElement.GetProperty("result").GetProperty("prompts")
                    .EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray();
                AssertTrue(pNames.Contains("play-tag-force-partner"));
                AssertTrue(pNames.Contains("evaluate-deck-six-dims"));
                AssertTrue(pNames.Contains("review-finished-duel"));
            }

            for (int m = 1; m <= 53; m++)
            {
                using ((await CallMcpToolAsync(tokA1, "duel_save_memory", new
                {
                    deckHash = canonicalHash,
                    opponentArchetype = m % 2 == 0 ? "青眼龙族" : "珠泪哀歌族",
                    opponentName = "海马濑人",
                    result = "win",
                    strategicNote = $"第{m}局实战备忘：优先保留星尘闪光结界拦停核心展开"
                })).Body) { }
            }

            var (memErr, memDoc) = await CallMcpToolAsync(tokA1, "duel_get_memories", new { deckHash = canonicalHash, limit = 50 });
            using (memDoc)
            {
                AssertFalse(memErr);
                AssertEqual(50, memDoc.RootElement.GetProperty("count").GetInt32());
            }

            // 3. 验证 Phase 5C: 卡组静态剖析 + 0~100 六维整数边界校验 + Web 雷达接口查询
            var (anaErr, anaDoc) = await CallMcpToolAsync(tokSpec, "deck_analyze_features", new { ydkContent = ydkForward, deckName = "游星同调构筑" });
            using (anaDoc)
            {
                AssertFalse(anaErr);
                JsonElement prof = anaDoc.RootElement.GetProperty("profile");
                AssertEqual(canonicalHash, prof.GetProperty("deckHash").GetString()!);
                AssertEqual(40, prof.GetProperty("mainCount").GetInt32());
                AssertEqual(2, prof.GetProperty("extraCount").GetInt32());
                AssertEqual(38, prof.GetProperty("lowLevelMonsterCopies").GetInt32());
                AssertEqual(1, prof.GetProperty("normalSpellCopies").GetInt32());
                AssertEqual(1, prof.GetProperty("counterTrapCopies").GetInt32());
                AssertEqual(2, prof.GetProperty("synchroCopies").GetInt32());
                AssertTrue(prof.GetProperty("diyCardCopies").GetInt32() >= 1);
                JsonElement kw = prof.GetProperty("keywordStats");
                AssertTrue(kw.GetProperty("searchOrAddHand").GetInt32() >= 1);
                AssertTrue(kw.GetProperty("negateOrDisable").GetInt32() >= 1);
                AssertTrue(kw.GetProperty("floodgateOrLock").GetInt32() >= 1);
            }

            // 越界分值 105 与 -2 必须被严格拦截
            var (badEvalErr1, badEvalDoc1) = await CallMcpToolAsync(tokSpec, "deck_submit_evaluation", new
            {
                deckHash = canonicalHash,
                burst_power = 105,
                resilience = 80,
                stun_toxicity = 40,
                fun_factor = 75,
                complexity = 70,
                meta_adaptability = 82
            });
            using (badEvalDoc1)
            {
                AssertTrue(badEvalErr1);
                AssertEqual("invalid_dimension_score", badEvalDoc1.RootElement.GetProperty("error").GetString()!);
            }

            var (badEvalErr2, badEvalDoc2) = await CallMcpToolAsync(tokSpec, "deck_submit_evaluation", new
            {
                deckHash = canonicalHash,
                burst_power = 85,
                resilience = 80,
                stun_toxicity = -2,
                fun_factor = 75,
                complexity = 70,
                meta_adaptability = 82
            });
            using (badEvalDoc2)
            {
                AssertTrue(badEvalErr2);
            }

            // 提交合法 0~100 整数六维评分
            var (okEvalErr, okEvalDoc) = await CallMcpToolAsync(tokSpec, "deck_submit_evaluation", new
            {
                ydkContent = ydkForward,
                deckName = "游星同调构筑",
                archetypeTitle = "废品星尘加速同调",
                burst_power = 92,
                resilience = 88,
                stun_toxicity = 45,
                fun_factor = 95,
                complexity = 84,
                meta_adaptability = 86,
                coreRoutines = new[] { "废品同调士通召拉墓地 -> 同调星尘龙·极 -> 盖星尘闪光结界护航" },
                strengths = new[] { "终端自带破坏无效且后场反击封锁特召" },
                weaknesses = new[] { "通召点吃无限泡影容易停牌" },
                commentary = "兼具魂度与高强度压制的经典同调构筑。"
            });
            using (okEvalDoc)
            {
                AssertFalse(okEvalErr);
                AssertEqual("S+", okEvalDoc.RootElement.GetProperty("evaluation").GetProperty("overallTier").GetString()!);
            }

            // 用乱序版 ydkReversed 请求 Web 门户 /api/portal/decks/analyze，验证顺序无关 Hash 自动命中刚才保存的 S+ 雷达评测！
            using (HttpResponseMessage portalAnaResp = await http.PostAsync(
                new Uri(portalBase, "/api/portal/decks/analyze"),
                new StringContent(JsonSerializer.Serialize(new { ydkContent = ydkReversed }), Encoding.UTF8, "application/json")))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)portalAnaResp.StatusCode);
                using JsonDocument portalAnaDoc = JsonDocument.Parse(await portalAnaResp.Content.ReadAsStringAsync());
                AssertEqual(canonicalHash, portalAnaDoc.RootElement.GetProperty("profile").GetProperty("deckHash").GetString()!);
                JsonElement evalNode = portalAnaDoc.RootElement.GetProperty("evaluation");
                AssertEqual("S+", evalNode.GetProperty("overallTier").GetString()!);
                AssertEqual(92, evalNode.GetProperty("dimensions").GetProperty("burst_power").GetInt32());
                AssertEqual(95, evalNode.GetProperty("dimensions").GetProperty("fun_factor").GetInt32());
            }

            // 4. 验证 Phase 5B: 2v2《卡片力量》四席轮转拓扑、同队私密战术协同与网页指令盘
            using (HttpRequestMessage createTagReq = new(HttpMethod.Post, new Uri(portalBase, "/api/portal/rooms")))
            {
                createTagReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", portalBearer);
                createTagReq.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    roomName = "TagForce01",
                    mode = 2,
                    masterRule = 5,
                    noCheckDeck = true,
                    randomDeckMode = "ChaosDraft"
                }), Encoding.UTF8, "application/json");
                using HttpResponseMessage createTagResp = await http.SendAsync(createTagReq);
                AssertEqual((int)HttpStatusCode.OK, (int)createTagResp.StatusCode);
            }

            async Task<string> JoinAndReadyAsync(string tok)
            {
                var (jErr, jDoc) = await CallMcpToolAsync(tok, "duel_join_room", new { room = "TagForce01" });
                using (jDoc)
                {
                    AssertFalse(jErr);
                    return jDoc.RootElement.GetProperty("sessionId").GetString()!;
                }
            }

            string sid0 = await JoinAndReadyAsync(tokA1); // Seat 0 (Team 0)
            string sid1 = await JoinAndReadyAsync(tokA2); // Seat 1 (Team 0)
            string sid2 = await JoinAndReadyAsync(tokB1); // Seat 2 (Team 1)
            string sid3 = await JoinAndReadyAsync(tokB2); // Seat 3 (Team 1)

            using ((await CallMcpToolAsync(tokA2, "duel_set_ready", new { sessionId = sid1, ready = true, startIfHost = false })).Body) { }
            using ((await CallMcpToolAsync(tokB1, "duel_set_ready", new { sessionId = sid2, ready = true, startIfHost = false })).Body) { }
            using ((await CallMcpToolAsync(tokB2, "duel_set_ready", new { sessionId = sid3, ready = true, startIfHost = false })).Body) { }
            using ((await CallMcpToolAsync(tokA1, "duel_set_ready", new { sessionId = sid0, ready = true, startIfHost = true })).Body) { }

            // 2v2 猜拳由两队代表（Seat 0 与 Seat 2）进行：Seat 0 出石头(1)，Seat 2 出剪刀(0) -> Seat 0 选先攻(0)
            using ((await CallMcpToolAsync(tokA1, "duel_wait_for_turn", new { sessionId = sid0, timeoutSeconds = 5, detail = "minimal" })).Body) { }
            using ((await CallMcpToolAsync(tokB1, "duel_wait_for_turn", new { sessionId = sid2, timeoutSeconds = 5, detail = "minimal" })).Body) { }
            using ((await CallMcpToolAsync(tokA1, "duel_submit_action", new { sessionId = sid0, actionIndex = 1, detail = "none" })).Body) { }
            using ((await CallMcpToolAsync(tokB1, "duel_submit_action", new { sessionId = sid2, actionIndex = 0, detail = "none" })).Body) { }
            using ((await CallMcpToolAsync(tokA1, "duel_wait_for_turn", new { sessionId = sid0, timeoutSeconds = 5, detail = "minimal" })).Body) { }
            using ((await CallMcpToolAsync(tokA1, "duel_submit_action", new { sessionId = sid0, actionIndex = 0, detail = "none" })).Body) { }
            using ((await CallMcpToolAsync(tokA1, "duel_wait_for_turn", new { sessionId = sid0, timeoutSeconds = 5, detail = "standard" })).Body) { }

            // 验证进行中对局严禁通过 duel_get_replay_timeline 偷看全视角卡组
            var (midTimelineErr, midTimelineDoc) = await CallMcpToolAsync(tokSpec, "duel_get_replay_timeline", new { duelId = "R#1" });
            using (midTimelineDoc)
            {
                AssertTrue(midTimelineErr);
                AssertEqual("duel_not_finished_or_not_found", midTimelineDoc.RootElement.GetProperty("error").GetString()!);
            }

            // Seat 0 调用 duel_tag_sync 向同队 Seat 1 发送私密战术交接
            var (syncErr, syncDoc) = await CallMcpToolAsync(tokA1, "duel_tag_sync", new
            {
                sessionId = sid0,
                stance = "setup_partner",
                note = "下回合交给你红莲魔龙清场",
                sharedBoardHandoffNote = "秘密后场盖了星尘闪光结界切勿误拆"
            });
            using (syncDoc)
            {
                AssertFalse(syncErr);
                AssertEqual("setup_partner", syncDoc.RootElement.GetProperty("partnerTactic").GetProperty("stance").GetString()!);
            }

            await Task.Delay(120);

            // 验证 Seat 0 的 tagContext 含完整四席 0->2->1->3 轮转拓扑 + sharedBoardHandoffNote
            var (_, obs0Doc) = await CallMcpToolAsync(tokA1, "duel_get_observation", new { sessionId = sid0, detail = "standard" });
            using (obs0Doc)
            {
                JsonElement tagCtx = obs0Doc.RootElement.GetProperty("tagContext");
                AssertEqual(0, tagCtx.GetProperty("myTeamId").GetInt32());
                AssertEqual(1, tagCtx.GetProperty("partnerSeatIndex").GetInt32());
                AssertEqual("杰克亚特兰斯", tagCtx.GetProperty("partnerName").GetString()!);
                int[] rotSeats = tagCtx.GetProperty("turnRotationOrder").EnumerateArray().Select(x => x.GetProperty("seatIndex").GetInt32()).ToArray();
                AssertCollectionEqual(new[] { 0, 2, 1, 3 }, rotSeats);
                AssertTrue(tagCtx.GetProperty("sharedBoardHandoffNote").GetString()!.Contains("星尘闪光结界", StringComparison.Ordinal));
            }

            // 验证队友 Seat 1 收到了 setup_partner 私密方针，而敌方 Seat 2 及网页公开观战流绝不泄露秘密盖卡文本！
            var (_, obs1Doc) = await CallMcpToolAsync(tokA2, "duel_get_observation", new { sessionId = sid1, detail = "standard" });
            using (obs1Doc)
            {
                AssertEqual("setup_partner", obs1Doc.RootElement.GetProperty("partnerTactic").GetProperty("stance").GetString()!);
            }

            var (_, obs2Doc) = await CallMcpToolAsync(tokB1, "duel_get_observation", new { sessionId = sid2, detail = "standard" });
            using (obs2Doc)
            {
                string rawEnemyObs = obs2Doc.RootElement.GetRawText();
                AssertFalse(rawEnemyObs.Contains("星尘闪光结界", StringComparison.Ordinal));
                AssertFalse(rawEnemyObs.Contains("PartnerTactic", StringComparison.Ordinal));
            }

            using (HttpResponseMessage pubRoomResp = await http.GetAsync(new Uri(portalBase, "/api/portal/rooms/TagForce01")))
            {
                string pubRoomText = await pubRoomResp.Content.ReadAsStringAsync();
                AssertFalse(pubRoomText.Contains("星尘闪光结界", StringComparison.Ordinal));
                AssertFalse(pubRoomText.Contains("PartnerTactic", StringComparison.Ordinal));
            }

            // 验证通过网页门户「卡片力量搭档战术指令盘」向 A 队下发 all_in_otk 指令可实时唤醒更新同队 AI，且不向 B 队泄漏
            using (HttpRequestMessage padReq = new(HttpMethod.Post, new Uri(portalBase, "/api/portal/rooms/TagForce01/partner-tactic")))
            {
                padReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", portalBearer);
                padReq.Content = new StringContent("""{"teamId":0,"stance":"all_in_otk","note":"全军突击斩杀"}""", Encoding.UTF8, "application/json");
                using HttpResponseMessage padResp = await http.SendAsync(padReq);
                AssertEqual((int)HttpStatusCode.OK, (int)padResp.StatusCode);
            }

            var (_, obs1AfterPad) = await CallMcpToolAsync(tokA2, "duel_get_observation", new { sessionId = sid1, detail = "minimal" });
            using (obs1AfterPad)
            {
                AssertEqual("all_in_otk", obs1AfterPad.RootElement.GetProperty("partnerTactic").GetProperty("stance").GetString()!);
            }

            // 验证 expectedPromptId 非销毁式票据防重 + duel_submit_action 内联 planUpdate 与 chatLine 吐槽
            using ((await CallMcpToolAsync(tokA1, "duel_auto_play", new { sessionId = sid0, strategy = "pass_chains", maxSteps = 6, detail = "none" })).Body) { }
            var (staleErr, staleDoc) = await CallMcpToolAsync(tokA1, "duel_submit_action", new
            {
                sessionId = sid0,
                actionIndex = 0,
                expectedPromptId = 999999
            });
            using (staleDoc)
            {
                AssertTrue(staleErr);
                AssertTrue(staleDoc.RootElement.GetProperty("hasPendingAction").GetBoolean());
            }

            // Seat 0 提交一个带内联 planUpdate、thoughtSummary 与公开 chatLine 的动作（直接进结束阶段）
            var (_, idlePromptDoc) = await CallMcpToolAsync(tokA1, "duel_get_legal_actions", new { sessionId = sid0 });
            int endPhaseIdx = 0;
            using (idlePromptDoc)
            {
                foreach (JsonElement ch in idlePromptDoc.RootElement.GetProperty("pendingPrompt").GetProperty("choices").EnumerateArray())
                {
                    if (ch.GetProperty("kind").GetString() == "to_end_phase")
                        endPhaseIdx = ch.GetProperty("actionIndex").GetInt32();
                }
            }

            var (act5AErr, act5ADoc) = await CallMcpToolAsync(tokA1, "duel_submit_action", new
            {
                sessionId = sid0,
                actionIndex = endPhaseIdx,
                thoughtSummary = "先稳住场面把爆发轮次交接给搭档",
                chatLine = "聚集的祈愿将成为新生的闪耀之星！",
                planUpdate = new
                {
                    winCondition = "双打同调接力斩杀",
                    handoffToPartner = "留足共享 LP 给杰克"
                }
            });
            using (act5ADoc)
            {
                AssertFalse(act5AErr);
                AssertEqual("双打同调接力斩杀", act5ADoc.RootElement.GetProperty("observation").GetProperty("strategicPlan").GetProperty("winCondition").GetString()!);
            }

            await Task.Delay(100);
            // 敌方观测到公开台词时附带 untrusted_player_chat 提示
            var (_, enemyChatDoc) = await CallMcpToolAsync(tokB1, "duel_get_observation", new { sessionId = sid2, detail = "standard" });
            using (enemyChatDoc)
            {
                AssertEqual("untrusted_player_chat", enemyChatDoc.RootElement.GetProperty("chatTrustNotice").GetString()!);
                AssertTrue(enemyChatDoc.RootElement.GetProperty("recentChats").GetRawText().Contains("聚集的祈愿", StringComparison.Ordinal));
            }

            // 5. 验证 Phase 5D: B 队双人确认投降完结对局 -> 战报与时间轴归档 -> 赛后全视角复盘与生涯记忆联动
            using ((await CallMcpToolAsync(tokB1, "duel_surrender", new { sessionId = sid2 })).Body) { }
            using ((await CallMcpToolAsync(tokB2, "duel_surrender", new { sessionId = sid3 })).Body) { }

            long finishedDuelId = 0;
            DateTime waitArchiveDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
            while (DateTime.UtcNow < waitArchiveDeadline)
            {
                IReadOnlyList<DuelRecordEntity> recs = await store.ListDuelRecordsAsync(5);
                if (recs.Count > 0)
                {
                    finishedDuelId = recs[0].Id;
                    break;
                }
                await Task.Delay(50);
            }
            AssertTrue(finishedDuelId > 0);

            // 对局已完结归档：只读 spectator 角色现在可以合法读取 R#<id> 的全视角构筑与公开时间轴（且不含私密 PartnerTactic 与 ClientIp）
            var (tlErr, tlDoc) = await CallMcpToolAsync(tokSpec, "duel_get_replay_timeline", new { duelId = $"R#{finishedDuelId}" });
            using (tlDoc)
            {
                AssertFalse(tlErr);
                AssertEqual(4, tlDoc.RootElement.GetProperty("players").GetArrayLength());
                string tlRaw = tlDoc.RootElement.GetRawText();
                AssertTrue(tlRaw.Contains("聚集的祈愿", StringComparison.Ordinal));
                AssertTrue(tlRaw.Contains("先稳住场面", StringComparison.Ordinal));
                AssertFalse(tlRaw.Contains("星尘闪光结界", StringComparison.Ordinal));
                AssertFalse(tlRaw.Contains("clientIp", StringComparison.OrdinalIgnoreCase));
            }

            // 越界 playScore=120 必须被拒绝
            var (badRevErr, badRevDoc) = await CallMcpToolAsync(tokA1, "duel_submit_review", new
            {
                duelId = $"R#{finishedDuelId}",
                headline = "非法分数测试",
                summary = "越界分数应被拦截",
                playerRatings = new[] { new { playerName = "不动游星", playScore = 120 } }
            });
            using (badRevDoc)
            {
                AssertTrue(badRevErr);
            }

            // 提交完整结构化赛后复盘报告，并顺带通过 lessonForMemory 同步写入跨局生涯记忆
            var (revErr, revDoc) = await CallMcpToolAsync(tokA1, "duel_submit_review", new
            {
                duelId = $"R#{finishedDuelId}",
                headline = "A队双打战术无缝衔接迫使对手投降",
                summary = "游星与杰克通过私密战术盘精准统一节奏，稳健过渡第一回合锁定胜局。",
                reviewerRole = "self_player",
                playerRatings = new object[]
                {
                    new { seatIndex = 0, playerName = "不动游星", playScore = 95, teamworkScore = 98, mvp = true, highlights = new[] { "精准交接共享场面" } },
                    new { seatIndex = 1, playerName = "杰克亚特兰斯", playScore = 91, teamworkScore = 96, mvp = false },
                    new { seatIndex = 2, playerName = "海马濑人", playScore = 72, teamworkScore = 68, mistakes = new[] { "过早交出核心阻抗" } },
                    new { seatIndex = 3, playerName = "城之内克也", playScore = 70, teamworkScore = 70 }
                },
                turningPoints = new object[]
                {
                    new { turn = 1, phase = "Main1", actor = "不动游星", impact = "game_winning", analysis = "稳健留场并与搭档完成战术同步" }
                },
                observedPatterns = new object[]
                {
                    new { playerName = "不动游星", patternName = "卡片力量双打交接体系", effectiveness = "极佳" }
                },
                lessonForMemory = "2v2 双打结束回合前务必用 duel_tag_sync 叮嘱队友后场盖卡用途"
            });
            using (revDoc)
            {
                AssertFalse(revErr);
                AssertTrue(revDoc.RootElement.TryGetProperty("savedMemoryId", out JsonElement memId) && memId.ValueKind == JsonValueKind.String);
            }

            // 验证 lessonForMemory 已沉淀进生涯记忆库
            var (_, lessonMemDoc) = await CallMcpToolAsync(tokA1, "duel_get_memories", new { opponentKeyword = "duel_tag_sync", limit = 5 });
            using (lessonMemDoc)
            {
                AssertTrue(lessonMemDoc.RootElement.GetProperty("count").GetInt32() >= 1);
            }

            // 验证 Web 门户 GET /api/portal/replays/{id}/detail 可完整读取复盘评分、转折点与时间轴，且零 IP 泄漏
            using (HttpResponseMessage repDetailResp = await http.GetAsync(new Uri(portalBase, $"/api/portal/replays/{finishedDuelId}/detail")))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)repDetailResp.StatusCode);
                string repRaw = await repDetailResp.Content.ReadAsStringAsync();
                AssertFalse(repRaw.Contains("clientIp", StringComparison.OrdinalIgnoreCase));
                using JsonDocument repDetailDoc = JsonDocument.Parse(repRaw);
                JsonElement revs = repDetailDoc.RootElement.GetProperty("reviews");
                AssertEqual(1, revs.GetArrayLength());
                AssertEqual("A队双打战术无缝衔接迫使对手投降", revs[0].GetProperty("headline").GetString()!);
                AssertEqual(95, revs[0].GetProperty("playerRatings")[0].GetProperty("playScore").GetInt32());
                AssertEqual(98, revs[0].GetProperty("playerRatings")[0].GetProperty("teamworkScore").GetInt32());
            }

            await serverHost.StopAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

}
