using System.Collections.Generic;
using System.Runtime.InteropServices;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Core
{
    public class Deck
    {
        private readonly ICardRepository _cardRepository;

        public IList<int> Main { get; private set; }
        public IList<int> Extra { get; private set; }
        public IList<int> Side { get; private set; }

        public Deck()
            : this(EmptyCardRepository.Instance)
        {
        }

        public Deck(ICardRepository cardRepository)
        {
            _cardRepository = cardRepository;
            Main = new List<int>();
            Extra = new List<int>();
            Side = new List<int>();
        }

        public void AddMain(int cardId)
        {
            if (!_cardRepository.TryGetCard(cardId, out Card card))
                return;
            if ((card.Type & (int)CardType.Token) != 0)
                return;
            if (card.IsExtraCard())
            {
                if (Extra.Count < 15)
                    Extra.Add(cardId);
            }
            else
            {
                if (Main.Count < 60)
                    Main.Add(cardId);
            }
        }

        public void AddSide(int cardId)
        {
            if (!_cardRepository.TryGetCard(cardId, out Card card))
                return;
            if ((card.Type & (int)CardType.Token) != 0)
                return;
            if (Side.Count < 15)
                Side.Add(cardId);
        }

        public int Check(Banlist? ban, bool ocg, bool tcg)
        {
            return Check(ban, ocg, tcg, allowCustom: true, DeckRules.Default);
        }

        public int Check(Banlist? ban, bool ocg, bool tcg, DeckRules rules)
        {
            return Check(ban, ocg, tcg, allowCustom: true, rules);
        }

        /// <summary>
        /// 校验卡组张数、区域合法性（OCG / TCG / DIY Custom）和禁限表数量限制。
        /// </summary>
        public int Check(Banlist? ban, bool ocg, bool tcg, bool allowCustom, DeckRules rules)
        {
            ArgumentNullException.ThrowIfNull(rules);

            if (Main.Count < rules.MainDeckMinSize ||
                Main.Count > rules.MainDeckMaxSize ||
                Extra.Count > rules.ExtraDeckMaxSize ||
                Side.Count > rules.SideDeckMaxSize)
                return 1;

            Dictionary<int, int> cards = new(Main.Count + Extra.Count + Side.Count);

            int invalidCard = CheckStack(Main, cards, ocg, tcg, allowCustom);
            if (invalidCard != 0)
                return invalidCard;
            invalidCard = CheckStack(Extra, cards, ocg, tcg, allowCustom);
            if (invalidCard != 0)
                return invalidCard;
            invalidCard = CheckStack(Side, cards, ocg, tcg, allowCustom);
            if (invalidCard != 0)
                return invalidCard;

            if (ban == null)
                return 0;

            foreach (KeyValuePair<int, int> pair in cards)
            {
                int max = ban.GetQuantity(pair.Key);
                if (pair.Value > max)
                    return pair.Key;
            }

            return 0;
        }

        public bool Check(Deck deck)
        {
            if (deck.Main.Count != Main.Count || deck.Extra.Count != Extra.Count || deck.Side.Count != Side.Count)
                return false;

            Dictionary<int, int> cards = new(Main.Count + Extra.Count + Side.Count);
            CountStack(Main, cards);
            CountStack(Extra, cards);
            CountStack(Side, cards);

            Dictionary<int, int> ncards = new(deck.Main.Count + deck.Extra.Count + deck.Side.Count);
            CountStack(deck.Main, ncards);
            CountStack(deck.Extra, ncards);
            CountStack(deck.Side, ncards);

            if (cards.Count != ncards.Count)
                return false;

            foreach (KeyValuePair<int, int> pair in cards)
            {
                if (!ncards.TryGetValue(pair.Key, out int count) || count != pair.Value)
                    return false;
            }
            return true;
        }

        private int CheckStack(IList<int> stack, Dictionary<int, int> cards, bool ocg, bool tcg, bool allowCustom)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                int id = stack[i];
                if (!_cardRepository.TryGetCard(id, out Card card))
                    continue;

                AddToCards(cards, card);
                if (!ocg && card.Ot == 1 || !tcg && card.Ot == 2)
                    return id;
                // YGOPro/EDOPro 约定 ot & 0x4 表示 DIY/Custom 卡片。
                if (!allowCustom && (card.Ot & 0x4) != 0)
                    return id;
            }

            return 0;
        }

        private static void CountStack(IList<int> stack, Dictionary<int, int> counts)
        {
            for (int i = 0; i < stack.Count; i++)
                CollectionsMarshal.GetValueRefOrAddDefault(counts, stack[i], out _)++;
        }

        private static void AddToCards(Dictionary<int, int> cards, Card card)
        {
            int id = card.Alias != 0 ? card.Alias : card.Id;
            CollectionsMarshal.GetValueRefOrAddDefault(cards, id, out _)++;
        }
    }
}
