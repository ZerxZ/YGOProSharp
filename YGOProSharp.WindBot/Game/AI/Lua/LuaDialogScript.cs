using System;
using System.Collections;
using System.Collections.Generic;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 基于泛用 Lua 抽象接口（<see cref="ILuaRuntime"/> / <see cref="ILuaScriptContext"/>）加载与执行的 WindBot 对话脚本宿主。
    /// 同时支持三种写法：
    /// 1) xmake 风格声明式 DSL（<c>dialog("...") ... add_summon(...) ... on_summon(...) ... dialog_end()</c>）；
    /// 2) 模块表返回式（<c>return { welcome = {...}, OnSummon = function(ctx, monster) ... end }</c>）；
    /// 3) 全局函数式（<c>function OnSummon(ctx, monster) ... end</c>）。
    /// </summary>
    [GenerateLuaDialogBindings]
    public sealed partial class LuaDialogScript : IDisposable
    {
        private ILuaScriptContext _context;
        private ILuaTable _moduleTable;
        private readonly ILuaScriptSourceProvider _scriptProvider;
        private readonly HashSet<string> _includedScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ILuaFunction> _ownedFunctions = new();
        private readonly Dictionary<string, Func<object[], object>> _eventHooks = new(StringComparer.OrdinalIgnoreCase);
        private Func<object[], object> _onInitHook;
        private Func<object[], object> _onMessageHook;
        private Func<object[], object> _onDisposeHook;
        private bool _initialized;
        private bool _throwOnScriptError;
        private bool _disposed;

        /// <summary>
        /// 当前脚本块名，用于日志与排错定位。
        /// </summary>
        public string ScriptChunkName { get; }

        /// <summary>
        /// xmake 风格声明式 DSL 宿主，包含脚本声明的对话元数据与静态台词。
        /// </summary>
        public LuaDialogDsl Dsl { get; }

        /// <summary>
        /// 暴露给 Lua 回调的上下文实例。
        /// </summary>
        public LuaDialogContext Context { get; }

        /// <summary>
        /// 脚本声明的静态台词表（仅包含脚本显式定义或清空的事件键）。
        /// </summary>
        public IReadOnlyDictionary<string, string[]> DefinedLines { get; }

        /// <summary>
        /// 脚本声明的里侧怪兽称呼（未声明时为 <c>null</c>）。
        /// </summary>
        public string FacedownMonsterName { get; }

        /// <summary>
        /// 脚本声明的回退对话名（未声明时为 <c>null</c>）。
        /// </summary>
        public string FallbackDialog { get; }

        /// <summary>
        /// 是否存在需要在对局期间保持活跃的动态钩子。
        /// </summary>
        public bool HasDynamicHooks => _eventHooks.Count > 0 || _onMessageHook != null || _onDisposeHook != null;

        /// <summary>
        /// 是否已释放底层 Lua 资源。
        /// </summary>
        public bool IsDisposed => _disposed;

        internal LuaDialogScript(
            Dialogs dialogs,
            GameClient game,
            Duel duel,
            ILuaRuntime runtime,
            string scriptSource,
            string dialogName,
            string locale,
            string chunkName,
            bool throwOnScriptError,
            ILuaScriptSourceProvider scriptProvider = null)
        {
            ArgumentNullException.ThrowIfNull(dialogs);
            ArgumentNullException.ThrowIfNull(game);
            ArgumentNullException.ThrowIfNull(runtime);
            if (string.IsNullOrWhiteSpace(scriptSource))
                throw new ArgumentException("Lua dialog script source cannot be null or whitespace.", nameof(scriptSource));

            ScriptChunkName = string.IsNullOrWhiteSpace(chunkName) ? "LuaDialog.lua" : chunkName;
            _scriptProvider = scriptProvider;
            _includedScripts.Add(ScriptChunkName);
            _throwOnScriptError = throwOnScriptError;

            _context = runtime.CreateContext(ScriptChunkName)
                ?? throw new InvalidOperationException($"Lua runtime returned a null context for '{ScriptChunkName}'.");

            try
            {
                Dsl = new LuaDialogDsl(_context, dialogName, locale, ExecuteInclude);
                Context = new LuaDialogContext(dialogs, game, duel, _context, dialogName, locale, ScriptChunkName);

                BindHostGlobals(game, duel);
                Dsl.BindGlobals();

                object scriptResult = _context.DoString(scriptSource, ScriptChunkName);
                _moduleTable = scriptResult as ILuaTable;

                if (Dsl.DeclaredThrowOnError.HasValue)
                    _throwOnScriptError = Dsl.DeclaredThrowOnError.Value;

                // 汇总静态台词与元数据（DSL 声明优先于返回表字段）
                Dictionary<string, string[]> definedLines = new(StringComparer.OrdinalIgnoreCase);
                string facedownName = Dsl.DeclaredFacedownMonsterName;
                string fallback = Dsl.DeclaredFallback;

                if (_moduleTable != null)
                {
                    if (string.IsNullOrEmpty(facedownName) &&
                        _moduleTable.TryGet(LuaDialogHookNames.FacedownMonsterName, out object tableFacedown) &&
                        tableFacedown != null)
                    {
                        facedownName = tableFacedown.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(fallback) &&
                        _moduleTable.TryGet("fallback", out object tableFallback) &&
                        tableFallback != null)
                    {
                        fallback = tableFallback.ToString()?.Trim();
                    }

                    foreach (string eventKey in AllEventKeys)
                    {
                        if (_moduleTable.TryGet(eventKey, out object rawLines) && rawLines != null)
                        {
                            List<string> parsed = new List<string>();
                            Dsl.AppendResolvedStrings(parsed, rawLines);
                            definedLines[eventKey] = parsed.ToArray();
                        }
                    }
                }

                foreach (string eventKey in Dsl.DefinedEvents)
                {
                    definedLines[eventKey] = Dsl.LinesByEvent.TryGetValue(eventKey, out List<string> list)
                        ? list.ToArray()
                        : Array.Empty<string>();
                }

                DefinedLines = definedLines;
                FacedownMonsterName = facedownName;
                FallbackDialog = string.IsNullOrWhiteSpace(fallback) ? null : fallback;

                // 解析所有动态钩子（DSL -> 模块表 -> 全局函数）
                _onInitHook = ResolveHook(LuaDialogHookNames.OnInit);
                _onDisposeHook = ResolveHook(LuaDialogHookNames.OnDispose);
                _onMessageHook = ResolveHook(LuaDialogHookNames.OnMessage);

                foreach (string eventKey in AllEventKeys)
                {
                    string hookName = LuaDialogHookNames.GetHookNameForEvent(eventKey);
                    Func<object[], object> invoker = ResolveHook(hookName);
                    if (invoker != null)
                        _eventHooks[eventKey] = invoker;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 在 <see cref="Dialogs"/> 完成回退合并与静态台词装配后触发 <c>on_init</c>，并在纯静态脚本场景下立即释放底层 Lua 上下文。
        /// </summary>
        internal void RunInitAndFinalizeLoad()
        {
            if (_disposed || _initialized)
                return;

            _initialized = true;

            try
            {
                if (_onInitHook != null)
                {
                    Func<object[], object> initHook = _onInitHook;
                    _onInitHook = null;
                    InvokeHookSafely(initHook, LuaDialogHookNames.OnInit, [Context]);
                }

                // 若脚本为纯静态台词声明（无任何运行期钩子），加载完毕即可释放底层 Lua 上下文，避免未手动 Dispose 时残留资源。
                if (!HasDynamicHooks)
                    ReleaseLuaResources();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 尝试通过动态 Lua 钩子解析事件台词。
        /// 约定：返回 <c>nil</c> 回退静态台词池；返回 <c>false</c> 或空字符串静默跳过；返回非空字符串或字符串数组则发送。
        /// </summary>
        public bool TryResolveEventMessage(string eventKey, object[] args, out bool suppressMessage, out string message)
        {
            suppressMessage = false;
            message = null;

            if (_disposed || !_eventHooks.TryGetValue(eventKey, out Func<object[], object> hook))
                return false;

            object[] invokeArgs = BuildInvokeArgs(args);
            object rawResult = InvokeHookSafely(hook, LuaDialogHookNames.GetHookNameForEvent(eventKey), invokeArgs);
            return InterpretHookResult(rawResult, out suppressMessage, out message);
        }

        /// <summary>
        /// 若脚本注册了 <c>on_message(ctx, eventName, text)</c>，在最终发送前进行拦截或改写。
        /// </summary>
        public bool TryTransformOutgoingMessage(string eventKey, string currentMessage, out string transformedMessage)
        {
            transformedMessage = currentMessage;
            if (_disposed || _onMessageHook == null)
                return true;

            object rawResult = InvokeHookSafely(
                _onMessageHook,
                LuaDialogHookNames.OnMessage,
                [Context, eventKey, currentMessage]);

            if (rawResult == null)
                return true;

            if (rawResult is bool b && !b)
                return false;

            if (InterpretHookResult(rawResult, out bool suppress, out string nextMessage))
            {
                if (suppress || string.IsNullOrEmpty(nextMessage))
                    return false;

                transformedMessage = nextMessage;
                return true;
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_onDisposeHook != null)
            {
                try
                {
                    InvokeHookSafely(_onDisposeHook, LuaDialogHookNames.OnDispose, [Context]);
                }
                catch (Exception ex)
                {
                    Logger.WriteErrorLine($"[LuaDialog:{ScriptChunkName}] Error in OnDispose: {ex}");
                }
            }

            _eventHooks.Clear();
            _onInitHook = null;
            _onMessageHook = null;
            _onDisposeHook = null;
            ReleaseLuaResources();
        }

        private void ReleaseLuaResources()
        {
            for (int i = _ownedFunctions.Count - 1; i >= 0; i--)
            {
                try
                {
                    _ownedFunctions[i]?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"[LuaDialog:{ScriptChunkName}] Failed to dispose Lua function: {ex.Message}");
                }
            }

            _ownedFunctions.Clear();

            if (_moduleTable != null)
            {
                try
                {
                    _moduleTable.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"[LuaDialog:{ScriptChunkName}] Failed to dispose Lua module table: {ex.Message}");
                }

                _moduleTable = null;
            }

            if (_context != null)
            {
                try
                {
                    _context.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.DebugWriteLine($"[LuaDialog:{ScriptChunkName}] Failed to dispose Lua context: {ex.Message}");
                }

                _context = null;
            }
        }

        private void ExecuteInclude(string includeName)
        {
            if (_scriptProvider == null)
                throw new InvalidOperationException($"Cannot include '{includeName}' without an ILuaScriptSourceProvider.");

            if (!_scriptProvider.TryGetScriptSource(includeName, out string includeSource, out string includeChunk))
                throw new System.IO.FileNotFoundException($"Included Lua dialog script '{includeName}' not found.", includeName);

            string normalizedKey = string.IsNullOrWhiteSpace(includeChunk) ? includeName : includeChunk;
            if (!_includedScripts.Add(normalizedKey))
                return;

            object includeResult = _context.DoString(includeSource, normalizedKey);
            if (includeResult is ILuaTable disposableTable)
                disposableTable.Dispose();
        }

        private void BindHostGlobals(GameClient game, Duel duel)
        {
            _context.SetGlobal("Self", Context);
            _context.SetGlobal("Dialog", Context);
            _context.SetGlobal("Game", game);
            _context.SetGlobal("Duel", duel);
            _context.SetGlobal("Bot", Context.BotField);
            _context.SetGlobal("Enemy", Context.EnemyField);
        }

        private Func<object[], object> ResolveHook(string hookName)
        {
            if (string.IsNullOrWhiteSpace(hookName))
                return null;

            if (Dsl.RegisteredHooks.TryGetValue(hookName, out object dslHook))
                return CreateInvoker(dslHook, hookName);

            if (_moduleTable != null && _moduleTable.TryGetFunction(hookName, out ILuaFunction moduleFn) && moduleFn != null)
            {
                _ownedFunctions.Add(moduleFn);
                return args => CallLuaFunction(moduleFn, args);
            }

            if (_context.TryGetFunction(hookName, out ILuaFunction globalFn) && globalFn != null)
            {
                _ownedFunctions.Add(globalFn);
                return args => CallLuaFunction(globalFn, args);
            }

            return null;
        }

        private Func<object[], object> CreateInvoker(object callback, string hookName)
        {
            if (callback == null)
                return null;

            if (callback is ILuaFunction luaFn)
            {
                _ownedFunctions.Add(luaFn);
                return args => CallLuaFunction(luaFn, args);
            }

            if (callback is string functionName)
            {
                string trimmed = functionName.Trim();
                if (_moduleTable != null && _moduleTable.TryGetFunction(trimmed, out ILuaFunction moduleFn) && moduleFn != null)
                {
                    _ownedFunctions.Add(moduleFn);
                    return args => CallLuaFunction(moduleFn, args);
                }

                if (_context.TryGetFunction(trimmed, out ILuaFunction globalFn) && globalFn != null)
                {
                    _ownedFunctions.Add(globalFn);
                    return args => CallLuaFunction(globalFn, args);
                }

                throw new MissingMethodException($"Lua dialog hook '{hookName}' references missing Lua function '{trimmed}'.");
            }

            if (_context.TryWrapFunction(callback, out ILuaFunction wrapped) && wrapped != null)
            {
                _ownedFunctions.Add(wrapped);
                return args => CallLuaFunction(wrapped, args);
            }

            if (callback is Delegate del)
            {
                return args => InvokeClrDelegate(del, args);
            }

            throw new ArgumentException(
                $"Unsupported callback type '{callback.GetType().Name}' for Lua dialog hook '{hookName}'.",
                nameof(callback));
        }

        private static object CallLuaFunction(ILuaFunction function, object[] args)
        {
            return args.Length switch
            {
                0 => function.Call(),
                1 => function.Call(args[0]),
                2 => function.Call(args[0], args[1]),
                _ => function.Call(args)
            };
        }

        private static object InvokeClrDelegate(Delegate del, object[] args)
        {
            var parameters = del.Method.GetParameters();
            if (parameters.Length == 0)
                return del.DynamicInvoke();

            object[] fitted = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i < args.Length)
                    fitted[i] = args[i];
                else if (parameters[i].HasDefaultValue)
                    fitted[i] = parameters[i].DefaultValue;
            }

            return del.DynamicInvoke(fitted);
        }

        private object InvokeHookSafely(Func<object[], object> hook, string hookName, object[] args)
        {
            try
            {
                return hook(args);
            }
            catch (Exception ex)
            {
                Logger.WriteErrorLine($"[LuaDialog:{ScriptChunkName}] Error invoking '{hookName}': {ex}");
                if (_throwOnScriptError)
                    throw;

                return null;
            }
        }

        private object[] BuildInvokeArgs(object[] eventArgs)
        {
            if (eventArgs == null || eventArgs.Length == 0)
                return [Context];

            object[] invokeArgs = new object[eventArgs.Length + 1];
            invokeArgs[0] = Context;
            Array.Copy(eventArgs, 0, invokeArgs, 1, eventArgs.Length);
            return invokeArgs;
        }

        private bool InterpretHookResult(object rawResult, out bool suppressMessage, out string message)
        {
            suppressMessage = false;
            message = null;

            if (rawResult == null)
                return false;

            if (rawResult is bool flag)
            {
                if (!flag)
                {
                    suppressMessage = true;
                    return true;
                }

                return false;
            }

            if (rawResult is string text)
            {
                if (text.Length == 0)
                {
                    suppressMessage = true;
                    return true;
                }

                message = text;
                return true;
            }

            // 支持返回 Lua 字符串数组表，自动随机抽取一条
            List<string> candidates = new List<string>();
            if (_context != null && _context.TryConvertList(rawResult, out IList<string> convertedStrings) && convertedStrings != null)
            {
                foreach (string item in convertedStrings)
                {
                    if (item != null)
                        candidates.Add(item);
                }
            }
            else if (_context != null && _context.TryConvertList(rawResult, out IList<object> convertedObjects) && convertedObjects != null)
            {
                foreach (object item in convertedObjects)
                {
                    if (item != null)
                        candidates.Add(item.ToString());
                }
            }
            else if (rawResult is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item != null)
                        candidates.Add(item.ToString());
                }
            }

            if (candidates.Count > 0)
            {
                string picked = candidates[WindBotRuntime.Random.Next(candidates.Count)];
                if (string.IsNullOrEmpty(picked))
                {
                    suppressMessage = true;
                    return true;
                }

                message = picked;
                return true;
            }

            return false;
        }
    }
}
