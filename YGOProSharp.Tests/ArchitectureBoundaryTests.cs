namespace YGOProSharp.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void ProjectBoundariesKeepNativeInteropOutOfCore()
    {
        string root = FindRepositoryRoot();
        string coreProject = Path.Combine(root, "YGOProSharp.Core");
        string abstractionsProject = Path.Combine(root, "YGOProSharp.Abstractions", "YGOProSharp.Abstractions.csproj");

        string[] forbiddenCoreTokens = ["LibraryImport", "DllImport", "SafeHandle", "byte*", "OcgCoreImports"];
        foreach (string file in Directory.EnumerateFiles(coreProject, "*.cs", SearchOption.AllDirectories)
                     .Where(IsRepositorySourcePath))
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbiddenCoreTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden native interop token {token}.");
            }
        }

        string abstractionsXml = File.ReadAllText(abstractionsProject);
        foreach (string forbiddenReference in new[] { "YGOProSharp.Native", "YGOProSharp.NativeApi", "Microsoft.Data.Sqlite", "SevenZip" })
        {
            if (abstractionsXml.Contains(forbiddenReference, StringComparison.Ordinal))
                throw new InvalidOperationException($"YGOProSharp.Abstractions references {forbiddenReference}.");
        }
    }

    [Fact]
    public void ProjectBoundariesKeepCardModelsAndSqliteSeparated()
    {
        string root = FindRepositoryRoot();
        string coreProject = Path.Combine(root, "YGOProSharp.Core");
        string sqliteManagerPath = Path.Combine(coreProject, "Cards", "SqliteCardDatabaseManager.cs");
        string[] cardModelFiles =
        [
            Path.Combine(coreProject, "Cards", "Card.cs"),
            Path.Combine(coreProject, "Cards", "NamedCard.cs")
        ];

        string[] forbiddenCardModelTokens = ["IDataRecord", "Microsoft.Data.Sqlite", "OcgCardData", "CardsManager"];
        foreach (string file in cardModelFiles)
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbiddenCardModelTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden card model dependency {token}.");
            }
        }

        string[] forbiddenSqliteTokens = ["Microsoft.Data.Sqlite", "SqliteConnection"];
        foreach (string file in Directory.EnumerateFiles(coreProject, "*.cs", SearchOption.AllDirectories)
                     .Where(file => IsRepositorySourcePath(file) &&
                                    !Path.GetFullPath(file).Equals(sqliteManagerPath, StringComparison.OrdinalIgnoreCase)))
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbiddenSqliteTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden direct sqlite dependency {token}.");
            }
        }

        string[] forbiddenStaticLookups = ["Card." + "Get(", "NamedCard." + "Get("];
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(IsRepositorySourcePath))
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbiddenStaticLookups)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden static card lookup.");
            }
        }
    }

    [Fact]
    public void ProjectBoundariesSplitCoreProtocolAndServer()
    {
        string root = FindRepositoryRoot();
        string coreProject = Path.Combine(root, "YGOProSharp.Core");
        string protocolProject = Path.Combine(root, "YGOProSharp.Protocol");
        string serverProject = Path.Combine(root, "YGOProSharp.Server");
        string windBotProject = Path.Combine(root, "YGOProSharp.WindBot");
        string cliProject = Path.Combine(root, "YGOProSharp.Cli");

        AssertDoesNotContainAny(
            ProjectText(coreProject),
            ["YGOProSharp.Protocol", "YGOProSharp.Server", "SevenZip", "YGOClient", "CtosMessage", "StocMessage"],
            "Core project boundary");

        AssertDoesNotContainAny(
            ProjectText(protocolProject),
            ["YGOProSharp.Core", "YGOProSharp.Server", "YGOProSharp.NativeApi", "Microsoft.Data.Sqlite", "SqliteConnection"],
            "Protocol project boundary");

        string serverProjectFile = File.ReadAllText(Path.Combine(serverProject, "YGOProSharp.Server.csproj"));
        foreach (string requiredReference in new[] { "YGOProSharp.Core", "YGOProSharp.Protocol", "YGOProSharp.NativeApi" })
        {
            if (!serverProjectFile.Contains(requiredReference, StringComparison.Ordinal))
                throw new InvalidOperationException($"Server project must reference {requiredReference}.");
        }

        AssertDoesNotContainAny(
            ProjectText(serverProject),
            ["Config.Load", "RunAsync(string[] args", "CliConfiguration", "CliOptionsFactory"],
            "Server library entry boundary");

        string nonCliEntryText = string.Join(Environment.NewLine, [
            ProjectText(coreProject),
            ProjectText(protocolProject),
            ProjectText(serverProject),
            ProjectText(windBotProject)
        ]);
        AssertDoesNotContainAny(
            nonCliEntryText,
            ["Console.CancelKeyPress", "AddSimpleConsole", "CliConfiguration", "CliOptionsFactory"],
            "Non-CLI entry boundary");

        string cliText = ProjectText(cliProject);
        foreach (string requiredCliToken in new[] { "Console.CancelKeyPress", "AddSimpleConsole", "CliConfiguration", "CliOptionsFactory" })
        {
            if (!cliText.Contains(requiredCliToken, StringComparison.Ordinal))
                throw new InvalidOperationException($"CLI project should own {requiredCliToken}.");
        }
    }

    [Fact]
    public void ProjectBoundariesKeepWindBotDecoupled()
    {
        string root = FindRepositoryRoot();
        string windBotProject = Path.Combine(root, "YGOProSharp.WindBot");

        string sourceText = ProjectText(windBotProject);
        string consolePrefix = "Console.";
        string consoleErrorPrefix = consolePrefix + "Error.";
        AssertDoesNotContainAny(
            sourceText,
            ["YGOSharp", "MDPro3", "YGOProSharp.Server", "YGOProSharp.NativeApi", consolePrefix + "WriteLine", consoleErrorPrefix + "WriteLine"],
            "WindBot project boundary");

        string projectFile = File.ReadAllText(Path.Combine(windBotProject, "YGOProSharp.WindBot.csproj"));
        AssertDoesNotContainAny(
            projectFile,
            ["OutputType", "StartupObject", "Microsoft.Extensions.Logging.Console"],
            "WindBot library project boundary");

        AssertDoesNotContainAny(
            sourceText,
            ["static void Main", "Environment.Exit", "Config.Load"],
            "WindBot library entry boundary");

        foreach (string requiredReference in new[] { "YGOProSharp.Abstractions", "YGOProSharp.Core", "YGOProSharp.Protocol" })
        {
            if (!projectFile.Contains(requiredReference, StringComparison.Ordinal))
                throw new InvalidOperationException($"WindBot project must reference {requiredReference}.");
        }
    }

    [Fact]
    public void ProjectBoundariesKeepDirectConsoleWritesOutOfSource()
    {
        string root = FindRepositoryRoot();
        string consolePrefix = "Console.";
        string consoleErrorPrefix = consolePrefix + "Error.";
        string[] forbiddenWrites =
        [
            consolePrefix + "WriteLine",
            consoleErrorPrefix + "WriteLine",
            consolePrefix + "Write(",
            consoleErrorPrefix + "Write("
        ];

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(IsRepositorySourcePath))
        {
            string text = File.ReadAllText(file);
            foreach (string forbiddenWrite in forbiddenWrites)
            {
                if (text.Contains(forbiddenWrite, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden direct console write {forbiddenWrite}.");
            }
        }
    }

    [Fact]
    public void ProjectBoundariesKeepLoggerParametersOutOfBusinessApis()
    {
        string root = FindRepositoryRoot();
        string[] businessProjects =
        [
            Path.Combine(root, "YGOProSharp.Core"),
            Path.Combine(root, "YGOProSharp.Protocol"),
            Path.Combine(root, "YGOProSharp.Server")
        ];
        string appLogPath = Path.Combine(root, "YGOProSharp.Abstractions", "Logging", "AppLog.cs");
        string[] forbiddenParameterTokens = ["ILogger ", "ILogger<", "ILoggerFactory"];

        foreach (string file in businessProjects.SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
                     .Where(file => IsRepositorySourcePath(file) &&
                                    !Path.GetFullPath(file).Equals(appLogPath, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string line in File.ReadLines(file))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("public ", StringComparison.Ordinal))
                    continue;
                if (!trimmed.Contains('('))
                    continue;

                foreach (string token in forbiddenParameterTokens)
                {
                    if (trimmed.Contains(token, StringComparison.Ordinal))
                        throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} exposes logger parameter in public API.");
                }
            }
        }
    }

    [Fact]
    public void ProjectBoundariesKeepConcreteLuaLibrariesOut()
    {
        string root = FindRepositoryRoot();
        string[] projectFiles =
        [
            Path.Combine(root, "YGOProSharp.Abstractions", "YGOProSharp.Abstractions.csproj"),
            Path.Combine(root, "YGOProSharp.WindBot", "YGOProSharp.WindBot.csproj")
        ];
        string[] forbiddenLuaPackages = ["XLua", "NLua", "MoonSharp", "KeraLua", "LuaCSharp", "Lua.CSharp", "SLua", "ToLua"];

        foreach (string projectFile in projectFiles)
        {
            string xml = File.ReadAllText(projectFile);
            AssertDoesNotContainAny(xml, forbiddenLuaPackages, Path.GetRelativePath(root, projectFile));
        }
    }

    [Fact]
    public void ProjectBoundariesIsolatePersistenceWebAndForbidLlmSdks()
    {
        string root = FindRepositoryRoot();
        string[] coreDomainProjects =
        [
            Path.Combine(root, "YGOProSharp.Abstractions"),
            Path.Combine(root, "YGOProSharp.Core"),
            Path.Combine(root, "YGOProSharp.Protocol"),
            Path.Combine(root, "YGOProSharp.NativeApi"),
            Path.Combine(root, "YGOProSharp.Server"),
            Path.Combine(root, "YGOProSharp.WindBot")
        ];

        string[] forbiddenOperationalRefs =
        [
            "YGOProSharp.Persistence",
            "YGOProSharp.Web",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "StackExchange.Redis",
            "Konscious.Security.Cryptography"
        ];

        foreach (string projectDir in coreDomainProjects)
        {
            AssertDoesNotContainAny(
                ProjectText(projectDir),
                forbiddenOperationalRefs,
                $"{Path.GetFileName(projectDir)} operational isolation");
        }

        string persistenceDir = Path.Combine(root, "YGOProSharp.Persistence");
        AssertDoesNotContainAny(
            ProjectText(persistenceDir),
            ["YGOProSharp.Protocol", "YGOProSharp.Server", "YGOProSharp.NativeApi", "YGOProSharp.WindBot", "YGOProSharp.Web"],
            "Persistence upward dependency isolation");

        string agentBridgeDir = Path.Combine(root, "YGOProSharp.AgentBridge");
        AssertDoesNotContainAny(
            ProjectText(agentBridgeDir),
            ["YGOProSharp.Server", "YGOProSharp.NativeApi", "YGOProSharp.Persistence", "YGOProSharp.Web", "YGOProSharp.Mcp"],
            "AgentBridge pure protocol isolation");

        string[] forbiddenLlmSdkTokens =
        [
            "PackageReference Include=\"OpenAI",
            "PackageReference Include=\"Anthropic",
            "PackageReference Include=\"Google.GenAI",
            "PackageReference Include=\"Microsoft.SemanticKernel",
            "PackageReference Include=\"Microsoft.Extensions.AI.OpenAI"
        ];
        foreach (string csproj in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                     .Where(IsRepositorySourcePath))
        {
            string xml = File.ReadAllText(csproj);
            AssertDoesNotContainAny(xml, forbiddenLlmSdkTokens, Path.GetRelativePath(root, csproj));
        }
    }

}
