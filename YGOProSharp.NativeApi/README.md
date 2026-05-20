# YGOProSharp.NativeApi

`YGOProSharp.NativeApi` is the managed interop layer for `ocgcore`.

It maps the exported functions in `YGOProSharp.Native/ygopro-core/ocgapi.h` to safe C# APIs. Raw native details such as `LibraryImport`, `SafeHandle`, `IntPtr`, function pointers, and native buffers stay inside this project.

## Relationship to Native

- `YGOProSharp.Native` provides runtime files only: `ocgcore.dll`, `libocgcore.so`, and `libocgcore.dylib`.
- `YGOProSharp.NativeApi` provides the managed API that calls those files.
- Application code should use `IOcgRuntime`, `IDuelFactory`, and `IDuelSession` from `YGOProSharp.Abstractions`.

## Wrapped ocgapi exports

The raw binding covers:

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

`create_duel_v2` is the default managed creation path and requires eight seed values. `create_duel` is kept only for legacy/replay compatibility. `default_script_reader` is bound internally for completeness, but it is not part of the public business API.

## Providers

`NativeOcgRuntime.Initialize` receives:

- `ICardDataProvider` for `card_data`
- `IScriptProvider` for Lua script bytes
- optional message/log callbacks through managed duel sessions

This keeps database and file-system policy outside the native interop layer.

Public callers should not depend on the raw native binding. Use `IOcgRuntime`, `IDuelFactory`, and `IDuelSession` from `YGOProSharp.Abstractions.Ocg` instead.
