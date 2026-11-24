using System.Globalization;
using YGOProSharp.Core;

namespace YGOProSharp.Server;

/// <summary>
/// 从房间密码前缀解析出的规则覆写项（参考主流 srvpro2 房号语法）。
/// </summary>
public sealed class RoomRuleOverrides
{
    public int? Mode { get; set; }
    public int? Region { get; set; }
    public int? MasterRule { get; set; }
    public int? BanlistIndex { get; set; }
    public bool? NoBanlist { get; set; }
    public int? StartLp { get; set; }
    public int? StartHand { get; set; }
    public int? DrawCount { get; set; }
    public int? GameTimer { get; set; }
    public bool? EnablePriority { get; set; }
    public bool? NoCheckDeck { get; set; }
    public bool? NoShuffleDeck { get; set; }
    public int? MatchWins { get; set; }
    public RandomDeckMode? RandomDeck { get; set; }
    public int? ChaosMainDeckSize { get; set; }
    public bool? SuddenDeath { get; set; }
    public List<string> ExtraScripts { get; } = new();
    public Dictionary<string, string> CustomTags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasAnyOverride =>
        Mode.HasValue ||
        Region.HasValue ||
        MasterRule.HasValue ||
        BanlistIndex.HasValue ||
        NoBanlist.HasValue ||
        StartLp.HasValue ||
        StartHand.HasValue ||
        DrawCount.HasValue ||
        GameTimer.HasValue ||
        EnablePriority.HasValue ||
        NoCheckDeck.HasValue ||
        NoShuffleDeck.HasValue ||
        MatchWins.HasValue ||
        RandomDeck.HasValue ||
        ChaosMainDeckSize.HasValue ||
        SuddenDeath.HasValue ||
        ExtraScripts.Count > 0 ||
        CustomTags.Count > 0;

    /// <summary>
    /// 在基础 <see cref="GameOptions"/> 上应用本次解析出的规则覆写，返回新的配置实例。
    /// </summary>
    public GameOptions ApplyTo(GameOptions baseOptions)
    {
        ArgumentNullException.ThrowIfNull(baseOptions);

        int mode = Mode ?? baseOptions.Mode;
        int defaultStartLp = baseOptions.StartLp;
        if (Mode == 2 && baseOptions.Mode != 2)
            defaultStartLp = baseOptions.StartLp * 2;
        else if (Mode.HasValue && Mode.Value != 2 && baseOptions.Mode == 2 && baseOptions.StartLp == Game.DEFAULT_TAG_LIFEPOINTS)
            defaultStartLp = Game.DEFAULT_LIFEPOINTS;

        int startLp = StartLp ?? defaultStartLp;
        int banlist = NoBanlist == true
            ? -1
            : (BanlistIndex ?? baseOptions.Banlist);

        IReadOnlyList<string> extraScripts = ExtraScripts.Count == 0
            ? baseOptions.ExtraScripts
            : baseOptions.ExtraScripts.Concat(ExtraScripts).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        IReadOnlyDictionary<string, string>? customTags = baseOptions.CustomTags;
        if (CustomTags.Count > 0)
        {
            Dictionary<string, string> merged = baseOptions.CustomTags is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(baseOptions.CustomTags, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in CustomTags)
                merged[pair.Key] = pair.Value;
            customTags = merged;
        }

        return baseOptions with
        {
            Mode = mode,
            Region = Region ?? baseOptions.Region,
            MasterRule = MasterRule ?? baseOptions.MasterRule,
            Banlist = banlist,
            StartLp = startLp,
            StartHand = StartHand ?? baseOptions.StartHand,
            DrawCount = DrawCount ?? baseOptions.DrawCount,
            GameTimer = GameTimer ?? baseOptions.GameTimer,
            EnablePriority = EnablePriority ?? baseOptions.EnablePriority,
            NoCheckDeck = NoCheckDeck ?? baseOptions.NoCheckDeck,
            NoShuffleDeck = NoShuffleDeck ?? baseOptions.NoShuffleDeck,
            MatchWins = MatchWins ?? baseOptions.MatchWins,
            RandomDeck = RandomDeck ?? baseOptions.RandomDeck,
            ChaosMainDeckSize = ChaosMainDeckSize ?? baseOptions.ChaosMainDeckSize,
            SuddenDeath = SuddenDeath ?? baseOptions.SuddenDeath,
            ExtraScripts = extraScripts,
            CustomTags = customTags
        };
    }
}

