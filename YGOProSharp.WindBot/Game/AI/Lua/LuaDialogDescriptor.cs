using System;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 描述一个通过泛用 Lua 运行时加载的 WindBot 对话脚本。
    /// </summary>
    public sealed class LuaDialogDescriptor
    {
        public LuaDialogDescriptor(
            string name,
            string scriptName = null,
            string locale = null,
            string fallbackDialog = null,
            bool allowOverride = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Lua dialog name cannot be null or whitespace.", nameof(name));

            Name = name.Trim();
            ScriptName = string.IsNullOrWhiteSpace(scriptName) ? Name : scriptName.Trim();
            Locale = string.IsNullOrWhiteSpace(locale) ? null : locale.Trim();
            FallbackDialog = string.IsNullOrWhiteSpace(fallbackDialog) ? null : fallbackDialog.Trim();
            AllowOverride = allowOverride;
        }

        /// <summary>
        /// 对话逻辑名称（对应 <see cref="WindBotInfo.Dialog"/>）。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 传递给 <see cref="YGOProSharp.Abstractions.Scripting.ILuaScriptSourceProvider"/> 的脚本名或路径。
        /// </summary>
        public string ScriptName { get; }

        /// <summary>
        /// 可选语言区域标识（如 <c>zh-CN</c>、<c>en-US</c>）。
        /// </summary>
        public string Locale { get; }

        /// <summary>
        /// 可选回退对话名称（如 <c>zh-CN</c> 或 <c>default</c>），未声明的事件台词自动继承自该对话。
        /// </summary>
        public string FallbackDialog { get; }

        /// <summary>
        /// 当存在同名内置 <c>.json</c> 对话文件时，是否优先使用此 Lua 对话。
        /// </summary>
        public bool AllowOverride { get; }
    }
}
