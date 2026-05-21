using System;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Protocol.Enums;

namespace YGOProSharp.Server.Addons
{
    /// <summary>
    /// 可选 addon：把选定 game event 输出为稳定的 "::::" protocol log line，供外部进程集成。
    /// </summary>
    public class StandardStreamProtocol : AddonBase
    {
        private readonly ILogger<StandardStreamProtocol> _logger;

        public StandardStreamProtocol(Game game)
            : base(game)
        {
            _logger = AppLog.CreateLogger<StandardStreamProtocol>();

            if (!Config.GetBool("StandardStreamProtocol"))
                return;

            Game.OnNetworkReady += Game_OnNetworkReady;
            Game.OnNetworkEnd += Game_OnNetworkEnd;
            Game.OnPlayerChat += Game_OnPlayerChat;
            Game.OnPlayerJoin += Game_OnPlayerJoin;
            Game.OnPlayerLeave += Game_OnPlayerLeave;
            Game.OnPlayerMove += Game_OnPlayerMove;
            Game.OnPlayerReady += Game_OnPlayerReady;
            Game.OnGameStart += Game_OnGameStart;
            Game.OnGameEnd += Game_OnGameEnd;
            Game.OnDuelEnd += Game_OnDuelEnd;
        }

        private void Game_OnNetworkReady(object sender, EventArgs e)
        {
            WriteProtocolMessage("::::network-ready");
        }

        private void Game_OnNetworkEnd(object sender, EventArgs e)
        {
            WriteProtocolMessage("::::network-end");
        }

        private void Game_OnPlayerChat(object sender, PlayerChatEventArgs e)
        {
            Player player = (Player)e.Player;
            WriteProtocolMessage("::::chat|" + player.Name + "|" + e.Message);
        }

        private void Game_OnPlayerJoin(object sender, PlayerEventArgs e)
        {
            if (Game.State != GameState.Lobby)
                return;

            Player player = (Player)e.Player;
            if (player.Type != (int)PlayerType.Observer)
            {
                WriteProtocolMessage("::::join-slot|" + player.Type + "|" + player.Name);
            }
            else
            {
                WriteProtocolMessage("::::spectator|" + Game.Observers.Count);
            }
        }

        private void Game_OnPlayerLeave(object sender, PlayerEventArgs e)
        {
            if (Game.State != GameState.Lobby)
                return;

            Player player = (Player)e.Player;
            if (player.Type != (int)PlayerType.Observer)
            {
                WriteProtocolMessage("::::left-slot|" + player.Type + "|" + player.Name);
            }
            else
            {
                WriteProtocolMessage("::::spectator|" + Game.Observers.Count);
            }
        }

        private void Game_OnPlayerMove(object sender, PlayerMoveEventArgs e)
        {
            if (Game.State != GameState.Lobby)
                return;

            Player player = (Player)e.Player;
            if (e.FromType != (int)PlayerType.Observer)
            {
                WriteProtocolMessage("::::left-slot|" + e.FromType + "|" + player.Name);
            }
            if (player.Type != (int)PlayerType.Observer)
            {
                WriteProtocolMessage("::::join-slot|" + player.Type + "|" + player.Name);
            }
            if (e.FromType == (int)PlayerType.Observer || player.Type == (int)PlayerType.Observer)
            {
                WriteProtocolMessage("::::spectator|" + Game.Observers.Count);
            }
        }

        private void Game_OnPlayerReady(object sender, PlayerEventArgs e)
        {
            Player player = (Player)e.Player;
            WriteProtocolMessage("::::lock-slot|" + player.Type + "|" + Game.IsReady[player.Type]);
        }

        private void Game_OnGameStart(object sender, EventArgs e)
        {
            WriteProtocolMessage("::::start-game");
        }

        private void Game_OnGameEnd(object sender, EventArgs e)
        {
            WriteProtocolMessage("::::end-game|" + Game.Winner);
        }

        private void Game_OnDuelEnd(object sender, EventArgs e)
        {
            WriteProtocolMessage("::::end-duel|" + Game.MatchResults[Game.DuelCount - 1] + "|" + Game.MatchReasons[Game.DuelCount - 1]);
        }

        private void WriteProtocolMessage(string message)
        {
            _logger.LogInformation("{ProtocolMessage}", message);
        }
    }
}