/// <summary>
/// 房间密码解析结果。
/// </summary>
public sealed record ParsedRoomPassword(
    string RawPassword,
    string RoomName,
    bool HasRulePrefix,
    RoomRuleOverrides Overrides,
    IReadOnlyList<string> MatchedTokens,
    IReadOnlyList<string> UnknownTokens)
{
    public GameOptions ApplyTo(GameOptions baseOptions) => Overrides.ApplyTo(baseOptions);
}

/// <summary>
/// 可扩展的房间密码规则解析器（支持 <c>M#</c>、<c>T#</c>、<c>DIY#</c>、<c>LP4000,MR5,NC,NS,LF1#room</c> 等，
/// 并允许宿主注册自定义 DIY 模式 token）。
/// </summary>
public sealed class RoomPasswordParser
{
    public static RoomPasswordParser Default { get; } = new();

    private readonly Dictionary<string, Action<RoomRuleOverrides, string>> _exactTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Prefix, Action<RoomRuleOverrides, string> Handler)> _prefixTokens = new();

    public RoomPasswordParser()
    {
        RegisterBuiltInTokens();
    }

    /// <summary>
    /// 注册一个或多个精确匹配的规则关键字（例如 <c>SPEED</c>、<c>BOSS</c>）。
    /// </summary>
    public RoomPasswordParser RegisterToken(IEnumerable<string> aliases, Action<RoomRuleOverrides, string> handler)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(handler);

        foreach (string alias in aliases)
        {
            string trimmed = alias.Trim();
            if (trimmed.Length > 0)
                _exactTokens[trimmed] = handler;
        }

        return this;
    }

    /// <summary>
    /// 注册带数值或参数后缀的前缀规则（例如 <c>LP</c>、<c>MR</c>、<c>SCRIPT:</c>）。
    /// </summary>
    public RoomPasswordParser RegisterPrefixToken(string prefix, Action<RoomRuleOverrides, string> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(handler);

        string normalized = prefix.Trim();
        _prefixTokens.RemoveAll(item => string.Equals(item.Prefix, normalized, StringComparison.OrdinalIgnoreCase));
        _prefixTokens.Add((normalized, handler));
        // 更长的前缀优先匹配，避免短前缀吞掉长前缀。
        _prefixTokens.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        return this;
    }

    public ParsedRoomPassword Parse(string? rawPassword)
    {
        string raw = rawPassword?.Trim() ?? string.Empty;
        RoomRuleOverrides overrides = new();
        List<string> matched = new();
        List<string> unknown = new();

        int hashIndex = raw.IndexOf('#');
        if (hashIndex < 0)
        {
            return new ParsedRoomPassword(raw, raw, HasRulePrefix: false, overrides, matched, unknown);
        }

        string prefixPart = raw[..hashIndex].Trim();
        string roomName = raw[(hashIndex + 1)..].Trim();
        if (prefixPart.Length > 0)
        {
            string[] tokens = prefixPart.Split([',', '，', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (token.Length == 0)
                    continue;

                if (_exactTokens.TryGetValue(token, out Action<RoomRuleOverrides, string>? exactHandler))
                {
                    exactHandler(overrides, token);
                    matched.Add(token);
                    continue;
                }

                bool matchedPrefix = false;
                for (int i = 0; i < _prefixTokens.Count; i++)
                {
                    (string prefix, Action<RoomRuleOverrides, string> handler) = _prefixTokens[i];
                    if (token.Length > prefix.Length && token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string arg = token[prefix.Length..];
                        handler(overrides, arg);
                        matched.Add(token);
                        matchedPrefix = true;
                        break;
                    }
                }

                if (!matchedPrefix)
                    unknown.Add(token);
            }
        }

        return new ParsedRoomPassword(raw, roomName, HasRulePrefix: true, overrides, matched, unknown);
    }

    private void RegisterBuiltInTokens()
    {
        RegisterToken(["S", "SINGLE", "1V1", "1VS1"], static (o, _) => o.Mode = 0);
        RegisterToken(["M", "MATCH"], static (o, _) => o.Mode = 1);
        RegisterToken(["T", "TAG", "2V2", "2VS2"], static (o, _) => o.Mode = 2);

        RegisterToken(["OO", "OCG"], static (o, _) => o.Region = 0);
        RegisterToken(["TO", "TCG"], static (o, _) => o.Region = 1);
        RegisterToken(["OT"], static (o, _) => o.Region = 2);
        RegisterToken(["DIY", "CUSTOM"], static (o, _) => o.Region = 3);
        RegisterToken(["NU", "NOEXCLUSIVE"], static (o, _) => o.Region = 4);
        RegisterToken(["ALL"], static (o, _) => o.Region = 5);

        RegisterToken(["PR", "PRIORITY"], static (o, _) => o.EnablePriority = true);
        RegisterToken(["NC", "NOCHECK"], static (o, _) => o.NoCheckDeck = true);
        RegisterToken(["NS", "NOSHUFFLE"], static (o, _) => o.NoShuffleDeck = true);
        RegisterToken(["ED", "DEATH", "SUDDENDEATH"], static (o, _) => o.SuddenDeath = true);
        RegisterToken(["NF", "NOLFLIST"], static (o, _) =>
        {
            o.NoBanlist = true;
            o.BanlistIndex = -1;
        });

        RegisterToken(["RD", "RAND"], static (o, _) => o.RandomDeck = RandomDeckMode.PresetRandom);
        RegisterToken(["RDM", "MIRROR"], static (o, _) => o.RandomDeck = RandomDeckMode.MirrorRandom);
        RegisterToken(["CHAOS"], static (o, _) => o.RandomDeck = RandomDeckMode.ChaosDraft);
        RegisterToken(["PURECHAOS"], static (o, _) => o.RandomDeck = RandomDeckMode.PureChaos);
        RegisterToken(["CHAOS60"], static (o, _) =>
        {
            o.RandomDeck = RandomDeckMode.ChaosDraft;
            o.ChaosMainDeckSize = 60;
        });
        RegisterToken(["DIYRD"], static (o, _) =>
        {
            o.RandomDeck = RandomDeckMode.DiyRandom;
            o.Region ??= 3;
        });
        RegisterToken(["DIYCHAOS"], static (o, _) =>
        {
            o.RandomDeck = RandomDeckMode.DiyChaos;
            o.Region ??= 3;
        });

        RegisterPrefixToken("LF", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 1)
            {
                o.NoBanlist = false;
                o.BanlistIndex = value - 1;
            }
        });
        RegisterPrefixToken("MR", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                o.MasterRule = Math.Clamp(value, 1, 5);
        });
        RegisterPrefixToken("LP", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                o.StartLp = Math.Clamp(value, 1, 999999);
        });
        RegisterPrefixToken("TM", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                o.GameTimer = Math.Clamp(value, 0, 9999);
        });
        RegisterPrefixToken("TI", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                o.GameTimer = Math.Clamp(value, 0, 9999);
        });
        RegisterPrefixToken("ST", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                o.StartHand = Math.Clamp(value, 0, 40);
        });
        RegisterPrefixToken("DR", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                o.DrawCount = Math.Clamp(value, 0, 35);
        });
        RegisterPrefixToken("BO", static (o, arg) =>
        {
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bo) && bo >= 1)
            {
                int winsNeeded = (bo / 2) + 1;
                o.MatchWins = Math.Clamp(winsNeeded, 1, 9);
                o.Mode = bo == 1 ? 0 : 1;
            }
        });
    }
}
