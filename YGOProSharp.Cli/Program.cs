using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.NativeApi;
using YGOProSharp.Server;

using CancellationTokenSource shutdown = new();
string? rawLogLevel = GetLogLevelValue(args);
LogLevel logLevel = AppLog.ParseLevel(rawLogLevel);
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(logLevel);
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
});
AppLog.Configure(loggerFactory);
using NativeOcgRuntime runtime = new();
ILogger logger = AppLog.CreateLogger("YGOProSharp.Cli");
if (!string.IsNullOrWhiteSpace(rawLogLevel) && !Enum.TryParse(rawLogLevel, ignoreCase: true, out LogLevel _))
    logger.LogWarning("Invalid LogLevel '{LogLevel}', falling back to {DefaultLogLevel}.", rawLogLevel, logLevel);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    logger.LogInformation("Shutdown requested from console.");
    shutdown.Cancel();
};

try
{
    logger.LogInformation("Starting YGOProSharp CLI with log level {LogLevel}.", logLevel);
    await YGOProSharpServer.RunAsync(args, runtime, shutdown.Token);
    logger.LogInformation("YGOProSharp CLI stopped.");
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    string crashFile = $"crash_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.txt";
    await File.WriteAllTextAsync(crashFile, ex.ToString());
    logger.LogCritical(ex, "YGOProSharp crashed. Crash details were written to {CrashFile}.", crashFile);
    return 1;
}

static string? GetLogLevelValue(string[] args)
{
    return args
        .Select(arg => arg.Split('=', 2, StringSplitOptions.TrimEntries))
        .Where(parts => parts.Length == 2 && parts[0].Equals("LogLevel", StringComparison.OrdinalIgnoreCase))
        .Select(parts => parts[1])
        .LastOrDefault();
}
