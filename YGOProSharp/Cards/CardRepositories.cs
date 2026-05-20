namespace YGOProSharp.Cards;

public interface ICardRepository
{
    bool TryGetCard(int id, out Card card);
}

public interface INamedCardRepository
{
    bool TryGetCard(int id, out NamedCard card);

    IReadOnlyCollection<NamedCard> GetAllCards();
}

public interface ICardDatabaseManager
{
    ICardRepository LoadCards(string databaseFullPath);

    INamedCardRepository LoadNamedCards(string databaseFullPath);

    INamedCardRepository LoadNamedCards(IEnumerable<string> databaseFullPaths);
}

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
