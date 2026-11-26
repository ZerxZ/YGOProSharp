using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Mcp;

/// <summary>
/// MCP Resources 与 Prompts 目录内容定义（使用官方 <see cref="ModelContextProtocol.Protocol"/> 强类型协议契约并按角色裁剪）。
/// </summary>
internal static class McpCatalogContent
{
    private static readonly JsonSerializerOptions PrettyJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static ListResourcesResult ListResources(ValidatedMcpToken caller)
    {
        List<Resource> resources =
        [
            new()
            {
                Uri = "ygopro://guide/duelist-protocol",
                Name = "YGOProSharp 外部 AI 决斗操作指南",
                Description = "1v1 / 2v2 双打、盲盒随机房与合法动作编号提交流程说明",
                MimeType = "text/markdown"
            },
            new()
            {
                Uri = "ygopro://rooms/public",
                Name = "公开活动房间实时快照",
                Description = "当前服务器上所有活动房间与席位概览（不含暗牌与玩家 IP）",
                MimeType = "application/json"
            }
        ];

        if (caller.McpRole is McpRoleCodes.DiyCreator or McpRoleCodes.Admin)
        {
            resources.Add(new Resource
            {
                Uri = "ygopro://guide/windbot-lua-dsl",
                Name = "WindBot xmake 风格 Lua AI 四层决策规范",
                Description = "deck(...) 作用域、0-based 列表、EmitThought 与 AskExternalAgent 双脑接口说明",
                MimeType = "text/markdown"
            });
        }

        return new ListResourcesResult { Resources = resources };
    }

