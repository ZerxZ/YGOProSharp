using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WindBot;
using WindBot.Game.AI;
using WindBot.Game.AI.Lua;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Scripting;
using YGOProSharp.AgentBridge;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Mcp.Services;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Server;

namespace YGOProSharp.Mcp.Tools;

/// <summary>
/// DIY 卡片沙盒与提案、WindBot Lua 脚本编写模拟，以及房间创建/管理运维工具。
/// </summary>
public sealed class DiyAndAdminTools(McpRuntimeDependencies deps, DeckAndReplayAppService appService)
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILogger _logger = AppLog.CreateLogger<DiyAndAdminTools>();

    [McpServerTool(Name = "diy_list_packages")]
    [McpRoles(McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("列出已安装的受管 DIY 扩展包清单与状态。")]
    public ToolOutcome DiyListPackages()
    {
        return ToolOutcome.Ok(new { packages = deps.DiyManager.ListPackages(enabledOnly: false) });
    }

    [McpServerTool(Name = "diy_test_card_sandbox")]
    [McpRoles(McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("在进程内 ocgcore 原生引擎沙盒中实测单张 DIY 卡的 Lua 脚本（检验 initial_effect 注册与发动/处理无报错）。")]
    public ToolOutcome DiyTestCardSandbox(
        int cardId,
        string luaScript,
        string? name = null,
        int type = 0x21,
        int level = 4,
        int attack = 1800,
        int defense = 1000,
        int race = 1,
        int attribute = 1,
        bool testActivation = true)
    {
        LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(luaScript ?? string.Empty);
        if (!audit.IsSafe)
            return ToolOutcome.Fail(new { message = "Lua 脚本未通过安全静态审计：" + string.Join("; ", audit.Violations) });

        DiyCardEntry entry = new()
        {
            Id = cardId > 0 ? cardId : 99000001,
            Ot = 4,
            Alias = 0,
            Setcode = 0,
            Type = type,
            Attack = attack,
            Defense = defense,
            Level = level,
            Race = race,
            Attribute = attribute,
            Category = 0,
            Name = !string.IsNullOrWhiteSpace(name) ? name.Trim() : $"DIY_{cardId}",
            Desc = "Sandbox test card",
            Strings = []
        };

        CardSandboxResult res = deps.ServerHost.Server.RunCardSandboxTest(new CardSandboxRequest(entry, luaScript ?? string.Empty, testActivation));
        return ToolOutcome.From(res.Success, res);
    }

    [McpServerTool(Name = "diy_upsert_card")]
    [McpRoles(McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("向 DIY 包提交新卡或修改卡片（若 Token 开启了审批门禁则自动创建待审 Proposal，否则直接写入包）。")]
    public async Task<ToolOutcome> DiyUpsertCardAsync(
        string packageId,
        int cardId,
        string name,
        string desc,
        int type = 0x21,
        int level = 4,
        int attack = 0,
        int defense = 0,
        int race = 1,
        int attribute = 1,
        string? luaScript = null,
        CancellationToken ct = default)
    {
        string pkgId = packageId?.Trim() ?? string.Empty;
        if (!DiyPackageManager.IsValidPackageId(pkgId))
            return ToolOutcome.Fail(new { message = "非法 DIY 包 ID。" });

        if (cardId <= 0)
            return ToolOutcome.Fail(new { message = "cardId 必须为正整数。" });

        if (!string.IsNullOrEmpty(luaScript))
        {
            LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(luaScript);
            if (!audit.IsSafe)
                return ToolOutcome.Fail(new { message = "Lua 安全预检未通过：" + string.Join("; ", audit.Violations) });
        }

        DiyCardEntry entry = new()
        {
            Id = cardId,
            Ot = 4,
            Alias = 0,
            Setcode = 0,
            Type = type,
            Attack = attack,
            Defense = defense,
            Level = level,
            Race = race,
            Attribute = attribute,
            Category = 0,
            Name = name?.Trim() ?? string.Empty,
            Desc = desc?.Trim() ?? string.Empty,
            Strings = []
        };

        McpCallerScope scope = McpCallerScope.Current;
        ValidatedMcpToken caller = scope.Caller;
        if (caller.RequireApproval)
        {
            string diffJson = JsonSerializer.Serialize(new
            {
                cards = new[]
                {
                    new
                    {
                        card = new
                        {
                            id = entry.Id,
                            ot = entry.Ot,
                            type = entry.Type,
                            attack = entry.Attack,
                            defense = entry.Defense,
                            level = entry.Level,
                            race = entry.Race,
                            attribute = entry.Attribute,
                            name = entry.Name,
                            desc = entry.Desc
                        },
                        luaScript
                    }
                }
            }, CompactJson);

            DiyProposalEntity prop = await deps.Store.CreateDiyProposalAsync(
                pkgId,
                $"[MCP:{caller.Name}] {entry.Name} (#{entry.Id})",
                diffJson,
                caller.TokenId,
                caller.Name,
                "mcp_token",
                caller.TokenId.ToString(),
                scope.RemoteIp,
                ct).ConfigureAwait(false);

            return ToolOutcome.Ok(new { mode = "proposal_created", proposalId = prop.Id, status = prop.Status });
        }

        deps.DiyManager.UpsertManifest(new DiyPackageManifest
        {
            Id = pkgId,
            Name = pkgId,
            Enabled = true,
            Priority = 100
        });
        deps.DiyManager.UpsertCard(pkgId, entry, luaScript);
        return ToolOutcome.Ok(new { mode = "applied_directly", packageId = pkgId, cardId = entry.Id });
    }

    [McpServerTool(Name = "deck_inspect")]
    [McpRoles(McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("解析指定预设卡组或 .ydk 文本，返回卡组内每张卡的卡名、数量与效果文本，便于分析展开链和编写 AI。")]
    public ToolOutcome DeckInspect(
        string? presetName = null,
        string? ydkContent = null)
    {
        YdkDeckData? data = null;
        if (!string.IsNullOrWhiteSpace(ydkContent))
            data = YdkSerializer.Parse(ydkContent.Trim(), "Inspect");
        else if (!string.IsNullOrWhiteSpace(presetName))
            data = appService.Presets.FindByNameOrId(deps.ServerHost.Server, presetName)?.Data;

        if (data is null)
            return ToolOutcome.Fail(new { message = "请提供有效的 presetName 或 ydkContent。" });

        INamedCardRepository repo = deps.NamedCardRepositoryProvider();
        object Describe(IReadOnlyList<int> ids) => ids.GroupBy(static x => x).Select(g =>
        {
            repo.TryGetCard(g.Key, out NamedCard? nc);
            return new
            {
                id = g.Key,
                copies = g.Count(),
                name = nc?.Name ?? $"#{g.Key}",
                type = nc?.Type,
                attack = nc?.Attack,
                defense = nc?.Defense,
                desc = nc?.Description
            };
        }).ToArray();

        return ToolOutcome.Ok(new
        {
            data.Name,
            main = Describe(data.Main),
            extra = Describe(data.Extra),
            side = Describe(data.Side)
        });
    }

    [McpServerTool(Name = "windbot_list_lua_scripts")]
    [McpRoles(McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("列出已注册的内置 WindBot 卡组与自定义 Lua 卡组执行器列表。")]
    public ToolOutcome WindbotListLuaScripts()
    {
        string luaDir = Path.Combine(Path.GetFullPath(deps.ServerHost.Server.Options.RootPath), "windbot", "Decks", "Lua");
        string[] customLua = Directory.Exists(luaDir)
            ? Directory.EnumerateFiles(luaDir, "*.lua").Select(Path.GetFileNameWithoutExtension).Where(static x => x is not null).Cast<string>().ToArray()
            : [];

        return ToolOutcome.Ok(new
        {
            builtInDecks = DecksManager.GetRegisteredDeckNames(),
            customLuaScripts = customLua
        });
    }

    [McpServerTool(Name = "windbot_write_deck_script")]
    [McpRoles(McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("对 WindBot Lua 卡组脚本执行静态安全审计与 NLua 沙箱干跑预检，通过后保存到受管 windbot/Decks/Lua 目录。")]
    public async Task<ToolOutcome> WindbotWriteDeckScriptAsync(
        [Description("不含扩展名的脚本名，如 MyHeroAI")] string scriptName,
        [Description("xmake 风格 WindBot Lua 源码")] string luaSource,
        CancellationToken ct = default)
    {
        string name = scriptName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || !DiyPackageManager.IsValidPackageId(name))
            return ToolOutcome.Fail(new { message = "scriptName 仅允许字母、数字、下划线或连字符。" });

        LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(luaSource ?? string.Empty);
        if (!audit.IsSafe)
            return ToolOutcome.Fail(new { message = "Lua 安全审计拒绝：" + string.Join("; ", audit.Violations) });

        ILuaRuntime? runtime = deps.LuaRuntimeProvider?.Invoke();
        if (runtime is not null)
        {
            WindBot.Game.Duel dummyDuel = new();
            WindBot.Game.GameClient dummyClient = new(new WindBotInfo { Name = "DryRun" }, deps.NamedCardRepositoryProvider());
            WindBot.Game.GameAI dummyAi = new(dummyClient, dummyDuel);
            using LuaDeckExecutor dryExec = new(dummyAi, dummyDuel, runtime, luaSource ?? string.Empty, $"{name}.lua", throwOnScriptError: true);
        }

        string luaDir = Path.Combine(Path.GetFullPath(deps.ServerHost.Server.Options.RootPath), "windbot", "Decks", "Lua");
        Directory.CreateDirectory(luaDir);
        string targetPath = Path.Combine(luaDir, $"{name}.lua");
        await File.WriteAllTextAsync(targetPath, luaSource ?? string.Empty, ct).ConfigureAwait(false);

        McpCallerScope scope = McpCallerScope.Current;
        await deps.Store.AppendAuditAsync("mcp_token", scope.Caller.TokenId.ToString(), scope.Caller.Name, "mcp.windbot_write_lua", scope.Caller.McpRole, name, scope.RemoteIp, "ok", null, ct).ConfigureAwait(false);
        return ToolOutcome.Ok(new { success = true, scriptName = name, savedPath = $"windbot/Decks/Lua/{name}.lua" });
    }

    [McpServerTool(Name = "windbot_simulate_match")]
    [McpRoles(McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("在服务端纯内存通道内极速运行 1~3 局内置 WindBot 对战复盘，返回胜负、回合数、双方最终 LP 与 Lua 思考日志。")]
    public async Task<ToolOutcome> WindbotSimulateMatchAsync(
        string? deckA = null,
        string? deckB = null,
        int duelCount = 1,
        CancellationToken ct = default)
    {
        if (deps.SpawnWindBotCallback is null)
            return ToolOutcome.Fail(new { message = "当前服务端未加载可用卡库，无法启动内置 WindBot 模拟。" });

        int count = Math.Clamp(duelCount, 1, 3);
        string effDeckA = string.IsNullOrWhiteSpace(deckA) ? "random" : deckA.Trim();
        string effDeckB = string.IsNullOrWhiteSpace(deckB) ? "random" : deckB.Trim();

        List<LuaThoughtEntry> capturedThoughts = new();
        void OnThought(LuaThoughtEntry entry)
        {
            lock (capturedThoughts)
            {
                if (capturedThoughts.Count < 50)
                    capturedThoughts.Add(entry);
            }
        }

        LuaDeckExecutor.GlobalThoughtEmitted += OnThought;
        List<object> results = new(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                string simRoomName = $"Sim_{Guid.NewGuid():N}"[..11];
                GameOptions opts = deps.ServerHost.Server.Options.Game with
                {
                    Mode = 0,
                    NoCheckDeck = true,
                    RandomDeck = effDeckA == "random" && effDeckB == "random" ? RandomDeckMode.ChaosDraft : RandomDeckMode.None
                };

                if (!deps.ServerHost.Server.TryCreateRoom(simRoomName, out Game? simGame, opts) || simGame is null)
                    break;

                deps.SpawnWindBotCallback(simGame, effDeckA, null, 1);
                deps.SpawnWindBotCallback(simGame, effDeckB, null, 1);

                DateTime waitEnd = DateTime.UtcNow + TimeSpan.FromSeconds(12);
                while (DateTime.UtcNow < waitEnd && !ct.IsCancellationRequested)
                {
                    if (simGame.State == GameState.End || simGame.Winner >= 0)
                        break;
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }

                results.Add(new
                {
                    gameIndex = i + 1,
                    roomName = simRoomName,
                    winnerSeat = simGame.Winner,
                    turnCount = simGame.TurnCount,
                    lp0 = simGame.LifePoints[0],
                    lp1 = simGame.LifePoints[1]
                });

                CloseGameRoom(simGame);
            }
        }
        finally
        {
            LuaDeckExecutor.GlobalThoughtEmitted -= OnThought;
        }

        return ToolOutcome.Ok(new { matches = results, thoughts = capturedThoughts });
    }

    [McpServerTool(Name = "server_get_status")]
    [McpRoles(McpRoleCodes.Admin)]
    [Description("查看游戏服务端运行状态、活动房间数量与卡库版本。")]
    public ToolOutcome ServerGetStatus()
    {
        CoreServer srv = deps.ServerHost.Server;
        return ToolOutcome.Ok(new
        {
            srv.IsRunning,
            srv.IsListening,
            srv.IsMaintenanceMode,
            roomCount = deps.ServerHost.GetRooms().Count,
            activeMcpSessions = deps.SessionManager.ListAllActiveSessions().Count,
            catalogVersion = srv.ReloadableCards.CatalogVersion,
            presetDeckCount = srv.PresetDecks.Count
        });
    }

    [McpServerTool(Name = "room_create")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("在服务器创建一个公开对局房间（player 与 admin 均可用；支持规则前缀如 'CHAOS'、'RD'、'NC,NF'、'2V2,RDM'，并可一键召唤内置 WindBot 对手）。")]
    public async Task<ToolOutcome> RoomCreateAsync(
        [Description("房间名称（如 Arena01）")] string roomName,
        [Description("可选规则前缀，如 'CHAOS'、'RD'、'NC,NF'、'2V2,RDM'")] string? rulePrefix = null,
        bool? noCheckDeck = null,
        bool spawnWindBot = false,
        string? windBotDeck = null,
        int windBotCount = 1,
        CancellationToken ct = default)
    {
        string rawInput = roomName?.Trim() ?? string.Empty;
        string prefix = rulePrefix?.Trim() ?? string.Empty;
        if (rawInput.Contains('#'))
        {
            int hashIdx = rawInput.LastIndexOf('#');
            if (string.IsNullOrWhiteSpace(prefix))
                prefix = rawInput[..hashIdx].Trim();
            rawInput = rawInput[(hashIdx + 1)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(rawInput) || rawInput.Length > 24)
            return ToolOutcome.Fail(new { success = false, message = "roomName 长度必须在 1~24 个字符之间。" });

        McpCallerScope scope = McpCallerScope.Current;
        ValidatedMcpToken caller = scope.Caller;
        if (caller.McpRole != McpRoleCodes.Admin &&
            caller.AllowedRooms.Count > 0 &&
            !caller.AllowedRooms.Contains(rawInput, StringComparer.OrdinalIgnoreCase))
        {
            return ToolOutcome.Fail(new
            {
                success = false,
                error = "room_not_in_whitelist",
                message = $"当前 Token 仅被允许在房间白名单 [{string.Join(", ", caller.AllowedRooms)}] 内建房或进房。"
            });
        }

        GameOptions opts = deps.ServerHost.Server.Options.Game;
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            string spec = prefix.Contains('#') ? prefix : $"{prefix}#{rawInput}";
            opts = deps.ServerHost.Server.PasswordParser.Parse(spec).ApplyTo(opts);
        }
        if (noCheckDeck is not null)
            opts = opts with { NoCheckDeck = noCheckDeck.Value };

        bool created = deps.ServerHost.Server.TryCreateRoom(rawInput, out Game? g, opts);
        if (!created || g is null)
            return ToolOutcome.Fail(new { success = false, message = $"房间 '{rawInput}' 已存在或服务器已达房间上限。" });

        IReadOnlyList<string>? spawnedBots = null;
        if (spawnWindBot && deps.SpawnWindBotCallback is not null)
        {
            int botCnt = Math.Clamp(windBotCount, 1, g.IsTag ? 3 : 1);
            try
            {
                spawnedBots = deps.SpawnWindBotCallback(g, windBotDeck, null, botCnt);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Optional WindBot spawn during room_create failed.");
            }
        }

        await deps.Store.AppendAuditAsync(
            "mcp_token",
            caller.TokenId.ToString(),
            $"{caller.OwnerUsername}:{caller.Name}",
            "mcp.room_create",
            caller.McpRole,
            g.RoomName,
            scope.RemoteIp,
            "ok",
            prefix,
            ct).ConfigureAwait(false);

        return ToolOutcome.Ok(new
        {
            success = true,
            roomName = g.RoomName,
            mode = g.Mode,
            randomDeckMode = g.RandomDeckMode.ToString(),
            noCheckDeck = g.NoCheckDeck,
            spawnedBots
        });
    }

    [McpServerTool(Name = "room_close")]
    [McpRoles(McpRoleCodes.Admin)]
    [Description("强制关闭指定房间。")]
    public ToolOutcome RoomClose(string roomName, string? reason = null)
    {
        if (!deps.ServerHost.Server.TryGetRoom(roomName?.Trim() ?? string.Empty, out Game? g) || g is null)
            return ToolOutcome.Fail(new { message = $"房间 '{roomName}' 不存在。" });
        CloseGameRoom(g);
        return ToolOutcome.Ok(new { success = true, reason });
    }

    [McpServerTool(Name = "room_kick_player")]
    [McpRoles(McpRoleCodes.Admin)]
    [Description("将指定房间的某个席位或观众请出房间。")]
    public ToolOutcome RoomKickPlayer(
        string roomName,
        int? seatIndex = null,
        string? playerName = null)
    {
        if (!deps.ServerHost.Server.TryGetRoom(roomName?.Trim() ?? string.Empty, out Game? g) || g is null)
            return ToolOutcome.Fail(new { message = $"房间 '{roomName}' 不存在。" });

        Player? target = null;
        if (seatIndex is not null)
        {
            int idx = seatIndex.Value;
            if (idx >= 0 && idx < g.Players.Length)
                target = g.Players[idx];
        }
        else if (!string.IsNullOrWhiteSpace(playerName))
        {
            string pName = playerName.Trim();
            target = g.Players.FirstOrDefault(p => p is not null && string.Equals(p.Name, pName, StringComparison.OrdinalIgnoreCase))
                     ?? g.Observers.FirstOrDefault(o => o is not null && string.Equals(o.Name, pName, StringComparison.OrdinalIgnoreCase));
        }

        if (target is null)
            return ToolOutcome.Fail(new { success = false, message = "未找到目标玩家或观众。" });

        g.RemovePlayer(target);
        target.Disconnect();
        return ToolOutcome.Ok(new { success = true });
    }

    [McpServerTool(Name = "room_spawn_windbot")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("向指定大厅房间召唤 1~4 台内置 WindBot 机器人陪练或补齐 2v2 席位（player 角色可向自己所在房间或新建空房召唤）。")]
    public ToolOutcome RoomSpawnWindbot(
        [Description("目标房间名")] string roomName,
        [Description("可选：卡组名（如 BlueEyes、Yubel）或 'random'")] string? deck = null,
        [Description("召唤数量 (1~4，默认 1)")] int count = 1)
    {
        if (deps.SpawnWindBotCallback is null)
            return ToolOutcome.Fail(new { success = false, message = "当前未配置内置 WindBot 服务或尚未加载卡库。" });

        string rName = AgentSessionManager.ExtractTargetRoomName(roomName ?? string.Empty);
        if (!deps.ServerHost.Server.TryGetRoom(rName, out Game? g) || g is null)
            return ToolOutcome.Fail(new { success = false, message = $"房间 '{rName}' 不存在。" });

        ValidatedMcpToken caller = McpCallerScope.Current.Caller;
        if (caller.McpRole != McpRoleCodes.Admin)
        {
            if (caller.AllowedRooms.Count > 0 &&
                !caller.AllowedRooms.Contains(g.RoomName, StringComparer.OrdinalIgnoreCase))
            {
                return ToolOutcome.Fail(new { success = false, message = $"当前 Token 无权操作房间 '{g.RoomName}'。" });
            }

            bool isRoomEmpty = g.Players.All(static p => p is null);
            bool callerInRoom = deps.SessionManager.ListOwnedSessions(caller.TokenId)
                .Any(s => s.IsConnected && string.Equals(s.RoomName, g.RoomName, StringComparison.OrdinalIgnoreCase));
            if (!isRoomEmpty && !callerInRoom)
            {
                return ToolOutcome.Fail(new { success = false, message = "非管理员角色仅可向自己当前所在的房间或刚创建的空房间召唤内置 AI。" });
            }
        }

        try
        {
            int clamped = Math.Clamp(count, 1, 4);
            IReadOnlyList<string> spawned = deps.SpawnWindBotCallback(g, deck, null, clamped);
            return ToolOutcome.Ok(new { success = true, roomName = g.RoomName, spawned });
        }
        catch (Exception ex)
        {
            return ToolOutcome.Fail(new { success = false, message = ex.Message });
        }
    }

    private static void CloseGameRoom(Game game)
    {
        foreach (Player? p in game.Players.ToArray())
            p?.Disconnect();
        foreach (Player? obs in game.Observers.ToArray())
            obs?.Disconnect();
        game.End();
    }
}
