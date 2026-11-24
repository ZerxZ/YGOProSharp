using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YGOProSharp.Core
{
    public static class BanlistManager
    {
        public static List<Banlist> Banlists { get; private set; } = new();

        public static void Init(string fileName)
        {
            Banlists = ParseText(File.ReadAllText(fileName));
        }

        /// <summary>
        /// 按顺序加载多个禁限表文件（例如 DIY / expansions / 基础 <c>lflist.conf</c>），跳过不存在的可选路径。
        /// </summary>
        public static void Init(IEnumerable<string> fileNames)
        {
            ArgumentNullException.ThrowIfNull(fileNames);

            List<Banlist> loaded = new();
            foreach (string fileName in fileNames)
            {
                if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
                    continue;

                loaded.AddRange(ParseText(File.ReadAllText(fileName)));
            }

            Banlists = loaded;
        }

        /// <summary>
        /// 追加或前置加载额外的禁限表文件（默认前置，使扩展/DIY 禁限表排在前面，与主流 srvpro 习惯一致）。
        /// </summary>
        public static int LoadAdditional(string fileName, bool prepend = true)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
                return 0;

            List<Banlist> parsed = ParseText(File.ReadAllText(fileName));
            if (parsed.Count == 0)
                return 0;

            if (prepend)
                Banlists.InsertRange(0, parsed);
            else
                Banlists.AddRange(parsed);

            return parsed.Count;
        }

        public static void AddBanlist(Banlist banlist, bool prepend = false)
        {
            ArgumentNullException.ThrowIfNull(banlist);
            if (prepend)
                Banlists.Insert(0, banlist);
            else
                Banlists.Add(banlist);
        }

        public static List<Banlist> ParseText(string text)
        {
            List<Banlist> banlists = new List<Banlist>();
            Banlist? current = null;
            using StringReader reader = new(text);

            while (reader.ReadLine() is { } rawLine)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("#"))
                    continue;
                if (line.StartsWith("!"))
                {
                    current = new Banlist(line[1..].Trim());
                    banlists.Add(current);
                    continue;
                }
                if (line.StartsWith("$"))
                {
                    if (current != null && line.Equals("$whitelist", StringComparison.OrdinalIgnoreCase))
                        current.EnableWhitelistMode();
                    continue;
                }
                if (current == null)
                    continue;
                string[] data = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (data.Length < 2)
                    continue;
                if (!int.TryParse(data[0], out int id) || !int.TryParse(data[1], out int count))
                    continue;
                current.Add(id, count);
            }

            return banlists;
        }

        public static int GetIndex(uint hash)
        {
            for (int i = 0; i < Banlists.Count; i++)
                if (Banlists[i].Hash == hash)
                    return i;
            return 0;
        }

        public static bool TryGetIndex(uint hash, out int index)
        {
            for (int i = 0; i < Banlists.Count; i++)
            {
                if (Banlists[i].Hash == hash)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        public static Banlist? FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return Banlists.FirstOrDefault(b => string.Equals(b.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryGetByHash(uint hash, out Banlist? banlist)
        {
            if (TryGetIndex(hash, out int index))
            {
                banlist = Banlists[index];
                return true;
            }

            banlist = null;
            return false;
        }
    }
}
