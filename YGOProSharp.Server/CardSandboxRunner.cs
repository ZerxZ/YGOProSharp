using System.Buffers;
using System.Diagnostics;
using System.Text;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Server;

public sealed record CardSandboxRequest(
    DiyCardEntry Card,
    string? LuaScript = null,
    bool TestExecuteFirstActivation = true);

public sealed record CardSandboxResult(
    bool Success,
    bool NativeExecuted,
    double ElapsedMs,
    IReadOnlyList<string> StaticAuditViolations,
    IReadOnlyList<string> LuaErrors,
    IReadOnlyList<string> ObservedMessages,
    int SummonableCount,
    int SpSummonableCount,
    int ActivatableCount,
    int PostPlayer0Lp,
    int PostPlayer1Lp);

internal sealed class SandboxThreadOverlay
{
    [ThreadStatic]
    private static SandboxThreadOverlay? t_current;

    public static SandboxThreadOverlay? Current => t_current;

    public Dictionary<uint, OcgCardData> Cards { get; } = new();
    public Dictionary<string, byte[]> Scripts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable Push(SandboxThreadOverlay overlay)
    {
        SandboxThreadOverlay? prev = t_current;
        t_current = overlay;
        return new PopScope(prev);
    }

    private sealed class PopScope(SandboxThreadOverlay? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            t_current = previous;
        }
    }
}

internal sealed class SandboxOverlayCardDataProvider(ICardDataProvider inner) : ICardDataProvider
{
    public bool TryGetCardData(uint cardCode, out OcgCardData cardData)
    {
        SandboxThreadOverlay? overlay = SandboxThreadOverlay.Current;
        if (overlay is not null && overlay.Cards.TryGetValue(cardCode, out cardData))
            return true;

        return inner.TryGetCardData(cardCode, out cardData);
    }
}

internal sealed class SandboxOverlayScriptProvider(IScriptProvider inner) : IScriptProvider, IDisposable
{
    private static readonly byte[] FallbackConstantLua = Encoding.UTF8.GetBytes("""
        EFFECT_TYPE_SINGLE = 0x0001
        EFFECT_TYPE_FIELD = 0x0002
        EFFECT_TYPE_EQUIP = 0x0004
        EFFECT_TYPE_ACTIONS = 0x0008
        EFFECT_TYPE_ACTIVATE = 0x0010
        EFFECT_TYPE_FLIP = 0x0020
        EFFECT_TYPE_IGNITION = 0x0040
        EFFECT_TYPE_TRIGGER_O = 0x0080
        EFFECT_TYPE_QUICK_O = 0x0100
        EFFECT_TYPE_TRIGGER_F = 0x0200
        EFFECT_TYPE_QUICK_F = 0x0400
        EFFECT_TYPE_CONTINUOUS = 0x0800
        EVENT_FREE_CHAIN = 1002
        LOCATION_DECK = 0x01
        LOCATION_HAND = 0x02
        LOCATION_MZONE = 0x04
        LOCATION_SZONE = 0x08
        LOCATION_GRAVE = 0x10
        LOCATION_REMOVED = 0x20
        LOCATION_EXTRA = 0x40
        CATEGORY_DESTROY = 0x1
        CATEGORY_DRAW = 0x10000
        CATEGORY_DAMAGE = 0x80000
        CATEGORY_RECOVER = 0x100000
        REASON_EFFECT = 0x40
        """);

    private static readonly byte[] FallbackUtilityLua = Encoding.UTF8.GetBytes("""
        function GetID()
            local offset = self_code < 100000000 and 1 or 100
            return self_table, self_code, offset
        end
        """);

    private static readonly byte[] FallbackProcedureLua = Encoding.UTF8.GetBytes("-- sandbox fallback procedure.lua\n");

    public bool TryGetScript(string scriptName, out byte[] content)
    {
        SandboxThreadOverlay? overlay = SandboxThreadOverlay.Current;
        if (overlay is not null)
        {
            string fileName = Path.GetFileName(scriptName);
            if (overlay.Scripts.TryGetValue(fileName, out content!) ||
                overlay.Scripts.TryGetValue(scriptName, out content!))
            {
                return true;
            }

            if (inner.TryGetScript(scriptName, out content))
                return true;

            if (fileName.Equals("constant.lua", StringComparison.OrdinalIgnoreCase))
            {
                content = FallbackConstantLua;
                return true;
            }
            if (fileName.Equals("utility.lua", StringComparison.OrdinalIgnoreCase))
            {
                content = FallbackUtilityLua;
                return true;
            }
            if (fileName.Equals("procedure.lua", StringComparison.OrdinalIgnoreCase))
            {
                content = FallbackProcedureLua;
                return true;
            }

            return false;
        }

        return inner.TryGetScript(scriptName, out content);
    }

