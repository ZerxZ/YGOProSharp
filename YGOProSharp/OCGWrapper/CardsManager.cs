using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using YGOProSharp.Abstractions.Ocg;

namespace YGOProSharp.Cards
{
    internal static class CardsManager
    {
        private static IDictionary<int, Card> _cards = new Dictionary<int, Card>();

        internal static void Init(string databaseFullPath)
        {
            _cards = new Dictionary<int, Card>();

            using (SqliteConnection connection = new SqliteConnection("Data Source=" + databaseFullPath))
            {
                connection.Open();

                using (IDbCommand command = new SqliteCommand("SELECT id, ot, alias, setcode, type, level, race, attribute, atk, def FROM datas", connection))
                {
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            LoadCard(reader);
                        }
                    }
                }
            }
        }

        internal static Card? GetCard(int id)
        {
            return _cards.TryGetValue(id, out Card? card) ? card : null;
        }

        private static void LoadCard(IDataRecord reader)
        {
            Card card = new Card(reader);
            _cards.Add(card.Id, card);
        }
    }

    public sealed class SqliteCardDataProvider : ICardDataProvider
    {
        public SqliteCardDataProvider(string databaseFullPath)
        {
            CardsManager.Init(databaseFullPath);
        }

        public bool TryGetCardData(uint code, out OcgCardData data)
        {
            Card? card = CardsManager.GetCard((int)code);
            if (card is null)
            {
                data = default;
                return false;
            }

            data = card.Data;
            return true;
        }
    }
}
