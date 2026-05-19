namespace YGOProSharp;

public static class Config
{
    private const string ConfigFileOption = "Config";
    private const char SeparatorChar = '=';
    private const char CommentChar = '#';

    private static Dictionary<string, string> _fields = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> IntegerCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> BooleanCache = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(string[] args)
    {
        IntegerCache.Clear();
        BooleanCache.Clear();

        _fields = LoadArgs(args);

        if (GetString(ConfigFileOption) is not { } filename)
            return;

        foreach ((string key, string value) in LoadFile(filename))
            _fields.TryAdd(key, value);
    }

    private static Dictionary<string, string> LoadArgs(IEnumerable<string> args)
    {
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);

        foreach (string option in args)
        {
            int position = option.IndexOf(SeparatorChar);

            if (position == -1)
                throw new ArgumentException($"Invalid argument '{option}': no key/value separator", nameof(args));

            string key = option[..position].Trim();
            string value = option[(position + 1)..].Trim();

            if (!fields.TryAdd(key, value))
                throw new ArgumentException($"Invalid argument '{option}': duplicate key '{key}'", nameof(args));
        }

        return fields;
    }

    private static Dictionary<string, string> LoadFile(string filename)
    {
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);

        using StreamReader reader = new(filename);

        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            line = line.Trim();

            if (line.Length == 0 || line[0] == CommentChar)
                continue;

            int position = line.IndexOf(SeparatorChar);

            if (position == -1)
                throw new FormatException($"Invalid configuration file: no key/value separator line {lineNumber}");

            string key = line[..position].Trim();
            string value = line[(position + 1)..].Trim();

            if (!fields.TryAdd(key, value))
                throw new FormatException($"Invalid configuration file: duplicate key '{key}' line {lineNumber}");
        }

        return fields;
    }

    public static string? GetString(string key)
    {
        return _fields.TryGetValue(key, out string? value) ? value : null;
    }

    public static string GetString(string key, string defaultValue)
    {
        return _fields.TryGetValue(key, out string? value) ? value : defaultValue;
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        if (IntegerCache.TryGetValue(key, out int cached))
            return cached;

        int value = defaultValue;
        if (_fields.TryGetValue(key, out string? rawValue))
        {
            value = rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(rawValue[2..], 16)
                : Convert.ToInt32(rawValue);
        }

        IntegerCache.Add(key, value);
        return value;
    }

    public static uint GetUInt(string key, uint defaultValue = 0)
    {
        return (uint)GetInt(key, (int)defaultValue);
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        if (BooleanCache.TryGetValue(key, out bool cached))
            return cached;

        bool value = defaultValue;
        if (_fields.TryGetValue(key, out string? rawValue))
            value = Convert.ToBoolean(rawValue);

        BooleanCache.Add(key, value);
        return value;
    }
}
