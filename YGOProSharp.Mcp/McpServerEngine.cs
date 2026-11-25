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
    private readonly object _bundledDeckGate = new();
    private PresetDeckEntry[]? _cachedBundledDecks;

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

        string namingGuidance = string.IsNullOrWhiteSpace(caller.PreferredPlayerName)
            ? "【首次使用取名约定】该 Token 尚未绑定决斗者昵称！首次调用 duel_join_room（或 duel_set_profile）时，请严禁使用 AI1、AI2、Bot、Player 等机械编号，请结合你自身的模型人格或游戏王风格自己取一个有个性的决斗者昵称（2~18字符，如『星尘观测者』『深渊魔导』『青眼龙骑』等）填入 playerName，服务器将永久为你记住这个名字！"
            : $"你当前绑定的专属决斗者昵称为「{caller.PreferredPlayerName}」（进房时默认自动沿用，也可通过 duel_set_profile 更改）。";

        string instructions =
            $"已连接 YGOProSharp MCP 服务（当前令牌角色: {caller.McpRole}）。{namingGuidance} " +
            "打牌高效流程：1) 调用 room_list 查看房间，或调用 room_create 自建房间并可用 room_spawn_windbot 召唤内置 AI 对手；" +
            "2) 调用 duel_join_room 加入房间获得 sessionId；" +
            "3) 非强制随机房调用 duel_set_deck（自动适配房间禁限表与卡池），再调用 duel_set_ready(ready=true, startIfHost=true)；" +
            "4) 循环调用 duel_wait_for_turn 等待决策，阅读 pendingPrompt（关注 submitField='actionIndex' 或 'cardIndices'，选项自带卡名/攻守/子效果说明），" +
            "调用 duel_submit_action 提交动作（高频连打可设 detail='minimal' 或 'none' 节省 90% Token，也可用 duel_submit_batch 批量连招或 duel_auto_play 自动跳过连锁）。";

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
                    1. `room_list` 查看当前房间与规则（如 `RD` 盲盒预组、`RDM` 镜像同卡组、`CHAOS` 混沌随机、`2V2` 双打 16000LP）；无房时可直接用 `room_create` 建房并用 `room_spawn_windbot` 召唤内置 AI 陪练。
                    2. `duel_join_room` 传入 `room`（如 `"Room01"` 或带规则前缀 `"RD#Room01"`）加入房间，保存返回的 `sessionId`。
                    3. 若房间为自备卡组房，调用 `duel_set_deck`（支持 `randomMode: "preset" | "chaos" | "diy"` 自动适配房间禁限表，或指定 `presetName`）；若是强制随机房（`RD`/`RDM`/`CHAOS`）可直接跳过组卡。
                    4. 调用 `duel_set_ready`（`ready: true, startIfHost: true`）等待服务端完成卡组校验与开局同步。
                    5. 循环调用 `duel_wait_for_turn(sessionId)` 阻塞等待轮到自己操作；支持 `detail: "minimal" | "standard" | "full"` 控制盘面体积。
                    6. 每次唤醒后阅读 `pendingPrompt`（`submitField` 会明确指示应传 `actionIndex` 还是 `cardIndices` 数组，单选场景两者互通兼容）：
                       - 单步提交用 `duel_submit_action`（支持 `detail: "none"` 仅返回增量事件与下一步提示，极省上下文）；
                       - 多步确定性连招可用 `duel_submit_batch` 一次提交；跳过连锁响应可用 `duel_auto_play(strategy="pass_chains")` 或 `duel_configure_policy`；
                       - 需要查看己方完整卡组剩余构成时再按需调用 `duel_get_deck_list`。
                    7. 2v2 双打注意：你与队友共享场上怪兽/魔陷与墓地，但严禁也无法看到队友未公开的手牌；可用 `duel_sync_partner_tactic` 与搭档同步战术方针；当 `teammateSurrenderPending = true` 时代表队友请求投降，若同意可调用 `duel_surrender`。
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
                "duel_set_profile",
                "查看或设置当前 AI Token 在游戏内持久绑定的决斗者昵称（2~18字符，禁止使用 AI1/AI2/Bot 等生硬编号，推荐有个人风格的名字）。",
                [McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.Admin],
                """{"type":"object","properties":{"playerName":{"type":"string","description":"你想使用的专属决斗者昵称（2~18字符，不要用 AI1/AI2/Bot 等机械名；留空则仅查询当前昵称）"}}}""",
                async (args, caller, _, ct) =>
                {
                    string requested = GetString(args, "playerName");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        return (false, new
                        {
                            tokenId = caller.TokenId,
                            preferredPlayerName = caller.PreferredPlayerName,
                            needsSelfNaming = string.IsNullOrWhiteSpace(caller.PreferredPlayerName),
                            hint = string.IsNullOrWhiteSpace(caller.PreferredPlayerName)
                                ? "当前尚未绑定昵称，请传入一个有个性的决斗者昵称（不要用 AI1/AI2/Bot）。"
                                : $"当前绑定昵称为「{caller.PreferredPlayerName}」。"
                        });
                    }

                    (bool ok, string msg, string? bound) = await _deps.Store.BindMcpTokenPlayerNameAsync(caller.TokenId, requested, ct).ConfigureAwait(false);
                    return (!ok, new { success = ok, message = msg, preferredPlayerName = bound });
                }),

            new(
                "duel_list_preset_decks",
                "列出服务端预设卡组池与内置 WindBot 可用卡组（.ydk），可选按目标房间规则过滤出可直接过检的合法卡组。",
                playerRoles,
                """{"type":"object","properties":{"roomName":{"type":"string","description":"可选：按指定房间的禁限卡表与 OCG/TCG 区域过滤兼容卡组"},"compatibleOnly":{"type":"boolean","description":"若提供 roomName，是否仅返回完全符合该房间禁限表的卡组（默认 false）"}}}""",
                (args, _, _, _) =>
                {
                    string roomName = GetString(args, "roomName");
                    bool compatibleOnly = GetBool(args, "compatibleOnly", false);
                    IReadOnlyList<PresetDeckEntry> allEntries = GetCombinedPresetDecks();

                    Game? targetRoom = null;
                    if (!string.IsNullOrWhiteSpace(roomName))
                        _deps.ServerHost.Server.TryGetRoom(AgentSessionManager.ExtractTargetRoomName(roomName), out targetRoom);

                    var presets = allEntries
                        .Select(d =>
                        {
                            bool? roomLegal = null;
                            if (targetRoom is not null)
                                roomLegal = IsDeckCompatibleWithRoom(d.Data, targetRoom);
                            return new
                            {
                                d.Id,
                                d.Name,
                                d.HasDiyCards,
                                roomCompatible = roomLegal,
                                tags = d.Data.Tags,
                                mainCount = d.Data.Main.Count,
                                extraCount = d.Data.Extra.Count
                            };
                        })
                        .Where(x => !compatibleOnly || targetRoom is null || x.roomCompatible == true)
                        .ToArray();

                    return Task.FromResult<(bool, object)>((false, new
                    {
                        count = presets.Length,
                        presetDecks = presets,
                        windBotDecks = DecksManager.GetRegisteredDeckNames()
                    }));
                }),

            new(
                "duel_join_room",
                "以玩家（或观战者）身份加入指定房间。若该 Token 已被玩家预设了昵称或之前已绑定过昵称，可省略 playerName 自动沿用；若是首次使用未命名 Token，请你自己取一个有个性的决斗者昵称（严禁 AI1/AI2/Bot 等机械名）传入 playerName，系统会自动永久保存。",
                playerRoles,
                """{"type":"object","properties":{"room":{"type":"string","description":"房间名或完整主机密码，如 Room01 或 RD#Room01"},"playerName":{"type":"string","description":"游戏内决斗者昵称（2~18字符，严禁使用 AI1/AI2/Bot 等机械名；已绑定昵称时可留空沿用）"},"asObserver":{"type":"boolean","description":"是否作为观战者进入"},"detail":{"type":"string","enum":["minimal","standard","full"],"description":"返回盘面详略级别（默认 standard）"}},"required":["room"]}""",
                async (args, caller, remoteIp, ct) =>
                {
                    string roomSpec = GetString(args, "room");
                    string requestedName = GetString(args, "playerName");
                    bool asObs = GetBool(args, "asObserver", false);
                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);

                    string effectiveName;
                    if (!string.IsNullOrWhiteSpace(caller.PreferredPlayerName))
                    {
                        // 已由玩家预设昵称，或该 Token 首次使用时已自取绑定昵称 -> 默认优先沿用
                        effectiveName = caller.PreferredPlayerName;
                    }
                    else
                    {
                        // 首次使用未指定昵称的 Token：要求外部 AI 自己取一个非机械的个性化名字并永久绑定
                        if (string.IsNullOrWhiteSpace(requestedName) || OperationalStore.IsMechanicalAiName(requestedName))
                        {
                            return (true, new
                            {
                                success = false,
                                error = "self_naming_required",
                                needsSelfNaming = true,
                                message = "这是该 AI Token 首次登场且玩家未预设名字！请不要留空或使用 'AI1'/'AI2'/'Bot'/'Player' 等生硬编号——请结合你的模型个性或游戏王决斗者风格，自己构思一个帅气的名字（2~18字符，如『星尘观测者』『深渊魔导』『电子流·改』），将其填入 playerName 参数再次调用 duel_join_room，服务器会为你永久记住该名字并立即进房！"
                            });
                        }

                        (bool boundOk, string boundMsg, string? cleanBound) = await _deps.Store.BindMcpTokenPlayerNameAsync(caller.TokenId, requestedName, ct).ConfigureAwait(false);
                        if (!boundOk || cleanBound is null)
                            return (true, new { success = false, error = "invalid_player_name", message = boundMsg });

                        effectiveName = cleanBound;
                    }

                    (bool ok, string msg, AgentDuelSession? session) = await _deps.SessionManager.CreateAndJoinSessionAsync(
                        caller.TokenId,
                        caller.AllowedRooms,
                        caller.MaxSessions,
                        effectiveName,
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
                        playerName = session.PlayerName,
                        roomName = session.RoomName,
                        observation = session.GetObservation(detail)
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
                """{"type":"object","properties":{"sessionId":{"type":"string"},"toObserver":{"type":"boolean"},"detail":{"type":"string","enum":["minimal","standard","full"]}},"required":["sessionId"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Minimal);
                    (bool swapped, string msg, AgentObservation obs) = await s.SwapSeatAsync(GetBool(args, "toObserver", false), detail, ct).ConfigureAwait(false);
                    return (!swapped, new { success = swapped, message = msg, observation = obs });
                }),

            new(
                "duel_set_deck",
                "在大厅或换副阶段提交卡组：支持 randomMode ('preset'|'chaos'|'diy') 自动按当前房间禁限表生成合法卡组，或传 presetName / ydkContent / main+extra 数组。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"randomMode":{"type":"string","enum":["preset","chaos","diy"]},"presetName":{"type":"string"},"ydkContent":{"type":"string"},"main":{"type":"array","items":{"type":"integer"}},"extra":{"type":"array","items":{"type":"integer"}},"side":{"type":"array","items":{"type":"integer"}},"detail":{"type":"string","enum":["minimal","standard","full"]}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Minimal);
                    YdkDeckData? deckData = null;
                    string? selectedSource = null;
                    string presetName = GetString(args, "presetName");
                    string ydkText = GetString(args, "ydkContent");
                    string randomMode = GetString(args, "randomMode");

                    _deps.ServerHost.Server.TryGetRoom(s.RoomName, out Game? roomGame);
                    (Banlist? roomBanlist, bool ocg, bool tcg, bool allowCustom, bool noCheck) = ExtractRoomDeckConstraints(roomGame);

                    if (!string.IsNullOrWhiteSpace(ydkText))
                    {
                        deckData = YdkSerializer.Parse(ydkText, "McpCustom");
                        selectedSource = "ydkContent";
                    }
                    else if (!string.IsNullOrWhiteSpace(presetName))
                    {
                        PresetDeckEntry? found = FindPresetDeckByName(presetName);
                        if (found is null)
                            return Task.FromResult<(bool, object)>((true, new { message = $"未找到预设卡组 '{presetName}'。可调用 duel_list_preset_decks 查看可用列表。" }));
                        deckData = found.Data;
                        selectedSource = $"preset:{found.Id}";
                    }
                    else if (args["main"] is JsonArray mainArr && mainArr.Count > 0)
                    {
                        int[] mainIds = ReadIntArray(mainArr);
                        int[] extraIds = ReadIntArray(args["extra"] as JsonArray);
                        int[] sideIds = ReadIntArray(args["side"] as JsonArray);
                        deckData = new YdkDeckData("McpArrayDeck", mainIds, extraIds, sideIds, []);
                        selectedSource = "array";
                    }
                    else
                    {
                        // 默认按 randomMode 生成严格符合当前房间禁限表与 OCG/TCG/DIY 卡池的合法卡组
                        bool wantDiy = string.Equals(randomMode, "diy", StringComparison.OrdinalIgnoreCase);
                        if (string.IsNullOrWhiteSpace(randomMode) || string.Equals(randomMode, "preset", StringComparison.OrdinalIgnoreCase) || wantDiy)
                        {
                            List<PresetDeckEntry> compatible = GetCombinedPresetDecks()
                                .Where(e => (!wantDiy || e.HasDiyCards) && IsDeckCompatibleWithRoom(e.Data, roomGame))
                                .ToList();
                            if (compatible.Count > 0)
                            {
                                PresetDeckEntry chosen = compatible[Random.Shared.Next(compatible.Count)];
                                deckData = chosen.Data;
                                selectedSource = $"preset:{chosen.Id}";
                            }
                        }

                        if (deckData is null)
                        {
                            RandomDeckGenerator generator = new(_deps.ServerHost.Server.CardRepository);
                            Deck generated = generator.GenerateChaosDeck(
                                banlist: noCheck ? null : roomBanlist,
                                ocg: noCheck || ocg,
                                tcg: noCheck || tcg,
                                allowCustom: noCheck || allowCustom || wantDiy,
                                options: new ChaosDraftOptions
                                {
                                    MainDeckSize = 40,
                                    ExtraDeckSize = 15,
                                    BalancedRatio = true,
                                    PreferDiyCards = wantDiy
                                },
                                random: Random.Shared);
                            deckData = new YdkDeckData("McpChaosDeck", generated.Main.ToArray(), generated.Extra.ToArray(), generated.Side.ToArray(), []);
                            selectedSource = "chaos_draft";
                        }
                    }

                    (bool subOk, string subMsg) = s.SubmitDeck(deckData);
                    return Task.FromResult<(bool, object)>((!subOk, new
                    {
                        success = subOk,
                        message = subMsg,
                        deckName = deckData.Name,
                        source = selectedSource,
                        mainCount = deckData.Main.Count,
                        extraCount = deckData.Extra.Count,
                        observation = s.GetObservation(detail)
                    }));
                }),

            new(
                "duel_get_deck_list",
                "按需查询己方本局已提交卡组的无序张数清单与估算剩余在牌堆中的张数（不会泄露洗牌顺序；平时无需每回合调用）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    IReadOnlyList<AgentDeckCountSummary> list = s.GetDeckList();
                    return Task.FromResult<(bool, object)>((false, new { count = list.Count, cards = list }));
                }),

            new(
                "duel_set_ready",
                "在大厅准备或取消准备（自动等待服务端完成卡组禁限校验并同步最新席位/开局状态）；若自己是房主且 startIfHost=true，会同时尝试开局。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"ready":{"type":"boolean"},"startIfHost":{"type":"boolean"},"detail":{"type":"string","enum":["minimal","standard","full"]}},"required":["sessionId"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    bool ready = GetBool(args, "ready", true);
                    bool startIfHost = GetBool(args, "startIfHost", true);
                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);
                    (bool rOk, string rMsg, AgentObservation obs) = await s.SetReadyAsync(ready, startIfHost, detail, ct).ConfigureAwait(false);
                    return (!rOk, new { success = rOk, message = rMsg, observation = obs });
                }),

            new(
                "duel_start",
                "当自己是房主且全员已准备就绪时，主动发起开局并等待进入猜拳/选先后攻。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"detail":{"type":"string","enum":["minimal","standard","full"]}},"required":["sessionId"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);
                    (bool stOk, string stMsg, AgentObservation obs) = await s.RequestStartDuelAsync(detail, ct).ConfigureAwait(false);
                    return (!stOk, new { success = stOk, message = stMsg, observation = obs });
                }),

            new(
                "duel_wait_for_turn",
                "长轮询等待直到轮到当前会话做出决策（猜拳、选先后攻、出牌、连锁、选卡、2v2队友投降征询）或对局结束，直接返回己方视角局面与编号化合法动作。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"timeoutSeconds":{"type":"integer","description":"最长等待秒数 (1~28，默认 20)"},"detail":{"type":"string","enum":["minimal","standard","full"],"description":"盘面详略级别（默认 standard；紧凑连打可传 minimal）"}},"required":["sessionId"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    int sec = Math.Clamp(GetInt(args, "timeoutSeconds", 20), 1, 28);
                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);
                    AgentObservation obs = await s.WaitForTurnAsync(TimeSpan.FromSeconds(sec), detail, ct).ConfigureAwait(false);
                    return (false, obs);
                }),

            new(
                "duel_get_observation",
                "立即读取严格己方战争迷雾视角的结构化局面（默认 standard 不重复携带 40 张初始卡组表，传 detail='full' 可展开全部卡效）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"detail":{"type":"string","enum":["minimal","standard","full"],"description":"盘面详略级别（minimal|standard|full，默认 standard）"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);
                    return Task.FromResult<(bool, object)>((false, s.GetObservation(detail)));
                }),

            new(
                "duel_get_legal_actions",
                "读取当前等待响应的 promptId、submitField（'actionIndex' 或 'cardIndices'）与编号化合法动作列表。",
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
                "提交所选合法动作并等待服务端 ocgcore 完成结算后再返回最新快照：单选传 actionIndex，多张选卡/祭品/凑星传 cardIndices 数组（单选两者自动双向兼容）；支持 detail='none' 仅返回增量变化与下一步提示。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"actionIndex":{"type":"integer","description":"单选合法动作编号（单祭品/单张选卡也可直接传）"},"cardIndices":{"type":"array","items":{"type":"integer"},"description":"多张选卡、多祭品或凑星时的候选下标数组（如 [0, 1]）"},"indices":{"type":"array","items":{"type":"integer"},"description":"cardIndices 的兼容别名"},"promptId":{"type":"integer","description":"可选：校验当前询问编号防止重复提交"},"detail":{"type":"string","enum":["none","minimal","standard","full"],"description":"返回快照详略级别（默认 standard；传 none 最省上下文，仅回传 nextPrompt 与 newEvents）"},"thoughtSummary":{"type":"string","description":"可选：公开展示给观众的简短思考理由（勿写暗手牌机密）"},"chatMessage":{"type":"string","description":"可选：顺带发送到房间聊天框的台词"}},"required":["sessionId"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    int? actionIndex = args["actionIndex"] is not null ? GetInt(args, "actionIndex", 0) : null;
                    JsonArray? idxArr = args["cardIndices"] as JsonArray ?? args["indices"] as JsonArray;
                    int[]? cardIndices = idxArr is not null ? ReadIntArray(idxArr) : null;
                    long? promptId = args["promptId"] is not null ? args["promptId"]!.GetValue<long>() : null;
                    string? thought = args["thoughtSummary"]?.GetValue<string>();
                    string? chat = args["chatMessage"]?.GetValue<string>();
                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);

                    AgentActionResult res = await s.SubmitActionAsync(actionIndex, cardIndices, promptId, thought, chat, detail, waitSettleMs: 850, cancellationToken: ct).ConfigureAwait(false);
                    return (!res.Success, res);
                }),

            new(
                "duel_submit_batch",
                "在单次请求内按谓词连续执行多步确定性操作（如：发动某卡 -> 选目标 -> 自动放弃连锁），一旦遇到未预期分支或对方连锁立即安全停驻并返回当前询问。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"steps":{"type":"array","description":"顺序匹配步骤列表（最多 16 步）","items":{"type":"object","properties":{"promptType":{"type":"string","description":"期望的询问类型，如 select_idle_cmd / select_card / select_chain"},"kind":{"type":"string","description":"匹配 choice.kind，如 summon / activate / pass_chain"},"cardCode":{"type":"integer","description":"匹配候选卡片密码"},"labelContains":{"type":"string","description":"匹配选项标签子串"},"cancel":{"type":"boolean","description":"是否优先选择取消/不连锁选项"},"actionIndex":{"type":"integer","description":"兜底直接指定下标"}}}},"detail":{"type":"string","enum":["none","minimal","standard","full"]}},"required":["sessionId","steps"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    JsonArray? stepsArr = args["steps"] as JsonArray;
                    if (stepsArr is null || stepsArr.Count == 0)
                        return (true, new { message = "steps 数组不能为空。" });

                    List<AgentBatchStep> steps = new(Math.Min(16, stepsArr.Count));
                    foreach (JsonNode? item in stepsArr.Take(16))
                    {
                        if (item is not JsonObject stepObj)
                            continue;
                        steps.Add(new AgentBatchStep(
                            PromptType: stepObj["promptType"]?.GetValue<string>(),
                            Kind: stepObj["kind"]?.GetValue<string>(),
                            CardCode: stepObj["cardCode"] is not null ? GetInt(stepObj, "cardCode", 0) : null,
                            LabelContains: stepObj["labelContains"]?.GetValue<string>(),
                            Cancel: GetBool(stepObj, "cancel", false),
                            ActionIndex: stepObj["actionIndex"] is not null ? GetInt(stepObj, "actionIndex", 0) : null));
                    }

                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);
                    AgentActionResult res = await s.ExecuteBatchStepsAsync(steps, detail, ct).ConfigureAwait(false);
                    return (!res.Success, res);
                }),

            new(
                "duel_auto_play",
                "快速连打/自动代步工具：支持 strategy='pass_chains'（自动跳过所有非必发连锁窗口直至轮到主要决策）或 'simple_beatdown'（自动通召最高攻怪兽、进战阶攻击并结束回合，适合快速测试或残局收尾）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"strategy":{"type":"string","enum":["pass_chains","simple_beatdown"],"description":"自动策略（默认 pass_chains）"},"maxSteps":{"type":"integer","description":"最多自动执行步数 (1~20，默认 8)"},"detail":{"type":"string","enum":["none","minimal","standard","full"]}},"required":["sessionId"]}""",
                async (args, caller, _, ct) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return (true, new { message = err });

                    string strategy = GetString(args, "strategy") is { Length: > 0 } st ? st : "pass_chains";
                    int maxSteps = Math.Clamp(GetInt(args, "maxSteps", 8), 1, 20);
                    AgentObservationDetail detail = ParseDetail(args, AgentObservationDetail.Standard);
                    AgentActionResult res = await s.AutoPlayStepsAsync(maxSteps, strategy, detail, ct).ConfigureAwait(false);
                    return (!res.Success, res);
                }),

            new(
                "duel_configure_policy",
                "配置当前对局会话的快速响应策略：如开启唯一合法选项自动确认、同阶段重复连锁窗口自动折叠，或静默跳过接下来 N 次可取消连锁询问。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"autoConfirmSingleForced":{"type":"boolean","description":"仅剩唯一合法选项时是否自动确认（默认 true）"},"suppressRepeatedChainPass":{"type":"boolean","description":"同一阶段同一张卡 Pass 后是否自动跳过重复询问（默认 true）"},"silenceChainPrompts":{"type":"integer","description":"接下来自动放弃的可取消连锁询问次数 (0~100)"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    bool? autoForced = args["autoConfirmSingleForced"] is not null ? GetBool(args, "autoConfirmSingleForced", true) : null;
                    bool? suppressEcho = args["suppressRepeatedChainPass"] is not null ? GetBool(args, "suppressRepeatedChainPass", true) : null;
                    int? silence = args["silenceChainPrompts"] is not null ? GetInt(args, "silenceChainPrompts", 0) : null;
                    s.ConfigurePolicy(autoForced, suppressEcho, silence);
                    return Task.FromResult<(bool, object)>((false, new { success = true, message = "已更新会话自动响应策略。" }));
                }),

            new(
                "duel_update_plan",
                "更新当前会话的跨回合战术备忘录（保存在会话观测 strategicPlan 中，帮助跨回合保持长线计划）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"winCondition":{"type":"string"},"suspectedOpponentDeck":{"type":"string"},"turnGoal":{"type":"string"},"handoffToPartner":{"type":"string"},"bo3SidePlan":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    AgentStrategicPlan plan = s.UpdateStrategicPlan(
                        args["winCondition"]?.GetValue<string>(),
                        args["suspectedOpponentDeck"]?.GetValue<string>(),
                        args["turnGoal"]?.GetValue<string>(),
                        args["handoffToPartner"]?.GetValue<string>(),
                        args["bo3SidePlan"]?.GetValue<string>());
                    return Task.FromResult<(bool, object)>((false, new { success = true, strategicPlan = plan }));
                }),

            new(
                "duel_sync_partner_tactic",
                "在 2v2 双打模式下向同队搭档同步《卡片力量》式战术姿态（仅己方队伍可见，不向敌方公开）。",
                playerRoles,
                """{"type":"object","properties":{"sessionId":{"type":"string"},"stance":{"type":"string","enum":["aggressive","all_in_otk","control","setup_partner","defensive","balanced"]},"note":{"type":"string"}},"required":["sessionId"]}""",
                (args, caller, _, _) =>
                {
                    (bool ok, string err, AgentDuelSession? s) = _deps.SessionManager.TryGetOwnedSession(caller.TokenId, GetString(args, "sessionId"));
                    if (!ok || s is null)
                        return Task.FromResult<(bool, object)>((true, new { message = err }));

                    (bool syncOk, string msg, AgentPartnerTactic? tactic) = s.SyncPartnerTactic(
                        args["stance"]?.GetValue<string>(),
                        args["note"]?.GetValue<string>());
                    return Task.FromResult<(bool, object)>((!syncOk, new { success = syncOk, message = msg, partnerTactic = tactic }));
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
                        data = FindPresetDeckByName(presetName)?.Data;

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
                "在服务器创建一个公开对局房间（player 与 admin 均可用；支持规则前缀如 'CHAOS'、'RD'、'NC,NF'、'2V2,RDM'，并可一键召唤内置 WindBot 对手）。",
                playerRoles,
                """{"type":"object","properties":{"roomName":{"type":"string","description":"房间名称（如 Arena01；若受 AllowedRooms 白名单约束需在白名单内）"},"rulePrefix":{"type":"string","description":"可选规则前缀，如 'CHAOS'、'RD'、'NC,NF'、'2V2,RDM'"},"noCheckDeck":{"type":"boolean","description":"可选：是否关闭卡组禁限检查"},"spawnWindBot":{"type":"boolean","description":"可选：建房后是否立即召唤内置 WindBot 进入该房间"},"windBotDeck":{"type":"string","description":"可选：召唤的内置 WindBot 卡组名（如 BlueEyes 或 random）"},"windBotCount":{"type":"integer","description":"召唤数量 (1~3，默认 1)"}},"required":["roomName"]}""",
                async (args, caller, remoteIp, ct) =>
                {
                    string rawInput = GetString(args, "roomName");
                    string prefix = GetString(args, "rulePrefix");
                    if (rawInput.Contains('#'))
                    {
                        int hashIdx = rawInput.LastIndexOf('#');
                        if (string.IsNullOrWhiteSpace(prefix))
                            prefix = rawInput[..hashIdx].Trim();
                        rawInput = rawInput[(hashIdx + 1)..].Trim();
                    }

                    if (string.IsNullOrWhiteSpace(rawInput) || rawInput.Length > 24)
                        return (true, new { success = false, message = "roomName 长度必须在 1~24 个字符之间。" });

                    if (caller.McpRole != McpRoleCodes.Admin &&
                        caller.AllowedRooms.Count > 0 &&
                        !caller.AllowedRooms.Contains(rawInput, StringComparer.OrdinalIgnoreCase))
                    {
                        return (true, new
                        {
                            success = false,
                            error = "room_not_in_whitelist",
                            message = $"当前 Token 仅被允许在房间白名单 [{string.Join(", ", caller.AllowedRooms)}] 内建房或进房。"
                        });
                    }

                    GameOptions opts = _deps.ServerHost.Server.Options.Game;
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        string spec = prefix.Contains('#') ? prefix : $"{prefix}#{rawInput}";
                        opts = _deps.ServerHost.Server.PasswordParser.Parse(spec).ApplyTo(opts);
                    }
                    if (args["noCheckDeck"] is not null)
                        opts = opts with { NoCheckDeck = GetBool(args, "noCheckDeck", opts.NoCheckDeck) };

                    bool created = _deps.ServerHost.Server.TryCreateRoom(rawInput, out Game? g, opts);
                    if (!created || g is null)
                        return (true, new { success = false, message = $"房间 '{rawInput}' 已存在或服务器已达房间上限。" });

                    IReadOnlyList<string>? spawnedBots = null;
                    if (GetBool(args, "spawnWindBot", false) && _deps.SpawnWindBotCallback is not null)
                    {
                        string? botDeck = args["windBotDeck"]?.GetValue<string>();
                        int botCnt = Math.Clamp(GetInt(args, "windBotCount", 1), 1, g.IsTag ? 3 : 1);
                        try
                        {
                            spawnedBots = _deps.SpawnWindBotCallback(g, botDeck, null, botCnt);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Optional WindBot spawn during room_create failed.");
                        }
                    }

                    await _deps.Store.AppendAuditAsync(
                        "mcp_token",
                        caller.TokenId.ToString(),
                        $"{caller.OwnerUsername}:{caller.Name}",
                        "mcp.room_create",
                        caller.McpRole,
                        g.RoomName,
                        remoteIp,
                        "ok",
                        prefix,
                        ct).ConfigureAwait(false);

                    return (false, new
                    {
                        success = true,
                        roomName = g.RoomName,
                        mode = g.Mode,
                        randomDeckMode = g.RandomDeckMode.ToString(),
                        noCheckDeck = g.NoCheckDeck,
                        spawnedBots
                    });
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
                "向指定大厅房间召唤 1~4 台内置 WindBot 机器人陪练或补齐 2v2 席位（player 角色可向自己所在房间或新建空房召唤）。",
                playerRoles,
                """{"type":"object","properties":{"roomName":{"type":"string","description":"目标房间名"},"deck":{"type":"string","description":"可选：卡组名（如 BlueEyes、Yubel）或 'random'"},"count":{"type":"integer","description":"召唤数量 (1~4，默认 1)"}},"required":["roomName"]}""",
                (args, caller, _, _) =>
                {
                    if (_deps.SpawnWindBotCallback is null)
                        return Task.FromResult<(bool, object)>((true, new { success = false, message = "当前未配置内置 WindBot 服务或尚未加载卡库。" }));

                    string rName = AgentSessionManager.ExtractTargetRoomName(GetString(args, "roomName"));
                    if (!_deps.ServerHost.Server.TryGetRoom(rName, out Game? g) || g is null)
                        return Task.FromResult<(bool, object)>((true, new { success = false, message = $"房间 '{rName}' 不存在。" }));

                    if (caller.McpRole != McpRoleCodes.Admin)
                    {
                        if (caller.AllowedRooms.Count > 0 &&
                            !caller.AllowedRooms.Contains(g.RoomName, StringComparer.OrdinalIgnoreCase))
                        {
                            return Task.FromResult<(bool, object)>((true, new { success = false, message = $"当前 Token 无权操作房间 '{g.RoomName}'。" }));
                        }

                        bool isRoomEmpty = g.Players.All(static p => p is null);
                        bool callerInRoom = _deps.SessionManager.ListOwnedSessions(caller.TokenId)
                            .Any(s => s.IsConnected && string.Equals(s.RoomName, g.RoomName, StringComparison.OrdinalIgnoreCase));
                        if (!isRoomEmpty && !callerInRoom)
                        {
                            return Task.FromResult<(bool, object)>((true, new { success = false, message = "非管理员角色仅可向自己当前所在的房间或刚创建的空房间召唤内置 AI。" }));
                        }
                    }

                    try
                    {
                        string? deck = args["deck"]?.GetValue<string>();
                        int count = Math.Clamp(GetInt(args, "count", 1), 1, 4);
                        IReadOnlyList<string> spawned = _deps.SpawnWindBotCallback(g, deck, null, count);
                        return Task.FromResult<(bool, object)>((false, new { success = true, roomName = g.RoomName, spawned }));
                    }
                    catch (Exception ex)
                    {
                        return Task.FromResult<(bool, object)>((true, new { success = false, message = ex.Message }));
                    }
                })
        ];
    }

    // ==================== 预设卡组发现、房间禁限匹配与辅助 ====================

    private IReadOnlyList<PresetDeckEntry> GetCombinedPresetDecks()
    {
        IReadOnlyList<PresetDeckEntry> serverPool = _deps.ServerHost.Server.PresetDecks.Entries;
        PresetDeckEntry[] bundled = GetBundledWindBotDecks();
        if (bundled.Length == 0)
            return serverPool;
        if (serverPool.Count == 0)
            return bundled;

        Dictionary<string, PresetDeckEntry> merged = new(serverPool.Count + bundled.Length, StringComparer.OrdinalIgnoreCase);
        foreach (PresetDeckEntry entry in serverPool)
            merged[entry.Id] = entry;
        foreach (PresetDeckEntry entry in bundled)
            merged.TryAdd(entry.Id, entry);
        return merged.Values.ToArray();
    }

    private PresetDeckEntry[] GetBundledWindBotDecks()
    {
        if (_cachedBundledDecks is not null)
            return _cachedBundledDecks;

        lock (_bundledDeckGate)
        {
            if (_cachedBundledDecks is not null)
                return _cachedBundledDecks;

            PresetDeckPool tempPool = new();
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "Decks"),
                "Decks"
            ];
            tempPool.ReloadFromDirectories(candidates, _deps.ServerHost.Server.CardRepository, additionalTags: ["windbot"]);
            _cachedBundledDecks = tempPool.Entries.ToArray();
            return _cachedBundledDecks;
        }
    }

    private PresetDeckEntry? FindPresetDeckByName(string nameOrId)
    {
        string trimmed = nameOrId.Trim();
        string aiPrefixed = trimmed.StartsWith("AI_", StringComparison.OrdinalIgnoreCase) ? trimmed : $"AI_{trimmed}";
        foreach (PresetDeckEntry d in GetCombinedPresetDecks())
        {
            if (string.Equals(d.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Name, aiPrefixed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Id, aiPrefixed, StringComparison.OrdinalIgnoreCase))
            {
                return d;
            }
        }
        return null;
    }

    private bool IsDeckCompatibleWithRoom(YdkDeckData data, Game? room)
    {
        ICardRepository cardRepo = _deps.ServerHost.Server.CardRepository;
        Deck deck = YdkSerializer.ToDeck(data, cardRepo);
        if (deck.Main.Count < 40)
            return false;

        if (room is null || room.NoCheckDeck)
            return true;

        (Banlist? banlist, bool ocg, bool tcg, bool allowCustom, _) = ExtractRoomDeckConstraints(room);
        return deck.Check(banlist, ocg, tcg, allowCustom, DeckRules.Default) == 0;
    }

    private static (Banlist? Banlist, bool Ocg, bool Tcg, bool AllowCustom, bool NoCheck) ExtractRoomDeckConstraints(Game? room)
    {
        if (room is null)
            return (null, true, true, true, false);

        int region = room.Region;
        bool ocg = region != 1;
        bool tcg = region != 0;
        bool allowCustom = region >= 3;
        return (room.Banlist, ocg, tcg, allowCustom, room.NoCheckDeck);
    }

    private static AgentObservationDetail ParseDetail(JsonObject args, AgentObservationDetail fallback)
    {
        string raw = GetString(args, "detail");
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        return raw.ToLowerInvariant() switch
        {
            "none" or "quiet" => AgentObservationDetail.None,
            "min" or "minimal" or "compact" => AgentObservationDetail.Minimal,
            "std" or "standard" => AgentObservationDetail.Standard,
            "full" or "verbose" => AgentObservationDetail.Full,
            _ => fallback
        };
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
        string text = payload is string s ? s : JsonSerializer.Serialize(payload, JsonOpts);
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
