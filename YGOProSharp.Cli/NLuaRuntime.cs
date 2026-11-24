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

    public ILuaScriptContext CreateContext(string? name = null)
    {
        return new NLuaScriptContext();
    }

    private sealed class NLuaScriptContext : ILuaScriptContext
    {
        private readonly Lua _lua;
        private bool _disposed;

        public NLuaScriptContext()
        {
            _lua = new Lua();
            _lua.State.Encoding = Encoding.UTF8;
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
            ThrowIfDisposed();
            object[]? results = _lua.DoString(code, chunkName ?? "LuaDeck.lua");
            if (results is null || results.Length == 0)
                return null;

            return results[0] is LuaTable table
                ? new NLuaTableAdapter(table)
                : results[0];
        }

        public bool TryGetFunction(string name, out ILuaFunction? function)
        {
            ThrowIfDisposed();
            if (_lua[name] is LuaFunction luaFunction)
            {
                function = new NLuaFunctionAdapter(luaFunction);
                return true;
            }

            function = null;
            return false;
        }

        public bool TryWrapFunction(object rawFunction, out ILuaFunction? function)
        {
            if (rawFunction is LuaFunction luaFunction)
            {
                function = new NLuaFunctionAdapter(luaFunction);
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
        private bool _disposed;

        public NLuaTableAdapter(LuaTable table)
        {
            _table = table;
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
                function = new NLuaFunctionAdapter(luaFunction);
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
        private bool _disposed;

        public NLuaFunctionAdapter(LuaFunction function)
        {
            _function = function;
        }

        public object? Call(params object?[] args)
        {
            if (_disposed)
                return null;

            object[]? results = _function.Call(args);
            return results is { Length: > 0 } ? results[0] : null;
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
