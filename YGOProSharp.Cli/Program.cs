using Microsoft.Extensions.Logging;
using YGOProSharp;
using YGOProSharp.NativeApi;

using CancellationTokenSource shutdown = new();
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
});
using NativeOcgRuntime runtime = new();
ILogger logger = loggerFactory.CreateLogger("YGOProSharp.Cli");

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown?.Cancel();
};

try
{
    await YGOProSharpServer.RunAsync(args, runtime, loggerFactory, shutdown.Token);
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    string crashFile = $"crash_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.txt";
    await File.WriteAllTextAsync(crashFile, ex.ToString());
    logger.LogCritical(ex, "YGOProSharp crashed. Crash details were written to {CrashFile}.", crashFile);
    return 1;
}
