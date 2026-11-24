# YGOProSharp.Core

`YGOProSharp.Core` 保存可复用的领域能力，不承载网络协议、服务端房间状态或 native duel 生命周期。

## 职责

- 卡片领域模型：`Card`、`NamedCard`。
- 卡片仓库与分层叠加：`ICardRepository`、`INamedCardRepository`、`InMemoryCardRepository`、`CompositeCardRepository`、`CompositeNamedCardRepository`。
- SQLite 数据库加载：`SqliteCardDatabaseManager`（支持单库与多 `.cdb` 覆盖加载，用于 `expansions/` 与 `diy/` 卡库叠加）。
- native card callback adapter：`RepositoryCardDataProvider`。
- 卡组与规则：`Deck`（支持 OCG / TCG / DIY Custom `ot=4` 校验）、`DeckRules`。
- 禁限表：`Banlist`、`BanlistManager`（支持多 `lflist.conf` 合并/前置加载、`$whitelist`、按名称或 Hash 查询）。
- 分层脚本读取：`FileScriptProvider`（多目录优先级）、`ZipArchiveScriptProvider`（基于 BCL `ZipArchive` 读取 `.ypk` / `.zip`）、`InMemoryScriptProvider`、`CompositeScriptProvider`。
- OCG message 基础读取能力。

## 边界

Core 不引用 Protocol、Server、NativeApi 或 WindBot，也不引入第三方解压库。SQLite 类型只应集中在数据库 manager 实现中，不能泄漏到 Card、Deck、Game 或 native provider 之外。

## 常用命令

```bash
dotnet build YGOProSharp.Core/YGOProSharp.Core.csproj -c Release
```
