using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WindBot;
using WindBot.Game.AI;
using WindBot.Game.AI.Lua;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Scripting;
using YGOProSharp.AgentBridge;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Server;

namespace YGOProSharp.Mcp;

public sealed class McpRuntimeDependencies
{
    public required YGOProSharpServerHost ServerHost { get; init; }
    public required OperationalStore Store { get; init; }
    public required AgentSessionManager SessionManager { get; init; }
    public required DiyPackageManager DiyManager { get; init; }
    public required Func<INamedCardRepository> NamedCardRepositoryProvider { get; init; }
    public Func<ILuaRuntime>? LuaRuntimeProvider { get; init; }
    public Func<Game, string?, string?, int, IReadOnlyList<string>>? SpawnWindBotCallback { get; init; }
    public Action<string, object>? PublishPublicEventCallback { get; init; }
}

/// <summary>
/// 纯净零外部大模型 SDK 依赖的标准 MCP (Model Context Protocol) JSON-RPC 2.0 协议引擎。
/// 按调用方 Token 的 <c>McpRole</c> 动态裁剪可见 Tool / Resource / Prompt，严格隔离对局战争迷雾与会话归属。
/// </summary>
public sealed class McpServerEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private static readonly string[] SupportedProtocolVersions =
    [
        "2025-06-18",
        "2025-03-26",
        "2024-11-05"
    ];

    private readonly ILogger _logger = AppLog.CreateLogger<McpServerEngine>();
    private readonly McpRuntimeDependencies _deps;
    private readonly IReadOnlyList<McpToolRegistration> _tools;

    public McpServerEngine(McpRuntimeDependencies dependencies)
    {
        _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _tools = BuildToolRegistry();
    }

    public async Task<JsonNode?> ProcessPayloadAsync(
        JsonNode? payload,
        ValidatedMcpToken caller,
        string? remoteIp,
        CancellationToken cancellationToken = default)
    {
        if (payload is JsonArray batch)
        {
            if (batch.Count == 0)
                return BuildJsonRpcError(null, -32600, "Invalid Request: empty batch.");

            JsonArray responses = new();
            foreach (JsonNode? item in batch)
            {
                JsonNode? singleRes = await ProcessSingleMessageAsync(item, caller, remoteIp, cancellationToken).ConfigureAwait(false);
                if (singleRes is not null)
                    responses.Add(singleRes);
            }

            return responses.Count == 0 ? null : responses;
        }

        return await ProcessSingleMessageAsync(payload, caller, remoteIp, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonNode?> ProcessSingleMessageAsync(
        JsonNode? node,
        ValidatedMcpToken caller,
        string? remoteIp,
        CancellationToken cancellationToken)
    {
        if (node is not JsonObject obj)
            return BuildJsonRpcError(null, -32600, "Invalid JSON-RPC message object.");

        bool hasId = obj.TryGetPropertyValue("id", out JsonNode? idNode) && idNode is not null;
        string? method = obj["method"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(method))
        {
            // 客户端发来的通知或响应无需回包
            return hasId ? BuildJsonRpcError(idNode, -32600, "Missing JSON-RPC method.") : null;
        }

        // JSON-RPC Notification（无 id，如 notifications/initialized、notifications/cancelled）返回 null -> HTTP 202
        if (!hasId || method.StartsWith("notifications/", StringComparison.Ordinal))
            return null;

        JsonObject? @params = obj["params"] as JsonObject;

        try
        {
            return method switch
            {
                "initialize" => HandleInitialize(idNode, @params, caller),
                "ping" => BuildJsonRpcResult(idNode, new JsonObject()),
                "tools/list" => HandleToolsList(idNode, caller),
                "tools/call" => await HandleToolsCallAsync(idNode, @params, caller, remoteIp, cancellationToken).ConfigureAwait(false),
                "resources/list" => HandleResourcesList(idNode, caller),
                "resources/read" => HandleResourcesRead(idNode, @params, caller),
                "prompts/list" => HandlePromptsList(idNode, caller),
                "prompts/get" => HandlePromptsGet(idNode, @params, caller),
                _ => BuildJsonRpcError(idNode, -32601, $"Method '{method}' not found.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP method {Method} failed for TokenId={TokenId}.", method, caller.TokenId);
            return BuildJsonRpcError(idNode, -32603, $"Internal MCP error: {ex.Message}");
        }
    }

    private static JsonObject HandleInitialize(JsonNode? idNode, JsonObject? @params, ValidatedMcpToken caller)
    {
        string requestedVer = @params?["protocolVersion"]?.GetValue<string>() ?? "2025-03-26";
        string negotiatedVer = SupportedProtocolVersions.Contains(requestedVer, StringComparer.Ordinal)
            ? requestedVer
            : "2025-03-26";

        string instructions =
            $"已连接 YGOProSharp MCP 服务（当前令牌角色: {caller.McpRole}，名称: {caller.Name}）。" +
            "打牌标准流程：1) 调用 room_list 查看房间；2) 调用 duel_join_room 加入目标房间获得 sessionId；" +
            "3) 非随机房调用 duel_set_deck 设卡组，再调用 duel_set_ready(ready=true, startIfHost=true)；" +
            "4) 循环调用 duel_wait_for_turn 等待轮到我方决策，阅读返回的 observation 与 pendingPrompt.choices，" +
            "遇到陌生卡可用 card_get 查卡效，最后调用 duel_submit_action 提交合法动作下标。";

        JsonObject result = new()
        {
            ["protocolVersion"] = negotiatedVer,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
                ["resources"] = new JsonObject { ["listChanged"] = false },
                ["prompts"] = new JsonObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "ygoprosharp-mcp",
                ["version"] = "0.3.0"
            },
            ["instructions"] = instructions
        };

        return BuildJsonRpcResult(idNode, result);
    }

    private JsonObject HandleToolsList(JsonNode? idNode, ValidatedMcpToken caller)
    {
        JsonArray list = new();
        foreach (McpToolRegistration tool in _tools)
        {
            if (!IsRoleAllowed(caller.McpRole, tool.AllowedRoles))
                continue;

            list.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchemaJson)!
            });
        }

        return BuildJsonRpcResult(idNode, new JsonObject { ["tools"] = list });
    }

    private async Task<JsonObject> HandleToolsCallAsync(
        JsonNode? idNode,
        JsonObject? @params,
        ValidatedMcpToken caller,
        string? remoteIp,
        CancellationToken ct)
    {
        string? toolName = @params?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(toolName))
            return BuildJsonRpcError(idNode, -32602, "Missing tool 'name' in tools/call.");

        McpToolRegistration? tool = _tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));
        if (tool is null || !IsRoleAllowed(caller.McpRole, tool.AllowedRoles))
        {
            await _deps.Store.AppendAuditAsync(
                "mcp_token",
                caller.TokenId.ToString(),
                $"{caller.OwnerUsername}:{caller.Name}",
                $"mcp.tool.{toolName}",
                caller.McpRole,
                null,
                remoteIp,
                "permission_denied",
                null,
                ct).ConfigureAwait(false);

            return BuildToolCallResponse(idNode, isError: true, new
            {
                error = "permission_denied",
                message = $"当前 MCP Token 角色 '{caller.McpRole}' 无权调用工具 '{toolName}'。"
            });
        }

        JsonObject args = @params?["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            (bool isError, object payload) = await tool.Handler(args, caller, remoteIp, ct).ConfigureAwait(false);
            return BuildToolCallResponse(idNode, isError, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {ToolName} threw an exception.", toolName);
            return BuildToolCallResponse(idNode, isError: true, new { error = "tool_execution_failed", message = ex.Message });
        }
    }

    private static JsonObject HandleResourcesList(JsonNode? idNode, ValidatedMcpToken caller)
    {
        JsonArray resources = new()
        {
            new JsonObject
            {
                ["uri"] = "ygopro://guide/duelist-protocol",
                ["name"] = "YGOProSharp 外部 AI 决斗操作指南",
                ["description"] = "1v1 / 2v2 双打、盲盒随机房与合法动作编号提交流程说明",
                ["mimeType"] = "text/markdown"
            },
            new JsonObject
            {
                ["uri"] = "ygopro://rooms/public",
                ["name"] = "公开活动房间实时快照",
                ["description"] = "当前服务器上所有活动房间与席位概览（不含暗牌与玩家 IP）",
                ["mimeType"] = "application/json"
            }
        };

        if (caller.McpRole is McpRoleCodes.DiyCreator or McpRoleCodes.Admin)
        {
            resources.Add(new JsonObject
            {
                ["uri"] = "ygopro://guide/windbot-lua-dsl",
                ["name"] = "WindBot xmake 风格 Lua AI 四层决策规范",
                ["description"] = "deck(...) 作用域、0-based 列表、EmitThought 与 AskExternalAgent 双脑接口说明",
                ["mimeType"] = "text/markdown"
            });
        }

        return BuildJsonRpcResult(idNode, new JsonObject { ["resources"] = resources });
    }

    private JsonObject HandleResourcesRead(JsonNode? idNode, JsonObject? @params, ValidatedMcpToken caller)
    {
        string? uri = @params?["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri))
            return BuildJsonRpcError(idNode, -32602, "Missing resource 'uri'.");

        string mime = "text/markdown";
        string text;

        switch (uri)
        {
            case "ygopro://guide/duelist-protocol":
                text = """
                    # YGOProSharp 外部 AI 决斗指南
                    1. `room_list` 查看当前房间与规则（如 `RD` 盲盒预组、`RDM` 镜像同卡组、`CHAOS` 混沌随机、`2V2` 双打 16000LP）。
                    2. `duel_join_room` 传入 `roomName`（如 `"Room01"` 或带规则前缀 `"RD#Room01"`）加入房间，保存返回的 `sessionId`。
                    3. 若房间为自备卡组房，调用 `duel_set_deck`（支持 `randomMode: "preset" | "chaos" | "diy"` 或指定预设名）；若是强制随机房（`RD`/`RDM`/`CHAOS`）可直接跳过组卡。
                    4. 调用 `duel_set_ready`（`ready: true, startIfHost: true`）准备就绪。
                    5. 循环调用 `duel_wait_for_turn(sessionId)` 阻塞等待轮到自己操作。
                    6. 每次唤醒后阅读 `pendingPrompt.choices`：
                       - 单选动作传 `actionIndex`；
                       - 多张选卡/同调凑星（`supportsMultiSelect = true`）传 `cardIndices: [0, 2]`；
                       - 可附带 `thoughtSummary`（展示在观战界面思考气泡，请勿泄露手牌机密）与 `chatMessage`（与对手/2v2队友互动）。
                    7. 2v2 双打注意：你与队友共享场上怪兽/魔陷与墓地，但严禁也无法看到队友未公开的手牌；当 `teammateSurrenderPending = true` 时代表队友请求投降，若同意可调用 `duel_surrender`。
                    """;
                break;

            case "ygopro://rooms/public":
                mime = "application/json";
                text = JsonSerializer.Serialize(_deps.ServerHost.GetRooms(), PrettyJsonOpts);
                break;

            case "ygopro://guide/windbot-lua-dsl" when caller.McpRole is McpRoleCodes.DiyCreator or McpRoleCodes.Admin:
                text = """
                    # WindBot Lua AI 编写速查
                    - 使用 `deck("MyDeck")` ... `deck_end()` 声明卡组执行器，`set_deckfile("MyDeck")` 绑定 `.ydk`。
                    - 注册规则：`on_summon(cardId, "FuncName")`、`on_spsummon(cardId, "FuncName")`、`on_activate(cardId, "FuncName")`、`add_default("Repos", -1, "DefaultMonsterRepos")`。
                    - 注意：`Bot.Hand`、`Cards` 等 CLR 列表在 Lua 中为 **0-based 下标**（`0 .. Count - 1`）。
                    - 可调用 `emit_thought("先攻展开：检索核心本家")` 输出思考轨迹，或在关键节点调用 `ask_external_agent("是否交灰流丽", {"交康", "放行"})`。
                    """;
                break;

            default:
                return BuildJsonRpcError(idNode, -32602, $"Resource '{uri}' not found or unauthorized.");
        }

        return BuildJsonRpcResult(idNode, new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = mime,
                    ["text"] = text
                }
            }
        });
    }

    private static JsonObject HandlePromptsList(JsonNode? idNode, ValidatedMcpToken caller)
    {
        JsonArray prompts = new();
        if (caller.McpRole is McpRoleCodes.Player or McpRoleCodes.Admin)
        {
            prompts.Add(new JsonObject
            {
                ["name"] = "play-duel-room",
                ["description"] = "引导 AI 进入指定房间并按规则完成整局 1v1 或 2v2 决斗",
                ["arguments"] = new JsonArray
                {
                    new JsonObject { ["name"] = "room", ["description"] = "房间名称或主机密码（如 Room01 或 RD#Room01）", ["required"] = true },
                    new JsonObject { ["name"] = "playerName", ["description"] = "AI 游戏内昵称", ["required"] = false }
                }
            });
        }

        if (caller.McpRole is McpRoleCodes.DiyCreator or McpRoleCodes.Admin)
        {
            prompts.Add(new JsonObject
            {
                ["name"] = "author-windbot-lua",
                ["description"] = "读取卡组效文、编写 WindBot Lua 四层决策脚本并通过闪电自博弈验证",
                ["arguments"] = new JsonArray
                {
                    new JsonObject { ["name"] = "deckName", ["description"] = "目标卡组名", ["required"] = true }
                }
            });
        }

        return BuildJsonRpcResult(idNode, new JsonObject { ["prompts"] = prompts });
    }

    private static JsonObject HandlePromptsGet(JsonNode? idNode, JsonObject? @params, ValidatedMcpToken caller)
    {
        string? name = @params?["name"]?.GetValue<string>();
        JsonObject? args = @params?["arguments"] as JsonObject;

        if (name == "play-duel-room" && caller.McpRole is McpRoleCodes.Player or McpRoleCodes.Admin)
        {
            string room = args?["room"]?.GetValue<string>() ?? "Room01";
            string pName = args?["playerName"]?.GetValue<string>() ?? caller.Name;
            return BuildJsonRpcResult(idNode, new JsonObject
            {
                ["description"] = $"加入房间 {room} 进行游戏王对决",
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = $"请以昵称 '{pName}' 调用 duel_join_room 加入房间 '{room}'。若非强制随机房则使用 duel_set_deck(randomMode='preset') 领一套卡组，然后 duel_set_ready 准备开局，并通过 duel_wait_for_turn 与 duel_submit_action 认真打完本场对决。"
                        }
                    }
                }
            });
        }

        if (name == "author-windbot-lua" && caller.McpRole is McpRoleCodes.DiyCreator or McpRoleCodes.Admin)
        {
            string deckName = args?["deckName"]?.GetValue<string>() ?? "ExampleBlueEyes";
            return BuildJsonRpcResult(idNode, new JsonObject
            {
                ["description"] = $"为卡组 {deckName} 编写并测试 WindBot Lua 脚本",
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = $"请先调用 deck_inspect 分析 '{deckName}' 的全部卡片效果，阅读 ygopro://guide/windbot-lua-dsl，调用 windbot_write_deck_script 编写脚本，最后调用 windbot_simulate_match 跑对局复盘。"
                        }
                    }
                }
            });
        }

        return BuildJsonRpcError(idNode, -32602, $"Prompt '{name}' not found or unauthorized.");
    }

    // ==================== 工具注册与实现 ====================

    private IReadOnlyList<McpToolRegistration> BuildToolRegistry()
    {
        string[] allRoles = [McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin];
        string[] playerRoles = [McpRoleCodes.Player, McpRoleCodes.Admin];
        string[] creatorRoles = [McpRoleCodes.DiyCreator, McpRoleCodes.Admin];
        string[] adminOnly = [McpRoleCodes.Admin];

        return
        [
            // ---------- 1. 公共与观战查询工具 ----------
            new(
                "room_list",
                "列出当前服务器上的所有公开活动房间（含赛制 Mode、大师规则、随机卡组模式、决斗席与观战人数）。",
                allRoles,
                """{"type":"object","properties":{}}""",
                (args, caller, _, _) =>
                {
                    var rooms = _deps.ServerHost.GetRooms().Select(r => new
                    {
                        r.RoomName,
                        state = r.State.ToString(),
                        r.Mode,
                        isTag2v2 = r.Mode == 2,
                        r.Region,
                        r.MasterRule,
                        r.StartLp,
                        randomDeckMode = r.RandomDeckMode.ToString(),
                        r.IsSuddenDeath,
                        r.DuelistCount,
                        r.MaxDuelists,
                        r.ObserverCount,
                        r.HostPlayerName,
                        r.PlayerNames,
                        r.TurnCount,
                        r.Team0Lp,
                        r.Team1Lp
                    }).ToArray();

                    return Task.FromResult<(bool, object)>((false, new { count = rooms.Length, rooms }));
                }),

            new(
                "room_get_public_state",
                "以观战者公开视角读取指定房间的双方 LP、回合数、决斗席与观众列表（严格隐藏所有未公开手牌、盖卡与玩家 IP）。",
                allRoles,
                """{"type":"object","properties":{"roomName":{"type":"string","description":"目标房间名"}},"required":["roomName"]}""",
                (args, _, _, _) =>
                {
                    string roomName = GetString(args, "roomName");
                    if (!_deps.ServerHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                        return Task.FromResult<(bool, object)>((true, new { message = $"房间 '{roomName}' 不存在或已结束。" }));

                    RoomSnapshot snap = game.CreateSnapshot();
                    string[] observers = game.Observers.Where(static o => o is not null).Select(static o => o.Name ?? "Observer").ToArray();
                    return Task.FromResult<(bool, object)>((false, new
                    {
                        snap.RoomName,
                        state = snap.State.ToString(),
                        snap.Mode,
                        snap.MasterRule,
                        randomDeckMode = snap.RandomDeckMode.ToString(),
                        snap.IsSuddenDeath,
                        snap.TurnCount,
                        snap.Team0Lp,
                        snap.Team1Lp,
                        slots = game.Players.Select((p, idx) => new
                        {
                            seatIndex = idx,
                            teamId = game.IsTag ? (idx >= 2 ? 1 : 0) : idx,
                            playerName = p?.Name,
                            isReady = idx < game.IsReady.Length && game.IsReady[idx],
                            seatOrigin = p?.SeatOrigin ?? "empty"
                        }),
                        observers
                    }));
                }),

            new(
                "card_get",
                "按卡片密码（卡密 ID）查询卡片的完整名称、属性、种族、星级、攻守数值与完整效果文本（支持本服 DIY 卡）。",
                allRoles,
                """{"type":"object","properties":{"cardCode":{"type":"integer","description":"卡片正整数密码 ID"}},"required":["cardCode"]}""",
                (args, _, _, _) =>
                {
                    int code = GetInt(args, "cardCode", 0);
                    INamedCardRepository namedRepo = _deps.NamedCardRepositoryProvider();
                    if (code > 0 && namedRepo.TryGetCard(code, out NamedCard? nc) && nc is not null)
                    {
                        return Task.FromResult<(bool, object)>((false, new
                        {
                            nc.Id,
                            nc.Name,
                            nc.Description,
                            nc.Type,
                            level = nc.Level & 0xff,
                            nc.LScale,
                            nc.RScale,
                            nc.Attack,
                            nc.Defense,
                            nc.Attribute,
                            nc.Race,
                            nc.Ot,
                            isDiy = (nc.Ot & 0x4) != 0
                        }));
                    }

                    if (code > 0 && _deps.ServerHost.Server.CardRepository.TryGetCard(code, out Card rawCard))
                    {
                        return Task.FromResult<(bool, object)>((false, new
                        {
                            rawCard.Id,
                            name = $"#{rawCard.Id}",
                            description = "",
                            rawCard.Type,
                            level = rawCard.Level & 0xff,
                            rawCard.Attack,
                            rawCard.Defense,
                            rawCard.Attribute,
                            rawCard.Race,
                            rawCard.Ot
                        }));
                    }

                    return Task.FromResult<(bool, object)>((true, new { message = $"未找到卡密 #{code}。" }));
                }),

            new(
                "card_search",
                "按关键词搜索卡片名称或效果文本（最多返回 25 条）。",
                allRoles,
                """{"type":"object","properties":{"query":{"type":"string","description":"卡名或效果关键词"},"diyOnly":{"type":"boolean"},"limit":{"type":"integer"}}}""",
                (args, _, _, _) =>
                {
                    string q = GetString(args, "query");
                    bool diyOnly = GetBool(args, "diyOnly", false);
                    int limit = Math.Clamp(GetInt(args, "limit", 15), 1, 25);
                    INamedCardRepository repo = _deps.NamedCardRepositoryProvider();
                    IEnumerable<NamedCard> src = repo.GetAllCards();
                    if (diyOnly)
                        src = src.Where(static c => (c.Ot & 0x4) != 0);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        src = src.Where(c =>
                            c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            c.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
                    }

                    var items = src.Take(limit).Select(static c => new
                    {
                        c.Id,
                        c.Name,
                        level = c.Level & 0xff,
                        c.Attack,
                        c.Defense,
                        c.Type,
                        isDiy = (c.Ot & 0x4) != 0,
                        c.Description
                    }).ToArray();
                    return Task.FromResult<(bool, object)>((false, new { count = items.Length, cards = items }));
                }),

            new(
                "banlist_list",
                "查看当前服务器加载的禁限卡表列表。",
                allRoles,
                """{"type":"object","properties":{}}""",
                (_, _, _, _) =>
                {
                    var list = BanlistManager.Banlists.Select((b, idx) => new
                    {
                        index = idx,
                        b.Name,
                        b.WhitelistOnly,
                        bannedCount = b.BannedIds.Count,
                        limitedCount = b.LimitedIds.Count,
                        semiLimitedCount = b.SemiLimitedIds.Count
                    }).ToArray();
                    return Task.FromResult<(bool, object)>((false, new { banlists = list }));
                }),

            // ---------- 2. 决斗玩家工具 (player / admin) ----------
            new(
                "duel_list_preset_decks",
                "列出服务端预设随机卡组池（.ydk）与可用卡组名称。",
                playerRoles,
                """{"type":"object","properties":{}}""",
                (_, _, _, _) =>
                {
                    var presets = _deps.ServerHost.Server.PresetDecks.Entries.Select(static d => new
                    {
                        d.Id,
                        d.Name,
                        d.HasDiyCards,
                        tags = d.Data.Tags,
                        mainCount = d.Data.Main.Count,
                        extraCount = d.Data.Extra.Count
                    }).ToArray();
                    return Task.FromResult<(bool, object)>((false, new { presetDecks = presets }));
                }),

            new(
                "duel_join_room",
                "以玩家（或观战者）身份加入指定房间（支持规则前缀如 'RD#Box01' 或 '2V2,RDM#Tag01'），返回绑定的专属 sessionId 与当前局面。",
                playerRoles,
                """{"type":"object","properties":{"room":{"type":"string","description":"房间名或完整主机密码，如 Room01 或 RD#Room01"},"playerName":{"type":"string","description":"游戏内显示的 AI 昵称"},"asObserver":{"type":"boolean","description":"是否作为观战者进入"}},"required":["room"]}""",
                async (args, caller, remoteIp, ct) =>
                {
                    string roomSpec = GetString(args, "room");
                    string playerName = GetString(args, "playerName");
                    if (string.IsNullOrWhiteSpace(playerName))
                        playerName = caller.Name;
                    bool asObs = GetBool(args, "asObserver", false);

                    (bool ok, string msg, AgentDuelSession? session) = await _deps.SessionManager.CreateAndJoinSessionAsync(
                        caller.TokenId,
                        caller.AllowedRooms,
                        caller.MaxSessions,
                        playerName,
                        roomSpec,
                        asObs,
                        ct).ConfigureAwait(false);

                    if (!ok || session is null)
                        return (true, new { success = false, message = msg });

                    // 标记服务端席位来源为 mcp_ai，方便网页大厅与观众识别外部 AI 选手
                    if (_deps.ServerHost.Server.TryGetRoom(session.RoomName, out Game? game) && game is not null)
                    {
                        foreach (Player? p in game.Players)
                        {
                            if (p is not null && string.Equals(p.Name, session.PlayerName, StringComparison.Ordinal))
                                p.SeatOrigin = "mcp_ai";
                        }
                        foreach (Player? obs in game.Observers)
                        {
                            if (obs is not null && string.Equals(obs.Name, session.PlayerName, StringComparison.Ordinal))
                                obs.SeatOrigin = "mcp_ai";
                        }
                    }

                    await _deps.Store.AppendAuditAsync(
                        "mcp_token",
                        caller.TokenId.ToString(),
                        $"{caller.OwnerUsername}:{caller.Name}",
                        "mcp.duel_join_room",
                        caller.McpRole,
                        session.RoomName,
                        remoteIp,
                        "ok",
                        session.SessionId,
                        ct).ConfigureAwait(false);

                    return (false, new
                    {
                        success = true,
                        message = msg,
                        sessionId = session.SessionId,
                        observation = session.GetObservation()
                    });
                }),

            new(
                "duel_list_sessions",
                "列出当前 AI Token 名下正在运行的对局会话列表。",
                playerRoles,
                """{"type":"object","properties":{}}""",
                (_, caller, _, _) =>
                {
                    var list = _deps.SessionManager.ListOwnedSessions(caller.TokenId);
                    return Task.FromResult<(bool, object)>((false, new { sessions = list }));
                }),

            new(
                "duel_swap_seat",
                "在大厅未准备阶段切换决斗席位/更换 2v2 队伍，或切换到观战席。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"toObserver":{"type":"boolean"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    (bool swapped, string msg) = s.SwapSeat(GetBool(args, "toObserver", false));
                    return Task.FromResult<(bool, object)>((!swapped, new { success = swapped, message = msg, observation = s.GetObservation() }));
                }),

            new(
                "duel_set_deck",
                "在大厅或换副阶段提交卡组：支持 randomMode ('preset'|'chaos'|'diy') 自动生成合法卡组，或传 presetName / ydkContent / main+extra 数组。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"randomMode":{"type":"string","enum":["preset","chaos","diy"]},"presetName":{"type":"string"},"ydkContent":{"type":"string"},"main":{"type":"array","items":{"type":"integer"}},"extra":{"type":"array","items":{"type":"integer"}},"side":{"type":"array","items":{"type":"integer"}}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    YdkDeckData? deckData = null;
                    string presetName = GetString(args, "presetName");
                    string ydkText = GetString(args, "ydkContent");
                    string randomMode = GetString(args, "randomMode");

                    if (!string.IsNullOrWhiteSpace(ydkText))
                    {
                        deckData = YdkSerializer.Parse(ydkText, "McpCustom");
                    }
                    else if (!string.IsNullOrWhiteSpace(presetName))
                    {
                        PresetDeckEntry? found = _deps.ServerHost.Server.PresetDecks.Entries
                            .FirstOrDefault(d => string.Equals(d.Name, presetName, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Id, presetName, StringComparison.OrdinalIgnoreCase));
                        if (found is null)
                            return Task.FromResult<(bool, object)>((true, new { message = $"未找到预设卡组 '{presetName}'。" }));
                        deckData = found.Data;
                    }
                    else if (args["main"] is JsonArray mainArr && mainArr.Count > 0)
                    {
                        int[] mainIds = ReadIntArray(mainArr);
                        int[] extraIds = ReadIntArray(args["extra"] as JsonArray);
                        int[] sideIds = ReadIntArray(args["side"] as JsonArray);
                        deckData = new YdkDeckData("McpArrayDeck", mainIds, extraIds, sideIds, []);
                    }
                    else
                    {
                        // 默认按 randomMode 生成合法卡组（优先 preset，无预组时自动回退 chaos）
                        bool wantDiy = string.Equals(randomMode, "diy", StringComparison.OrdinalIgnoreCase);
                        if (string.IsNullOrWhiteSpace(randomMode) || string.Equals(randomMode, "preset", StringComparison.OrdinalIgnoreCase))
                        {
                            var candidates = _deps.ServerHost.Server.PresetDecks.Entries
                                .Where(e => !wantDiy || e.HasDiyCards)
                                .ToList();
                            if (candidates.Count > 0)
                                deckData = candidates[Random.Shared.Next(candidates.Count)].Data;
                        }

                        if (deckData is null)
                        {
                            RandomDeckGenerator generator = new(_deps.ServerHost.Server.CardRepository);
                            Deck generated = generator.GenerateChaosDeck(
                                banlist: null,
                                ocg: true,
                                tcg: true,
                                allowCustom: true,
                                options: new ChaosDraftOptions
                                {
                                    MainDeckSize = 40,
                                    ExtraDeckSize = 15,
                                    BalancedRatio = true,
                                    PreferDiyCards = wantDiy
                                },
                                random: Random.Shared);
                            deckData = new YdkDeckData("McpChaosDeck", generated.Main.ToArray(), generated.Extra.ToArray(), generated.Side.ToArray(), []);
                        }
                    }

                    (bool subOk, string subMsg) = s.SubmitDeck(deckData);
                    return Task.FromResult<(bool, object)>((!subOk, new
                    {
                        success = subOk,
                        message = subMsg,
                        mainCount = deckData.Main.Count,
                        extraCount = deckData.Extra.Count,
                        observation = s.GetObservation()
                    }));
                }),

            new(
                "duel_set_ready",
                "在大厅准备或取消准备；若自己是房主且传入 startIfHost=true，会同时尝试开始对局。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"ready":{"type":"boolean"},"startIfHost":{"type":"boolean"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    bool ready = GetBool(args, "ready", true);
                    bool startIfHost = GetBool(args, "startIfHost", true);
                    (bool rOk, string rMsg) = s.SetReady(ready, startIfHost);
                    return Task.FromResult<(bool, object)>((!rOk, new { success = rOk, message = rMsg, observation = s.GetObservation() }));
                }),

            new(
                "duel_start",
                "当自己是房主且全员已准备就绪时，主动发起开局。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    (bool stOk, string stMsg) = s.RequestStartDuel();
                    return Task.FromResult<(bool, object)>((!stOk, new { success = stOk, message = stMsg, observation = s.GetObservation() }));
                }),

            new(
                "duel_wait_for_turn",
                "长轮询等待直到轮到当前会话做出决策（猜拳、选先后攻、出牌、连锁、选卡、2v2队友投降征询）或对局结束，直接返回己方视角局面与编号化合法动作。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"timeoutSeconds":{"type":"integer","description":"最长等待秒数 (1~28，默认 20)"}},"required":["sessionId"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    int sec = Math.Clamp(GetInt(args, "timeoutSeconds", 20), 1, 28);
                    AgentObservation obs = await s.WaitForTurnAsync(TimeSpan.FromSeconds(sec), ct).ConfigureAwait(false);
                    return (false, obs);
                }),

            new(
                "duel_get_observation",
                "立即读取严格己方战争迷雾视角的结构化局面（双方 LP、回合阶段、己方手牌与共享场面、墓地除外、连锁与最近公开事件）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    return Task.FromResult<(bool, object)>((false, s.GetObservation()));
                }),

            new(
                "duel_get_legal_actions",
                "读取当前等待响应的 promptId 与编号化合法动作列表。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    AgentPendingPrompt? prompt = s.GetPendingPrompt();
                    return Task.FromResult<(bool, object)>((false, new { hasPendingAction = prompt is not null, pendingPrompt = prompt }));
                }),

            new(
                "duel_submit_action",
                "提交所选合法动作：单选传 actionIndex，多张选卡/凑星传 cardIndices 数组；可选附带 thoughtSummary（观战思考气泡）与 chatMessage（房间聊天）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"actionIndex":{"type":"integer","description":"单选合法动作编号"},"cardIndices":{"type":"array","items":{"type":"integer"},"description":"多张选卡或凑星时的候选下标数组"},"promptId":{"type":"integer","description":"可选：校验当前询问编号防止重复提交"},"thoughtSummary":{"type":"string","description":"可选：公开展示给观众的简短思考理由（勿写暗手牌机密）"},"chatMessage":{"type":"string","description":"可选：顺带发送到房间聊天框的台词"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    int? actionIndex = args["actionIndex"] is not null ? GetInt(args, "actionIndex", 0) : null;
                    int[]? cardIndices = args["cardIndices"] is JsonArray arr ? ReadIntArray(arr) : null;
                    long? promptId = args["promptId"] is not null ? args["promptId"]!.GetValue<long>() : null;
                    string? thought = args["thoughtSummary"]?.GetValue<string>();
                    string? chat = args["chatMessage"]?.GetValue<string>();

                    AgentActionResult res = s.SubmitAction(actionIndex, cardIndices, promptId, thought, chat);
                    return Task.FromResult<(bool, object)>((!res.Success, res));
                }),

            new(
                "duel_chat",
                "在当前所在对局房间发送聊天消息（支持与人类对手或 2v2 队友沟通）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"message":{"type":"string"}},"required":["sessionId","message"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    (bool sent, string msg) = s.SendChat(GetString(args, "message"));
                    return Task.FromResult<(bool, object)>((!sent, new { success = sent, message = msg }));
                }),

            new(
                "duel_surrender",
                "主动投降（2v2 双打下首次调用向队友发送投降确认征询，或确认队友的投降请求）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    (bool surOk, string msg) = s.Surrender();
                    return Task.FromResult<(bool, object)>((!surOk, new { success = surOk, message = msg }));
                }),

            new(
                "duel_leave",
                "离开房间并关闭释放该 sessionId。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    string sid = GetString(args, "sessionId");
                    bool closed = _deps.SessionManager.CloseOwnedSession(caller.TokenId, sid);
                    return Task.FromResult<(bool, object)>((!closed, new { success = closed, message = closed ? "已离开房间并释放会话。" : "未找到归属当前 Token 的该会话。" }));
                }),

            // ---------- 3. DIY 与 WindBot Lua 创作工具 (diy_creator / admin) ----------
            new(
                "diy_list_packages",
                "列出已安装的受管 DIY 扩展包清单与状态。",
                creatorRoles,
                """{"type":"object","properties":{}}""",
                (_, _, _, _) =>
                {
                    var pkgs = _deps.DiyManager.ListPackages(enabledOnly: false);
                    return Task.FromResult<(bool, object)>((false, new { packages = pkgs }));
                }),

            new(
                "diy_test_card_sandbox",
                "在进程内 ocgcore 原生引擎沙盒中实测单张 DIY 卡的 Lua 脚本（检验 initial_effect 注册与发动/处理无报错）。",
                creatorRoles,
                """{"type":"object","properties":{"cardId":{"type":"integer"},"name":{"type":"string"},"type":{"type":"integer"},"level":{"type":"integer"},"attack":{"type":"integer"},"defense":{"type":"integer"},"race":{"type":"integer"},"attribute":{"type":"integer"},"luaScript":{"type":"string"},"testActivation":{"type":"boolean"}},"required":["cardId","luaScript"]}""",
                (args, _, _, _) =>
                {
                    int cardId = GetInt(args, "cardId", 99000001);
                    string lua = GetString(args, "luaScript");
                    LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(lua);
                    if (!audit.IsSafe)
                        return Task.FromResult<(bool, object)>((true, new { message = "Lua 脚本未通过安全静态审计：" + string.Join("; ", audit.Violations) }));

                    DiyCardEntry entry = new()
                    {
                        Id = cardId,
                        Ot = 4,
                        Alias = 0,
                        Setcode = 0,
                        Type = GetInt(args, "type", 0x21),
                        Attack = GetInt(args, "attack", 1800),
                        Defense = GetInt(args, "defense", 1000),
                        Level = GetInt(args, "level", 4),
                        Race = GetInt(args, "race", 1),
                        Attribute = GetInt(args, "attribute", 1),
                        Category = 0,
                        Name = GetString(args, "name") is { Length: > 0 } n ? n : $"DIY_{cardId}",
                        Desc = "Sandbox test card",
                        Strings = []
                    };

                    CardSandboxResult res = _deps.ServerHost.Server.RunCardSandboxTest(new CardSandboxRequest(entry, lua, GetBool(args, "testActivation", true)));
                    return Task.FromResult<(bool, object)>((!res.Success, res));
                }),

            new(
                "diy_upsert_card",
                "向 DIY 包提交新卡或修改卡片（若 Token 开启了审批门禁则自动创建待审 Proposal，否则直接写入包）。",
                creatorRoles,
                """{"type":"object","properties":{"packageId":{"type":"string"},"cardId":{"type":"integer"},"name":{"type":"string"},"desc":{"type":"string"},"type":{"type":"integer"},"level":{"type":"integer"},"attack":{"type":"integer"},"defense":{"type":"integer"},"race":{"type":"integer"},"attribute":{"type":"integer"},"luaScript":{"type":"string"}},"required":["packageId","cardId","name","desc"]}""",
                async (args, caller, remoteIp, ct) =>
                {
                    string pkgId = GetString(args, "packageId");
                    if (!DiyPackageManager.IsValidPackageId(pkgId))
                        return (true, new { message = "非法 DIY 包 ID。" });

                    int cardId = GetInt(args, "cardId", 0);
                    if (cardId <= 0)
                        return (true, new { message = "cardId 必须为正整数。" });

                    string? lua = args["luaScript"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(lua))
                    {
                        LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(lua);
                        if (!audit.IsSafe)
                            return (true, new { message = "Lua 安全预检未通过：" + string.Join("; ", audit.Violations) });
                    }

                    DiyCardEntry entry = new()
                    {
                        Id = cardId,
                        Ot = 4,
                        Alias = 0,
                        Setcode = 0,
                        Type = GetInt(args, "type", 0x21),
                        Attack = GetInt(args, "attack", 0),
                        Defense = GetInt(args, "defense", 0),
                        Level = GetInt(args, "level", 4),
                        Race = GetInt(args, "race", 1),
                        Attribute = GetInt(args, "attribute", 1),
                        Category = 0,
                        Name = GetString(args, "name"),
                        Desc = GetString(args, "desc"),
                        Strings = []
                    };

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
                                    luaScript = lua
                                }
                            }
                        }, JsonOpts);

                        DiyProposalEntity prop = await _deps.Store.CreateDiyProposalAsync(
                            pkgId,
                            $"[MCP:{caller.Name}] {entry.Name} (#{entry.Id})",
                            diffJson,
                            caller.TokenId,
                            caller.Name,
                            "mcp_token",
                            caller.TokenId.ToString(),
                            remoteIp,
                            ct).ConfigureAwait(false);

                        return (false, new { mode = "proposal_created", proposalId = prop.Id, status = prop.Status });
                    }

                    _deps.DiyManager.UpsertManifest(new DiyPackageManifest
                    {
                        Id = pkgId,
                        Name = pkgId,
                        Enabled = true,
                        Priority = 100
                    });
                    _deps.DiyManager.UpsertCard(pkgId, entry, lua);
                    return (false, new { mode = "applied_directly", packageId = pkgId, cardId = entry.Id });
                }),

            new(
                "deck_inspect",
                "解析指定预设卡组或 .ydk 文本，返回卡组内每张卡的卡名、数量与效果文本，便于分析展开链和编写 AI。",
                creatorRoles,
                """{"type":"object","properties":{"presetName":{"type":"string"},"ydkContent":{"type":"string"}}}""",
                (args, _, _, _) =>
                {
                    string presetName = GetString(args, "presetName");
                    string ydk = GetString(args, "ydkContent");
                    YdkDeckData? data = null;

                    if (!string.IsNullOrWhiteSpace(ydk))
                        data = YdkSerializer.Parse(ydk, "Inspect");
                    else if (!string.IsNullOrWhiteSpace(presetName))
                        data = _deps.ServerHost.Server.PresetDecks.Entries.FirstOrDefault(d => string.Equals(d.Name, presetName, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Id, presetName, StringComparison.OrdinalIgnoreCase))?.Data;

                    if (data is null)
                        return Task.FromResult<(bool, object)>((true, new { message = "请提供有效的 presetName 或 ydkContent。" }));

                    INamedCardRepository repo = _deps.NamedCardRepositoryProvider();
                    object Describe(IReadOnlyList<int> ids) => ids.GroupBy(static x => x).Select(g =>
                    {
                        repo.TryGetCard(g.Key, out NamedCard? nc);
                        return new { id = g.Key, copies = g.Count(), name = nc?.Name ?? $"#{g.Key}", type = nc?.Type, attack = nc?.Attack, defense = nc?.Defense, desc = nc?.Description };
                    }).ToArray();

                    return Task.FromResult<(bool, object)>((false, new
                    {
                        data.Name,
                        main = Describe(data.Main),
                        extra = Describe(data.Extra),
                        side = Describe(data.Side)
                    }));
                }),

            new(
                "windbot_list_lua_scripts",
                "列出已注册的内置 WindBot 卡组与自定义 Lua 卡组执行器列表。",
                creatorRoles,
                """{"type":"object","properties":{}}""",
                (_, _, _, _) =>
                {
                    string luaDir = Path.Combine(Path.GetFullPath(_deps.ServerHost.Server.Options.RootPath), "windbot", "Decks", "Lua");
                    string[] customLua = Directory.Exists(luaDir)
                        ? Directory.EnumerateFiles(luaDir, "*.lua").Select(Path.GetFileNameWithoutExtension).Where(static x => x is not null).Cast<string>().ToArray()
                        : [];

                    return Task.FromResult<(bool, object)>((false, new
                    {
                        builtInDecks = DecksManager.GetRegisteredDeckNames(),
                        customLuaScripts = customLua
                    }));
                }),

            new(
                "windbot_write_deck_script",
                "对 WindBot Lua 卡组脚本执行静态安全审计与 NLua 沙箱干跑预检，通过后保存到受管 windbot/Decks/Lua 目录。",
                creatorRoles,
                """{"type":"object","properties":{"scriptName":{"type":"string","description":"不含扩展名的脚本名，如 MyHeroAI"},"luaSource":{"type":"string","description":"xmake 风格 WindBot Lua 源码"}},"required":["scriptName","luaSource"]}""",
                (args, caller, remoteIp, ct) =>
                {
                    string name = GetString(args, "scriptName");
                    string luaSource = GetString(args, "luaSource");
                    if (string.IsNullOrWhiteSpace(name) || !DiyPackageManager.IsValidPackageId(name))
                        return Task.FromResult<(bool, object)>((true, new { message = "scriptName 仅允许字母、数字、下划线或连字符。" }));

                    LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(luaSource);
                    if (!audit.IsSafe)
                        return Task.FromResult<(bool, object)>((true, new { message = "Lua 安全审计拒绝：" + string.Join("; ", audit.Violations) }));

                    // 若宿主注入了硬化 NLuaRuntime，进行一次干跑语法与 DSL 校验
                    ILuaRuntime? runtime = _deps.LuaRuntimeProvider?.Invoke();
                    if (runtime is not null)
                    {
                        WindBot.Game.Duel dummyDuel = new();
                        WindBot.Game.GameClient dummyClient = new(new WindBotInfo { Name = "DryRun" }, _deps.NamedCardRepositoryProvider());
                        WindBot.Game.GameAI dummyAi = new(dummyClient, dummyDuel);
                        using LuaDeckExecutor dryExec = new(dummyAi, dummyDuel, runtime, luaSource, $"{name}.lua", throwOnScriptError: true);
                    }

                    string luaDir = Path.Combine(Path.GetFullPath(_deps.ServerHost.Server.Options.RootPath), "windbot", "Decks", "Lua");
                    Directory.CreateDirectory(luaDir);
                    string targetPath = Path.Combine(luaDir, $"{name}.lua");
                    File.WriteAllText(targetPath, luaSource);

                    _ = _deps.Store.AppendAuditAsync("mcp_token", caller.TokenId.ToString(), caller.Name, "mcp.windbot_write_lua", caller.McpRole, name, remoteIp, "ok", null, ct);
                    return Task.FromResult<(bool, object)>((false, new { success = true, scriptName = name, savedPath = $"windbot/Decks/Lua/{name}.lua" }));
                }),

            new(
                "windbot_simulate_match",
                "在服务端纯内存通道内极速运行 1~3 局内置 WindBot 对战复盘，返回胜负、回合数、双方最终 LP 与 Lua 思考日志。",
                creatorRoles,
                """{"type":"object","properties":{"deckA":{"type":"string"},"deckB":{"type":"string"},"duelCount":{"type":"integer"}},"required":[]}""",
                async (args, _, _, ct) =>
                {
                    if (_deps.SpawnWindBotCallback is null)
                        return (true, new { message = "当前服务端未加载可用卡库，无法启动内置 WindBot 模拟。" });

                    int count = Math.Clamp(GetInt(args, "duelCount", 1), 1, 3);
                    string deckA = GetString(args, "deckA") is { Length: > 0 } da ? da : "random";
                    string deckB = GetString(args, "deckB") is { Length: > 0 } db ? db : "random";

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
                            GameOptions opts = _deps.ServerHost.Server.Options.Game with
                            {
                                Mode = 0,
                                NoCheckDeck = true,
                                RandomDeck = deckA == "random" && deckB == "random" ? RandomDeckMode.ChaosDraft : RandomDeckMode.None
                            };

                            if (!_deps.ServerHost.Server.TryCreateRoom(simRoomName, out Game? simGame, opts) || simGame is null)
                                break;

                            _deps.SpawnWindBotCallback(simGame, deckA, null, 1);
                            _deps.SpawnWindBotCallback(simGame, deckB, null, 1);

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

                    return (false, new { matches = results, thoughts = capturedThoughts });
                }),

            // ---------- 4. 管理员运维工具 (admin only，同样严格遵守不透视暗牌原则) ----------
            new(
                "server_get_status",
                "查看游戏服务端运行状态、活动房间数量与卡库版本。",
                adminOnly,
                """{"type":"object","properties":{}}""",
                (_, _, _, _) =>
                {
                    CoreServer srv = _deps.ServerHost.Server;
                    return Task.FromResult<(bool, object)>((false, new
                    {
                        srv.IsRunning,
                        srv.IsListening,
                        srv.IsMaintenanceMode,
                        roomCount = _deps.ServerHost.GetRooms().Count,
                        activeMcpSessions = _deps.SessionManager.ListAllActiveSessions().Count,
                        catalogVersion = srv.ReloadableCards.CatalogVersion,
                        presetDeckCount = srv.PresetDecks.Count
                    }));
                }),

            new(
                "room_create",
                "在服务器预创建一个公开对局房间（支持规则前缀如 '2V2,RDM'）。",
                adminOnly,
                """{"type":"object","properties":{"roomName":{"type":"string"},"rulePrefix":{"type":"string"}},"required":["roomName"]}""",
                (args, _, _, _) =>
                {
                    string rName = GetString(args, "roomName");
                    string prefix = GetString(args, "rulePrefix");
                    GameOptions opts = _deps.ServerHost.Server.Options.Game;
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        string spec = prefix.Contains('#') ? prefix : $"{prefix}#{rName}";
                        opts = _deps.ServerHost.Server.PasswordParser.Parse(spec).ApplyTo(opts);
                    }
                    bool created = _deps.ServerHost.Server.TryCreateRoom(rName, out Game? g, opts);
                    return Task.FromResult<(bool, object)>((!created, new { success = created, roomName = g?.RoomName ?? rName }));
                }),

            new(
                "room_close",
                "强制关闭指定房间。",
                adminOnly,
                """{"type":"object","properties":{"roomName":{"type":"string"},"reason":{"type":"string"}},"required":["roomName"]}""",
                (args, _, _, _) =>
                {
                    string rName = GetString(args, "roomName");
                    if (!_deps.ServerHost.Server.TryGetRoom(rName, out Game? g) || g is null)
                        return Task.FromResult<(bool, object)>((true, new { message = $"房间 '{rName}' 不存在。" }));
                    CloseGameRoom(g);
                    return Task.FromResult<(bool, object)>((false, new { success = true }));
                }),

            new(
                "room_kick_player",
                "将指定房间的某个席位或观众请出房间。",
                adminOnly,
                """{"type":"object","properties":{"roomName":{"type":"string"},"seatIndex":{"type":"integer"},"playerName":{"type":"string"}},"required":["roomName"]}""",
                (args, _, _, _) =>
                {
                    string rName = GetString(args, "roomName");
                    if (!_deps.ServerHost.Server.TryGetRoom(rName, out Game? g) || g is null)
                        return Task.FromResult<(bool, object)>((true, new { message = $"房间 '{rName}' 不存在。" }));

                    Player? target = null;
                    if (args["seatIndex"] is not null)
                    {
                        int idx = GetInt(args, "seatIndex", -1);
                        if (idx >= 0 && idx < g.Players.Length)
                            target = g.Players[idx];
                    }
                    else
                    {
                        string pName = GetString(args, "playerName");
                        target = g.Players.FirstOrDefault(p => p is not null && string.Equals(p.Name, pName, StringComparison.OrdinalIgnoreCase))
                                 ?? g.Observers.FirstOrDefault(o => o is not null && string.Equals(o.Name, pName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (target is null)
                        return Task.FromResult<(bool, object)>((true, new { success = false, message = "未找到目标玩家或观众。" }));

                    g.RemovePlayer(target);
                    target.Disconnect();
                    return Task.FromResult<(bool, object)>((false, new { success = true }));
                }),

            new(
                "room_spawn_windbot",
                "向指定大厅房间召唤 1~4 台内置 WindBot 机器人。",
                adminOnly,
                """{"type":"object","properties":{"roomName":{"type":"string"},"deck":{"type":"string"},"count":{"type":"integer"}},"required":["roomName"]}""",
                (args, _, _, _) =>
                {
                    if (_deps.SpawnWindBotCallback is null)
                        return Task.FromResult<(bool, object)>((true, new { message = "当前未配置内置 WindBot 服务。" }));

                    string rName = GetString(args, "roomName");
                    if (!_deps.ServerHost.Server.TryGetRoom(rName, out Game? g) || g is null)
                        return Task.FromResult<(bool, object)>((true, new { message = $"房间 '{rName}' 不存在。" }));

                    string? deck = args["deck"]?.GetValue<string>();
                    int count = Math.Clamp(GetInt(args, "count", 1), 1, 4);
                    IReadOnlyList<string> spawned = _deps.SpawnWindBotCallback(g, deck, null, count);
                    return Task.FromResult<(bool, object)>((false, new { success = true, spawned }));
                })
        ];
    }

    // ==================== JSON-RPC 辅助 ====================

    private static void CloseGameRoom(Game game)
    {
        foreach (Player? p in game.Players.ToArray())
            p?.Disconnect();
        foreach (Player? obs in game.Observers.ToArray())
            obs?.Disconnect();
        game.End();
    }

    private static bool IsRoleAllowed(string callerRole, IReadOnlyList<string> allowedRoles)
    {
        return allowedRoles.Contains(callerRole, StringComparer.OrdinalIgnoreCase);
    }

    private static JsonObject BuildToolCallResponse(JsonNode? idNode, bool isError, object payload)
    {
        string text = payload is string s ? s : JsonSerializer.Serialize(payload, PrettyJsonOpts);
        return BuildJsonRpcResult(idNode, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = isError
        });
    }

    private static JsonObject BuildJsonRpcResult(JsonNode? idNode, JsonNode result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone(),
            ["result"] = result
        };
    }

    public static JsonObject BuildJsonRpcError(JsonNode? idNode, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    private static string GetString(JsonObject obj, string key) =>
        obj[key]?.GetValue<string>()?.Trim() ?? string.Empty;

    private static int GetInt(JsonObject obj, string key, int fallback)
    {
        JsonNode? n = obj[key];
        if (n is null) return fallback;
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out int i)) return i;
            if (v.TryGetValue(out long l)) return (int)l;
            if (v.TryGetValue(out string? s) && int.TryParse(s, out int parsed)) return parsed;
        }
        return fallback;
    }

    private static bool GetBool(JsonObject obj, string key, bool fallback)
    {
        JsonNode? n = obj[key];
        if (n is null) return fallback;
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out bool b)) return b;
            if (v.TryGetValue(out string? s) && bool.TryParse(s, out bool parsed)) return parsed;
        }
        return fallback;
    }

    private static int[] ReadIntArray(JsonArray? arr)
    {
        if (arr is null || arr.Count == 0)
            return [];
        List<int> list = new(arr.Count);
        foreach (JsonNode? item in arr)
        {
            if (item is JsonValue v)
            {
                if (v.TryGetValue(out int i)) list.Add(i);
                else if (v.TryGetValue(out long l)) list.Add((int)l);
            }
        }
        return list.ToArray();
    }

    private sealed record McpToolRegistration(
        string Name,
        string Description,
        IReadOnlyList<string> AllowedRoles,
        string InputSchemaJson,
        Func<JsonObject, ValidatedMcpToken, string?, CancellationToken, Task<(bool IsError, object Payload)>> Handler);
}
