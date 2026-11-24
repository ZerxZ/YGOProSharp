using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 暴露给 Lua 对话脚本回调的上下文对象（<c>Self</c> / <c>Dialog</c> / 回调首参 <c>ctx</c>）。
    /// 保持纯公开属性与简单方法签名，便于 XLua <c>[LuaCallCSharp]</c>、NLua 与 MoonSharp 零反射开销调用。
    /// </summary>
    public sealed class LuaDialogContext
    {
        private readonly Dialogs _dialogs;
        private readonly ILuaScriptContext _luaContext;

        internal LuaDialogContext(
            Dialogs dialogs,
            GameClient game,
            Duel duel,
            ILuaScriptContext luaContext,
            string dialogName,
            string locale,
            string scriptChunkName)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Duel = duel;
            _luaContext = luaContext;
            DialogName = dialogName ?? "default";
            Locale = string.IsNullOrWhiteSpace(locale) ? string.Empty : locale.Trim();
            ScriptChunkName = scriptChunkName ?? "LuaDialog.lua";
        }

        /// <summary>
        /// 当前 Bot 客户端实例。
        /// </summary>
        public GameClient Game { get; }

        /// <summary>
        /// 当前对局状态（在独立构造 <see cref="Dialogs"/> 且未传入 <see cref="Duel"/> 时可能为 <c>null</c>）。
        /// </summary>
        public Duel Duel { get; }

        /// <summary>
        /// 己方场地快照（当 <see cref="Duel"/> 可用时）。
        /// </summary>
        public ClientField BotField => Duel?.Fields != null && Duel.Fields.Length > 0 ? Duel.Fields[0] : null;

        /// <summary>
        /// 对方场地快照（当 <see cref="Duel"/> 可用时）。
        /// </summary>
        public ClientField EnemyField => Duel?.Fields != null && Duel.Fields.Length > 1 ? Duel.Fields[1] : null;

        /// <summary>
        /// 当前回合数（<see cref="Duel"/> 不可用时为 0）。
        /// </summary>
        public int Turn => Duel?.Turn ?? 0;

        /// <summary>
        /// 当前 Bot 昵称。
        /// </summary>
        public string BotName => Game.Username ?? string.Empty;

        /// <summary>
        /// 当前 Bot 卡组名。
        /// </summary>
        public string Deck => Game.Deck ?? string.Empty;

        /// <summary>
        /// 当前对话资源名。
        /// </summary>
        public string DialogName { get; }

        /// <summary>
        /// 当前生效的本地化区域标识（如 <c>"zh-CN"</c>、<c>"en-US"</c>；未配置时为空字符串）。
        /// </summary>
        public string Locale { get; }

        /// <summary>
        /// 判断当前对话区域是否匹配指定语言标签（支持精确匹配与主语言前缀匹配，如 <c>"zh"</c> 匹配 <c>"zh-CN"</c>）。
        /// </summary>
        public bool IsLocale(string localeTag)
        {
            return LuaDialogDsl.MatchesLocale(Locale, localeTag);
        }

        /// <summary>
        /// 当前脚本块名（用于日志与排错）。
        /// </summary>
        public string ScriptChunkName { get; }

        /// <summary>
        /// 里侧表示怪兽的称呼文本。
        /// </summary>
        public string FacedownMonsterName => _dialogs.FacedownMonsterName;

        /// <summary>
        /// 立即发送一条聊天消息（受 <see cref="GameClient._chat"/> 控制）。
        /// </summary>
        public void Say(string message)
        {
            if (Game._chat && !string.IsNullOrEmpty(message))
                Game.Chat(message);
        }

        /// <summary>
        /// 从 Lua 数组表或集合中随机挑选一条台词。
        /// </summary>
        public string Pick(object lines)
        {
            IList<string> resolved = ResolveStringList(lines);
            if (resolved == null || resolved.Count == 0)
                return string.Empty;

            return resolved[WindBotRuntime.Random.Next(resolved.Count)] ?? string.Empty;
        }

        /// <summary>
        /// 从指定事件（如 <c>"summon"</c>、<c>"attack"</c>）的静态台词池中随机抽取一条并可选格式化。
        /// </summary>
        public string PickEventLine(string eventName, object arg0 = null, object arg1 = null)
        {
            IReadOnlyList<string> lines = _dialogs.GetLines(eventName);
            if (lines == null || lines.Count == 0)
                return string.Empty;

            string template = lines[WindBotRuntime.Random.Next(lines.Count)];
            return Format(template, arg0, arg1);
        }

        /// <summary>
        /// 返回指定事件的静态台词数组（只读副本）。
        /// </summary>
        public IReadOnlyList<string> GetLines(string eventName)
        {
            return _dialogs.GetLines(eventName);
        }

        /// <summary>
        /// 使用 <see cref="string.Format(string, object[])"/> 格式化台词模板（兼容 <c>{0}</c>、<c>{1}</c> 占位符）。
        /// </summary>
        public string Format(string template, object arg0 = null, object arg1 = null)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            if (arg0 == null && arg1 == null)
                return template;

            try
            {
                return arg1 == null
                    ? string.Format(CultureInfo.CurrentCulture, template, arg0)
                    : string.Format(CultureInfo.CurrentCulture, template, arg0, arg1);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        public void LogInfo(string message)
        {
            Logger.WriteLine($"[LuaDialog:{ScriptChunkName}] {message}");
        }

        public void LogDebug(string message)
        {
            Logger.DebugWriteLine($"[LuaDialog:{ScriptChunkName}] {message}");
        }

        public void LogWarning(string message)
        {
            Logger.WriteErrorLine($"[LuaDialog:{ScriptChunkName}] {message}");
        }

        private IList<string> ResolveStringList(object value)
        {
            if (value == null)
                return null;

            if (value is string single)
                return [single];

            if (value is IList<string> stringList)
                return stringList;

            if (_luaContext != null && _luaContext.TryConvertList(value, out IList<string> converted) && converted != null)
                return converted;

            if (_luaContext != null && _luaContext.TryConvertList(value, out IList<object> objectList) && objectList != null)
            {
                List<string> results = new List<string>(objectList.Count);
                foreach (object item in objectList)
                {
                    if (item != null)
                        results.Add(item.ToString());
                }
                return results;
            }

            if (value is IEnumerable enumerable)
            {
                List<string> results = new List<string>();
                foreach (object item in enumerable)
                {
                    if (item != null)
                        results.Add(item.ToString());
                }
                return results;
            }

            return [value.ToString()];
        }
    }
}
