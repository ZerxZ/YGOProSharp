using System.Globalization;
using System.Text;
using NLua;
using YGOProSharp.Abstractions.Scripting;

namespace YGOProSharp.Cli;

/// <summary>
/// 基于 NLua（Lua 5.4）实现的 <see cref="ILuaRuntime"/> 适配器，供 <c>YGOProSharp.Cli</c> 组合根装配给 WindBot。
/// 每个脚本执行上下文持有独立的 <see cref="Lua"/> 实例，生命周期结束时随执行器一起释放。
/// </summary>
public sealed class NLuaRuntime : ILuaRuntime
{
    public static NLuaRuntime Instance { get; } = new();

    private readonly int _maxInstructionBatches;
    private readonly int _maxCallDurationMs;

    public NLuaRuntime(int maxInstructionBatches = 250, int maxCallDurationMs = 2000)
    {
        _maxInstructionBatches = Math.Max(5, maxInstructionBatches);
        _maxCallDurationMs = Math.Max(50, maxCallDurationMs);
    }

    public ILuaScriptContext CreateContext(string? name = null)
    {
        return new NLuaScriptContext(_maxInstructionBatches, _maxCallDurationMs);
    }

    private sealed class NLuaScriptContext : ILuaScriptContext
    {
        private const string SandboxBootstrapLua = """
            if debug and debug.sethook then
                local guard = __ygp_step_guard
                debug.sethook(function() guard() end, "", 2000)
            end
            os = nil
            io = nil
            package = nil
            debug = nil
            dofile = nil
            loadfile = nil
            load = nil
            require = nil
            luanet = nil
            import = nil
            collectgarbage = nil
            __ygp_step_guard = nil
            """;

        private readonly Lua _lua;
        private readonly int _maxInstructionBatches;
        private readonly int _maxCallDurationMs;
        private int _callDepth;
        private int _currentBatches;
        private long _deadlineTickCount;
        private bool _disposed;

        public NLuaScriptContext(int maxInstructionBatches, int maxCallDurationMs)
        {
            _maxInstructionBatches = maxInstructionBatches;
            _maxCallDurationMs = maxCallDurationMs;
            _lua = new Lua();
            _lua.State.Encoding = Encoding.UTF8;
            _lua["__ygp_step_guard"] = (Action)CheckExecutionBudget;
            _lua.DoString(SandboxBootstrapLua, "ygp_sandbox_init");
        }

        internal void EnterExecutionScope()
        {
            ThrowIfDisposed();
            if (_callDepth == 0)
            {
                _currentBatches = 0;
                _deadlineTickCount = Environment.TickCount64 + _maxCallDurationMs;
            }
            _callDepth++;
        }

        internal void ExitExecutionScope()
        {
            if (_callDepth > 0)
                _callDepth--;
        }

        private void CheckExecutionBudget()
        {
            if (_callDepth <= 0)
                return;

            _currentBatches++;
            if (_currentBatches > _maxInstructionBatches || Environment.TickCount64 > _deadlineTickCount)
                throw new TimeoutException("Lua 脚本单次执行指令数或耗时超出安全沙箱上限（疑似死循环）。");
        }

        public void SetGlobal(string name, object? value)
        {
            ThrowIfDisposed();

            // 将只读常量枚举字典映射为原生 Lua 表，使脚本支持零反射的 EnumName.Member 语法与 pairs 遍历。
            if (value is IReadOnlyDictionary<string, int> constants)
            {
                _lua.NewTable(name);
                using LuaTable table = _lua.GetTable(name);
                foreach (KeyValuePair<string, int> entry in constants)
                    table[entry.Key] = entry.Value;
                return;
            }

            _lua[name] = value;
        }

        public bool TryGetGlobal(string name, out object? value)
        {
            ThrowIfDisposed();
            value = _lua[name];
            return value is not null;
        }

