using YGOProSharp.Core;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Server;

/// <summary>
/// 统一预设卡组目录与房间禁限兼容性服务（供 Server、Web 门户与 MCP 共享，消除多处重复发现与禁限计算逻辑）。
/// </summary>
public sealed class PresetDeckCatalogService
{
    private readonly object _bundledGate = new();
    private PresetDeckEntry[]? _cachedBundledDecks;

    public IReadOnlyList<PresetDeckEntry> GetCombinedPresetDecks(CoreServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        IReadOnlyList<PresetDeckEntry> serverPool = server.PresetDecks.Entries;
        PresetDeckEntry[] bundled = GetBundledWindBotDecks(server.CardRepository);

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

    public PresetDeckEntry? FindByNameOrId(CoreServer server, string? nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
            return null;

        string trimmed = nameOrId.Trim();
        string aiPrefixed = trimmed.StartsWith("AI_", StringComparison.OrdinalIgnoreCase) ? trimmed : $"AI_{trimmed}";
        foreach (PresetDeckEntry d in GetCombinedPresetDecks(server))
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

    public static (Banlist? Banlist, bool Ocg, bool Tcg, bool AllowCustom, bool NoCheck) ExtractRoomDeckConstraints(Game? room)
    {
        if (room is null)
            return (null, true, true, true, false);

        int region = room.Region;
        bool ocg = region != 1;
        bool tcg = region != 0;
        bool allowCustom = region >= 3;
        return (room.Banlist, ocg, tcg, allowCustom, room.NoCheckDeck);
    }

    public static bool IsDeckCompatibleWithRoom(YdkDeckData data, Game? room, ICardRepository cardRepo)
    {
        Deck deck = YdkSerializer.ToDeck(data, cardRepo);
        if (deck.Main.Count < 40)
            return false;

        if (room is null || room.NoCheckDeck)
            return true;

        (Banlist? banlist, bool ocg, bool tcg, bool allowCustom, _) = ExtractRoomDeckConstraints(room);
        return deck.Check(banlist, ocg, tcg, allowCustom, DeckRules.Default) == 0;
    }

    private PresetDeckEntry[] GetBundledWindBotDecks(ICardRepository cardRepo)
    {
        if (_cachedBundledDecks is not null)
            return _cachedBundledDecks;

        lock (_bundledGate)
        {
            if (_cachedBundledDecks is not null)
                return _cachedBundledDecks;

            PresetDeckPool tempPool = new();
            tempPool.ReloadFromDirectories(
                [Path.Combine(AppContext.BaseDirectory, "Decks"), "Decks"],
                cardRepo,
                additionalTags: ["windbot"]);
            _cachedBundledDecks = tempPool.Entries.ToArray();
            return _cachedBundledDecks;
        }
    }
}
