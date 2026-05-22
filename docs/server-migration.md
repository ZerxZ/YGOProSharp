# 服务端与协议拆分说明

本轮拆分把原来混在旧 `YGOProSharp` 主库里的代码拆成三个项目：

- `YGOProSharp.Core`
- `YGOProSharp.Protocol`
- `YGOProSharp.Server`

这里的“前端”指 YGO 客户端协议边界，包括 CTOS/STOC、packet framing、socket client/server 和 `YGOClient`，不是浏览器或桌面 UI。

## 迁移结果

进入 `YGOProSharp.Core`：

- `Cards/**`
- `Deck`、`DeckRules`
- `Banlist`、`BanlistManager`
- `CoreMessage`
- `FileScriptProvider`

进入 `YGOProSharp.Protocol`：

- `Network/**`
- `CtosMessage`、`StocMessage`、`PlayerType`、`PlayerState`、`PlayerChange`、`GameState`
- `PacketFramer`、`PacketReader`
- `NetworkClient`、`NetworkServer`
- `BinaryClient`、`YGOClient`
- `BinaryExtensions`
- `ClientCard`

进入 `YGOProSharp.Server`：

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
WindBot -> Core + Protocol + Abstractions
```

旧 `YGOProSharp.csproj` 已移除，不保留兼容 facade。调用方需要按职责引用 `YGOProSharp.Core`、`YGOProSharp.Protocol` 或 `YGOProSharp.Server`。

## 拆分原则

- Core 只保留可复用领域能力，不包含房间、玩家、socket、native session 生命周期。
- Protocol 只处理网络与 YGO 协议包，不读取卡库，不调用 native。
- Server 组合 Core、Protocol、NativeApi，并持有服务端状态机。
- CLI 是 host，不承载业务逻辑。
- WindBot 是外部客户端，不嵌入 Server。

## 兼容影响

这是破坏式项目结构调整。旧引用方需要把原来的 `YGOProSharp` 引用替换为更具体的新项目引用。

运行行为、协议读写顺序和 native 调用顺序不应因为拆分而改变；如果出现行为差异，应优先视为拆分回归。
