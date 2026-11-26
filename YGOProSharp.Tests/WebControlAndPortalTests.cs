namespace YGOProSharp.Tests;

public sealed class WebControlAndPortalTests
{
    [Fact]
    public async Task WebControlHostEnforcesAuthMustChangePasswordCsrfStreamerRedactionAndJoinHooksAsync()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpWebTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PersistenceOptions persistenceOptions = new()
            {
                DbProvider = OperationalDbProvider.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(tempDir, "webops.db")}",
                ReplayDirectory = Path.Combine(tempDir, "replays"),
                Argon2MemoryKb = 4096,
                Argon2Iterations = 1,
                Argon2Parallelism = 1,
                LoginRateLimitCount = 50
            };

            await using (OperationalStore store = new(persistenceOptions))
            {
                BootstrapAdminResult boot = await store.InitializeAsync();
                OperationalServerCoordinator coordinator = new(store);

                ICardRepository cards = TestRepository(TestCard(89631139), TestCard(46986414));
                ServerOptions serverOptions = new()
                {
                    RootPath = tempDir,
                    EnableNetworkListener = false,
                    EnableMultiRoom = true,
                    Game = new GameOptions { MasterRule = 5, NoCheckDeck = true },
                    AddonFactories = [coordinator.CreateRoomAddonFactory()],
                    JoinAccessValidator = coordinator.CreateJoinAccessValidator()
                };

                LogRingBuffer logBuffer = new(200);
                logBuffer.Append(LogLevel.Information, "TestCategory", "Internal server diagnostic line");

                await using YGOProSharpServerHost serverHost = new(duelFactory: null, cardRepository: cards, options: serverOptions);
                serverHost.StartBackgroundLoop(tickDelayMilliseconds: 2, embedded: true);

                await using WebControlHost webHost = new(
                    serverHost,
                    store,
                    coordinator,
                    new WebControlOptions { Host = "127.0.0.1", Port = 0 },
                    logBuffer);
                await webHost.StartAsync();

                string baseUrl = webHost.BoundAddresses.First();
                Uri baseUri = new(baseUrl);

                CookieContainer adminCookies = new();
                using HttpClient adminHttp = new(new HttpClientHandler { CookieContainer = adminCookies, UseCookies = true })
                {
                    BaseAddress = baseUri
                };

                // 1. /healthz 公开探针与未登录拦截
                using (HttpResponseMessage healthRes = await adminHttp.GetAsync("/healthz"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)healthRes.StatusCode);
                    using JsonDocument healthDoc = JsonDocument.Parse(await healthRes.Content.ReadAsStringAsync());
                    AssertEqual("healthy", healthDoc.RootElement.GetProperty("status").GetString()!);
                    AssertEqual("wal", healthDoc.RootElement.GetProperty("database").GetProperty("journalMode").GetString()!.ToLowerInvariant());
                }

                using (HttpResponseMessage unauthRes = await adminHttp.GetAsync("/api/v1/server/status"))
                {
                    AssertEqual((int)HttpStatusCode.Unauthorized, (int)unauthRes.StatusCode);
                }

                // 2. 初始超管登录、首次强制改密门禁与 Cookie CSRF 防护校验
                string adminCsrf;
                using (HttpResponseMessage loginRes = await adminHttp.PostAsync(
                           "/api/v1/auth/login",
                           new StringContent(JsonSerializer.Serialize(new { username = "admin", password = boot.GeneratedOneTimePassword }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)loginRes.StatusCode);
                    using JsonDocument loginDoc = JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
                    adminCsrf = loginDoc.RootElement.GetProperty("csrfToken").GetString()!;
                    AssertTrue(loginDoc.RootElement.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
                }

                using (HttpResponseMessage blockedByMustChange = await adminHttp.GetAsync("/api/v1/server/status"))
                {
                    AssertEqual((int)HttpStatusCode.Forbidden, (int)blockedByMustChange.StatusCode);
                    using JsonDocument errDoc = JsonDocument.Parse(await blockedByMustChange.Content.ReadAsStringAsync());
                    AssertEqual("must_change_password", errDoc.RootElement.GetProperty("error").GetString()!);
                }

                // 未携带 X-CSRF-Token 的 Cookie 写操作必须被拒绝
                using (HttpResponseMessage csrfFail = await adminHttp.PostAsync(
                           "/api/v1/auth/change-password",
                           new StringContent(JsonSerializer.Serialize(new { currentPassword = boot.GeneratedOneTimePassword, newPassword = "Stardust#Dragon2026!" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.Forbidden, (int)csrfFail.StatusCode);
                    using JsonDocument csrfDoc = JsonDocument.Parse(await csrfFail.Content.ReadAsStringAsync());
                    AssertEqual("csrf_invalid", csrfDoc.RootElement.GetProperty("error").GetString()!);
                }

                adminHttp.DefaultRequestHeaders.Add("X-CSRF-Token", adminCsrf);
                using (HttpResponseMessage changeOk = await adminHttp.PostAsync(
                           "/api/v1/auth/change-password",
                           new StringContent(JsonSerializer.Serialize(new { currentPassword = boot.GeneratedOneTimePassword, newPassword = "Stardust#Dragon2026!" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)changeOk.StatusCode);
                }

                using (HttpResponseMessage statusOk = await adminHttp.GetAsync("/api/v1/server/status"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)statusOk.StatusCode);
                }

                // 3. 面板预创建房间（自动签发 2 枚斗蛐蛐 player Token）并让一名模拟玩家进入房间
                using (HttpResponseMessage createRoomRes = await adminHttp.PostAsync(
                           "/api/v1/rooms",
                           new StringContent(JsonSerializer.Serialize(new { roomName = "ShowMatch1", rulePrefix = "RD", issuePlayerTokens = true }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)createRoomRes.StatusCode);
                    using JsonDocument roomDoc = JsonDocument.Parse(await createRoomRes.Content.ReadAsStringAsync());
                    AssertEqual(2, roomDoc.RootElement.GetProperty("issuedPlayerTokens").GetArrayLength());
                }

                YGOClient humanClient = serverHost.ConnectLocalClient();
                SendPlayerJoin(humanClient, "PlayerAlpha$secretpw", "ShowMatch1");
                DateTime joinDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < joinDeadline)
                {
                    humanClient.Update();
                    if (serverHost.Server.TryGetRoom("ShowMatch1", out Game? g) && g is not null && g.CreateSnapshot().DuelistCount == 1)
                        break;
                    await Task.Delay(10);
                }

                // 4. 创建 streamer 账号，验证直播防窥（剥离玩家 IP、隐藏私密房口令、脱敏系统日志）与禁止越权签发 player Token
                using (HttpResponseMessage createStreamerRes = await adminHttp.PostAsync(
                           "/api/v1/users",
                           new StringContent(JsonSerializer.Serialize(new
                           {
                               username = "live_obs",
                               displayName = "Live Streamer",
                               initialPassword = "StreamerPassword#2026!",
                               roleCodes = new[] { BuiltInRoleCodes.Streamer },
                               mustChangePassword = false
                           }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)createStreamerRes.StatusCode);
                }

                CookieContainer streamerCookies = new();
                using HttpClient streamerHttp = new(new HttpClientHandler { CookieContainer = streamerCookies, UseCookies = true })
                {
                    BaseAddress = baseUri
                };
                using (HttpResponseMessage sLoginRes = await streamerHttp.PostAsync(
                           "/api/v1/auth/login",
                           new StringContent(JsonSerializer.Serialize(new { username = "live_obs", password = "StreamerPassword#2026!" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)sLoginRes.StatusCode);
                    using JsonDocument sDoc = JsonDocument.Parse(await sLoginRes.Content.ReadAsStringAsync());
                    streamerHttp.DefaultRequestHeaders.Add("X-CSRF-Token", sDoc.RootElement.GetProperty("csrfToken").GetString()!);
                }

                // Admin 可见完整房间密码与席位 IP；Streamer 响应中两者均被剥离
                using (HttpResponseMessage adminRoomsRes = await adminHttp.GetAsync("/api/v1/rooms"))
                using (HttpResponseMessage streamerRoomsRes = await streamerHttp.GetAsync("/api/v1/rooms"))
                {
                    using JsonDocument aRooms = JsonDocument.Parse(await adminRoomsRes.Content.ReadAsStringAsync());
                    using JsonDocument sRooms = JsonDocument.Parse(await streamerRoomsRes.Content.ReadAsStringAsync());

                    JsonElement aRoom = aRooms.RootElement.GetProperty("rooms")[0];
                    JsonElement sRoom = sRooms.RootElement.GetProperty("rooms")[0];

                    AssertEqual("RD#ShowMatch1", aRoom.GetProperty("joinPassword").GetString()!);
                    AssertEqual("127.0.0.1", aRoom.GetProperty("slots")[0].GetProperty("clientIp").GetString()!);
                    AssertEqual("PlayerAlpha", aRoom.GetProperty("slots")[0].GetProperty("playerName").GetString()!);

                    AssertFalse(sRoom.TryGetProperty("joinPassword", out JsonElement sPw) && sPw.ValueKind != JsonValueKind.Null);
                    AssertFalse(sRoom.GetProperty("slots")[0].TryGetProperty("clientIp", out JsonElement sIp) && sIp.ValueKind != JsonValueKind.Null);
                }

                using (HttpResponseMessage streamerLogsRes = await streamerHttp.GetAsync("/api/v1/system/logs"))
                {
                    using JsonDocument lDoc = JsonDocument.Parse(await streamerLogsRes.Content.ReadAsStringAsync());
                    AssertTrue(lDoc.RootElement.GetProperty("redactedForStreamer").GetBoolean());
                    AssertEqual(0, lDoc.RootElement.GetProperty("entries").GetArrayLength());
                }

                using (HttpResponseMessage streamerEscalation = await streamerHttp.PostAsync(
                           "/api/v1/mcp/tokens",
                           new StringContent(JsonSerializer.Serialize(new { name = "TryPlayer", mcpRole = "player" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.Forbidden, (int)streamerEscalation.StatusCode);
                }

                // 5. 验证一次性 WebSocket Ticket 握手与防重放
                string wsTicket;
                using (HttpResponseMessage ticketRes = await adminHttp.PostAsync("/api/v1/auth/ws-ticket", new StringContent("{}", Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)ticketRes.StatusCode);
                    using JsonDocument tDoc = JsonDocument.Parse(await ticketRes.Content.ReadAsStringAsync());
                    wsTicket = tDoc.RootElement.GetProperty("ticket").GetString()!;
                }

                Uri wsUri = new($"ws://{baseUri.Host}:{baseUri.Port}/ws/events?ticket={Uri.EscapeDataString(wsTicket)}");
                using (ClientWebSocket ws = new())
                {
                    await ws.ConnectAsync(wsUri, CancellationToken.None);
                    byte[] buf = new byte[4096];
                    WebSocketReceiveResult recv = await ws.ReceiveAsync(buf, CancellationToken.None);
                    string welcomeJson = Encoding.UTF8.GetString(buf, 0, recv.Count);
                    using JsonDocument wsDoc = JsonDocument.Parse(welcomeJson);
                    AssertEqual("connected", wsDoc.RootElement.GetProperty("type").GetString()!);
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }

                // 同一枚一次性 Ticket 重放必须失败
                using (ClientWebSocket replayWs = new())
                {
                    bool rejected = false;
                    try
                    {
                        await replayWs.ConnectAsync(wsUri, CancellationToken.None);
                    }
                    catch (WebSocketException)
                    {
                        rejected = true;
                    }
                    AssertTrue(rejected);
                }

                // 6. 验证客户端输入 R#1 口令直接下发云录像 (.yrp) 以及封禁名单实时拦截进房
                byte[] cloudYrp = [0x59, 0x52, 0x50, 0x31, 0xAA, 0xBB, 0xCC];
                await store.SaveDuelRecordAsync(new SaveDuelRecordRequest(
                    RoomName: "CloudRoom",
                    RoomIdentifier: "CloudRoom",
                    Mode: 0,
                    Region: 0,
                    MasterRule: 5,
                    StartLp: 8000,
                    HostInfoJson: "{}",
                    RandomDeckMode: "None",
                    IsSuddenDeath: false,
                    DuelCount: 1,
                    TurnCount: 3,
                    WinnerTeam: 0,
                    WinReason: 2,
                    Seed: 7U,
                    StartedAt: DateTime.UtcNow.AddMinutes(-2),
                    EndedAt: DateTime.UtcNow,
                    YrpBytes: cloudYrp,
                    ObserverPacketFrames: null,
                    Players: []));

                YGOClient replayClient = serverHost.ConnectLocalClient();
                List<byte[]> replayPackets = new();
                replayClient.PacketReceivedRaw += p => replayPackets.Add(p.ToArray());
                SendPlayerJoin(replayClient, "ReplayWatcher", "R#1");

                DateTime replayDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < replayDeadline && !replayPackets.Any(p => p.Length > 1 && p[0] == (byte)StocMessage.Replay))
                {
                    replayClient.Update();
                    await Task.Delay(10);
                }

                byte[]? receivedReplayPkt = replayPackets.FirstOrDefault(p => p.Length > 1 && p[0] == (byte)StocMessage.Replay);
                AssertTrue(receivedReplayPkt is not null);
                AssertSequenceEqual(cloudYrp, receivedReplayPkt!.AsSpan(1).ToArray());

                // 面板添加昵称封禁，验证新连接立即被拦截无法进入房间
                using (HttpResponseMessage banRes = await adminHttp.PostAsync(
                           "/api/v1/bans",
                           new StringContent(JsonSerializer.Serialize(new { targetType = "name", targetValue = "ToxicCheater", reason = "Cheating" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)banRes.StatusCode);
                }

                YGOClient bannedClient = serverHost.ConnectLocalClient();
                List<byte[]> bannedPackets = new();
                bannedClient.PacketReceivedRaw += p => bannedPackets.Add(p.ToArray());
                SendPlayerJoin(bannedClient, "ToxicCheater", "ShowMatch1");

                DateTime banDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < banDeadline && !bannedPackets.Any(p => p.Length >= 1 && p[0] == (byte)StocMessage.ErrorMsg))
                {
                    bannedClient.Update();
                    await Task.Delay(10);
                }

                AssertTrue(bannedPackets.Any(p => p.Length >= 1 && p[0] == (byte)StocMessage.ErrorMsg));
                AssertTrue(serverHost.Server.TryGetRoom("ShowMatch1", out Game? roomAfterBan) && roomAfterBan!.CreateSnapshot().DuelistCount == 1);

                humanClient.Close();
                await serverHost.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WebDiyStudioSandboxProposalsHotReloadAndDiyDuelEndToEndAsync()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpDiyE2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "script"));

        try
        {
            // 准备基础卡库与最小标准 Lua 常量脚本，使真实 ocgcore 对局与沙盒均可运行
            List<(int Id, int Ot, int Alias, long Setcode, int Type, int Level, int Race, int Attr, int Atk, int Def, string Name, string Desc)> baseCards = new();
            for (int i = 1; i <= 15; i++)
                baseCards.Add((1000 + i, 3, 0, 0, (int)(CardType.Monster | CardType.Normal), 4, 1, 1, 1800, 1000, $"BaseWarrior{i}", "vanilla"));
            CreateTestCardDb(Path.Combine(tempDir, "cards.cdb"), baseCards);

            await File.WriteAllTextAsync(Path.Combine(tempDir, "script", "constant.lua"), """
                EFFECT_TYPE_ACTIVATE = 0x0010
                EFFECT_TYPE_IGNITION = 0x0040
                EVENT_FREE_CHAIN = 1002
                LOCATION_MZONE = 0x04
                CATEGORY_RECOVER = 0x100000
                REASON_EFFECT = 0x40
                """);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "script", "utility.lua"), """
                function GetID()
                    return self_table, self_code, 1
                end
                """);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "script", "procedure.lua"), "-- procedure stub\n");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "lflist.conf"), "!TestBanlist\n");

            PersistenceOptions persistenceOptions = new()
            {
                DbProvider = OperationalDbProvider.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(tempDir, "diy_e2e.db")}",
                ReplayDirectory = Path.Combine(tempDir, "replays"),
                Argon2MemoryKb = 4096,
                Argon2Iterations = 1,
                Argon2Parallelism = 1,
                LoginRateLimitCount = 50
            };

            await using (OperationalStore store = new(persistenceOptions))
            {
                BootstrapAdminResult boot = await store.InitializeAsync();
                OperationalServerCoordinator coordinator = new(store);

                ServerOptions serverOptions = new()
                {
                    RootPath = tempDir,
                    DatabaseFile = "cards.cdb",
                    ScriptDirectory = "script",
                    BanlistFile = "lflist.conf",
                    EnableNetworkListener = false,
                    EnableMultiRoom = true,
                    AutoLoadExpansions = true,
                    AddonFactories = [coordinator.CreateRoomAddonFactory()],
                    JoinAccessValidator = coordinator.CreateJoinAccessValidator(),
                    Game = new GameOptions
                    {
                        MasterRule = 5,
                        DeckRules = new DeckRules(MainDeckMinSize: 10, MainDeckMaxSize: 40)
                    }
                };

                using NativeOcgRuntime runtime = new();
                await using YGOProSharpServerHost serverHost = YGOProSharpServerHost.Create(serverOptions, runtime);
                serverHost.StartBackgroundLoop(tickDelayMilliseconds: 2, embedded: true);

                await using WebControlHost webHost = new(
                    serverHost,
                    store,
                    coordinator,
                    new WebControlOptions { Host = "127.0.0.1", Port = 0 });
                await webHost.StartAsync();

                Uri baseUri = new(webHost.BoundAddresses.First());
                CookieContainer cookies = new();
                using HttpClient http = new(new HttpClientHandler { CookieContainer = cookies, UseCookies = true })
                {
                    BaseAddress = baseUri
                };

                // 登录并完成首次改密
                using (HttpResponseMessage loginRes = await http.PostAsync(
                           "/api/v1/auth/login",
                           new StringContent(JsonSerializer.Serialize(new { username = "admin", password = boot.GeneratedOneTimePassword }), Encoding.UTF8, "application/json")))
                {
                    using JsonDocument doc = JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
                    http.DefaultRequestHeaders.Add("X-CSRF-Token", doc.RootElement.GetProperty("csrfToken").GetString()!);
                }

                using (HttpResponseMessage pwRes = await http.PostAsync(
                           "/api/v1/auth/change-password",
                           new StringContent(JsonSerializer.Serialize(new { currentPassword = boot.GeneratedOneTimePassword, newPassword = "Starlight#Road2026!" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)pwRes.StatusCode);
                }

                var diyHealSpellCard = new
                {
                    id = 99000001,
                    ot = 4,
                    type = (int)CardType.Spell,
                    name = "星辉回复术",
                    desc = "①：自己回复1000基本分。"
                };

                string validSpellLua = """
                    local s,id=GetID()
                    function s.initial_effect(c)
                        local e1=Effect.CreateEffect(c)
                        e1:SetType(EFFECT_TYPE_ACTIVATE)
                        e1:SetCode(EVENT_FREE_CHAIN)
                        e1:SetTarget(s.rectg)
                        e1:SetOperation(s.recop)
                        c:RegisterEffect(e1)
                    end
                    function s.rectg(e,tp,eg,ep,ev,re,r,rp,chk)
                        if chk==0 then return true end
                        Duel.SetOperationInfo(0,CATEGORY_RECOVER,nil,0,tp,1000)
                    end
                    function s.recop(e,tp,eg,ep,ev,re,r,rp)
                        Duel.Recover(tp,1000,REASON_EFFECT)
                    end
                    """;

                // 1. 验证 /api/v1/diy/sandbox-test 拦截危险调用、捕获 Lua 运行时崩溃，以及验证有效 DIY 卡在 ocgcore 中发动使 P0 LP 从 8000 升至 9000
                using (HttpResponseMessage unsafeSandboxRes = await http.PostAsync(
                           "/api/v1/diy/sandbox-test",
                           new StringContent(JsonSerializer.Serialize(new { card = diyHealSpellCard, luaScript = "os.execute('calc')" }), Encoding.UTF8, "application/json")))
                {
                    using JsonDocument d = JsonDocument.Parse(await unsafeSandboxRes.Content.ReadAsStringAsync());
                    JsonElement r = d.RootElement.GetProperty("result");
                    AssertFalse(r.GetProperty("success").GetBoolean());
                    AssertFalse(r.GetProperty("nativeExecuted").GetBoolean());
                    AssertTrue(r.GetProperty("staticAuditViolations").GetArrayLength() > 0);
                }

                using (HttpResponseMessage brokenLuaRes = await http.PostAsync(
                           "/api/v1/diy/sandbox-test",
                           new StringContent(JsonSerializer.Serialize(new { card = diyHealSpellCard, luaScript = "local s,id=GetID()\nfunction s.initial_effect(c)\n call_nonexistent_api()\nend\n" }), Encoding.UTF8, "application/json")))
                {
                    using JsonDocument d = JsonDocument.Parse(await brokenLuaRes.Content.ReadAsStringAsync());
                    JsonElement r = d.RootElement.GetProperty("result");
                    AssertFalse(r.GetProperty("success").GetBoolean());
                    AssertTrue(r.GetProperty("nativeExecuted").GetBoolean());
                    AssertTrue(r.GetProperty("luaErrors").GetArrayLength() > 0);
                }

                using (HttpResponseMessage okSandboxRes = await http.PostAsync(
                           "/api/v1/diy/sandbox-test",
                           new StringContent(JsonSerializer.Serialize(new { card = diyHealSpellCard, luaScript = validSpellLua, testExecuteFirstActivation = true }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)okSandboxRes.StatusCode);
                    using JsonDocument d = JsonDocument.Parse(await okSandboxRes.Content.ReadAsStringAsync());
                    JsonElement r = d.RootElement.GetProperty("result");
                    AssertTrue(r.GetProperty("success").GetBoolean());
                    AssertTrue(r.GetProperty("nativeExecuted").GetBoolean());
                    AssertEqual(0, r.GetProperty("luaErrors").GetArrayLength());
                    AssertTrue(r.GetProperty("activatableCount").GetInt32() >= 1);
                    AssertEqual(9000, r.GetProperty("postPlayer0Lp").GetInt32());
                }

                // 2. 通过 Proposal 门禁提交新 DIY 卡 + 主题预组 .ydk，审批后自动落盘并触发免重启热重载
                string diyPresetYdk = string.Join("\n", ["#main", .. Enumerable.Repeat("99000001", 10), "#extra", "!side"]);
                Guid proposalId;
                using (HttpResponseMessage submitPropRes = await http.PostAsync(
                           "/api/v1/diy/proposals",
                           new StringContent(JsonSerializer.Serialize(new
                           {
                               packageId = "star-diy",
                               title = "新增星辉回复术与测试盲盒预组",
                               payload = new
                               {
                                   cards = new[] { new { card = diyHealSpellCard, luaScript = validSpellLua } },
                                   decks = new[] { new { name = "StarHealPreset", ydkContent = diyPresetYdk } },
                                   stringsConf = "!setname 0xa100 星辉\n"
                               }
                           }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)submitPropRes.StatusCode);
                    using JsonDocument d = JsonDocument.Parse(await submitPropRes.Content.ReadAsStringAsync());
                    proposalId = d.RootElement.GetProperty("proposal").GetProperty("id").GetGuid();
                }

                using (HttpResponseMessage approveRes = await http.PostAsync(
                           $"/api/v1/diy/proposals/{proposalId}/review",
                           new StringContent(JsonSerializer.Serialize(new { approve = true, comment = "沙盒已验证通过，准予合并" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)approveRes.StatusCode);
                    using JsonDocument d = JsonDocument.Parse(await approveRes.Content.ReadAsStringAsync());
                    AssertEqual("approved", d.RootElement.GetProperty("proposal").GetProperty("status").GetString()!);
                    AssertEqual(2L, d.RootElement.GetProperty("reload").GetProperty("catalogVersion").GetInt64());
                    AssertEqual(1, d.RootElement.GetProperty("reload").GetProperty("presetDeckCount").GetInt32());
                }

                AssertTrue(serverHost.Server.CardRepository.TryGetCard(99000001, out Card hotLoadedCard));
                AssertEqual(4, hotLoadedCard.Ot);

                // 上传卡图并验证一键导出 .ypk
                using (MultipartFormDataContent imgForm = new())
                {
                    ByteArrayContent pngPart = new([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D]);
                    imgForm.Add(pngPart, "image", "99000001.png");
                    using HttpResponseMessage imgRes = await http.PostAsync("/api/v1/diy/packages/star-diy/cards/99000001/image", imgForm);
                    AssertEqual((int)HttpStatusCode.OK, (int)imgRes.StatusCode);
                }

                using (HttpResponseMessage exportRes = await http.GetAsync("/api/v1/diy/packages/star-diy/export.ypk"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)exportRes.StatusCode);
                    byte[] ypkBytes = await exportRes.Content.ReadAsByteArrayAsync();
                    using MemoryStream ms = new(ypkBytes);
                    using ZipArchive zip = new(ms, ZipArchiveMode.Read);
                    AssertTrue(zip.GetEntry("cards.cdb") is not null);
                    AssertTrue(zip.GetEntry("script/c99000001.lua") is not null);
                    AssertTrue(zip.GetEntry("pics/99000001.png") is not null);
                    AssertTrue(zip.GetEntry("decks/StarHealPreset.ydk") is not null);
                }

                // 3. 端到端对局：两名客户端使用 DIYRD,NC#LiveDiyRoom 免传卡组进房，随机抽到刚热重载的 DIY 魔法卡并在实战中成功发动 (+1000 LP)
                YGOClient p0 = serverHost.ConnectLocalClient();
                YGOClient p1 = serverHost.ConnectLocalClient();
                bool sawSelectIdle = false;

                p0.PacketReceivedRaw += pkt =>
                {
                    if (pkt.Length >= 2 && pkt.Span[0] == (byte)StocMessage.GameMsg)
                    {
                        GameMessage gm = (GameMessage)pkt.Span[1];
                        if (gm == GameMessage.SelectIdleCmd)
                        {
                            sawSelectIdle = true;
                        }
                        else if (gm == GameMessage.SelectPlace)
                        {
                            p0.Send([(byte)CtosMessage.Response, 0, (byte)CardLocation.SpellZone, 2]);
                        }
                    }
                };

                SendPlayerJoin(p0, "DiyTesterA", "DIYRD,NC#DiyRoom");
                SendPlayerJoin(p1, "DiyTesterB", "DIYRD,NC#DiyRoom");

                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
                Game? diyRoom = null;
                while (DateTime.UtcNow < deadline)
                {
                    p0.Update();
                    p1.Update();
                    if (serverHost.Server.TryGetRoom("DiyRoom", out diyRoom) && diyRoom is not null && diyRoom.CreateSnapshot().DuelistCount == 2)
                        break;
                    await Task.Delay(10);
                }
                AssertTrue(diyRoom is not null);

                SendSimpleCtos(p0, CtosMessage.HsReady);
                SendSimpleCtos(p1, CtosMessage.HsReady);
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
                while (DateTime.UtcNow < deadline && !(diyRoom!.IsReady[0] && diyRoom.IsReady[1]))
                {
                    p0.Update();
                    p1.Update();
                    await Task.Delay(10);
                }

                SendSimpleCtos(p0, CtosMessage.HsStart);
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
                while (DateTime.UtcNow < deadline && diyRoom!.State != GameState.Hand)
                {
                    p0.Update();
                    p1.Update();
                    await Task.Delay(10);
                }

                SendByteCtos(p0, CtosMessage.HandResult, 2);
                SendByteCtos(p1, CtosMessage.HandResult, 1);
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
                while (DateTime.UtcNow < deadline && diyRoom!.State != GameState.Starting)
                {
                    p0.Update();
                    p1.Update();
                    await Task.Delay(10);
                }

                SendByteCtos(p0, CtosMessage.TpResult, 1);

                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
                while (DateTime.UtcNow < deadline && !sawSelectIdle)
                {
                    p0.Update();
                    p1.Update();
                    await Task.Delay(10);
                }

                AssertTrue(sawSelectIdle);
                AssertEqual((int)GameState.Duel, (int)diyRoom!.State);
                AssertTrue(diyRoom.Players[0].Deck.Main.Contains(99000001));
                AssertEqual(8000, diyRoom.LifePoints[0]);

                // 先攻 Main1 直接发动手牌第 0 张「星辉回复术」((0 << 16) | 5)，验证实战 LP 变为 9000
                using (MemoryStream actStream = new())
                using (BinaryWriter w = new(actStream))
                {
                    w.Write((byte)CtosMessage.Response);
                    w.Write((0 << 16) | 5);
                    p0.Send(actStream.ToArray());
                }

                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
                while (DateTime.UtcNow < deadline && diyRoom.LifePoints[0] == 8000)
                {
                    p0.Update();
                    p1.Update();
                    await Task.Delay(10);
                }

                AssertEqual(9000, diyRoom.LifePoints[0]);

                p0.Close();
                p1.Close();
                await serverHost.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyCatalogStartupAndThreePortAdminPortalIsolationWorkAsync()
    {
        string emptyRoot = Path.Combine(Path.GetTempPath(), "YGOProSharpThreePort_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);

        try
        {
            PersistenceOptions persistenceOptions = new()
            {
                DbProvider = OperationalDbProvider.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(emptyRoot, "data", "db", "ops.db")}",
                ReplayDirectory = Path.Combine(emptyRoot, "data", "replays"),
                Argon2MemoryKb = 4096,
                Argon2Iterations = 1,
                Argon2Parallelism = 1,
                LoginRateLimitCount = 50
            };

            await using (OperationalStore store = new(persistenceOptions))
            {
                BootstrapAdminResult boot = await store.InitializeAsync();
                OperationalServerCoordinator coordinator = new(store);

                // 1. 验证完全空白目录（无 cards.cdb、无 lflist.conf、无 script/）下服务端可平滑启动，不抛 FileNotFoundException
                ServerOptions serverOptions = new()
                {
                    RootPath = emptyRoot,
                    Port = 7911,
                    EnableNetworkListener = false,
                    EnableMultiRoom = true,
                    AddonFactories = [coordinator.CreateRoomAddonFactory()],
                    JoinAccessValidator = coordinator.CreateJoinAccessValidator()
                };

                using ServerTestFakeOcgRuntime fakeRuntime = new();
                await using YGOProSharpServerHost serverHost = YGOProSharpServer.CreateHost(serverOptions, fakeRuntime);
                serverHost.StartBackgroundLoop(tickDelayMilliseconds: 2, embedded: true);

                // 2. 在两个完全独立的端口上分别启动后台管理面板 (WebControlHost) 与前台用户面板 (UserPortalHost)
                await using WebControlHost adminHost = new(
                    serverHost,
                    store,
                    coordinator,
                    new WebControlOptions { Host = "127.0.0.1", Port = 0 });
                await adminHost.StartAsync();

                await using UserPortalHost portalHost = new(
                    serverHost,
                    store,
                    coordinator,
                    new UserPortalOptions { Host = "127.0.0.1", Port = 0, PublicGameHost = "duel.local" });
                await portalHost.StartAsync();

                Uri adminUri = new(adminHost.BoundAddresses.First());
                Uri portalUri = new(portalHost.BoundAddresses.First());
                AssertTrue(adminUri.Port != portalUri.Port);

                using HttpClient portalHttp = new() { BaseAddress = portalUri };
                CookieContainer adminCookies = new();
                using HttpClient adminHttp = new(new HttpClientHandler { CookieContainer = adminCookies, UseCookies = true })
                {
                    BaseAddress = adminUri
                };

                // 3. 验证单端口融合控制面同时支持默认玩家门户 (/) 与 /admin 管理路径，且可选独立门户端口物理不存在后台接口 (404)
                using (HttpResponseMessage pSurface = await portalHttp.GetAsync("/api/surface"))
                using (HttpResponseMessage aSurface = await adminHttp.GetAsync("/api/surface"))
                {
                    using JsonDocument pDoc = JsonDocument.Parse(await pSurface.Content.ReadAsStringAsync());
                    using JsonDocument aDoc = JsonDocument.Parse(await aSurface.Content.ReadAsStringAsync());
                    AssertEqual("portal", pDoc.RootElement.GetProperty("surface").GetString()!);
                    AssertEqual("admin", aDoc.RootElement.GetProperty("surface").GetString()!);
                    AssertTrue(aDoc.RootElement.GetProperty("unifiedPortal").GetBoolean());
                    AssertEqual("portal", aDoc.RootElement.GetProperty("defaultPage").GetString()!);
                    AssertEqual("/admin", aDoc.RootElement.GetProperty("adminPath").GetString()!);
                }

                // 单端口下 /api/portal/* 公开接口直接在同一端口工作，且 SPA 回退同时服务 / 与 /admin
                using (HttpResponseMessage unifiedOverviewRes = await adminHttp.GetAsync("/api/portal/overview"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)unifiedOverviewRes.StatusCode);
                }
                using (HttpResponseMessage rootHtmlRes = await adminHttp.GetAsync("/"))
                using (HttpResponseMessage adminHtmlRes = await adminHttp.GetAsync("/admin"))
                {
                    if (rootHtmlRes.StatusCode == HttpStatusCode.OK)
                    {
                        AssertEqual((int)HttpStatusCode.OK, (int)adminHtmlRes.StatusCode);
                        string adminHtml = await adminHtmlRes.Content.ReadAsStringAsync();
                        AssertTrue(adminHtml.Contains("<!doctype html>", StringComparison.OrdinalIgnoreCase) || adminHtml.Contains("<html", StringComparison.OrdinalIgnoreCase));
                    }
                }

                foreach (string adminOnlyPath in new[] { "/api/v1/auth/login", "/api/v1/users", "/api/v1/bans", "/api/v1/server/status" })
                {
                    using HttpResponseMessage res = await portalHttp.GetAsync(adminOnlyPath);
                    AssertEqual((int)HttpStatusCode.NotFound, (int)res.StatusCode);
                }

                // 4. 模拟决斗者与观战者进房，验证观众列表 (observers)、快速主机密码与前台用户面板剥离玩家 IP
                YGOClient p0 = serverHost.ConnectLocalClient();
                SendPlayerJoin(p0, "PublicDuelist", "M#OpenTable1");
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < deadline)
                {
                    p0.Update();
                    if (serverHost.Server.TryGetRoom("OpenTable1", out Game? g) && g is not null && g.CreateSnapshot().DuelistCount == 1)
                        break;
                    await Task.Delay(10);
                }

                YGOClient spectatorClient = serverHost.ConnectLocalClient();
                SendPlayerJoin(spectatorClient, "WatcherOne", "OpenTable1");
                DateTime specJoinDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < specJoinDeadline)
                {
                    spectatorClient.Update();
                    if (serverHost.Server.TryGetRoom("OpenTable1", out Game? g) && g is not null && g.CreateSnapshot().DuelistCount == 2)
                        break;
                    await Task.Delay(10);
                }
                spectatorClient.Send(CtosMessage.HsToObserver);
                DateTime specMoveDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < specMoveDeadline)
                {
                    spectatorClient.Update();
                    if (serverHost.Server.TryGetRoom("OpenTable1", out Game? g) && g is not null && g.CreateSnapshot().ObserverCount == 1)
                        break;
                    await Task.Delay(10);
                }
                await store.Cache.FlushNamespaceAsync("portal");

                using (HttpResponseMessage portalRoomsRes = await portalHttp.GetAsync("/api/portal/rooms"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)portalRoomsRes.StatusCode);
                    using JsonDocument doc = JsonDocument.Parse(await portalRoomsRes.Content.ReadAsStringAsync());
                    JsonElement room = doc.RootElement.GetProperty("rooms")[0];
                    AssertEqual("OpenTable1", room.GetProperty("roomName").GetString()!);
                    AssertEqual("OpenTable1", room.GetProperty("hostPassword").GetString()!);
                    AssertEqual("M#OpenTable1", room.GetProperty("fullHostPassword").GetString()!);
                    AssertFalse(room.TryGetProperty("joinPassword", out _));
                    AssertFalse(room.GetProperty("slots")[0].TryGetProperty("clientIp", out _));

                    JsonElement observers = room.GetProperty("observers");
                    AssertEqual(1, observers.GetArrayLength());
                    AssertEqual("WatcherOne", observers[0].GetProperty("playerName").GetString()!);
                    AssertFalse(observers[0].TryGetProperty("clientIp", out _));
                }

                // 4.5 验证前台玩家自助注册账号、选择完整房间配置建房并生成 AI Token、独立签发/吊销个人 AI Token 及越权拦截
                CookieContainer playerCookies = new();
                using HttpClient playerHttp = new(new HttpClientHandler { CookieContainer = playerCookies, UseCookies = true })
                {
                    BaseAddress = portalUri
                };

                using (HttpResponseMessage regRes = await playerHttp.PostAsync(
                           "/api/portal/auth/register",
                           new StringContent(JsonSerializer.Serialize(new { username = "judai", password = "123456" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)regRes.StatusCode);
                    using JsonDocument regDoc = JsonDocument.Parse(await regRes.Content.ReadAsStringAsync());
                    AssertEqual("judai", regDoc.RootElement.GetProperty("user").GetProperty("username").GetString()!);
                    playerHttp.DefaultRequestHeaders.Add("X-CSRF-Token", regDoc.RootElement.GetProperty("csrfToken").GetString()!);
                }

                using (HttpResponseMessage createPortalRoomRes = await playerHttp.PostAsync(
                           "/api/portal/rooms",
                           new StringContent(JsonSerializer.Serialize(new
                           {
                               roomName = "HeroTag01",
                               mode = 2,
                               region = 3,
                               masterRule = 5,
                               randomDeckMode = "MirrorRandom",
                               startLp = 12000,
                               gameTimer = 300,
                               noCheckDeck = true,
                               suddenDeath = true,
                               issuePlayerTokens = true,
                               aiTokenCount = 3
                           }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)createPortalRoomRes.StatusCode);
                    using JsonDocument cDoc = JsonDocument.Parse(await createPortalRoomRes.Content.ReadAsStringAsync());
                    AssertEqual("HeroTag01", cDoc.RootElement.GetProperty("hostPassword").GetString()!);
                    AssertEqual("2V2,DIY,RDM,LP12000,TM300,NC,ED#HeroTag01", cDoc.RootElement.GetProperty("fullHostPassword").GetString()!);
                    AssertEqual(3, cDoc.RootElement.GetProperty("issuedPlayerTokens").GetArrayLength());
                }

                AssertTrue(serverHost.Server.TryGetRoom("HeroTag01", out Game? heroRoom) && heroRoom is not null);
                AssertTrue(heroRoom!.IsTag);
                AssertEqual(3, heroRoom.Region);
                AssertEqual(12000, heroRoom.StartLp);
                AssertEqual(300, heroRoom.Timer);
                AssertTrue(heroRoom.IsSuddenDeath);

                // 普通玩家禁止在前台签发 diy_creator / admin 令牌，允许签发 spectator / player 令牌并吊销
                using (HttpResponseMessage forbiddenMcpRes = await playerHttp.PostAsync(
                           "/api/portal/mcp/tokens",
                           new StringContent(JsonSerializer.Serialize(new { name = "EscalateAttempt", mcpRole = "admin" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.Forbidden, (int)forbiddenMcpRes.StatusCode);
                }

                string specTokenId;
                using (HttpResponseMessage issueSpecRes = await playerHttp.PostAsync(
                           "/api/portal/mcp/tokens",
                           new StringContent(JsonSerializer.Serialize(new { name = "MyCasterAI", mcpRole = "spectator", allowedRooms = new[] { "HeroTag01" } }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)issueSpecRes.StatusCode);
                    using JsonDocument iDoc = JsonDocument.Parse(await issueSpecRes.Content.ReadAsStringAsync());
                    specTokenId = iDoc.RootElement.GetProperty("token").GetProperty("id").GetString()!;
                }

                using (HttpResponseMessage listTokensRes = await playerHttp.GetAsync("/api/portal/mcp/tokens"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)listTokensRes.StatusCode);
                    using JsonDocument ltDoc = JsonDocument.Parse(await listTokensRes.Content.ReadAsStringAsync());
                    AssertEqual(4, ltDoc.RootElement.GetProperty("tokens").GetArrayLength());
                }

                using (HttpResponseMessage revokeRes = await playerHttp.DeleteAsync($"/api/portal/mcp/tokens/{specTokenId}"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)revokeRes.StatusCode);
                }

                // 禁止关闭他人房间，允许关闭本人创建的房间
                using (HttpResponseMessage closeOthersRes = await playerHttp.DeleteAsync("/api/portal/rooms/OpenTable1"))
                {
                    AssertEqual((int)HttpStatusCode.Forbidden, (int)closeOthersRes.StatusCode);
                }
                using (HttpResponseMessage closeOwnRes = await playerHttp.DeleteAsync("/api/portal/rooms/HeroTag01"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)closeOwnRes.StatusCode);
                }

                // 5. 验证玩家在前台用户面板自助绑定 $vpass、提交赛事 .ydk 卡组及投递 DIY 新卡提案，并在管理后台收到待审提案
                using (HttpResponseMessage vpassRes = await portalHttp.PostAsync(
                           "/api/portal/player/vpass",
                           new StringContent(JsonSerializer.Serialize(new { playerName = "Yusei", vpass = "Stardust" }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)vpassRes.StatusCode);
                }

                string fortyCardYdk = string.Join("\n", ["#main", .. Enumerable.Repeat("89631139", 40), "#extra", "!side"]);
                using (HttpResponseMessage deckRes = await portalHttp.PostAsync(
                           "/api/portal/tournament-decks",
                           new StringContent(JsonSerializer.Serialize(new { tournamentTag = "cup01", playerName = "Yusei", ydkContent = fortyCardYdk }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)deckRes.StatusCode);
                }

                using (HttpResponseMessage propRes = await portalHttp.PostAsync(
                           "/api/portal/diy/proposals",
                           new StringContent(JsonSerializer.Serialize(new
                           {
                               packageId = "community-diy",
                               authorName = "Yusei",
                               card = new { id = 99200001, ot = 4, type = 0x21, name = "社区投稿新星", desc = "测试投稿" },
                               luaScript = "local s,id=GetID()\nfunction s.initial_effect(c)\nend\n"
                           }), Encoding.UTF8, "application/json")))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)propRes.StatusCode);
                }

                // 登录管理后台端口，确认收到来自玩家面板投递的待审提案
                using (HttpResponseMessage loginRes = await adminHttp.PostAsync(
                           "/api/v1/auth/login",
                           new StringContent(JsonSerializer.Serialize(new { username = "admin", password = boot.GeneratedOneTimePassword }), Encoding.UTF8, "application/json")))
                {
                    using JsonDocument lDoc = JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
                    adminHttp.DefaultRequestHeaders.Add("X-CSRF-Token", lDoc.RootElement.GetProperty("csrfToken").GetString()!);
                }
                using (await adminHttp.PostAsync(
                           "/api/v1/auth/change-password",
                           new StringContent(JsonSerializer.Serialize(new { currentPassword = boot.GeneratedOneTimePassword, newPassword = "Shooting#Quasar2026!" }), Encoding.UTF8, "application/json")))
                {
                }

                using (HttpResponseMessage adminPropsRes = await adminHttp.GetAsync("/api/v1/diy/proposals?status=pending"))
                {
                    AssertEqual((int)HttpStatusCode.OK, (int)adminPropsRes.StatusCode);
                    using JsonDocument aDoc = JsonDocument.Parse(await adminPropsRes.Content.ReadAsStringAsync());
                    AssertEqual(1, aDoc.RootElement.GetProperty("proposals").GetArrayLength());
                }

                p0.Close();
                spectatorClient.Close();
                await serverHost.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }

}