    public void Dispose()
    {
        if (inner is IDisposable d)
            d.Dispose();
    }
}

/// <summary>
/// 进程内 <c>ocgcore</c> 单卡情景沙盒运行器（参考 YGODIY）：
/// 通过线程局部覆盖层隔离加载草稿卡片与 Lua 脚本，检验 <c>initial_effect</c>、首回合各阶段时点及常见激活回调是否抛出 Lua 错误。
/// </summary>
internal static class CardSandboxRunner
{
    private const int DummyFillerMonsterId = 99999990;

    public static CardSandboxResult Run(IDuelFactory? duelFactory, CardSandboxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Card);

        Stopwatch sw = Stopwatch.StartNew();

        // 1. 先执行静态安全审计，拦截危险 Lua 调用与非法编码
        if (!string.IsNullOrEmpty(request.LuaScript))
        {
            LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(request.LuaScript);
            if (!audit.IsSafe)
            {
                return new CardSandboxResult(
                    Success: false,
                    NativeExecuted: false,
                    ElapsedMs: Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                    StaticAuditViolations: audit.Violations,
                    LuaErrors: [],
                    ObservedMessages: [],
                    SummonableCount: 0,
                    SpSummonableCount: 0,
                    ActivatableCount: 0,
                    PostPlayer0Lp: 8000,
                    PostPlayer1Lp: 8000);
            }
        }

        if (duelFactory is null)
        {
            return new CardSandboxResult(
                Success: true,
                NativeExecuted: false,
                ElapsedMs: Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                StaticAuditViolations: [],
                LuaErrors: [],
                ObservedMessages: ["SkippedNativeRuntime"],
                SummonableCount: 0,
                SpSummonableCount: 0,
                ActivatableCount: 0,
                PostPlayer0Lp: 8000,
                PostPlayer1Lp: 8000);
        }

        // 2. 准备草稿卡片与草稿脚本线程隔离覆盖层
        SandboxThreadOverlay overlay = new();
        Card domainCard = request.Card.ToCard();
        RepositoryCardDataProvider singleProvider = new(new InMemoryCardRepository(
        [
            domainCard,
            new Card(
                DummyFillerMonsterId,
                0x3,
                0,
                0,
                (int)(CardType.Monster | CardType.Normal),
                4,
                0,
                0,
                (int)CardRace.Warrior,
                (int)CardAttribute.Light,
                1500,
                1200,
                0)
        ]));

        if (singleProvider.TryGetCardData((uint)domainCard.Id, out OcgCardData targetOcgData))
            overlay.Cards[(uint)domainCard.Id] = targetOcgData;
        if (singleProvider.TryGetCardData(DummyFillerMonsterId, out OcgCardData fillerOcgData))
            overlay.Cards[DummyFillerMonsterId] = fillerOcgData;

        if (request.LuaScript is not null)
            overlay.Scripts[$"c{domainCard.Id}.lua"] = Encoding.UTF8.GetBytes(request.LuaScript);

        List<string> luaErrors = new();
        List<string> observedMessages = new();
        int summonCount = 0;
        int spSummonCount = 0;
        int activateCount = 0;
        int[] lp = [8000, 8000];
        bool idleSeen = false;

