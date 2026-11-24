using WindBot.Game;
using WindBot.Game.AI;

namespace WindBot;

public static class WindBotRuntime
{
    public static Random Random { get; private set; } = new Random();
    public static bool UsePreErrataEffects { get; private set; }
    public static bool ServerMode { get; internal set; }

    /// <summary>
    /// 运行时默认本地化区域标识（如 <c>zh-CN</c>、<c>zh-TW</c>、<c>en-US</c>）。
    /// </summary>
    public static string Locale { get; set; }

    /// <summary>
    /// 运行时默认连续聊天最小间隔（毫秒）。
    /// </summary>
    public static int ChatIntervalMilliseconds { get; set; } = 800;

    /// <summary>
    /// 运行时默认开局聊天延迟（毫秒）。
    /// </summary>
    public static int DuelStartChatDelayMilliseconds { get; set; } = 4000;

    /// <summary>
    /// 运行时默认待发送聊天队列上限。
    /// </summary>
    public static int MaxQueuedChatMessages { get; set; } = 6;

    public static void Configure(WindBotRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Random = options.Random ?? new Random();
        UsePreErrataEffects = options.UsePreErrataEffects;
        ServerMode = options.ServerMode;
        Locale = string.IsNullOrWhiteSpace(options.Locale) ? null : options.Locale.Trim();
        ChatIntervalMilliseconds = Math.Max(0, options.ChatIntervalMilliseconds);
        DuelStartChatDelayMilliseconds = Math.Max(0, options.DuelStartChatDelayMilliseconds);
        MaxQueuedChatMessages = Math.Max(1, options.MaxQueuedChatMessages);
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

        if (options.LuaRuntime != null || options.LuaDialogScriptProvider != null || options.PreferLuaDialogs || options.ThrowOnLuaScriptError)
        {
            DialogsManager.ConfigureLua(
                options.LuaRuntime,
                options.LuaDialogScriptProvider,
                options.PreferLuaDialogs,
                options.ThrowOnLuaScriptError);
        }

        if (options.LuaDialogs != null && options.LuaDialogs.Count > 0)
            DialogsManager.RegisterLuaDialogs(options.LuaDialogs);
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
