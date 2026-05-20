using System.Collections.Generic;

namespace YGOProSharp
{
    public class AddonsManager
    {
        public List<AddonBase> Addons { get; private set; }

        public AddonsManager()
        {
            Addons = new List<AddonBase>();
        }

        public void Init(Game game)
        {
            // TODO find a way to load specific addons

            Addons.Add(new Addons.StandardStreamProtocol(game));
        }
    }
}
