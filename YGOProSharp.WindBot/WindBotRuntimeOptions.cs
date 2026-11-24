using System.Collections.Generic;
using WindBot.Game.AI.Lua;
using YGOProSharp.Abstractions.Scripting;
using YGOProSharp.Core.Cards;

namespace WindBot;

public sealed class WindBotRuntimeOptions
{
    public INamedCardRepository CardRepository { get; init; } = EmptyNamedCardRepository.Instance;
    public int TickDelayMilliseconds { get; init; } = 30;
    public WindBotInfo DefaultBot { get; init; } = new WindBotInfo();
    public Random Random { get; init; }

    /// <summary>
    /// 可选的泛用 Lua 运行时实现（如外部适配的 XLua、MoonSharp、NLua、Lua-CSharp）。
    /// </summary>
    public ILuaRuntime LuaRuntime { get; init; }

    /// <summary>
    /// 可选的 Lua 卡组脚本源码提供者。未指定时默认使用 <see cref="FileSystemLuaDeckScriptProvider"/>。
    /// </summary>
    public ILuaScriptSourceProvider LuaScriptProvider { get; init; }

    /// <summary>
    /// 可选的静态注册 Lua 卡组描述符列表。
    /// </summary>
    public IReadOnlyList<LuaDeckDescriptor> LuaDecks { get; init; }

    /// <summary>
    /// 当 Lua 卡组与内置 C# 卡组同名时，是否默认优先使用 Lua 卡组执行器。
    /// </summary>
    public bool PreferLuaDecks { get; init; }

    /// <summary>
    /// Lua 钩子发生异常时是否向上抛出（默认 false：记录错误日志并回退默认行为）。
    /// </summary>
    public bool ThrowOnLuaScriptError { get; init; }

    /// <summary>
    /// 是否启用旧版（未削弱/勘误前）效果策略（供部分怀旧卡组如 Monarch506 使用）。
    /// </summary>
    public bool UsePreErrataEffects { get; init; }

    /// <summary>
    /// 是否运行在 ServerMode（部分日志行为如聊天回显会据此静默）。
    /// </summary>
    public bool ServerMode { get; init; }
}

public sealed class WindBotServerModeOptions
{
    public int ServerPort { get; init; } = 2399;
    public string BotListPath { get; init; }
    public WindBotRuntimeOptions RuntimeOptions { get; init; } = new WindBotRuntimeOptions();
}
