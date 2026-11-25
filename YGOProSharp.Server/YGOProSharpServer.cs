using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Server;

/// <summary>
/// 主库入口（library entrypoint），由 CLI host 调用，用来串接配置、分层 DIY/扩展资源、native runtime 和核心 tick loop。
/// </summary>
public static class YGOProSharpServer
{
    public const uint DefaultClientVersion = 0x1349;

    public static uint ClientVersion { get; private set; } = DefaultClientVersion;

    internal static void SetClientVersion(uint clientVersion)
    {
        ClientVersion = clientVersion;
    }

    /// <summary>
    /// 根据配置加载基础与扩展/DIY 卡库、脚本、禁限表并初始化 native runtime，返回可直接托管或手动 Tick 的 <see cref="YGOProSharpServerHost"/>。
    /// </summary>
    public static YGOProSharpServerHost CreateHost(ServerOptions options, IOcgRuntime runtime)
    {
        CoreServer server = InitializeCoreServer(options, runtime);
        return new YGOProSharpServerHost(server);
    }

    /// <summary>
    /// 启动服务循环（server loop），但不接管 logger 配置或 native runtime 构造。
    /// </summary>
    public static async Task RunAsync(ServerOptions options, IOcgRuntime runtime, CancellationToken cancellationToken = default)
    {
        using YGOProSharpServerHost host = CreateHost(options, runtime);
        await host.RunLoopAsync(tickDelayMilliseconds: 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 按加载顺序解析最终卡片数据库（<c>.cdb</c>）列表（基础卡库在前，expansions / diy / ExtraDatabases 在后以实现覆盖），
    /// 供服务端与内嵌 WindBot 共享同一套 DIY 卡库。
    /// </summary>
    public static IReadOnlyList<string> ResolveDatabasePaths(ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return DiscoverAssets(options).DatabasePaths;
    }

    internal static CoreServer InitializeCoreServer(ServerOptions options, IOcgRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);

        ILogger logger = AppLog.CreateLogger("YGOProSharp.Server");

        string rootPath = options.RootPath;
        string scriptDirectory = options.ScriptDirectory;
        string databaseFile = options.DatabaseFile;
        int port = options.Port;
        ClientVersion = options.ClientVersion;
        ICardDatabaseManager cardDatabaseManager = new SqliteCardDatabaseManager();

        DiscoveredAssets assets = DiscoverAssets(options);

        logger.LogInformation(
            "Starting server with RootPath={RootPath}, ScriptDirectory={ScriptDirectory}, DatabaseFile={DatabaseFile}, ClientVersion={ClientVersion}, Port={Port}, EnableNetworkListener={EnableNetworkListener}, EnableMultiRoom={EnableMultiRoom}, DatabaseCount={DatabaseCount}, ScriptDirs={ScriptDirCount}, ScriptArchives={ArchiveCount}, BanlistFiles={BanlistFileCount}.",
            rootPath,
            scriptDirectory,
            databaseFile,
            ClientVersion,
            port,
            options.EnableNetworkListener,
            options.EnableMultiRoom,
            assets.DatabasePaths.Count,
            assets.ScriptDirectories.Count,
            assets.ScriptArchives.Count,
            assets.BanlistPaths.Count);

        ICardRepository initialRepository;
        try
        {
            ICardRepository diskRepository;
            if (assets.DatabasePaths.Count == 0)
            {
                logger.LogWarning(
                    "No card database (.cdb) found yet (Primary={DatabasePath}). Starting with an empty card catalog; place cards.cdb or upload a DIY package and trigger hot-reload.",
                    assets.PrimaryDatabasePath);
                diskRepository = EmptyCardRepository.Instance;
            }
            else
            {
                if (!File.Exists(assets.PrimaryDatabasePath))
                {
                    logger.LogWarning(
                        "Primary database {DatabasePath} was not found; loading {DatabaseCount} discovered expansion/DIY database(s) only.",
                        assets.PrimaryDatabasePath,
                        assets.DatabasePaths.Count);
                }

                diskRepository = assets.DatabasePaths.Count == 1
                    ? cardDatabaseManager.LoadCards(assets.DatabasePaths[0])
                    : cardDatabaseManager.LoadCards(assets.DatabasePaths);

                logger.LogInformation("Loaded {DatabaseCount} card database(s). Primary={DatabasePath}.", assets.DatabasePaths.Count, assets.PrimaryDatabasePath);
            }

            initialRepository = options.AdditionalCardRepositories.Count == 0
                ? diskRepository
                : new CompositeCardRepository(options.AdditionalCardRepositories.Concat([diskRepository]));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load card database from {DatabasePath}.", assets.PrimaryDatabasePath);
            throw;
        }

        ReloadableCardRepository reloadableCards = new(initialRepository);

        BanlistManager.Init(assets.BanlistPaths);
        if (assets.BanlistPaths.Count == 0)
            logger.LogWarning("No banlist file (lflist.conf) found yet; starting with an empty banlist list.");
        else
            logger.LogInformation("Loaded {BanlistCount} banlist(s) from {BanlistFileCount} file(s).", BanlistManager.Banlists.Count, assets.BanlistPaths.Count);

        PresetDeckPool presetDeckPool = options.PresetDeckPool ?? new PresetDeckPool();
        if (assets.PresetDeckDirectories.Count > 0)
        {
            int deckCount = presetDeckPool.ReloadFromDirectories(assets.PresetDeckDirectories, reloadableCards);
            logger.LogInformation("Loaded {PresetDeckCount} preset random deck(s).", deckCount);
        }

        ReloadableScriptProvider reloadableScripts = new(BuildScriptProvider(options, assets, logger));
        try
        {
            runtime.Initialize(new OcgRuntimeOptions(
                rootPath,
                scriptDirectory,
                databaseFile,
                new SandboxOverlayCardDataProvider(new RepositoryCardDataProvider(reloadableCards)),
                new SandboxOverlayScriptProvider(reloadableScripts)));
            logger.LogInformation("Native OCG runtime initialized.");
        }
        catch (Exception ex)
        {
            reloadableScripts.Dispose();
            logger.LogError(ex, "Failed to initialize native OCG runtime.");
            throw;
        }

        ServerOptions effectiveOptions = options with { PresetDeckPool = presetDeckPool };
        Func<ServerReloadResult> reloadCallback = () =>
            ReloadServerAssets(effectiveOptions, cardDatabaseManager, reloadableCards, reloadableScripts, presetDeckPool, logger);

        return new CoreServer(runtime.DuelFactory, reloadableCards, effectiveOptions, reloadableScripts, reloadCallback);
    }

    private static ServerReloadResult ReloadServerAssets(
        ServerOptions options,
        ICardDatabaseManager cardDatabaseManager,
        ReloadableCardRepository reloadableCards,
        ReloadableScriptProvider reloadableScripts,
        PresetDeckPool presetDeckPool,
        ILogger logger)
    {
        DiscoveredAssets assets = DiscoverAssets(options);
        ICardRepository diskRepository = assets.DatabasePaths.Count switch
        {
            0 => EmptyCardRepository.Instance,
            1 => cardDatabaseManager.LoadCards(assets.DatabasePaths[0]),
            _ => cardDatabaseManager.LoadCards(assets.DatabasePaths)
        };

        ICardRepository nextRepo = options.AdditionalCardRepositories.Count == 0
            ? diskRepository
            : new CompositeCardRepository(options.AdditionalCardRepositories.Concat([diskRepository]));

        long catalogVersion = reloadableCards.Reload(nextRepo);

        BanlistManager.Init(assets.BanlistPaths);

        IScriptProvider nextScripts = BuildScriptProvider(options, assets, logger);
        long scriptVersion = reloadableScripts.Reload(nextScripts, disposePreviousImmediately: false);

        if (assets.PresetDeckDirectories.Count > 0)
            presetDeckPool.ReloadFromDirectories(assets.PresetDeckDirectories, reloadableCards);

        logger.LogInformation(
            "Hot-reloaded server assets. CatalogVersion={CatalogVersion}, ScriptVersion={ScriptVersion}, Databases={DatabaseCount}, Banlists={BanlistCount}, PresetDecks={PresetDeckCount}.",
            catalogVersion,
            scriptVersion,
            assets.DatabasePaths.Count,
            BanlistManager.Banlists.Count,
            presetDeckPool.Count);

        return new ServerReloadResult(
            catalogVersion,
            scriptVersion,
            assets.DatabasePaths.Count,
            BanlistManager.Banlists.Count,
            presetDeckPool.Count);
    }

    private static IScriptProvider BuildScriptProvider(ServerOptions options, DiscoveredAssets assets, ILogger logger)
    {
        List<IScriptProvider> providers = new();

        // 1. 最高优先级：代码级注入的脚本 provider（例如内存 DIY 规则脚本）
        for (int i = 0; i < options.AdditionalScriptProviders.Count; i++)
        {
            if (options.AdditionalScriptProviders[i] is not null)
                providers.Add(options.AdditionalScriptProviders[i]);
        }

        // 2. 分层目录：显式 ExtraScriptDirectories -> diy/script -> expansions/script -> 基础 script/
        if (assets.ScriptDirectories.Count > 0)
            providers.Add(new FileScriptProvider(assets.ScriptDirectories));

        // 3. 压缩扩展包：.ypk / .zip（高优先级在前；使用内存拷贝打开以避免 Windows 下文件锁阻塞热重载更新）
        for (int i = 0; i < assets.ScriptArchives.Count; i++)
        {
            string archivePath = assets.ScriptArchives[i];
            try
            {
                providers.Add(new ZipArchiveScriptProvider(archivePath, copyToMemory: true));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to open script archive {ArchivePath}; skipping.", archivePath);
            }
        }

        if (providers.Count == 1)
            return providers[0];

        // 仅释放内部创建的 ZipArchiveScriptProvider，不错误释放调用方外部持有的 AdditionalScriptProviders。
        return new CompositeScriptProvider(providers, disposeChildren: options.AdditionalScriptProviders.Count == 0);
    }

    private static DiscoveredAssets DiscoverAssets(ServerOptions options)
    {
        string rootFullPath = Path.GetFullPath(options.RootPath);
        string primaryDbPath = ResolvePath(rootFullPath, options.DatabaseFile);
        string baseScriptDir = ResolvePath(rootFullPath, options.ScriptDirectory);
        string primaryBanlistPath = ResolveBanlistPath(rootFullPath, options.BanlistFile);

        List<string> expansionRoots = new();
        if (options.AutoLoadExpansions)
        {
            string defaultExpansions = Path.Combine(rootFullPath, "expansions");
            if (Directory.Exists(defaultExpansions))
                expansionRoots.Add(defaultExpansions);

            string defaultDiy = Path.Combine(rootFullPath, "diy");
            if (Directory.Exists(defaultDiy))
            {
                expansionRoots.Add(defaultDiy);
                DiyPackageManager pkgManager = new(defaultDiy, new SqliteCardDatabaseManager());
                foreach (DiyPackageManifest pkg in pkgManager.ListPackages(enabledOnly: true))
                    expansionRoots.Add(pkgManager.GetPackageDirectory(pkg.Id));
            }
        }

        foreach (string extraRoot in options.ExpansionDirectories)
        {
            if (string.IsNullOrWhiteSpace(extraRoot))
                continue;

            string resolved = ResolvePath(rootFullPath, extraRoot);
            if (Directory.Exists(resolved))
                expansionRoots.Add(resolved);
        }

        expansionRoots = expansionRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 数据库顺序：基础 cards.cdb -> expansions/*.cdb -> diy/*.cdb -> 显式 ExtraDatabases（后者覆盖前者）
        List<string> databasePaths = new();
        if (File.Exists(primaryDbPath))
            databasePaths.Add(primaryDbPath);

        foreach (string expansionRoot in expansionRoots)
        {
            foreach (string cdb in Directory.EnumerateFiles(expansionRoot, "*.cdb", SearchOption.TopDirectoryOnly).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
                databasePaths.Add(Path.GetFullPath(cdb));
        }

        foreach (string extraDb in options.ExtraDatabases)
        {
            if (string.IsNullOrWhiteSpace(extraDb))
                continue;

            string resolved = ResolvePath(rootFullPath, extraDb);
            if (File.Exists(resolved))
                databasePaths.Add(resolved);
        }

        databasePaths = databasePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 脚本目录顺序（前者优先）：显式 ExtraScriptDirectories -> expansionRoots 倒序中的 script/ -> 基础 script/
        List<string> scriptDirs = new();
        foreach (string extraDir in options.ExtraScriptDirectories)
        {
            if (string.IsNullOrWhiteSpace(extraDir))
                continue;

            string resolved = ResolvePath(rootFullPath, extraDir);
            if (Directory.Exists(resolved))
                scriptDirs.Add(resolved);
        }

        for (int i = expansionRoots.Count - 1; i >= 0; i--)
        {
            string expScriptDir = Path.Combine(expansionRoots[i], "script");
            if (Directory.Exists(expScriptDir))
                scriptDirs.Add(Path.GetFullPath(expScriptDir));
        }

        scriptDirs.Add(baseScriptDir);
        scriptDirs = scriptDirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 脚本压缩包顺序（前者优先）：显式 ExtraScriptArchives -> expansionRoots 倒序中的 *.ypk/*.zip
        List<string> scriptArchives = new();
        foreach (string extraArchive in options.ExtraScriptArchives)
        {
            if (string.IsNullOrWhiteSpace(extraArchive))
                continue;

            string resolved = ResolvePath(rootFullPath, extraArchive);
            if (File.Exists(resolved))
                scriptArchives.Add(resolved);
        }

        for (int i = expansionRoots.Count - 1; i >= 0; i--)
        {
            IEnumerable<string> archives = Directory
                .EnumerateFiles(expansionRoots[i], "*.*", SearchOption.TopDirectoryOnly)
                .Where(static p => p.EndsWith(".ypk", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static p => p, StringComparer.OrdinalIgnoreCase);

            foreach (string archive in archives)
                scriptArchives.Add(Path.GetFullPath(archive));
        }

        scriptArchives = scriptArchives.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 禁限表顺序（前者靠前，作为 index 0）：显式 ExtraBanlistFiles -> expansionRoots 倒序中的 lflist.conf -> 基础 lflist.conf
        List<string> banlistPaths = new();
        foreach (string extraBanlist in options.ExtraBanlistFiles)
        {
            if (string.IsNullOrWhiteSpace(extraBanlist))
                continue;

            string resolved = ResolveBanlistPath(rootFullPath, extraBanlist);
            if (File.Exists(resolved))
                banlistPaths.Add(resolved);
        }

        for (int i = expansionRoots.Count - 1; i >= 0; i--)
        {
            string expBanlist = Path.Combine(expansionRoots[i], "lflist.conf");
            if (File.Exists(expBanlist))
                banlistPaths.Add(Path.GetFullPath(expBanlist));
        }

        if (File.Exists(primaryBanlistPath))
            banlistPaths.Add(primaryBanlistPath);

        banlistPaths = banlistPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 随机盲盒预设卡组目录顺序：decks/random -> RootPath 下 Decks/deck/windbot/Decks -> expansionRoots 中的 decks/ -> 显式 RandomDeckDirectories
        List<string> presetDeckDirs = new();
        string[] candidatePresetDirs =
        [
            Path.Combine(rootFullPath, "decks", "random"),
            Path.Combine(rootFullPath, "Decks"),
            Path.Combine(rootFullPath, "deck"),
            Path.Combine(rootFullPath, "windbot", "Decks")
        ];
        foreach (string candidateDir in candidatePresetDirs)
        {
            if (Directory.Exists(candidateDir))
                presetDeckDirs.Add(Path.GetFullPath(candidateDir));
        }

        for (int i = 0; i < expansionRoots.Count; i++)
        {
            string expDeckDir = Path.Combine(expansionRoots[i], "decks");
            if (Directory.Exists(expDeckDir))
                presetDeckDirs.Add(Path.GetFullPath(expDeckDir));
        }

        foreach (string customDeckDir in options.RandomDeckDirectories)
        {
            if (string.IsNullOrWhiteSpace(customDeckDir))
                continue;

            string resolved = ResolvePath(rootFullPath, customDeckDir);
            if (Directory.Exists(resolved))
                presetDeckDirs.Add(resolved);
        }

        presetDeckDirs = presetDeckDirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new DiscoveredAssets(
            primaryDbPath,
            primaryBanlistPath,
            databasePaths,
            scriptDirs,
            scriptArchives,
            banlistPaths,
            presetDeckDirs);
    }

    private static string ResolvePath(string rootFullPath, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(rootFullPath, path));
    }

    private static string ResolveBanlistPath(string rootFullPath, string banlistFile)
    {
        string resolved = ResolvePath(rootFullPath, banlistFile);
        if (!File.Exists(resolved) && File.Exists(banlistFile))
            return Path.GetFullPath(banlistFile);
        return resolved;
    }

    private sealed record DiscoveredAssets(
        string PrimaryDatabasePath,
        string PrimaryBanlistPath,
        List<string> DatabasePaths,
        List<string> ScriptDirectories,
        List<string> ScriptArchives,
        List<string> BanlistPaths,
        List<string> PresetDeckDirectories);
}
