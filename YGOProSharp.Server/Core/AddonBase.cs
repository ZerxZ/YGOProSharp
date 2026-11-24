using System;

namespace YGOProSharp.Server
{
    public abstract class AddonBase : IDisposable
    {
        public Game Game { get; private set; }

        protected AddonBase(Game game)
        {
            Game = game;
        }

        public virtual void Dispose()
        {
        }
    }
}
