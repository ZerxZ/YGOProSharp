using System.Collections.Generic;
using System.Data;
using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace YGOProSharp.OCGWrapper
{
    public static class NamedCardsManager
    {
        private static IDictionary<int, NamedCard> _cards = new Dictionary<int, NamedCard>();

        public static void Init(string databaseFullPath)
        {
            try
            {
                if (!File.Exists(databaseFullPath))
                {
                    throw new Exception("Could not find the cards database.");
                }

                _cards = new Dictionary<int, NamedCard>();

                using (SqliteConnection connection = new SqliteConnection("Data Source=" + databaseFullPath))
                {
                    connection.Open();

                    using (IDbCommand command = new SqliteCommand(
                        "SELECT datas.id, ot, alias, setcode, type, level, race, attribute, atk, def, texts.name, texts.desc"
                        + " FROM datas INNER JOIN texts ON datas.id = texts.id",
                        connection))
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
            catch (Exception ex)
            {
                throw new Exception("Could not initialize the cards database. Check the inner exception for more details.", ex);
            }
        }

        public static void InitForMulti(List<string> databaseFullPaths)
        {
            try
            {
                _cards = new Dictionary<int, NamedCard>();

                foreach (string databaseFullPath in databaseFullPaths)
                {
                    if (!File.Exists(databaseFullPath))
                        continue;

                    using (SqliteConnection connection = new SqliteConnection("Data Source=" + databaseFullPath))
                    {
                        connection.Open();

                        using (IDbCommand command = new SqliteCommand(
                            "SELECT datas.id, ot, alias, setcode, type, level, race, attribute, atk, def, texts.name, texts.desc"
                            + " FROM datas INNER JOIN texts ON datas.id = texts.id",
                            connection))
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
            }
            catch (Exception ex)
            {
                throw new Exception("Could not initialize the cards database. Check the inner exception for more details.", ex);
            }
        }

        internal static NamedCard? GetCard(int id)
        {
            return _cards.TryGetValue(id, out NamedCard? card) ? card : null;
        }

        internal static IList<NamedCard> GetAllCards()
        {
            var returnValue = new List<NamedCard>();
            foreach (NamedCard card in _cards.Values)
                returnValue.Add(card);
            return returnValue;
        }

        private static void LoadCard(IDataRecord reader)
        {
            NamedCard card = new NamedCard(reader);
            _cards[card.Id] = card;
        }
    }
}
