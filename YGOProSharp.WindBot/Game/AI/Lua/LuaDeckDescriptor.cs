using System;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// Lua 卡组执行器描述符，对应 C# 执行器上的 <see cref="DeckAttribute"/>。
    /// </summary>
    public sealed class LuaDeckDescriptor
    {
        /// <summary>
        /// 卡组执行器注册名称（与 <see cref="WindBotInfo.Deck"/> 匹配）。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 默认关联的 .ydk 卡组文件名（不带扩展名时与 WindBot 原有约定一致）。
        /// </summary>
        public string File { get; }

        /// <summary>
        /// 卡组难度等级（默认 "Normal"，与 <see cref="DeckAttribute.Level"/> 一致）。
        /// </summary>
        public string Level { get; }

        /// <summary>
        /// 对应的 Lua 脚本逻辑名或路径，交由 <see cref="YGOProSharp.Abstractions.Scripting.ILuaScriptSourceProvider"/> 解析。
        /// </summary>
        public string ScriptName { get; }

        /// <summary>
        /// 是否允许覆盖同名的内置 C# 卡组执行器。
        /// </summary>
        public bool AllowOverride { get; }

        public LuaDeckDescriptor(
            string name,
            string file = null,
            string scriptName = null,
            string level = "Normal",
            bool allowOverride = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Lua deck name cannot be null or whitespace.", nameof(name));

            Name = name.Trim();
            File = string.IsNullOrWhiteSpace(file) ? Name : file.Trim();
            ScriptName = string.IsNullOrWhiteSpace(scriptName) ? Name : scriptName.Trim();
            Level = string.IsNullOrWhiteSpace(level) ? "Normal" : level.Trim();
            AllowOverride = allowOverride;
        }
    }
}
