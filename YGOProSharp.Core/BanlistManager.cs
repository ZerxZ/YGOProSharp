using System;
using System.Collections.Generic;
using System.IO;

namespace YGOProSharp.Core
{
    public static class BanlistManager
    {
        public static List<Banlist> Banlists { get; private set; } = new();

        public static void Init(string fileName)
        {
            Banlists = ParseText(File.ReadAllText(fileName));
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
                int id = int.Parse(data[0]);
                int count = int.Parse(data[1]);
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
    }
}
