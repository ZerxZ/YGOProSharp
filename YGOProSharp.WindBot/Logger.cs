using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace WindBot
{
    public static class Logger
    {
        private static readonly ILogger Log = AppLog.CreateLogger("YGOProSharp.WindBot");
        private static readonly AsyncLocal<Func<string>> _contextProvider = new();

        public static void SetContext(Func<string> contextProvider)
        {
            _contextProvider.Value = contextProvider;
        }

        public static void ClearContext()
        {
            _contextProvider.Value = null;
        }

        public static void WriteLine(string message)
        {
            Log.LogInformation("{WindBotMessage}", message);
        }

        public static void DebugWriteLine(string message, bool printStackTrace = false)
        {
            if (!Log.IsEnabled(LogLevel.Debug))
                return;

            if (printStackTrace)
                Log.LogDebug("{WindBotMessage}\n{StackTrace}", message, new StackTrace(1, true).ToString());
            else
                Log.LogDebug("{WindBotMessage}", message);
        }

        public static void WriteErrorLine(string message)
        {
            string context = _contextProvider.Value?.Invoke();
            if (!string.IsNullOrEmpty(context))
                Log.LogError("{WindBotMessage} | Context: {WindBotContext}", message, context);
            else
                Log.LogError("{WindBotMessage}", message);
        }

        public static void WriteErrorLine(string message, Exception exception)
        {
            string context = _contextProvider.Value?.Invoke();
            if (!string.IsNullOrEmpty(context))
                Log.LogError(exception, "{WindBotMessage} | Context: {WindBotContext}", message, context);
            else
                Log.LogError(exception, "{WindBotMessage}", message);
        }
    }
}
