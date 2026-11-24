using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using WindBot.Game.AI.Lua;

namespace WindBot.Game.AI
{
    [DataContract]
    public class DialogsData
    {
        [DataMember]
        public string[] welcome { get; set; }
        [DataMember]
        public string[] deckerror { get; set; }
        [DataMember]
        public string[] duelstart { get; set; }
        [DataMember]
        public string[] newturn { get; set; }
        [DataMember]
        public string[] endturn { get; set; }
        [DataMember]
        public string[] directattack { get; set; }
        [DataMember]
        public string[] attack { get; set; }
        [DataMember]
        public string[] ondirectattack { get; set; }
        [DataMember]
        public string facedownmonstername { get; set; }
        [DataMember]
        public string[] activate { get; set; }
        [DataMember]
        public string[] summon { get; set; }
        [DataMember]
        public string[] setmonster { get; set; }
        [DataMember]
        public string[] chaining { get; set; }
        [DataMember]
        public string[] surrender { get; set; }
        [DataMember]
        public string[] custom { get; set; }
    }

    public class Dialogs : IDisposable
    {
        private readonly GameClient _game;
        private readonly Duel _duel;
        private LuaDialogScript _luaScript;
        private bool _disposed;

        private string[] _welcome = Array.Empty<string>();
        private string[] _deckerror = Array.Empty<string>();
        private string[] _duelstart = Array.Empty<string>();
        private string[] _newturn = Array.Empty<string>();
        private string[] _endturn = Array.Empty<string>();
        private string[] _directattack = Array.Empty<string>();
        private string[] _attack = Array.Empty<string>();
        private string[] _ondirectattack = Array.Empty<string>();
        private string _facedownmonstername = "monster";
        private string[] _activate = Array.Empty<string>();
        private string[] _summon = Array.Empty<string>();
        private string[] _setmonster = Array.Empty<string>();
        private string[] _chaining = Array.Empty<string>();
        private string[] _surrender = Array.Empty<string>();
        private string[] _custom = Array.Empty<string>();

        /// <summary>
        /// 当前使用的里侧怪兽称呼。
        /// </summary>
        public string FacedownMonsterName => _facedownmonstername;

        /// <summary>
        /// 当前对话生效的本地化区域标识（来自 <see cref="GameClient.Locale"/> 或 <see cref="WindBotRuntime.Locale"/>）。
        /// </summary>
        public string Locale { get; }

        /// <summary>
        /// 实际解析命中的对话资源名（用于日志与排错，如 <c>cirno.zh-CN</c> 或 <c>ExampleBlueEyes</c>）。
        /// </summary>
        public string ResolvedDialogName { get; }

        /// <summary>
        /// 若当前对话由 Lua 脚本加载，则返回对应的 <see cref="LuaDialogScript"/> 实例；JSON 对话返回 <c>null</c>。
        /// </summary>
        public LuaDialogScript LuaScript => _luaScript;

        public Dialogs(GameClient game)
            : this(game, null)
        {
        }

        public Dialogs(GameClient game, Duel duel)
        {
            _game = game;
            _duel = duel;

            string dialogName = string.IsNullOrWhiteSpace(game.Dialog) ? "default" : game.Dialog.Trim();
            string effectiveLocale = LuaDialogDsl.NormalizeLocale(
                string.IsNullOrWhiteSpace(game.Locale) ? WindBotRuntime.Locale : game.Locale);
            Locale = effectiveLocale ?? string.Empty;

            bool explicitLua = dialogName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);

            string resolvedJsonName = null;
            DialogsData jsonData = explicitLua
                ? null
                : TryReadLocalizedJsonDialog(dialogName, effectiveLocale, out resolvedJsonName);
            bool jsonExists = jsonData != null;

