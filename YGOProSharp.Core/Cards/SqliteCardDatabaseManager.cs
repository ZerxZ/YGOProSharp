using Microsoft.Data.Sqlite;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.Core.Cards;

/// <summary>
/// 基于 SQLite 的 YGOPro 卡片数据库 loader；这是唯一应该知道 Microsoft.Data.Sqlite 的层。
/// </summary>
public sealed class SqliteCardDatabaseManager : ICardDatabaseManager
{
    private const string CardQuery = "SELECT id, ot, alias, setcode, type, level, race, attribute, atk, def FROM datas";
    private const string NamedCardQuery =
        "SELECT datas.id, ot, alias, setcode, type, level, race, attribute, atk, def, texts.name, texts.desc" +
        " FROM datas INNER JOIN texts ON datas.id = texts.id";

    public ICardRepository LoadCards(string databaseFullPath)
    {
        EnsureDatabaseExists(databaseFullPath);
        return LoadCardsCore(new[] { databaseFullPath });
    }

    public ICardRepository LoadCards(IEnumerable<string> databaseFullPaths)
    {
        ArgumentNullException.ThrowIfNull(databaseFullPaths);
        return LoadCardsCore(databaseFullPaths.Where(File.Exists));
    }

    private static ICardRepository LoadCardsCore(IEnumerable<string> databaseFullPaths)
    {
        List<Card> cards = new();
        foreach (string databaseFullPath in databaseFullPaths)
        {
            using SqliteConnection connection = OpenConnection(databaseFullPath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = CardQuery;

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                cards.Add(ReadCard(reader));
        }

        return cards.Count == 0
            ? EmptyCardRepository.Instance
            : new InMemoryCardRepository(cards);
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
        RawCardFields fields = ReadCardFields(reader);
        return new Card(
            fields.Id,
            fields.Ot,
            fields.Alias,
            fields.Setcode,
            fields.Type,
            fields.Level,
            fields.LScale,
            fields.RScale,
            fields.Race,
            fields.Attribute,
            fields.Attack,
            fields.Defense,
            fields.LinkMarker);
    }

    private static NamedCard ReadNamedCard(SqliteDataReader reader)
    {
        // 使用值类型中转字段，避免加载 NamedCard 时为每张卡额外分配临时 Card 对象。
        RawCardFields fields = ReadCardFields(reader);
        return new NamedCard(
            fields.Id,
            fields.Ot,
            fields.Alias,
            fields.Setcode,
            fields.Type,
            fields.Level,
            fields.LScale,
            fields.RScale,
            fields.Race,
            fields.Attribute,
            fields.Attack,
            fields.Defense,
            fields.LinkMarker,
            reader.GetString(10),
            reader.GetString(11));
    }

    private static RawCardFields ReadCardFields(SqliteDataReader reader)
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

        int level = levelInfo & 0xff;
        int lScale = (levelInfo >> 24) & 0xff;
        int rScale = (levelInfo >> 16) & 0xff;
        int linkMarker = 0;

        if ((type & (int)CardType.Link) != 0)
        {
            linkMarker = defense;
            defense = 0;
        }

        return new RawCardFields(id, ot, alias, setcode, type, level, lScale, rScale, race, attribute, attack, defense, linkMarker);
    }

    private readonly record struct RawCardFields(
        int Id,
        int Ot,
        int Alias,
        long Setcode,
        int Type,
        int Level,
        int LScale,
        int RScale,
        int Race,
        int Attribute,
        int Attack,
        int Defense,
        int LinkMarker);

    private static void EnsureDatabaseExists(string databaseFullPath)
    {
        if (!File.Exists(databaseFullPath))
            throw new FileNotFoundException("Could not find the cards database.", databaseFullPath);
    }
}
