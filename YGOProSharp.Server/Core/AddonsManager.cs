using System.Collections.Generic;

namespace YGOProSharp.Server
{
    /// <summary>
    /// 创建并初始化围绕 game 的可选集成（addon）。
    /// </summary>
    public class AddonsManager
    {
        private readonly bool _standardStreamProtocol;

        public List<AddonBase> Addons { get; private set; }

        public AddonsManager(bool standardStreamProtocol)
        {
            _standardStreamProtocol = standardStreamProtocol;
            Addons = new List<AddonBase>();
        }

        public void Init(Game game)
        {
            if (_standardStreamProtocol)
                Addons.Add(new Addons.StandardStreamProtocol(game));
        }
    }
}