            if (DialogsManager.TryLoadLuaDialog(
                    this,
                    _game,
                    _duel,
                    dialogName,
                    effectiveLocale,
                    jsonExists,
                    out LuaDialogScript luaScript,
                    out LuaDialogDescriptor descriptor))
            {
                _luaScript = luaScript;
                ResolvedDialogName = luaScript.ScriptChunkName;
                try
                {
                    // 若声明了 fallback（脚本内 set_fallback 优先，其次描述符 FallbackDialog），先加载回退基线。
                    // 支持 set_fallback("auto") 自动按当前 Locale 匹配 <locale>.json -> default.json。
                    string fallbackName = !string.IsNullOrWhiteSpace(luaScript.FallbackDialog)
                        ? luaScript.FallbackDialog
                        : descriptor?.FallbackDialog;

                    if (!string.IsNullOrWhiteSpace(fallbackName))
                    {
                        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { dialogName };
                        ApplyFallbackChain(fallbackName, effectiveLocale, visited);
                    }

                    ApplyLuaDialogDefinitions(luaScript);
                    luaScript.RunInitAndFinalizeLoad();
                }
                catch
                {
                    Dispose();
                    throw;
                }

                return;
            }

            if (jsonData != null)
            {
                ResolvedDialogName = resolvedJsonName ?? dialogName;
                ApplyDialogsData(jsonData);
                return;
            }

            ResolvedDialogName = dialogName;
            // 未提供 Dialogs 资源时退化为空对话集，避免阻塞无对话文件场景下的连接与对局。
            Logger.DebugWriteLine($"Dialog file '{dialogName}.json' not found, using empty dialogs.");
        }

        /// <summary>
        /// 按事件名查询当前装配好的静态台词列表（支持 JSON 键名与 snake_case 别名）。
        /// </summary>
        public IReadOnlyList<string> GetLines(string eventName)
        {
            if (!LuaDialogHookNames.TryNormalizeEventKey(eventName, out string normalized))
                return Array.Empty<string>();

            return normalized switch
            {
                LuaDialogHookNames.Welcome => _welcome,
                LuaDialogHookNames.DeckError => _deckerror,
                LuaDialogHookNames.DuelStart => _duelstart,
                LuaDialogHookNames.NewTurn => _newturn,
                LuaDialogHookNames.EndTurn => _endturn,
                LuaDialogHookNames.DirectAttack => _directattack,
                LuaDialogHookNames.Attack => _attack,
                LuaDialogHookNames.OnDirectAttack => _ondirectattack,
                LuaDialogHookNames.Activate => _activate,
                LuaDialogHookNames.Summon => _summon,
                LuaDialogHookNames.SetMonster => _setmonster,
                LuaDialogHookNames.Chaining => _chaining,
                LuaDialogHookNames.Surrender => _surrender,
                LuaDialogHookNames.Custom => _custom,
                _ => Array.Empty<string>()
            };
        }

        public void SendSorry()
        {
            InternalSendMessageForced(new[] { "Sorry, an error occurs." });
        }

        public void SendDeckSorry(string card)
        {
            if (card == "DECK")
                InternalSendMessageForced(new[] { "Deck illegal. Please check the database of your YGOPro and WindBot." });
            else
                DispatchEvent(LuaDialogHookNames.DeckError, _deckerror, forced: true, card);
        }

        public void SendWelcome()
        {
            DispatchEvent(LuaDialogHookNames.Welcome, _welcome, forced: false);
        }

        public void SendDuelStart()
        {
            DispatchEvent(LuaDialogHookNames.DuelStart, _duelstart, forced: false);
        }

        public void SendNewTurn()
        {
            DispatchEvent(LuaDialogHookNames.NewTurn, _newturn, forced: false);
        }

        public void SendEndTurn()
        {
            DispatchEvent(LuaDialogHookNames.EndTurn, _endturn, forced: false);
        }

        public void SendDirectAttack(string attacker)
        {
            DispatchEvent(LuaDialogHookNames.DirectAttack, _directattack, forced: false, attacker);
        }

        public void SendAttack(string attacker, string defender)
        {
            if (defender == "monster")
            {
                defender = _facedownmonstername;
            }
            DispatchEvent(LuaDialogHookNames.Attack, _attack, forced: false, attacker, defender);
        }

        public void SendOnDirectAttack(string attacker)
        {
            if (string.IsNullOrEmpty(attacker))
            {
                attacker = _facedownmonstername;
            }
            DispatchEvent(LuaDialogHookNames.OnDirectAttack, _ondirectattack, forced: false, attacker);
        }

        public void SendOnDirectAttack()
        {
            DispatchEvent(LuaDialogHookNames.OnDirectAttack, _ondirectattack, forced: false);
        }