        public object? DoString(string code, string? chunkName = null)
        {
            EnterExecutionScope();
            try
            {
                object[]? results = _lua.DoString(code, chunkName ?? "LuaDeck.lua");
                if (results is null || results.Length == 0)
                    return null;

                return results[0] is LuaTable table
                    ? new NLuaTableAdapter(table, this)
                    : results[0];
            }
            finally
            {
                ExitExecutionScope();
            }
        }

        public bool TryGetFunction(string name, out ILuaFunction? function)
        {
            ThrowIfDisposed();
            if (_lua[name] is LuaFunction luaFunction)
            {
                function = new NLuaFunctionAdapter(luaFunction, this);
                return true;
            }

            function = null;
            return false;
        }

        public bool TryWrapFunction(object rawFunction, out ILuaFunction? function)
        {
            if (rawFunction is LuaFunction luaFunction)
            {
                function = new NLuaFunctionAdapter(luaFunction, this);
                return true;
            }

            function = null;
            return false;
        }

        public bool TryConvertList<T>(object? value, out IList<T>? list)
        {
            if (value is not LuaTable table)
            {
                list = null;
                return false;
            }

            try
            {
                List<T> items = new();

                // 优先按 Lua 1-based 顺序数组键读取，保证顺序稳定。
                for (long index = 1; ; index++)
                {
                    object? element = table[index];
                    if (element is null)
                        break;

                    if (TryConvertElement(element, out T? converted))
                        items.Add(converted!);
                }

                // 若非纯顺序数组表，回退遍历 Values。
                if (items.Count == 0)
                {
                    foreach (object? element in table.Values)
                    {
                        if (TryConvertElement(element, out T? converted))
                            items.Add(converted!);
                    }
                }

                list = items;
                return true;
            }
            finally
            {
                table.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lua.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NLuaScriptContext));
        }

        private static bool TryConvertElement<T>(object? element, out T? result)
        {
            if (element is null)
            {
                result = default;
                return false;
            }

            if (element is T direct)
            {
                result = direct;
                return true;
            }

            if (typeof(T) == typeof(int))
            {
                switch (element)
                {
                    case long l when l is >= int.MinValue and <= int.MaxValue:
                        result = (T)(object)(int)l;
                        return true;
                    case double d when !double.IsNaN(d) && !double.IsInfinity(d) && d is >= int.MinValue and <= int.MaxValue:
                        result = (T)(object)(int)d;
                        return true;
                    case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                        result = (T)(object)parsed;
                        return true;
                }
            }

            result = default;
            return false;
        }
    }

    private sealed class NLuaTableAdapter : ILuaTable
    {
        private readonly LuaTable _table;
        private readonly NLuaScriptContext _owner;
        private bool _disposed;

        public NLuaTableAdapter(LuaTable table, NLuaScriptContext owner)
        {
            _table = table;
            _owner = owner;
        }

        public bool TryGet(string key, out object? value)
        {
            if (_disposed)
            {
                value = null;
                return false;
            }

            value = _table[key];
            return value is not null;
        }

        public bool TryGetFunction(string key, out ILuaFunction? function)
        {
            if (!_disposed && _table[key] is LuaFunction luaFunction)
            {
                function = new NLuaFunctionAdapter(luaFunction, _owner);
                return true;
            }

            function = null;
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _table.Dispose();
        }
    }

    private sealed class NLuaFunctionAdapter : ILuaFunction
    {
        private readonly LuaFunction _function;
        private readonly NLuaScriptContext _owner;
        private bool _disposed;

        public NLuaFunctionAdapter(LuaFunction function, NLuaScriptContext owner)
        {
            _function = function;
            _owner = owner;
        }

        public object? Call(params object?[] args)
        {
            if (_disposed)
                return null;

            _owner.EnterExecutionScope();
            try
            {
                object[]? results = _function.Call(args);
                return results is { Length: > 0 } ? results[0] : null;
            }
            finally
            {
                _owner.ExitExecutionScope();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _function.Dispose();
        }
    }
}
