using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace YGOProSharp.Core.Cards;

/// <summary>
/// 只读卡片查询（read-only lookup），供卡组校验、随机组卡和 native card-data callback 使用。
/// </summary>
public interface ICardRepository
{
    bool TryGetCard(int id, out Card card);

    IReadOnlyCollection<Card> GetAllCards();
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
/// <c>.cdb</c> 卡片数据库结构校验结果。
/// </summary>
public sealed record CardDatabaseValidationResult(
    bool IsValid,
    int TotalCards,
    int CustomCards,
    IReadOnlyList<int> CardIds,
    string? ErrorMessage = null);

/// <summary>
/// 用于 DIY 工作台读写完整卡片属性与文本的领域 DTO（覆盖标准 <c>datas</c> 与 <c>texts</c> 全部字段）。
/// </summary>
public sealed record DiyCardEntry
{
    public required int Id { get; init; }
    public int Ot { get; init; } = 0x4;
    public int Alias { get; init; }
    public long Setcode { get; init; }
    public int Type { get; init; } = 0x21;
    public int Level { get; init; } = 4;
    public int LScale { get; init; }
    public int RScale { get; init; }
    public int Race { get; init; } = 1;
    public int Attribute { get; init; } = 1;
    public int Attack { get; init; }
    public int Defense { get; init; }
    public int LinkMarker { get; init; }
    public long Category { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Desc { get; init; } = string.Empty;
    public IReadOnlyList<string> Strings { get; init; } = [];

    public Card ToCard() => new(
        Id,
        Ot,
        Alias,
        Setcode,
        Type,
        Level,
        LScale,
        RScale,
        Race,
        Attribute,
        Attack,
        Defense,
        LinkMarker);

    public NamedCard ToNamedCard() => new(
        Id,
        Ot,
        Alias,
        Setcode,
        Type,
        Level,
        LScale,
        RScale,
        Race,
        Attribute,
        Attack,
        Defense,
        LinkMarker,
        Name,
        Desc);
}

/// <summary>
/// 从持久化存储加载 card repository；SQLite 细节应留在该接口的实现内部。
/// </summary>
public interface ICardDatabaseManager
{
    ICardRepository LoadCards(string databaseFullPath);

    /// <summary>
    /// 按顺序加载多个卡片数据库（靠后的数据库覆盖前面同 ID 的卡片记录，便于 expansions / diy 覆盖基础卡库）。
    /// </summary>
    ICardRepository LoadCards(IEnumerable<string> databaseFullPaths);

    INamedCardRepository LoadNamedCards(string databaseFullPath);

    INamedCardRepository LoadNamedCards(IEnumerable<string> databaseFullPaths);

    /// <summary>
    /// 校验单个 <c>.cdb</c> 文件的表结构与数据可读性，并返回包含的卡密列表以供冲突分析。
    /// </summary>
    CardDatabaseValidationResult ValidateDatabase(string databaseFullPath);

    /// <summary>
    /// 读取单个 <c>.cdb</c> 内的全部可编辑卡片条目（含 <c>category</c> 与 <c>str1..str16</c> 提示文本）。
    /// </summary>
    IReadOnlyList<DiyCardEntry> LoadEditableCards(string databaseFullPath);

    /// <summary>
    /// 通过工作副本（Working Copy）创建或更新单张卡片记录，校验通过后原子替换目标 <c>.cdb</c>。
    /// </summary>
    CardDatabaseValidationResult UpsertCard(string databaseFullPath, DiyCardEntry card);

    /// <summary>
    /// 通过工作副本从目标 <c>.cdb</c> 删除指定卡密，校验通过后原子替换。
    /// </summary>
    CardDatabaseValidationResult DeleteCard(string databaseFullPath, int cardId);
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

    public IReadOnlyCollection<Card> GetAllCards()
    {
        return [];
    }
}

/// <summary>
/// 数据库加载后生成的不可变内存 card repository（in-memory repository）。
/// </summary>
public sealed class InMemoryCardRepository : ICardRepository
{
    private readonly FrozenDictionary<int, Card> _cards;
    private readonly Card[] _allCards;