        public void SendActivate(string spell)
        {
            DispatchEvent(LuaDialogHookNames.Activate, _activate, forced: false, spell);
        }

        public void SendSummon(string monster)
        {
            DispatchEvent(LuaDialogHookNames.Summon, _summon, forced: false, monster);
        }

        public void SendSetMonster()
        {
            DispatchEvent(LuaDialogHookNames.SetMonster, _setmonster, forced: false);
        }

        public void SendChaining(string card)
        {
            DispatchEvent(LuaDialogHookNames.Chaining, _chaining, forced: false, card);
        }

        public void SendSurrender()
        {
            DispatchEvent(LuaDialogHookNames.Surrender, _surrender, forced: false);
        }

        public void SendCustomChat(int index, params object[] opts)
        {
            if (!_game._chat)
                return;

            if (_luaScript != null)
            {
                object[] hookArgs = BuildCustomHookArgs(index, opts);
                if (_luaScript.TryResolveEventMessage(LuaDialogHookNames.Custom, hookArgs, out bool suppress, out string dynamicMessage))
                {
                    if (!suppress && !string.IsNullOrEmpty(dynamicMessage))
                        EmitChatMessage(LuaDialogHookNames.Custom, dynamicMessage);
                    return;
                }
            }

            if (_custom == null || index < 0 || index >= _custom.Length)
                return;

            string message = FormatMessage(_custom[index], opts);
            if (!string.IsNullOrEmpty(message))
                EmitChatMessage(LuaDialogHookNames.Custom, message);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _luaScript?.Dispose();
            _luaScript = null;
        }

        private void DispatchEvent(string eventKey, IList<string> staticLines, bool forced, params object[] opts)
        {
            if (!forced && !_game._chat)
                return;

            if (_luaScript != null &&
                _luaScript.TryResolveEventMessage(eventKey, opts, out bool suppress, out string dynamicMessage))
            {
                if (!suppress && !string.IsNullOrEmpty(dynamicMessage))
                    EmitChatMessage(eventKey, dynamicMessage);
                return;
            }

            if (staticLines == null || staticLines.Count == 0)
                return;

            string message = FormatMessage(staticLines[WindBotRuntime.Random.Next(staticLines.Count)], opts);
            if (!string.IsNullOrEmpty(message))
                EmitChatMessage(eventKey, message);
        }

        private void EmitChatMessage(string eventKey, string message)
        {
            if (_luaScript != null &&
                !_luaScript.TryTransformOutgoingMessage(eventKey, message, out message))
            {
                return;
            }

            if (!string.IsNullOrEmpty(message))
                _game.Chat(message);
        }

        private void InternalSendMessageForced(IList<string> array, params object[] opts)
        {
            if (array == null || array.Count == 0)
                return;
            string message = FormatMessage(array[WindBotRuntime.Random.Next(array.Count)], opts);
            if (!string.IsNullOrEmpty(message))
                _game.Chat(message);
        }

        private static string FormatMessage(string template, object[] opts)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            if (opts == null || opts.Length == 0)
                return template;

