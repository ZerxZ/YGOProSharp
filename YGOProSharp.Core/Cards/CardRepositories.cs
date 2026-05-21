namespace YGOProSharp.Core.Cards;

/// <summary>
/// 只读卡片查询（read-only lookup），供卡组校验和 native card-data callback 使用。
/// </summary>
public interface ICardRepository
{
    bool TryGetCard(int id, out Card card);
}

/// <summary>
/// 带展示文本的只读卡片查询（named-card lookup），与 duel/runtime card data 保持分离。
/// </summary>
public interface INamedCardRepository
{
    bool TryGetCard(int id, out NamedCard card);

    IReadOnlyCollection<NamedCard> GetAllCards();
}

/// <summary>
/// 从持久化存储加载 card repository；SQLite 细节应留在该接口的实现内部。
/// </summary>
public interface ICardDatabaseManager
{
    ICardRepository LoadCards(string databaseFullPath);

    INamedCardRepository LoadNamedCards(string databaseFullPath);

    INamedCardRepository LoadNamedCards(IEnumerable<string> databaseFullPaths);
}

/// <summary>
/// 空 fallback repository，用于测试或刻意不加载卡片数据库的构造路径。
/// </summary>
public sealed class EmptyCardRepository : ICardRepository
{
    public static EmptyCardRepository Instance { get; } = new();

    private EmptyCardRepository()
    {
    }

    public bool TryGetCard(int id, out Card card)
    {
        card = null!;
        return false;
    }
}

/// <summary>
/// 数据库加载后生成的不可变内存 card repository（in-memory repository）。
/// </summary>
public sealed class InMemoryCardRepository : ICardRepository
{
    private readonly IReadOnlyDictionary<int, Card> _cards;

    public InMemoryCardRepository(IEnumerable<Card> cards)
    {
        Dictionary<int, Card> indexedCards = new();
        foreach (Card card in cards)
            indexedCards[card.Id] = card;

        _cards = indexedCards;
    }

    public bool TryGetCard(int id, out Card card)
    {
        return _cards.TryGetValue(id, out card!);
    }
}

/// <summary>
/// 名称与文本查询的空 fallback repository。
/// </summary>
public sealed class EmptyNamedCardRepository : INamedCardRepository
{
    public static EmptyNamedCardRepository Instance { get; } = new();

    private EmptyNamedCardRepository()
    {
    }

    public bool TryGetCard(int id, out NamedCard card)
    {
        card = null!;
        return false;
    }

    public IReadOnlyCollection<NamedCard> GetAllCards()
    {
        return Array.Empty<NamedCard>();
    }
}

/// <summary>
/// 数据库加载后生成的不可变内存 named-card repository。
/// </summary>
public sealed class InMemoryNamedCardRepository : INamedCardRepository
{
    private readonly IReadOnlyDictionary<int, NamedCard> _cards;

    public InMemoryNamedCardRepository(IEnumerable<NamedCard> cards)
    {
        Dictionary<int, NamedCard> indexedCards = new();
        foreach (NamedCard card in cards)
            indexedCards[card.Id] = card;

        _cards = indexedCards;
    }

    public bool TryGetCard(int id, out NamedCard card)
    {
        return _cards.TryGetValue(id, out card!);
    }

    public IReadOnlyCollection<NamedCard> GetAllCards()
    {
        return _cards.Values.ToArray();
    }
}
