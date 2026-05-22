# YGOProSharp.Protocol

`YGOProSharp.Protocol` 保存 YGO 客户端协议层能力。这里的“前端”指 CTOS/STOC 协议边界，不是 UI。

## 职责

- socket client/server：`NetworkClient`、`NetworkServer`。
- packet framing：`PacketFramer`、`PacketReader`。
- YGO 客户端包装：`BinaryClient`、`YGOClient`。
- 协议枚举：`CtosMessage`、`StocMessage`、`PlayerType`、`GameState` 等。
- 协议读写辅助：`BinaryExtensions`、`ClientCard`。

## 边界

Protocol 不引用 Core、Server、NativeApi、SQLite 或 WindBot。它只处理 bytes、packet 和协议类型，不读取卡库、不创建 duel、不管理房间状态。

## 常用命令

```bash
dotnet build YGOProSharp.Protocol/YGOProSharp.Protocol.csproj -c Release
```