            try
            {
                return string.Format(template, opts);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        private static object[] BuildCustomHookArgs(int index, object[] opts)
        {
            if (opts == null || opts.Length == 0)
                return [index];

            object[] args = new object[opts.Length + 1];
            args[0] = index;
            Array.Copy(opts, 0, args, 1, opts.Length);
            return args;
        }

        private void ApplyFallbackChain(string fallbackName, string effectiveLocale, HashSet<string> visited)
        {
            if (string.IsNullOrWhiteSpace(fallbackName))
                return;

            string resolvedTarget = string.Equals(fallbackName, "auto", StringComparison.OrdinalIgnoreCase)
                ? "default"
                : fallbackName.Trim();

            if (!visited.Add(resolvedTarget))
                return;

            bool explicitLua = resolvedTarget.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
            DialogsData jsonData = explicitLua
                ? null
                : TryReadLocalizedJsonDialog(resolvedTarget, effectiveLocale, out _);

            if (DialogsManager.TryLoadLuaDialog(
                    this,
                    _game,
                    _duel,
                    resolvedTarget,
                    effectiveLocale,
                    jsonExists: jsonData != null,
                    out LuaDialogScript fallbackLua,
                    out LuaDialogDescriptor fallbackDescriptor))
            {
                using (fallbackLua)
                {
                    string nestedFallback = !string.IsNullOrWhiteSpace(fallbackLua.FallbackDialog)
                        ? fallbackLua.FallbackDialog
                        : fallbackDescriptor?.FallbackDialog;

                    if (!string.IsNullOrWhiteSpace(nestedFallback))
                        ApplyFallbackChain(nestedFallback, effectiveLocale, visited);

                    ApplyLuaDialogDefinitions(fallbackLua);
                }

                return;
            }

            if (jsonData != null)
                ApplyDialogsData(jsonData);
        }

        private static DialogsData TryReadLocalizedJsonDialog(string dialogName, string locale, out string resolvedName)
        {
            IReadOnlyList<string> candidates = LuaDialogDsl.ExpandLocalizedDialogCandidates(dialogName, locale);
            for (int i = 0; i < candidates.Count; i++)
            {
                DialogsData found = TryReadJsonDialog(candidates[i]);
                if (found != null)
                {
                    resolvedName = candidates[i];
                    return found;
                }
            }

            resolvedName = null;
            return null;
        }

        private static DialogsData TryReadJsonDialog(string dialogName)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(DialogsData));
            try
            {
                using FileStream fs = WindBotRuntime.ReadFile("Dialogs", dialogName, "json");
                return (DialogsData)serializer.ReadObject(fs);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }

        private void ApplyDialogsData(DialogsData data)
        {
            if (data == null)
                return;

            _welcome = data.welcome ?? Array.Empty<string>();
            _deckerror = data.deckerror ?? Array.Empty<string>();
            _duelstart = data.duelstart ?? Array.Empty<string>();
            _newturn = data.newturn ?? Array.Empty<string>();
            _endturn = data.endturn ?? Array.Empty<string>();
            _directattack = data.directattack ?? Array.Empty<string>();
            _attack = data.attack ?? Array.Empty<string>();
            _ondirectattack = data.ondirectattack ?? Array.Empty<string>();
            _facedownmonstername = string.IsNullOrEmpty(data.facedownmonstername) ? "monster" : data.facedownmonstername;
            _activate = data.activate ?? Array.Empty<string>();
            _summon = data.summon ?? Array.Empty<string>();
            _setmonster = data.setmonster ?? Array.Empty<string>();
            _chaining = data.chaining ?? Array.Empty<string>();
            _surrender = data.surrender ?? Array.Empty<string>();
            _custom = data.custom ?? Array.Empty<string>();
        }

        private void ApplyLuaDialogDefinitions(LuaDialogScript script)
        {
            if (!string.IsNullOrEmpty(script.FacedownMonsterName))
                _facedownmonstername = script.FacedownMonsterName;

            foreach (KeyValuePair<string, string[]> entry in script.DefinedLines)
            {
                string[] lines = entry.Value ?? Array.Empty<string>();
                switch (entry.Key)
                {
                    case LuaDialogHookNames.Welcome:
                        _welcome = lines;
                        break;
                    case LuaDialogHookNames.DeckError:
                        _deckerror = lines;
                        break;
                    case LuaDialogHookNames.DuelStart:
                        _duelstart = lines;
                        break;
                    case LuaDialogHookNames.NewTurn:
                        _newturn = lines;
                        break;
                    case LuaDialogHookNames.EndTurn:
                        _endturn = lines;
                        break;
                    case LuaDialogHookNames.DirectAttack:
                        _directattack = lines;
                        break;
                    case LuaDialogHookNames.Attack:
                        _attack = lines;
                        break;
                    case LuaDialogHookNames.OnDirectAttack:
                        _ondirectattack = lines;
                        break;
                    case LuaDialogHookNames.Activate:
                        _activate = lines;
                        break;
                    case LuaDialogHookNames.Summon:
                        _summon = lines;
                        break;
                    case LuaDialogHookNames.SetMonster:
                        _setmonster = lines;
                        break;
                    case LuaDialogHookNames.Chaining:
                        _chaining = lines;
                        break;
                    case LuaDialogHookNames.Surrender:
                        _surrender = lines;
                        break;
                    case LuaDialogHookNames.Custom:
                        _custom = lines;
                        break;
                }
            }
        }
    }
}
