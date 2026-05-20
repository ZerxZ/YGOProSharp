using YGOProSharp;
using YGOProSharp.NativeApi;

using CancellationTokenSource shutdown = new();
using NativeOcgRuntime runtime = new();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown?.Cancel();
};

try
{
    await YGOProSharpServer.RunAsync(args, runtime, shutdown.Token);
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    string crashFile = $"crash_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.txt";
    await File.WriteAllTextAsync(crashFile, ex.ToString());
    Console.Error.WriteLine(ex);
    return 1;
}
