using System;

namespace YGOProSharp
{
    public class PlayerEventArgs : EventArgs
    {
        public Player Player { get; private set; }

        public PlayerEventArgs(Player player)
        {
            Player = player;
        }
    }
}
