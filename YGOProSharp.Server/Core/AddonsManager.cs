using System;
using System.Collections.Generic;
using YGOProSharp.Server.Addons;

namespace YGOProSharp.Server
{
    /// <summary>
    /// 创建并管理围绕单个 <see cref="Game"/> 房间的内置与自定义扩展插件（addon）。
    /// </summary>
    public class AddonsManager : IDisposable
    {
        private readonly bool _standardStreamProtocol;
        private readonly bool _enableChatCommands;
        private readonly IReadOnlyList<ChatCommandDefinition> _chatCommands;
        private readonly IReadOnlyList<Func<Game, AddonBase>> _addonFactories;
        private bool _disposed;

        public List<AddonBase> Addons { get; private set; }

        public AddonsManager(bool standardStreamProtocol)
            : this(standardStreamProtocol, enableChatCommands: false, chatCommands: null, addonFactories: null)
        {
        }

        public AddonsManager(
            bool standardStreamProtocol,
            bool enableChatCommands,
            IEnumerable<ChatCommandDefinition>? chatCommands = null,
            IEnumerable<Func<Game, AddonBase>>? addonFactories = null)
        {
            _standardStreamProtocol = standardStreamProtocol;
            _enableChatCommands = enableChatCommands;
            _chatCommands = chatCommands is null ? [] : new List<ChatCommandDefinition>(chatCommands);
            _addonFactories = addonFactories is null ? [] : new List<Func<Game, AddonBase>>(addonFactories);
            Addons = new List<AddonBase>();
        }

        public void Init(Game game)
        {
            ArgumentNullException.ThrowIfNull(game);

            if (_standardStreamProtocol)
                Addons.Add(new StandardStreamProtocol(game));

            if (_enableChatCommands)
                Addons.Add(new ChatCommandAddon(game, _chatCommands));

            for (int i = 0; i < _addonFactories.Count; i++)
            {
                AddonBase? addon = _addonFactories[i](game);
                if (addon is not null)
                    Addons.Add(addon);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            for (int i = Addons.Count - 1; i >= 0; i--)
                Addons[i].Dispose();
            Addons.Clear();
        }
    }
}
