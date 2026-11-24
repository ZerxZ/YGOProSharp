using System.Globalization;
using System.Text;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Core;

/// <summary>
/// 标准 <c>.ydk</c> 卡组文件的结构化数据模型。
/// </summary>
public sealed record YdkDeckData(
    string Name,
    IReadOnlyList<int> Main,
    IReadOnlyList<int> Extra,
    IReadOnlyList<int> Side,
    IReadOnlyList<string> Tags);

/// <summary>
/// <c>.ydk</c> 卡组文本的解析、序列化与 <see cref="Deck"/> 转换器。
/// </summary>
public static class YdkSerializer
{
    private enum Section
    {
        Main,
        Extra,
        Side
    }

    public static YdkDeckData Parse(string ydkContent, string name = "", IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(ydkContent);

        List<int> main = new();
        List<int> extra = new();
        List<int> side = new();
        Section current = Section.Main;

        using StringReader reader = new(ydkContent);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.Equals("#main", StringComparison.OrdinalIgnoreCase))
            {
                current = Section.Main;
                continue;
            }
            if (trimmed.Equals("#extra", StringComparison.OrdinalIgnoreCase))
            {
                current = Section.Extra;
                continue;
            }
            if (trimmed.Equals("!side", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("#side", StringComparison.OrdinalIgnoreCase))
            {
                current = Section.Side;
                continue;
            }
            if (trimmed[0] == '#' || trimmed[0] == '!')
                continue;

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cardId) && cardId > 0)
            {
                switch (current)
                {
                    case Section.Main:
                        main.Add(cardId);
                        break;
                    case Section.Extra:
                        extra.Add(cardId);
                        break;
                    case Section.Side:
                        side.Add(cardId);
                        break;
                }
            }
        }

        string[] resolvedTags = tags is null
            ? []
            : tags.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new YdkDeckData(name.Trim(), main, extra, side, resolvedTags);
    }

    public static YdkDeckData LoadFile(string filePath, IEnumerable<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        string content = File.ReadAllText(fullPath, Encoding.UTF8);
        string name = Path.GetFileNameWithoutExtension(fullPath);
        return Parse(content, name, tags);
    }

    public static Deck ToDeck(YdkDeckData data, ICardRepository cardRepository)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(cardRepository);

        Deck deck = new(cardRepository);
        for (int i = 0; i < data.Main.Count; i++)
            deck.AddMain(data.Main[i]);
        for (int i = 0; i < data.Extra.Count; i++)
            deck.AddMain(data.Extra[i]);
        for (int i = 0; i < data.Side.Count; i++)
            deck.AddSide(data.Side[i]);

        return deck;
    }

    public static YdkDeckData FromDeck(Deck deck, string name = "", IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(deck);
        string[] resolvedTags = tags is null
            ? []
            : tags.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new YdkDeckData(name.Trim(), deck.Main.ToArray(), deck.Extra.ToArray(), deck.Side.ToArray(), resolvedTags);
    }

    public static string Serialize(Deck deck, string? createdBy = "YGOProSharp")
    {
        ArgumentNullException.ThrowIfNull(deck);
        return Serialize(FromDeck(deck), createdBy);
    }

    public static string Serialize(YdkDeckData data, string? createdBy = "YGOProSharp")
    {
        ArgumentNullException.ThrowIfNull(data);

        StringBuilder sb = new();
        if (!string.IsNullOrWhiteSpace(createdBy))
            sb.Append("#created by ").AppendLine(createdBy.Trim());

        sb.AppendLine("#main");
        for (int i = 0; i < data.Main.Count; i++)
            sb.AppendLine(data.Main[i].ToString(CultureInfo.InvariantCulture));

        sb.AppendLine("#extra");
        for (int i = 0; i < data.Extra.Count; i++)
            sb.AppendLine(data.Extra[i].ToString(CultureInfo.InvariantCulture));

        sb.AppendLine("!side");
        for (int i = 0; i < data.Side.Count; i++)
            sb.AppendLine(data.Side[i].ToString(CultureInfo.InvariantCulture));

        return sb.ToString();
    }
}
