namespace YGOProSharp.Tests;

public sealed class McpAndAgentBridgeTests
{
    [Fact]
    public async Task McpStreamableHttpQueryKeyStdioAndAgentDuelFogOfWarWorkEndToEndAsync()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "YGOProSharpMcpTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 构建一个含 1 张通常怪兽的最小测试 cards.cdb 以便真实跑通 ocgcore 1v1 发牌与主阶段询问
            string cdbPath = Path.Combine(tempRoot, "cards.cdb");
            using (SqliteConnection conn = new($"Data Source={cdbPath};Pooling=False"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE datas (id INTEGER PRIMARY KEY, ot INTEGER, alias INTEGER, setcode INTEGER, type INTEGER, atk INTEGER, def INTEGER, level INTEGER, race INTEGER, attribute INTEGER, category INTEGER);
                    CREATE TABLE texts (id INTEGER PRIMARY KEY, name TEXT, desc TEXT, str1 TEXT, str2 TEXT, str3 TEXT, str4 TEXT, str5 TEXT, str6 TEXT, str7 TEXT, str8 TEXT, str9 TEXT, str10 TEXT, str11 TEXT, str12 TEXT, str13 TEXT, str14 TEXT, str15 TEXT, str16 TEXT);
                    INSERT INTO datas VALUES (89631139, 3, 0, 0, 17, 3000, 2500, 4, 8192, 16, 0);
                    INSERT INTO texts VALUES (89631139, '青眼白龙', '以高攻击力著称的传说之龙。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    """;
                cmd.ExecuteNonQuery();
            }

            SqliteCardDatabaseManager cdbMgr = new();
            INamedCardRepository namedRepo = cdbMgr.LoadNamedCards([cdbPath]);

            PersistenceOptions pOpts = new()
            {
                DbProvider = OperationalDbProvider.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(tempRoot, "mcp_ops.db")}",
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
                EnableNetworkListener = false,
                EnableMultiRoom = true,
                AddonFactories = [coordinator.CreateRoomAddonFactory()],
                JoinAccessValidator = coordinator.CreateJoinAccessValidator(),
                Game = new GameOptions
                {
                    NoCheckDeck = true,
                    StartHand = 5,
                    DrawCount = 1
                }
            };

            using NativeOcgRuntime ocgRuntime = new();
            await using YGOProSharpServerHost serverHost = YGOProSharpServerHost.Create(srvOpts, ocgRuntime);
            serverHost.StartBackgroundLoop(tickDelayMilliseconds: 1, embedded: true);

            await using WebControlHost adminHost = new(
                serverHost,
                store,
                coordinator,
                new WebControlOptions
                {
                    Host = "127.0.0.1",
                    Port = 0,
                    NamedCardRepositoryProvider = () => namedRepo,
                    LuaRuntimeProvider = static () => NLuaRuntime.Instance
                });
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

            await adminHost.StartAsync();
            await portalHost.StartAsync();

            Uri adminBase = new(adminHost.BoundAddresses.First());
            Uri portalBase = new(portalHost.BoundAddresses.First());

            // 1. 玩家在单端口融合页面 (adminBase /api/portal/*) 注册账号并建房：Token A 由玩家预设昵称「海马濑人」，Token B 留空交由 AI 首次进房自主取名
            CookieContainer pCookies = new();
            using HttpClient playerWeb = new(new HttpClientHandler { CookieContainer = pCookies, UseCookies = true }) { BaseAddress = adminBase };
            using (HttpResponseMessage regRes = await playerWeb.PostAsync(
                       "/api/portal/auth/register",
                       new StringContent(JsonSerializer.Serialize(new { username = "kaiba", password = "123456" }), Encoding.UTF8, "application/json")))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)regRes.StatusCode);
                using JsonDocument regDoc = JsonDocument.Parse(await regRes.Content.ReadAsStringAsync());
                playerWeb.DefaultRequestHeaders.Add("X-CSRF-Token", regDoc.RootElement.GetProperty("csrfToken").GetString()!);
            }

            string tokenA;
            string tokenB;
            string tokenBId;
            using (HttpResponseMessage roomRes = await playerWeb.PostAsync(
                       "/api/portal/rooms",
                       new StringContent(JsonSerializer.Serialize(new
                       {
                           roomName = "McpArena01",
                           mode = 0,
                           noCheckDeck = true,
                           issuePlayerTokens = true,
                           aiTokenCount = 2,
                           aiPlayerNames = new[] { "海马濑人", "" }
                       }), Encoding.UTF8, "application/json")))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)roomRes.StatusCode);
                using JsonDocument rDoc = JsonDocument.Parse(await roomRes.Content.ReadAsStringAsync());
                JsonElement issued = rDoc.RootElement.GetProperty("issuedPlayerTokens");
                AssertEqual(2, issued.GetArrayLength());
                tokenA = issued[0].GetProperty("rawToken").GetString()!;
                tokenB = issued[1].GetProperty("rawToken").GetString()!;
                AssertEqual("海马濑人", issued[0].GetProperty("preferredPlayerName").GetString()!);
                AssertFalse(issued[1].TryGetProperty("preferredPlayerName", out JsonElement pNameB) && pNameB.ValueKind == JsonValueKind.String);
                AssertFalse(issued[1].GetProperty("name").GetString()!.Contains("AI2", StringComparison.OrdinalIgnoreCase));
            }

            using (HttpResponseMessage listTokRes = await playerWeb.GetAsync("/api/portal/mcp/tokens"))
            {
                using JsonDocument ltDoc = JsonDocument.Parse(await listTokRes.Content.ReadAsStringAsync());
                JsonElement tokArr = ltDoc.RootElement.GetProperty("tokens");
                AssertEqual(2, tokArr.GetArrayLength());
                tokenBId = tokArr.EnumerateArray()
                    .First(t => !t.TryGetProperty("preferredPlayerName", out JsonElement pn) || pn.ValueKind == JsonValueKind.Null)
                    .GetProperty("id").GetString()!;
            }

            // 2. 验证管理员签发的 admin MCP Token 在前台门户 :7913/mcp 被拦截 (403)，在后台 :7912/mcp 正常可用
            LoginResult adminLogin = await store.LoginAsync("admin", "AdminPass123", "127.0.0.1", "Test");
            AssertTrue(adminLogin.Success && adminLogin.Principal is not null);
            (_, _, IssuedMcpTokenResult? adminMcp) = await store.IssueMcpTokenAsync(adminLogin.Principal!, "OpsAI", McpRoleCodes.Admin, null, 2, false, 12, "127.0.0.1");
            AssertTrue(adminMcp is not null);

            using HttpClient rawHttp = new();
            using (HttpResponseMessage portalAdminBlocked = await rawHttp.PostAsync(
                       new Uri(portalBase, $"/mcp?key={Uri.EscapeDataString(adminMcp!.RawToken)}"),
                       new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""", Encoding.UTF8, "application/json")))
            {
                AssertEqual((int)HttpStatusCode.Forbidden, (int)portalAdminBlocked.StatusCode);
            }

            using (HttpResponseMessage adminMcpOk = await rawHttp.PostAsync(
                       new Uri(adminBase, $"/mcp?key={Uri.EscapeDataString(adminMcp.RawToken)}"),
                       new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"server_get_status","arguments":{}}}""", Encoding.UTF8, "application/json")))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)adminMcpOk.StatusCode);
                using JsonDocument envDoc = JsonDocument.Parse(await adminMcpOk.Content.ReadAsStringAsync());
                string statusText = envDoc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
                using JsonDocument statusDoc = JsonDocument.Parse(statusText);
                AssertTrue(statusDoc.RootElement.GetProperty("roomCount").GetInt32() >= 1);
            }

            // 3. 验证客户端通过无 Header 的 ?key= 直连 Streamable HTTP MCP：initialize -> notifications/initialized (202) -> tools/list 最小权限裁剪
            Uri mcpUrlKeyA = new(portalBase, $"/mcp?key={Uri.EscapeDataString(tokenA)}");
            string mcpSessionA;
            using (HttpResponseMessage initRes = await rawHttp.PostAsync(
                       mcpUrlKeyA,
                       new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""", Encoding.UTF8, "application/json")))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)initRes.StatusCode);
                mcpSessionA = initRes.Headers.GetValues("Mcp-Session-Id").First();
                AssertTrue(mcpSessionA.StartsWith("mcp_", StringComparison.Ordinal));
            }

            using (HttpRequestMessage notifReq = new(HttpMethod.Post, mcpUrlKeyA)
                   {
                       Content = new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json")
                   })
            {
                notifReq.Headers.Add("Mcp-Session-Id", mcpSessionA);
                using HttpResponseMessage notifRes = await rawHttp.SendAsync(notifReq);
                AssertEqual((int)HttpStatusCode.Accepted, (int)notifRes.StatusCode);
            }

            using (HttpResponseMessage toolsRes = await rawHttp.PostAsync(
                       mcpUrlKeyA,
                       new StringContent("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json")))
            {
                string toolsJson = await toolsRes.Content.ReadAsStringAsync();
                AssertTrue(toolsJson.Contains("duel_join_room", StringComparison.Ordinal));
                AssertTrue(toolsJson.Contains("duel_wait_for_turn", StringComparison.Ordinal));
                AssertFalse(toolsJson.Contains("server_get_status", StringComparison.Ordinal));
                AssertFalse(toolsJson.Contains("diy_upsert_card", StringComparison.Ordinal));
            }

            // 4. 验证 stdio 转发模式 (McpStdioRelay) 零杂音工作
            using (MemoryStream stdioIn = new(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":99,"method":"tools/call","params":{"name":"room_list","arguments":{}}}""" + "\n")))
            using (MemoryStream stdioOut = new())
            {
                int code = await McpStdioRelay.RunAsync(new Uri(portalBase, "/mcp"), tokenB, stdioIn, stdioOut);
                AssertEqual(0, code);
                string outText = Encoding.UTF8.GetString(stdioOut.ToArray()).Trim();
                using JsonDocument outDoc = JsonDocument.Parse(outText);
                AssertEqual(99, outDoc.RootElement.GetProperty("id").GetInt32());
                AssertTrue(outDoc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!.Contains("McpArena01", StringComparison.Ordinal));
            }

            // 5. 两个外部 AI (Token A 与 Token B) 分别进房、组卡、准备、猜拳并进入第 1 回合主要阶段，验证战争迷雾与跨会话防越权
            async Task<JsonDocument> CallMcpToolAsync(string rawTok, string toolName, object arguments)
            {
                string rpc = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 10,
                    method = "tools/call",
                    @params = new { name = toolName, arguments }
                });
                using HttpRequestMessage req = new(HttpMethod.Post, new Uri(portalBase, $"/mcp?key={Uri.EscapeDataString(rawTok)}"))
                {
                    Content = new StringContent(rpc, Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage resp = await rawHttp.SendAsync(req);
                AssertEqual((int)HttpStatusCode.OK, (int)resp.StatusCode);
                using JsonDocument envelope = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                JsonElement resElem = envelope.RootElement.GetProperty("result");
                string innerText = resElem.GetProperty("content")[0].GetProperty("text").GetString()!;
                return JsonDocument.Parse(innerText);
            }

            // 房间白名单校验：尝试进未授权的其他房间应被拒绝
            using (JsonDocument wrongRoomDoc = await CallMcpToolAsync(tokenA, "duel_join_room", new { room = "UnauthorizedRoom" }))
            {
                AssertFalse(wrongRoomDoc.RootElement.GetProperty("success").GetBoolean());
            }

            // Token B 尚未绑定昵称：首次调用不传昵称或传 "AI2" 等机械用户名时，必须引导外部 AI 自主取名并拒绝机械代号
            using (JsonDocument emptyNameAttempt = await CallMcpToolAsync(tokenB, "duel_join_room", new { room = "McpArena01" }))
            {
                AssertFalse(emptyNameAttempt.RootElement.GetProperty("success").GetBoolean());
                AssertEqual("self_naming_required", emptyNameAttempt.RootElement.GetProperty("error").GetString()!);
                AssertTrue(emptyNameAttempt.RootElement.GetProperty("needsSelfNaming").GetBoolean());
            }
            using (JsonDocument mechanicalNameAttempt = await CallMcpToolAsync(tokenB, "duel_join_room", new { room = "McpArena01", playerName = "AI2" }))
            {
                AssertFalse(mechanicalNameAttempt.RootElement.GetProperty("success").GetBoolean());
                AssertEqual("self_naming_required", mechanicalNameAttempt.RootElement.GetProperty("error").GetString()!);
            }

            // Token A 已由玩家指定昵称「海马濑人」，无需传参直接沿用；Token B 由外部 AI 首次自取昵称「星尘观测者」并永久绑定到该 Token
            string sessionA;
            string sessionB;
            using (JsonDocument joinA = await CallMcpToolAsync(tokenA, "duel_join_room", new { room = "McpArena01" }))
            using (JsonDocument joinB = await CallMcpToolAsync(tokenB, "duel_join_room", new { room = "McpArena01", playerName = "星尘观测者" }))
            {
                AssertEqual("海马濑人", joinA.RootElement.GetProperty("playerName").GetString()!);
                AssertEqual("星尘观测者", joinB.RootElement.GetProperty("playerName").GetString()!);
                sessionA = joinA.RootElement.GetProperty("sessionId").GetString()!;
                sessionB = joinB.RootElement.GetProperty("sessionId").GetString()!;
            }

            ValidatedMcpToken? boundTokenB = await store.ValidateMcpTokenAsync(tokenB);
            AssertTrue(boundTokenB is not null);
            AssertEqual("星尘观测者", boundTokenB!.PreferredPlayerName!);

            // 玩家也可随时在网页端调用 PATCH /api/portal/mcp/tokens/{id} 为 AI 重新改名
            using (HttpResponseMessage renameRes = await playerWeb.PatchAsync(
                       $"/api/portal/mcp/tokens/{tokenBId}",
                       new StringContent(JsonSerializer.Serialize(new { preferredPlayerName = "电子流大师" }), Encoding.UTF8, "application/json")))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)renameRes.StatusCode);
                ValidatedMcpToken? renamedTokenB = await store.ValidateMcpTokenAsync(tokenB);
                AssertEqual("电子流大师", renamedTokenB!.PreferredPlayerName!);
            }

            // 跨 Token 越权访问对方 sessionId 必须被拦截
            using (JsonDocument crossSteal = await CallMcpToolAsync(tokenA, "duel_get_observation", new { sessionId = sessionB }))
            {
                AssertTrue(crossSteal.RootElement.GetProperty("message").GetString()!.Contains("越权拒绝", StringComparison.Ordinal));
            }

            int[] fortyBlueEyes = Enumerable.Repeat(89631139, 40).ToArray();
            using (await CallMcpToolAsync(tokenA, "duel_set_deck", new { sessionId = sessionA, main = fortyBlueEyes })) { }
            using (await CallMcpToolAsync(tokenB, "duel_set_deck", new { sessionId = sessionB, main = fortyBlueEyes })) { }
            using (await CallMcpToolAsync(tokenA, "duel_set_ready", new { sessionId = sessionA, ready = true, startIfHost = true })) { }
            using (await CallMcpToolAsync(tokenB, "duel_set_ready", new { sessionId = sessionB, ready = true, startIfHost = true })) { }

            // 驱动猜拳与先后攻直至进入 InDuel 主阶段询问
            for (int step = 0; step < 20; step++)
            {
                using JsonDocument obsA = await CallMcpToolAsync(tokenA, "duel_get_observation", new { sessionId = sessionA });
                using JsonDocument obsB = await CallMcpToolAsync(tokenB, "duel_get_observation", new { sessionId = sessionB });

                bool reachedIdle = obsA.RootElement.GetProperty("state").GetInt32() == (int)AgentSessionState.InDuel &&
                    ((obsA.RootElement.TryGetProperty("pendingPrompt", out JsonElement pa) && pa.ValueKind == JsonValueKind.Object && pa.GetProperty("promptType").GetString() == "select_idle_cmd") ||
                     (obsB.RootElement.TryGetProperty("pendingPrompt", out JsonElement pb) && pb.ValueKind == JsonValueKind.Object && pb.GetProperty("promptType").GetString() == "select_idle_cmd"));
                if (reachedIdle)
                    break;

                bool acted = false;
                if (obsA.RootElement.GetProperty("hasPendingAction").GetBoolean())
                {
                    using (await CallMcpToolAsync(tokenA, "duel_submit_action", new { sessionId = sessionA, actionIndex = 0 })) { }
                    acted = true;
                }

                if (obsB.RootElement.GetProperty("hasPendingAction").GetBoolean())
                {
                    string pt = obsB.RootElement.GetProperty("pendingPrompt").GetProperty("promptType").GetString()!;
                    int pick = pt == "rock_paper_scissors" ? 2 : 0; // B 出布，避免猜拳平局
                    using (await CallMcpToolAsync(tokenB, "duel_submit_action", new { sessionId = sessionB, actionIndex = pick })) { }
                    acted = true;
                }

                if (!acted)
                    await Task.Delay(30);
            }

            // 验证进入对局后：己方手牌真实可见，对手手牌严格遮蔽为 isHidden=true, code=0
            using (JsonDocument duelObsA = await CallMcpToolAsync(tokenA, "duel_get_observation", new { sessionId = sessionA }))
            using (JsonDocument duelObsB = await CallMcpToolAsync(tokenB, "duel_get_observation", new { sessionId = sessionB }))
            {
                JsonElement handA = duelObsA.RootElement.GetProperty("selfSide").GetProperty("hand");
                JsonElement oppoHandFromA = duelObsA.RootElement.GetProperty("opponentSide").GetProperty("hand");
                AssertTrue(handA.GetArrayLength() >= 5);
                AssertEqual(89631139, handA[0].GetProperty("code").GetInt32());
                AssertFalse(handA[0].GetProperty("isHidden").GetBoolean());

                AssertTrue(oppoHandFromA.GetArrayLength() >= 5);
                foreach (JsonElement oppCard in oppoHandFromA.EnumerateArray())
                {
                    AssertEqual(0, oppCard.GetProperty("code").GetInt32());
                    AssertTrue(oppCard.GetProperty("isHidden").GetBoolean());
                }

                // 当前先攻方提交通召青眼白龙并附带公开思考气泡（同时校验候选动作已富化怪兽攻守与简短效果摘要）
                JsonElement activeObs = duelObsA.RootElement.GetProperty("hasPendingAction").GetBoolean() ? duelObsA.RootElement : duelObsB.RootElement;
                JsonElement firstChoice = activeObs.GetProperty("pendingPrompt").GetProperty("choices")[0];
                AssertEqual(3000, firstChoice.GetProperty("attack").GetInt32());
                AssertEqual(2500, firstChoice.GetProperty("defense").GetInt32());
                AssertTrue(firstChoice.GetProperty("shortEffect").GetString()!.Contains("传说之龙", StringComparison.Ordinal));

                string activeTok = duelObsA.RootElement.GetProperty("hasPendingAction").GetBoolean() ? tokenA : tokenB;
                string activeSid = duelObsA.RootElement.GetProperty("hasPendingAction").GetBoolean() ? sessionA : sessionB;
                using JsonDocument submitDoc = await CallMcpToolAsync(activeTok, "duel_submit_action", new
                {
                    sessionId = activeSid,
                    actionIndex = 0,
                    thoughtSummary = "先攻回合尝试展开场面压制"
                });
                AssertTrue(submitDoc.RootElement.GetProperty("success").GetBoolean());
            }

            using (HttpResponseMessage roomDetailRes = await portalWebOrRawGetAsync(rawHttp, new Uri(portalBase, "/api/portal/rooms/McpArena01")))
            {
                string detailJson = await roomDetailRes.Content.ReadAsStringAsync();
                AssertTrue(detailJson.Contains("先攻回合尝试展开场面压制", StringComparison.Ordinal));
            }

            // 吊销 Token 后对应活跃对局会话立即断开清理
            Guid realTokenAGuid = (await store.ValidateMcpTokenAsync(tokenA))!.TokenId;
            using (HttpResponseMessage revRes = await playerWeb.DeleteAsync($"/api/portal/mcp/tokens/{realTokenAGuid}"))
            {
                AssertEqual((int)HttpStatusCode.OK, (int)revRes.StatusCode);
            }
            using (JsonDocument afterRevoke = await CallMcpToolAsync(tokenB, "duel_list_sessions", new { }))
            {
                AssertEqual(1, afterRevoke.RootElement.GetProperty("sessions").GetArrayLength());
            }

            await serverHost.StopAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        static Task<HttpResponseMessage> portalWebOrRawGetAsync(HttpClient client, Uri uri) => client.GetAsync(uri);
    }

    [Fact]
    public async Task McpPlayerFeedbackFixesPermissionsCompactDetailSettlementPromptsAndBatchAutoPlayWorkAsync()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "YGOProSharpMcpFixes_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "script"));

        try
        {
            // 准备测试卡库：
            // 89631140: 4星通常怪兽「星辉剑士」ATK 1800
            // 89631141: 5星通常怪兽「星辉上级龙」ATK 2400（用于测试上级召唤 1 祭品 select_tribute 双向选卡兼容）
            // 89631142: 通常魔法「星光增幅」str1="回复我方1000基本分"（用于测试 str1..str16 子效果解码）
            // 89631143: 速攻魔法「星光连击」str1="连锁追加500回复"（用于测试 select_chain 触发上下文与 Chain 2 提示）
            // 90000001: 4星怪兽「禁忌魔导龙」（写入禁限表设为 0 禁止，用于测试 DeckError 可读中文解码与非粘滞 lastError）
            string cdbPath = Path.Combine(tempRoot, "cards.cdb");
            using (SqliteConnection conn = new($"Data Source={cdbPath};Pooling=False"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE datas (id INTEGER PRIMARY KEY, ot INTEGER, alias INTEGER, setcode INTEGER, type INTEGER, atk INTEGER, def INTEGER, level INTEGER, race INTEGER, attribute INTEGER, category INTEGER);
                    CREATE TABLE texts (id INTEGER PRIMARY KEY, name TEXT, desc TEXT, str1 TEXT, str2 TEXT, str3 TEXT, str4 TEXT, str5 TEXT, str6 TEXT, str7 TEXT, str8 TEXT, str9 TEXT, str10 TEXT, str11 TEXT, str12 TEXT, str13 TEXT, str14 TEXT, str15 TEXT, str16 TEXT);
                    INSERT INTO datas VALUES (89631140, 3, 0, 0, 17, 1800, 1200, 4, 1, 16, 0);
                    INSERT INTO texts VALUES (89631140, '星辉剑士', '操纵星芒的剑士。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    INSERT INTO datas VALUES (89631141, 3, 0, 0, 17, 2400, 2000, 5, 8192, 16, 0);
                    INSERT INTO texts VALUES (89631141, '星辉上级龙', '需要解放1只怪兽上级召唤的光之龙。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    INSERT INTO datas VALUES (89631142, 3, 0, 0, 2, 0, 0, 0, 0, 0, 1048576);
                    INSERT INTO texts VALUES (89631142, '星光增幅', '①：自己回复1000基本分。', '回复我方1000基本分', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    INSERT INTO datas VALUES (89631143, 3, 0, 0, 65538, 0, 0, 0, 0, 0, 1048576);
                    INSERT INTO texts VALUES (89631143, '星光连击', '①：自己回复500基本分。', '连锁追加500回复', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    INSERT INTO datas VALUES (90000001, 3, 0, 0, 17, 4000, 4000, 4, 8192, 32, 0);
                    INSERT INTO texts VALUES (90000001, '禁忌魔导龙', '被禁限表禁止投入的远古魔龙。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                    """;
                cmd.ExecuteNonQuery();
                for (int filler = 89632001; filler <= 89632025; filler++)
                {
                    using SqliteCommand fillCmd = conn.CreateCommand();
                    fillCmd.CommandText = $"""
                        INSERT INTO datas VALUES ({filler}, 3, 0, 0, 17, 1600, 1400, 4, 1, 1, 0);
                        INSERT INTO texts VALUES ({filler}, '星辉卫兵{filler - 89632000}', '守护防线的通常怪兽。', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '');
                        """;
                    fillCmd.ExecuteNonQuery();
                }
            }

            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "constant.lua"), """
                EFFECT_TYPE_ACTIVATE = 0x0010
                EVENT_FREE_CHAIN = 1002
                CATEGORY_RECOVER = 0x100000
                REASON_EFFECT = 0x40
                """);
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "utility.lua"), """
                function GetID()
                    return self_table, self_code, 1
                end
                """);
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "procedure.lua"), "-- procedure stub\n");
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "c89631142.lua"), """
                local s,id=GetID()
                function s.initial_effect(c)
                    local e1 = Effect.CreateEffect(c)
                    e1:SetDescription(id * 16 + 0)
                    e1:SetCategory(CATEGORY_RECOVER)
                    e1:SetType(EFFECT_TYPE_ACTIVATE)
                    e1:SetCode(EVENT_FREE_CHAIN)
                    e1:SetTarget(function(e,tp,eg,ep,ev,re,r,rp,chk)
                        if chk==0 then return true end
                        Duel.SetOperationInfo(0,CATEGORY_RECOVER,nil,0,tp,1000)
                    end)
                    e1:SetOperation(function(e,tp,eg,ep,ev,re,r,rp)
                        Duel.Recover(tp,1000,REASON_EFFECT)
                    end)
                    c:RegisterEffect(e1)
                end
                """);
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "script", "c89631143.lua"), """
                local s,id=GetID()
                function s.initial_effect(c)
                    local e1 = Effect.CreateEffect(c)
                    e1:SetDescription(id * 16 + 0)
                    e1:SetCategory(CATEGORY_RECOVER)
                    e1:SetType(EFFECT_TYPE_ACTIVATE)
                    e1:SetCode(EVENT_FREE_CHAIN)
                    e1:SetTarget(function(e,tp,eg,ep,ev,re,r,rp,chk)
                        if chk==0 then return true end
                        Duel.SetOperationInfo(0,CATEGORY_RECOVER,nil,0,tp,500)
                    end)
                    e1:SetOperation(function(e,tp,eg,ep,ev,re,r,rp)
                        Duel.Recover(tp,500,REASON_EFFECT)
                    end)
                    c:RegisterEffect(e1)
                end
                """);
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "lflist.conf"), "!StrictTestList\n90000001 0 --banned\n");

            SqliteCardDatabaseManager cdbMgr = new();
            INamedCardRepository namedRepo = cdbMgr.LoadNamedCards([cdbPath]);
            AssertTrue(namedRepo.TryGetCard(89631142, out NamedCard? loadedSpell) && loadedSpell is not null);
            AssertEqual("回复我方1000基本分", loadedSpell!.GetEffectString(0)!);

            PersistenceOptions pOpts = new()
            {
                DbProvider = OperationalDbProvider.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(tempRoot, "mcp_fixes_ops.db")}",
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
                BanlistFile = "lflist.conf",
                EnableNetworkListener = false,
                EnableMultiRoom = true,
                AddonFactories = [coordinator.CreateRoomAddonFactory()],
                JoinAccessValidator = coordinator.CreateJoinAccessValidator(),
                Game = new GameOptions
                {
                    Banlist = 0,
                    Region = 2,
                    MasterRule = 5,
                    NoCheckDeck = false
                }
            };

            using NativeOcgRuntime ocgRuntime = new();
            await using YGOProSharpServerHost serverHost = YGOProSharpServerHost.Create(srvOpts, ocgRuntime);
            serverHost.StartBackgroundLoop(tickDelayMilliseconds: 1, embedded: true);

            List<string> spawnedBotLog = new();
            IReadOnlyList<string> TestWindBotSpawner(Game targetRoom, string? deck, string? dialog, int count)
            {
                string name = $"MockBot_{deck ?? "Default"}_{spawnedBotLog.Count + 1}";
                spawnedBotLog.Add($"{targetRoom.RoomName}:{name}");
                return [name];
            }

            await using UserPortalHost portalHost = new(
                serverHost,
                store,
                coordinator,
                new UserPortalOptions
                {
                    Host = "127.0.0.1",
                    Port = 0,
                    NamedCardRepositoryProvider = () => namedRepo,
                    WindBotSpawner = TestWindBotSpawner
                });
            await portalHost.StartAsync();
            Uri portalBase = new(portalHost.BoundAddresses.First());

            // 签发无房间限制的普通 player Token A、受限白名单 player Token Restricted，以及对手 player Token B
            LoginResult adminLogin = await store.LoginAsync("admin", "AdminPass123", "127.0.0.1", "Test");
            AssertTrue(adminLogin.Success && adminLogin.Principal is not null);
            (_, _, IssuedMcpTokenResult? playerTokA) = await store.IssueMcpTokenAsync(adminLogin.Principal!, "PlayerFreeA", McpRoleCodes.Player, null, 3, false, 12, "127.0.0.1");
            (_, _, IssuedMcpTokenResult? playerTokB) = await store.IssueMcpTokenAsync(adminLogin.Principal!, "PlayerFreeB", McpRoleCodes.Player, null, 3, false, 12, "127.0.0.1");
            (_, _, IssuedMcpTokenResult? restrictedTok) = await store.IssueMcpTokenAsync(adminLogin.Principal!, "ScopedPlayer", McpRoleCodes.Player, ["AllowedOnly"], 1, false, 12, "127.0.0.1");
            await store.BindMcpTokenPlayerNameAsync((await store.ValidateMcpTokenAsync(playerTokA!.RawToken))!.TokenId, "游城十代");
            await store.BindMcpTokenPlayerNameAsync((await store.ValidateMcpTokenAsync(playerTokB!.RawToken))!.TokenId, "万丈目准");
            await store.BindMcpTokenPlayerNameAsync((await store.ValidateMcpTokenAsync(restrictedTok!.RawToken))!.TokenId, "丸藤翔");

            using HttpClient rawHttp = new();
            async Task<(bool IsError, JsonDocument Body)> CallPortalMcpAsync(string rawTok, string toolName, object arguments)
            {
                string rpc = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "tools/call",
                    @params = new { name = toolName, arguments }
                });
                using HttpResponseMessage resp = await rawHttp.PostAsync(
                    new Uri(portalBase, $"/mcp?key={Uri.EscapeDataString(rawTok)}"),
                    new StringContent(rpc, Encoding.UTF8, "application/json"));
                AssertEqual((int)HttpStatusCode.OK, (int)resp.StatusCode);
                using JsonDocument env = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                JsonElement res = env.RootElement.GetProperty("result");
                bool isErr = res.TryGetProperty("isError", out JsonElement ie) && ie.GetBoolean();
                string innerText = res.GetProperty("content")[0].GetProperty("text").GetString()!;
                return (isErr, JsonDocument.Parse(innerText));
            }

            // 1. 验证高频连续请求不触发 HTTP 429（65 次快速调用全部 200 OK）
            for (int burst = 0; burst < 65; burst++)
            {
                using HttpResponseMessage pingRes = await rawHttp.PostAsync(
                    new Uri(portalBase, $"/mcp?key={Uri.EscapeDataString(playerTokA!.RawToken)}"),
                    new StringContent("""{"jsonrpc":"2.0","id":7,"method":"ping"}""", Encoding.UTF8, "application/json"));
                AssertEqual((int)HttpStatusCode.OK, (int)pingRes.StatusCode);
            }

            // 2. 验证 player 角色在前台门户端口 (:7913/mcp) 拥有 room_create 与 room_spawn_windbot 权限，且房间白名单仍然生效
            var (blockedErr, blockedCreateDoc) = await CallPortalMcpAsync(restrictedTok!.RawToken, "room_create", new { roomName = "ForbiddenRoom" });
            using (blockedCreateDoc)
            {
                AssertTrue(blockedErr);
                AssertEqual("room_not_in_whitelist", blockedCreateDoc.RootElement.GetProperty("error").GetString()!);
            }

            var (createErr, createDoc) = await CallPortalMcpAsync(playerTokA!.RawToken, "room_create", new { roomName = "StrictBanRoom" });
            using (createDoc)
            {
                AssertFalse(createErr);
                AssertTrue(createDoc.RootElement.GetProperty("success").GetBoolean());
            }

            var (spawnErr, spawnDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "room_spawn_windbot", new { roomName = "StrictBanRoom", deck = "BlueEyes", count = 1 });
            using (spawnDoc)
            {
                AssertFalse(spawnErr);
                AssertEqual(1, spawnedBotLog.Count);
            }

            // 3. 验证 duel_list_preset_decks 即使在无 decks/random 的空目录也能发现内置 WindBot .ydk 卡组
            var (presetErr, presetDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_list_preset_decks", new { });
            using (presetDoc)
            {
                AssertFalse(presetErr);
                AssertTrue(presetDoc.RootElement.GetProperty("count").GetInt32() > 10);
                AssertTrue(presetDoc.RootElement.GetProperty("windBotDecks").GetArrayLength() > 10);
            }

            // 4. 验证 DeckError 可读中文解码、duel_set_ready 实时结算返回 false，以及 lastError 读取一次后自动清除不残留
            string banSessionA;
            var (_, joinBanA) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_join_room", new { room = "StrictBanRoom", detail = "minimal" });
            using (joinBanA)
            {
                banSessionA = joinBanA.RootElement.GetProperty("sessionId").GetString()!;
            }

            int[] illegalDeck = [90000001, .. Enumerable.Repeat(89631140, 3), .. Enumerable.Range(89632001, 12).SelectMany(static id => Enumerable.Repeat(id, 3))];
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_set_deck", new { sessionId = banSessionA, main = illegalDeck })).Body) { }

            var (readyFailErr, readyFailDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_set_ready", new { sessionId = banSessionA, ready = true });
            using (readyFailDoc)
            {
                AssertTrue(readyFailErr);
                AssertFalse(readyFailDoc.RootElement.GetProperty("success").GetBoolean());
                string errMsg = readyFailDoc.RootElement.GetProperty("message").GetString()!;
                AssertTrue(errMsg.Contains("禁忌魔导龙", StringComparison.Ordinal));
                AssertTrue(errMsg.Contains("90000001", StringComparison.Ordinal));
                AssertEqual(errMsg, readyFailDoc.RootElement.GetProperty("observation").GetProperty("lastError").GetString()!);
            }

            // 再次调用 duel_get_observation 时，上一轮已经消费过的 lastError 不应永久粘滞
            var (_, nextObsDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_get_observation", new { sessionId = banSessionA });
            using (nextObsDoc)
            {
                AssertFalse(nextObsDoc.RootElement.TryGetProperty("lastError", out JsonElement le) && le.ValueKind == JsonValueKind.String);
            }

            // 调用 duel_set_deck(randomMode="preset") 自动适配当前房间禁限表（剔除被禁的 90000001），随后 duel_set_ready 立即过检且返回 isReady=true！
            var (adaptErr, adaptDeckDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_set_deck", new { sessionId = banSessionA, randomMode = "preset" });
            using (adaptDeckDoc)
            {
                AssertFalse(adaptErr);
                AssertEqual(40, adaptDeckDoc.RootElement.GetProperty("mainCount").GetInt32());
            }

            var (readyOkErr, readyOkDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_set_ready", new { sessionId = banSessionA, ready = true, startIfHost = false });
            using (readyOkDoc)
            {
                AssertFalse(readyOkErr);
                AssertTrue(readyOkDoc.RootElement.GetProperty("success").GetBoolean());
                AssertTrue(readyOkDoc.RootElement.GetProperty("observation").GetProperty("lobbySlots")[0].GetProperty("isReady").GetBoolean());
            }

            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_leave", new { sessionId = banSessionA })).Body) { }

            // 5. 实战验证：紧凑 detail、召唤后立即结算怪兽上场、str1..str16 子效果与 Chain 2 上下文解码、上级召唤祭品双向选卡兼容、duel_submit_batch 与 duel_auto_play
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "room_create", new { roomName = "TributeChainLab", rulePrefix = "NS,NC" })).Body) { }

            string sidA, sidB;
            var (_, jA) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_join_room", new { room = "TributeChainLab" });
            var (_, jB) = await CallPortalMcpAsync(playerTokB!.RawToken, "duel_join_room", new { room = "TributeChainLab" });
            using (jA) sidA = jA.RootElement.GetProperty("sessionId").GetString()!;
            using (jB) sidB = jB.RootElement.GetProperty("sessionId").GetString()!;

            // 构造不洗牌固定起手（首尾各放一套 4星剑士+5星上级龙+通常魔法+速攻魔法，无论 ocgcore 从头或从尾发牌均必在起手 5 张中）
            int[] headTailHand = [89631140, 89631141, 89631142, 89631143, 89631140];
            int[] middleFill = Enumerable.Repeat(89631140, 30).ToArray();
            int[] labDeckA = [.. headTailHand, .. middleFill, .. headTailHand];
            int[] labDeckB = Enumerable.Repeat(89632001, 40).ToArray();

            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_set_deck", new { sessionId = sidA, main = labDeckA })).Body) { }
            using ((await CallPortalMcpAsync(playerTokB.RawToken, "duel_set_deck", new { sessionId = sidB, main = labDeckB })).Body) { }
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_set_ready", new { sessionId = sidA, ready = true, startIfHost = true })).Body) { }
            using ((await CallPortalMcpAsync(playerTokB.RawToken, "duel_set_ready", new { sessionId = sidB, ready = true, startIfHost = true })).Body) { }

            // A 出石头(1)，B 出剪刀(0) -> A 赢猜拳并选先攻(0)
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_wait_for_turn", new { sessionId = sidA, timeoutSeconds = 5, detail = "minimal" })).Body) { }
            using ((await CallPortalMcpAsync(playerTokB.RawToken, "duel_wait_for_turn", new { sessionId = sidB, timeoutSeconds = 5, detail = "minimal" })).Body) { }
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_submit_action", new { sessionId = sidA, actionIndex = 1, detail = "none" })).Body) { }
            using ((await CallPortalMcpAsync(playerTokB.RawToken, "duel_submit_action", new { sessionId = sidB, actionIndex = 0, detail = "none" })).Body) { }
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_wait_for_turn", new { sessionId = sidA, timeoutSeconds = 5, detail = "minimal" })).Body) { }
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_submit_action", new { sessionId = sidA, actionIndex = 0, detail = "standard" })).Body) { }
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_wait_for_turn", new { sessionId = sidA, timeoutSeconds = 5, detail = "minimal" })).Body) { }
            // 跳过抽卡/准备阶段己方手牌速攻魔法的自由时点询问，直达 Main1 空闲询问
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_auto_play", new { sessionId = sidA, strategy = "pass_chains", maxSteps = 6, detail = "none" })).Body) { }

            // 验证默认 detail="standard" 显著小于 detail="full"（不含 ownSubmittedDeckList 与冗余长卡效，而 duel_get_deck_list 可按需读取）
            var (_, fullObsDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_get_observation", new { sessionId = sidA, detail = "full" });
            var (_, stdObsDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_get_observation", new { sessionId = sidA, detail = "standard" });
            var (_, minObsDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_get_observation", new { sessionId = sidA, detail = "minimal" });
            using (fullObsDoc)
            using (stdObsDoc)
            using (minObsDoc)
            {
                AssertTrue(fullObsDoc.RootElement.TryGetProperty("ownSubmittedDeckList", out JsonElement fullDl) && fullDl.ValueKind == JsonValueKind.Array);
                AssertFalse(stdObsDoc.RootElement.TryGetProperty("ownSubmittedDeckList", out JsonElement stdDl) && stdDl.ValueKind == JsonValueKind.Array);
                string fullRaw = fullObsDoc.RootElement.GetRawText();
                string stdRaw = stdObsDoc.RootElement.GetRawText();
                string minRaw = minObsDoc.RootElement.GetRawText();
                AssertTrue(stdRaw.Length < fullRaw.Length);
                AssertTrue(minRaw.Length <= stdRaw.Length);

                // 验证 select_idle_cmd 中发动「星光增幅」的选项已解码 str1 = "回复我方1000基本分"
                JsonElement choices = stdObsDoc.RootElement.GetProperty("pendingPrompt").GetProperty("choices");
                bool foundDecodedEffect = choices.EnumerateArray().Any(c =>
                    c.GetProperty("kind").GetString() == "activate" &&
                    c.TryGetProperty("effectText", out JsonElement et) &&
                    et.GetString()!.Contains("回复我方1000基本分", StringComparison.Ordinal));
                AssertTrue(foundDecodedEffect);
            }

            var (_, deckListDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_get_deck_list", new { sessionId = sidA });
            using (deckListDoc)
            {
                AssertTrue(deckListDoc.RootElement.GetProperty("count").GetInt32() >= 4);
            }

            // 验证通召 4星「星辉剑士」(89631140) 后，duel_submit_batch 返回的同一帧快照里怪兽区已立即包含该怪兽（消除快照滞后！）
            var (sumErr, sumDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_submit_batch", new
            {
                sessionId = sidA,
                steps = new object[]
                {
                    new { promptType = "select_idle_cmd", kind = "summon", cardCode = 89631140 },
                    new { promptType = "select_place", actionIndex = 0 }
                }
            });
            using (sumDoc)
            {
                AssertFalse(sumErr);
                JsonElement mzone = sumDoc.RootElement.GetProperty("observation").GetProperty("selfSide").GetProperty("monsterZone");
                AssertEqual(1, mzone.GetArrayLength());
                AssertEqual(89631140, mzone[0].GetProperty("code").GetInt32());
                AssertEqual("星辉剑士", mzone[0].GetProperty("name").GetString()!);
            }

            // 若通召成功打开了手牌速攻魔法的召唤响应时点，自动 Pass 回到 Main1 空闲态
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_auto_play", new { sessionId = sidA, strategy = "pass_chains", maxSteps = 4, detail = "none" })).Body) { }

            // 用 duel_submit_batch 发动通常魔法「星光增幅」(89631142) 并选魔陷格，停在己方手牌速攻魔法「星光连击」的 Chain 2 连锁询问
            var (actErr, actDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_submit_batch", new
            {
                sessionId = sidA,
                detail = "none",
                steps = new object[]
                {
                    new { promptType = "select_idle_cmd", kind = "activate", cardCode = 89631142 },
                    new { promptType = "select_place", actionIndex = 0 }
                }
            });
            using (actDoc)
            {
                AssertFalse(actErr);
                // detail="none" 时不携带完整 observation，但携带 nextPrompt 与 newEvents
                AssertFalse(actDoc.RootElement.TryGetProperty("observation", out JsonElement noneObs) && noneObs.ValueKind == JsonValueKind.Object);
                JsonElement nextPrompt = actDoc.RootElement.GetProperty("nextPrompt");
                AssertEqual("select_chain", nextPrompt.GetProperty("promptType").GetString()!);
                string chainHint = nextPrompt.GetProperty("hintText").GetString()!;
                AssertTrue(chainHint.Contains("Chain 2", StringComparison.Ordinal));
                AssertTrue(chainHint.Contains("星光增幅", StringComparison.Ordinal));
                AssertTrue(nextPrompt.GetProperty("choices").EnumerateArray().Any(c =>
                    c.GetProperty("label").GetString()!.Contains("连锁追加500回复", StringComparison.Ordinal)));
            }

            // 用 duel_auto_play(strategy="pass_chains") 自动跳过连锁窗口，确认 LP 从 8000 升至 9000！
            var (passErr, passDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_auto_play", new { sessionId = sidA, strategy = "pass_chains", maxSteps = 4 });
            using (passDoc)
            {
                AssertFalse(passErr);
                AssertEqual(9000, passDoc.RootElement.GetProperty("observation").GetProperty("selfSide").GetProperty("lifePoints").GetInt32());
            }

            // A 结束第 1 回合 -> B 在第 2 回合直接结束回合 -> 回到第 3 回合 A 的 Main1
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_submit_batch", new
            {
                sessionId = sidA,
                steps = new object[] { new { promptType = "select_idle_cmd", kind = "to_end_phase" } }
            })).Body) { }
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_auto_play", new { sessionId = sidA, strategy = "pass_chains", maxSteps = 6 })).Body) { }
            using ((await CallPortalMcpAsync(playerTokB.RawToken, "duel_submit_batch", new
            {
                sessionId = sidB,
                steps = new object[] { new { promptType = "select_idle_cmd", kind = "to_end_phase" } }
            })).Body) { }
            using ((await CallPortalMcpAsync(playerTokA.RawToken, "duel_auto_play", new { sessionId = sidA, strategy = "pass_chains", maxSteps = 6 })).Body) { }

            // 第 3 回合 A 上级召唤 5 星「星辉上级龙」(89631141)：进入 select_tribute 后故意传 cardIndices: [0]（验证单选/多选参数双向兼容不死循环！）
            var (_, tribStartDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_submit_batch", new
            {
                sessionId = sidA,
                steps = new object[] { new { promptType = "select_idle_cmd", kind = "summon", cardCode = 89631141 } }
            });
            using (tribStartDoc)
            {
                JsonElement tribPrompt = tribStartDoc.RootElement.GetProperty("nextPrompt");
                AssertEqual("select_tribute", tribPrompt.GetProperty("promptType").GetString()!);
                AssertEqual("actionIndex", tribPrompt.GetProperty("submitField").GetString()!);
            }

            var (tribPickErr, tribPickDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_submit_action", new
            {
                sessionId = sidA,
                cardIndices = new[] { 0 }
            });
            using (tribPickDoc)
            {
                AssertFalse(tribPickErr);
                AssertTrue(tribPickDoc.RootElement.GetProperty("success").GetBoolean());
            }

            // 若紧跟 MR5 选格询问或连锁窗口，交由 duel_auto_play(strategy="simple_beatdown") 自动落位、进战阶直接攻击对方 2400 伤害！
            var (beatErr, beatDoc) = await CallPortalMcpAsync(playerTokA.RawToken, "duel_auto_play", new
            {
                sessionId = sidA,
                strategy = "simple_beatdown",
                maxSteps = 10
            });
            using (beatDoc)
            {
                AssertFalse(beatErr);
                JsonElement selfMz = beatDoc.RootElement.GetProperty("observation").GetProperty("selfSide").GetProperty("monsterZone");
                AssertEqual(1, selfMz.GetArrayLength());
                AssertEqual(89631141, selfMz[0].GetProperty("code").GetInt32());
                AssertEqual(5600, beatDoc.RootElement.GetProperty("observation").GetProperty("opponentSide").GetProperty("lifePoints").GetInt32());
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
