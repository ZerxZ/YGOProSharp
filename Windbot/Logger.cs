using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace WindBot
{
    public static class Logger
    {
        private static readonly ILogger Log = AppLog.CreateLogger("YGOProSharp.WindBot");

        public static void WriteLine(string message)
        {
            Log.LogInformation("{WindBotMessage}", message);
        }

        public static void DebugWriteLine(string message)
        {
            Log.LogDebug("{WindBotMessage}", message);
        }

        public static void WriteErrorLine(string message)
        {
            Log.LogError("{WindBotMessage}", message);
        }
    }
}
