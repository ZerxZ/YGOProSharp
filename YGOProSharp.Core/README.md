# YGOProSharp.Core

`YGOProSharp.Core` 保存可复用的领域能力，不承载网络协议、服务端房间状态或 native duel 生命周期。

## 职责

- 卡片领域模型：`Card`、`NamedCard`。
- 卡片仓库：`ICardRepository`、`INamedCardRepository`。
- SQLite 数据库加载：`SqliteCardDatabaseManager`。
- native card callback adapter：`RepositoryCardDataProvider`。
- 卡组与规则：`Deck`、`DeckRules`。
- 禁限表：`Banlist`、`BanlistManager`。
- 脚本读取：`FileScriptProvider`。
- OCG message 基础读取能力。

## 边界

Core 不引用 Protocol、Server、NativeApi 或 WindBot。SQLite 类型只应集中在数据库 manager 实现中，不能泄漏到 Card、Deck、Game 或 native provider 之外。

## 常用命令

```bash
dotnet build YGOProSharp.Core/YGOProSharp.Core.csproj -c Release
```
