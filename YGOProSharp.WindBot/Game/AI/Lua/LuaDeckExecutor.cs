using System;
using System.Collections.Generic;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 基于泛用 Lua 抽象接口（<see cref="ILuaRuntime"/> / <see cref="ILuaScriptContext"/>）实现的 WindBot 卡组执行器。
    /// 继承自 <see cref="DefaultExecutor"/>；生命周期/决策覆写方法、内置规则映射与枚举常量表均由 <c>YGOProSharp.Generators</c> 在编译期增量生成。
    /// </summary>
    [GenerateLuaExecutorBindings]
    public partial class LuaDeckExecutor : DefaultExecutor, IDisposable
    {
        private readonly ILuaScriptContext _context;
        private readonly ILuaTable _moduleTable;
        private readonly List<ILuaFunction> _ownedFunctions = new List<ILuaFunction>();
        private readonly List<PendingDslRule> _pendingDslRules = new List<PendingDslRule>();
        private readonly List<LuaThoughtEntry> _thoughts = new List<LuaThoughtEntry>();
        private bool _isLoadingScript;
        private bool _throwOnScriptError;
        private bool _disposed;

        /// <summary>
        /// 当前脚本块名，用于日志与排错定位。
        /// </summary>
        public string ScriptChunkName { get; }

        /// <summary>
        /// xmake 风格声明式 DSL 宿主，包含脚本声明的卡组元数据。
        /// </summary>
        public LuaDeckDsl Dsl { get; }

        /// <summary>
        /// 是否已释放底层 Lua 资源。
        /// </summary>
        public bool IsDisposed => _disposed;

        // 将 Executor 中的 protected 状态以公开属性暴露，便于各类 Lua 引擎直接读取。
        public ExecutorType CurrentType => Type;
        public ClientCard CurrentCard => Card;
        public int CurrentActivateDescription => ActivateDescription;
        public int CurrentEffectTiming => CurrentTiming;
        public ClientField BotField => Bot;
        public ClientField EnemyField => Enemy;

        /// <summary>
        /// 全局 Lua 思考事件（用于自博弈复盘与围观气泡订阅）。
        /// </summary>
        public static event Action<LuaThoughtEntry> GlobalThoughtEmitted;

        /// <summary>
        /// 当前执行器实例的思考事件。
        /// </summary>
        public event Action<LuaThoughtEntry> ThoughtEmitted;

        /// <summary>
        /// 可选全局双脑慢思考决策提供器（仅在脚本主动调用 <see cref="AskExternalAgent"/> 时触发）。
        /// </summary>
        public static Func<ExternalAgentDecisionRequest, int?> GlobalExternalAgentHandler { get; set; }

        /// <summary>
        /// 可选单实例双脑慢思考决策提供器。
        /// </summary>
        public Func<ExternalAgentDecisionRequest, int?> ExternalAgentHandler { get; set; }

        /// <summary>
        /// 最近输出的思考记录（最多保留 64 条）。
        /// </summary>
        public IReadOnlyList<LuaThoughtEntry> ThoughtHistory => _thoughts;

        public LuaDeckExecutor(
            GameAI ai,
            Duel duel,
            ILuaRuntime runtime,
            string scriptSource,
            string chunkName = null,
            bool throwOnScriptError = false,
            string targetDeckName = null)
            : base(ai, duel)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (string.IsNullOrWhiteSpace(scriptSource))
                throw new ArgumentException("Lua deck script source cannot be null or whitespace.", nameof(scriptSource));

            ScriptChunkName = string.IsNullOrWhiteSpace(chunkName) ? "LuaDeck.lua" : chunkName;
            _throwOnScriptError = throwOnScriptError;

            _context = runtime.CreateContext(ScriptChunkName)
                ?? throw new InvalidOperationException($"Lua runtime returned a null context for '{ScriptChunkName}'.");

            try
            {
                Dsl = new LuaDeckDsl(this, _context, targetDeckName);
                BindHostGlobals();
                Dsl.BindGlobals();

                _isLoadingScript = true;
                object scriptResult;
                try
                {
                    scriptResult = _context.DoString(scriptSource, ScriptChunkName);
                }
                finally
                {
                    _isLoadingScript = false;
                }

                _moduleTable = scriptResult as ILuaTable;

                if (Dsl.DeclaredThrowOnError.HasValue)
                    _throwOnScriptError = Dsl.DeclaredThrowOnError.Value;

                FlushPendingDslRules();
                ResolveGeneratedHooks();

                if (_onInit != null)
                    InvokeVoid(_onInit, LuaDeckHookNames.OnInit, this);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void AddLuaExecutor(ExecutorType type, int cardId, object callback = null)
        {
            ThrowIfDisposed();
            Func<bool> resolved = ResolveExecutorCallback(callback);
            AddExecutor(type, cardId, resolved);
        }

        public void AddLuaExecutor(int type, int cardId, object callback = null) =>
            AddLuaExecutor((ExecutorType)type, cardId, callback);

        public void AddLuaExecutor(string typeName, int cardId, object callback = null)
        {
            if (!Enum.TryParse(typeName, ignoreCase: true, out ExecutorType parsed))
                throw new ArgumentException($"Unknown ExecutorType '{typeName}'.", nameof(typeName));

            AddLuaExecutor(parsed, cardId, callback);
        }

        public void AddLuaExecutor(ExecutorType type, object callback) =>
            AddLuaExecutor(type, -1, callback);

        public void AddLuaExecutor(string typeName, object callback) =>
            AddLuaExecutor(typeName, -1, callback);

        public void AddDefaultExecutor(ExecutorType type, int cardId, string defaultRuleName)
        {
            ThrowIfDisposed();
            if (!TryGetDefaultRule(defaultRuleName, out Func<bool> rule))
                throw new ArgumentException($"Unknown DefaultExecutor rule '{defaultRuleName}'.", nameof(defaultRuleName));

            AddExecutor(type, cardId, rule);
        }

        public void AddDefaultExecutor(string typeName, int cardId, string defaultRuleName)
        {
            if (!Enum.TryParse(typeName, ignoreCase: true, out ExecutorType parsed))
                throw new ArgumentException($"Unknown ExecutorType '{typeName}'.", nameof(typeName));

            AddDefaultExecutor(parsed, cardId, defaultRuleName);
        }

        public void AddDefaultExecutor(ExecutorType type, string defaultRuleName) =>
            AddDefaultExecutor(type, -1, defaultRuleName);

        public void AddDefaultExecutor(string typeName, string defaultRuleName) =>
            AddDefaultExecutor(typeName, -1, defaultRuleName);

        public bool CallDefaultRule(string defaultRuleName)
        {
            if (string.IsNullOrWhiteSpace(defaultRuleName))
                return false;

            if (DefaultRuleMap.TryGetValue(defaultRuleName, out Func<LuaDeckExecutor, bool> rule))
                return rule(this);

            Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Unknown DefaultExecutor rule '{defaultRuleName}'.");
            return false;
        }

        public bool TryGetDefaultRule(string defaultRuleName, out Func<bool> rule)
        {
            if (!string.IsNullOrWhiteSpace(defaultRuleName) &&
                DefaultRuleMap.TryGetValue(defaultRuleName, out Func<LuaDeckExecutor, bool> entry))
            {
                rule = () => entry(this);
                return true;
            }

            rule = null;
            return false;
        }

        public bool CheckCardIsNegated(ClientCard card) => DefaultCheckWhetherCardIsNegated(card);
        public bool CheckCardIdIsNegated(int cardId) => DefaultCheckWhetherCardIdIsNegated(cardId);
        public int GetCalledByTheGraveCount(int cardId) => GetCalledbytheGraveIdCount(cardId);
        public bool CheckSpellActivateWillBeNegated(ClientCard card) => DefaultCheckWhetherSpellActivateWillBeNegated(card);

        public void LogInfo(string message) => Logger.WriteLine($"[LuaDeck:{ScriptChunkName}] {message}");
        public void LogDebug(string message) => Logger.DebugWriteLine($"[LuaDeck:{ScriptChunkName}] {message}");
        public void LogError(string message) => Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] {message}");

        public void EmitThought(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return;

            string clean = summary.Trim();
            if (clean.Length > 240)
                clean = clean.Substring(0, 240);

            LuaThoughtEntry entry = new LuaThoughtEntry(
                ScriptChunkName,
                Duel?.Turn ?? 0,
                Duel?.Phase.ToString() ?? "Lobby",
                clean,
                DateTime.UtcNow);

            if (_thoughts.Count >= 64)
                _thoughts.RemoveAt(0);
            _thoughts.Add(entry);

            Logger.DebugWriteLine($"[Thought:{ScriptChunkName} T{entry.Turn} {entry.Phase}] {clean}");
            ThoughtEmitted?.Invoke(entry);
            GlobalThoughtEmitted?.Invoke(entry);
        }

        public object AskExternalAgent(string reason, object candidates = null)
        {
            Func<ExternalAgentDecisionRequest, int?> handler = ExternalAgentHandler ?? GlobalExternalAgentHandler;
            if (handler == null)
                return null;

            List<string> candidateList = new List<string>();
            if (candidates is IEnumerable<string> strSeq)
            {
                candidateList.AddRange(strSeq);
            }
            else if (_context != null && _context.TryConvertList(candidates, out IList<string> luaStrings) && luaStrings != null)
            {
                candidateList.AddRange(luaStrings);
            }
            else if (candidates != null)
            {
                candidateList.Add(candidates.ToString());
            }

            ExternalAgentDecisionRequest req = new ExternalAgentDecisionRequest(
                ScriptChunkName,
                Duel?.Turn ?? 0,
                Duel?.Phase.ToString() ?? "Unknown",
                Bot?.LifePoints ?? 0,
                Enemy?.LifePoints ?? 0,
                reason ?? string.Empty,
                candidateList);

            try
            {
                int? chosen = handler(req);
                return chosen.HasValue ? (object)chosen.Value : null;
            }
            catch (Exception ex)
            {
                Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] AskExternalAgent failed: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                if (_onDispose != null)
                    InvokeVoid(_onDispose, LuaDeckHookNames.OnDispose, this);
            }
            finally
            {
                _disposed = true;
                Executors.Clear();

                for (int i = _ownedFunctions.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _ownedFunctions[i]?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Dispose function error: {ex}");
                    }
                }
                _ownedFunctions.Clear();

                try
                {
                    _moduleTable?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Dispose module table error: {ex}");
                }

                try
                {
                    _context?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Dispose context error: {ex}");
                }
            }
        }

        private void BindHostGlobals()
        {
            _context.SetGlobal("Self", this);
            _context.SetGlobal("Executor", this);
            _context.SetGlobal("Duel", Duel);
            _context.SetGlobal("AI", AI);
            _context.SetGlobal("Util", Util);
            _context.SetGlobal("Bot", Bot);
            _context.SetGlobal("Enemy", Enemy);

            BindGeneratedEnumGlobals();
        }

        internal void QueueDslExecutorRule(ExecutorType type, int cardId, object callback)
        {
            ThrowIfDisposed();
            if (_isLoadingScript)
            {
                _pendingDslRules.Add(new PendingDslRule(PendingDslRuleKind.LuaExecutor, type, cardId, callback, null));
                return;
            }

            AddLuaExecutor(type, cardId, callback);
        }

        internal void QueueDslDefaultRule(ExecutorType type, int cardId, string defaultRuleName)
        {
            ThrowIfDisposed();
            if (_isLoadingScript)
            {
                _pendingDslRules.Add(new PendingDslRule(PendingDslRuleKind.DefaultRule, type, cardId, null, defaultRuleName));
                return;
            }

            AddDefaultExecutor(type, cardId, defaultRuleName);
        }

        internal void QueueDslNoExecutorRule(ExecutorType type)
        {
            ThrowIfDisposed();
            if (_isLoadingScript)
            {
                _pendingDslRules.Add(new PendingDslRule(PendingDslRuleKind.DefaultNoExecutor, type, -1, null, null));
                return;
            }

            AddExecutor(type);
        }

        private void FlushPendingDslRules()
        {
            for (int i = 0; i < _pendingDslRules.Count; i++)
            {
                PendingDslRule rule = _pendingDslRules[i];
                switch (rule.Kind)
                {
                    case PendingDslRuleKind.LuaExecutor:
                        AddLuaExecutor(rule.Type, rule.CardId, rule.Callback);
                        break;
                    case PendingDslRuleKind.DefaultRule:
                        AddDefaultExecutor(rule.Type, rule.CardId, rule.DefaultRuleName);
                        break;
                    case PendingDslRuleKind.DefaultNoExecutor:
                        AddExecutor(rule.Type);
                        break;
                }
            }

            _pendingDslRules.Clear();
        }

        private ILuaFunction ResolveHook(string name)
        {
            if (Dsl != null && Dsl.RegisteredHooks.TryGetValue(name, out object dslCallback) && dslCallback != null)
            {
                ILuaFunction dslFunction = ResolveHookObject(dslCallback, name);
                if (dslFunction != null)
                    return dslFunction;
            }

            return ResolveNamedFunction(name);
        }

        private ILuaFunction ResolveNamedFunction(string name)
        {
            ILuaFunction function = null;

            if (_moduleTable != null && _moduleTable.TryGetFunction(name, out ILuaFunction moduleFunction) && moduleFunction != null)
                function = moduleFunction;
            else if (_context.TryGetFunction(name, out ILuaFunction globalFunction) && globalFunction != null)
                function = globalFunction;

            if (function != null)
                TrackOwnedFunction(function);

            return function;
        }

        private ILuaFunction ResolveHookObject(object callback, string hookName)
        {
            if (callback is ILuaFunction luaFunction)
            {
                TrackOwnedFunction(luaFunction);
                return luaFunction;
            }

            if (callback is string functionName)
            {
                ILuaFunction resolved = ResolveNamedFunction(functionName);
                if (resolved == null)
                {
                    throw new InvalidOperationException(
                        $"Lua hook callback function '{functionName}' registered for '{hookName}' was not found in '{ScriptChunkName}'.");
                }

                return resolved;
            }

            if (callback is Delegate managedDelegate)
            {
                ILuaFunction adapted = new DelegateLuaFunction(managedDelegate);
                TrackOwnedFunction(adapted);
                return adapted;
            }

            if (_context.TryWrapFunction(callback, out ILuaFunction wrapped) && wrapped != null)
            {
                TrackOwnedFunction(wrapped);
                return wrapped;
            }

            throw new ArgumentException(
                $"Unsupported Lua hook callback of type '{callback.GetType().FullName}' for '{hookName}'. " +
                "Pass an ILuaFunction, Delegate, function name string, or implement ILuaScriptContext.TryWrapFunction.",
                nameof(callback));
        }

        private Func<bool> ResolveExecutorCallback(object callback)
        {
            if (callback == null)
                return null;

            if (callback is Func<bool> directDelegate)
                return directDelegate;

            if (callback is Func<LuaDeckExecutor, bool> selfDelegate)
                return () => selfDelegate(this);

            if (callback is ILuaFunction luaFunction)
            {
                TrackOwnedFunction(luaFunction);
                return CreateLuaRuleDelegate(luaFunction, "<inline>");
            }

            if (callback is string functionOrRuleName)
            {
                if (string.IsNullOrWhiteSpace(functionOrRuleName))
                    return null;

                ILuaFunction namedFunction = ResolveNamedFunction(functionOrRuleName);
                if (namedFunction != null)
                    return CreateLuaRuleDelegate(namedFunction, functionOrRuleName);

                if (TryGetDefaultRule(functionOrRuleName, out Func<bool> defaultRule))
                    return defaultRule;

                throw new InvalidOperationException(
                    $"Lua callback or DefaultExecutor rule '{functionOrRuleName}' was not found in '{ScriptChunkName}'.");
            }

            if (_context.TryWrapFunction(callback, out ILuaFunction wrapped) && wrapped != null)
            {
                TrackOwnedFunction(wrapped);
                return CreateLuaRuleDelegate(wrapped, "<wrapped>");
            }

            throw new ArgumentException(
                $"Unsupported Lua executor callback of type '{callback.GetType().FullName}'. " +
                "Pass a Func<bool>, ILuaFunction, function/default-rule name string, or implement ILuaScriptContext.TryWrapFunction.",
                nameof(callback));
        }

        private Func<bool> CreateLuaRuleDelegate(ILuaFunction function, string label)
        {
            return () => !_disposed && TryInvokeBool(function, label, out bool result, this) && result;
        }

        private void TrackOwnedFunction(ILuaFunction function)
        {
            if (!_ownedFunctions.Contains(function))
                _ownedFunctions.Add(function);
        }

        private void InvokeVoid(ILuaFunction function, string hookName, params object[] args)
        {
            if (_disposed)
                return;

            try
            {
                function.Call(args);
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
            }
        }

        private bool TryInvokeObject(ILuaFunction function, string hookName, out object result, params object[] args)
        {
            result = null;
            if (_disposed)
                return false;

            try
            {
                result = function.Call(args);
                return result != null;
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
                return false;
            }
        }

        private bool TryInvokeBool(ILuaFunction function, string hookName, out bool result, params object[] args)
        {
            if (TryInvokeObject(function, hookName, out object raw, args) &&
                LuaValueConverter.TryToBoolean(raw, out result))
            {
                return true;
            }

            result = false;
            return false;
        }

        private bool TryInvokeInt(ILuaFunction function, string hookName, out int result, params object[] args)
        {
            if (TryInvokeObject(function, hookName, out object raw, args) &&
                LuaValueConverter.TryToInt32(raw, out result))
            {
                return true;
            }

            result = 0;
            return false;
        }

        private bool TryInvokeCardList(ILuaFunction function, string hookName, out IList<ClientCard> result, params object[] args)
        {
            if (TryInvokeObject(function, hookName, out object raw, args))
            {
                result = LuaValueConverter.TryToCardList(_context, raw);
                return result != null;
            }

            result = null;
            return false;
        }

        private void HandleScriptError(string hookName, Exception ex)
        {
            Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Error in '{hookName}': {ex}");
            if (_throwOnScriptError)
                throw ex;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LuaDeckExecutor));
        }

        private enum PendingDslRuleKind
        {
            LuaExecutor,
            DefaultRule,
            DefaultNoExecutor
        }

        private readonly record struct PendingDslRule(
            PendingDslRuleKind Kind,
            ExecutorType Type,
            int CardId,
            object Callback,
            string DefaultRuleName);

        private sealed class DelegateLuaFunction : ILuaFunction
        {
            private readonly Delegate _delegate;
            private readonly int _paramCount;

            public DelegateLuaFunction(Delegate managedDelegate)
            {
                _delegate = managedDelegate;
                _paramCount = managedDelegate.Method.GetParameters().Length;
            }

            public object Call(params object[] args)
            {
                if (_paramCount == 0)
                    return _delegate.DynamicInvoke(Array.Empty<object>());

                if (args != null && args.Length == _paramCount)
                    return _delegate.DynamicInvoke(args);

                object[] invokeArgs = new object[_paramCount];
                if (args != null)
                {
                    int copyCount = Math.Min(args.Length, _paramCount);
                    Array.Copy(args, invokeArgs, copyCount);
                }

                return _delegate.DynamicInvoke(invokeArgs);
            }

            public void Dispose()
            {
            }
        }
    }

    public sealed record LuaThoughtEntry(
        string ScriptChunkName,
        int Turn,
        string Phase,
        string Summary,
        DateTime TimestampUtc);

    public sealed record ExternalAgentDecisionRequest(
        string ScriptChunkName,
        int Turn,
        string Phase,
        int BotLp,
        int EnemyLp,
        string Reason,
        IReadOnlyList<string> Candidates);
}