    public static ReadResourceResult? TryReadResource(string uri, ValidatedMcpToken caller, McpRuntimeDependencies deps)
    {
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
                text = JsonSerializer.Serialize(deps.ServerHost.GetRooms(), PrettyJsonOpts);
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
                return null;
        }

        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = uri,
                    MimeType = mime,
                    Text = text
                }
            ]
        };
    }

    public static ListPromptsResult ListPrompts(ValidatedMcpToken caller)
    {
        List<Prompt> prompts = [];

        if (caller.McpRole is McpRoleCodes.Player or McpRoleCodes.Admin)
        {
            prompts.Add(new Prompt
            {
                Name = "play-duel-room",
                Description = "引导 AI 进入指定房间并按规则完成整局 1v1 或 2v2 决斗",
                Arguments =
                [
                    new PromptArgument { Name = "room", Description = "房间名称或主机密码（如 Room01 或 RD#Room01）", Required = true },
                    new PromptArgument { Name = "playerName", Description = "AI 游戏内昵称", Required = false }
                ]
            });

            prompts.Add(new Prompt
            {
                Name = "play-tag-force-partner",
                Description = "《游戏王：卡片力量（Tag Force）》2v2 双打搭档协同规范：理解四席轮转、共享场上墓地与同队私密战术沟通",
                Arguments =
                [
                    new PromptArgument { Name = "room", Description = "2v2 房间名或主机密码（如 2V2#TagRoom）", Required = true },
                    new PromptArgument { Name = "personaStyle", Description = "可选：搭档性格与台词风格（如 热血魂系 / 冷静学院派 / 傲娇社长）", Required = false }
                ]
            });
        }

        prompts.Add(new Prompt
        {
            Name = "evaluate-deck-six-dims",
            Description = "剖析卡组静态构筑特征并从爆发强度、续航韧性、阴间指数、娱乐整活度、操作复杂度、环境适应性六个维度进行 0~100 整数打分与套路提炼",
            Arguments =
            [
                new PromptArgument { Name = "presetName", Description = "预设卡组名（与 ydkContent 二选一）", Required = false },
                new PromptArgument { Name = "ydkContent", Description = ".ydk 卡组文本内容", Required = false }
            ]
        });

        prompts.Add(new Prompt
        {
            Name = "review-finished-duel",
            Description = "读取已结束归档对局（R#<id>）的全视角时间轴与双方构筑，生成赛后教练级复盘报告、0~100 发挥评分、胜负手转折点与改进建议",
            Arguments =
            [
                new PromptArgument { Name = "duelId", Description = "已结束对局编号（如 R#1 或 1）", Required = true }
            ]
        });

        if (caller.McpRole is McpRoleCodes.DiyCreator or McpRoleCodes.Admin)
        {
            prompts.Add(new Prompt
            {
                Name = "author-windbot-lua",
                Description = "读取卡组效文、编写 WindBot Lua 四层决策脚本并通过闪电自博弈验证",
                Arguments =
                [
                    new PromptArgument { Name = "deckName", Description = "目标卡组名", Required = true }
                ]
            });
        }

        return new ListPromptsResult { Prompts = prompts };
    }

    public static GetPromptResult? TryGetPrompt(string? name, JsonObject? args, ValidatedMcpToken caller)
    {
        if (name == "play-duel-room" && caller.McpRole is McpRoleCodes.Player or McpRoleCodes.Admin)
        {
            string room = args?["room"]?.GetValue<string>() ?? "Room01";
            string pName = args?["playerName"]?.GetValue<string>() ?? caller.PreferredPlayerName ?? caller.Name;
            return BuildSingleUserPrompt(
                $"加入房间 {room} 进行游戏王对决",
                $"请先调用 duel_get_memories 回顾你的历史实战备忘，再以昵称 '{pName}' 调用 duel_join_room 加入房间 '{room}'。若非强制随机房则使用 duel_set_deck(randomMode='preset') 领一套卡组，然后 duel_set_ready 准备开局，并通过 duel_wait_for_turn 与 duel_submit_action（可附带 planUpdate 与贴合人设的 chatLine）认真打完本场对决。");
        }

        if (name == "play-tag-force-partner" && caller.McpRole is McpRoleCodes.Player or McpRoleCodes.Admin)
        {
            string room = args?["room"]?.GetValue<string>() ?? "2V2#TagRoom";
            string persona = args?["personaStyle"]?.GetValue<string>() ?? caller.PersonaStyle ?? "默契可靠的卡片力量搭档";
            return BuildSingleUserPrompt(
                $"以《卡片力量》双打搭档身份加入 {room}",
                $"你将以人设「{persona}」进入 2v2 双打房间 '{room}' 与队友并肩作战！请严格遵循《卡片力量（Tag Force）》双打准则：\n" +
                "1. 敌我与轮转铁律：认真阅读 observation.tagContext（turnRotationOrder 展示 0->2->1->3 轮转顺序、谁是你的同队 partnerName、谁是两名敌方 enemyNames）；切勿误炸己方共享场上的终端或乱交搭档留下的阻抗盖卡。\n" +
                "2. 尊重队友战术指令：随时检查 observation.partnerTactic（若队友指示 all_in_otk 则全力清场斩杀；若指示 setup_partner/control 则稳健做场留康保护下回合接棒的队友）。\n" +
                "3. 回合结束交接：结束自己回合前，务必调用 duel_tag_sync(stance, note, sharedBoardHandoffNote) 告诉搭档你场上盖卡/怪兽的用途与留场意图（仅队友可见，敌人看不到）！严禁在公共 chatLine 中泄露任何暗牌。");
        }

        if (name == "evaluate-deck-six-dims")
        {
            string preset = args?["presetName"]?.GetValue<string>() ?? string.Empty;
            string ydk = args?["ydkContent"]?.GetValue<string>() ?? string.Empty;
            string targetDesc = !string.IsNullOrWhiteSpace(preset) ? $"presetName='{preset}'" : (!string.IsNullOrWhiteSpace(ydk) ? "所提供的 ydkContent" : "目标卡组");
            return BuildSingleUserPrompt(
                "卡组静态特征剖析与 0~100 六维量化评测",
                $"请对 {targetDesc} 执行专业六维构筑评测：\n" +
                "1. 先调用 deck_analyze_features 获取该卡组的精确怪魔陷配比、本家字段分布、效果词频与完整卡片效文；\n" +
                "2. 研读核心初动、检索链、终场阻抗与后手突破点，为六个维度分别给出 **0 ~ 100 整数分**：\n" +
                "   - burst_power（爆发强度：单回合展开上限、OTK 斩杀力）\n" +
                "   - resilience（续航韧性：踩手坑妥协、墓地资源回收与长盘能力）\n" +
                "   - stun_toxicity（阴间指数：贴纸封锁、先攻自闭、红坑折磨与反制交互压迫感）\n" +
                "   - fun_factor（娱乐整活度：魂度、节目效果、奇袭 Combo 惊喜感）\n" +
                "   - complexity（操作复杂度：分岔抉择难度、连锁时点要求）\n" +
                "   - meta_adaptability（环境适应性：泛用手坑兼容度、先后手均衡性与解场泛用度）\n" +
                "3. 调用 deck_submit_evaluation 提交 6 项整数评分、核心 Combo 套路（coreRoutines）、优缺点与改构建议，使前端自动点亮六维雷达图。");
        }

        if (name == "review-finished-duel")
        {
            string duelId = args?["duelId"]?.GetValue<string>() ?? "R#1";
            return BuildSingleUserPrompt(
                $"对已结束对局 {duelId} 执行全视角教练级复盘与打分",
                $"请对已结束归档的对局 '{duelId}' 进行深度赛后复盘：\n" +
                "1. 调用 duel_get_replay_timeline(duelId) 读取双方完整卡组构筑与逐回合公开决策时间轴；\n" +
                "2. 定位本局的胜负手转折点（turningPoints：哪一回合哪次交康/贪打/精准突破决定了局势）以及双方打出的套路配合（observedPatterns）；\n" +
                "3. 为每位参赛选手打出 0~100 整数发挥评分（playScore，若是 2v2 双打则额外给出 0~100 的 teamworkScore 评估搭档资源交接与协同度）并评选 MVP；\n" +
                "4. 调用 duel_submit_review 提交结构化复盘报告（若你自身也作为选手参与过类似对局，可顺带填写 lessonForMemory 沉淀进跨局生涯记忆库）。");
        }

        if (name == "author-windbot-lua" && caller.McpRole is McpRoleCodes.DiyCreator or McpRoleCodes.Admin)
        {
            string deckName = args?["deckName"]?.GetValue<string>() ?? "ExampleBlueEyes";
            return BuildSingleUserPrompt(
                $"为卡组 {deckName} 编写并测试 WindBot Lua 脚本",
                $"请先调用 deck_inspect 分析 '{deckName}' 的全部卡片效果，阅读 ygopro://guide/windbot-lua-dsl，调用 windbot_write_deck_script 编写脚本，最后调用 windbot_simulate_match 跑对局复盘。");
        }

        return null;
    }

    private static GetPromptResult BuildSingleUserPrompt(string description, string text) =>
        new()
        {
            Description = description,
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = text }
                }
            ]
        };
}
