using YGOProSharp.OCGWrapper;

namespace YGOProSharp;

public static class YGOProSharpServer
{
    public const uint DefaultClientVersion = 0x1349;

    public static uint ClientVersion { get; private set; } = DefaultClientVersion;

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        Config.Load(args);

        BanlistManager.Init(Config.GetString("BanlistFile", "lflist.conf"));
        Api.Init(
            Config.GetString("RootPath", "."),
            Config.GetString("ScriptDirectory", "script"),
            Config.GetString("DatabaseFile", "cards.cdb"));

        ClientVersion = Config.GetUInt("ClientVersion", ClientVersion);

        CoreServer server = new();
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