        using (SandboxThreadOverlay.Push(overlay))
        {
            using IDuelSession duel = duelFactory.Create(0x59474F53U);
            duel.SetErrorHandler(err =>
            {
                if (!string.IsNullOrWhiteSpace(err))
                    luaErrors.Add(err.Trim());
            });

            duel.SetAnalyzer((GameMessage msg, ref SequenceReader<byte> reader, ReadOnlyMemory<byte> raw) =>
            {
                if (observedMessages.Count < 48)
                    observedMessages.Add(msg.ToString());

                CoreMessage cmsg = new(msg, raw);
                switch (msg)
                {
                    case GameMessage.Start:
                        return 2;
                    case GameMessage.NewTurn:
                        cmsg.Reader.Skip(1);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    case GameMessage.NewPhase:
                        cmsg.Reader.Skip(2);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    case GameMessage.Draw:
                    {
                        cmsg.Reader.Skip(1);
                        int drawCnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(drawCnt * 4);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    }
                    case GameMessage.Hint:
                        cmsg.Reader.Skip(6);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    case GameMessage.Move:
                    case GameMessage.Chaining:
                        cmsg.Reader.Skip(16);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    case GameMessage.PosChange:
                        cmsg.Reader.Skip(9);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    case GameMessage.Set:
                    case GameMessage.Summoning:
                    case GameMessage.SpSummoning:
                    case GameMessage.FlipSummoning:
                        cmsg.Reader.Skip(8);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    case GameMessage.Chained:
                    case GameMessage.ChainSolving:
                    case GameMessage.ChainSolved:
                    case GameMessage.ChainNegated:
                    case GameMessage.ChainDisabled:
                    case GameMessage.ShuffleDeck:
                    case GameMessage.RefreshDeck:
                        cmsg.Reader.Skip(1);
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    case GameMessage.ChainEnd:
                    case GameMessage.Summoned:
                    case GameMessage.SpSummoned:
                    case GameMessage.FlipSummoned:
                        return 0;
                    case GameMessage.Damage:
                    case GameMessage.Recover:
                    {
                        int pl = cmsg.Reader.ReadByte();
                        int val = cmsg.Reader.ReadInt32();
                        if (pl is 0 or 1)
                            lp[pl] += msg == GameMessage.Recover ? val : -val;
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    }
                    case GameMessage.PayLpCost:
                    {
                        int pl = cmsg.Reader.ReadByte();
                        int val = cmsg.Reader.ReadInt32();
                        if (pl is 0 or 1)
                            lp[pl] -= val;
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    }
                    case GameMessage.LpUpdate:
                    {
                        int pl = cmsg.Reader.ReadByte();
                        int val = cmsg.Reader.ReadInt32();
                        if (pl is 0 or 1)
                            lp[pl] = val;
                        reader.Advance(cmsg.Reader.Position);
                        return 0;
                    }
                    case GameMessage.SelectChain:
                    {
                        cmsg.Reader.Skip(1);
                        int cnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(9 + cnt * 14);
                        reader.Advance(cmsg.Reader.Position);
                        duel.SetResponse(-1);
                        return 0;
                    }
                    case GameMessage.SelectPlace:
                    case GameMessage.SelectDisfield:
                    {
                        int pl = cmsg.Reader.ReadByte();
                        int cnt = Math.Max(1, (int)cmsg.Reader.ReadByte());
                        uint flag = cmsg.Reader.ReadUInt32();
                        reader.Advance(cmsg.Reader.Position);

                        uint usable = ~flag;
                        byte[] resp = new byte[cnt * 3];
                        byte targetPl = (byte)pl;
                        byte loc = (byte)CardLocation.SpellZone;
                        byte seq = 0;

                        if ((usable & 0x7fU) != 0)
                        {
                            loc = (byte)CardLocation.MonsterZone;
                            seq = FirstBitIndex(usable & 0x7fU);
                        }
                        else if ((usable & 0x1f00U) != 0)
                        {
                            loc = (byte)CardLocation.SpellZone;
                            seq = FirstBitIndex((usable >> 8) & 0x1fU);
                        }
                        else if ((usable & 0x7f0000U) != 0)
                        {
                            targetPl = (byte)(1 - pl);
                            loc = (byte)CardLocation.MonsterZone;
                            seq = FirstBitIndex((usable >> 16) & 0x7fU);
                        }
                        else if ((usable & 0x1f000000U) != 0)
                        {
                            targetPl = (byte)(1 - pl);
                            loc = (byte)CardLocation.SpellZone;
                            seq = FirstBitIndex((usable >> 24) & 0x1fU);
                        }

                        if (cnt == 1)
                        {
                            resp[0] = targetPl;
                            resp[1] = loc;
                            resp[2] = seq;
                            duel.SetResponse(resp);
                            return 0;
                        }

                        return 1;
                    }
                    case GameMessage.SelectPosition:
                    {
                        cmsg.Reader.Skip(5);
                        int positions = cmsg.Reader.ReadByte();
                        reader.Advance(cmsg.Reader.Position);
                        int chosen = (positions & (int)CardPosition.FaceUpAttack) != 0
                            ? (int)CardPosition.FaceUpAttack
                            : FirstBitValue((uint)positions);
                        duel.SetResponse(chosen);
                        return 0;
                    }
                    case GameMessage.SelectIdleCmd:
                    {
                        cmsg.Reader.Skip(1);
                        int sCnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(sCnt * 7);
                        int spCnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(spCnt * 7);
                        int rpCnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(rpCnt * 7);
                        int msCnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(msCnt * 7);
                        int ssCnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(ssCnt * 7);
                        int actCnt = cmsg.Reader.ReadByte();
                        cmsg.Reader.Skip(actCnt * 11 + 3);
                        reader.Advance(cmsg.Reader.Position);

                        if (!idleSeen)
                        {
                            idleSeen = true;
                            summonCount = sCnt;
                            spSummonCount = spCnt;
                            activateCount = actCnt;
                        }

                        return 1;
                    }
                    default:
                        // 遇到需要交互选择（SelectCard / SelectYesNo / SelectPlace 等）说明脚本已成功推进到目标/代价选择阶段
                        return 1;
                }
            });

            duel.InitPlayers(8000, startHand: 2, drawCount: 1);

            bool isExtraMonster = domainCard.HasType(CardType.Fusion) ||
                                  domainCard.HasType(CardType.Synchro) ||
                                  domainCard.HasType(CardType.Xyz) ||
                                  domainCard.HasType(CardType.Link);

            if (isExtraMonster)
            {
                duel.AddCard(domainCard.Id, 0, CardLocation.Extra);
                duel.AddCard(DummyFillerMonsterId, 0, CardLocation.MonsterZone);
                duel.AddCard(DummyFillerMonsterId, 0, CardLocation.MonsterZone);
            }
            else
            {
                // 将目标 DIY 卡放入墓地与整个主卡组牌堆，确保无论洗切或正反序均稳定抽上手
                duel.AddCard(domainCard.Id, 0, CardLocation.Grave);
                for (int i = 0; i < 5; i++)
                    duel.AddCard(domainCard.Id, 0, CardLocation.Deck);
            }

            duel.AddCard(DummyFillerMonsterId, 1, CardLocation.MonsterZone);
            for (int i = 0; i < 5; i++)
                duel.AddCard(DummyFillerMonsterId, 1, CardLocation.Deck);

            // Master Rule 5 + 不洗切牌堆以便稳定把目标 DIY 卡抽入手牌
            const int mr5NoShuffleOptions = (5 << 16) | 0x10;
            duel.Start(mr5NoShuffleOptions);

            for (int step = 0; step < 8 && luaErrors.Count == 0 && !idleSeen; step++)
            {
                int res = duel.Process();
                if (res < 0)
                    break;
            }

            // 若在 Main1 检测到可发动效果且启用了试触发，自动试执行第 0 号 Activate 效果以覆盖 Cost/Target/Operation
            if (luaErrors.Count == 0 && idleSeen && activateCount > 0 && request.TestExecuteFirstActivation)
            {
                duel.SetResponse((0 << 16) | 5);
                for (int step = 0; step < 6 && luaErrors.Count == 0; step++)
                {
                    int res = duel.Process();
                    if (res != 0)
                        break;
                }
            }
        }

        return new CardSandboxResult(
            Success: luaErrors.Count == 0,
            NativeExecuted: true,
            ElapsedMs: Math.Round(sw.Elapsed.TotalMilliseconds, 2),
            StaticAuditViolations: [],
            LuaErrors: luaErrors,
            ObservedMessages: observedMessages,
            SummonableCount: summonCount,
            SpSummonableCount: spSummonCount,
            ActivatableCount: activateCount,
            PostPlayer0Lp: lp[0],
            PostPlayer1Lp: lp[1]);
    }

    private static byte FirstBitIndex(uint mask)
    {
        for (byte i = 0; i < 32; i++)
        {
            if (((mask >> i) & 1U) != 0)
                return i;
        }
        return 0;
    }

    private static int FirstBitValue(uint mask)
    {
        for (int i = 0; i < 32; i++)
        {
            if (((mask >> i) & 1U) != 0)
                return 1 << i;
        }
        return 1;
    }
}
