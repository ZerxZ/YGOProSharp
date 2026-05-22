# YGOProSharp.NativeApi

`YGOProSharp.NativeApi` 是 `ocgcore` 的托管 interop 层。它把 `YGOProSharp.Native/ygopro-core/ocgapi.h` 中导出的函数映射为安全的 C# API。

raw native 细节，例如 `LibraryImport`、`SafeHandle`、`IntPtr`、函数指针和 native buffer，都限制在本项目内。

## 与 Native 的关系

- `YGOProSharp.Native` 只提供 runtime 文件：`ocgcore.dll`、`libocgcore.so`、`libocgcore.dylib`。
- `YGOProSharp.NativeApi` 提供调用这些文件的托管 API。
- 业务代码应使用 `YGOProSharp.Abstractions` 中的 `IOcgRuntime`、`IDuelFactory` 和 `IDuelSession`。

## 已封装的 ocgapi 导出

raw binding 覆盖：

- `set_script_reader`
- `set_card_reader`
- `set_message_handler`
- `create_duel`
- `create_duel_v2`
- `start_duel`
- `end_duel`
- `set_player_info`
- `get_log_message`
- `get_message`
- `process`
- `new_card`
- `new_tag_card`
- `query_card`
- `query_field_count`
- `query_field_card`
- `query_field_info`
- `set_responsei`
- `set_responseb`
- `preload_script`
- `default_script_reader`

`create_duel_v2` 是默认托管创建路径，需要八个 seed。`create_duel` 只保留给 legacy / replay 兼容路径。`default_script_reader` 只作为内部 raw binding，不作为业务层 public API 暴露。

## Providers

`NativeOcgRuntime.Initialize` 接收：

- `ICardDataProvider`：提供 `card_data`
- `IScriptProvider`：提供 Lua script bytes
- message / log callback：通过托管 duel session 进入上层

这让数据库策略和文件系统策略留在 Core / Server，而不是泄漏到 native interop 层。

## 使用边界

public caller 不应依赖 raw native binding。请通过 `YGOProSharp.Abstractions.Ocg` 中的接口调用 native 能力：

- `IOcgRuntime`
- `IDuelFactory`
- `IDuelSession`
- `ICardDataProvider`
- `IScriptProvider`

本项目可以依赖 `YGOProSharp.Native`，但不应依赖 `YGOProSharp.Server` 或 `YGOProSharp.Protocol`。
