using WindBot.Game;
using WindBot.Game.AI;

namespace WindBot;

public static class WindBotRuntime
{
    public static Random Random { get; private set; } = new Random();
    public static bool UsePreErrataEffects { get; private set; }
    public static bool ServerMode { get; internal set; }

    public static void Configure(WindBotRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Random = options.Random ?? new Random();
        UsePreErrataEffects = options.UsePreErrataEffects;
        ServerMode = options.ServerMode;
        CardDatabase.Initialize(options.CardRepository);
        DecksManager.Init();

        if (options.LuaRuntime != null || options.LuaScriptProvider != null || options.PreferLuaDecks || options.ThrowOnLuaScriptError)
        {
            DecksManager.ConfigureLua(
                options.LuaRuntime,
                options.LuaScriptProvider,
                options.PreferLuaDecks,
                options.ThrowOnLuaScriptError);
        }

        if (options.LuaDecks != null && options.LuaDecks.Count > 0)
            DecksManager.RegisterLuaDecks(options.LuaDecks);
    }

    public static FileStream ReadFile(string directory, string filename, string extension)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new FileNotFoundException("Resource filename cannot be empty.", filename);

        string path = ResolveResourcePath(directory, filename, extension);
        return new FileStream(path, FileMode.Open, FileAccess.Read);
    }

    private static string ResolveResourcePath(string directory, string filename, string extension)
    {
        string tryFilename = filename + "." + extension;
        string[] relativeCandidates =
        [
            Path.Combine(directory, tryFilename),
            filename,
            Path.Combine("..", filename),
            Path.Combine("..", "deck", filename),
            Path.Combine("..", tryFilename),
            Path.Combine("..", "deck", tryFilename),
            Path.Combine("Data", "WindBot", directory, tryFilename),
            Path.Combine("Deck", tryFilename),
            Path.Combine("windbot", directory, tryFilename),
            Path.Combine("windbot", "windbot", directory, tryFilename)
        ];

        foreach (string candidate in relativeCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        string baseDir = AppContext.BaseDirectory;
        string[] baseCandidates =
        [
            Path.Combine(baseDir, directory, tryFilename),
            Path.Combine(baseDir, "Data", "WindBot", directory, tryFilename),
            Path.Combine(baseDir, "windbot", directory, tryFilename),
            Path.Combine(baseDir, "windbot", "windbot", directory, tryFilename)
        ];

        foreach (string candidate in baseCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return relativeCandidates[0];
    }
}