    public InMemoryCardRepository(IEnumerable<Card> cards)
    {
        Dictionary<int, Card> indexedCards = new();
        foreach (Card card in cards)
            indexedCards[card.Id] = card;

        _cards = indexedCards.ToFrozenDictionary();
        _allCards = [.. indexedCards.Values];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCard(int id, out Card card)
    {
        return _cards.TryGetValue(id, out card!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyCollection<Card> GetAllCards()
    {
        return _allCards;
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
        return [];
    }
}

/// <summary>
/// 数据库加载后生成的不可变内存 named-card repository。
/// </summary>
public sealed class InMemoryNamedCardRepository : INamedCardRepository
{
    private readonly FrozenDictionary<int, NamedCard> _cards;
    private readonly NamedCard[] _allCards;

    public InMemoryNamedCardRepository(IEnumerable<NamedCard> cards)
    {
        Dictionary<int, NamedCard> indexedCards = new();
        foreach (NamedCard card in cards)
            indexedCards[card.Id] = card;

        _cards = indexedCards.ToFrozenDictionary();
        _allCards = [.. indexedCards.Values];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCard(int id, out NamedCard card)
    {
        return _cards.TryGetValue(id, out card!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyCollection<NamedCard> GetAllCards()
    {
        return _allCards;
    }
}

/// <summary>
/// 按优先级组合多个 <see cref="ICardRepository"/>（列表越靠前优先级越高），
/// 用于在基础 <c>cards.cdb</c> 之上叠加内存 DIY 卡或扩展卡库。
/// </summary>
public sealed class CompositeCardRepository : ICardRepository
{
    private readonly ICardRepository[] _repositories;
    private readonly Lazy<Card[]> _allCards;

    public CompositeCardRepository(params ICardRepository[] repositories)
        : this((IEnumerable<ICardRepository>)repositories)
    {
    }

    public CompositeCardRepository(IEnumerable<ICardRepository> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        _repositories = repositories.Where(static repo => repo is not null).ToArray();
        _allCards = new Lazy<Card[]>(BuildMergedCards);
    }

    public bool TryGetCard(int id, out Card card)
    {
        for (int i = 0; i < _repositories.Length; i++)
        {
            if (_repositories[i].TryGetCard(id, out card))
                return true;
        }

        card = null!;
        return false;
    }

    public IReadOnlyCollection<Card> GetAllCards()
    {
        return _allCards.Value;
    }

    private Card[] BuildMergedCards()
    {
        Dictionary<int, Card> merged = new();
        for (int i = _repositories.Length - 1; i >= 0; i--)
        {
            foreach (Card card in _repositories[i].GetAllCards())
                merged[card.Id] = card;
        }

        return [.. merged.Values];
    }
}

/// <summary>
/// 支持运行时原子热重载的卡片仓库包装器；每次 <see cref="Reload"/> 自动递增 <see cref="CatalogVersion"/>，
/// 供上层缓存键空间与随机卡组池无锁切换。
/// </summary>
public sealed class ReloadableCardRepository : ICardRepository
{
    private volatile ICardRepository _current;
    private long _catalogVersion = 1;

    public ReloadableCardRepository(ICardRepository initialRepository)
    {
        ArgumentNullException.ThrowIfNull(initialRepository);
        _current = initialRepository;
    }

    public ICardRepository Current => _current;

    public long CatalogVersion => Volatile.Read(ref _catalogVersion);

    public event Action<long>? Reloaded;

    public long Reload(ICardRepository nextRepository)
    {
        ArgumentNullException.ThrowIfNull(nextRepository);
        _current = nextRepository;
        long nextVersion = Interlocked.Increment(ref _catalogVersion);
        Reloaded?.Invoke(nextVersion);
        return nextVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCard(int id, out Card card)
    {
        return _current.TryGetCard(id, out card);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyCollection<Card> GetAllCards()
    {
        return _current.GetAllCards();
    }
}

/// <summary>
/// 按优先级组合多个 <see cref="INamedCardRepository"/>（列表越靠前优先级越高）。
/// </summary>
public sealed class CompositeNamedCardRepository : INamedCardRepository
{
    private readonly INamedCardRepository[] _repositories;
    private readonly Lazy<NamedCard[]> _allCards;

    public CompositeNamedCardRepository(params INamedCardRepository[] repositories)
        : this((IEnumerable<INamedCardRepository>)repositories)
    {
    }

    public CompositeNamedCardRepository(IEnumerable<INamedCardRepository> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        _repositories = repositories.Where(static repo => repo is not null).ToArray();
        _allCards = new Lazy<NamedCard[]>(BuildMergedCards);
    }

    public bool TryGetCard(int id, out NamedCard card)
    {
        for (int i = 0; i < _repositories.Length; i++)
        {
            if (_repositories[i].TryGetCard(id, out card))
                return true;
        }

        card = null!;
        return false;
    }

    public IReadOnlyCollection<NamedCard> GetAllCards()
    {
        return _allCards.Value;
    }

    private NamedCard[] BuildMergedCards()
    {
        Dictionary<int, NamedCard> merged = new();
        // 从低优先级到高优先级写入，确保高优先级 repository 覆盖同 ID 卡片。
        for (int i = _repositories.Length - 1; i >= 0; i--)
        {
            foreach (NamedCard card in _repositories[i].GetAllCards())
                merged[card.Id] = card;
        }

        return [.. merged.Values];
    }
}
