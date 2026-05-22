namespace YGOProSharp.Cli;

public sealed class CliConfiguration
{
    private const string ConfigFileOption = "Config";
    private const char SeparatorChar = '=';
    private const char CommentChar = '#';

    private readonly Dictionary<string, string> _fields;

    private CliConfiguration(Dictionary<string, string> fields)
    {
        _fields = fields;
    }

    public static CliConfiguration Load(IEnumerable<string> args)
    {
        Dictionary<string, string> fields = LoadArgs(args);

        if (fields.TryGetValue(ConfigFileOption, out string? filename))
        {
            foreach ((string key, string value) in LoadFile(filename))
                fields.TryAdd(key, value);
        }

        return new CliConfiguration(fields);
    }

    public string? GetString(string key)
    {
        return _fields.TryGetValue(key, out string? value) ? value : null;
    }

    public string GetString(string key, string defaultValue)
    {
        return _fields.TryGetValue(key, out string? value) ? value : defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        if (!_fields.TryGetValue(key, out string? rawValue))
            return defaultValue;

        return rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(rawValue[2..], 16)
            : Convert.ToInt32(rawValue);
    }

    public uint GetUInt(string key, uint defaultValue = 0)
    {
        return (uint)GetInt(key, (int)defaultValue);
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        return _fields.TryGetValue(key, out string? rawValue) ? Convert.ToBoolean(rawValue) : defaultValue;
    }

    public bool Has(string key)
    {
        return _fields.ContainsKey(key);
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
}
