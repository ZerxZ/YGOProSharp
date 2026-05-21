using System;
using System.Collections.Generic;
using System.IO;
using YGOProSharp.Core.Cards;

namespace WindBot.Game
{
    public class Deck
    {
        public IList<NamedCard> Cards { get; private set; }
        public IList<NamedCard> ExtraCards { get; private set; }
        public IList<NamedCard> SideCards { get; private set; }
        private readonly INamedCardRepository _cardRepository;

        public Deck()
            : this(CardDatabase.Repository)
        {
        }

        public Deck(INamedCardRepository cardRepository)
        {
            _cardRepository = cardRepository ?? EmptyNamedCardRepository.Instance;
            Cards = new List<NamedCard>();
            ExtraCards = new List<NamedCard>();
            SideCards = new List<NamedCard>();
        }

        private void AddNewCard(int cardId, bool sideDeck)
        {
            if (!_cardRepository.TryGetCard(cardId, out NamedCard newCard))
                return;

            if (!sideDeck)
                AddCard(newCard);
            else
                SideCards.Add(newCard);
        }

        private void AddCard(NamedCard card)
        {
            if (card.IsExtraCard())
                ExtraCards.Add(card);
            else
                Cards.Add(card);
        }

        public static Deck Load(string name)
        {
            return Load(name, CardDatabase.Repository);
        }

        public static Deck Load(string name, INamedCardRepository cardRepository)
        {
            StreamReader reader = null;
            try
            {
                reader = new StreamReader(Program.ReadFile("Decks", name, "ydk"));

                Deck deck = new Deck(cardRepository);
                bool side = false;

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                        continue;

                    line = line.Trim();
                    if (line.StartsWith("#"))
                        continue;
                    if (line.Equals("!side"))
                    {
                        side = true;
                        continue;
                    }

                    int id;
                    if (!int.TryParse(line, out id))
                        continue;

                    deck.AddNewCard(id, side);
                }

                reader.Close();

                if (deck.Cards.Count > 60)
                    return null;
                if (deck.ExtraCards.Count > 15)
                    return null;
                if (deck.SideCards.Count > 15)
                    return null;

                return deck;
            }
            catch (Exception)
            {
                reader?.Close();
                return null;
            }
        }
    }
}
