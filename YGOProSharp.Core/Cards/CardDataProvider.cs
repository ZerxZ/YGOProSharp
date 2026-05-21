using YGOProSharp.Abstractions.Ocg;

namespace YGOProSharp.Core.Cards;

/// <summary>
/// 连接领域 card repository 与 native callback contract，避免把 SQLite 或 Card 暴露给 NativeApi。
/// </summary>
public sealed class RepositoryCardDataProvider : ICardDataProvider
{
    private readonly ICardRepository _cardRepository;

    public RepositoryCardDataProvider(ICardRepository cardRepository)
    {
        _cardRepository = cardRepository;
    }

    public bool TryGetCardData(uint code, out OcgCardData data)
    {
        if (!_cardRepository.TryGetCard((int)code, out Card card))
        {
            data = default;
            return false;
        }

        data = CardDataMapper.ToOcgCardData(card);
        return true;
    }
}

/// <summary>
/// 将领域 card data 转换为 ocgcore 期望的紧凑布局（compact layout）。
/// </summary>
public static class CardDataMapper
{
    public static OcgCardData ToOcgCardData(Card card)
    {
        return OcgCardData.Create(
            (uint)card.Id,
            (uint)card.Alias,
            unchecked((ulong)card.Setcode),
            (uint)card.Type,
            (uint)card.Level,
            (uint)card.Attribute,
            (uint)card.Race,
            card.Attack,
            card.Defense,
            (uint)card.LScale,
            (uint)card.RScale,
            (uint)card.LinkMarker);
    }
}
