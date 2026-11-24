namespace YGOProSharp.Abstractions.Scripting;

/// <summary>
/// 泛用 Lua 运行时抽象，用于为每个脚本或对局实例创建隔离的执行上下文。
/// 外部可对接 XLua（共享 LuaEnv + 独立 LuaTable 沙箱）、MoonSharp、NLua、Lua-CSharp 等任意 Lua 引擎，
/// 核心库本身不依赖任何具体 Lua 实现。
/// </summary>
public interface ILuaRuntime
{
    /// <summary>
    /// 创建一个隔离的 Lua 脚本执行上下文。
    /// 调用方负责在生命周期结束时释放返回的上下文。
    /// </summary>
    /// <param name="name">上下文名称（可选，便于日志与调试定位）。</param>
    ILuaScriptContext CreateContext(string? name = null);
}

/// <summary>
/// 隔离的 Lua 脚本执行上下文，负责全局变量注入、代码执行、函数解析与类型适配。
/// </summary>
public interface ILuaScriptContext : IDisposable
{
    /// <summary>
    /// 向当前脚本作用域注入宿主对象、常量或委托。
    /// </summary>
    void SetGlobal(string name, object? value);

    /// <summary>
    /// 尝试从当前脚本作用域读取指定名称的全局变量。
    /// </summary>
    bool TryGetGlobal(string name, out object? value);

    /// <summary>
    /// 执行一段 Lua 源码。
    /// 若脚本通过 <c>return M</c> 返回模块表，适配器可返回 <see cref="ILuaTable"/> 或原始对象；无返回值时返回 <c>null</c>。
    /// </summary>
    object? DoString(string code, string? chunkName = null);

    /// <summary>
    /// 尝试从当前脚本作用域解析指定名称的可调用 Lua 函数。
    /// 返回的 <see cref="ILuaFunction"/> 归调用方管理并在结束时释放。
    /// </summary>
    bool TryGetFunction(string name, out ILuaFunction? function);

    /// <summary>
    /// 尝试将 Lua 侧直接传入的原始函数对象（如 XLua.LuaFunction、MoonSharp Closure/DynValue、NLua.LuaFunction）
    /// 包装为统一的 <see cref="ILuaFunction"/>。
    /// 默认仅识别已实现 <see cref="ILuaFunction"/> 的对象；具体适配器可按需覆写。
    /// </summary>
    bool TryWrapFunction(object rawFunction, out ILuaFunction? function)
    {
        if (rawFunction is ILuaFunction luaFunction)
        {
            function = luaFunction;
            return true;
        }

        function = null;
        return false;
    }

    /// <summary>
    /// 尝试将 Lua 侧返回的数组/表对象（如 XLua.LuaTable、MoonSharp Table、NLua.LuaTable）转换为强类型列表。
    /// 具体适配器可覆写此方法以适配各自引擎的 Table 映射。
    /// </summary>
    bool TryConvertList<T>(object? value, out IList<T>? list)
    {
        list = null;
        return false;
    }
}

/// <summary>
/// 泛用 Lua 模块表抽象。当脚本通过 <c>return M</c> 返回模块表时，适配器可返回此接口以支持直接查表与释放。
/// </summary>
public interface ILuaTable : IDisposable
{
    /// <summary>
    /// 尝试按字符串键读取表字段。
    /// </summary>
    bool TryGet(string key, out object? value);

    /// <summary>
    /// 尝试按字符串键解析表中的 Lua 函数。
    /// 返回的 <see cref="ILuaFunction"/> 归调用方管理并在结束时释放。
    /// </summary>
    bool TryGetFunction(string key, out ILuaFunction? function);
}

/// <summary>
/// 泛用 Lua 函数句柄抽象。
/// 实现类应支持幂等 <see cref="IDisposable.Dispose"/>，避免 XLua 等引擎出现 C# 回调引用泄漏。
/// </summary>
public interface ILuaFunction : IDisposable
{
    /// <summary>
    /// 调用 Lua 函数并返回首个结果（无返回值或返回 nil 时返回 <c>null</c>）。
    /// </summary>
    object? Call(params object?[] args);

    /// <summary>
    /// 无参调用重载。高性能适配器可覆写以避免空数组分配。
    /// </summary>
    object? Call() => Call(Array.Empty<object?>());

    /// <summary>
    /// 单参调用重载。高性能适配器可覆写以避免 params 数组分配。
    /// </summary>
    object? Call(object? arg1) => Call(new[] { arg1 });

    /// <summary>
    /// 双参调用重载。高性能适配器可覆写以避免 params 数组分配。
    /// </summary>
    object? Call(object? arg1, object? arg2) => Call(new[] { arg1, arg2 });
}

/// <summary>
/// 泛用 Lua 脚本源码提供者抽象。
/// 可对接本地文件系统、嵌入式资源、内存字典、热更包或 Unity TextAsset/Addressables。
/// </summary>
public interface ILuaScriptSourceProvider
{
    /// <summary>
    /// 尝试按逻辑脚本名或路径读取 Lua 脚本源码。
    /// </summary>
    /// <param name="scriptName">脚本逻辑名或路径。</param>
    /// <param name="source">读取到的 Lua 源码文本。</param>
    /// <param name="chunkName">传递给 Lua 引擎的 chunk 名称（用于堆栈和报错定位）。</param>
    bool TryGetScriptSource(string scriptName, out string? source, out string? chunkName);
}
