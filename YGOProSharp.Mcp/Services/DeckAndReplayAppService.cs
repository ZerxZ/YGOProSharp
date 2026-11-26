using System.Text.Json.Nodes;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Server;

namespace YGOProSharp.Mcp.Services;

/// <summary>
/// 供 MCP 与 Web 门户共享的卡组静态剖析、六维评分 DTO 格式化与赛后已完结战报查询服务（严格仅限已结束对局，零暗牌与 IP 泄露）。
/// </summary>
public sealed class DeckAndReplayAppService(
    YGOProSharpServerHost serverHost,
    OperationalStore store,
    PresetDeckCatalogService presetCatalog,
    Func<INamedCardRepository> namedCardRepoProvider)
{
    public PresetDeckCatalogService Presets => presetCatalog;

    public async Task<(bool Ok, string Error, YdkDeckData? Data, string SourceType, string? SourceRef)> ResolveTargetDeckAsync(
        string? presetName,
        string? ydkContent,
        string? deckName,
        long? duelId,
        int seatIndex = 0,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(ydkContent))
        {
            YdkDeckData parsed = YdkSerializer.Parse(ydkContent.Trim(), string.IsNullOrWhiteSpace(deckName) ? "CustomDeck" : deckName.Trim());
            if (parsed.Main.Count == 0 && parsed.Extra.Count == 0)
                return (false, "提供的 ydkContent 未解析到任何有效卡片密码。", null, "custom_ydk", null);
            return (true, string.Empty, parsed, "custom_ydk", parsed.Name);
        }

        if (!string.IsNullOrWhiteSpace(presetName))
        {
            PresetDeckEntry? preset = presetCatalog.FindByNameOrId(serverHost.Server, presetName);
            if (preset is null)
                return (false, $"未找到预设卡组 '{presetName.Trim()}'。", null, "preset", presetName.Trim());
            return (true, string.Empty, preset.Data, "preset", preset.Id);
        }

        if (duelId is > 0)
        {
            DuelRecordEntity? record = await store.GetDuelRecordByIdAsync(duelId.Value, ct).ConfigureAwait(false);
            if (record is null)
                return (false, $"未找到已结束归档的对局 R#{duelId.Value}（进行中对局禁止提取选手卡组）。", null, "duel_record", $"R#{duelId.Value}");

            DuelRecordPlayerEntity? slot = record.Players.FirstOrDefault(p => p.SeatIndex == seatIndex)
                                           ?? record.Players.OrderBy(static p => p.SeatIndex).FirstOrDefault();
            string? slotYdk = slot is null ? null : (!string.IsNullOrWhiteSpace(slot.IngameDeckYdk) ? slot.IngameDeckYdk : slot.StartDeckYdk);
            if (slot is null || string.IsNullOrWhiteSpace(slotYdk))
                return (false, $"对局 R#{duelId.Value} 席位 #{seatIndex} 未保存可用卡组记录。", null, "duel_record", $"R#{duelId.Value}:seat{seatIndex}");

            YdkDeckData parsed = YdkSerializer.Parse(slotYdk, $"{slot.PlayerName}_R#{duelId.Value}");
            return (true, string.Empty, parsed, "duel_record", $"R#{duelId.Value}:seat{slot.SeatIndex}");
        }

        return (false, "请提供 presetName、ydkContent 或已结束对局的 duelId。", null, "none", null);
    }

    public async Task<(DeckStaticProfile Profile, object? Evaluation)> AnalyzeDeckAsync(
        YdkDeckData deckData,
        CancellationToken ct = default)
    {
        DeckStaticProfile profile = DeckStaticProfileBuilder.Analyze(
            deckData,
            namedCardRepoProvider(),
            serverHost.Server.CardRepository);
        DeckEvaluationEntity? eval = await store.GetLatestDeckEvaluationByHashAsync(profile.DeckHash, ct).ConfigureAwait(false);
        return (profile, eval is null ? null : FormatDeckEvaluationDto(eval));
    }

    public async Task<object?> GetArchivedReplayDetailAsync(
        long duelId,
        bool includeFullExtraAndAllMain,
        CancellationToken ct = default)
    {
        DuelRecordEntity? record = await store.GetDuelRecordByIdAsync(duelId, ct).ConfigureAwait(false);
        if (record is null)
            return null;

        INamedCardRepository namedRepo = namedCardRepoProvider();
        IReadOnlyList<DuelReviewEntity> reviews = await store.ListDuelReviewsAsync(record.Id, ct).ConfigureAwait(false);

        var players = record.Players.OrderBy(static p => p.SeatIndex).Select(p =>
        {
            string? ydkRaw = !string.IsNullOrWhiteSpace(p.IngameDeckYdk) ? p.IngameDeckYdk : p.StartDeckYdk;
            string? deckHash = null;
            int mainCount = 0;
            int extraCount = 0;
            object? fullDeck = null;
            object[] sampleCards = [];

            if (!string.IsNullOrWhiteSpace(ydkRaw))
            {
                try
                {
                    YdkDeckData parsed = YdkSerializer.Parse(ydkRaw, p.PlayerName);
                    deckHash = DeckStaticProfileBuilder.ComputeCanonicalDeckHash(parsed);
                    mainCount = parsed.Main.Count;
                    extraCount = parsed.Extra.Count;

                    var groupedMain = parsed.Main.GroupBy(static id => id).Select(g => new
                    {
                        id = g.Key,
                        copies = g.Count(),
                        name = namedRepo.TryGetCard(g.Key, out NamedCard? nc) && nc is not null ? nc.Name : $"#{g.Key}"
                    }).ToArray();

                    sampleCards = groupedMain.Take(16).Cast<object>().ToArray();

                    if (includeFullExtraAndAllMain)
                    {
                        var groupedExtra = parsed.Extra.GroupBy(static id => id).Select(g => new
                        {
                            id = g.Key,
                            copies = g.Count(),
                            name = namedRepo.TryGetCard(g.Key, out NamedCard? nc) && nc is not null ? nc.Name : $"#{g.Key}"
                        }).ToArray();

                        fullDeck = new
                        {
                            mainCount,
                            extraCount,
                            mainCards = groupedMain,
                            extraCards = groupedExtra
                        };
                    }
                }
                catch
                {
                }
            }

            return new
            {
                p.SeatIndex,
                p.TeamId,
                p.PlayerName,
                p.SeatType,
                p.IsWinner,
                deckHash,
                mainCount,
                extraCount,
                sampleCards,
                deck = fullDeck
            };
        }).ToArray();

        JsonNode? timeline = null;
        List<object> lpCurve = new();
        if (!string.IsNullOrWhiteSpace(record.DecisionTimelineJson))
        {
            try
            {
                timeline = JsonNode.Parse(record.DecisionTimelineJson);
                if (timeline is JsonArray arr)
                {
                    foreach (JsonNode? item in arr)
                    {
                        if (item is not JsonObject obj)
                            continue;
                        int? t0 = (int?)(obj["team0Lp"] ?? obj["Team0Lp"]);
                        int? t1 = (int?)(obj["team1Lp"] ?? obj["Team1Lp"]);
                        if (t0.HasValue && t1.HasValue)
                        {
                            lpCurve.Add(new
                            {
                                turn = (int?)(obj["turn"] ?? obj["Turn"]) ?? 0,
                                kind = (string?)(obj["kind"] ?? obj["Kind"]) ?? "duel.lp",
                                actor = (string?)(obj["actor"] ?? obj["Actor"]),
                                summary = (string?)(obj["text"] ?? obj["Text"]) ?? string.Empty,
                                team0Lp = t0.Value,
                                team1Lp = t1.Value
                            });
                        }
                    }
                }
            }
            catch
            {
            }
        }

        object[] formattedReviews = reviews.Select(FormatDuelReviewDto).ToArray();
        return new
        {
            success = true,
            duelId = record.Id,
            replayCode = $"R#{record.Id}",
            record.RoomName,
            record.Mode,
            isTag2v2 = record.Mode == 2,
            record.MasterRule,
            record.StartLp,
            record.RandomDeckMode,
            record.IsSuddenDeath,
            record.TurnCount,
            record.WinnerTeam,
            record.WinReason,
            record.StartedAt,
            record.EndedAt,
            hasYrp = !string.IsNullOrWhiteSpace(record.YrpPath),
            players,
            lpCurve,
            timeline = timeline ?? new JsonArray(),
            reviews = formattedReviews,
            existingReviews = formattedReviews
        };
    }

    public static object FormatDeckEvaluationDto(DeckEvaluationEntity e)
    {
        JsonNode? routines = null;
        if (!string.IsNullOrWhiteSpace(e.RoutinesJson))
        {
            try { routines = JsonNode.Parse(e.RoutinesJson); } catch { }
        }

        return new
        {
            e.Id,
            e.DeckHash,
            e.DeckName,
            e.SourceType,
            e.SourceRef,
            e.ArchetypeTitle,
            e.OverallTier,
            dimensions = new
            {
                burst_power = e.BurstPower,
                resilience = e.Resilience,
                stun_toxicity = e.StunToxicity,
                fun_factor = e.FunFactor,
                complexity = e.Complexity,
                meta_adaptability = e.MetaAdaptability
            },
            routines,
            e.Commentary,
            e.EvaluatorName,
            e.UpdatedAt
        };
    }

    public static object FormatDuelReviewDto(DuelReviewEntity r)
    {
        JsonNode? ratings = null;
        JsonNode? turningPoints = null;
        JsonNode? observedPatterns = null;
        try { ratings = JsonNode.Parse(r.PlayerRatingsJson); } catch { }
        try { turningPoints = JsonNode.Parse(r.TurningPointsJson); } catch { }
        try { observedPatterns = JsonNode.Parse(r.ObservedPatternsJson); } catch { }

        return new
        {
            r.Id,
            duelId = r.DuelRecordId,
            replayCode = $"R#{r.DuelRecordId}",
            r.Headline,
            r.Summary,
            r.ReviewerName,
            r.ReviewerRole,
            playerRatings = ratings ?? new JsonArray(),
            turningPoints = turningPoints ?? new JsonArray(),
            observedPatterns = observedPatterns ?? new JsonArray(),
            r.CreatedAt
        };
    }
}
