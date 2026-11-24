using System;
using System.Collections.Generic;
using System.Linq;
using YGOProSharp.Protocol.Enums;

namespace YGOProSharp.Server.Addons;

/// <summary>
/// 房间内聊天命令执行上下文。
/// </summary>
public sealed class ChatCommandContext
{
    public Game Game { get; }
    public Player Player { get; }
    public string CommandName { get; }
    public string RawArguments { get; }
    public IReadOnlyList<string> Arguments { get; }

    public ChatCommandContext(Game game, Player player, string commandName, string rawArguments, IReadOnlyList<string> arguments)
    {
        Game = game;
        Player = player;
        CommandName = commandName;
        RawArguments = rawArguments;
        Arguments = arguments;
    }

    public void Reply(string message) => Game.SendSystemMessage(Player, message);

    public void Broadcast(string message) => Game.BroadcastSystemMessage(message);
}

/// <summary>
/// 一条可注册的聊天命令定义（如 <c>/help</c>、<c>/rules</c> 或自定义 DIY 命令）。
/// </summary>
public sealed record ChatCommandDefinition(
    string Name,
    string Description,
    Action<ChatCommandContext> Handler,
    IReadOnlyList<string>? Aliases = null);

/// <summary>
/// 内置房间聊天命令插件。拦截以 <see cref="CommandPrefix"/> 开头的消息，
/// 提供 <c>/help</c>、<c>/rules</c>、<c>/room</c>、<c>/rooms</c>、<c>/surrender</c> 以及可扩展 DIY 命令。
/// </summary>
public sealed class ChatCommandAddon : AddonBase
{
    private readonly Dictionary<string, ChatCommandDefinition> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChatCommandDefinition> _registered = new();
    private bool _disposed;

    public char CommandPrefix { get; }

    public ChatCommandAddon(
        Game game,
        IEnumerable<ChatCommandDefinition>? customCommands = null,
        char commandPrefix = '/')
        : base(game)
    {
        CommandPrefix = commandPrefix;
        RegisterBuiltInCommands();

        if (customCommands is not null)
        {
            foreach (ChatCommandDefinition command in customCommands)
                RegisterCommand(command);
        }

        Game.OnBeforePlayerChat += HandleBeforePlayerChat;
    }

    public void RegisterCommand(ChatCommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string key = NormalizeName(command.Name);
        if (key.Length == 0)
            throw new ArgumentException("Command name cannot be empty.", nameof(command));

        _registered.RemoveAll(c => string.Equals(NormalizeName(c.Name), key, StringComparison.OrdinalIgnoreCase));
        _registered.Add(command);
        _commands[key] = command;

        if (command.Aliases is not null)
        {
            foreach (string alias in command.Aliases)
            {
                string aliasKey = NormalizeName(alias);
                if (aliasKey.Length > 0)
                    _commands[aliasKey] = command;
            }
        }
    }

    public void RegisterCommand(string name, string description, Action<ChatCommandContext> handler, params string[] aliases)
    {
        RegisterCommand(new ChatCommandDefinition(name, description, handler, aliases));
    }

    private void RegisterBuiltInCommands()
    {
        RegisterCommand("help", "显示可用命令列表", ctx =>
        {
            string summary = string.Join(" | ", _registered.Select(c => $"{CommandPrefix}{NormalizeName(c.Name)}: {c.Description}"));
            ctx.Reply($"[Commands] {summary}");
        }, "?");

        RegisterCommand("rules", "查看当前房间规则与 DIY 配置", ctx =>
        {
            Game g = ctx.Game;
            string banlistName = g.Banlist is null ? "None" : g.Banlist.Name;
            string modeName = g.Mode switch { 1 => $"Match(Wins={g.MatchWins})", 2 => "Tag", _ => "Single" };
            string regionName = g.Region switch
            {
                0 => "OCG",
                1 => "TCG",
                2 => "OCG/TCG",
                3 => "DIY/Custom",
                4 => "NoExclusive",
                _ => "All"
            };
            string extra = g.ExtraScripts.Count > 0 ? $", ExtraScripts={string.Join(",", g.ExtraScripts)}" : string.Empty;
            ctx.Reply($"[Rules] Room={g.RoomName}, Mode={modeName}, Region={regionName}, MR={g.MasterRule}, LP={g.StartLp}, Hand={g.StartHand}, Draw={g.DrawCount}, Timer={g.Timer}s, Banlist={banlistName}, NoCheck={g.NoCheckDeck}, NoShuffle={g.NoShuffleDeck}{extra}");
        });

        RegisterCommand("room", "查看当前房间玩家与状态", ctx =>
        {
            RoomSnapshot snap = ctx.Game.CreateSnapshot();
            string players = snap.PlayerNames.Count == 0 ? "<empty>" : string.Join(", ", snap.PlayerNames);
            ctx.Reply($"[Room] {snap.RoomName} | State={snap.State} | Players({snap.DuelistCount}/{snap.MaxDuelists}): {players} | Observers={snap.ObserverCount}");
        });

        RegisterCommand("rooms", "查看当前服务端所有活动房间", ctx =>
        {
            IReadOnlyList<RoomSnapshot> rooms = ctx.Game.Server.GetRooms();
            if (rooms.Count == 0)
            {
                ctx.Reply("[Rooms] 当前没有活动房间。");
                return;
            }

            string list = string.Join(" ; ", rooms.Select(r => $"{r.RoomName}({r.State}, {r.DuelistCount}/{r.MaxDuelists})"));
            ctx.Reply($"[Rooms] 共 {rooms.Count} 个: {list}");
        });

        RegisterCommand("surrender", "对局中主动投降", ctx =>
        {
            if (ctx.Game.State != GameState.Duel)
            {
                ctx.Reply("[Surrender] 当前未处于对局阶段。");
                return;
            }

            ctx.Game.Surrender(ctx.Player, 0);
        }, "ff");
    }

    private void HandleBeforePlayerChat(object sender, PlayerChatInterceptEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Message))
            return;

        string trimmed = e.Message.Trim();
        if (trimmed.Length < 2 || trimmed[0] != CommandPrefix)
            return;

        if (e.Player is not Player player)
            return;

        string body = trimmed[1..].Trim();
        if (body.Length == 0)
            return;

        int spaceIndex = body.IndexOf(' ');
        string commandName = spaceIndex < 0 ? body : body[..spaceIndex];
        string rawArgs = spaceIndex < 0 ? string.Empty : body[(spaceIndex + 1)..].Trim();
        string[] args = rawArgs.Length == 0
            ? []
            : rawArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        e.Handled = true;

        if (_commands.TryGetValue(commandName, out ChatCommandDefinition? definition))
        {
            definition.Handler(new ChatCommandContext(Game, player, commandName, rawArgs, args));
        }
        else
        {
            Game.SendSystemMessage(player, $"[Commands] 未知命令 '{CommandPrefix}{commandName}'，输入 {CommandPrefix}help 查看帮助。");
        }
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().TrimStart('/');
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Game.OnBeforePlayerChat -= HandleBeforePlayerChat;
    }
}
