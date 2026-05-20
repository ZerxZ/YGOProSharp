using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Cards;

namespace YGOProSharp;

public static class YGOProSharpServer
{
    public const uint DefaultClientVersion = 0x1349;

    public static uint ClientVersion { get; private set; } = DefaultClientVersion;

    public static async Task RunAsync(string[] args, IOcgRuntime runtime, ILoggerFactory loggerFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Config.Load(args);

        string rootPath = Config.GetString("RootPath", ".");
        string scriptDirectory = Config.GetString("ScriptDirectory", "script");
        string databaseFile = Config.GetString("DatabaseFile", "cards.cdb");
        string databaseFullPath = Path.Combine(Path.GetFullPath(rootPath), databaseFile);

        BanlistManager.Init(Config.GetString("BanlistFile", "lflist.conf"));
        runtime.Initialize(new OcgRuntimeOptions(
            rootPath,
            scriptDirectory,
            databaseFile,
            new SqliteCardDataProvider(databaseFullPath),
            new FileScriptProvider(rootPath, scriptDirectory)));

        ClientVersion = Config.GetUInt("ClientVersion", ClientVersion);

        CoreServer server = new(runtime.DuelFactory, loggerFactory);
        server.Start();

        try
        {
            while (server.IsRunning && !cancellationToken.IsCancellationRequested)
            {
                server.Tick();
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (server.IsRunning)
                server.Stop();
        }
    }
}
