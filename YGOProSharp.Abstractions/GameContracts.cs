namespace YGOProSharp.Abstractions;

public interface IGame
{
    event Action<object, EventArgs>? OnNetworkReady;
    event Action<object, EventArgs>? OnNetworkEnd;
    event Action<object, EventArgs>? OnGameStart;
    event Action<object, EventArgs>? OnGameEnd;
    event Action<object, EventArgs>? OnDuelEnd;
    event Action<object, PlayerEventArgs>? OnPlayerJoin;
    event Action<object, PlayerEventArgs>? OnPlayerLeave;
    event Action<object, PlayerMoveEventArgs>? OnPlayerMove;
    event Action<object, PlayerEventArgs>? OnPlayerReady;
    event Action<object, PlayerChatEventArgs>? OnPlayerChat;
}

public class PlayerEventArgs : EventArgs
{
    public object Player { get; }

    public PlayerEventArgs(object player)
    {
        Player = player;
    }
}

public sealed class PlayerMoveEventArgs : PlayerEventArgs
{
    public int FromType { get; }

    public PlayerMoveEventArgs(object player, int fromType)
        : base(player)
    {
        FromType = fromType;
    }
}

public sealed class PlayerChatEventArgs : PlayerEventArgs
{
    public string Message { get; }

    public PlayerChatEventArgs(object player, string message)
        : base(player)
    {
        Message = message;
    }
}
