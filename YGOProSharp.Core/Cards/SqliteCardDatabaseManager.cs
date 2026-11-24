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

    public CardDatabaseValidationResult ValidateDatabase(string databaseFullPath)
    {
        if (string.IsNullOrWhiteSpace(databaseFullPath) || !File.Exists(databaseFullPath))
            return new CardDatabaseValidationResult(false, 0, 0, [], "Database file does not exist.");

        try
        {
            List<int> cardIds = new();
            int customCount = 0;

            using SqliteConnection connection = OpenConnection(Path.GetFullPath(databaseFullPath));
            using (SqliteCommand datasCmd = connection.CreateCommand())
            {
                datasCmd.CommandText = CardQuery;
                using SqliteDataReader reader = datasCmd.ExecuteReader();
                while (reader.Read())
                {
                    Card card = ReadCard(reader);
                    cardIds.Add(card.Id);
                    if ((card.Ot & 0x4) != 0)
                        customCount++;
                }
            }

            using (SqliteCommand textsCmd = connection.CreateCommand())
            {
                textsCmd.CommandText = "SELECT COUNT(*) FROM texts";
                _ = textsCmd.ExecuteScalar();
            }

            return new CardDatabaseValidationResult(true, cardIds.Count, customCount, cardIds);
        }
        catch (Exception ex)
        {
            return new CardDatabaseValidationResult(false, 0, 0, [], ex.Message);
        }
    }

    public IReadOnlyList<DiyCardEntry> LoadEditableCards(string databaseFullPath)
    {
        if (string.IsNullOrWhiteSpace(databaseFullPath) || !File.Exists(databaseFullPath))
            return [];

        string fullPath = Path.GetFullPath(databaseFullPath);
        using SqliteConnection connection = OpenConnection(fullPath);

        HashSet<string> dataCols = GetTableColumns(connection, "datas");
        HashSet<string> textCols = GetTableColumns(connection, "texts");

        bool hasCategory = dataCols.Contains("category");
        int stringColCount = 0;
        for (int i = 1; i <= 16; i++)
        {
            if (textCols.Contains($"str{i}"))
                stringColCount = i;
            else
                break;
        }

        string categoryExpr = hasCategory ? "datas.category" : "0";
        string strSelect = stringColCount > 0
            ? ", " + string.Join(", ", Enumerable.Range(1, stringColCount).Select(static i => $"COALESCE(texts.str{i}, '')"))
            : string.Empty;

        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            $"SELECT datas.id, datas.ot, datas.alias, datas.setcode, datas.type, datas.level, datas.race, datas.attribute, datas.atk, datas.def, " +
            $"COALESCE(texts.name, ''), COALESCE(texts.desc, ''), {categoryExpr}{strSelect} " +
            "FROM datas LEFT JOIN texts ON datas.id = texts.id ORDER BY datas.id ASC";

        List<DiyCardEntry> list = new();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            RawCardFields f = ReadCardFields(reader);
            string name = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
            string desc = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);
            int category = reader.IsDBNull(12) ? 0 : reader.GetInt32(12);

            string[] strs = stringColCount > 0 ? new string[16] : [];
            if (stringColCount > 0)
            {
                for (int i = 0; i < 16; i++)
                {
                    int colIdx = 13 + i;
                    strs[i] = i < stringColCount && !reader.IsDBNull(colIdx) ? reader.GetString(colIdx) : string.Empty;
                }
            }

            list.Add(new DiyCardEntry
            {
                Id = f.Id,
                Ot = f.Ot,
                Alias = f.Alias,
                Setcode = f.Setcode,
                Type = f.Type,
                Level = f.Level,
                LScale = f.LScale,
                RScale = f.RScale,
                Race = f.Race,
                Attribute = f.Attribute,
                Attack = f.Attack,
                Defense = f.Defense,
                LinkMarker = f.LinkMarker,
                Category = category,
                Name = name,
                Desc = desc,
                Strings = strs
            });
        }

        return list;
    }

    public CardDatabaseValidationResult UpsertCard(string databaseFullPath, DiyCardEntry card)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFullPath);
        ArgumentNullException.ThrowIfNull(card);
        if (card.Id <= 0)
            return new CardDatabaseValidationResult(false, 0, 0, [], "Card ID must be a positive integer.");

        string fullTarget = Path.GetFullPath(databaseFullPath);
        string? dir = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string workingCopy = fullTarget + "." + Guid.NewGuid().ToString("N") + ".working.cdb";
        try
        {
            if (File.Exists(fullTarget))
                File.Copy(fullTarget, workingCopy, overwrite: true);

            using (SqliteConnection conn = OpenWritableConnection(workingCopy))
            {
                EnsureEditableSchema(conn);
                using SqliteTransaction tx = conn.BeginTransaction();

                int packedLevel = ((card.LScale & 0xff) << 24) | ((card.RScale & 0xff) << 16) | (card.Level & 0xff);
                int packedDef = (card.Type & (int)CardType.Link) != 0 ? card.LinkMarker : card.Defense;

                using (SqliteCommand upsertData = conn.CreateCommand())
                {
                    upsertData.Transaction = tx;
                    upsertData.CommandText =
                        "INSERT OR REPLACE INTO datas (id, ot, alias, setcode, type, atk, def, level, race, attribute, category) " +
                        "VALUES ($id, $ot, $alias, $setcode, $type, $atk, $def, $level, $race, $attr, $cat)";
                    upsertData.Parameters.AddWithValue("$id", card.Id);
                    upsertData.Parameters.AddWithValue("$ot", card.Ot);
                    upsertData.Parameters.AddWithValue("$alias", card.Alias);
                    upsertData.Parameters.AddWithValue("$setcode", card.Setcode);
                    upsertData.Parameters.AddWithValue("$type", card.Type);
                    upsertData.Parameters.AddWithValue("$atk", card.Attack);
                    upsertData.Parameters.AddWithValue("$def", packedDef);
                    upsertData.Parameters.AddWithValue("$level", packedLevel);
                    upsertData.Parameters.AddWithValue("$race", card.Race);
                    upsertData.Parameters.AddWithValue("$attr", card.Attribute);
                    upsertData.Parameters.AddWithValue("$cat", card.Category);
                    upsertData.ExecuteNonQuery();
                }

                using (SqliteCommand upsertText = conn.CreateCommand())
                {
                    upsertText.Transaction = tx;
                    upsertText.CommandText =
                        "INSERT OR REPLACE INTO texts (id, name, desc, str1, str2, str3, str4, str5, str6, str7, str8, str9, str10, str11, str12, str13, str14, str15, str16) " +
                        "VALUES ($id, $name, $desc, $s1, $s2, $s3, $s4, $s5, $s6, $s7, $s8, $s9, $s10, $s11, $s12, $s13, $s14, $s15, $s16)";
                    upsertText.Parameters.AddWithValue("$id", card.Id);
                    upsertText.Parameters.AddWithValue("$name", card.Name ?? string.Empty);
                    upsertText.Parameters.AddWithValue("$desc", card.Desc ?? string.Empty);
                    for (int i = 1; i <= 16; i++)
                    {
                        string val = card.Strings is not null && i - 1 < card.Strings.Count
                            ? (card.Strings[i - 1] ?? string.Empty)
                            : string.Empty;
                        upsertText.Parameters.AddWithValue($"$s{i}", val);
                    }
                    upsertText.ExecuteNonQuery();
                }

                tx.Commit();
            }

            CardDatabaseValidationResult validation = ValidateDatabase(workingCopy);
            if (!validation.IsValid)
                return validation;

            File.Move(workingCopy, fullTarget, overwrite: true);
            return validation;
        }
        finally
        {
            if (File.Exists(workingCopy))
            {
                try { File.Delete(workingCopy); } catch { }
            }
        }
    }

    public CardDatabaseValidationResult DeleteCard(string databaseFullPath, int cardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFullPath);
        string fullTarget = Path.GetFullPath(databaseFullPath);
        if (!File.Exists(fullTarget))
            return new CardDatabaseValidationResult(false, 0, 0, [], "Database file does not exist.");

        string workingCopy = fullTarget + "." + Guid.NewGuid().ToString("N") + ".working.cdb";
        try
        {
            File.Copy(fullTarget, workingCopy, overwrite: true);
            using (SqliteConnection conn = OpenWritableConnection(workingCopy))
            {
                using SqliteTransaction tx = conn.BeginTransaction();
                using (SqliteCommand delData = conn.CreateCommand())
                {
                    delData.Transaction = tx;
                    delData.CommandText = "DELETE FROM datas WHERE id = $id";
                    delData.Parameters.AddWithValue("$id", cardId);
                    delData.ExecuteNonQuery();
                }
                using (SqliteCommand delText = conn.CreateCommand())
                {
                    delText.Transaction = tx;
                    delText.CommandText = "DELETE FROM texts WHERE id = $id";
                    delText.Parameters.AddWithValue("$id", cardId);
                    delText.ExecuteNonQuery();
                }
                tx.Commit();
            }

            CardDatabaseValidationResult validation = ValidateDatabase(workingCopy);
            if (!validation.IsValid)
                return validation;

            File.Move(workingCopy, fullTarget, overwrite: true);
            return validation;
        }
        finally
        {
            if (File.Exists(workingCopy))
            {
                try { File.Delete(workingCopy); } catch { }
            }
        }
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
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
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

    private static SqliteConnection OpenWritableConnection(string databaseFullPath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databaseFullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    private static HashSet<string> GetTableColumns(SqliteConnection connection, string tableName)
    {
        HashSet<string> cols = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!r.IsDBNull(1))
                cols.Add(r.GetString(1));
        }
        return cols;
    }

    private static void EnsureEditableSchema(SqliteConnection connection)
    {
        using (SqliteCommand createCmd = connection.CreateCommand())
        {
            createCmd.CommandText =
                "CREATE TABLE IF NOT EXISTS datas (id INTEGER PRIMARY KEY, ot INTEGER, alias INTEGER, setcode INTEGER, type INTEGER, atk INTEGER, def INTEGER, level INTEGER, race INTEGER, attribute INTEGER, category INTEGER);" +
                "CREATE TABLE IF NOT EXISTS texts (id INTEGER PRIMARY KEY, name TEXT, desc TEXT, str1 TEXT, str2 TEXT, str3 TEXT, str4 TEXT, str5 TEXT, str6 TEXT, str7 TEXT, str8 TEXT, str9 TEXT, str10 TEXT, str11 TEXT, str12 TEXT, str13 TEXT, str14 TEXT, str15 TEXT, str16 TEXT);";
            createCmd.ExecuteNonQuery();
        }

        HashSet<string> dataCols = GetTableColumns(connection, "datas");
        if (!dataCols.Contains("category"))
        {
            using SqliteCommand alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE datas ADD COLUMN category INTEGER DEFAULT 0";
            alter.ExecuteNonQuery();
        }

        HashSet<string> textCols = GetTableColumns(connection, "texts");
        for (int i = 1; i <= 16; i++)
        {
            string col = $"str{i}";
            if (!textCols.Contains(col))
            {
                using SqliteCommand alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE texts ADD COLUMN {col} TEXT DEFAULT ''";
                alter.ExecuteNonQuery();
            }
        }
    }

    private static void EnsureDatabaseExists(string databaseFullPath)
    {
        if (!File.Exists(databaseFullPath))
            throw new FileNotFoundException("Could not find the cards database.", databaseFullPath);
    }
}
