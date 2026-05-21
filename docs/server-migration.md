# 服务器与协议拆分说明

本轮拆分把原来混在 `YGOProSharp` 主库里的代码拆成三个项目：

- `YGOProSharp.Core`
- `YGOProSharp.Protocol`
- `YGOProSharp.Server`

这里的“前端”指 YGO 客户端协议边界，包括 CTOS/STOC、packet framing、socket client/server 和 `YGOClient`，不是浏览器或桌面 UI。

## 迁移清单

迁入 `YGOProSharp.Core`：

- `Cards/**`
- `Deck`、`DeckRules`
- `Banlist`、`BanlistManager`
- `CoreMessage`
- `FileScriptProvider`

迁入 `YGOProSharp.Protocol`：

- `Network/**`
- `CtosMessage`、`StocMessage`、`PlayerType`、`PlayerState`、`PlayerChange`、`GameState`
- `PacketFramer`、`PacketReader`
- `NetworkClient`、`NetworkServer`
- `BinaryClient`、`YGOClient`
- `BinaryExtensions`
- `ClientCard`

迁入 `YGOProSharp.Server`：

- `YGOProSharpServer`
- `CoreServer`
- `Game`
- `Player`
- `GameAnalyser`
- `GamePacketFactory`
- `Config`
- `Replay`
- `AddonBase`、`AddonsManager`、`StandardStreamProtocol`
- `IGame` 与 Player event args

## 新依赖方向

```text
Core -> Abstractions
Protocol -> Abstractions
Server -> Core + Protocol + NativeApi
Cli -> Server + NativeApi + Native
```

旧 `YGOProSharp.csproj` 已移除，不保留兼容 facade。调用方需要按职责引用 `YGOProSharp.Core`、`YGOProSharp.Protocol` 或 `YGOProSharp.Server`。
