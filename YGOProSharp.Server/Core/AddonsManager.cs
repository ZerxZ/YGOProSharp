using System.Collections.Generic;

namespace YGOProSharp.Server
{
    /// <summary>
    /// 创建并初始化围绕 game 的可选集成（addon），避免把 addon 关注点放进游戏状态机。
    /// </summary>
    public class AddonsManager
    {
        public List<AddonBase> Addons { get; private set; }

        public AddonsManager()
        {
            Addons = new List<AddonBase>();
        }

        public void Init(Game game)
        {
            // TODO: 后续支持按配置加载指定 addon。

            Addons.Add(new Addons.StandardStreamProtocol(game));
        }
    }
}
