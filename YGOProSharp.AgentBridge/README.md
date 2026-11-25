# YGOProSharp.AgentBridge

`YGOProSharp.AgentBridge` 是受限视角协议决斗代理层，仅依赖 `YGOProSharp.Abstractions`、`YGOProSharp.Core` 与 `YGOProSharp.Protocol`，**不直接引用 `Server`、`NativeApi`、`Persistence` 或 `Web`**。

## 核心职责

- **标准协议会话封装**（`AgentDuelSession`）：通过 `YGOClient` / `INetworkTransport` 以普通客户端身份完成进房、提交卡组、准备、猜拳、选先后攻、主要/战斗阶段操作、连锁询问（含 `texts.str1..str16` 子效果与连锁触发上下文解码）、双向兼容选卡/祭品（`submitField`）、同调凑星、批量连招（`ExecuteBatchStepsAsync`）、快速代步（`AutoPlayStepsAsync`）及 2v2 双打换人与搭档战术协同；所有动作均等待 `ocgcore` 结算稳态后再返回快照，避免状态滞后。
- **严格战争迷雾隔离**：仅记录并暴露服务端在当前视角实际下发的公开信息与己方可见卡；对手及 2v2 非执子队友的暗手牌、里侧盖卡与洗牌顺序在观测快照中统一遮蔽为 `isHidden = true, code = 0`。
- **单 Token 会话归属与白名单**（`AgentSessionManager`）：按 `(TokenId, SessionId)` 绑定会话所有权，强制校验房间白名单（`AllowedRooms`）与并发席位上限（`MaxSessions`），杜绝不同外部 AI 之间互相读取或抢控操作。
