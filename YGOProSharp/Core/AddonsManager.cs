using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace YGOProSharp
{
    public class AddonsManager
    {
        public List<AddonBase> Addons { get; private set; }
        private readonly ILoggerFactory _loggerFactory;

        public AddonsManager(ILoggerFactory? loggerFactory = null)
        {
            Addons = new List<AddonBase>();
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        }

        public void Init(Game game)
        {
            // TODO find a way to load specific addons

            Addons.Add(new Addons.StandardStreamProtocol(game, _loggerFactory.CreateLogger<Addons.StandardStreamProtocol>()));
        }
    }
}
