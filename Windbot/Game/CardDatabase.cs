using YGOProSharp.Core.Cards;

namespace WindBot.Game;

public static class CardDatabase
{
    private static INamedCardRepository _repository = EmptyNamedCardRepository.Instance;

    public static INamedCardRepository Repository => _repository;

    public static void Initialize(INamedCardRepository repository)
    {
        _repository = repository ?? EmptyNamedCardRepository.Instance;
    }

    public static NamedCard Get(int id)
    {
        return _repository.TryGetCard(id, out NamedCard card) ? card : null;
    }

    public static IReadOnlyCollection<NamedCard> GetAllCards()
    {
        return _repository.GetAllCards();
    }
}
