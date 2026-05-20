using Microsoft.Data.Sqlite;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.Cards;

public sealed class SqliteCardDatabaseManager : ICardDatabaseManager
{
    private const string CardQuery = "SELECT id, ot, alias, setcode, type, level, race, attribute, atk, def FROM datas";
    private const string NamedCardQuery =
        "SELECT datas.id, ot, alias, setcode, type, level, race, attribute, atk, def, texts.name, texts.desc" +
        " FROM datas INNER JOIN texts ON datas.id = texts.id";

    public ICardRepository LoadCards(string databaseFullPath)
    {
        EnsureDatabaseExists(databaseFullPath);

        List<Card> cards = new();
        using SqliteConnection connection = OpenConnection(databaseFullPath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = CardQuery;

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            cards.Add(ReadCard(reader));

        return new InMemoryCardRepository(cards);
    }

    public INamedCardRepository LoadNamedCards(string databaseFullPath)
    {
        EnsureDatabaseExists(databaseFullPath);
        return LoadNamedCardsCore(new[] { databaseFullPath });
    }

    public INamedCardRepository LoadNamedCards(IEnumerable<string> databaseFullPaths)
    {
        return LoadNamedCardsCore(databaseFullPaths.Where(File.Exists));
    }

    private static INamedCardRepository LoadNamedCardsCore(IEnumerable<string> databaseFullPaths)
    {
        List<NamedCard> cards = new();
        foreach (string databaseFullPath in databaseFullPaths)
        {
            using SqliteConnection connection = OpenConnection(databaseFullPath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = NamedCardQuery;

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                cards.Add(ReadNamedCard(reader));
        }

        return cards.Count == 0
            ? EmptyNamedCardRepository.Instance
            : new InMemoryNamedCardRepository(cards);
    }

    private static SqliteConnection OpenConnection(string databaseFullPath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databaseFullPath,
            Mode = SqliteOpenMode.ReadOnly
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    private static Card ReadCard(SqliteDataReader reader)
    {
        int id = reader.GetInt32(0);
        int ot = reader.GetInt32(1);
        int alias = reader.GetInt32(2);
        long setcode = reader.GetInt64(3);
        int type = reader.GetInt32(4);
        int levelInfo = reader.GetInt32(5);
        int race = reader.GetInt32(6);
        int attribute = reader.GetInt32(7);
        int attack = reader.GetInt32(8);
        int defense = reader.GetInt32(9);

        return CreateCard(id, ot, alias, setcode, type, levelInfo, race, attribute, attack, defense);
    }

    private static NamedCard ReadNamedCard(SqliteDataReader reader)
    {
        Card card = ReadCard(reader);
        return new NamedCard(
            card.Id,
            card.Ot,
            card.Alias,
            card.Setcode,
            card.Type,
            card.Level,
            card.LScale,
            card.RScale,
            card.Race,
            card.Attribute,
            card.Attack,
            card.Defense,
            card.LinkMarker,
            reader.GetString(10),
            reader.GetString(11));
    }

    private static Card CreateCard(int id, int ot, int alias, long setcode, int type, int levelInfo, int race, int attribute, int attack, int defense)
    {
        int level = levelInfo & 0xff;
        int lScale = (levelInfo >> 24) & 0xff;
        int rScale = (levelInfo >> 16) & 0xff;
        int linkMarker = 0;

        if ((type & (int)CardType.Link) != 0)
        {
            linkMarker = defense;
            defense = 0;
        }

        return new Card(id, ot, alias, setcode, type, level, lScale, rScale, race, attribute, attack, defense, linkMarker);
    }

    private static void EnsureDatabaseExists(string databaseFullPath)
    {
        if (!File.Exists(databaseFullPath))
            throw new FileNotFoundException("Could not find the cards database.", databaseFullPath);
    }
}
