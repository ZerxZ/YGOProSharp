using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace YGOProSharp.OCGWrapper;

internal static unsafe partial class OcgCoreNative
{
    private const string LibraryName = "ocgcore";

    [LibraryImport(LibraryName, EntryPoint = "set_card_reader")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void SetCardReader([MarshalAs(UnmanagedType.FunctionPtr)] Api.CardReader callback);

    [LibraryImport(LibraryName, EntryPoint = "set_message_handler")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void SetMessageHandler([MarshalAs(UnmanagedType.FunctionPtr)] Api.MessageHandler callback);

    [LibraryImport(LibraryName, EntryPoint = "set_script_reader")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void SetScriptReader([MarshalAs(UnmanagedType.FunctionPtr)] Api.ScriptReader callback);

    [LibraryImport(LibraryName, EntryPoint = "create_duel")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr CreateDuel(uint seed);

    [LibraryImport(LibraryName, EntryPoint = "start_duel")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void StartDuel(DuelHandle duel, int options);

    [LibraryImport(LibraryName, EntryPoint = "end_duel")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void EndDuel(IntPtr duel);

    [LibraryImport(LibraryName, EntryPoint = "set_player_info")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void SetPlayerInfo(DuelHandle duel, int playerId, int lp, int startCount, int drawCount);

    [LibraryImport(LibraryName, EntryPoint = "new_card")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void NewCard(DuelHandle duel, uint code, byte owner, byte playerId, byte location, byte sequence, byte position);

    [LibraryImport(LibraryName, EntryPoint = "new_tag_card")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void NewTagCard(DuelHandle duel, uint code, byte owner, byte location);

    [LibraryImport(LibraryName, EntryPoint = "process")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Process(DuelHandle duel);

    [LibraryImport(LibraryName, EntryPoint = "get_message")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int GetMessage(DuelHandle duel, byte* buffer);

    [LibraryImport(LibraryName, EntryPoint = "get_log_message")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void GetLogMessage(DuelHandle duel, byte* buffer);

    [LibraryImport(LibraryName, EntryPoint = "set_responseb")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void SetResponseBytes(DuelHandle duel, byte* buffer);

    [LibraryImport(LibraryName, EntryPoint = "set_responsei")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void SetResponseInt(DuelHandle duel, uint value);

    [LibraryImport(LibraryName, EntryPoint = "query_card")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int QueryCard(DuelHandle duel, byte playerId, byte location, byte sequence, int queryFlag, byte* buffer, int useCache);

    [LibraryImport(LibraryName, EntryPoint = "query_field_count")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int QueryFieldCount(DuelHandle duel, byte playerId, byte location);

    [LibraryImport(LibraryName, EntryPoint = "query_field_card")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int QueryFieldCard(DuelHandle duel, byte playerId, byte location, int queryFlag, byte* buffer, int useCache);

    [LibraryImport(LibraryName, EntryPoint = "query_field_info")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int QueryFieldInfo(DuelHandle duel, byte* buffer);
}
